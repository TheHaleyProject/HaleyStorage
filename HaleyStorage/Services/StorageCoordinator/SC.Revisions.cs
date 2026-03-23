using Haley.Abstractions;
using Haley.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Haley.Services {
    /// <summary>
    /// Partial class — filesystem revision backup operations (list and stream ##v{n}## copies).
    /// </summary>
    public partial class StorageCoordinator : IStorageCoordinator {

        /// <inheritdoc/>
        public async Task<IFeedback<List<VaultRevisionInfo>>> GetRevisions(IVaultFileReadRequest input) {
            var result = new Feedback<List<VaultRevisionInfo>>() { Status = false };
            try {
                var storageRef = ProcessAndBuildStoragePath(input, true).targetPath;
                if (string.IsNullOrWhiteSpace(storageRef)) {
                    result.Message = "Unable to resolve storage path for this file.";
                    return result;
                }

                if (!(ResolveProvider(input) is FileSystemStorageProvider fsp)) {
                    result.Message = "Revision backups are only available for FileSystem providers.";
                    result.Result  = new List<VaultRevisionInfo>();
                    result.Status  = true;
                    return result;
                }

                result.Result = fsp.GetRevisions(storageRef);
                result.Status = true;
            } catch (Exception ex) {
                result.Message = ex.Message;
                if (ThrowExceptions) throw;
            }
            return result;
        }

        /// <inheritdoc/>
        public async Task<IVaultStreamResponse> DownloadRevision(IVaultFileReadRequest input, int version) {
            var result = new VaultStreamResponse() { Status = false, Stream = Stream.Null };
            try {
                if (version < 1) {
                    result.Message = "Version must be a positive integer.";
                    return result;
                }

                var storageRef = ProcessAndBuildStoragePath(input, true).targetPath;
                if (string.IsNullOrWhiteSpace(storageRef)) {
                    result.Message = "Unable to resolve storage path for this file.";
                    return result;
                }

                if (!(ResolveProvider(input) is FileSystemStorageProvider fsp)) {
                    result.Message = "Revision backups are only available for FileSystem providers.";
                    return result;
                }

                var revisionPath = fsp.GetRevisionPath(storageRef, version);
                var readResult   = await fsp.ReadAsync(revisionPath, autoSearchExtension: false);
                if (!readResult.Success) {
                    result.Message = readResult.Message ?? $"Revision {version} not found.";
                    return result;
                }

                // Build a meaningful download name: original display/storage name with _rev{n} appended.
                var displayBase = Path.GetFileNameWithoutExtension(
                    !string.IsNullOrWhiteSpace(input.File?.DisplayName)
                        ? input.File.DisplayName
                        : input.File?.StorageName ?? Path.GetFileName(storageRef));
                result.SaveName  = $"{displayBase}_rev{version}{Path.GetExtension(storageRef)}";
                result.Extension = readResult.Extension;
                result.Stream    = readResult.Stream;
                result.Status    = true;
            } catch (Exception ex) {
                result.Message = ex.Message;
                if (ThrowExceptions) throw;
            }
            return result;
        }
    }
}
