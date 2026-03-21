using Haley.Abstractions;
using Haley.Models;
using Haley.Utils;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using static Haley.Internal.IndexingConstant;
using static Haley.Internal.IndexingQueries;

namespace Haley.Utils {
    /// <summary>
    /// Partial class — DB-backed browse/explore APIs for folders and file history.
    /// </summary>
    public partial class MariaDBIndexing : IVaultIndexing {
        public async Task<IFeedback<VaultFolderBrowseResponse>> BrowseFolder(IVaultReadRequest request, int page = 1, int pageSize = 50) {
            var fb = new Feedback<VaultFolderBrowseResponse>();
            try {
                if (request == null) return fb.SetMessage("Input request cannot be empty.");
                if (request.Scope?.Module == null || request.Scope.Module.Cuid == Guid.Empty)
                    return fb.SetMessage("Module CUID is mandatory to browse a folder.");
                if (request.Scope?.Workspace == null || request.Scope.Workspace.Cuid == Guid.Empty)
                    return fb.SetMessage("Workspace CUID is mandatory to browse a folder.");

                if (page < 1) page = 1;
                if (pageSize < 1) pageSize = 50;
                if (pageSize > 200) pageSize = 200;

                var moduleCuid = request.Scope.Module.Cuid.ToString("N");
                if (!_agw.ContainsKey(moduleCuid)) return fb.SetMessage($@"No adapter found for the key {moduleCuid}");

                var wsId = await ResolveWorkspaceId(request.Scope.Workspace.Cuid.ToString("N"));
                if (wsId < 1) return fb.SetMessage("Workspace is not registered in the core index.");

                var folderInfo = await ResolveFolderInfo(moduleCuid, request, wsId);
                if (!folderInfo.status) return fb.SetMessage(folderInfo.message);

                var totalFolders = await _agw.ScalarAsync<long?>(moduleCuid, INSTANCE.DIRECTORY.COUNT_CHILDREN, default, (WSPACE, wsId), (PARENT, folderInfo.id)) ?? 0;
                var totalFiles = await _agw.ScalarAsync<long?>(moduleCuid, INSTANCE.DOCUMENT.COUNT_BY_DIRECTORY, default, (WSPACE, wsId), (PARENT, folderInfo.id)) ?? 0;
                var offset = (page - 1) * pageSize;

                var rows = await _agw.RowsAsync(moduleCuid, INSTANCE.DIRECTORY.BROWSE_ITEMS, default,
                    (WSPACE, wsId),
                    (PARENT, folderInfo.id),
                    (LIMIT_ROWS, pageSize),
                    (OFFSET_ROWS, offset));

                var response = new VaultFolderBrowseResponse {
                    WorkspaceId = wsId,
                    WorkspaceCuid = request.Scope.Workspace.Cuid.ToString("N"),
                    IsRoot = folderInfo.isRoot,
                    CurrentFolderId = folderInfo.id,
                    CurrentFolderCuid = folderInfo.cuid,
                    CurrentFolderName = folderInfo.displayName,
                    CurrentFolderParentId = folderInfo.parentId,
                    Page = page,
                    PageSize = pageSize,
                    TotalFolders = totalFolders,
                    TotalFiles = totalFiles,
                    TotalItems = totalFolders + totalFiles
                };

                foreach (var row in rows) {
                    response.Items.Add(MapBrowseItem(row));
                }

                return fb.SetStatus(true).SetResult(response);
            } catch (Exception ex) {
                _logger?.LogError(ex.Message + Environment.NewLine + ex.StackTrace);
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<VaultFileDetailsResponse>> GetFileDetails(IVaultFileReadRequest request) {
            var fb = new Feedback<VaultFileDetailsResponse>();
            try {
                if (request == null) return fb.SetMessage("Input request cannot be empty.");
                if (request.Scope?.Module == null || request.Scope.Module.Cuid == Guid.Empty)
                    return fb.SetMessage("Module CUID is mandatory to fetch file details.");

                var moduleCuid = request.Scope.Module.Cuid.ToString("N");
                if (!_agw.ContainsKey(moduleCuid)) return fb.SetMessage($@"No adapter found for the key {moduleCuid}");

                var documentId = await ResolveDocumentId(moduleCuid, request);
                if (documentId < 1) return fb.SetMessage("Unable to resolve the target file.");

                var docRow = await _agw.RowAsync(moduleCuid, INSTANCE.DOCUMENT.GET_DETAILS_BY_ID, default, (ID, documentId));
                if (docRow == null) return fb.SetMessage("Unable to fetch document details.");

                var versionRows = await _agw.RowsAsync(moduleCuid, INSTANCE.DOCVERSION.GET_ALL_BY_PARENT, default, (PARENT, documentId));

                var response = new VaultFileDetailsResponse {
                    DocumentId = ToLong(docRow, "document_id"),
                    DocumentCuid = ToString(docRow, "document_cuid"),
                    DisplayName = ToString(docRow, "display_name"),
                    WorkspaceId = ToLong(docRow, "workspace_id"),
                    WorkspaceCuid = request.Scope?.Workspace?.Cuid.ToString("N") ?? string.Empty,
                    DirectoryId = ToLong(docRow, "directory_id"),
                    DirectoryCuid = ToString(docRow, "directory_cuid"),
                    DirectoryName = ToString(docRow, "directory_name"),
                    DirectoryParentId = ToLong(docRow, "directory_parent_id"),
                    VersionCount = versionRows.Count
                };

                foreach (var row in versionRows) {
                    response.Versions.Add(new VaultFileVersionInfo {
                        VersionId = ToLong(row, "version_id"),
                        VersionCuid = ToString(row, "version_cuid"),
                        VersionNumber = ToInt(row, "version_no"),
                        Created = ToDateTime(row, "version_created"),
                        Size = ToNullableLong(row, "size"),
                        StorageName = ToString(row, "storage_name"),
                        StorageRef = ToString(row, "storage_ref"),
                        StagingRef = ToString(row, "staging_ref"),
                        Flags = ToInt(row, "flags"),
                        Hash = ToString(row, "hash"),
                        SyncedAt = ToDateTime(row, "synced_at"),
                        Metadata = ToString(row, "metadata")
                    });
                }

                return fb.SetStatus(true).SetResult(response);
            } catch (Exception ex) {
                _logger?.LogError(ex.Message + Environment.NewLine + ex.StackTrace);
                return fb.SetMessage(ex.Message);
            }
        }

        async Task<long> ResolveWorkspaceId(string workspaceCuid) {
            if (string.IsNullOrWhiteSpace(workspaceCuid)) return 0;
            var wsInfo = await _agw.Scalar(new AdapterArgs(_key) { Query = WORKSPACE.EXISTS_BY_CUID }, (CUID, workspaceCuid));
            return wsInfo != null && long.TryParse(wsInfo.ToString(), out var wsId) ? wsId : 0;
        }

        async Task<(bool status, string message, bool isRoot, long id, string cuid, string displayName, long parentId)> ResolveFolderInfo(string moduleCuid, IVaultReadRequest request, long workspaceId) {
            var folder = request.Scope?.Folder;
            if (folder == null || (folder.Id < 1 && string.IsNullOrWhiteSpace(folder.Cuid) && string.IsNullOrWhiteSpace(folder.DisplayName))) {
                return (true, string.Empty, true, 0, string.Empty, string.Empty, 0);
            }

            DbRow row = null;
            if (folder.Id > 0) {
                row = await _agw.RowAsync(moduleCuid, INSTANCE.DIRECTORY.GET_DETAILS_BY_ID, default, (VALUE, folder.Id));
            } else if (!string.IsNullOrWhiteSpace(folder.Cuid)) {
                row = await _agw.RowAsync(moduleCuid, INSTANCE.DIRECTORY.GET_DETAILS_BY_CUID, default, (VALUE, folder.Cuid));
            } else {
                var parentId = folder.Parent?.Id ?? 0;
                row = await _agw.RowAsync(moduleCuid, INSTANCE.DIRECTORY.GET_DETAILS, default,
                    (WSPACE, workspaceId),
                    (PARENT, parentId),
                    (NAME, folder.DisplayName.ToDBName()));
            }

            if (row == null) return (false, "Folder not found.", false, 0, string.Empty, string.Empty, 0);
            if (ToLong(row, "workspace") != workspaceId) return (false, "Folder does not belong to the requested workspace.", false, 0, string.Empty, string.Empty, 0);

            return (true, string.Empty, false,
                ToLong(row, "id"),
                ToString(row, "uid"),
                ToString(row, "display_name"),
                ToLong(row, "parent"));
        }

        async Task<long> ResolveDocumentId(string moduleCuid, IVaultFileReadRequest request) {
            if (request?.File?.Id > 0) {
                return await _agw.ScalarAsync<long?>(moduleCuid, INSTANCE.DOCVERSION.GET_DOCUMENT_ID_BY_VERSION_ID, default, (VALUE, request.File.Id)) ?? 0;
            }

            if (!string.IsNullOrWhiteSpace(request?.File?.Cuid)) {
                return await _agw.ScalarAsync<long?>(moduleCuid, INSTANCE.DOCVERSION.GET_DOCUMENT_ID_BY_VERSION_CUID, default, (VALUE, request.File.Cuid)) ?? 0;
            }

            if (request?.Scope?.Workspace == null || request.Scope.Workspace.Cuid == Guid.Empty) return 0;

            var wsId = await ResolveWorkspaceId(request.Scope.Workspace.Cuid.ToString("N"));
            if (wsId < 1) return 0;

            var folderInfo = await ResolveFolderInfo(moduleCuid, request, wsId);
            if (!folderInfo.status) return 0;

            var fileName = request.File?.DisplayName ?? request.RequestedName;
            if (string.IsNullOrWhiteSpace(fileName)) return 0;

            var dirName = folderInfo.isRoot ? VaultConstants.DEFAULT_NAME : folderInfo.displayName;
            var dirParentId = folderInfo.isRoot ? 0 : folderInfo.parentId;
            var name = System.IO.Path.GetFileNameWithoutExtension(fileName).ToDBName();
            var extension = System.IO.Path.GetExtension(fileName)?.ToDBName() ?? VaultConstants.DEFAULT_NAME;

            return await _agw.ScalarAsync<long?>(moduleCuid, INSTANCE.DOCUMENT.GET_BY_NAME, default,
                (NAME, name),
                (EXT, extension),
                (WSPACE, wsId),
                (PARENT, dirParentId),
                (DIRNAME, dirName.ToDBName())) ?? 0;
        }

        static VaultBrowseItem MapBrowseItem(DbRow row) {
            return new VaultBrowseItem {
                ItemType = ToString(row, "item_type"),
                Id = ToLong(row, "id"),
                Cuid = ToString(row, "uid"),
                DisplayName = ToString(row, "display_name"),
                ParentId = ToLong(row, "parent_id"),
                Created = ToDateTime(row, "created"),
                Modified = ToDateTime(row, "modified"),
                LatestVersionId = ToNullableLong(row, "version_id"),
                LatestVersionCuid = ToString(row, "version_cuid"),
                LatestVersionNumber = ToNullableInt(row, "version_no"),
                VersionCount = ToNullableInt(row, "version_count"),
                LatestVersionCreated = ToDateTime(row, "version_created"),
                Size = ToNullableLong(row, "size"),
                StorageName = ToString(row, "storage_name"),
                StorageRef = ToString(row, "storage_ref"),
                StagingRef = ToString(row, "staging_ref"),
                Flags = ToNullableInt(row, "flags"),
                Hash = ToString(row, "hash"),
                SyncedAt = ToDateTime(row, "synced_at")
            };
        }

        static bool IsDbNull(object value) {
            return value == null || value == DBNull.Value;
        }

        static string ToString(DbRow row, string key) {
            return row.TryGetValue(key, out var value) && !IsDbNull(value) ? value?.ToString() ?? string.Empty : string.Empty;
        }

        static long ToLong(DbRow row, string key) {
            if (!row.TryGetValue(key, out var value) || IsDbNull(value)) return 0;
            return long.TryParse(value?.ToString(), out var result) ? result : 0;
        }

        static long? ToNullableLong(DbRow row, string key) {
            if (!row.TryGetValue(key, out var value) || IsDbNull(value)) return null;
            return long.TryParse(value?.ToString(), out var result) ? result : null;
        }

        static int ToInt(DbRow row, string key) {
            if (!row.TryGetValue(key, out var value) || IsDbNull(value)) return 0;
            return int.TryParse(value?.ToString(), out var result) ? result : 0;
        }

        static int? ToNullableInt(DbRow row, string key) {
            if (!row.TryGetValue(key, out var value) || IsDbNull(value)) return null;
            return int.TryParse(value?.ToString(), out var result) ? result : null;
        }

        static DateTime? ToDateTime(DbRow row, string key) {
            if (!row.TryGetValue(key, out var value) || IsDbNull(value)) return null;
            if (value is DateTime dateTime) return dateTime;
            return DateTime.TryParse(value?.ToString(), out var parsed) ? parsed : null;
        }
    }
}
