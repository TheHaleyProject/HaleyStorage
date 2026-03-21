using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Haley.Services {
    /// <summary>
    /// Partial class — path resolution pipeline.
    /// Converts a <see cref="IVaultReadRequest"/> into the fully-qualified storage path (or object key)
    /// used by a provider. Steps: prepare context → resolve workspace base path → resolve file route.
    /// </summary>
    public partial class StorageCoordinator : IStorageCoordinator {
        ConcurrentDictionary<string, string> _pathCache = new ConcurrentDictionary<string, string>();

        // ─────────────────────────────────────────────────────────────────────
        // Public entry point
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Resolves the workspace base path and the complete target path for a request.
        /// Single responsibility: orchestrate the three steps below and return both paths.
        /// </summary>
        public (string basePath, string targetPath) ProcessAndBuildStoragePath(IVaultReadRequest input, bool allowRootAccess = false) {
            PrepareRequestContext(input);                          // 1. Normalise CUID, mark virtual folder
            var provider = ResolveProvider(input);                 // 2. Resolve provider once for all steps
            var bpath = FetchWorkspaceBasePath(input, provider);   // 3. Resolve workspace base path (cached)
            if (input is IVaultFileReadRequest fileRead)
                ProcessFileRoute(fileRead, provider).Wait();       // 4. Resolve file path (may query/register indexer)
            var path = input?.BuildStoragePath(bpath, allowRootAccess, provider is FileSystemStorageProvider);
            return (bpath, path);
        }

        /// <summary>Returns the root storage directory used by the FileSystem provider.</summary>
        public string GetStorageRoot() => BasePath;

        /// <summary>
        /// Returns the sharding parameters (split length and depth) for a given identifier type.
        /// Numeric IDs use <see cref="IVaultRegistryConfig.SplitLengthNumber"/> /
        /// <see cref="IVaultRegistryConfig.DepthNumber"/>; hash/GUID IDs use the hash variants.
        /// </summary>
        /// <param name="isNumber"><c>true</c> for auto-increment IDs; <c>false</c> for compact-N GUIDs.</param>
        public (int length, int depth) SplitProvider(bool isNumber) {
            if (isNumber) return (Config.SplitLengthNumber, Config.DepthNumber);
            return (Config.SplitLengthHash, Config.DepthHash);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Step 1 — Request context preparation
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Normalises the request before path resolution begins.
        /// - Workspace CUID is always derived deterministically from names (not trusted from caller).
        /// - Folders in a managed workspace are marked virtual (DB-only, no physical directory).
        /// </summary>
        void PrepareRequestContext(IVaultReadRequest input) {
            // Workspace CUID is always re-derived deterministically from names.
            input.Scope?.Workspace.SetCuid(StorageUtils.GenerateCuid(input, Enums.VaultObjectType.WorkSpace));
            // All folders are virtual (DB-only). No physical directory marking needed.
        }

        // ─────────────────────────────────────────────────────────────────────
        // Step 2 — Workspace base path resolution
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the fully-qualified base path (or key prefix) for the workspace.
        /// Results are cached by workspace CUID after the first resolution.
        /// For the FileSystem provider the resolved path is verified to exist on disk;
        /// cloud providers use the same string as an object-key prefix — no directory check.
        /// </summary>
        string FetchWorkspaceBasePath(IVaultReadRequest request, IStorageProvider provider = null, bool ignoreCache = false) {
            Initialize().Wait(); // Ensures default structures exist. No-op in ReadOnly mode.
            provider ??= ResolveProvider(request);
            string result;

            if (!ignoreCache
                && _pathCache.TryGetValue(request.Scope.Workspace.Cuid.ToString("N"), out var cached)
                && !string.IsNullOrWhiteSpace(cached)) {
                result = cached;
            } else {
                var paths = new List<string> { BasePath };
                AddComponentPath<VaultClient>(request, paths, provider);
                AddComponentPath<StorageModule>(request, paths, provider);
                AddComponentPath<StorageWorkspace>(request, paths, provider);
                result = provider is FileSystemStorageProvider
                    ? Path.Combine(paths.ToArray())
                    : string.Join("/", paths.Select(p => p.Trim('/', '\\')));
            }

            _logger?.LogDebug($"Workspace base path: {result}");

            // Directory existence is only meaningful for the FileSystem provider.
            // Cloud providers use object keys — no directories to verify.
            if (provider is FileSystemStorageProvider && !Directory.Exists(result)) {
                throw new DirectoryNotFoundException(
                    $"Workspace base path '{result}' does not exist. " +
                    $"Ensure the client/module/workspace are registered and the storage root is accessible.");
            }

            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Step 3 — File route resolution
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Resolves <see cref="IVaultFileReadRequest.File"/>.Path for the given request.
        /// For FS reads where the logical ID is already known, uses a DB-free sharded-path reconstruction.
        /// Otherwise queries the indexer by ID, CUID, or display name, calling
        /// <see cref="PopulateFileFromDic"/> to fill the route. For uploads, also registers
        /// the document with the indexer via <c>StorageUtils.GenerateFileSystemSavePath</c>.
        /// </summary>
        public async Task ProcessFileRoute(IVaultFileReadRequest input, IStorageProvider provider = null) {
            provider ??= ResolveProvider(input);
            if (input == null) return;
            if (!string.IsNullOrWhiteSpace(input.OverrideRef)) return;
            if (input.File != null && !string.IsNullOrWhiteSpace(input.File.StorageRef)) return;

            IVaultFileWriteRequest inputW = input as IVaultFileWriteRequest;
            bool forupload = inputW != null;

            if (!Indexer.TryGetComponentInfo<StorageWorkspace>(input.Scope.Workspace.Cuid.ToString("N"), out StorageWorkspace wInfo) && forupload) {
                throw new Exception($"Unable to find workspace info. Name: {input.Scope.Workspace.Name} — Cuid: {input.Scope.Workspace.Cuid}.");
            }

            // ── FS read-only fast path ─────────────────────────────────────
            // When the file ID (Number mode) or CUID (Guid mode) is already known and the
            // provider is local FileSystem, reconstruct the sharded path directly without
            // any DB round-trip. This is the "DB-free read" the design intends for FS.
            if (!forupload && provider is FileSystemStorageProvider && wInfo != null && input.File != null) {
                string logicalId = null;
                string ext = Path.GetExtension(input.File.StorageName ?? input.RequestedName ?? string.Empty);

                if (wInfo.NameMode == StorageNameMode.Number && input.File.Id > 0)
                    logicalId = input.File.Id.ToString();
                else if (wInfo.NameMode == StorageNameMode.Guid && !string.IsNullOrWhiteSpace(input.File.Cuid)) {
                    // Storage names are always generated as compact-N (no dashes).
                    // Normalize regardless of what form the caller provided (dashed, braced, compact).
                    if (Guid.TryParse(input.File.Cuid, out var g))
                        logicalId = g.ToString("N");
                }

                if (!string.IsNullOrWhiteSpace(logicalId)) {
                    input.File.StorageRef = provider.BuildStorageRef(logicalId, ext, SplitProvider, Config.SuffixFile);
                    return; // DB-free — no indexer query needed
                }
            }

            // Attempt to resolve path without generating a new one (DB-backed).
            if ((!forupload || !string.IsNullOrWhiteSpace(input.File?.Cuid)) && input.File != null) {
                if (PopulateFromSavedPath(input, forupload, wInfo, provider)) return;
                if (await GetPathFromIndexer(input, forupload, input.Scope.Workspace.Cuid.ToString("N"))) return;
            }

            // ── Determine the target filename ──────────────────────────────
            string targetFileName = string.Empty;

            if (!string.IsNullOrWhiteSpace(input.RequestedName)) {
                targetFileName = Path.GetFileName(input.RequestedName);
            } else if (forupload) {
                if (!string.IsNullOrWhiteSpace(inputW!.OriginalName)) {
                    targetFileName = Path.GetFileName(inputW.OriginalName);
                } else if (inputW.FileStream is FileStream fs) {
                    targetFileName = Path.GetFileName(fs.Name);
                    if (string.IsNullOrWhiteSpace(inputW.OriginalName)) inputW.SetOriginalName(targetFileName);
                }
            }

            // ── Ensure extension is present ────────────────────────────────
            string targetExtension = Path.GetExtension(targetFileName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(targetExtension) && forupload) {
                if (!string.IsNullOrWhiteSpace(inputW!.OriginalName))
                    targetExtension = Path.GetExtension(inputW.OriginalName);
                else if (inputW!.FileStream is FileStream fs)
                    targetExtension = Path.GetExtension(fs.Name);

                if (!string.IsNullOrWhiteSpace(targetExtension)) targetFileName += targetExtension;
                input.SetRequestedName(targetFileName);
            }

            if (forupload && !IsFormatAllowed(targetExtension, FormatControlMode.Extension))
                throw new ArgumentException("Uploading this file format is not allowed.");

            if (string.IsNullOrWhiteSpace(input.RequestedName) && !string.IsNullOrWhiteSpace(targetFileName))
                input.SetRequestedName(targetFileName);

            // ── Generate storage path and register with indexer ────────────
            if (input.File == null || string.IsNullOrWhiteSpace(input.File.StorageRef)) {
                if (string.IsNullOrWhiteSpace(targetFileName))
                    throw new ArgumentNullException("No target file name specified for this request.");

                var holder = new VaultProfile(targetFileName, wInfo.NameMode, wInfo.ParseMode, isVirtual: false);

                // Step A: register with indexer and populate holder.Id / holder.Cuid / holder.StorageName.
                // GenerateFileSystemSavePath is reused here only for the ID-registration side-effect;
                // the path it returns is only used for FS providers.
                var logicalResult = StorageUtils.GenerateFileSystemSavePath(
                    holder,
                    uidManager: (h) => {
                        // Only register in indexer when uploading — reads never create DB entries.
                        if (Indexer == null || !forupload) return (0, Guid.Empty);
                        return Indexer.RegisterDocuments(input, h).GetAwaiter().GetResult();
                    },
                    splitProvider: SplitProvider,
                    suffix: Config.SuffixFile,
                    throwExceptions: true);

                // Step B: build the actual storage reference via the resolved provider.
                // FS: sharded path (same as before). Cloud: flat object key.
                var targetFilePath = provider is FileSystemStorageProvider
                    ? logicalResult.path
                    : provider.BuildStorageRef(
                        logicalResult.name,
                        Path.GetExtension(targetFileName),
                        SplitProvider,
                        Config.SuffixFile);

                if (input.File == null)
                    input.SetFile(new StorageFileRoute(targetFileName, targetFilePath) {
                        Id = holder.Id, Cuid = holder.Cuid.ToString("N"), Version = holder.Version, StorageName = holder.StorageName
                    });

                input.File.StorageRef = targetFilePath;
                if (string.IsNullOrWhiteSpace(input.File.DisplayName)) input.File.SetDisplayName(input.RequestedName);
                if (string.IsNullOrWhiteSpace(input.File.Cuid)) input.File.SetCuid(holder.Cuid);
                if (string.IsNullOrWhiteSpace(input.File.StorageName)) input.File.StorageName = holder.StorageName;
                if (input.File.Id < 1) input.File.SetId(holder.Id);
                if (forupload) input.File.Size = inputW!.FileStream?.Length ?? 0;

                // Stamp the effective profile_info_id so UpdateDocVersionInfo persists it to version_info.
                // Priority: workspace (most specific) → module.
                // This records exactly which profile was active when the file was stored,
                // enabling future reads to reconstruct the original provider even after a profile change.
                if (forupload && input.File is StorageFileRoute sfrWrite && sfrWrite.ProfileInfoId == 0) {
                    long activeProfileInfoId = 0;
                    if (TryGetWorkspace(input, out StorageWorkspace wsW) && wsW.ProfileInfoId > 0)
                        activeProfileInfoId = wsW.ProfileInfoId;
                    else if (Indexer != null
                        && Indexer.TryGetComponentInfo<StorageModule>(input.Scope.Module.Cuid.ToString("N"), out StorageModule mW)
                        && mW.ProfileInfoId > 0)
                        activeProfileInfoId = mW.ProfileInfoId;
                    if (activeProfileInfoId > 0) sfrWrite.ProfileInfoId = activeProfileInfoId;
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Generates the storage name and relative directory path for a hierarchy component
        /// (client, module, or workspace). Throws <see cref="InvalidOperationException"/> for
        /// <c>VaultObjectType.File</c> — use <c>StorageUtils.GenerateFileSystemSavePath</c> instead.
        /// </summary>
        public (string name, string path) GenerateBasePath(IVaultStorable input, Enums.VaultObjectType component) {
            string suffix = string.Empty;
            int length = 2, depth = 0;
            bool case_sensitive = false;

            switch (component) {
                case Enums.VaultObjectType.Client:
                suffix = Config.SuffixClient;
                length = 0; depth = 0;
                case_sensitive = _caseSensitivePairs.Any(p => input.Name.ToDBName().Equals(p.client, StringComparison.OrdinalIgnoreCase));
                break;

                case Enums.VaultObjectType.Module:
                suffix = Config.SuffixModule;
                length = 0; depth = 0;
                case_sensitive = _caseSensitivePairs.Any(p => input.Name.ToDBName().Equals(p.module, StringComparison.OrdinalIgnoreCase));
                break;

                case Enums.VaultObjectType.WorkSpace:
                var suffixAddon = (input is VaultProfile pp && pp.ParseMode == StorageNameParseMode.Generate) ? "f" : "p";
                suffix = suffixAddon + Config.SuffixWorkSpace;
                length = 1; depth = 5;
                break;

                case Enums.VaultObjectType.File:
                // Files use sharded paths generated by StorageUtils.GenerateFileSystemSavePath.
                throw new InvalidOperationException(
                    "GenerateBasePath does not support VaultObjectType.File. " +
                    "Use StorageUtils.GenerateFileSystemSavePath for file path generation.");
            }

            return StorageUtils.GenerateFileSystemSavePath(input, StorageNameParseMode.Generate,
                (n) => (length, depth), suffix: suffix, throwExceptions: false, caseSensitive: case_sensitive);
        }

        /// <summary>
        /// Extracts the relevant <see cref="IVaultStorable"/> target, its object type, the corresponding
        /// meta-file name, and the CUID string for a hierarchy type <typeparamref name="T"/>.
        /// Used by <see cref="AddComponentPath{T}"/> to dispatch between client, module, and workspace.
        /// </summary>
        (IVaultStorable target, Enums.VaultObjectType type, string metaFilePath, string cuid) GetTargetInfo<T>(IVaultReadRequest input) where T : IVaultStorable {
            IVaultStorable target = null;
            Enums.VaultObjectType targetType = Enums.VaultObjectType.Client;
            string metaFilePath = string.Empty;

            if (typeof(IVaultClient).IsAssignableFrom(typeof(T))) {
                targetType = Enums.VaultObjectType.Client; metaFilePath = CLIENTMETAFILE; target = input.Scope.Client as IVaultStorable;
            } else if (typeof(IVaultModule).IsAssignableFrom(typeof(T))) {
                targetType = Enums.VaultObjectType.Module; metaFilePath = MODULEMETAFILE; target = input.Scope.Module as IVaultStorable;
            } else if (typeof(IVaultWorkSpace).IsAssignableFrom(typeof(T))) {
                targetType = Enums.VaultObjectType.WorkSpace; metaFilePath = WORKSPACEMETAFILE; target = input.Scope.Workspace as IVaultStorable;
            }

            return (target, targetType, metaFilePath, StorageUtils.GenerateCuid(input, targetType));
        }

        /// <summary>
        /// Resolves the stored path for a hierarchy component (client/module/workspace)
        /// and appends it to <paramref name="paths"/>.
        /// For the FileSystem provider, falls back to reading the .meta file from disk when
        /// the indexer cache is cold. Cloud providers have no on-disk meta files.
        /// </summary>
        void AddComponentPath<T>(IVaultReadRequest input, List<string> paths, IStorageProvider provider) where T : IVaultStorable {
            if (Indexer == null) return;
            var info = GetTargetInfo<T>(input);
            if (info.target == null) return;

            if (Indexer.TryGetComponentInfo(info.cuid, out T obj)) {
                // StorageRef lives on IVaultWorkSpace (workspace) or VaultComponent (client/module concrete).
                // For cloud providers, StorageRef is empty for client/module — GenerateBasePath handles those.
                var storedRef = (obj as IVaultWorkSpace)?.StorageRef ?? (obj as VaultComponent)?.StorageRef;
                if (!string.IsNullOrWhiteSpace(storedRef)) paths.Add(storedRef);
            } else {
                var tuple = GenerateBasePath(info.target, info.type);
                paths.Add(tuple.path);

                // .meta file warm-up is FileSystem-specific: cloud providers have no physical meta files.
                if (provider is FileSystemStorageProvider) {
                    try {
                        var metafile = Path.Combine(BasePath, tuple.path, info.metaFilePath);
                        if (File.Exists(metafile)) {
                            var mfileInfo = File.ReadAllText(metafile).FromJson<T>();
                            if (mfileInfo != null) Indexer?.TryAddInfo(mfileInfo);
                        }
                    } catch (Exception) {
                        // .meta file is a convenience cache — failure is non-fatal.
                    }
                }
            }

            var joinedPath = provider is FileSystemStorageProvider
                ? Path.Combine(paths.ToArray())
                : string.Join("/", paths.Select(p => p.Trim('/', '\\')));
            _pathCache.TryAdd(info.cuid, string.Empty);
            _pathCache.TryUpdate(info.cuid, joinedPath, string.Empty);
        }

        /// <summary>
        /// Queries the indexer for an existing doc_version record and populates the file route.
        /// Looks up by ID or CUID first; falls back to name+directory search.
        /// Returns <c>true</c> when the file route was populated from the DB.
        /// Throws <see cref="ArgumentException"/> on reads when the file is not found.
        /// </summary>
        async Task<bool> GetPathFromIndexer(IVaultFileReadRequest input, bool forupload, string workspaceCuid) {
            if (!string.IsNullOrWhiteSpace(input.File?.Cuid) || input.File?.Id > 0) {
                var existing = input.File.Id > 0
                    ? await Indexer.GetDocVersionInfo(input.Scope.Module.Cuid.ToString("N"), input.File.Id)
                    : await Indexer.GetDocVersionInfo(input.Scope.Module.Cuid.ToString("N"), input.File.Cuid);

                if (existing?.Status == true && existing.Result is Dictionary<string, object> dic && dic.Count > 0)
                    return PopulateFileFromDic(input, dic);

            } else if (!string.IsNullOrWhiteSpace(input.File?.DisplayName) || !string.IsNullOrWhiteSpace(input.RequestedName)) {
                var searchName = input.File?.DisplayName ?? input.RequestedName;
                var dirName = input.Scope.Folder?.DisplayName ?? VaultConstants.DEFAULT_NAME;
                var dirParent = input.Scope.Folder?.Parent?.Id ?? 0;
                var existing = await Indexer.GetDocVersionInfo(input.Scope.Module.Cuid.ToString("N"), workspaceCuid, searchName, dirName, dirParent);

                if (existing?.Status == true && existing.Result is Dictionary<string, object> dic && dic.Count > 0) {
                    if (input.File == null)
                        input.SetFile(new StorageFileRoute(input.RequestedName, string.Empty) { Cuid = dic["uid"]?.ToString() });
                    if (string.IsNullOrWhiteSpace(input.File.Cuid)) input.File.SetCuid(dic["uid"]?.ToString());
                    if (string.IsNullOrWhiteSpace(input.File.DisplayName)) input.File.SetDisplayName(searchName);
                    return PopulateFileFromDic(input, dic);
                } else {
                    if (!forupload) throw new ArgumentException("File not found in the indexer.");
                }
            }
            return false;
        }

        /// <summary>
        /// Populates path, size, save-as name, staging path, and lifecycle flags on the file route
        /// from a <c>version_info</c> dictionary row returned by the indexer.
        /// Returns <c>true</c> if the file was located (storage or staging path was found).
        /// A file that is still in staging (flags bit 4 set, bit 8 not yet set) will have
        /// <see cref="IVaultFileRoute.StorageRef"/> set to the staging key so the coordinator
        /// can decide whether to stream from staging or redirect to a pre-signed URL.
        /// </summary>
        bool PopulateFileFromDic(IVaultFileReadRequest input, Dictionary<string, object> dic) {
            var storagePath = dic.TryGetValue("path", out var p) ? p?.ToString() : null;
            var stagingPath = dic.TryGetValue("staging_path", out var sp) ? sp?.ToString() : null;

            // A row exists but neither path is populated — treat as not found.
            if (string.IsNullOrWhiteSpace(storagePath) && string.IsNullOrWhiteSpace(stagingPath))
                return false;

            // Prefer the promoted storage path; fall back to staging path if file is still in staging.
            input.File.StorageRef = !string.IsNullOrWhiteSpace(storagePath) ? storagePath : stagingPath;

            if (long.TryParse(dic["size"]?.ToString(), out var size)) input.File.Size = size;
            // saveas_name = vi.storage_name alias; dname = di.display_name (human readable)
            input.File.StorageName = dic.TryGetValue("saveas_name", out var sn) ? sn?.ToString() ?? string.Empty : string.Empty;

            // Flags, staging path, and stored profile_info_id are carried on StorageFileRoute (concrete type).
            if (input.File is StorageFileRoute sfr) {
                if (int.TryParse(dic["flags"]?.ToString(), out var flags)) sfr.Flags = flags;
                sfr.StagingRef = stagingPath ?? string.Empty;
                // Restore the profile_info_id that was active when this version was written.
                // ResolveProvider will use it to reconstruct the exact original provider chain.
                if (dic.TryGetValue("profile_info_id", out var pidObj)
                    && long.TryParse(pidObj?.ToString(), out var pid) && pid > 0)
                    sfr.ProfileInfoId = pid;
            }

            return true;
        }

        /// <summary>
        /// Fast-path for reads where <see cref="IVaultFileRoute.StorageName"/> is already known.
        /// Validates the name matches the workspace's <see cref="StorageNameMode"/> then calls
        /// <see cref="IStorageProvider.BuildStorageRef"/> to reconstruct the sharded/flat key without a DB round-trip.
        /// Returns <c>false</c> for uploads or when StorageName is empty.
        /// </summary>
        bool PopulateFromSavedPath(IVaultFileReadRequest input, bool forupload, StorageWorkspace wInfo, IStorageProvider provider) {
            if (forupload || string.IsNullOrWhiteSpace(input?.File?.StorageName)) return false;
            var sname = Path.GetFileNameWithoutExtension(input.File.StorageName);
            var extension = Path.GetExtension(input.File.StorageName);

            if (wInfo.NameMode == StorageNameMode.Number && !sname.IsNumber())
                throw new ArgumentException("StorageName must be numeric for this workspace.");

            if (wInfo.NameMode == StorageNameMode.Guid) {
                if (sname.IsCompactGuid(out Guid g) || sname.IsValidGuid(out g))
                    sname = g.ToString("N");
                else
                    throw new ArgumentException("StorageName must be a valid GUID for this workspace.");
            }

            // Use provider's key format: FS applies sharding, cloud returns a flat key.
            input.File.StorageRef = provider.BuildStorageRef(sname, extension, SplitProvider, Config.SuffixFile);
            return true;
        }
    }
}
