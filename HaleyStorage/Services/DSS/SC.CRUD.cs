using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;

namespace Haley.Services {

    public partial class StorageCoordinator : IStorageCoordinator {

        // ─── Upload ───────────────────────────────────────────────────────────

        public async Task<IVaultResponse> Upload(IVaultFileWriteRequest input) {
            var result = new VaultResponse() { Status = false, RawName = input?.FileOriginalName };
            try {
                if (!WriteMode) { result.Message = "Application is in Read-Only mode."; return result; }
                if (input == null) { result.Message = "Input cannot be empty or null."; return result; }

                input.GenerateCallId();
                var gPaths = ProcessAndBuildStoragePath(input, true);

                if (string.IsNullOrWhiteSpace(input.TargetPath)) {
                    result.Message = "Unable to generate the final storage path. Please check inputs.";
                    return result;
                }
                if (input.TargetPath == gPaths.basePath)
                    throw new ArgumentException("No file save name is processed.");
                if (input.FileStream == null)
                    throw new ArgumentException("File stream is null. Nothing to save.");

                // Security: ensure target is within the storage root (path traversal guard).
                if (!input.TargetPath.StartsWith(BasePath)) {
                    result.Message = "Not authorized for this path. Please check the inputs.";
                    return result;
                }

                if (input.BufferSize < (1024 * 80)) input.BufferSize = (1024 * 80);

                var writeResult = await GetDefaultProvider().WriteAsync(
                    input.TargetPath, input.FileStream, input.BufferSize, input.ResolveMode);

                result.Status = writeResult.Success;
                result.PhysicalObjectExists = writeResult.AlreadyExisted;
                result.Message = writeResult.Message;

                if (input.File != null) result.SetResult(input.File);
                if (result.Status && !writeResult.AlreadyExisted) {
                    result.Size = input.FileStream.Length;
                    result.SizeHR = result.Size.ToFileSize(false);
                }
            } catch (Exception ex) {
                result.Message = ex.Message + Environment.NewLine + ex.StackTrace;
                result.Status = false;
            } finally {
                IFeedback upInfo = null;
                if (WriteMode && Indexer != null && input != null && input.Module != null) {
                    if (input.File != null && result.Status) {
                        upInfo = await Indexer.UpdateDocVersionInfo(input.Module.Cuid, input.File, input.CallID);
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

        public async Task<IVaultStreamResponse> Download(IVaultFileReadRequest input, bool auto_search_extension = true) {
            var result = new VaultStreamResponse() { Status = false, Stream = Stream.Null };

            var path = ProcessAndBuildStoragePath(input, true).targetPath;
            if (string.IsNullOrWhiteSpace(path)) return result;

            var comparison = _caseSensitivePairs.Any(p => p.client.Equals(input.Client.Name.ToDBName()))
                ? StringComparison.InvariantCulture
                : StringComparison.OrdinalIgnoreCase;

            var readResult = await GetDefaultProvider().ReadAsync(path, auto_search_extension, comparison);

            if (!readResult.Success) { result.Message = readResult.Message; return result; }

            result.SaveName = input.File?.SaveAsName;
            result.Status = true;
            result.Extension = readResult.Extension;
            result.Stream = readResult.Stream;
            return result;
        }

        public async Task<IVaultStreamResponse> Download(IVaultFileRoute input, bool auto_search_extension = true) {
            var result = new VaultStreamResponse() { Status = false, Stream = Stream.Null };

            if (input == null || string.IsNullOrWhiteSpace(input.Path)) {
                result.Message = "File route path is empty.";
                return result;
            }

            // input.Path is the relative storage reference. Combine with BasePath for FS provider.
            var path = Path.IsPathRooted(input.Path) ? input.Path : Path.Combine(BasePath, input.Path);

            var readResult = await GetDefaultProvider().ReadAsync(path, auto_search_extension);

            if (!readResult.Success) { result.Message = readResult.Message; return result; }

            result.SaveName = input.SaveAsName;
            result.Status = true;
            result.Extension = readResult.Extension;
            result.Stream = readResult.Stream;
            return result;
        }

        // ─── Delete ───────────────────────────────────────────────────────────

        public async Task<IFeedback> Delete(IVaultFileReadRequest input) {
            var feedback = new Feedback() { Status = false };
            if (!WriteMode) { feedback.Message = "Application is in Read-Only mode."; return feedback; }

            var path = ProcessAndBuildStoragePath(input, true).targetPath;
            if (string.IsNullOrWhiteSpace(path)) {
                feedback.Message = "Unable to generate path from provided inputs.";
                return feedback;
            }

            var provider = GetDefaultProvider();
            if (!provider.Exists(path)) {
                feedback.Message = $"File does not exist: {path}.";
                return feedback;
            }

            feedback.Status = await provider.DeleteAsync(path);
            feedback.Message = feedback.Status
                ? "File deleted."
                : "Unable to delete the file. Check if it is in use by another process and try again.";
            return feedback;
        }

        // ─── Exists / GetSize ─────────────────────────────────────────────────

        public IFeedback Exists(IVaultReadRequest input, bool isFilePath = false) {
            var feedback = new Feedback() { Status = false };
            var path = ProcessAndBuildStoragePath(input, isFilePath).targetPath;
            if (string.IsNullOrWhiteSpace(path)) {
                feedback.Message = "Unable to generate path from provided inputs.";
                return feedback;
            }

            // For files: ask the provider.
            // For directories: virtual dirs are in DB (TODO: query indexer); physical workspace dirs checked via FS.
            feedback.Status = isFilePath ? GetDefaultProvider().Exists(path) : Directory.Exists(path);
            if (!feedback.Status) feedback.Message = $"Does not exist: {path}";
            return feedback;
        }

        public long GetSize(IVaultReadRequest input) {
            var path = ProcessAndBuildStoragePath(input, true).targetPath;
            if (string.IsNullOrWhiteSpace(path)) return 0;
            return GetDefaultProvider().GetSize(path);
        }

        // ─── GetParent ────────────────────────────────────────────────────────

        public async Task<IFeedback<string>> GetParent(IVaultFileReadRequest input) {
            input.Workspace.SetCuid(StorageUtils.GenerateCuid(input, Enums.VaultObjectType.WorkSpace));
            return await Indexer?.GetParentName(input);
        }

        // ─── Directory operations (virtual — pending MariaDB phase) ───────────

        public Task<IVaultDirResponse> GetDirectoryInfo(IVaultReadRequest input) {
            // TODO: virtual directories live in MariaDB — query Indexer.GetDirectoryInfo once available.
            return Task.FromResult<IVaultDirResponse>(new VaultDirResponse() {
                Status = false,
                Message = "GetDirectoryInfo requires indexer implementation (pending MariaDB phase)."
            });
        }

        public Task<IVaultResponse> CreateDirectory(IVaultReadRequest input, string rawname) {
            // TODO: register virtual directory in MariaDB via Indexer.RegisterDirectory.
            return Task.FromResult<IVaultResponse>(new VaultResponse() {
                Status = false, RawName = rawname,
                Message = "CreateDirectory requires indexer implementation (pending MariaDB phase)."
            });
        }

        public Task<IFeedback> DeleteDirectory(IVaultReadRequest input, bool recursive) {
            // TODO: soft-delete virtual directory in MariaDB via Indexer.DeleteDirectory.
            return Task.FromResult<IFeedback>(new Feedback() {
                Status = false,
                Message = "DeleteDirectory requires indexer implementation (pending MariaDB phase)."
            });
        }
    }
}
