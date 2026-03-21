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
    /// Partial class — staging promotion queries.
    /// Exposes <see cref="GetPendingStagedVersions"/> and <see cref="UpdateVersionPromotion"/>
    /// used by <c>StagingPromotionWorker</c> to move staged files to primary storage.
    /// </summary>
    internal partial class MariaDBIndexing {

        /// <inheritdoc/>
        public async Task<IEnumerable<StagedVersionRef>> GetPendingStagedVersions(string moduleCuid, int batchSize = 20) {
            try {
                if (string.IsNullOrWhiteSpace(moduleCuid)) return Enumerable.Empty<StagedVersionRef>();
                if (!_agw.ContainsKey(moduleCuid)) return Enumerable.Empty<StagedVersionRef>();
                if (batchSize < 1) batchSize = 20;
                if (batchSize > 200) batchSize = 200;

                var rows = await _agw.RowsAsync(moduleCuid, INSTANCE.STAGING.GET_PENDING, default, (LIMIT_ROWS, batchSize));
                var result = new List<StagedVersionRef>();
                foreach (var row in rows) {
                    result.Add(new StagedVersionRef {
                        VersionId     = row.TryGetValue("version_id",    out var vid)  && long.TryParse(vid?.ToString(),  out var vl)  ? vl  : 0L,
                        StorageName   = row.TryGetValue("storage_name",  out var sn)   ? sn?.ToString()  : null,
                        StorageRef    = row.TryGetValue("storage_ref",   out var sr)   ? sr?.ToString()  : null,
                        StagingRef    = row.TryGetValue("staging_ref",   out var stgr) ? stgr?.ToString() : null,
                        ProfileInfoId = row.TryGetValue("profile_info_id", out var pid) && long.TryParse(pid?.ToString(), out var pl)  ? pl  : 0L,
                        WorkspaceCuid = row.TryGetValue("workspace_cuid", out var wc)  ? wc?.ToString()  : null,
                        ModuleCuid    = moduleCuid,
                    });
                }
                return result;
            } catch (Exception ex) {
                _logger?.LogError(ex.Message + Environment.NewLine + ex.StackTrace);
                return Enumerable.Empty<StagedVersionRef>();
            }
        }

        /// <inheritdoc/>
        public async Task<IFeedback> UpdateVersionPromotion(string moduleCuid, long versionId, string storageRef, int newFlags, DateTime syncedAt, long size = 0, string hash = null) {
            var fb = new Feedback();
            try {
                if (string.IsNullOrWhiteSpace(moduleCuid)) return fb.SetMessage("moduleCuid is required.");
                if (versionId < 1)                          return fb.SetMessage("versionId must be > 0.");
                if (!_agw.ContainsKey(moduleCuid))          return fb.SetMessage($"No adapter found for module {moduleCuid}.");

                await _agw.ExecAsync(moduleCuid, INSTANCE.STAGING.UPDATE_PROMOTION, default,
                    (ID,        versionId),
                    (PATH,      storageRef),
                    (FLAGS,     newFlags),
                    (SYNCED_AT, syncedAt),
                    (SIZE,      size),
                    (HASH,      (object?)hash ?? DBNull.Value));

                return fb.SetStatus(true);
            } catch (Exception ex) {
                _logger?.LogError(ex.Message + Environment.NewLine + ex.StackTrace);
                return fb.SetMessage(ex.Message);
            }
        }
    }
}
