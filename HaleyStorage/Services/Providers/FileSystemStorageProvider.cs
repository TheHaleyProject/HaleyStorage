using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using System;
using System.IO;
using System.Linq;
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
    ///   - Versioned-file copy for Revise mode
    ///   - Extension-search fallback when no extension is provided
    /// </summary>
    public class FileSystemStorageProvider : IStorageProvider {
        public const string PROVIDER_KEY = "FileSystem";
        public string Key { get; set; } = PROVIDER_KEY;

        // ─── Write ────────────────────────────────────────────────────────────

        public async Task<ProviderWriteResult> WriteAsync(
            string storagePath, Stream dataStream, int bufferSize, ExistConflictResolveMode conflictMode) {

            // 1. Ensure the sharded directory structure exists.
            var targetDir = Path.GetDirectoryName(storagePath);
            if (!await targetDir.TryCreateDirectory())
                return ProviderWriteResult.Fail($"Unable to create storage directory: {targetDir}");

            bool alreadyExists = File.Exists(storagePath);

            if (!alreadyExists) {
                // Simple write — no conflict.
                return await dataStream.TryReplaceFileAsync(storagePath, bufferSize)
                    ? ProviderWriteResult.Ok()
                    : ProviderWriteResult.Fail("Failed to write file.");
            }

            // 2. File exists — apply conflict mode.
            switch (conflictMode) {
                case ExistConflictResolveMode.Skip:
                return ProviderWriteResult.Skipped();

                case ExistConflictResolveMode.ReturnError:
                return ProviderWriteResult.ExistsError();

                case ExistConflictResolveMode.Replace:
                return await dataStream.TryReplaceFileAsync(storagePath, bufferSize)
                    ? ProviderWriteResult.Ok(alreadyExisted: true, message: "Replaced.")
                    : ProviderWriteResult.Fail("Failed to replace file.");

                case ExistConflictResolveMode.Revise:
                // Copy current file to a versioned name, then overwrite the main path.
                if (DirectoryUtils.PopulateVersionedPath(targetDir, storagePath, out var versionPath)) {
                    try {
                        if (await DirectoryUtils.TryCopyFileAsync(storagePath, versionPath)) {
                            return await dataStream.TryReplaceFileAsync(storagePath, bufferSize)
                                ? ProviderWriteResult.Ok(alreadyExisted: true, message: "Revised.")
                                : ProviderWriteResult.Fail("Failed to write revised file.");
                        }
                    } catch (Exception) {
                        await versionPath.TryDeleteFile();
                    }
                }
                return ProviderWriteResult.Fail("Failed to create versioned copy.");

                default:
                return ProviderWriteResult.Fail($"Unhandled conflict mode: {conflictMode}.");
            }
        }

        // ─── Read ─────────────────────────────────────────────────────────────

        public async Task<ProviderReadResult> ReadAsync(
            string storagePath, bool autoSearchExtension = true,
            StringComparison nameComparison = StringComparison.OrdinalIgnoreCase) {

            string resolvedPath = storagePath;

            if (!File.Exists(resolvedPath) && autoSearchExtension
                && string.IsNullOrWhiteSpace(Path.GetExtension(storagePath))) {

                var dir = Path.GetDirectoryName(storagePath);
                var nameWithoutExt = Path.GetFileNameWithoutExtension(storagePath);

                if (!Directory.Exists(dir))
                    return ProviderReadResult.Fail("The storage directory doesn't exist.");

                var matches = new DirectoryInfo(dir).GetFiles()
                    .Where(f => Path.GetFileNameWithoutExtension(f.Name).Equals(nameWithoutExt, nameComparison))
                    .ToList();

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
        public string BuildStorageRef(string logicalId, string extension,
            Func<bool, (int length, int depth)> splitProvider, string suffix) {
            return StorageUtils.PreparePath(logicalId, splitProvider, suffix: suffix, extension: extension);
        }

        /// <summary>
        /// Local filesystem has no URL concept — returns null.
        /// Callers must fall back to streaming bytes through the server.
        /// </summary>
        public Task<string> GetAccessUrl(string storageRef, TimeSpan expiry)
            => Task.FromResult<string>(null);
    }
}
