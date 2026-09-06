using static Haley.Internal.IndexingConstant;

namespace Haley.Internal {
    internal partial class IndexingQueries {
        public partial class INSTANCE {
            public class STATS {
                public const string INSERT_DIR_PATH_FOR_DIRECTORY =
                    $@"insert ignore into dir_path (ancestor, descendant, depth)
                       select src.id, src.id, 0
                       from directory as src
                       where src.id = {ID}
                       union all
                       select path.ancestor, src.id, path.depth + 1
                       from directory as src
                       inner join dir_path as path on path.descendant = src.parent
                       where src.id = {ID};";

                public const string QUEUE_EVENT =
                    $@"insert ignore into stat_evt (
                            event_key, event_type, node_type, node_id, workspace, `document`, `version`, ext,
                            active_folders_delta, deleted_folders_delta, active_docs_delta, deleted_docs_delta,
                            active_versions_delta, deleted_versions_delta, active_thumbs_delta, deleted_thumbs_delta,
                            active_bytes_delta, deleted_bytes_delta, archived_bytes_delta, purged_bytes_delta)
                       values (
                            {EVENT_KEY}, {EVENT_TYPE}, {NODE_TYPE}, {NODE_ID}, {WORKSPACE_ID}, {DOCUMENT_ID}, {VERSION_ID}, {EXT_NAME},
                            {ACTIVE_FOLDERS_DELTA}, {DELETED_FOLDERS_DELTA}, {ACTIVE_DOCS_DELTA}, {DELETED_DOCS_DELTA},
                            {ACTIVE_VERSIONS_DELTA}, {DELETED_VERSIONS_DELTA}, {ACTIVE_THUMBS_DELTA}, {DELETED_THUMBS_DELTA},
                            {ACTIVE_BYTES_DELTA}, {DELETED_BYTES_DELTA}, {ARCHIVED_BYTES_DELTA}, {PURGED_BYTES_DELTA});";

                public const string GET_PENDING =
                    $@"select id, event_key, event_type, node_type, node_id, workspace, `document`, `version`, ext,
                              active_folders_delta, deleted_folders_delta, active_docs_delta, deleted_docs_delta,
                              active_versions_delta, deleted_versions_delta, active_thumbs_delta, deleted_thumbs_delta,
                              active_bytes_delta, deleted_bytes_delta, archived_bytes_delta, purged_bytes_delta
                       from stat_evt
                       where processed is null
                       order by id
                       limit {BATCH_SIZE};";

                public const string MARK_PROCESSED =
                    $@"update stat_evt set processed = utc_timestamp(), message = {MESSAGE} where id = {ID};";

                public const string GET_TREE_TARGETS =
                    $@"select 1 as node_type, {WORKSPACE_ID} as node_id, {WORKSPACE_ID} as workspace
                       union all
                       select 2 as node_type, path.ancestor as node_id, dir.workspace as workspace
                       from dir_path as path
                       inner join directory as dir on dir.id = path.ancestor
                       where {NODE_TYPE} = 2 and path.descendant = {NODE_ID};";

                public const string UPSERT_NODE_STAT_DELTA =
                    $@"insert into node_stat (
                            node_type, node_id, workspace,
                            active_folders, deleted_folders, active_docs, deleted_docs,
                            active_versions, deleted_versions, active_thumbs, deleted_thumbs,
                            active_bytes, deleted_bytes, archived_bytes, purged_bytes)
                       values (
                            {NODE_TYPE}, {NODE_ID}, {WORKSPACE_ID},
                            {ACTIVE_FOLDERS_DELTA}, {DELETED_FOLDERS_DELTA}, {ACTIVE_DOCS_DELTA}, {DELETED_DOCS_DELTA},
                            {ACTIVE_VERSIONS_DELTA}, {DELETED_VERSIONS_DELTA}, {ACTIVE_THUMBS_DELTA}, {DELETED_THUMBS_DELTA},
                            {ACTIVE_BYTES_DELTA}, {DELETED_BYTES_DELTA}, {ARCHIVED_BYTES_DELTA}, {PURGED_BYTES_DELTA})
                       on duplicate key update
                            workspace = values(workspace),
                            active_folders = greatest(0, active_folders + values(active_folders)),
                            deleted_folders = greatest(0, deleted_folders + values(deleted_folders)),
                            active_docs = greatest(0, active_docs + values(active_docs)),
                            deleted_docs = greatest(0, deleted_docs + values(deleted_docs)),
                            active_versions = greatest(0, active_versions + values(active_versions)),
                            deleted_versions = greatest(0, deleted_versions + values(deleted_versions)),
                            active_thumbs = greatest(0, active_thumbs + values(active_thumbs)),
                            deleted_thumbs = greatest(0, deleted_thumbs + values(deleted_thumbs)),
                            active_bytes = greatest(0, active_bytes + values(active_bytes)),
                            deleted_bytes = greatest(0, deleted_bytes + values(deleted_bytes)),
                            archived_bytes = greatest(0, archived_bytes + values(archived_bytes)),
                            purged_bytes = greatest(0, purged_bytes + values(purged_bytes));";

                public const string UPSERT_TREE_STAT_DELTA =
                    $@"insert into tree_stat (
                            node_type, node_id, workspace,
                            active_folders, deleted_folders, active_docs, deleted_docs,
                            active_versions, deleted_versions, active_thumbs, deleted_thumbs,
                            active_bytes, deleted_bytes, archived_bytes, purged_bytes)
                       values (
                            {NODE_TYPE}, {NODE_ID}, {WORKSPACE_ID},
                            {ACTIVE_FOLDERS_DELTA}, {DELETED_FOLDERS_DELTA}, {ACTIVE_DOCS_DELTA}, {DELETED_DOCS_DELTA},
                            {ACTIVE_VERSIONS_DELTA}, {DELETED_VERSIONS_DELTA}, {ACTIVE_THUMBS_DELTA}, {DELETED_THUMBS_DELTA},
                            {ACTIVE_BYTES_DELTA}, {DELETED_BYTES_DELTA}, {ARCHIVED_BYTES_DELTA}, {PURGED_BYTES_DELTA})
                       on duplicate key update
                            workspace = values(workspace),
                            active_folders = greatest(0, active_folders + values(active_folders)),
                            deleted_folders = greatest(0, deleted_folders + values(deleted_folders)),
                            active_docs = greatest(0, active_docs + values(active_docs)),
                            deleted_docs = greatest(0, deleted_docs + values(deleted_docs)),
                            active_versions = greatest(0, active_versions + values(active_versions)),
                            deleted_versions = greatest(0, deleted_versions + values(deleted_versions)),
                            active_thumbs = greatest(0, active_thumbs + values(active_thumbs)),
                            deleted_thumbs = greatest(0, deleted_thumbs + values(deleted_thumbs)),
                            active_bytes = greatest(0, active_bytes + values(active_bytes)),
                            deleted_bytes = greatest(0, deleted_bytes + values(deleted_bytes)),
                            archived_bytes = greatest(0, archived_bytes + values(archived_bytes)),
                            purged_bytes = greatest(0, purged_bytes + values(purged_bytes));";

                public const string UPSERT_NODE_EXT_STAT_DELTA =
                    $@"insert into node_ext_stat (
                            node_type, node_id, workspace, ext,
                            active_docs, deleted_docs, active_versions, deleted_versions,
                            active_thumbs, deleted_thumbs, active_bytes, deleted_bytes, archived_bytes, purged_bytes)
                       values (
                            {NODE_TYPE}, {NODE_ID}, {WORKSPACE_ID}, {EXT_NAME},
                            {ACTIVE_DOCS_DELTA}, {DELETED_DOCS_DELTA}, {ACTIVE_VERSIONS_DELTA}, {DELETED_VERSIONS_DELTA},
                            {ACTIVE_THUMBS_DELTA}, {DELETED_THUMBS_DELTA}, {ACTIVE_BYTES_DELTA}, {DELETED_BYTES_DELTA},
                            {ARCHIVED_BYTES_DELTA}, {PURGED_BYTES_DELTA})
                       on duplicate key update
                            workspace = values(workspace),
                            active_docs = greatest(0, active_docs + values(active_docs)),
                            deleted_docs = greatest(0, deleted_docs + values(deleted_docs)),
                            active_versions = greatest(0, active_versions + values(active_versions)),
                            deleted_versions = greatest(0, deleted_versions + values(deleted_versions)),
                            active_thumbs = greatest(0, active_thumbs + values(active_thumbs)),
                            deleted_thumbs = greatest(0, deleted_thumbs + values(deleted_thumbs)),
                            active_bytes = greatest(0, active_bytes + values(active_bytes)),
                            deleted_bytes = greatest(0, deleted_bytes + values(deleted_bytes)),
                            archived_bytes = greatest(0, archived_bytes + values(archived_bytes)),
                            purged_bytes = greatest(0, purged_bytes + values(purged_bytes));";

                public const string UPSERT_TREE_EXT_STAT_DELTA =
                    $@"insert into tree_ext_stat (
                            node_type, node_id, workspace, ext,
                            active_docs, deleted_docs, active_versions, deleted_versions,
                            active_thumbs, deleted_thumbs, active_bytes, deleted_bytes, archived_bytes, purged_bytes)
                       values (
                            {NODE_TYPE}, {NODE_ID}, {WORKSPACE_ID}, {EXT_NAME},
                            {ACTIVE_DOCS_DELTA}, {DELETED_DOCS_DELTA}, {ACTIVE_VERSIONS_DELTA}, {DELETED_VERSIONS_DELTA},
                            {ACTIVE_THUMBS_DELTA}, {DELETED_THUMBS_DELTA}, {ACTIVE_BYTES_DELTA}, {DELETED_BYTES_DELTA},
                            {ARCHIVED_BYTES_DELTA}, {PURGED_BYTES_DELTA})
                       on duplicate key update
                            workspace = values(workspace),
                            active_docs = greatest(0, active_docs + values(active_docs)),
                            deleted_docs = greatest(0, deleted_docs + values(deleted_docs)),
                            active_versions = greatest(0, active_versions + values(active_versions)),
                            deleted_versions = greatest(0, deleted_versions + values(deleted_versions)),
                            active_thumbs = greatest(0, active_thumbs + values(active_thumbs)),
                            deleted_thumbs = greatest(0, deleted_thumbs + values(deleted_thumbs)),
                            active_bytes = greatest(0, active_bytes + values(active_bytes)),
                            deleted_bytes = greatest(0, deleted_bytes + values(deleted_bytes)),
                            archived_bytes = greatest(0, archived_bytes + values(archived_bytes)),
                            purged_bytes = greatest(0, purged_bytes + values(purged_bytes));";

                public const string GET_VERSION_SOURCE =
                    $@"select dv.id as version_id, dv.parent as document_id, dv.ver as version_no, dv.sub_ver as sub_version_no,
                              dv.delete_state as version_delete_state, d.workspace, d.parent as directory_id,
                              d.delete_state as document_delete_state, coalesce(vi.size, 0) as size,
                              coalesce(vi.flags, 0) as flags,
                              case
                                  when dv.sub_ver = 0 then ext.name
                                  when instr(coalesce(nullif(vi.storage_ref, ''), vi.storage_name), '.') > 0
                                      then lower(concat('.', substring_index(coalesce(nullif(vi.storage_ref, ''), vi.storage_name), '.', -1)))
                                  else 'default'
                              end as ext
                       from doc_version as dv
                       inner join document as d on d.id = dv.parent
                       left join version_info as vi on vi.id = dv.id
                       left join name_store as ns on ns.id = d.name
                       left join extension as ext on ext.id = ns.extension
                       where dv.id = {ID}
                       limit 1;";

                public const string GET_VERSION_SOURCES_BY_PARENT =
                    $@"select dv.id as version_id, dv.parent as document_id, dv.ver as version_no, dv.sub_ver as sub_version_no,
                              dv.delete_state as version_delete_state, d.workspace, d.parent as directory_id,
                              d.delete_state as document_delete_state, coalesce(vi.size, 0) as size,
                              coalesce(vi.flags, 0) as flags,
                              case
                                  when dv.sub_ver = 0 then ext.name
                                  when instr(coalesce(nullif(vi.storage_ref, ''), vi.storage_name), '.') > 0
                                      then lower(concat('.', substring_index(coalesce(nullif(vi.storage_ref, ''), vi.storage_name), '.', -1)))
                                  else 'default'
                              end as ext
                       from doc_version as dv
                       inner join document as d on d.id = dv.parent
                       left join version_info as vi on vi.id = dv.id
                       left join name_store as ns on ns.id = d.name
                       left join extension as ext on ext.id = ns.extension
                       where dv.parent = {PARENT}
                       order by dv.ver, dv.sub_ver;";

                public const string GET_VERSION_SOURCES_BY_VERSION =
                    $@"select dv.id as version_id, dv.parent as document_id, dv.ver as version_no, dv.sub_ver as sub_version_no,
                              dv.delete_state as version_delete_state, d.workspace, d.parent as directory_id,
                              d.delete_state as document_delete_state, coalesce(vi.size, 0) as size,
                              coalesce(vi.flags, 0) as flags,
                              case
                                  when dv.sub_ver = 0 then ext.name
                                  when instr(coalesce(nullif(vi.storage_ref, ''), vi.storage_name), '.') > 0
                                      then lower(concat('.', substring_index(coalesce(nullif(vi.storage_ref, ''), vi.storage_name), '.', -1)))
                                  else 'default'
                              end as ext
                       from doc_version as dv
                       inner join document as d on d.id = dv.parent
                       left join version_info as vi on vi.id = dv.id
                       left join name_store as ns on ns.id = d.name
                       left join extension as ext on ext.id = ns.extension
                       where dv.parent = {PARENT} and dv.ver = {VERSION}
                       order by dv.sub_ver;";

                public const string COUNT_ACTIVE_COMPLETED_CONTENT_EXCLUDING =
                    $@"select count(*)
                       from doc_version as dv
                       inner join document as d on d.id = dv.parent and d.delete_state = 0
                       inner join version_info as vi on vi.id = dv.id
                       where dv.parent = {PARENT}
                         and dv.id <> {ID}
                         and dv.sub_ver = 0
                         and dv.delete_state = 0
                         and (coalesce(vi.flags, 0) & 64) <> 0;";

                public const string COUNT_ACTIVE_COMPLETED_CONTENT =
                    $@"select count(*)
                       from doc_version as dv
                       inner join document as d on d.id = dv.parent and d.delete_state = 0
                       inner join version_info as vi on vi.id = dv.id
                       where dv.parent = {PARENT}
                         and dv.sub_ver = 0
                         and dv.delete_state = 0
                         and (coalesce(vi.flags, 0) & 64) <> 0;";

                public const string CLEAR_TREE_EXT = "delete from tree_ext_stat;";
                public const string CLEAR_NODE_EXT = "delete from node_ext_stat;";
                public const string CLEAR_TREE = "delete from tree_stat;";
                public const string CLEAR_NODE = "delete from node_stat;";
                public const string CLEAR_DIR_PATH = "delete from dir_path;";
                public const string CLEAR_EVENTS = "delete from stat_evt;";

                public const string REBUILD_DIR_PATH =
                    @"insert ignore into dir_path (ancestor, descendant, depth)
                      with recursive path_tree as (
                          select dir.id as ancestor, dir.id as descendant, 0 as depth
                          from directory as dir
                          union all
                          select path_tree.ancestor, child.id as descendant, path_tree.depth + 1 as depth
                          from path_tree
                          inner join directory as child on child.parent = path_tree.descendant
                          where child.parent > 0
                      )
                      select ancestor, descendant, depth from path_tree;";

                public const string REBUILD_NODE_STAT_WORKSPACE =
                    @"insert into node_stat (
                          node_type, node_id, workspace,
                          active_folders, deleted_folders, active_docs, deleted_docs,
                          active_versions, deleted_versions, active_thumbs, deleted_thumbs,
                          active_bytes, deleted_bytes, archived_bytes, purged_bytes)
                      select 1, ws.id, ws.id,
                             (select count(*) from directory as dir where dir.workspace = ws.id and dir.parent = 0 and dir.delete_state = 0),
                             (select count(*) from directory as dir where dir.workspace = ws.id and dir.parent = 0 and dir.delete_state > 0),
                             0, 0, 0, 0, 0, 0, 0, 0, 0, 0
                      from workspace as ws;";

                public const string REBUILD_NODE_STAT_DIRECTORY =
                    @"insert into node_stat (
                          node_type, node_id, workspace,
                          active_folders, deleted_folders, active_docs, deleted_docs,
                          active_versions, deleted_versions, active_thumbs, deleted_thumbs,
                          active_bytes, deleted_bytes, archived_bytes, purged_bytes)
                      select 2, dir.id, dir.workspace,
                             (select count(*) from directory as child where child.workspace = dir.workspace and child.parent = dir.id and child.delete_state = 0),
                             (select count(*) from directory as child where child.workspace = dir.workspace and child.parent = dir.id and child.delete_state > 0),
                             (select count(distinct doc.id) from document as doc inner join doc_version as dv on dv.parent = doc.id and dv.sub_ver = 0 and dv.delete_state = 0 inner join version_info as vi on vi.id = dv.id where doc.parent = dir.id and doc.delete_state = 0 and (coalesce(vi.flags, 0) & 64) <> 0),
                             (select count(*) from document as doc where doc.parent = dir.id and doc.delete_state > 0),
                             (select count(*) from document as doc inner join doc_version as dv on dv.parent = doc.id and dv.sub_ver = 0 and dv.delete_state = 0 inner join version_info as vi on vi.id = dv.id where doc.parent = dir.id and doc.delete_state = 0 and (coalesce(vi.flags, 0) & 64) <> 0),
                             (select count(*) from document as doc inner join doc_version as dv on dv.parent = doc.id and dv.sub_ver = 0 and dv.delete_state > 0 inner join version_info as vi on vi.id = dv.id where doc.parent = dir.id and (coalesce(vi.flags, 0) & 64) <> 0),
                             (select count(*) from document as doc inner join doc_version as dv on dv.parent = doc.id and dv.sub_ver > 0 and dv.delete_state = 0 inner join version_info as vi on vi.id = dv.id where doc.parent = dir.id and doc.delete_state = 0 and (coalesce(vi.flags, 0) & 64) <> 0),
                             (select count(*) from document as doc inner join doc_version as dv on dv.parent = doc.id and dv.sub_ver > 0 and dv.delete_state > 0 inner join version_info as vi on vi.id = dv.id where doc.parent = dir.id and (coalesce(vi.flags, 0) & 64) <> 0),
                             (select coalesce(sum(vi.size), 0) from document as doc inner join doc_version as dv on dv.parent = doc.id and dv.delete_state = 0 inner join version_info as vi on vi.id = dv.id where doc.parent = dir.id and doc.delete_state = 0 and (coalesce(vi.flags, 0) & 64) <> 0),
                             (select coalesce(sum(vi.size), 0) from document as doc inner join doc_version as dv on dv.parent = doc.id and dv.delete_state > 0 inner join version_info as vi on vi.id = dv.id where doc.parent = dir.id and (coalesce(vi.flags, 0) & 64) <> 0),
                             (select coalesce(sum(vi.size), 0) from document as doc inner join doc_version as dv on dv.parent = doc.id and dv.delete_state = 2 inner join version_info as vi on vi.id = dv.id where doc.parent = dir.id and (coalesce(vi.flags, 0) & 64) <> 0),
                             (select coalesce(sum(vi.size), 0) from document as doc inner join doc_version as dv on dv.parent = doc.id and dv.delete_state = 3 inner join version_info as vi on vi.id = dv.id where doc.parent = dir.id and (coalesce(vi.flags, 0) & 64) <> 0)
                      from directory as dir;";

                public const string REBUILD_NODE_EXT_STAT =
                    @"insert into node_ext_stat (
                          node_type, node_id, workspace, ext,
                          active_docs, deleted_docs, active_versions, deleted_versions,
                          active_thumbs, deleted_thumbs, active_bytes, deleted_bytes, archived_bytes, purged_bytes)
                      select 2, d.parent, d.workspace,
                             coalesce(case
                                 when dv.sub_ver = 0 then ext.name
                                 when instr(coalesce(nullif(vi.storage_ref, ''), vi.storage_name), '.') > 0
                                     then lower(concat('.', substring_index(coalesce(nullif(vi.storage_ref, ''), vi.storage_name), '.', -1)))
                                 else 'default'
                             end, 'default') as ext_name,
                             count(distinct case when dv.sub_ver = 0 and d.delete_state = 0 and dv.delete_state = 0 then d.id end),
                             count(distinct case when dv.sub_ver = 0 and d.delete_state > 0 then d.id end),
                             sum(case when dv.sub_ver = 0 and d.delete_state = 0 and dv.delete_state = 0 then 1 else 0 end),
                             sum(case when dv.sub_ver = 0 and dv.delete_state > 0 then 1 else 0 end),
                             sum(case when dv.sub_ver > 0 and d.delete_state = 0 and dv.delete_state = 0 then 1 else 0 end),
                             sum(case when dv.sub_ver > 0 and dv.delete_state > 0 then 1 else 0 end),
                             coalesce(sum(case when d.delete_state = 0 and dv.delete_state = 0 then vi.size else 0 end), 0),
                             coalesce(sum(case when dv.delete_state > 0 then vi.size else 0 end), 0),
                             coalesce(sum(case when dv.delete_state = 2 then vi.size else 0 end), 0),
                             coalesce(sum(case when dv.delete_state = 3 then vi.size else 0 end), 0)
                      from document as d
                      inner join doc_version as dv on dv.parent = d.id
                      inner join version_info as vi on vi.id = dv.id
                      left join name_store as ns on ns.id = d.name
                      left join extension as ext on ext.id = ns.extension
                      where (coalesce(vi.flags, 0) & 64) <> 0
                      group by d.parent, d.workspace, ext_name;";

                public const string REBUILD_TREE_STAT_WORKSPACE =
                    @"insert into tree_stat (
                          node_type, node_id, workspace,
                          active_folders, deleted_folders, active_docs, deleted_docs,
                          active_versions, deleted_versions, active_thumbs, deleted_thumbs,
                          active_bytes, deleted_bytes, archived_bytes, purged_bytes)
                      select 1, ws.id, ws.id,
                             coalesce(sum(ns.active_folders), 0),
                             coalesce(sum(ns.deleted_folders), 0),
                             coalesce(sum(ns.active_docs), 0),
                             coalesce(sum(ns.deleted_docs), 0),
                             coalesce(sum(ns.active_versions), 0),
                             coalesce(sum(ns.deleted_versions), 0),
                             coalesce(sum(ns.active_thumbs), 0),
                             coalesce(sum(ns.deleted_thumbs), 0),
                             coalesce(sum(ns.active_bytes), 0),
                             coalesce(sum(ns.deleted_bytes), 0),
                             coalesce(sum(ns.archived_bytes), 0),
                             coalesce(sum(ns.purged_bytes), 0)
                      from workspace as ws
                      left join node_stat as ns on ns.workspace = ws.id
                      group by ws.id;";

                public const string REBUILD_TREE_STAT_DIRECTORY =
                    @"insert into tree_stat (
                          node_type, node_id, workspace,
                          active_folders, deleted_folders, active_docs, deleted_docs,
                          active_versions, deleted_versions, active_thumbs, deleted_thumbs,
                          active_bytes, deleted_bytes, archived_bytes, purged_bytes)
                      select 2, path.ancestor, dir.workspace,
                             coalesce(sum(ns.active_folders), 0),
                             coalesce(sum(ns.deleted_folders), 0),
                             coalesce(sum(ns.active_docs), 0),
                             coalesce(sum(ns.deleted_docs), 0),
                             coalesce(sum(ns.active_versions), 0),
                             coalesce(sum(ns.deleted_versions), 0),
                             coalesce(sum(ns.active_thumbs), 0),
                             coalesce(sum(ns.deleted_thumbs), 0),
                             coalesce(sum(ns.active_bytes), 0),
                             coalesce(sum(ns.deleted_bytes), 0),
                             coalesce(sum(ns.archived_bytes), 0),
                             coalesce(sum(ns.purged_bytes), 0)
                      from dir_path as path
                      inner join directory as dir on dir.id = path.ancestor
                      left join node_stat as ns on ns.node_type = 2 and ns.node_id = path.descendant
                      group by path.ancestor, dir.workspace;";

                public const string REBUILD_TREE_EXT_STAT_WORKSPACE =
                    @"insert into tree_ext_stat (
                          node_type, node_id, workspace, ext,
                          active_docs, deleted_docs, active_versions, deleted_versions,
                          active_thumbs, deleted_thumbs, active_bytes, deleted_bytes, archived_bytes, purged_bytes)
                      select 1, ws.id, ws.id, nes.ext,
                             coalesce(sum(nes.active_docs), 0),
                             coalesce(sum(nes.deleted_docs), 0),
                             coalesce(sum(nes.active_versions), 0),
                             coalesce(sum(nes.deleted_versions), 0),
                             coalesce(sum(nes.active_thumbs), 0),
                             coalesce(sum(nes.deleted_thumbs), 0),
                             coalesce(sum(nes.active_bytes), 0),
                             coalesce(sum(nes.deleted_bytes), 0),
                             coalesce(sum(nes.archived_bytes), 0),
                             coalesce(sum(nes.purged_bytes), 0)
                      from workspace as ws
                      inner join node_ext_stat as nes on nes.workspace = ws.id
                      group by ws.id, nes.ext;";

                public const string REBUILD_TREE_EXT_STAT_DIRECTORY =
                    @"insert into tree_ext_stat (
                          node_type, node_id, workspace, ext,
                          active_docs, deleted_docs, active_versions, deleted_versions,
                          active_thumbs, deleted_thumbs, active_bytes, deleted_bytes, archived_bytes, purged_bytes)
                      select 2, path.ancestor, dir.workspace, nes.ext,
                             coalesce(sum(nes.active_docs), 0),
                             coalesce(sum(nes.deleted_docs), 0),
                             coalesce(sum(nes.active_versions), 0),
                             coalesce(sum(nes.deleted_versions), 0),
                             coalesce(sum(nes.active_thumbs), 0),
                             coalesce(sum(nes.deleted_thumbs), 0),
                             coalesce(sum(nes.active_bytes), 0),
                             coalesce(sum(nes.deleted_bytes), 0),
                             coalesce(sum(nes.archived_bytes), 0),
                             coalesce(sum(nes.purged_bytes), 0)
                      from dir_path as path
                      inner join directory as dir on dir.id = path.ancestor
                      inner join node_ext_stat as nes on nes.node_type = 2 and nes.node_id = path.descendant
                      group by path.ancestor, dir.workspace, nes.ext;";

                public const string GET_NODE_STAT =
                    $@"select * from node_stat where node_type = {NODE_TYPE} and node_id = {NODE_ID} limit 1;";

                public const string GET_TREE_STAT =
                    $@"select * from tree_stat where node_type = {NODE_TYPE} and node_id = {NODE_ID} limit 1;";

                public const string GET_NODE_EXT_STATS =
                    $@"select * from node_ext_stat
                       where node_type = {NODE_TYPE}
                         and node_id = {NODE_ID}
                         and ({EXT_NAME} is null or ext = {EXT_NAME})
                       order by ext;";

                public const string GET_TREE_EXT_STATS =
                    $@"select * from tree_ext_stat
                       where node_type = {NODE_TYPE}
                         and node_id = {NODE_ID}
                         and ({EXT_NAME} is null or ext = {EXT_NAME})
                       order by ext;";

                public const string GET_WORKSPACE_TREE_STATS =
                    @"select * from tree_stat where node_type = 1 order by node_id;";

                public const string INSERT_RUN =
                    $@"insert into stat_run (run_type, status, message) values ({RUN_TYPE}, {STATUS}, {MESSAGE});";
            }
        }
    }
}
