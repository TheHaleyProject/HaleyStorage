using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using static Haley.Internal.IndexingConstant;
using static Haley.Internal.IndexingQueries;

namespace Haley.Utils {
    /// <summary>
    /// Partial class — workspace-scoped search for folders and files (latest version only).
    /// Searches vault names (filename stems) using LIKE patterns; extension is a separate filter.
    /// Supports three scope modes: entire workspace, single directory, recursive subtree.
    /// </summary>
    internal partial class MariaDBIndexing {

        public async Task<IFeedback<VaultFolderBrowseResponse>> SearchItems(IVaultReadRequest request, string searchTerm, VaultSearchMode searchMode, string extension = null, bool recursive = false, int page = 1, int pageSize = 50, bool includeAll = false) {

            var fb = new Feedback<VaultFolderBrowseResponse>();
            try {
                if (string.IsNullOrWhiteSpace(searchTerm))
                    return fb.SetMessage("Search term cannot be empty.");
                if (request?.Scope?.Module == null || request.Scope.Module.Cuid == Guid.Empty)
                    return fb.SetMessage("Module CUID is mandatory for search.");
                if (request.Scope?.Workspace == null || request.Scope.Workspace.Cuid == Guid.Empty)
                    return fb.SetMessage("Workspace CUID is mandatory for search.");

                if (page < 1) page = 1;
                if (pageSize < 1) pageSize = 50;
                if (pageSize > 200) pageSize = 200;

                var moduleCuid = request.Scope.Module.Cuid.ToString("N");
                if (!_agw.ContainsKey(moduleCuid))
                    return fb.SetMessage($"No adapter found for module {moduleCuid}.");

                var wsId = await ResolveWorkspaceId(request.Scope.Workspace.Cuid.ToString("N"));
                if (wsId < 1) return fb.SetMessage("Workspace is not registered in the core index.");

                var likePattern = BuildSearchPattern(searchTerm.Trim().ToLowerInvariant(), searchMode);
                // Pass DBNull.Value when no extension filter — lets (@EXT is null or ...) short-circuit.
                object extParam = string.IsNullOrWhiteSpace(extension)? (object)DBNull.Value : extension.TrimStart('.').ToLowerInvariant();

                var offset = (page - 1) * pageSize;
                long totalDirs, totalFiles;
                IEnumerable<DbRow> rows;

                var directoryId = request.Scope.Folder?.Id ?? -1;   // -1 indicates root scope (entire workspace)
                if (directoryId < 1) {
                    // Scope: entire workspace.
                    totalDirs  = await _agw.ScalarAsync<long?>(moduleCuid, includeAll ? INSTANCE.SEARCH.COUNT_DIRS_ALL_INCLUDE_DELETED : INSTANCE.SEARCH.COUNT_DIRS_ALL,  default, (WSPACE, wsId), (VALUE, likePattern)) ?? 0;
                    totalFiles = await _agw.ScalarAsync<long?>(moduleCuid, includeAll ? INSTANCE.SEARCH.COUNT_FILES_ALL_INCLUDE_DELETED : INSTANCE.SEARCH.COUNT_FILES_ALL, default, (WSPACE, wsId), (VALUE, likePattern), (EXT, extParam)) ?? 0;
                    rows       = await _agw.RowsAsync(moduleCuid, includeAll ? INSTANCE.SEARCH.ITEMS_ALL_INCLUDE_DELETED : INSTANCE.SEARCH.ITEMS_ALL, default, (WSPACE, wsId), (VALUE, likePattern), (EXT, extParam), (LIMIT_ROWS, pageSize), (OFFSET_ROWS, offset));
                } else if (!recursive) {
                    // Scope: direct children of a specific directory.
                    totalDirs  = await _agw.ScalarAsync<long?>(moduleCuid, includeAll ? INSTANCE.SEARCH.COUNT_DIRS_IN_DIR_INCLUDE_DELETED : INSTANCE.SEARCH.COUNT_DIRS_IN_DIR,  default, (WSPACE, wsId), (PARENT, directoryId), (VALUE, likePattern)) ?? 0;
                    totalFiles = await _agw.ScalarAsync<long?>(moduleCuid, includeAll ? INSTANCE.SEARCH.COUNT_FILES_IN_DIR_INCLUDE_DELETED : INSTANCE.SEARCH.COUNT_FILES_IN_DIR, default, (WSPACE, wsId), (PARENT, directoryId), (VALUE, likePattern), (EXT, extParam)) ?? 0;
                    rows       = await _agw.RowsAsync(moduleCuid, includeAll ? INSTANCE.SEARCH.ITEMS_IN_DIR_INCLUDE_DELETED : INSTANCE.SEARCH.ITEMS_IN_DIR, default, (WSPACE, wsId), (PARENT, directoryId), (VALUE, likePattern), (EXT, extParam), (LIMIT_ROWS, pageSize), (OFFSET_ROWS, offset));
                } else {
                    // Scope: recursive subtree of a directory (WITH RECURSIVE CTE).
                    totalDirs  = await _agw.ScalarAsync<long?>(moduleCuid, includeAll ? INSTANCE.SEARCH.COUNT_DIRS_RECURSIVE_INCLUDE_DELETED : INSTANCE.SEARCH.COUNT_DIRS_RECURSIVE,  default, (WSPACE, wsId), (PARENT, directoryId), (VALUE, likePattern)) ?? 0;
                    totalFiles = await _agw.ScalarAsync<long?>(moduleCuid, includeAll ? INSTANCE.SEARCH.COUNT_FILES_RECURSIVE_INCLUDE_DELETED : INSTANCE.SEARCH.COUNT_FILES_RECURSIVE, default, (WSPACE, wsId), (PARENT, directoryId), (VALUE, likePattern), (EXT, extParam)) ?? 0;
                    rows       = await _agw.RowsAsync(moduleCuid, includeAll ? INSTANCE.SEARCH.ITEMS_RECURSIVE_INCLUDE_DELETED : INSTANCE.SEARCH.ITEMS_RECURSIVE, default, (WSPACE, wsId), (PARENT, directoryId), (VALUE, likePattern), (EXT, extParam), (LIMIT_ROWS, pageSize), (OFFSET_ROWS, offset));
                }

                var response = new VaultFolderBrowseResponse { WorkspaceId    = wsId, WorkspaceCuid  = request.Scope.Workspace.Cuid.ToString("N"), IsRoot = directoryId < 1, CurrentFolderId = directoryId, IncludeAll = includeAll, Page = page, PageSize = pageSize, TotalFolders = totalDirs, TotalFiles = totalFiles, TotalItems = totalDirs + totalFiles };

                foreach (var row in rows)
                    response.Items.Add(MapBrowseItem(row));

                return fb.SetStatus(true).SetResult(response);
            } catch (Exception ex) {
                _logger?.LogError(ex.Message + Environment.NewLine + ex.StackTrace);
                return fb.SetMessage(ex.Message);
            }
        }

        /// <summary>Builds a MariaDB LIKE pattern from a pre-normalized (trimmed + lowercased) term.</summary>
        static string BuildSearchPattern(string normalizedTerm, VaultSearchMode mode) => mode switch {
            VaultSearchMode.StartsWith => $"{normalizedTerm}%",
            VaultSearchMode.EndsWith   => $"%{normalizedTerm}",
            VaultSearchMode.Contains   => $"%{normalizedTerm}%",
            _                          => normalizedTerm,   // Equals — exact match, no wildcards
        };
    }
}
