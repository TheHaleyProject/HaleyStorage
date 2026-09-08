using Haley.Abstractions;
using Haley.Models;
using Haley.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Haley.Services {
    public partial class StorageCoordinator : IStorageCoordinator {
        public async Task<IFeedback<VaultMoveResult>> MoveFile(IVaultFileReadRequest source, IVaultReadRequest target, bool rename = false) {
            var fb = new Feedback<VaultMoveResult>();
            try {
                if (source == null || target == null) return fb.SetMessage("Source and target are required.");
                var sourceRootCuid = (source.File as StorageFileRoute)?.RootCuid;
                var sourceAccess = await CheckTargetWriteAccessAsync(source, source.File?.Id, source.File?.Cuid, sourceRootCuid);
                if (!sourceAccess.Status) return fb.SetMessage(sourceAccess.Message);
                var targetAccess = CheckWriteAccess(target);
                if (!targetAccess.Status) return fb.SetMessage(targetAccess.Message);
                if (Indexer == null) return fb.SetMessage("MoveFile requires an indexer.");
                if (source.Scope?.Workspace == null || target.Scope?.Workspace == null)
                    return fb.SetMessage("Source and target workspace information is required.");

                source.Scope.Workspace.SetCuid(StorageUtils.GenerateCuid(source, Enums.VaultObjectType.WorkSpace));
                target.Scope.Workspace.SetCuid(StorageUtils.GenerateCuid(target, Enums.VaultObjectType.WorkSpace));
                await EnsureWorkspaceContextAsync(source, forceRefresh: true);
                await EnsureWorkspaceContextAsync(target, forceRefresh: true);

                var relocatedFiles = await RelocateDocumentFilesForWorkspaceMove(source, target);
                IFeedback<VaultMoveResult> moved;
                try {
                    moved = await Indexer.MoveDocument(source, target, rename);
                } catch {
                    await RollbackWorkspaceFileMoves(relocatedFiles);
                    throw;
                }
                if (moved?.Status != true)
                    await RollbackWorkspaceFileMoves(relocatedFiles);
                return moved;
            } catch (Exception ex) {
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<VaultMoveResult>> MoveDirectory(IVaultReadRequest source, IVaultReadRequest target, bool rename = false) {
            var fb = new Feedback<VaultMoveResult>();
            try {
                if (source == null || target == null) return fb.SetMessage("Source and target are required.");
                var sourceAccess = CheckWriteAccess(source);
                if (!sourceAccess.Status) return fb.SetMessage(sourceAccess.Message);
                var targetAccess = CheckWriteAccess(target);
                if (!targetAccess.Status) return fb.SetMessage(targetAccess.Message);
                if (Indexer == null) return fb.SetMessage("MoveDirectory requires an indexer.");
                if (source.Scope?.Workspace == null || target.Scope?.Workspace == null)
                    return fb.SetMessage("Source and target workspace information is required.");

                source.Scope.Workspace.SetCuid(StorageUtils.GenerateCuid(source, Enums.VaultObjectType.WorkSpace));
                target.Scope.Workspace.SetCuid(StorageUtils.GenerateCuid(target, Enums.VaultObjectType.WorkSpace));
                await EnsureWorkspaceContextAsync(source, forceRefresh: true);
                await EnsureWorkspaceContextAsync(target, forceRefresh: true);

                var sourceProvider = ResolveProvider(source);
                var targetProvider = ResolveProvider(target);
                if (sourceProvider is FileSystemStorageProvider && targetProvider is FileSystemStorageProvider) {
                    var sourceBase = FetchWorkspaceBasePath(source, sourceProvider);
                    var targetBase = FetchWorkspaceBasePath(target, targetProvider);
                    if (!SamePhysicalPath(sourceBase, targetBase))
                        return fb.SetMessage("Moving a folder between different physical workspace paths is not supported yet. Move its files individually or use virtual workspaces.");
                }
                return await Indexer.MoveDirectory(source, target, rename);
            } catch (Exception ex) {
                return fb.SetMessage(ex.Message);
            }
        }

        async Task<List<(string source, string target)>> RelocateDocumentFilesForWorkspaceMove(
            IVaultFileReadRequest source,
            IVaultReadRequest target) {

            var moved = new List<(string source, string target)>();
            if (Indexer is not MariaDBIndexing mariaIndexer) return moved;

            var lifecycle = await mariaIndexer.GetDocumentLifecycleForRestore(source);
            if (lifecycle?.Status != true || lifecycle.Result == null)
                throw new InvalidOperationException(lifecycle?.Message ?? "Unable to load document versions for the workspace move.");

            var incomplete = lifecycle.Result.Versions.FirstOrDefault(version =>
                version.DeleteState == 0 && (version.Flags & (int)Enums.VersionFlags.Completed) == 0);
            if (incomplete != null)
                throw new InvalidOperationException($"Document move is blocked while version {incomplete.VersionCuid} has an incomplete upload.");

            var planned = new List<(string source, string target)>();
            var seen = new HashSet<string>(OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);

            foreach (var version in lifecycle.Result.Versions.Where(version => version.DeleteState != 2 && version.DeleteState != 3)) {
                PlanWorkspaceFileMove(source, target, version.ProfileInfoId ?? 0, version.StorageRef, usePrimaryProvider: true, planned, seen);
                PlanWorkspaceFileMove(source, target, version.ProfileInfoId ?? 0, version.StagingRef, usePrimaryProvider: false, planned, seen);
            }

            try {
                foreach (var item in planned) {
                    var targetDir = Path.GetDirectoryName(item.target);
                    if (!string.IsNullOrWhiteSpace(targetDir)) Directory.CreateDirectory(targetDir);
                    File.Move(item.source, item.target, false);
                    moved.Add(item);
                }
                return moved;
            } catch {
                await RollbackWorkspaceFileMoves(moved);
                throw;
            }
        }

        void PlanWorkspaceFileMove(
            IVaultReadRequest source,
            IVaultReadRequest target,
            long profileInfoId,
            string storageRef,
            bool usePrimaryProvider,
            List<(string source, string target)> planned,
            HashSet<string> seen) {

            if (string.IsNullOrWhiteSpace(storageRef)) return;
            var moduleCuid = source.Scope.Module.Cuid.ToString("N");
            var provider = GetFileSystemProviderForVersion(profileInfoId, moduleCuid, usePrimaryProvider);
            if (provider == null) return;

            var sourcePath = provider.BuildFullPath(FetchWorkspaceBasePath(source, provider), storageRef);
            var targetPath = provider.BuildFullPath(FetchWorkspaceBasePath(target, provider), storageRef);
            if (SamePhysicalPath(sourcePath, targetPath) || !seen.Add(sourcePath)) return;
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException($"Cannot move the document because stored bytes are missing: '{storageRef}'.", sourcePath);
            if (File.Exists(targetPath))
                throw new IOException($"Cannot move the document because the target storage path already exists: '{targetPath}'.");
            planned.Add((sourcePath, targetPath));
        }

        static Task RollbackWorkspaceFileMoves(List<(string source, string target)> moved) {
            for (var index = moved.Count - 1; index >= 0; index--) {
                var item = moved[index];
                if (!File.Exists(item.target) || File.Exists(item.source)) continue;
                var sourceDir = Path.GetDirectoryName(item.source);
                if (!string.IsNullOrWhiteSpace(sourceDir)) Directory.CreateDirectory(sourceDir);
                File.Move(item.target, item.source, false);
            }
            return Task.CompletedTask;
        }

        static bool SamePhysicalPath(string left, string right) {
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), comparison);
        }
    }
}
