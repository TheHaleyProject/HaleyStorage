using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Haley.Services {
    /// <summary>
    /// Local-disk implementation of IStorageProvider.
    ///
    /// All storage references are absolute file paths on the local filesystem.
    /// Paths must be fully resolved by the StorageCoordinator before being passed here.
    ///
    /// This class owns all FS-specific I/O details:
    ///   - Directory creation for the sharded target path
    ///   - Conflict resolution (Skip / ReturnError / Replace / Revise)
    ///   - Versioned-file copy for Revise mode with configurable max-copies pruning
    ///   - Extension-search fallback when no extension is provided
    /// </summary>
    public class FileSystemStorageProvider : IStorageProvider {
        public const string PROVIDER_KEY = "FileSystem";
        public string Key { get; set; } = PROVIDER_KEY;

        /// <summary>
        /// Maximum number of <c>##v&lt;n&gt;##</c> revision copies to retain beside the live file.
        /// Older revisions beyond this limit are pruned after each Revise write.
        /// Set to 0 to disable pruning. Default: 3.
        /// </summary>
        public int MaxRevisionCopies { get; set; } = 3;

        // ─── Write ────────────────────────────────────────────────────────────

        public async Task<ProviderWriteResult> WriteAsync(string storagePath, Stream dataStream, int bufferSize, ExistConflictResolveMode conflictMode) {

            // 1. Ensure the sharded directory structure exists.
            var targetDir = Path.GetDirectoryName(storagePath);
            if (!await targetDir.TryCreateDirectory())
                return ProviderWriteResult.Fail($"Unable to create storage directory: {targetDir}");

            bool alreadyExists = File.Exists(storagePath);

            if (!alreadyExists) {
                // Simple write — no conflict.
                return await dataStream.TryReplaceFileAsync(storagePath, bufferSize)? ProviderWriteResult.Ok() : ProviderWriteResult.Fail("Failed to write file.");
            }

            // 2. File exists — apply conflict mode.
            switch (conflictMode) {
                case ExistConflictResolveMode.Skip:
                return ProviderWriteResult.Skipped();

                case ExistConflictResolveMode.ReturnError:
                return ProviderWriteResult.ExistsError();

                case ExistConflictResolveMode.Replace:
                    // PopulateVersionedPath computes the next version name and prunes old revisions beyond MaxRevisionCopies.
                    if (DirectoryUtils.PopulateVersionedPath(targetDir, storagePath, out var versionPath, MaxRevisionCopies)) {
                        try {
                            if (await DirectoryUtils.TryCopyFileAsync(storagePath, versionPath)) {
                                return await dataStream.TryReplaceFileAsync(storagePath, bufferSize) ? ProviderWriteResult.Ok(alreadyExisted: true, message: "Revised.") : ProviderWriteResult.Fail("Failed to write revised file.");
                            }
                        } catch (Exception) {
                            await versionPath.TryDeleteFile();
                        }
                    }
                    return ProviderWriteResult.Fail("Failed to revise the file.");
                default:
                return ProviderWriteResult.Fail($"Unhandled conflict mode: {conflictMode}.");
            }
        }

        // ─── Read ─────────────────────────────────────────────────────────────

        public async Task<ProviderReadResult> ReadAsync(string storagePath, bool autoSearchExtension = true, StringComparison nameComparison = StringComparison.OrdinalIgnoreCase) {

            string resolvedPath = storagePath;

            if (!File.Exists(resolvedPath) && autoSearchExtension && string.IsNullOrWhiteSpace(Path.GetExtension(storagePath))) {

                var dir = Path.GetDirectoryName(storagePath);
                var nameWithoutExt = Path.GetFileNameWithoutExtension(storagePath);

                if (!Directory.Exists(dir))
                    return ProviderReadResult.Fail("The storage directory doesn't exist.");

                var matches = new DirectoryInfo(dir).GetFiles().Where(f => Path.GetFileNameWithoutExtension(f.Name).Equals(nameWithoutExt, nameComparison)).ToList();

                if (matches.Count == 1) {
                    resolvedPath = matches[0].FullName;
                } else if (matches.Count > 1) {
                    return ProviderReadResult.Fail("Multiple matching files found. Please provide a valid extension.");
                }
            }

            if (!File.Exists(resolvedPath))
                return ProviderReadResult.Fail("File doesn't exist.");

            Stream stream = new FileStream(resolvedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return await Task.FromResult(ProviderReadResult.Ok(stream, Path.GetExtension(resolvedPath)));
        }

        // ─── Delete / Exists / Size ───────────────────────────────────────────

        public Task<bool> DeleteAsync(string storagePath) => storagePath.TryDeleteFile();

        public bool Exists(string storagePath) => File.Exists(storagePath);

        public long GetSize(string storagePath) {
            if (!File.Exists(storagePath)) return 0;
            return new FileInfo(storagePath).Length;
        }

        // ─── Storage reference builder ────────────────────────────────────────

        /// <summary>
        /// Applies directory sharding to the logical ID so related files are spread
        /// across a balanced directory tree instead of a single flat folder.
        /// Example: logicalId="1234567", depth=2, len=2 → "12/34/1234567.mp4"
        /// </summary>
        public string BuildStorageRef(string logicalId, string extension, Func<bool, (int length, int depth)> splitProvider, string suffix) {
            return StorageUtils.PreparePath(logicalId, splitProvider, suffix: suffix, extension: extension);
        }

        /// <summary>
        /// Combines the workspace base path with a file storage reference using OS-native separators.
        /// Rejects paths containing ".." to prevent directory traversal.
        /// Override this in a subclass to inject a prefix/suffix or apply FS-specific constraints.
        /// </summary>
        public string BuildFullPath(string basePath, string fileRef) {
            var result = string.IsNullOrEmpty(fileRef) ? basePath : Path.Combine(basePath, fileRef);
            if (result.Contains(".."))
                throw new ArgumentOutOfRangeException(nameof(fileRef), "Path contains invalid traversal segments.");
            return result;
        }

        /// <summary>
        /// Local filesystem has no URL concept — returns null.
        /// Callers must fall back to streaming bytes through the server.
        /// </summary>
        public Task<string> GetAccessUrl(string storageRef, TimeSpan expiry)
            => Task.FromResult<string>(null);

        // ─── Revision backups ─────────────────────────────────────────────────

        /// <summary>
        /// Returns all <c>##v{n}##</c> revision backups that exist alongside <paramref name="storageRef"/>,
        /// ordered newest-first (highest version number first).
        /// Uses the same naming pattern as <see cref="DirectoryUtils.PopulateVersionedPath"/>:
        /// <c>{basename}.##v{n}##.{ext}</c>.
        /// Returns an empty list when the live file or its directory does not exist.
        /// No DB query — all metadata comes from the filesystem.
        /// </summary>
        public List<VaultRevisionInfo> GetRevisions(string storageRef) {
            if (string.IsNullOrWhiteSpace(storageRef) || !File.Exists(storageRef))
                return new List<VaultRevisionInfo>();

            var dir      = Path.GetDirectoryName(storageRef);
            var basename = Path.GetFileName(storageRef);
            var ext      = Path.GetExtension(basename)?.TrimStart('.') ?? string.Empty;

            var pattern = $@"^{Regex.Escape(basename)}\.##v(\d+)##\.{Regex.Escape(ext)}$";
            var regex   = new Regex(pattern, RegexOptions.IgnoreCase);

            return Directory.GetFiles(dir, $"{basename}.*")
                .Select(f => {
                    var m = regex.Match(Path.GetFileName(f));
                    if (!m.Success || !int.TryParse(m.Groups[1].Value, out var n)) return null;
                    var fi = new FileInfo(f);
                    return new VaultRevisionInfo {
                        Version         = n,
                        Size            = fi.Length,
                        SizeHR          = fi.Length.ToFileSize(false),
                        LastModifiedUtc = fi.LastWriteTimeUtc
                    };
                })
                .Where(x => x != null)
                .OrderByDescending(x => x.Version)
                .ToList();
        }

        /// <summary>
        /// Derives the full path of a specific revision backup from the live file's storage ref
        /// and version number. Does not check whether the file exists.
        /// </summary>
        public string GetRevisionPath(string storageRef, int version) {
            var basename = Path.GetFileName(storageRef);
            var ext      = Path.GetExtension(basename)?.TrimStart('.') ?? string.Empty;
            var dir      = Path.GetDirectoryName(storageRef);
            return Path.Combine(dir, $"{basename}.##v{version}##.{ext}");
        }
    }
}
