using static Haley.Internal.IndexingConstant;

namespace Haley.Internal {
    internal partial class IndexingQueries {
        public partial class INSTANCE {
            public class STAGING {
                /// <summary>
                /// Selects the next batch of staged-but-not-yet-promoted rows.
                /// Filter: flags bit 4 (InStaging) set, bit 8 (InStorage) not set, synced_at NULL.
                /// Includes the workspace CUID (for profile resolution) and module CUID
                /// (for selecting the correct per-module DB at the coordinator level — added via JOIN in implementation).
                /// </summary>
                public const string GET_PENDING =
                    $@"select vi.id as version_id, vi.storage_name, vi.storage_ref,
                              vi.staging_ref, vi.profile_info_id, ws.cuid as workspace_cuid
                       from version_info as vi
                       inner join doc_version as dv  on dv.id = vi.id
                       inner join document    as d   on d.id = dv.parent  and d.delete_state = 0
                       inner join directory   as dir on dir.id = d.parent and dir.delete_state = 0
                       inner join workspace   as ws  on ws.id = dir.workspace
                       where (vi.flags & 4) > 0
                         and dv.delete_state = 0
                         and (vi.flags & 8) = 0
                         and vi.synced_at is null
                       order by vi.id asc
                       limit {LIMIT_ROWS};";

                /// <summary>
                /// Atomically marks a promoted row: sets storage_ref, clears/sets flags, and stamps synced_at.
                /// The caller computes <c>newFlags</c> based on <c>StorageProfileMode</c>:
                /// <c>StageAndMove → 8|64</c>, <c>StageAndRetainCopy → 4|8|64</c>.
                /// </summary>
                public const string UPDATE_PROMOTION =
                    $@"update version_info
                       set storage_ref = {PATH},
                           flags       = {FLAGS},
                           synced_at   = {SYNCED_AT},
                           size        = case when {SIZE} > 0 then {SIZE} else size end,
                           hash        = case when {HASH} is not null then {HASH} else hash end
                       where id = {ID};";
            }
        }
    }
}
