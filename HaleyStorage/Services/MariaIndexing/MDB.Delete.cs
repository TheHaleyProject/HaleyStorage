using Haley.Abstractions;
using Haley.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Haley.Internal.IndexingConstant;
using static Haley.Internal.IndexingQueries;

namespace Haley.Utils {
    /// <summary>
    /// Partial class — soft delete, deleted-collision lookup, archive finalization, restore,
    /// and recursive folder delete.
    /// </summary>
    internal partial class MariaDBIndexing {
        public async Task<IFeedback<DeletedDocumentInfo>> SoftDeleteVersion(IVaultFileReadRequest request) {
            var fb = new Feedback<DeletedDocumentInfo>();
            try {
                if (request == null) return fb.SetMessage("Request cannot be null.");
                if (request.ReadOnlyMode) return fb.SetMessage("Cannot delete a version in read-only mode.");
                if (request.Scope?.Module == null || request.Scope.Module.Cuid == Guid.Empty)
                    return fb.SetMessage("Module CUID is mandatory.");
                if (string.IsNullOrWhiteSpace(request.File?.Cuid))
                    return fb.SetMessage("Version uid is mandatory for version delete.");

                var moduleCuid = request.Scope.Module.Cuid.ToString("N");
                var target = await _agw.RowAsync(moduleCuid, INSTANCE.DOCVERSION.GET_DELETE_TARGET_BY_CUID, default, (CUID, ToDbCuid(request.File.Cuid)));
                if (target == null || target.Count < 1)
                    return fb.SetMessage("Unable to resolve the target active version.");

                var versionId = target.GetLong("version_id");
                var documentId = target.GetLong("document_id");
                var versionNo = target.GetInt("version_no");
                var subVer = target.GetInt("sub_version_no");
                var deleteState = target.GetInt("delete_state");

                if (deleteState > 0)
                    return fb.SetMessage("Version is already deleted.");

                var handler = _agw.GetTransactionHandler(moduleCuid);
                DeletedDocumentInfo lifecycle;
                using (handler?.Begin()) {
                    var load = new DbExecutionLoad(default, handler);
                    var deletedAt = DateTime.UtcNow;

                    if (subVer == 0) {
                        // Delete the content version and any thumbnails attached to the same content version.
                        await _agw.ExecAsync(moduleCuid, INSTANCE.DOCVERSION.SOFT_DELETE_BY_VERSION, load, (PARENT, documentId), (VERSION, versionNo), (DELETED, deletedAt));
                    } else {
                        // Thumbnail uid delete only hides that specific thumbnail row.
                        await _agw.ExecAsync(moduleCuid, INSTANCE.DOCVERSION.SOFT_DELETE_BY_ID, load, (ID, versionId), (DELETED, deletedAt));
                    }

                    lifecycle = await GetDocumentLifecycleById(moduleCuid, documentId);
                }

                if (lifecycle == null)
                    return fb.SetMessage("Version delete could not be confirmed.");
                if (lifecycle.IsDeleted)
                    return fb.SetMessage("The logical document is deleted. Restore the document instead.");

                return fb.SetStatus(true).SetResult(lifecycle).SetMessage(subVer == 0
                    ? "Version deleted."
                    : "Thumbnail version deleted.");
            } catch (Exception ex) {
                _logger?.LogError(ex.Message + Environment.NewLine + ex.StackTrace);
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<DeletedDocumentInfo>> SoftDeleteDocument(IVaultFileReadRequest request) {
            var fb = new Feedback<DeletedDocumentInfo>();
            try {
                if (request == null) return fb.SetMessage("Request cannot be null.");
                if (request.ReadOnlyMode) return fb.SetMessage("Cannot delete a document in read-only mode.");
                if (request.Scope?.Module == null || request.Scope.Module.Cuid == Guid.Empty)
                    return fb.SetMessage("Module CUID is mandatory.");

                var moduleCuid = request.Scope.Module.Cuid.ToString("N");
                var documentId = await ResolveDocumentIdForLifecycle(moduleCuid, request, includeDeleted: false);
                if (documentId < 1) return fb.SetMessage("Unable to resolve the target document.");

                var lifecycle = await GetDocumentLifecycleById(moduleCuid, documentId);
                if (lifecycle == null) return fb.SetMessage("Unable to load the target document.");
                if (lifecycle.IsDeleted) return fb.SetMessage("Document is already deleted.");

                var handler = _agw.GetTransactionHandler(moduleCuid);
                using (handler?.Begin()) {
                    var load = new DbExecutionLoad(default, handler);
                    var deletedAt = DateTime.UtcNow;
                    await _agw.ExecAsync(moduleCuid, INSTANCE.DOCUMENT.SOFT_DELETE_BY_ID, load, (ID, documentId), (DELETED, deletedAt));
                    await _agw.ExecAsync(moduleCuid, INSTANCE.DOCVERSION.SOFT_DELETE_BY_PARENT, load, (PARENT, documentId), (DELETED, deletedAt));
                }

                lifecycle = await GetDocumentLifecycleById(moduleCuid, documentId);
                if (lifecycle == null || !lifecycle.IsDeleted)
                    return fb.SetMessage("Document delete could not be confirmed.");
                return fb.SetStatus(true).SetResult(lifecycle);
            } catch (Exception ex) {
                _logger?.LogError(ex.Message + Environment.NewLine + ex.StackTrace);
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<DeletedDocumentInfo>> GetDeletedVersion(IVaultFileReadRequest request) {
            var fb = new Feedback<DeletedDocumentInfo>();
            try {
                if (request == null) return fb.SetMessage("Request cannot be null.");
                if (request.Scope?.Module == null || request.Scope.Module.Cuid == Guid.Empty)
                    return fb.SetMessage("Module CUID is mandatory.");
                if (string.IsNullOrWhiteSpace(request.File?.Cuid))
                    return fb.SetMessage("Version uid is mandatory.");

                var moduleCuid = request.Scope.Module.Cuid.ToString("N");
                var target = await _agw.RowAsync(moduleCuid, INSTANCE.DOCVERSION.GET_DELETE_TARGET_BY_CUID, default, (CUID, ToDbCuid(request.File.Cuid)));
                if (target == null || target.Count < 1)
                    return fb.SetMessage("Unable to resolve the target version.");

                var lifecycle = await GetDocumentLifecycleById(moduleCuid, target.GetLong("document_id"));
                if (lifecycle == null)
                    return fb.SetMessage("Unable to load the target document lifecycle.");

                var version = lifecycle.Versions.FirstOrDefault(v => SameCuid(v.VersionCuid, request.File.Cuid));
                if (version == null || !version.IsDeleted)
                    return fb.SetMessage("Deleted version not found.");

                if (lifecycle.IsDeleted)
                    return fb.SetMessage("The logical document is deleted. Restore the document instead.");

                return fb.SetStatus(true).SetResult(lifecycle);
            } catch (Exception ex) {
                _logger?.LogError(ex.Message + Environment.NewLine + ex.StackTrace);
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<DeletedDocumentInfo>> GetDeletedDocument(IVaultFileReadRequest request) {
            var fb = new Feedback<DeletedDocumentInfo>();
            try {
                if (request == null) return fb.SetMessage("Request cannot be null.");
                if (request.Scope?.Module == null || request.Scope.Module.Cuid == Guid.Empty)
                    return fb.SetMessage("Module CUID is mandatory.");

                var moduleCuid = request.Scope.Module.Cuid.ToString("N");
                var documentId = await ResolveDocumentIdForLifecycle(moduleCuid, request, includeDeleted: true);
                if (documentId < 1) return fb.SetMessage("Unable to resolve the target deleted document.");

                var lifecycle = await GetDocumentLifecycleById(moduleCuid, documentId);
                if (lifecycle == null || !lifecycle.IsDeleted)
                    return fb.SetMessage("Deleted document not found.");

                return fb.SetStatus(true).SetResult(lifecycle);
            } catch (Exception ex) {
                _logger?.LogError(ex.Message + Environment.NewLine + ex.StackTrace);
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<DeletedDocumentInfo>> GetDeletedDocumentByName(IVaultReadRequest request, string fileName) {
            var fb = new Feedback<DeletedDocumentInfo>();
            try {
                if (request == null) return fb.SetMessage("Request cannot be null.");
                if (string.IsNullOrWhiteSpace(fileName)) return fb.SetMessage("fileName is required.");
                if (request.Scope?.Module == null || request.Scope.Module.Cuid == Guid.Empty)
                    return fb.SetMessage("Module CUID is mandatory.");
                if (request.Scope?.Workspace == null || request.Scope.Workspace.Cuid == Guid.Empty)
                    return fb.SetMessage("Workspace CUID is mandatory.");

                var ws = await EnsureWorkSpace(request);
                if (!ws.status) return fb.SetMessage("Workspace not found or not registered.");

                var moduleCuid = request.Scope.Module.Cuid.ToString("N");
                var dir = await EnsureDirectory(request, ws.id);
                if (!dir.status || dir.result.id < 1) return fb.SetMessage("Unable to resolve the target directory.");

                var nameStore = await EnsureNameStore(moduleCuid, fileName, readOnly: true);
                if (!nameStore.status || nameStore.id < 1)
                    return fb.SetMessage("Unable to resolve the file name in the name-store.");

                var row = await _agw.RowAsync(moduleCuid, INSTANCE.DOCUMENT.EXISTS_DELETED, default, (PARENT, dir.result.id), (NAME, nameStore.id));
                if (row == null || row.Count < 1) return fb.SetMessage("No deleted document exists with this name in the target folder.");

                var lifecycle = await GetDocumentLifecycleById(moduleCuid, row.GetLong("id"));
                if (lifecycle == null || !lifecycle.IsDeleted)
                    return fb.SetMessage("Deleted document not found.");

                return fb.SetStatus(true).SetResult(lifecycle);
            } catch (Exception ex) {
                _logger?.LogError(ex.Message + Environment.NewLine + ex.StackTrace);
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback> FinalizeDeletedDocumentArchive(string moduleCuid, long documentId, string tombstoneFileName) {
            var fb = new Feedback();
            try {
                if (string.IsNullOrWhiteSpace(moduleCuid) || documentId < 1 || string.IsNullOrWhiteSpace(tombstoneFileName))
                    return fb.SetMessage("moduleCuid, documentId, and tombstoneFileName are all required.");
                if (!_agw.ContainsKey(moduleCuid))
                    return fb.SetMessage($"No adapter found for key {moduleCuid}.");

                var lifecycle = await GetDocumentLifecycleById(moduleCuid, documentId);
                if (lifecycle == null || !lifecycle.IsDeleted)
                    return fb.SetMessage("Deleted document not found.");

                var nameStore = await EnsureNameStore(moduleCuid, tombstoneFileName, readOnly: false);
                if (!nameStore.status || nameStore.id < 1)
                    return fb.SetMessage("Unable to resolve a tombstone name-store id.");

                var originalNameId = lifecycle.OriginalNameStoreId ?? lifecycle.NameStoreId;
                var handler = _agw.GetTransactionHandler(moduleCuid);
                using (handler?.Begin()) {
                    var load = new DbExecutionLoad(default, handler);
                    await _agw.ExecAsync(moduleCuid, INSTANCE.DOCUMENT.ARCHIVE_RENAME, load, (ID, documentId), (NAME, nameStore.id), (ORIGINAL_NAME, originalNameId));
                    await _agw.ExecAsync(moduleCuid, INSTANCE.DOCVERSION.ARCHIVE_BY_PARENT, load, (PARENT, documentId));
                }
                return fb.SetStatus(true).SetMessage("Deleted document archive metadata updated.");
            } catch (Exception ex) {
                _logger?.LogError(ex.Message + Environment.NewLine + ex.StackTrace);
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback> FinalizeDeletedVersionArchive(string moduleCuid, long documentId, long versionId, int versionNo, int subVersionNo) {
            var fb = new Feedback();
            try {
                if (string.IsNullOrWhiteSpace(moduleCuid) || documentId < 1 || versionId < 1 || versionNo < 1)
                    return fb.SetMessage("moduleCuid, documentId, versionId, and versionNo are required.");
                if (!_agw.ContainsKey(moduleCuid))
                    return fb.SetMessage($"No adapter found for key {moduleCuid}.");

                var handler = _agw.GetTransactionHandler(moduleCuid);
                using (handler?.Begin()) {
                    var load = new DbExecutionLoad(default, handler);
                    if (subVersionNo == 0) {
                        await _agw.ExecAsync(moduleCuid, INSTANCE.DOCVERSION.ARCHIVE_BY_VERSION, load, (PARENT, documentId), (VERSION, versionNo));
                    } else {
                        await _agw.ExecAsync(moduleCuid, INSTANCE.DOCVERSION.ARCHIVE_BY_ID, load, (ID, versionId));
                    }
                }

                return fb.SetStatus(true).SetMessage("Deleted version archive metadata updated.");
            } catch (Exception ex) {
                _logger?.LogError(ex.Message + Environment.NewLine + ex.StackTrace);
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback> RestoreDeletedDocument(string moduleCuid, long documentId) {
            var fb = new Feedback();
            try {
                if (string.IsNullOrWhiteSpace(moduleCuid) || documentId < 1)
                    return fb.SetMessage("moduleCuid and documentId are required.");
                if (!_agw.ContainsKey(moduleCuid))
                    return fb.SetMessage($"No adapter found for key {moduleCuid}.");

                var lifecycle = await GetDocumentLifecycleById(moduleCuid, documentId);
                if (lifecycle == null || !lifecycle.IsDeleted)
                    return fb.SetMessage("Deleted document not found.");

                var parentDirectory = await _agw.RowAsync(moduleCuid, INSTANCE.DIRECTORY.EXISTS_BY_ID, default, (VALUE, lifecycle.DirectoryId));
                if (parentDirectory == null || parentDirectory.Count < 1)
                    return fb.SetMessage("The original folder is deleted. Restore the folder first.");

                var restoreNameId = lifecycle.OriginalNameStoreId ?? lifecycle.NameStoreId;
                var activeConflict = await _agw.RowAsync(moduleCuid, INSTANCE.DOCUMENT.EXISTS, default, (PARENT, lifecycle.DirectoryId), (NAME, restoreNameId));
                if (activeConflict != null && activeConflict.Count > 0)
                    return fb.SetMessage("An active document with the same name already exists in the original folder.");

                var handler = _agw.GetTransactionHandler(moduleCuid);
                using (handler?.Begin()) {
                    var load = new DbExecutionLoad(default, handler);
                    await _agw.ExecAsync(moduleCuid, INSTANCE.DOCUMENT.RESTORE_NAME, load, (ID, documentId));
                    await _agw.ExecAsync(moduleCuid, INSTANCE.DOCUMENT.RESTORE_BY_ID, load, (ID, documentId));
                    await _agw.ExecAsync(moduleCuid, INSTANCE.DOCVERSION.RESTORE_BY_PARENT, load, (PARENT, documentId));
                }

                return fb.SetStatus(true).SetMessage("Document restored.");
            } catch (Exception ex) {
                _logger?.LogError(ex.Message + Environment.NewLine + ex.StackTrace);
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback> RestoreDeletedVersion(string moduleCuid, long documentId, long versionId, int versionNo, int subVersionNo) {
            var fb = new Feedback();
            try {
                if (string.IsNullOrWhiteSpace(moduleCuid) || documentId < 1 || versionId < 1 || versionNo < 1)
                    return fb.SetMessage("moduleCuid, documentId, versionId, and versionNo are required.");
                if (!_agw.ContainsKey(moduleCuid))
                    return fb.SetMessage($"No adapter found for key {moduleCuid}.");

                var lifecycle = await GetDocumentLifecycleById(moduleCuid, documentId);
                if (lifecycle == null)
                    return fb.SetMessage("Target document not found.");
                if (lifecycle.IsDeleted)
                    return fb.SetMessage("The logical document is deleted. Restore the document instead.");

                var handler = _agw.GetTransactionHandler(moduleCuid);
                using (handler?.Begin()) {
                    var load = new DbExecutionLoad(default, handler);
                    if (subVersionNo == 0) {
                        await _agw.ExecAsync(moduleCuid, INSTANCE.DOCVERSION.RESTORE_BY_VERSION, load, (PARENT, documentId), (VERSION, versionNo));
                    } else {
                        await _agw.ExecAsync(moduleCuid, INSTANCE.DOCVERSION.RESTORE_BY_ID, load, (ID, versionId));
                    }
                }

                return fb.SetStatus(true).SetMessage(subVersionNo == 0
                    ? "Version restored."
                    : "Thumbnail version restored.");
            } catch (Exception ex) {
                _logger?.LogError(ex.Message + Environment.NewLine + ex.StackTrace);
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback> SoftDeleteDirectory(IVaultReadRequest request, bool recursive) {
            var fb = new Feedback();
            try {
                if (request == null) return fb.SetMessage("Request cannot be null.");
                if (request.ReadOnlyMode) return fb.SetMessage("Cannot delete a directory in read-only mode.");
                if (request.Scope?.Module == null || request.Scope.Module.Cuid == Guid.Empty)
                    return fb.SetMessage("Module CUID is mandatory.");
                if (request.Scope?.Workspace == null || request.Scope.Workspace.Cuid == Guid.Empty)
                    return fb.SetMessage("Workspace CUID is mandatory.");

                var moduleCuid = request.Scope.Module.Cuid.ToString("N");
                var wsId = await ResolveWorkspaceId(request.Scope.Workspace.Cuid.ToString("N"));
                if (wsId < 1) return fb.SetMessage("Workspace is not registered in the core index.");

                var folderInfo = await ResolveFolderInfo(moduleCuid, request, wsId, includeAll: false);
                if (!folderInfo.status || folderInfo.isRoot || folderInfo.id < 1)
                    return fb.SetMessage("A non-root active folder is required.");

                var dirIds = new List<long>();
                var docIds = new List<long>();
                var queue = new Queue<long>();
                queue.Enqueue(folderInfo.id);

                while (queue.Count > 0) {
                    var currentId = queue.Dequeue();
                    dirIds.Add(currentId);

                    var childDocs = await _agw.RowsAsync(moduleCuid, INSTANCE.DOCUMENT.GET_IDS_BY_PARENT_ALL, default, (PARENT, currentId));
                    docIds.AddRange(childDocs.Select(r => r.GetLong("id")).Where(id => id > 0));

                    var childDirs = await _agw.RowsAsync(moduleCuid, INSTANCE.DIRECTORY.GET_CHILD_IDS_ALL, default, (PARENT, currentId));
                    foreach (var row in childDirs) {
                        var childId = row.GetLong("id");
                        if (childId > 0) queue.Enqueue(childId);
                    }
                }

                if (!recursive && (dirIds.Count > 1 || docIds.Count > 0))
                    return fb.SetMessage("Folder is not empty. Recursive delete is required.");

                var handler = _agw.GetTransactionHandler(moduleCuid);
                using (handler?.Begin()) {
                    var load = new DbExecutionLoad(default, handler);
                    var deletedAt = DateTime.UtcNow;

                    foreach (var documentId in docIds.Distinct()) {
                        await _agw.ExecAsync(moduleCuid, INSTANCE.DOCUMENT.SOFT_DELETE_BY_ID, load, (ID, documentId), (DELETED, deletedAt));
                        await _agw.ExecAsync(moduleCuid, INSTANCE.DOCVERSION.SOFT_DELETE_BY_PARENT, load, (PARENT, documentId), (DELETED, deletedAt));
                    }

                    foreach (var directoryId in dirIds.Distinct()) {
                        await _agw.ExecAsync(moduleCuid, INSTANCE.DIRECTORY.SOFT_DELETE_BY_ID, load, (ID, directoryId), (DELETED, deletedAt));
                    }
                }

                return fb.SetStatus(true).SetMessage("Directory deleted.");
            } catch (Exception ex) {
                _logger?.LogError(ex.Message + Environment.NewLine + ex.StackTrace);
                return fb.SetMessage(ex.Message);
            }
        }

        async Task<long> ResolveDocumentIdForLifecycle(string moduleCuid, IVaultFileReadRequest request, bool includeDeleted) {
            if (request?.File?.Id > 0)
                return await _agw.ScalarAsync<long?>(moduleCuid, INSTANCE.DOCVERSION.GET_DOCUMENT_ID_BY_VERSION_ID, default, (VALUE, request.File.Id)) ?? 0;

            if (!string.IsNullOrWhiteSpace(request?.File?.Cuid))
                return await _agw.ScalarAsync<long?>(moduleCuid, INSTANCE.DOCVERSION.GET_DOCUMENT_ID_BY_VERSION_CUID, default, (VALUE, ToDbCuid(request.File.Cuid))) ?? 0;

            var rootCuid = (request?.File as StorageFileRoute)?.RootCuid;
            if (!string.IsNullOrWhiteSpace(rootCuid))
                return await _agw.ScalarAsync<long?>(moduleCuid, includeDeleted ? INSTANCE.DOCUMENT.EXISTS_BY_CUID_ALL : INSTANCE.DOCUMENT.GET_BY_CUID, default, (CUID, ToDbCuid(rootCuid))) ?? 0;

            return 0;
        }

        async Task<DeletedDocumentInfo?> GetDocumentLifecycleById(string moduleCuid, long documentId) {
            if (documentId < 1) return null;

            var docRow = await _agw.RowAsync(moduleCuid, INSTANCE.DOCUMENT.GET_LIFECYCLE_BY_ID, default, (ID, documentId));
            if (docRow == null || docRow.Count < 1) return null;

            var versionRows = await _agw.RowsAsync(moduleCuid, INSTANCE.DOCVERSION.GET_ALL_BY_PARENT_ALL, default, (PARENT, documentId));

            var result = new DeletedDocumentInfo {
                DocumentId = docRow.GetLong("document_id"),
                DocumentCuid = docRow.GetString("document_cuid") ?? string.Empty,
                WorkspaceId = docRow.GetLong("workspace_id"),
                DirectoryId = docRow.GetLong("directory_id"),
                NameStoreId = docRow.GetLong("current_name_id"),
                OriginalNameStoreId = docRow.GetNullableLong("original_name_id"),
                CurrentFileName = docRow.GetString("current_file_name") ?? string.Empty,
                RestoreFileName = docRow.GetString("restore_file_name") ?? string.Empty,
                DeleteState = docRow.GetInt("delete_state"),
                IsDeleted = docRow.GetInt("delete_state") > 0,
                Deleted = docRow.GetDateTime("deleted")
            };

            foreach (var row in versionRows) {
                var deleteState = row.GetInt("delete_state");
                result.Versions.Add(new DeletedDocumentVersionInfo {
                    VersionId = row.GetLong("version_id"),
                    VersionCuid = row.GetString("version_cuid") ?? string.Empty,
                    VersionNumber = row.GetInt("version_no"),
                    SubVersionNumber = row.GetInt("sub_version_no"),
                    DeleteState = deleteState,
                    IsDeleted = deleteState > 0,
                    Deleted = row.GetDateTime("deleted"),
                    StorageName = row.GetString("storage_name") ?? string.Empty,
                    StorageRef = row.GetString("storage_ref") ?? string.Empty,
                    StagingRef = row.GetString("staging_ref") ?? string.Empty,
                    Flags = row.GetInt("flags"),
                    ProfileInfoId = row.GetNullableLong("profile_info_id")
                });
            }

            return result;
        }

        static bool SameCuid(string left, string right) {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
            if (Guid.TryParse(left, out var lg) && Guid.TryParse(right, out var rg))
                return lg == rg;
            return left.Equals(right, StringComparison.OrdinalIgnoreCase);
        }
    }
}
