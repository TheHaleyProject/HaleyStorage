using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace Haley.Services {

    /// <summary>
    /// Partial class — CRUD operations (Upload, Download, Delete, Exists, GetSize, directory stubs).
    /// Handles staging vs direct-save routing and delegates byte I/O to the resolved <see cref="IStorageProvider"/>.
    /// </summary>
    public partial class StorageCoordinator : IStorageCoordinator {

        // ─── Upload ───────────────────────────────────────────────────────────

        /// <summary>
        /// Uploads a file from <see cref="IVaultFileWriteRequest.FileStream"/> to the resolved storage provider.
        /// When the module's <see cref="VaultProfileMode"/> is not <c>DirectSave</c> and a staging provider
        /// is configured, the file is written to staging first with <c>flags=4</c> (InStaging);
        /// direct-save sets <c>flags=8|64</c> (InStorage|Completed) immediately.
        /// </summary>
        /// <param name="input">Write request including scope (client/module/workspace), file stream, and conflict resolve mode.</param>
        /// <returns>An <see cref="IVaultResponse"/> indicating success, size, and DB indexer update status.</returns>
        public async Task<IVaultResponse> Upload(IVaultFileWriteRequest input) {
            var result = new VaultResponse() { Status = false, OriginalName = input?.OriginalName };
            try {
                if (!WriteMode) { result.Message = "Application is in Read-Only mode."; return result; }
                if (input == null) { result.Message = "Input cannot be empty or null."; return result; }
                if (input.ReadOnlyMode) { result.Message = "Request is in Read-Only mode."; return result; }

                input.GenerateCallId();
                var gPaths = ProcessAndBuildStoragePath(input, true);

                if (string.IsNullOrWhiteSpace(input.OverrideRef)) {
                    result.Message = "Unable to generate the final storage path. Please check inputs.";
                    return result;
                }
                if (input.OverrideRef == gPaths.basePath)
                    throw new ArgumentException("No file save name is processed.");
                if (input.FileStream == null)
                    throw new ArgumentException("File stream is null. Nothing to save.");

                // ── Staging vs direct-save decision ───────────────────────────────
                // Resolution: workspace override → module → DirectSave default.
                var profileMode = ResolveProfileMode(input);
                var stagingProvider = ResolveStagingProvider(input);
                bool useStaging = profileMode != VaultProfileMode.DirectSave && stagingProvider != null
                                  && input.File != null && !string.IsNullOrWhiteSpace(input.File.StorageName);

                IStorageProvider writeProvider;
                string writePath;

                if (useStaging) {
                    // Build a key for the staging provider using the logical ID extracted from StorageName.
                    var logicalId = Path.GetFileNameWithoutExtension(input.File.StorageName);
                    var ext = Path.GetExtension(input.File.StorageName);
                    var stagingKey = stagingProvider.BuildStorageRef(logicalId, ext, SplitProvider, Config.SuffixFile);
                    writeProvider = stagingProvider;
                    writePath = stagingKey;

                    // Annotate the file route so the DB update records staging_path and flags.
                    if (input.File is StorageFileRoute sfr) {
                        sfr.StagingRef = stagingKey;
                        sfr.Flags = 4; // InStaging — promote will set InStorage|Completed later
                        // Clear the primary storage ref: storage_path stays null in DB until promoted.
                        sfr.StorageRef = string.Empty;
                    }
                } else {
                    // DirectSave — write to primary provider and mark as complete immediately.
                    writeProvider = ResolveProvider(input);
                    writePath = input.OverrideRef;
                    if (input.File is StorageFileRoute sfr)
                        sfr.Flags = 8 | 64; // InStorage | Completed
                }

                // Security: for FS, ensure target path stays within the storage root.
                if (writeProvider is FileSystemStorageProvider && !writePath.StartsWith(BasePath)) {
                    result.Message = "Not authorized for this path. Please check the inputs.";
                    return result;
                }

                if (input.BufferSize < (1024 * 80)) input.BufferSize = (1024 * 80);

                var writeResult = await writeProvider.WriteAsync(writePath, input.FileStream, input.BufferSize, input.WriteConflictMode);

                result.Status = writeResult.Success;
                result.PhysicalObjectExists = writeResult.AlreadyExisted;
                result.Message = writeResult.Message;

                if (input.File != null) result.SetResult(input.File);
                if (input.File is StorageFileRoute sfrUp) {
                    result.VersionCuid = sfrUp.Cuid;
                    result.RootCuid = sfrUp.RootCuid;
                }
                if (result.Status && !writeResult.AlreadyExisted) {
                    result.Size = input.FileStream.Length;
                    result.SizeHR = result.Size.ToFileSize(false);
                }
            } catch (Exception ex) {
                result.Message = ex.Message + Environment.NewLine + ex.StackTrace;
                result.Status = false;
            } finally {
                IFeedback upInfo = null;
                if (WriteMode && Indexer != null && input != null && input.Scope?.Module != null) {
                    if (input.File != null && result.Status) {
                        upInfo = await Indexer.UpdateDocVersionInfo(input.Scope.Module.Cuid.ToString("N"), input.File, input.CallID);
                    }
                    if (Indexer is MariaDBIndexing mdIdx) {
                        mdIdx.FinalizeTransaction(input.CallID, result.Status && !(upInfo == null || upInfo.Status == false));
                    }
                    _logger?.LogDebug($"Document version update: {upInfo?.Status} — {upInfo?.Result}");
                }
            }
            return result;
        }

        // ─── Download ─────────────────────────────────────────────────────────

        /// <summary>
        /// Downloads a file for the given read request.
        /// If the file is still in staging (flags bit 4 set, bit 8 not set), the staging provider is
        /// queried first. Cloud staging providers may return a pre-signed <see cref="IVaultStreamResponse.AccessUrl"/>;
        /// callers should redirect the HTTP response rather than stream bytes when that URL is non-null.
        /// </summary>
        /// <param name="input">Read request that identifies the client/module/workspace and the specific file.</param>
        /// <param name="auto_search_extension">When <c>true</c>, scans the target directory for a matching file if no extension is specified.</param>
        public async Task<IVaultStreamResponse> Download(IVaultFileReadRequest input, bool auto_search_extension = true) {
            var result = new VaultStreamResponse() { Status = false, Stream = Stream.Null };

            var path = ProcessAndBuildStoragePath(input, true).targetPath;
            if (string.IsNullOrWhiteSpace(path)) return result;

            var comparison = StringComparison.OrdinalIgnoreCase;

            // ── Staging-aware read ─────────────────────────────────────────────
            // If the file is still in staging (InStaging bit set, InStorage bit clear),
            // read from the staging provider. Cloud staging providers return a pre-signed
            // redirect URL via GetAccessUrl; FS staging falls back to streaming.
            if (input.File is StorageFileRoute sfr && (sfr.Flags & 4) != 0 && (sfr.Flags & 8) == 0 && !string.IsNullOrWhiteSpace(sfr.StagingRef)) {

                var stagingProvider = ResolveStagingProvider(input);
                if (stagingProvider != null) {
                    // Try a pre-signed URL redirect first (zero-bytes-through-server for cloud).
                    var accessUrl = await stagingProvider.GetAccessUrl(sfr.StagingRef, TimeSpan.FromHours(1));
                    if (!string.IsNullOrWhiteSpace(accessUrl)) {
                        result.Status = true;
                        result.AccessUrl = accessUrl;
                        result.Stream = Stream.Null;
                        result.SaveName = input.File.StorageName;
                        return result;
                    }

                    // No redirect — stream bytes from staging.
                    var stagingRead = await stagingProvider.ReadAsync(sfr.StagingRef, auto_search_extension, comparison);
                    if (!stagingRead.Success) { result.Message = stagingRead.Message; return result; }
                    result.SaveName = !string.IsNullOrWhiteSpace(input.File?.DisplayName)
                        ? input.File.DisplayName
                        : input.File?.StorageName;
                    result.Status = true;
                    result.Extension = stagingRead.Extension;
                    result.Stream = stagingRead.Stream;
                    return result;
                }
            }

            // ── Normal read from primary provider ─────────────────────────────
            var readResult = await ResolveProvider(input).ReadAsync(path, auto_search_extension, comparison);

            if (!readResult.Success) { result.Message = readResult.Message; return result; }

            // Prefer the human-readable display name (from doc_info) when available.
            // Fall back to the internal storage name so the download always has a filename.
            result.SaveName = !string.IsNullOrWhiteSpace(input.File?.DisplayName)
                ? input.File.DisplayName
                : input.File?.StorageName;
            result.Status = true;
            result.Extension = readResult.Extension;
            result.Stream = readResult.Stream;
            return result;
        }

        /// <summary>
        /// Downloads a file directly from an <see cref="IVaultFileRoute"/> without a full scope context.
        /// Uses the default provider and combines <see cref="StorageCoordinator.BasePath"/> with
        /// <see cref="IVaultRoute.StorageRef"/> when the ref is not already rooted.
        /// </summary>
        public async Task<IVaultStreamResponse> Download(IVaultFileRoute input, bool auto_search_extension = true) {
            var result = new VaultStreamResponse() { Status = false, Stream = Stream.Null };

            if (input == null || string.IsNullOrWhiteSpace(input.StorageRef)) {
                result.Message = "File route path is empty.";
                return result;
            }

            // input.StorageRef is the relative storage reference. Combine with BasePath for FS provider.
            // For cloud providers the coordinator's default is used since there is no request scope.
            var path = Path.IsPathRooted(input.StorageRef) ? input.StorageRef : Path.Combine(BasePath, input.StorageRef);

            var readResult = await GetDefaultProvider().ReadAsync(path, auto_search_extension);

            if (!readResult.Success) { result.Message = readResult.Message; return result; }

            result.SaveName = input.StorageName;
            result.Status = true;
            result.Extension = readResult.Extension;
            result.Stream = readResult.Stream;
            return result;
        }

        // ─── Delete ───────────────────────────────────────────────────────────

        /// <summary>
        /// Deletes a file from its resolved storage provider path.
        /// Returns an error feedback when <see cref="WriteMode"/> is <c>false</c> or the file does not exist.
        /// </summary>
        public async Task<IFeedback> Delete(IVaultFileReadRequest input) {
            var feedback = new Feedback() { Status = false };
            if (!WriteMode) { feedback.Message = "Application is in Read-Only mode."; return feedback; }
            if (input.ReadOnlyMode) { feedback.Message = "Request is in Read-Only mode."; return feedback; }

            var path = ProcessAndBuildStoragePath(input, true).targetPath;
            if (string.IsNullOrWhiteSpace(path)) {
                feedback.Message = "Unable to generate path from provided inputs.";
                return feedback;
            }

            var provider = ResolveProvider(input);
            if (!provider.Exists(path)) {
                feedback.Message = $"File does not exist: {path}.";
                return feedback;
            }

            feedback.Status = await provider.DeleteAsync(path);
            feedback.Message = feedback.Status? "File deleted." : "Unable to delete the file. Check if it is in use by another process and try again.";
            return feedback;
        }

        // ─── Exists / GetSize ─────────────────────────────────────────────────

        /// <summary>
        /// Checks whether a file or directory exists at the resolved path.
        /// </summary>
        /// <param name="isFilePath">
        /// <c>true</c> to check for a file via the resolved <see cref="IStorageProvider"/>;
        /// <c>false</c> to check for a physical directory (FS only).
        /// </param>
        public IFeedback Exists(IVaultReadRequest input, bool isFilePath = false) {
            var feedback = new Feedback() { Status = false };
            var path = ProcessAndBuildStoragePath(input, isFilePath).targetPath;
            if (string.IsNullOrWhiteSpace(path)) {
                feedback.Message = "Unable to generate path from provided inputs.";
                return feedback;
            }

            // For files: ask the provider.
            // For directories: virtual dirs are in DB — physical check only applies to FS provider.
            // Cloud providers cannot determine virtual folder existence without an indexer query (not yet implemented).
            feedback.Status = isFilePath? ResolveProvider(input).Exists(path) : (ResolveProvider(input) is FileSystemStorageProvider && Directory.Exists(path));
            if (!feedback.Status) feedback.Message = $"Does not exist: {path}";
            return feedback;
        }

        /// <summary>Returns the byte size of the file at the resolved path, or 0 if it does not exist.</summary>
        public long GetSize(IVaultReadRequest input) {
            var path = ProcessAndBuildStoragePath(input, true).targetPath;
            if (string.IsNullOrWhiteSpace(path)) return 0;
            return ResolveProvider(input).GetSize(path);
        }

        // ─── GetParent ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the display name of the parent directory for the given file.
        /// Requires a non-empty workspace CUID and file CUID; delegates to <see cref="IVaultIndexing"/>.
        /// </summary>
        public async Task<IFeedback<string>> GetParent(IVaultFileReadRequest input) {
            input.Scope.Workspace.SetCuid(StorageUtils.GenerateCuid(input, Enums.VaultObjectType.WorkSpace));
            return await Indexer?.GetParentName(input);
        }

        // ─── Directory operations (virtual — pending MariaDB phase) ───────────

        /// <summary>
        /// Returns directory metadata. Currently a stub — returns a failure response until
        /// the MariaDB directory-query phase is implemented.
        /// </summary>
        public Task<IVaultDirResponse> GetDirectoryInfo(IVaultReadRequest input) {
            // TODO: virtual directories live in MariaDB — query Indexer.GetDirectoryInfo once available.
            return Task.FromResult<IVaultDirResponse>(new VaultDirResponse() { Status = false, Message = "GetDirectoryInfo requires indexer implementation (pending MariaDB phase)." });
        }

        /// <summary>
        /// Creates a virtual directory (folder) in MariaDB under the workspace identified by <paramref name="input"/>.
        /// The parent folder is taken from <c>input.Scope.Folder</c> (Id=0 means root).
        /// Folders are DB-only — no physical directory is created on disk.
        /// Returns an error when no indexer is configured or the coordinator is in read-only mode.
        /// </summary>
        public async Task<IVaultResponse> CreateDirectory(IVaultReadRequest input, string rawname) {
            var result = new VaultResponse { Status = false, OriginalName = rawname };
            if (Indexer == null) { result.Message = "No indexer is configured. Directory creation requires a DB indexer."; return result; }
            if (!WriteMode) { result.Message = "Application is in Read-Only mode."; return result; }
            if (string.IsNullOrWhiteSpace(rawname)) { result.Message = "Folder name cannot be empty."; return result; }

            var fb = await Indexer.RegisterDirectory(input, rawname);
            result.Status = fb.Status;
            result.Message = fb.Status ? $"Folder '{rawname}' created (id={fb.Result.id})." : fb.Message;
            if (fb.Status) result.SetResult(fb.Result.cuid);
            return result;
        }

        /// <summary>
        /// Soft-deletes a virtual directory in MariaDB. Currently a stub — not yet implemented.
        /// </summary>
        public Task<IFeedback> DeleteDirectory(IVaultReadRequest input, bool recursive) {
            // TODO: soft-delete virtual directory in MariaDB via Indexer.DeleteDirectory.
            return Task.FromResult<IFeedback>(new Feedback() { Status = false, Message = "DeleteDirectory requires indexer implementation (pending MariaDB phase)." });
        }

        // ─── Revision backup helper ───────────────────────────────────────────

    }
}
