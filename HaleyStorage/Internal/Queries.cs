using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml.Linq;
using static Haley.Internal.IndexingConstant;

namespace Haley.Internal {
    /// <summary>
    /// SQL statement constants for all core-DB and per-module-DB operations used by
    /// <see cref="Haley.Utils.MariaDBIndexing"/>. Organised into nested classes mirroring the
    /// DB table structure: CLIENT, MODULE, WORKSPACE, PROVIDER, PROFILE, PROFILE_INFO, and the
    /// per-module INSTANCE group (WORKSPACE, DIRECTORY, EXTENSION, VAULT, NAMESTORE, DOCUMENT,
    /// DOCVERSION, CHUNK).
    /// </summary>
    internal class IndexingQueries {
        public class GENERAL {
            public const string SCHEMA_EXISTS = $@"select 1 from information_schema.schemata where schema_name = {NAME};";
        }
        public class CLIENT {
            public const string EXISTS = $@"select c.id from client as c where c.name = {NAME} LIMIT 1;";
            public const string UPSERTKEYS = $@"insert into client_keys (client,signing,encrypt,password) values ({ID},{SIGNKEY},{ENCRYPTKEY},{PASSWORD}) ON DUPLICATE KEY UPDATE signing =  VALUES(signing), encrypt = VALUES(encrypt), password = VALUES(password);";
            /// <summary>
            /// Plain insert — called only after EXISTS confirms the client does not yet exist.
            /// INSERT IGNORE handles the rare concurrent-race edge case without consuming an AUTO_INCREMENT id.
            /// </summary>
            public const string INSERT = $@"insert ignore into client (name,display_name,guid) values ({NAME},{DNAME},{GUID});";
            public const string UPDATE = $@"update client set display_name = {DNAME} where id = {ID};";
            public const string GETKEYS = $@"select * from client_keys as c where c.client = {ID} LIMIT 1;";
        }

        public class MODULE {
            public const string EXISTS = $@"select m.id from module as m where m.name = {NAME} and m.parent = {PARENT} LIMIT 1;";
            public const string EXISTS_BY_CUID = $@"select m.id from module as m where m.cuid = {CUID} LIMIT 1;";
            /// <summary>Plain insert — called only after EXISTS_BY_CUID confirms the module does not yet exist. INSERT IGNORE handles the rare concurrent-race edge case without consuming an AUTO_INCREMENT id.</summary>
            public const string INSERT = $@"insert ignore into module (parent,name,display_name,guid,cuid) values ({PARENT},{NAME},{DNAME},{GUID},{CUID});";
            public const string UPDATE = $@"update module set display_name = {DNAME} where id = {ID};";

            public const string UPDATE_STORAGE_PROFILE_BY_ID = $@"update module set storage_profile = {PROFILE_ID} where id = {ID};";
            public const string UPDATE_STORAGE_PROFILE_BY_CUID = $@"update module set storage_profile = {PROFILE_ID} where cuid = {CUID};";
            /// <summary>Returns all modules that have a storage_profile assigned, with resolved provider name strings.</summary>
            public const string GET_ALL_PROFILES_WITH_KEYS =
                $@"select m.cuid, pi.id as profile_info_id, pi.mode, sp.name as storage_provider_key, stp.name as staging_provider_key
                   from module as m
                   inner join profile_info as pi on pi.id = m.storage_profile
                   left join provider as sp on sp.id = pi.storage_provider
                   left join provider as stp on stp.id = pi.staging_provider
                   where m.storage_profile IS NOT NULL;";
        }

        public class WORKSPACE {
            public const string EXISTS = $@"select ws.id from workspace as ws where ws.name = {NAME} and ws.parent = {PARENT} LIMIT 1;";
            public const string EXISTS_BY_CUID = $@"select ws.id from workspace as ws where ws.cuid = {CUID} LIMIT 1;";
            /// <summary>Plain insert — called only after EXISTS_BY_CUID confirms the workspace does not yet exist. INSERT IGNORE handles the rare concurrent-race edge case without consuming an AUTO_INCREMENT id.</summary>
            public const string INSERT = $@"insert ignore into workspace (parent,name,display_name,guid,cuid,storagename_mode,storagename_parse) values ({PARENT},{NAME},{DNAME},{GUID},{CUID},{STORAGENAME_MODE},{STORAGENAME_PARSE});";
            public const string UPDATE = $@"update workspace set display_name={DNAME},storagename_mode={STORAGENAME_MODE},storagename_parse={STORAGENAME_PARSE} where id={ID};";
            public const string UPDATE_STORAGE_PROFILE_BY_CUID = $@"update workspace set storage_profile = {STORAGE_PROFILE} where cuid = {CUID};";
            public const string UPDATE_STORAGE_PROFILE_BY_ID = $@"update workspace set storage_profile = {STORAGE_PROFILE} where id = {ID};";
            /// <summary>Returns all workspaces that have a storage_profile assigned, with resolved provider name strings.</summary>
            public const string GET_ALL_PROFILES_WITH_KEYS =
                $@"select ws.cuid, pi.id as profile_info_id, pi.mode, sp.name as storage_provider_key, stp.name as staging_provider_key
                   from workspace as ws
                   inner join profile_info as pi on pi.id = ws.storage_profile
                   left join provider as sp on sp.id = pi.storage_provider
                   left join provider as stp on stp.id = pi.staging_provider
                   where ws.storage_profile IS NOT NULL;";
        }

        public class PROVIDER {
            public const string EXISTS = $@"select p.id from provider as p where p.name = {NAME} LIMIT 1;";
            /// <summary>Plain insert — called only after EXISTS confirms the provider does not yet exist.</summary>
            public const string INSERT = $@"insert ignore into provider (name, display_name, description) values ({NAME},{DNAME},{DESCRIPTION});";
            public const string UPDATE = $@"update provider set display_name = {DNAME}, description = {DESCRIPTION} where id = {ID};";
        }

        public class PROFILE {
            public const string EXISTS = $@"select pr.id from profile as pr where pr.name = {NAME} LIMIT 1;";
            /// <summary>Plain insert — called only after EXISTS confirms the profile does not yet exist.</summary>
            public const string INSERT = $@"insert ignore into profile (name, display_name) values ({NAME},{DNAME});";
            public const string UPDATE = $@"update profile set display_name = {DNAME} where id = {ID};";
        }

        public class PROFILE_INFO {
            public const string EXISTS = $@"select pi.id from profile_info as pi where pi.profile = {PROFILE_ID} and pi.version = {VERSION} LIMIT 1;";
            /// <summary>Deduplication check — returns the id of any existing row with the same configuration hash.</summary>
            public const string EXISTS_BY_HASH = $@"select pi.id from profile_info as pi where pi.hash = {HASH} LIMIT 1;";
            /// <summary>Plain insert — called only after EXISTS_BY_HASH and EXISTS confirm no equivalent row exists.</summary>
            public const string INSERT = $@"insert ignore into profile_info (profile, version, mode, storage_provider, staging_provider, metadata, hash) values ({PROFILE_ID},{VERSION},{MODE},{STORAGE_PROVIDER},{STAGING_PROVIDER},{METADATA},{HASH});";
            public const string UPDATE = $@"update profile_info set mode={MODE}, storage_provider={STORAGE_PROVIDER}, staging_provider={STAGING_PROVIDER}, metadata={METADATA}, hash={HASH} where id={ID};";
            /// <summary>Loads a profile_info row together with the resolved provider name strings.</summary>
            public const string GET_WITH_PROVIDER_KEYS =
                $@"select pi.id as profile_info_id, pi.mode, pi.metadata, sp.name as storage_provider_key, stp.name as staging_provider_key
                   from profile_info as pi
                   left join provider as sp on sp.id = pi.storage_provider
                   left join provider as stp on stp.id = pi.staging_provider
                   where pi.id = {PROFILE_ID}
                   limit 1;";

        }

        public class INSTANCE {
            public class WORKSPACE {
                public const string EXISTS = $@"select w.id from workspace as w where w.id = {ID};";
                public const string INSERT = $@"insert IGNORE into workspace (id) values ({ID});";
            }

            public class DIRECTORY {
                public const string EXISTS = $@"select dir.id, dir.cuid as uid from directory as dir where dir.workspace = {WSPACE} and dir.parent = {PARENT} and dir.name = {NAME} and dir.deleted = 0;";
                public const string EXISTS_BY_CUID = $@"select dir.id, dir.cuid as uid from directory as dir where dir.cuid = {VALUE} and dir.deleted = 0;";
                public const string EXISTS_BY_ID = $@"select dir.id, dir.cuid as uid from directory as dir where dir.id = {VALUE} and dir.deleted = 0;";
                public const string INSERT = $@"insert ignore into directory (workspace,parent,name,display_name,actor) values ({WSPACE},{PARENT},{NAME},{DNAME},{ACTOR});";
                public const string GET = $@"select dir.id from directory as dir where dir.workspace = {WSPACE} and dir.parent={PARENT} and dir.name ={NAME} and dir.deleted = 0;";
                public const string GET_BY_CUID = $@"select dir.id from directory as dir where dir.cuid = {CUID} and dir.deleted = 0;";
                public const string GET_DETAILS =
                    $@"select dir.id, dir.cuid as uid, dir.name, dir.display_name, dir.actor, dir.parent, dir.workspace, dir.created, dir.modified
                       from directory as dir
                       where dir.workspace = {WSPACE} and dir.parent = {PARENT} and dir.name = {NAME} and dir.deleted = 0
                       limit 1;";
                public const string GET_DETAILS_BY_CUID =
                    $@"select dir.id, dir.cuid as uid, dir.name, dir.display_name, dir.actor, dir.parent, dir.workspace, dir.created, dir.modified
                       from directory as dir
                       where dir.cuid = {VALUE} and dir.deleted = 0
                       limit 1;";
                public const string GET_DETAILS_BY_ID =
                    $@"select dir.id, dir.cuid as uid, dir.name, dir.display_name, dir.actor, dir.parent, dir.workspace, dir.created, dir.modified
                       from directory as dir
                       where dir.id = {VALUE} and dir.deleted = 0
                       limit 1;";
                public const string COUNT_CHILDREN = $@"select count(*) from directory as dir where dir.workspace = {WSPACE} and dir.parent = {PARENT} and dir.deleted = 0;";
                public const string BROWSE_ITEMS =
                    $@"select *
                       from (
                             select 0 as sort_group, 'folder' as item_type, dir.id, dir.cuid as uid, dir.display_name, dir.actor as actor_id, dir.parent as parent_id, dir.created, dir.modified, null as version_id, null as version_cuid, null as version_no, null as version_count, null as version_created, null as size, null as storage_name, null as storage_ref, null as staging_ref, null as flags, null as hash, null as synced_at
                             from directory as dir
                             where dir.workspace = {WSPACE} and dir.parent = {PARENT} and dir.deleted = 0

                             union all

                            select 1 as sort_group, 'file' as item_type, d.id, d.cuid as uid, coalesce(di.display_name, '') as display_name, dv.actor as actor_id, d.parent as parent_id, d.created, d.modified, dv.id as version_id, dv.cuid as version_cuid, dv.ver as version_no, latest.version_count, dv.created as version_created, vi.size, vi.storage_name, vi.storage_ref, vi.staging_ref, vi.flags, vi.hash, vi.synced_at
                            from document as d
                            left join doc_info as di on di.file = d.id
                            inner join (
                                select dvi.parent, max(dvi.ver) as max_ver, count(*) as version_count
                                from doc_version as dvi
                                where dvi.sub_ver = 0
                                group by dvi.parent
                             ) as latest on latest.parent = d.id
                             inner join doc_version as dv on dv.parent = d.id and dv.ver = latest.max_ver and dv.sub_ver = 0
                             left join version_info as vi on vi.id = dv.id
                             where d.workspace = {WSPACE} and d.parent = {PARENT} and d.deleted = 0
                       ) as browse_items
                       order by browse_items.sort_group asc, browse_items.display_name asc, browse_items.id asc
                       limit {LIMIT_ROWS} offset {OFFSET_ROWS};";

                public const string GET_BY_DOC_VERSION_CUID =
                    $@"select dir.display_name, dir.cuid, dir.name
                       from doc_version as dv
                       join document as d on d.id = dv.parent and d.deleted = 0
                       join directory as dir on dir.id = d.parent and dir.deleted = 0
                       where dv.cuid = {CUID};";
            }
            public class EXTENSION {
                public const string EXISTS = $@"select ext.id from extension as ext where ext.name = {NAME};";
                public const string INSERT = $@"insert ignore into extension (name) values ({NAME});";
            }

            public class VAULT {
                public const string EXISTS = $@"select v.id from vault as v where v.name = {NAME};";
                public const string INSERT = $@"insert ignore into vault (name) values ({NAME});";
            }

            public class NAMESTORE {
                public const string EXISTS = $@"select ns.id from name_store as ns where ns.name = {NAME} and ns.extension = {EXT};";
                public const string INSERT = $@"insert ignore into name_store (name,extension) values ({NAME},{EXT});";
                public const string GET = $@"SELECT ns.id FROM name_store AS ns 
                                            INNER JOIN ( SELECT vin.id FROM vault AS vin WHERE vin.name = {NAME}) AS v ON v.id = ns.name
                                            INNER JOIN extension AS ext ON ext.id = ns.extension
                                            WHERE ext.name = {EXT};";
            }

            public class DOCUMENT {
                public const string EXISTS = $@"select doc.id , doc.cuid as uid from document as doc where doc.parent = {PARENT} and doc.name = {NAME} and doc.deleted = 0;";
                public const string EXISTS_BY_CUID = $@"select doc.id from document as doc where doc.cuid = {CUID} and doc.deleted = 0;";
                public const string INSERT = $@"insert ignore into document (workspace,parent,name) values ({WSPACE},{PARENT},{NAME});";
                public const string INSERT_INFO = $@"insert into doc_info (file,display_name,actor) values ({PARENT}, {DNAME}, {ACTOR}) ON DUPLICATE KEY UPDATE display_name = VALUES(display_name);";
                public const string GET_BY_PARENT = $@"select doc.id from document as doc where doc.parent= {PARENT} and doc.name = {NAME} and doc.deleted = 0;";
                public const string GET_BY_CUID = $@"select doc.id from document as doc where doc.cuid = {CUID} and doc.deleted = 0;";
                public const string COUNT_BY_DIRECTORY = $@"select count(*) from document as doc where doc.workspace = {WSPACE} and doc.parent = {PARENT} and doc.deleted = 0;";
                public const string GET_DETAILS_BY_ID =
                    $@"select d.id as document_id, d.cuid as document_cuid, d.workspace as workspace_id, dir.id as directory_id, dir.cuid as directory_cuid, dir.display_name as directory_name, dir.actor as directory_actor_id, dir.parent as directory_parent_id, coalesce(di.display_name, '') as display_name, di.metadata as doc_metadata, di.actor as document_actor_id
                       from document as d
                       left join doc_info as di on di.file = d.id
                       inner join directory as dir on dir.id = d.parent and dir.deleted = 0
                       where d.id = {ID} and d.deleted = 0
                       limit 1;";
                public const string GET_META_BY_CUID =
                    $@"select di.metadata from doc_info as di inner join document as d on d.id = di.file where d.cuid = {CUID} and d.deleted = 0 limit 1;";
                public const string UPSERT_META =
                    $@"insert into doc_info (file, display_name, metadata) select d.id, coalesce(di.display_name, ''), {METADATA} from document as d left join doc_info as di on di.file = d.id where d.cuid = {CUID} and d.deleted = 0 limit 1 on duplicate key update metadata = VALUES(metadata);";
                public const string GET_BY_NAME =
                    $@"select dv.id
                       from document as dv
                       inner join (
                           select ns.id
                           from name_store as ns
                           inner join (select vin.id from vault as vin where vin.name = {NAME}) as v on v.id = ns.name
                           inner join extension as ext on ext.id = ns.extension
                           where ext.name = {EXT}
                       ) as ons on ons.id = dv.name
                        inner join (
                            select dir.id
                            from directory as dir
                            where dir.workspace = {WSPACE} and dir.parent = {PARENT} and dir.name = {DIRNAME} and dir.deleted = 0
                        ) as odir on odir.id = dv.parent
                        where dv.deleted = 0;";
            }
            
            public class DOCVERSION {
                public const string EXISTS = $@"select dv.id , dv.cuid as uid from doc_version as dv where dv.parent = {PARENT} and dv.ver = {VERSION} and dv.sub_ver = 0;";
                public const string EXISTS_BY_CUID = $@"select dv.id from doc_version as dv where dv.cuid = {CUID};";
                public const string EXISTS_BY_ID = $@"select 1 from doc_version as dv where dv.id = {ID};";
                public const string INSERT = $@"insert ignore into doc_version (parent,ver,actor) values({PARENT},{VERSION},{ACTOR});";
                public const string FIND_LATEST = $@"select MAX(dv.ver) from doc_version as dv where dv.parent = {PARENT} and dv.sub_ver = 0;";
                public const string GET_DOCUMENT_ID_BY_VERSION_ID = $@"select dv.parent from doc_version as dv where dv.id = {VALUE} limit 1;";
                public const string GET_DOCUMENT_ID_BY_VERSION_CUID = $@"select dv.parent from doc_version as dv where dv.cuid = {VALUE} limit 1;";
                /// <summary>Returns 1 if the given version CUID is the latest version of its document, 0 otherwise.</summary>
                public const string IS_LATEST_BY_CUID =
                    $@"select case when dv.ver = (select max(dvi.ver) from doc_version as dvi where dvi.parent = dv.parent and dvi.sub_ver = 0) then 1 else 0 end as is_latest
                       from doc_version as dv where dv.cuid = {VALUE} limit 1;";
                public const string GET_META_BY_CUID =
                    $@"select vi.metadata from version_info as vi inner join doc_version as dv on dv.id = vi.id where dv.cuid = {VALUE} limit 1;";
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
                       inner join document as d on d.id = dv.parent
                       inner join version_info as vi on vi.id = dv.id
                       left join doc_info as di on di.file = dv.parent
                       where dv.cuid = {VALUE};";

                public const string GET_FULL_BY_ID =
                    $@"select dv.id, dv.cuid as uid, d.cuid as ruid, dv.created, dv.ver, dv.actor, vi.storage_ref as path, vi.size, vi.storage_name as saveas_name, vi.staging_ref as staging_path, vi.hash, vi.synced_at, vi.flags, vi.metadata, vi.profile_info_id, di.display_name as dname
                       from doc_version as dv
                       inner join document as d on d.id = dv.parent
                       inner join version_info as vi on vi.id = dv.id
                       left join doc_info as di on di.file = dv.parent
                       where dv.id = {VALUE};";

                /// <summary>Returns the latest content (sub_ver=0) version row for a document. Excludes thumbnail sub-versions.</summary>
                public const string GET_LATEST_BY_PARENT =
                    $@"select dv.id, dv.cuid as uid, d.cuid as ruid, dv.created, dv.ver, dv.actor, vi.storage_ref as path, vi.size, vi.storage_name as saveas_name, vi.staging_ref as staging_path, vi.hash, vi.synced_at, vi.flags, vi.metadata, vi.profile_info_id, di.display_name as dname
                       from doc_version as dv
                       inner join document as d on d.id = dv.parent
                       inner join (select max(dvi.ver) as ver from doc_version as dvi where dvi.parent = {PARENT} and dvi.sub_ver = 0) as dvo on dvo.ver = dv.ver
                       inner join version_info as vi on vi.id = dv.id
                       left join doc_info as di on di.file = {PARENT}
                       where dv.parent = {PARENT} and dv.sub_ver = 0;";

                /// <summary>Returns all content (sub_ver=0) versions for a document, newest first. Excludes thumbnail sub-versions.</summary>
                public const string GET_ALL_BY_PARENT =
                    $@"select dv.id as version_id, dv.cuid as version_cuid, dv.ver as version_no, dv.actor as actor_id, dv.created as version_created, vi.size, vi.storage_name, vi.storage_ref, vi.staging_ref, vi.flags, vi.hash, vi.synced_at, vi.metadata
                       from doc_version as dv
                       left join version_info as vi on vi.id = dv.id
                       where dv.parent = {PARENT} and dv.sub_ver = 0
                       order by dv.ver desc;";

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
                    $@"select COALESCE(MAX(dv.sub_ver), 0) from doc_version as dv where dv.parent = {PARENT} and dv.ver = {VERSION} and dv.sub_ver > 0;";

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
                       where dv.parent = {PARENT} and dv.ver = {VERSION}
                         and dv.sub_ver = (
                             select MAX(dvi.sub_ver) from doc_version as dvi
                             where dvi.parent = {PARENT} and dvi.ver = {VERSION} and dvi.sub_ver > 0
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
            public class CHUNK {
                public const string INFO_EXISTS = $@"select 1 from chunk_info where id = {ID};";

                public const string INFO_UPSERT =
                    $@"insert into chunk_info (id, size, parts, name, path, is_completed)
                       values ({ID},{CHUNK_SIZE},{CHUNK_PARTS},{CHUNK_NAME},{PATH},{IS_COMPLETED})
                       ON DUPLICATE KEY UPDATE
                            size = VALUES(size),
                            parts = VALUES(parts),
                            name = VALUES(name),
                            path = VALUES(path),
                            is_completed = VALUES(is_completed);";

                public const string FILE_UPSERT =
                    $@"insert into chunked_files (id, part, size, hash)
                       values ({ID},{PART},{FILESIZE_MB},{HASH})
                       ON DUPLICATE KEY UPDATE
                            size = VALUES(size),
                            hash = COALESCE(VALUES(hash), hash),
                            uploaded = current_timestamp();";

                public const string MARK_COMPLETED =
                    $@"update chunk_info set is_completed = b'1' where id = {ID};";
            }

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
                       inner join document    as d   on d.id = dv.parent  and d.deleted = 0
                       inner join directory   as dir on dir.id = d.parent and dir.deleted = 0
                       inner join workspace   as ws  on ws.id = dir.workspace
                       where (vi.flags & 4) > 0
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

            public class SEARCH {
                // Shared file-join columns used by every ITEMS query (same shape as BROWSE_ITEMS).
                const string _FILE_COLS = $@"1 as sort_group, 'file' as item_type, d.id, d.cuid as uid, coalesce(di.display_name, '') as display_name, dv.actor as actor_id, d.parent as parent_id, d.created, d.modified, dv.id as version_id, dv.cuid as version_cuid, dv.ver as version_no, latest.version_count, dv.created as version_created, vi.size, vi.storage_name, vi.storage_ref, vi.staging_ref, vi.flags, vi.hash, vi.synced_at";
                const string _FILE_JOINS =
                    $@"left join doc_info as di on di.file = d.id
                       inner join (
                           select dvi.parent, max(dvi.ver) as max_ver, count(*) as version_count
                           from doc_version as dvi
                           where dvi.sub_ver = 0
                           group by dvi.parent
                       ) as latest on latest.parent = d.id
                       inner join doc_version as dv on dv.parent = d.id and dv.ver = latest.max_ver and dv.sub_ver = 0
                       left join version_info as vi on vi.id = dv.id
                       inner join name_store as ns on ns.id = d.name
                       inner join vault as v on v.id = ns.name
                       left join extension as ext on ext.id = ns.extension";
                const string _FILE_NAME_FILTER = $@"and v.name like {VALUE} and ({EXT} is null or ext.name = {EXT})";
                const string _DIR_COLS  = $@"0 as sort_group, 'folder' as item_type, dir.id, dir.cuid as uid, dir.display_name, dir.actor as actor_id, dir.parent as parent_id, dir.created, dir.modified, null as version_id, null as version_cuid, null as version_no, null as version_count, null as version_created, null as size, null as storage_name, null as storage_ref, null as staging_ref, null as flags, null as hash, null as synced_at";
                const string _ORDER_PAGE =
                    $@"order by sr.sort_group asc, sr.display_name asc, sr.id asc
                       limit {LIMIT_ROWS} offset {OFFSET_ROWS};";

                // Recursive CTE prefix — reused by all three RECURSIVE queries.
                const string _CTE =
                    $@"with recursive dir_tree as (
                           select id
                           from directory
                           where id = {PARENT} and workspace = {WSPACE} and deleted = 0
                           union all
                           select dch.id
                           from directory dch
                           inner join dir_tree dt on dch.parent = dt.id
                           where dch.workspace = {WSPACE} and dch.deleted = 0
                       ) ";

                // ── Workspace-wide (no directory scope) ───────────────────────────────
                public const string ITEMS_ALL =
                    $@"select *
                       from (
                            select {_DIR_COLS}
                            from directory as dir
                            where dir.workspace = {WSPACE} and dir.deleted = 0 and dir.name like {VALUE}
                            union all
                            select {_FILE_COLS}
                            from document as d
                            {_FILE_JOINS}
                            where d.workspace = {WSPACE} and d.deleted = 0 {_FILE_NAME_FILTER}
                        ) as sr
                        {_ORDER_PAGE}";

                public const string COUNT_DIRS_ALL =
                    $@"select count(*) from directory as dir where dir.workspace = {WSPACE} and dir.deleted = 0 and dir.name like {VALUE};";

                public const string COUNT_FILES_ALL =
                    $@"select count(*) from document as d {_FILE_JOINS} where d.workspace = {WSPACE} and d.deleted = 0 {_FILE_NAME_FILTER};";

                // ── Single directory — direct children only ────────────────────────────
                public const string ITEMS_IN_DIR =
                    $@"select *
                       from (
                            select {_DIR_COLS}
                            from directory as dir
                            where dir.workspace = {WSPACE} and dir.parent = {PARENT} and dir.deleted = 0 and dir.name like {VALUE}
                            union all
                            select {_FILE_COLS}
                            from document as d
                            {_FILE_JOINS}
                            where d.workspace = {WSPACE} and d.parent = {PARENT} and d.deleted = 0 {_FILE_NAME_FILTER}
                        ) as sr
                        {_ORDER_PAGE}";

                public const string COUNT_DIRS_IN_DIR =
                    $@"select count(*) from directory as dir where dir.workspace = {WSPACE} and dir.parent = {PARENT} and dir.deleted = 0 and dir.name like {VALUE};";

                public const string COUNT_FILES_IN_DIR =
                    $@"select count(*) from document as d {_FILE_JOINS} where d.workspace = {WSPACE} and d.parent = {PARENT} and d.deleted = 0 {_FILE_NAME_FILTER};";

                // ── Recursive subtree from a directory ────────────────────────────────
                public const string ITEMS_RECURSIVE = _CTE +
                    $@"select *
                       from (
                            select {_DIR_COLS}
                            from directory as dir
                            where dir.id in (select id from dir_tree) and dir.id != {PARENT} and dir.deleted = 0 and dir.name like {VALUE}
                            union all
                            select {_FILE_COLS}
                            from document as d
                            {_FILE_JOINS}
                            where d.parent in (select id from dir_tree) and d.deleted = 0 {_FILE_NAME_FILTER}
                        ) as sr
                        {_ORDER_PAGE}";

                public const string COUNT_DIRS_RECURSIVE = _CTE +
                    $@"select count(*) from directory as dir where dir.id in (select id from dir_tree) and dir.id != {PARENT} and dir.deleted = 0 and dir.name like {VALUE};";

                public const string COUNT_FILES_RECURSIVE = _CTE +
                    $@"select count(*) from document as d {_FILE_JOINS} where d.parent in (select id from dir_tree) and d.deleted = 0 {_FILE_NAME_FILTER};";
            }
        }
    }
}
