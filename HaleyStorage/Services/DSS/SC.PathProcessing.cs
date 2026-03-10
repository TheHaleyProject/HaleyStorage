using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Haley.Services {
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
            PrepareRequestContext(input);                // 1. Normalise CUID, mark virtual folder
            var bpath = FetchWorkspaceBasePath(input);  // 2. Resolve workspace base path (cached)
            if (input is IVaultFileReadRequest fileRead)
                ProcessFileRoute(fileRead).Wait();       // 3. Resolve file path (may query/register indexer)
            var path = input?.BuildStoragePath(bpath, allowRootAccess); // 4. Build final target path
            return (bpath, path);
        }

        public string GetStorageRoot() => BasePath;

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
        /// Returns the fully-qualified base path for the workspace (BasePath/client/module/workspace).
        /// Results are cached by workspace CUID after the first resolution.
        /// For the FileSystem provider the resolved path is verified to exist on disk.
        /// </summary>
        string FetchWorkspaceBasePath(IVaultReadRequest request, bool ignoreCache = false) {
            Initialize().Wait(); // Ensures default structures exist. No-op in ReadOnly mode.
            string result;

            if (!ignoreCache
                && _pathCache.TryGetValue(request.Scope.Workspace.Cuid.ToString("N"), out var cached)
                && !string.IsNullOrWhiteSpace(cached)) {
                result = cached;
            } else {
                var paths = new List<string> { BasePath };
                AddComponentPath<VaultClient>(request, paths);
                AddComponentPath<StorageModule>(request, paths);
                AddComponentPath<StorageWorkspace>(request, paths);
                result = Path.Combine(paths.ToArray());
            }

            _logger?.LogDebug($"Workspace base path: {result}");

            // Directory existence is only meaningful for the FileSystem provider.
            // Cloud providers use object keys — no directories to verify.
            if (GetDefaultProvider() is FileSystemStorageProvider && !Directory.Exists(result)) {
                throw new DirectoryNotFoundException(
                    $"Workspace base path '{result}' does not exist. " +
                    $"Ensure the client/module/workspace are registered and the storage root is accessible.");
            }

            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Step 3 — File route resolution
        // ─────────────────────────────────────────────────────────────────────

        public async Task ProcessFileRoute(IVaultFileReadRequest input) {
            if (input == null) return;
            if (!string.IsNullOrWhiteSpace(input.TargetPath)) return;
            if (input.File != null && !string.IsNullOrWhiteSpace(input.File.Path)) return;

            IVaultFileWriteRequest inputW = input as IVaultFileWriteRequest;
            bool forupload = inputW != null;

            if (!Indexer.TryGetComponentInfo<StorageWorkspace>(input.Scope.Workspace.Cuid.ToString("N"), out StorageWorkspace wInfo) && forupload) {
                throw new Exception($"Unable to find workspace info. Name: {input.Scope.Workspace.Name} — Cuid: {input.Scope.Workspace.Cuid}.");
            }

            // Attempt to resolve path without generating a new one.
            if ((!forupload || !string.IsNullOrWhiteSpace(input.File?.Cuid)) && input.File != null) {
                if (PopulateFromSavedPath(input, forupload, wInfo)) return;
                if (await GetPathFromIndexer(input, forupload, input.Scope.Workspace.Cuid.ToString("N"))) return;
            }

            // ── Determine the target filename ──────────────────────────────
            string targetFileName = string.Empty;

            if (!string.IsNullOrWhiteSpace(input.TargetName)) {
                targetFileName = Path.GetFileName(input.TargetName);
            } else if (forupload) {
                if (!string.IsNullOrWhiteSpace(inputW!.FileOriginalName)) {
                    targetFileName = Path.GetFileName(inputW.FileOriginalName);
                } else if (inputW.FileStream is FileStream fs) {
                    targetFileName = Path.GetFileName(fs.Name);
                    if (string.IsNullOrWhiteSpace(inputW.FileOriginalName)) inputW.SetFileOriginalName(targetFileName);
                }
            }

            // ── Ensure extension is present ────────────────────────────────
            string targetExtension = Path.GetExtension(targetFileName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(targetExtension) && forupload) {
                if (!string.IsNullOrWhiteSpace(inputW!.FileOriginalName))
                    targetExtension = Path.GetExtension(inputW.FileOriginalName);
                else if (inputW!.FileStream is FileStream fs)
                    targetExtension = Path.GetExtension(fs.Name);

                if (!string.IsNullOrWhiteSpace(targetExtension)) targetFileName += targetExtension;
                input.SetTargetName(targetFileName);
            }

            if (forupload && !IsFormatAllowed(targetExtension, FormatControlMode.Extension))
                throw new ArgumentException("Uploading this file format is not allowed.");

            if (string.IsNullOrWhiteSpace(input.TargetName) && !string.IsNullOrWhiteSpace(targetFileName))
                input.SetTargetName(targetFileName);

            // ── Generate storage path and register with indexer ────────────
            if (input.File == null || string.IsNullOrWhiteSpace(input.File.Path)) {
                if (string.IsNullOrWhiteSpace(targetFileName))
                    throw new ArgumentNullException("No target file name specified for this request.");

                var holder = new VaultProfile(targetFileName, wInfo.ContentControl, wInfo.ContentParse, isVirtual: false);
                var targetFilePath = StorageUtils.GenerateFileSystemSavePath(
                    holder,
                    uidManager: (h) => {
                        // Only register in indexer when uploading — reads never create DB entries.
                        if (Indexer == null || !forupload) return (0, Guid.Empty);
                        return Indexer.RegisterDocuments(input, h).GetAwaiter().GetResult();
                    },
                    splitProvider: SplitProvider,
                    suffix: Config.SuffixFile,
                    throwExceptions: true)
                    .path;

                if (input.File == null)
                    input.SetFile(new StorageFileRoute(targetFileName, targetFilePath) {
                        Id = holder.Id, Cuid = holder.Cuid.ToString("N"), Version = holder.Version, SaveAsName = holder.StorageName
                    });

                input.File.Path = targetFilePath;
                if (string.IsNullOrWhiteSpace(input.File.Name)) input.File.SetName(input.TargetName);
                if (string.IsNullOrWhiteSpace(input.File.Cuid)) input.File.SetCuid(holder.Cuid);
                if (string.IsNullOrWhiteSpace(input.File.SaveAsName)) input.File.SaveAsName = holder.StorageName;
                if (input.File.Id < 1) input.File.SetId(holder.Id);
                if (forupload) input.File.Size = inputW!.FileStream?.Length ?? 0;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        public (string name, string path) GenerateBasePath(IVaultInfo input, Enums.VaultObjectType component) {
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
                var suffixAddon = (input is VaultProfile pp && pp.ParseMode == VaultParseMode.Generate) ? "f" : "p";
                suffix = suffixAddon + Config.SuffixWorkSpace;
                length = 1; depth = 5;
                break;

                case Enums.VaultObjectType.File:
                // Files use sharded paths generated by StorageUtils.GenerateFileSystemSavePath.
                throw new InvalidOperationException(
                    "GenerateBasePath does not support VaultObjectType.File. " +
                    "Use StorageUtils.GenerateFileSystemSavePath for file path generation.");
            }

            return StorageUtils.GenerateFileSystemSavePath(input, VaultParseMode.Generate,
                (n) => (length, depth), suffix: suffix, throwExceptions: false, caseSensitive: case_sensitive);
        }

        (IVaultInfo target, Enums.VaultObjectType type, string metaFilePath, string cuid) GetTargetInfo<T>(IVaultReadRequest input) where T : IVaultObject {
            IVaultInfo target = null;
            Enums.VaultObjectType targetType = Enums.VaultObjectType.Client;
            string metaFilePath = string.Empty;

            if (typeof(IVaultClient).IsAssignableFrom(typeof(T))) {
                targetType = Enums.VaultObjectType.Client; metaFilePath = CLIENTMETAFILE; target = input.Scope.Client;
            } else if (typeof(IVaultModule).IsAssignableFrom(typeof(T))) {
                targetType = Enums.VaultObjectType.Module; metaFilePath = MODULEMETAFILE; target = input.Scope.Module;
            } else if (typeof(IVaultWorkSpace).IsAssignableFrom(typeof(T))) {
                targetType = Enums.VaultObjectType.WorkSpace; metaFilePath = WORKSPACEMETAFILE; target = input.Scope.Workspace;
            }

            return (target, targetType, metaFilePath, StorageUtils.GenerateCuid(input, targetType));
        }

        /// <summary>
        /// Resolves the stored path for a hierarchy component (client/module/workspace)
        /// and appends it to <paramref name="paths"/>.
        /// When the indexer cache is cold, falls back to reading the .meta file from disk (FS-specific).
        /// </summary>
        void AddComponentPath<T>(IVaultReadRequest input, List<string> paths) where T : IVaultObject {
            if (Indexer == null) return;
            var info = GetTargetInfo<T>(input);
            if (info.target == null) return;

            if (Indexer.TryGetComponentInfo(info.cuid, out T obj)) {
                if (!string.IsNullOrWhiteSpace(obj.Path)) paths.Add(obj.Path);
            } else {
                var tuple = GenerateBasePath(info.target, info.type);
                paths.Add(tuple.path);

                // FS-specific: warm the indexer cache from the on-disk .meta file.
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

            _pathCache.TryAdd(info.cuid, string.Empty);
            _pathCache.TryUpdate(info.cuid, Path.Combine(paths.ToArray()), string.Empty);
        }

        async Task<bool> GetPathFromIndexer(IVaultFileReadRequest input, bool forupload, string workspaceCuid) {
            if (!string.IsNullOrWhiteSpace(input.File?.Cuid) || input.File?.Id > 0) {
                var existing = input.File.Id > 0
                    ? await Indexer.GetDocVersionInfo(input.Scope.Module.Cuid.ToString("N"), input.File.Id)
                    : await Indexer.GetDocVersionInfo(input.Scope.Module.Cuid.ToString("N"), input.File.Cuid);

                if (existing?.Status == true && existing.Result is Dictionary<string, object> dic
                    && dic.TryGetValue("path", out var p) && !string.IsNullOrWhiteSpace(p?.ToString())) {
                    input.File.Path = p.ToString();
                    if (long.TryParse(dic["size"]?.ToString(), out var size)) input.File.Size = size;
                    input.File.SaveAsName = dic["dname"]?.ToString() ?? string.Empty;
                    return true;
                }
            } else if (!string.IsNullOrWhiteSpace(input.File?.Name) || !string.IsNullOrWhiteSpace(input.TargetName)) {
                var searchName = input.File?.Name ?? input.TargetName;
                var dirName = input.Scope.Folder?.Name ?? VaultConstants.DEFAULT_NAME;
                var dirParent = input.Scope.Folder?.Parent?.Id ?? 0;
                var existing = await Indexer.GetDocVersionInfo(input.Scope.Module.Cuid.ToString("N"), workspaceCuid, searchName, dirName, dirParent);

                if (existing?.Status == true && existing.Result is Dictionary<string, object> dic
                    && dic.TryGetValue("path", out var p) && !string.IsNullOrWhiteSpace(p?.ToString())) {
                    if (input.File == null)
                        input.SetFile(new StorageFileRoute(input.TargetName, string.Empty) { Cuid = dic["uid"]?.ToString() });
                    if (string.IsNullOrWhiteSpace(input.File.Cuid)) input.File.SetCuid(dic["uid"]?.ToString());
                    if (string.IsNullOrWhiteSpace(input.File.Name)) input.File.SetName(searchName);
                    input.File.Path = p.ToString();
                    if (long.TryParse(dic["size"]?.ToString(), out var size)) input.File.Size = size;
                    input.File.SaveAsName = dic["dname"]?.ToString() ?? string.Empty;
                    return true;
                } else {
                    if (!forupload) throw new ArgumentException("File not found in the indexer.");
                }
            }
            return false;
        }

        bool PopulateFromSavedPath(IVaultFileReadRequest input, bool forupload, StorageWorkspace wInfo) {
            if (forupload || string.IsNullOrWhiteSpace(input?.File?.SaveAsName)) return false;
            var sname = Path.GetFileNameWithoutExtension(input.File.SaveAsName);
            var extension = Path.GetExtension(input.File.SaveAsName);

            if (wInfo.ContentControl == VaultControlMode.Number && !sname.IsNumber())
                throw new ArgumentException("SaveAsName must be numeric for this workspace.");

            if (wInfo.ContentControl == VaultControlMode.Guid) {
                if (sname.IsCompactGuid(out Guid g) || sname.IsValidGuid(out g))
                    sname = g.ToString("N");
                else
                    throw new ArgumentException("SaveAsName must be a valid GUID for this workspace.");
            }

            input.File.Path = StorageUtils.PreparePath(sname, SplitProvider, wInfo.ContentControl, Config.SuffixFile, extension);
            return true;
        }
    }
}
