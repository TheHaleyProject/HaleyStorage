using Haley.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Haley.Internal.IndexingConstant;
using static Haley.Internal.IndexingQueries;

namespace Haley.Utils {
    internal partial class MariaDBIndexing {
        async Task<string> ResolveDirectoryPath(string moduleCuid, long directoryId, bool includeAll, Dictionary<long, string> cache) {
            if (directoryId < 1) return string.Empty;
            if (cache.TryGetValue(directoryId, out var cached)) return cached;

            var row = await _agw.RowAsync(moduleCuid, includeAll ? INSTANCE.DIRECTORY.GET_DETAILS_BY_ID_ALL : INSTANCE.DIRECTORY.GET_DETAILS_BY_ID, default, (VALUE, directoryId));
            if (row == null) {
                cache[directoryId] = string.Empty;
                return string.Empty;
            }

            var parentPath = await ResolveDirectoryPath(moduleCuid, row.GetLong("parent"), includeAll, cache);
            var name = row.GetString("display_name") ?? row.GetString("name") ?? row.GetString("uid") ?? string.Empty;
            var path = JoinVirtualPath(parentPath, name);
            cache[directoryId] = path;
            return path;
        }

        async Task ApplyBrowsePaths(string moduleCuid, VaultFolderBrowseResponse response, long currentFolderId, bool includeAll) {
            var cache = new Dictionary<long, string>();
            response.CurrentFolderPath = await ResolveDirectoryPath(moduleCuid, currentFolderId, includeAll, cache);

            foreach (var item in response.Items) {
                if (IsFolderItem(item)) {
                    var parentPath = await ResolveDirectoryPath(moduleCuid, item.ParentId, includeAll, cache);
                    item.VirtualPath = JoinVirtualPath(parentPath, item.DisplayName);
                } else {
                    item.VirtualPath = await ResolveDirectoryPath(moduleCuid, item.ParentId, includeAll, cache);
                }
            }
        }

        async Task ApplySearchPaths(string moduleCuid, VaultFolderBrowseResponse response, bool includeAll) {
            var cache = new Dictionary<long, string>();
            response.CurrentFolderPath = await ResolveDirectoryPath(moduleCuid, response.CurrentFolderId, includeAll, cache);

            foreach (var item in response.Items) {
                var pathDirectoryId = IsFolderItem(item) ? item.Id : item.ParentId;
                item.VirtualPath = await ResolveDirectoryPath(moduleCuid, pathDirectoryId, includeAll, cache);
            }
        }

        static bool IsFolderItem(VaultBrowseItem item)
            => string.Equals(item.ItemType, "folder", System.StringComparison.OrdinalIgnoreCase);

        static string JoinVirtualPath(string left, string right) {
            left = (left ?? string.Empty).Trim().Trim('/', '\\');
            right = (right ?? string.Empty).Trim().Trim('/', '\\');
            if (string.IsNullOrWhiteSpace(left)) return right;
            if (string.IsNullOrWhiteSpace(right)) return left;
            return $"{left}/{right}";
        }
    }
}
