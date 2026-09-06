using static Haley.Internal.IndexingConstant;

namespace Haley.Internal {
    internal partial class IndexingQueries {
        public class STATS_CORE {
            public const string GET_MODULE_IDS =
                $@"select m.id as module_id, m.parent as client_id
                   from module as m
                   where m.cuid = {CUID}
                   limit 1;";

            public const string UPSERT_WORKSPACE =
                $@"insert into ws_stat (
                        workspace, module, client,
                        active_folders, deleted_folders, active_docs, deleted_docs,
                        active_versions, deleted_versions, active_thumbs, deleted_thumbs,
                        active_bytes, deleted_bytes, archived_bytes, purged_bytes)
                   values (
                        {WORKSPACE_ID}, {ID}, {PARENT},
                        {ACTIVE_FOLDERS_DELTA}, {DELETED_FOLDERS_DELTA}, {ACTIVE_DOCS_DELTA}, {DELETED_DOCS_DELTA},
                        {ACTIVE_VERSIONS_DELTA}, {DELETED_VERSIONS_DELTA}, {ACTIVE_THUMBS_DELTA}, {DELETED_THUMBS_DELTA},
                        {ACTIVE_BYTES_DELTA}, {DELETED_BYTES_DELTA}, {ARCHIVED_BYTES_DELTA}, {PURGED_BYTES_DELTA})
                   on duplicate key update
                        module = values(module),
                        client = values(client),
                        active_folders = values(active_folders),
                        deleted_folders = values(deleted_folders),
                        active_docs = values(active_docs),
                        deleted_docs = values(deleted_docs),
                        active_versions = values(active_versions),
                        deleted_versions = values(deleted_versions),
                        active_thumbs = values(active_thumbs),
                        deleted_thumbs = values(deleted_thumbs),
                        active_bytes = values(active_bytes),
                        deleted_bytes = values(deleted_bytes),
                        archived_bytes = values(archived_bytes),
                        purged_bytes = values(purged_bytes);";

            public const string REBUILD_MODULE =
                $@"insert into mod_stat (
                        module, client,
                        active_folders, deleted_folders, active_docs, deleted_docs,
                        active_versions, deleted_versions, active_thumbs, deleted_thumbs,
                        active_bytes, deleted_bytes, archived_bytes, purged_bytes)
                   select {ID}, {PARENT},
                          coalesce(sum(active_folders), 0),
                          coalesce(sum(deleted_folders), 0),
                          coalesce(sum(active_docs), 0),
                          coalesce(sum(deleted_docs), 0),
                          coalesce(sum(active_versions), 0),
                          coalesce(sum(deleted_versions), 0),
                          coalesce(sum(active_thumbs), 0),
                          coalesce(sum(deleted_thumbs), 0),
                          coalesce(sum(active_bytes), 0),
                          coalesce(sum(deleted_bytes), 0),
                          coalesce(sum(archived_bytes), 0),
                          coalesce(sum(purged_bytes), 0)
                   from ws_stat
                   where module = {ID}
                   on duplicate key update
                        client = values(client),
                        active_folders = values(active_folders),
                        deleted_folders = values(deleted_folders),
                        active_docs = values(active_docs),
                        deleted_docs = values(deleted_docs),
                        active_versions = values(active_versions),
                        deleted_versions = values(deleted_versions),
                        active_thumbs = values(active_thumbs),
                        deleted_thumbs = values(deleted_thumbs),
                        active_bytes = values(active_bytes),
                        deleted_bytes = values(deleted_bytes),
                        archived_bytes = values(archived_bytes),
                        purged_bytes = values(purged_bytes);";

            public const string REBUILD_CLIENT =
                $@"insert into cli_stat (
                        client,
                        active_folders, deleted_folders, active_docs, deleted_docs,
                        active_versions, deleted_versions, active_thumbs, deleted_thumbs,
                        active_bytes, deleted_bytes, archived_bytes, purged_bytes)
                   select {PARENT},
                          coalesce(sum(active_folders), 0),
                          coalesce(sum(deleted_folders), 0),
                          coalesce(sum(active_docs), 0),
                          coalesce(sum(deleted_docs), 0),
                          coalesce(sum(active_versions), 0),
                          coalesce(sum(deleted_versions), 0),
                          coalesce(sum(active_thumbs), 0),
                          coalesce(sum(deleted_thumbs), 0),
                          coalesce(sum(active_bytes), 0),
                          coalesce(sum(deleted_bytes), 0),
                          coalesce(sum(archived_bytes), 0),
                          coalesce(sum(purged_bytes), 0)
                   from mod_stat
                   where client = {PARENT}
                   on duplicate key update
                        active_folders = values(active_folders),
                        deleted_folders = values(deleted_folders),
                        active_docs = values(active_docs),
                        deleted_docs = values(deleted_docs),
                        active_versions = values(active_versions),
                        deleted_versions = values(deleted_versions),
                        active_thumbs = values(active_thumbs),
                        deleted_thumbs = values(deleted_thumbs),
                        active_bytes = values(active_bytes),
                        deleted_bytes = values(deleted_bytes),
                        archived_bytes = values(archived_bytes),
                        purged_bytes = values(purged_bytes);";
        }
    }
}
