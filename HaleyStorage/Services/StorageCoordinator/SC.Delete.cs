using Haley.Abstractions;
using Haley.Models;
using Haley.Utils;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Haley.Services {
    /// <summary>
    /// Partial class — delete/archive/restore helpers.
    /// </summary>
    public partial class StorageCoordinator : IStorageCoordinator {
        internal async Task ArchiveDeletedNameCollision(IVaultReadRequest input, string targetFileName) {
            if (Indexer == null || input == null || string.IsNullOrWhiteSpace(targetFileName)) return;

            var collision = await Indexer.GetDeletedDocumentByName(input, targetFileName);
            if (collision?.Status != true || collision.Result == null) return;

            await MoveDeletedDocumentFilesToArchive(input, collision.Result);

            var tombstoneFileName = BuildDeletedTombstoneFileName(collision.Result);
            var finalize = await Indexer.FinalizeDeletedDocumentArchive(input.Scope.Module.Cuid.ToString("N"), collision.Result.DocumentId, tombstoneFileName);
            if (finalize?.Status != true)
                throw new InvalidOperationException(finalize?.Message ?? "Unable to finalize deleted document archive metadata.");
        }

        async Task<IFeedback> EnsureDeletedDocumentFilesAvailableForRestore(IVaultReadRequest request, DeletedDocumentInfo document) {
            var feedback = new Feedback() { Status = false };
            try {
                var pendingMoves = new List<(string source, string target)>();

                foreach (var version in document.Versions) {
                    await CollectRestoreMovesForRef(request, version.ProfileInfoId ?? 0, version.StorageRef, usePrimaryProvider: true, pendingMoves);
                    await CollectRestoreMovesForRef(request, version.ProfileInfoId ?? 0, version.StagingRef, usePrimaryProvider: false, pendingMoves);
                }

                foreach (var move in pendingMoves) {
                    await MoveFileWithOverwrite(move.source, move.target);
                }

                return feedback.SetStatus(true);
            } catch (Exception ex) {
                _logger?.LogError(ex.Message + Environment.NewLine + ex.StackTrace);
                return feedback.SetMessage(ex.Message);
            }
        }

        async Task<IFeedback> EnsureDeletedVersionFilesAvailableForRestore(IVaultReadRequest request, DeletedDocumentInfo document, string targetVersionCuid) {
            var feedback = new Feedback() { Status = false };
            try {
                var target = FindDeletedVersion(document, targetVersionCuid);
                if (target == null)
                    return feedback.SetMessage("Deleted version not found.");

                var pendingMoves = new List<(string source, string target)>();
                var versionsToRestore = target.SubVersionNumber == 0
                    ? document.Versions.Where(v => v.VersionNumber == target.VersionNumber).ToList()
                    : document.Versions.Where(v => v.VersionId == target.VersionId).ToList();

                foreach (var version in versionsToRestore) {
                    await CollectRestoreMovesForRef(request, version.ProfileInfoId ?? 0, version.StorageRef, usePrimaryProvider: true, pendingMoves);
                    await CollectRestoreMovesForRef(request, version.ProfileInfoId ?? 0, version.StagingRef, usePrimaryProvider: false, pendingMoves);
                }

                foreach (var move in pendingMoves) {
                    await MoveFileWithOverwrite(move.source, move.target);
                }

                return feedback.SetStatus(true);
            } catch (Exception ex) {
                _logger?.LogError(ex.Message + Environment.NewLine + ex.StackTrace);
                return feedback.SetMessage(ex.Message);
            }
        }

        async Task MoveDeletedDocumentFilesToArchive(IVaultReadRequest request, DeletedDocumentInfo document) {
            foreach (var version in document.Versions) {
                await MoveDeletedVersionRefToArchive(request, version.ProfileInfoId ?? 0, version.StorageRef, usePrimaryProvider: true);
                await MoveDeletedVersionRefToArchive(request, version.ProfileInfoId ?? 0, version.StagingRef, usePrimaryProvider: false);
            }
        }

        async Task ArchiveDeletedVersionChain(IVaultReadRequest request, DeletedDocumentInfo document, string targetVersionCuid) {
            if (document == null || string.IsNullOrWhiteSpace(targetVersionCuid)) return;

            var target = FindDeletedVersion(document, targetVersionCuid);
            if (target == null)
                throw new InvalidOperationException("Unable to resolve the deleted version for archive finalization.");

            var versionsToArchive = target.SubVersionNumber == 0
                ? document.Versions.Where(v => v.VersionNumber == target.VersionNumber).ToList()
                : document.Versions.Where(v => v.VersionId == target.VersionId).ToList();

            foreach (var version in versionsToArchive) {
                await MoveDeletedVersionRefToArchive(request, version.ProfileInfoId ?? 0, version.StorageRef, usePrimaryProvider: true);
                await MoveDeletedVersionRefToArchive(request, version.ProfileInfoId ?? 0, version.StagingRef, usePrimaryProvider: false);
            }

            var finalize = await Indexer.FinalizeDeletedVersionArchive(
                request.Scope.Module.Cuid.ToString("N"),
                document.DocumentId,
                target.VersionId,
                target.VersionNumber,
                target.SubVersionNumber);

            if (finalize?.Status != true)
                throw new InvalidOperationException(finalize?.Message ?? "Unable to finalize deleted version archive metadata.");
        }

        async Task MoveDeletedVersionRefToArchive(IVaultReadRequest request, long profileInfoId, string storageRef, bool usePrimaryProvider) {
            if (string.IsNullOrWhiteSpace(storageRef)) return;

            var provider = GetFileSystemProviderForVersion(profileInfoId, request.Scope.Module.Cuid.ToString("N"), usePrimaryProvider);
            if (provider == null) return;

            var workspaceBase = FetchWorkspaceBasePath(request, provider);
            var sourcePath = provider.BuildFullPath(workspaceBase, storageRef);
            var archivePath = BuildDeletedArchivePath(request, storageRef);

            if (File.Exists(sourcePath)) {
                await MoveFileWithOverwrite(sourcePath, archivePath);
                return;
            }

            if (File.Exists(archivePath))
                return;

            _logger?.LogWarning($"Deleted source file was not found during archive move. Source: {sourcePath}");
        }

        async Task CollectRestoreMovesForRef(IVaultReadRequest request, long profileInfoId, string storageRef, bool usePrimaryProvider, List<(string source, string target)> pendingMoves) {
            if (string.IsNullOrWhiteSpace(storageRef)) return;

            var provider = GetFileSystemProviderForVersion(profileInfoId, request.Scope.Module.Cuid.ToString("N"), usePrimaryProvider);
            if (provider == null) return;

            var workspaceBase = FetchWorkspaceBasePath(request, provider);
            var originalPath = provider.BuildFullPath(workspaceBase, storageRef);
            var archivePath = BuildDeletedArchivePath(request, storageRef);

            if (File.Exists(originalPath))
                return;
            if (File.Exists(archivePath)) {
                pendingMoves.Add((archivePath, originalPath));
                return;
            }

            throw new FileNotFoundException($"Deleted file content is missing. Unable to restore '{storageRef}'.");
        }

        FileSystemStorageProvider GetFileSystemProviderForVersion(long profileInfoId, string moduleCuid, bool usePrimaryProvider) {
            var providers = GetProvidersForProfile(profileInfoId, moduleCuid);
            var provider = usePrimaryProvider ? providers.primary : providers.staging;
            return provider as FileSystemStorageProvider;
        }

        string BuildDeletedArchivePath(IVaultReadRequest request, string storageRef) {
            var clientDir = request.Scope.Client?.Name?.ToDBName() ?? string.Empty;
            var moduleDir = request.Scope.Module?.Name?.ToDBName() ?? string.Empty;
            return Path.Combine(BasePath, "_deleted", clientDir, moduleDir, storageRef);
        }

        static string BuildDeletedTombstoneFileName(DeletedDocumentInfo document) {
            var extension = Path.GetExtension(document.RestoreFileName ?? string.Empty);
            return $"__deleted__{document.DocumentCuid}{extension}";
        }

        static bool SameCuid(string left, string right) {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
            if (Guid.TryParse(left, out var lg) && Guid.TryParse(right, out var rg))
                return lg == rg;
            return left.Equals(right, StringComparison.OrdinalIgnoreCase);
        }

        static DeletedDocumentVersionInfo FindDeletedVersion(DeletedDocumentInfo document, string targetVersionCuid)
            => document?.Versions?.FirstOrDefault(v => SameCuid(v.VersionCuid, targetVersionCuid));

        static async Task MoveFileWithOverwrite(string sourcePath, string targetPath) {
            var targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetDir))
                await targetDir.TryCreateDirectory();
            if (File.Exists(targetPath))
                File.Delete(targetPath);
            File.Move(sourcePath, targetPath);
        }
    }
}
