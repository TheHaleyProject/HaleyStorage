using Haley.Abstractions;
using Haley.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static Haley.Internal.IndexingConstant;
using static Haley.Internal.IndexingQueries;

namespace Haley.Utils {
    internal partial class MariaDBIndexing {
        public async Task<IFeedback<VaultMoveResult>> MoveDocument(IVaultFileReadRequest source, IVaultReadRequest target, bool rename) {
            var fb = new Feedback<VaultMoveResult>();
            try {
                if (source == null || target == null) return fb.SetMessage("Source and target are required.");
                if (source.ReadOnlyMode || target.ReadOnlyMode) return fb.SetMessage("Cannot move a file in read-only mode.");
                if (source.Scope?.Module == null || source.Scope.Module.Cuid == Guid.Empty)
                    return fb.SetMessage("Source module CUID is mandatory.");
                if (target.Scope?.Module == null || target.Scope.Module.Cuid == Guid.Empty)
                    return fb.SetMessage("Target module CUID is mandatory.");
                if (source.Scope.Module.Cuid != target.Scope.Module.Cuid)
                    return fb.SetMessage("File move is supported only inside the same module.");
                if (source.Scope?.Workspace == null || source.Scope.Workspace.Cuid == Guid.Empty)
                    return fb.SetMessage("Source workspace CUID is mandatory.");
                if (target.Scope?.Workspace == null || target.Scope.Workspace.Cuid == Guid.Empty)
                    return fb.SetMessage("Target workspace CUID is mandatory.");

                var moduleCuid = source.Scope.Module.Cuid.ToString("N");
                if (!_agw.ContainsKey(moduleCuid)) return fb.SetMessage($"No adapter found for key {moduleCuid}.");

                var sourceWorkspaceId = await ResolveWorkspaceId(source.Scope.Workspace.Cuid.ToString("N"));
                var targetWorkspaceId = await ResolveWorkspaceId(target.Scope.Workspace.Cuid.ToString("N"));
                if (sourceWorkspaceId < 1 || targetWorkspaceId < 1) return fb.SetMessage("Source and target workspaces must be registered.");

                var documentId = await ResolveDocumentId(moduleCuid, source, includeAll: false);
                if (documentId < 1) return fb.SetMessage("Unable to resolve the active source file.");

                var document = await _agw.RowAsync(moduleCuid, INSTANCE.MOVE.GET_DOCUMENT, default, (ID, documentId));
                if (document == null || document.Count == 0) return fb.SetMessage("Source file is not active.");
                if (document.GetLong("workspace") != sourceWorkspaceId)
                    return fb.SetMessage("Source file does not belong to the requested source workspace.");

                var targetFolder = await ResolveFolderInfo(moduleCuid, target, targetWorkspaceId);
                if (!targetFolder.status) return fb.SetMessage(targetFolder.message);
                if (targetFolder.isRoot) {
                    var defaultFolder = await EnsureDirectory(target, targetWorkspaceId);
                    if (!defaultFolder.status || defaultFolder.result.id < 1)
                        return fb.SetMessage("Unable to resolve the target default folder.");
                    targetFolder = (true, string.Empty, false, defaultFolder.result.id, defaultFolder.result.uid, VaultConstants.DEFAULT_NAME, 0);
                }

                var sourceParent = document.GetLong("parent");
                var targetParent = targetFolder.id;
                var sourceNameId = document.GetLong("name");
                var targetNameId = sourceNameId;
                var finalName = document.GetString("file_name") ?? document.GetString("display_name") ?? document.GetString("cuid") ?? string.Empty;

                if (await HasDocumentConflict(moduleCuid, targetParent, targetNameId, documentId)) {
                    if (!rename) return fb.SetMessage("An active file with the same name already exists in the target folder.");
                    var renamed = await ResolveAvailableDocumentName(moduleCuid, targetParent, finalName);
                    targetNameId = renamed.nameStoreId;
                    finalName = renamed.fileName;
                }

                var handler = _agw.GetTransactionHandler(moduleCuid);
                using (handler?.Begin()) {
                    var load = new DbExecutionLoad(default, handler);
                    await _agw.ExecAsync(moduleCuid, INSTANCE.MOVE.UPDATE_DOCUMENT, load, (ID, documentId), (TARGET_WORKSPACE, targetWorkspaceId), (TARGET_PARENT, targetParent), (NAME, targetNameId));
                    await RebuildStatsInternal(moduleCuid, load);
                }

                await RefreshCoreStats(moduleCuid);
                return fb.SetStatus(true).SetResult(new VaultMoveResult {
                    ItemType = "file",
                    Id = documentId,
                    Cuid = document.GetString("cuid") ?? string.Empty,
                    SourceWorkspaceId = sourceWorkspaceId,
                    SourceParentId = sourceParent,
                    TargetWorkspaceId = targetWorkspaceId,
                    TargetParentId = targetParent,
                    Name = finalName,
                    Renamed = targetNameId != sourceNameId
                });
            } catch (Exception ex) {
                _logger?.LogError(ex.Message + Environment.NewLine + ex.StackTrace);
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<VaultMoveResult>> MoveDirectory(IVaultReadRequest source, IVaultReadRequest target, bool rename) {
            var fb = new Feedback<VaultMoveResult>();
            try {
                if (source == null || target == null) return fb.SetMessage("Source and target are required.");
                if (source.ReadOnlyMode || target.ReadOnlyMode) return fb.SetMessage("Cannot move a directory in read-only mode.");
                if (source.Scope?.Module == null || source.Scope.Module.Cuid == Guid.Empty)
                    return fb.SetMessage("Source module CUID is mandatory.");
                if (target.Scope?.Module == null || target.Scope.Module.Cuid == Guid.Empty)
                    return fb.SetMessage("Target module CUID is mandatory.");
                if (source.Scope.Module.Cuid != target.Scope.Module.Cuid)
                    return fb.SetMessage("Directory move is supported only inside the same module.");
                if (source.Scope?.Workspace == null || source.Scope.Workspace.Cuid == Guid.Empty)
                    return fb.SetMessage("Source workspace CUID is mandatory.");
                if (target.Scope?.Workspace == null || target.Scope.Workspace.Cuid == Guid.Empty)
                    return fb.SetMessage("Target workspace CUID is mandatory.");

                var moduleCuid = source.Scope.Module.Cuid.ToString("N");
                if (!_agw.ContainsKey(moduleCuid)) return fb.SetMessage($"No adapter found for key {moduleCuid}.");

                var sourceWorkspaceId = await ResolveWorkspaceId(source.Scope.Workspace.Cuid.ToString("N"));
                var targetWorkspaceId = await ResolveWorkspaceId(target.Scope.Workspace.Cuid.ToString("N"));
                if (sourceWorkspaceId < 1 || targetWorkspaceId < 1) return fb.SetMessage("Source and target workspaces must be registered.");

                var sourceFolder = await ResolveFolderInfo(moduleCuid, source, sourceWorkspaceId);
                if (!sourceFolder.status) return fb.SetMessage(sourceFolder.message);
                if (sourceFolder.isRoot || sourceFolder.id < 1) return fb.SetMessage("A non-root source folder is required.");

                var sourceRow = await _agw.RowAsync(moduleCuid, INSTANCE.DIRECTORY.GET_DETAILS_BY_ID, default, (VALUE, sourceFolder.id));
                if (sourceRow == null || sourceRow.Count == 0) return fb.SetMessage("Source folder is not active.");
                if (sourceRow.GetLong("workspace") != sourceWorkspaceId)
                    return fb.SetMessage("Source folder does not belong to the requested source workspace.");

                var targetFolder = await ResolveFolderInfo(moduleCuid, target, targetWorkspaceId);
                if (!targetFolder.status) return fb.SetMessage(targetFolder.message);

                var subtreeIds = await CollectDirectoryIds(moduleCuid, sourceFolder.id);
                if (targetFolder.id == sourceFolder.id) return fb.SetMessage("Cannot move a folder into itself.");
                if (targetFolder.id > 0 && subtreeIds.Contains(targetFolder.id))
                    return fb.SetMessage("Cannot move a folder into its own descendant.");

                var sourceParent = sourceRow.GetLong("parent");
                var targetParent = targetFolder.id;
                var finalName = sourceRow.GetString("display_name") ?? sourceRow.GetString("name") ?? sourceRow.GetString("uid") ?? string.Empty;
                var finalDbName = (sourceRow.GetString("name") ?? finalName).ToDBName();

                if (await HasDirectoryConflict(moduleCuid, targetWorkspaceId, targetParent, finalDbName, sourceFolder.id)) {
                    if (!rename) return fb.SetMessage("An active folder with the same name already exists in the target folder.");
                    var renamed = await ResolveAvailableDirectoryName(moduleCuid, targetWorkspaceId, targetParent, finalName);
                    finalName = renamed.displayName;
                    finalDbName = renamed.dbName;
                }

                var handler = _agw.GetTransactionHandler(moduleCuid);
                using (handler?.Begin()) {
                    var load = new DbExecutionLoad(default, handler);
                    await _agw.ExecAsync(moduleCuid, INSTANCE.MOVE.UPDATE_DIRECTORY, load, (ID, sourceFolder.id), (TARGET_WORKSPACE, targetWorkspaceId), (TARGET_PARENT, targetParent), (NAME, finalDbName), (DNAME, finalName));

                    foreach (var directoryId in subtreeIds.Where(id => id != sourceFolder.id)) {
                        await _agw.ExecAsync(moduleCuid, INSTANCE.MOVE.UPDATE_DIRECTORY_WORKSPACE, load, (ID, directoryId), (TARGET_WORKSPACE, targetWorkspaceId));
                    }

                    foreach (var directoryId in subtreeIds) {
                        await _agw.ExecAsync(moduleCuid, INSTANCE.MOVE.UPDATE_DOCUMENTS_IN_DIRECTORY_WORKSPACE, load, (PARENT, directoryId), (TARGET_WORKSPACE, targetWorkspaceId));
                    }

                    await RebuildStatsInternal(moduleCuid, load);
                }

                await RefreshCoreStats(moduleCuid);
                return fb.SetStatus(true).SetResult(new VaultMoveResult {
                    ItemType = "folder",
                    Id = sourceFolder.id,
                    Cuid = sourceFolder.cuid ?? string.Empty,
                    SourceWorkspaceId = sourceWorkspaceId,
                    SourceParentId = sourceParent,
                    TargetWorkspaceId = targetWorkspaceId,
                    TargetParentId = targetParent,
                    Name = finalName,
                    Renamed = !string.Equals(finalName, sourceRow.GetString("display_name"), StringComparison.Ordinal)
                });
            } catch (Exception ex) {
                _logger?.LogError(ex.Message + Environment.NewLine + ex.StackTrace);
                return fb.SetMessage(ex.Message);
            }
        }

        async Task<bool> HasDocumentConflict(string moduleCuid, long targetParent, long nameStoreId, long sourceDocumentId) {
            var conflictId = await _agw.ScalarAsync<long?>(moduleCuid, INSTANCE.MOVE.DOCUMENT_CONFLICT, default, (TARGET_PARENT, targetParent), (NAME, nameStoreId)) ?? 0;
            return conflictId > 0 && conflictId != sourceDocumentId;
        }

        async Task<bool> HasDirectoryConflict(string moduleCuid, long targetWorkspace, long targetParent, string dbName, long sourceDirectoryId) {
            var conflictId = await _agw.ScalarAsync<long?>(moduleCuid, INSTANCE.MOVE.DIRECTORY_CONFLICT, default, (TARGET_WORKSPACE, targetWorkspace), (TARGET_PARENT, targetParent), (NAME, dbName)) ?? 0;
            return conflictId > 0 && conflictId != sourceDirectoryId;
        }

        async Task<(long nameStoreId, string fileName)> ResolveAvailableDocumentName(string moduleCuid, long targetParent, string fileName) {
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "file";

            for (var i = 0; i < 1000; i++) {
                var candidate = i == 0
                    ? $"{baseName}_Copy{ext}"
                    : $"{baseName}_Copy_{i:000}{ext}";
                var ns = await EnsureNameStore(moduleCuid, candidate, readOnly: false);
                if (!ns.status || ns.id < 1) continue;
                if (!await HasDocumentConflict(moduleCuid, targetParent, ns.id, 0))
                    return (ns.id, candidate);
            }

            throw new InvalidOperationException("Unable to find an available file name in the target folder.");
        }

        async Task<(string displayName, string dbName)> ResolveAvailableDirectoryName(string moduleCuid, long targetWorkspace, long targetParent, string folderName) {
            if (string.IsNullOrWhiteSpace(folderName)) folderName = "Folder";

            for (var i = 0; i < 1000; i++) {
                var candidate = i == 0
                    ? $"{folderName}_Copy"
                    : $"{folderName}_Copy_{i:000}";
                var dbName = candidate.ToDBName();
                if (!await HasDirectoryConflict(moduleCuid, targetWorkspace, targetParent, dbName, 0))
                    return (candidate, dbName);
            }

            throw new InvalidOperationException("Unable to find an available folder name in the target folder.");
        }

        async Task<HashSet<long>> CollectDirectoryIds(string moduleCuid, long rootDirectoryId) {
            var result = new HashSet<long>();
            if (rootDirectoryId < 1) return result;

            var queue = new Queue<long>();
            queue.Enqueue(rootDirectoryId);

            while (queue.Count > 0) {
                var currentId = queue.Dequeue();
                if (!result.Add(currentId)) continue;

                var childDirs = await _agw.RowsAsync(moduleCuid, INSTANCE.DIRECTORY.GET_CHILD_IDS_ALL, default, (PARENT, currentId));
                foreach (var row in childDirs) {
                    var childId = row.GetLong("id");
                    if (childId > 0) queue.Enqueue(childId);
                }
            }

            return result;
        }
    }
}
