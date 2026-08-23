using static Haley.Internal.IndexingConstant;

namespace Haley.Internal {
    internal partial class IndexingQueries {
        public partial class INSTANCE {
            public class DOCVERSION {
                public const string EXISTS = $@"select dv.id , dv.cuid as uid from doc_version as dv where dv.parent = {PARENT} and dv.ver = {VERSION} and dv.sub_ver = 0;";
                public const string EXISTS_BY_CUID = $@"select dv.id from doc_version as dv where dv.cuid = {CUID};";
                public const string EXISTS_ACTIVE_BY_CUID = $@"select dv.id from doc_version as dv inner join document as d on d.id = dv.parent and d.delete_state = 0 where dv.cuid = {CUID} and dv.delete_state = 0;";
                public const string EXISTS_BY_ID = $@"select 1 from doc_version as dv where dv.id = {ID};";
                public const string INSERT = $@"insert ignore into doc_version (parent,ver,actor) values({PARENT},{VERSION},{ACTOR});";
                public const string FIND_LATEST = $@"select MAX(dv.ver) from doc_version as dv where dv.parent = {PARENT} and dv.sub_ver = 0 and dv.delete_state = 0;";
                public const string GET_DOCUMENT_ID_BY_VERSION_ID = $@"select dv.parent from doc_version as dv where dv.id = {VALUE} limit 1;";
                public const string GET_DOCUMENT_ID_BY_VERSION_CUID = $@"select dv.parent from doc_version as dv where dv.cuid = {VALUE} limit 1;";
                /// <summary>Returns 1 if the given version CUID is the latest version of its document, 0 otherwise.</summary>
                public const string IS_LATEST_BY_CUID =
                    $@"select case when dv.ver = (select max(dvi.ver) from doc_version as dvi where dvi.parent = dv.parent and dvi.sub_ver = 0 and dvi.delete_state = 0) then 1 else 0 end as is_latest
                       from doc_version as dv
                       inner join document as d on d.id = dv.parent and d.delete_state = 0
                       where dv.cuid = {VALUE} and dv.delete_state = 0
                       limit 1;";
                public const string GET_META_BY_CUID =
                    $@"select vi.metadata from version_info as vi inner join doc_version as dv on dv.id = vi.id inner join document as d on d.id = dv.parent and d.delete_state = 0 where dv.cuid = {VALUE} and dv.delete_state = 0 limit 1;";
                public const string UPDATE_META_BY_ID =
                    $@"update version_info set metadata = {METADATA} where id = {ID};";

                // Writes -> storage_name/storage_ref/size/hash/synced_at/profile_info_id
                // hash, synced_at, and profile_info_id are nullable — pass DBNull.Value when not available.
                // profile_info_id uses COALESCE on update so existing stamped values are never overwritten.
                public const string INSERT_INFO =
                    $@"insert into version_info (id, storage_name, storage_ref, size, hash, synced_at, profile_info_id)
                       values({ID},{SAVENAME},{PATH},{SIZE},{HASH},{SYNCED_AT},{PROFILE_INFO_ID})
                       ON DUPLICATE KEY UPDATE
                            storage_name = VALUES(storage_name),
                            storage_ref = VALUES(storage_ref),
                            size = VALUES(size),
                            hash = COALESCE(VALUES(hash), hash),
                            synced_at = COALESCE(VALUES(synced_at), synced_at),
                            profile_info_id = COALESCE(VALUES(profile_info_id), profile_info_id);";

                // Aliases: storage_name→saveas_name, storage_ref→path, staging_ref→staging_path (backward compat with PopulateFileFromDic)
                public const string GET_INFO =
                    $@"select id, storage_name as saveas_name, storage_ref as path, staging_ref as staging_path, size, hash, synced_at, metadata, flags, profile_info_id
                       from version_info
                       where id = {ID};";

                public const string GET_FULL_BY_CUID =
                    $@"select dv.id, dv.cuid as uid, d.cuid as ruid, dv.created, dv.ver, dv.actor, vi.storage_ref as path, vi.size, vi.storage_name as saveas_name, vi.staging_ref as staging_path, vi.hash, vi.synced_at, vi.flags, vi.metadata, vi.profile_info_id, di.display_name as dname
                       from doc_version as dv
                       inner join document as d on d.id = dv.parent and d.delete_state = 0
                       inner join version_info as vi on vi.id = dv.id
                       left join doc_info as di on di.file = dv.parent
                       where dv.cuid = {VALUE} and dv.delete_state = 0;";

                public const string GET_FULL_BY_STORAGE_NAME =
                    $@"select dv.id, dv.cuid as uid, d.cuid as ruid, dv.created, dv.ver, dv.actor, vi.storage_ref as path, vi.size, vi.storage_name as saveas_name, vi.staging_ref as staging_path, vi.hash, vi.synced_at, vi.flags, vi.metadata, vi.profile_info_id, di.display_name as dname
                       from version_info as vi
                       inner join doc_version as dv on dv.id = vi.id and dv.delete_state = 0
                       inner join document as d on d.id = dv.parent and d.delete_state = 0
                       left join doc_info as di on di.file = dv.parent
                       where vi.storage_name = {VALUE}
                       limit 1;";

                public const string GET_FULL_BY_ID =
                    $@"select dv.id, dv.cuid as uid, d.cuid as ruid, dv.created, dv.ver, dv.actor, vi.storage_ref as path, vi.size, vi.storage_name as saveas_name, vi.staging_ref as staging_path, vi.hash, vi.synced_at, vi.flags, vi.metadata, vi.profile_info_id, di.display_name as dname
                       from doc_version as dv
                       inner join document as d on d.id = dv.parent and d.delete_state = 0
                       inner join version_info as vi on vi.id = dv.id
                       left join doc_info as di on di.file = dv.parent
                       where dv.id = {VALUE} and dv.delete_state = 0;";

                /// <summary>Returns the latest content (sub_ver=0) version row for a document. Excludes thumbnail sub-versions.</summary>
                public const string GET_LATEST_BY_PARENT =
                    $@"select dv.id, dv.cuid as uid, d.cuid as ruid, dv.created, dv.ver, dv.actor, vi.storage_ref as path, vi.size, vi.storage_name as saveas_name, vi.staging_ref as staging_path, vi.hash, vi.synced_at, vi.flags, vi.metadata, vi.profile_info_id, di.display_name as dname
                       from doc_version as dv
                       inner join document as d on d.id = dv.parent and d.delete_state = 0
                       inner join (select max(dvi.ver) as ver from doc_version as dvi where dvi.parent = {PARENT} and dvi.sub_ver = 0 and dvi.delete_state = 0) as dvo on dvo.ver = dv.ver
                       inner join version_info as vi on vi.id = dv.id
                       left join doc_info as di on di.file = {PARENT}
                       where dv.parent = {PARENT} and dv.sub_ver = 0 and dv.delete_state = 0;";

                /// <summary>Returns all content (sub_ver=0) versions for a document, newest first. Excludes thumbnail sub-versions.</summary>
                public const string GET_ALL_BY_PARENT =
                    $@"select dv.id as version_id, dv.cuid as version_cuid, dv.ver as version_no, dv.actor as actor_id, dv.created as version_created, vi.size, vi.storage_name, vi.storage_ref, vi.staging_ref, vi.flags, vi.hash, vi.synced_at, vi.metadata
                       from doc_version as dv
                       left join version_info as vi on vi.id = dv.id
                       where dv.parent = {PARENT} and dv.sub_ver = 0 and dv.delete_state = 0
                       order by dv.ver desc;";
                public const string GET_ALL_CONTENT_BY_PARENT_ALL =
                    $@"select dv.id as version_id, dv.cuid as version_cuid, dv.ver as version_no, dv.actor as actor_id, dv.delete_state, dv.deleted, dv.created as version_created, vi.size, vi.storage_name, vi.storage_ref, vi.staging_ref, vi.flags, vi.hash, vi.synced_at, vi.metadata
                       from doc_version as dv
                       left join version_info as vi on vi.id = dv.id
                       where dv.parent = {PARENT} and dv.sub_ver = 0
                       order by dv.ver desc;";
                public const string GET_ALL_BY_PARENT_ALL =
                    $@"select dv.id as version_id, dv.cuid as version_cuid, dv.ver as version_no, dv.sub_ver as sub_version_no, dv.actor as actor_id, dv.delete_state, dv.deleted, dv.created as version_created, vi.size, vi.storage_name, vi.storage_ref, vi.staging_ref, vi.flags, vi.hash, vi.synced_at, vi.metadata, vi.profile_info_id
                       from doc_version as dv
                       left join version_info as vi on vi.id = dv.id
                       where dv.parent = {PARENT}
                       order by dv.ver desc, dv.sub_ver desc;";
                public const string GET_DELETE_TARGET_BY_CUID =
                    $@"select dv.id as version_id, dv.parent as document_id, dv.ver as version_no, dv.sub_ver as sub_version_no, dv.delete_state
                       from doc_version as dv
                       inner join document as d on d.id = dv.parent
                       where dv.cuid = {CUID}
                       limit 1;";
                public const string SOFT_DELETE_BY_PARENT = $@"update doc_version set delete_state = 1, deleted = {DELETED} where parent = {PARENT};";
                public const string SOFT_DELETE_BY_VERSION = $@"update doc_version set delete_state = 1, deleted = {DELETED} where parent = {PARENT} and ver = {VERSION} and delete_state = 0;";
                public const string SOFT_DELETE_BY_ID = $@"update doc_version set delete_state = 1, deleted = {DELETED} where id = {ID} and delete_state = 0;";
                public const string ARCHIVE_BY_VERSION = $@"update doc_version set delete_state = 2 where parent = {PARENT} and ver = {VERSION} and delete_state > 0;";
                public const string ARCHIVE_BY_ID = $@"update doc_version set delete_state = 2 where id = {ID} and delete_state > 0;";
                public const string ARCHIVE_BY_PARENT = $@"update doc_version set delete_state = 2 where parent = {PARENT} and delete_state > 0;";
                public const string RESTORE_BY_VERSION = $@"update doc_version set delete_state = 0, deleted = null where parent = {PARENT} and ver = {VERSION} and delete_state in (1,2);";
                public const string RESTORE_BY_ID = $@"update doc_version set delete_state = 0, deleted = null where id = {ID} and delete_state in (1,2);";
                public const string RESTORE_BY_PARENT = $@"update doc_version set delete_state = 0, deleted = null where parent = {PARENT} and delete_state in (1,2);";

                // ── Thumbnail queries ─────────────────────────────────────────────────

                /// <summary>
                /// Inserts a thumbnail doc_version row with the given ver (same as the content version)
                /// and an explicit sub_ver (= MAX(sub_ver)+1 for that parent+ver, computed by caller).
                /// </summary>
                public const string INSERT_THUMBNAIL =
                    $@"insert ignore into doc_version (parent, ver, sub_ver, actor) values ({PARENT}, {VERSION}, {SUB_VER}, {ACTOR});";

                /// <summary>
                /// Returns COALESCE(MAX(sub_ver), 0) for thumbnail sub-versions of a specific (parent, ver).
                /// Caller adds 1 to get the next sub_ver to insert.
                /// </summary>
                public const string FIND_LATEST_SUB_VER =
                    $@"select COALESCE(MAX(dv.sub_ver), 0) from doc_version as dv where dv.parent = {PARENT} and dv.ver = {VERSION} and dv.sub_ver > 0 and dv.delete_state = 0;";

                /// <summary>
                /// Fetches the latest thumbnail sub-version storage info for a specific (parent document, content ver).
                /// Returns the row with the highest sub_ver > 0 for the given (parent, ver).
                /// </summary>
                public const string GET_LATEST_THUMB_BY_VERSION =
                    $@"select dv.id, dv.cuid as uid, dv.sub_ver,
                              vi.storage_ref as path, vi.size, vi.storage_name as saveas_name,
                              vi.staging_ref as staging_path, vi.hash, vi.flags, vi.profile_info_id
                       from doc_version as dv
                       inner join version_info as vi on vi.id = dv.id
                       where dv.parent = {PARENT} and dv.ver = {VERSION} and dv.delete_state = 0
                         and dv.sub_ver = (
                             select MAX(dvi.sub_ver) from doc_version as dvi
                             where dvi.parent = {PARENT} and dvi.ver = {VERSION} and dvi.sub_ver > 0 and dvi.delete_state = 0
                         );";

                /// <summary>Fetches back a doc_version row by (parent, ver, sub_ver) — used after INSERT_THUMBNAIL.</summary>
                public const string EXISTS_BY_VERSION_SUBVER =
                    $@"select dv.id, dv.cuid as uid from doc_version as dv where dv.parent = {PARENT} and dv.ver = {VERSION} and dv.sub_ver = {SUB_VER};";

                // Optional extended update — only called when caller explicitly provides these fields.
                // hash/synced_at use COALESCE so a NULL param leaves the existing value unchanged.
                public const string UPDATE_INFO_EXT =
                    $@"update version_info
                       set staging_ref = {STAGINGPATH},
                           metadata     = {METADATA},
                           flags        = {FLAGS},
                           hash         = COALESCE({HASH}, hash),
                           synced_at    = COALESCE({SYNCED_AT}, synced_at)
                       where id = {ID};";
            }
        }
    }
}
