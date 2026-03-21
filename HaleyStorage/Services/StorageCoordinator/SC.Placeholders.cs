using Haley.Abstractions;
using Haley.Models;
using Haley.Utils;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Haley.Services {

    /// <summary>
    /// Partial class — placeholder / background-move pipeline.
    /// <para>
    /// Allows a caller to reserve a DB record and receive the target storage location
    /// <em>before</em> the file bytes arrive. An external process (USB copy, server-side move,
    /// cloud-native copy, etc.) can then place the file at the returned path out-of-band,
    /// after which <see cref="FinalizePlaceholder"/> marks the version as complete.
    /// </para>
    /// </summary>
    public partial class StorageCoordinator : IStorageCoordinator {

        // ── 1. Create ─────────────────────────────────────────────────────────

        /// <summary>
        /// Reserves a DB record (document + doc_version + version_info with flags=256 Placeholder)
        /// and returns the pre-computed target storage location so an external process can copy
        /// the file out-of-band.
        /// <para>
        /// For FileSystem providers the parent shard directory is created immediately,
        /// so the caller can start the copy without any extra setup.
        /// </para>
        /// </summary>
        /// <param name="request">Scope (client/module/workspace). File route is ignored.</param>
        /// <param name="fileName">File name including extension (e.g. <c>"archive.mp4"</c>).</param>
        /// <param name="displayName">Optional human-readable display name stored in doc_info.</param>
        public async Task<IFeedback<PlaceholderInfo>> CreatePlaceholder(IVaultReadRequest request, string fileName, string displayName = null) {

            var fb = new Feedback<PlaceholderInfo>();
            try {
                if (!WriteMode) return fb.SetMessage("Application is in Read-Only mode.");
                if (request == null) return fb.SetMessage("Request cannot be null.");
                if (request.ReadOnlyMode) return fb.SetMessage("Request is in Read-Only mode.");
                if (string.IsNullOrWhiteSpace(fileName)) return fb.SetMessage("fileName is required.");
                if (Indexer == null) return fb.SetMessage("An indexer is required to create a placeholder.");

                // Build a write request so ProcessAndBuildStoragePath enters forupload mode,
                // which triggers RegisterDocuments and generates the storage name/path.
                // FileStream is intentionally null — the file does not exist yet.
                var writeReq = new StorageWriteRequest(request.Scope?.Client?.Name, request.Scope?.Module?.Name, request.Scope?.Workspace?.Name) { OriginalName = fileName, FileStream = null };

                writeReq.GenerateCallId();
                ProcessAndBuildStoragePath(writeReq, true);

                if (writeReq.File == null || writeReq.File.Id < 1 || string.IsNullOrWhiteSpace(writeReq.File.Cuid))
                    return fb.SetMessage("Failed to register document record. Check indexer configuration.");

                long versionId   = writeReq.File.Id;
                string versionCuid = writeReq.File.Cuid;
                string storageRef  = writeReq.OverrideRef;   // full path (FS) or object key (cloud)
                string storageName = writeReq.File.StorageName;
                var resolvedProvider = ResolveProvider(writeReq);

                // ── Staging ref ───────────────────────────────────────────────
                string stagingRef = null;
                var stagingProvider = ResolveStagingProvider(writeReq);
                if (stagingProvider != null && !string.IsNullOrWhiteSpace(storageName)) {
                    var logicalId = Path.GetFileNameWithoutExtension(storageName);
                    var ext       = Path.GetExtension(storageName);
                    stagingRef = stagingProvider.BuildStorageRef(logicalId, ext, SplitProvider, Config.SuffixFile);
                }

                // ── FS: pre-create the shard directory ────────────────────────
                if (resolvedProvider is FileSystemStorageProvider && !string.IsNullOrWhiteSpace(storageRef)) {
                    var dir = Path.GetDirectoryName(storageRef);
                    if (!string.IsNullOrWhiteSpace(dir))
                        Directory.CreateDirectory(dir);
                }

                // ── Write placeholder version_info to DB ──────────────────────
                // flags = 256 (Placeholder): DB record reserved, no file content yet.
                var sfr = new StorageFileRoute(fileName, storageRef) { Id          = versionId, Cuid        = versionCuid, StorageName = storageName, Size        = 0, Flags       = 256, StagingRef  = stagingRef ?? string.Empty };
                if (!string.IsNullOrWhiteSpace(displayName))
                    sfr.SetDisplayName(displayName);

                var moduleCuid = writeReq.Scope?.Module?.Cuid.ToString("N");
                var updateResult = await Indexer.UpdateDocVersionInfo(moduleCuid, sfr, writeReq.CallID);

                bool ok = updateResult.Status;
                if (Indexer is MariaDBIndexing idx) idx.FinalizeTransaction(writeReq.CallID, ok);

                if (!ok)
                    return fb.SetMessage($"DB placeholder record failed: {updateResult.Message}");

                // Persist a custom display name to doc_info if provided.
                // The auto-registered entry uses the raw fileName; this overwrites it.
                if (!string.IsNullOrWhiteSpace(displayName))
                    await Indexer.UpdateDocDisplayName(moduleCuid, versionId, displayName);

                return fb.SetStatus(true).SetResult(new PlaceholderInfo { VersionId   = versionId, VersionCuid = versionCuid, StorageName = storageName, StorageRef  = storageRef, StagingRef  = stagingRef });

            } catch (Exception ex) {
                return fb.SetMessage(ex.Message);
            }
        }

        // ── 2. Finalize ───────────────────────────────────────────────────────

        /// <summary>
        /// Marks a placeholder version as complete after the out-of-band copy lands.
        /// <list type="bullet">
        ///   <item>toStaging=false → sets flags = InStorage|Completed (8|64); updates storage_ref.</item>
        ///   <item>toStaging=true  → sets flags = InStaging (4); updates staging_ref.</item>
        /// </list>
        /// When <paramref name="size"/> is <c>null</c> and the provider is FileSystem and
        /// <paramref name="toStaging"/> is false, the file size is read directly from disk.
        /// </summary>
        /// <param name="request">Scope identifying the module DB (client/module/workspace).</param>
        /// <param name="versionId">The VersionId returned by <see cref="CreatePlaceholder"/>.</param>
        /// <param name="toStaging">True if the file was copied to staging rather than primary storage.</param>
        /// <param name="size">File size in bytes, or null to auto-detect (FS only).</param>
        /// <param name="hash">Optional SHA-256 hash of the copied file.</param>
        public async Task<IFeedback> FinalizePlaceholder(IVaultReadRequest request, long versionId, bool toStaging = false, long? size = null, string hash = null) {

            var fb = new Feedback();
            try {
                if (!WriteMode) return fb.SetMessage("Application is in Read-Only mode.");
                if (request == null) return fb.SetMessage("Request cannot be null.");
                if (request.ReadOnlyMode) return fb.SetMessage("Request is in Read-Only mode.");
                if (versionId < 1) return fb.SetMessage("A valid versionId is required.");
                if (Indexer == null) return fb.SetMessage("An indexer is required to finalize a placeholder.");

                PrepareRequestContext(request);
                var moduleCuid = StorageUtils.GenerateCuid(request, Haley.Enums.VaultObjectType.Module);

                // Fetch the stored paths and cuid from the DB.
                var existing = await Indexer.GetDocVersionInfo(moduleCuid, versionId);
                if (existing?.Status != true || existing.Result is not Dictionary<string, object> dic || dic.Count < 1)
                    return fb.SetMessage($"Version {versionId} not found in module {moduleCuid}.");

                var storedPath    = dic.TryGetValue("path",         out var p)  ? p?.ToString()  : null;
                var storedStaging = dic.TryGetValue("staging_path", out var sp) ? sp?.ToString() : null;
                var versionCuid   = dic.TryGetValue("uid",          out var vc) ? vc?.ToString() : null;
                var storageName   = dic.TryGetValue("saveas_name",  out var sn) ? sn?.ToString() : null;

                if (string.IsNullOrWhiteSpace(versionCuid))
                    return fb.SetMessage("Could not retrieve version CUID from the stored record.");

                // ── Auto-detect size for FS primary storage ────────────────────
                var resolvedProvider = ResolveProvider(request);
                long resolvedSize = size ?? 0;
                if (!size.HasValue && !toStaging && resolvedProvider is FileSystemStorageProvider) {
                    var fullPath = string.IsNullOrWhiteSpace(storedPath)? null : Path.IsPathRooted(storedPath)? storedPath : Path.Combine(FetchWorkspaceBasePath(request), storedPath);

                    if (!string.IsNullOrWhiteSpace(fullPath) && File.Exists(fullPath))
                        resolvedSize = new FileInfo(fullPath).Length;
                }

                // ── Build finalize route ───────────────────────────────────────
                // flags: InStaging=4, InStorage|Completed=8|64
                int finalFlags = toStaging ? 4 : (8 | 64);

                var sfr = new StorageFileRoute(storageName ?? string.Empty, toStaging ? string.Empty : (storedPath ?? string.Empty)) { Id          = versionId, Cuid        = versionCuid, StorageName = storageName ?? string.Empty, Size        = resolvedSize, Flags       = finalFlags, StagingRef  = toStaging ? (storedStaging ?? string.Empty) : (storedStaging ?? string.Empty), Hash        = hash };

                // When finalizing to staging we clear the primary storage ref.
                if (toStaging) sfr.StorageRef = string.Empty;

                var callId = Guid.NewGuid().ToString("N");
                var updateResult = await Indexer.UpdateDocVersionInfo(moduleCuid, sfr, callId);

                bool ok = updateResult.Status;
                if (Indexer is MariaDBIndexing idx) idx.FinalizeTransaction(callId, ok);

                if (!ok)
                    return fb.SetMessage($"Finalize failed: {updateResult.Message}");

                return fb.SetStatus(true).SetMessage(toStaging ? $"Version {versionId} marked as InStaging." : $"Version {versionId} marked as InStorage|Completed. Size: {resolvedSize} bytes.");

            } catch (Exception ex) {
                return fb.SetMessage(ex.Message);
            }
        }
    }
}
