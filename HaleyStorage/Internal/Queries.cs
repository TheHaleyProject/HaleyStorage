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
            /// <summary>Client has no path column — path is derived from name at runtime.</summary>
            public const string UPSERT = $@"insert into client (name,display_name,guid) values ({NAME},{DNAME},{GUID}) ON DUPLICATE KEY UPDATE display_name = VALUES(display_name);";
            public const string UPDATE = $@"update client set display_name = {DNAME} where id = {ID};";
            public const string GETKEYS = $@"select * from client_keys as c where c.client = {ID} LIMIT 1;";
        }

        public class MODULE {
            public const string EXISTS = $@"select m.id from module as m where m.name = {NAME} and m.parent = {PARENT} LIMIT 1;";
            public const string EXISTS_BY_CUID = $@"select m.id from module as m where m.cuid = {CUID} LIMIT 1;";
            /// <summary>Module has no path column — path is derived from name at runtime.</summary>
            public const string UPSERT = $@"insert into module (parent,name,display_name,guid,cuid) values ({PARENT},{NAME},{DNAME},{GUID},{CUID}) ON DUPLICATE KEY UPDATE display_name = VALUES(display_name);";
            public const string UPDATE = $@"update module set display_name = {DNAME} where id = {ID};";

            public const string UPDATE_STORAGE_PROFILE_BY_ID = $@"update module set storage_profile = {PROFILE_ID} where id = {ID};";
            public const string UPDATE_STORAGE_PROFILE_BY_CUID = $@"update module set storage_profile = {PROFILE_ID} where cuid = {CUID};";
        }

        public class WORKSPACE {
            public const string EXISTS = $@"select ws.id from workspace as ws where ws.name = {NAME} and ws.parent = {PARENT} LIMIT 1;";
            public const string EXISTS_BY_CUID = $@"select ws.id from workspace as ws where ws.cuid = {CUID} LIMIT 1;";
            public const string UPSERT = $@"insert into workspace (parent,name,display_name,guid,storage_ref,cuid,storagename_mode,storagename_parse) values ({PARENT},{NAME},{DNAME},{GUID},{STORAGE_REF},{CUID},{STORAGENAME_MODE},{STORAGENAME_PARSE}) ON DUPLICATE KEY UPDATE display_name=VALUES(display_name),storage_ref=VALUES(storage_ref),storagename_mode=VALUES(storagename_mode),storagename_parse=VALUES(storagename_parse);";
            public const string UPDATE = $@"update workspace set display_name={DNAME},storage_ref={STORAGE_REF},storagename_mode={STORAGENAME_MODE},storagename_parse={STORAGENAME_PARSE} where id={ID};";
            public const string UPDATE_STORAGE_PROFILE_BY_CUID = $@"update workspace set storage_profile = {STORAGE_PROFILE} where cuid = {CUID};";
            public const string UPDATE_STORAGE_PROFILE_BY_ID = $@"update workspace set storage_profile = {STORAGE_PROFILE} where id = {ID};";
            /// <summary>Returns all workspaces that have a storage_profile assigned, with resolved provider name strings.</summary>
            public const string GET_ALL_PROFILES_WITH_KEYS =
                $@"SELECT ws.cuid, pi.mode,
                          sp.name  AS storage_provider_key,
                          stp.name AS staging_provider_key
                   FROM workspace AS ws
                   INNER JOIN profile_info AS pi  ON pi.id  = ws.storage_profile
                   LEFT  JOIN provider     AS sp  ON sp.id  = pi.storage_provider
                   LEFT  JOIN provider     AS stp ON stp.id = pi.staging_provider
                   WHERE ws.storage_profile IS NOT NULL;";
        }

        public class PROVIDER {
            public const string EXISTS = $@"select p.id from provider as p where p.name = {NAME} LIMIT 1;";
            public const string UPSERT = $@"insert into provider (name, description) values ({NAME},{DESCRIPTION})
                                            ON DUPLICATE KEY UPDATE description = VALUES(description);";
        }

        public class PROFILE {
            public const string EXISTS = $@"select pr.id from profile as pr where pr.name = {NAME} LIMIT 1;";
            public const string UPSERT = $@"insert into profile (name) values ({NAME})
                                            ON DUPLICATE KEY UPDATE name = VALUES(name);";
        }

        public class PROFILE_INFO {
            public const string EXISTS = $@"select pi.id from profile_info as pi where pi.profile = {PROFILE_ID} and pi.version = {VERSION} LIMIT 1;";
            /// <summary>Loads a profile_info row together with the resolved provider name strings.</summary>
            public const string GET_WITH_PROVIDER_KEYS =
                $@"SELECT pi.mode, pi.metadata,
                          sp.name  AS storage_provider_key,
                          stp.name AS staging_provider_key
                   FROM profile_info AS pi
                   LEFT JOIN provider AS sp  ON sp.id  = pi.storage_provider
                   LEFT JOIN provider AS stp ON stp.id = pi.staging_provider
                   WHERE pi.id = {PROFILE_ID} LIMIT 1;";

            // MetaData
            public const string UPSERT = $@"insert into profile_info (profile, version, mode, storage_provider, staging_provider, metadata)
                                            values ({PROFILE_ID},{VERSION},{MODE},{STORAGE_PROVIDER},{STAGING_PROVIDER},{METADATA})
                                            ON DUPLICATE KEY UPDATE
                                                mode = VALUES(mode),
                                                storage_provider = VALUES(storage_provider),
                                                staging_provider = VALUES(staging_provider),
                                                metadata = VALUES(metadata);";
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
                public const string INSERT = $@"insert ignore into directory (workspace,parent,name,display_name) values ({WSPACE},{PARENT},{NAME},{DNAME});";
                public const string GET = $@"select dir.id from directory as dir where dir.workspace = {WSPACE} and dir.parent={PARENT} and dir.name ={NAME} and dir.deleted = 0;";
                public const string GET_BY_CUID = $@"select dir.id from directory as dir where dir.cuid = {CUID} and dir.deleted = 0;";
                public const string GET_DETAILS = $@"select dir.id, dir.cuid as uid, dir.name, dir.display_name, dir.parent, dir.workspace, dir.created, dir.modified
                                                     from directory as dir
                                                     where dir.workspace = {WSPACE} and dir.parent = {PARENT} and dir.name = {NAME} and dir.deleted = 0
                                                     limit 1;";
                public const string GET_DETAILS_BY_CUID = $@"select dir.id, dir.cuid as uid, dir.name, dir.display_name, dir.parent, dir.workspace, dir.created, dir.modified
                                                             from directory as dir
                                                             where dir.cuid = {VALUE} and dir.deleted = 0
                                                             limit 1;";
                public const string GET_DETAILS_BY_ID = $@"select dir.id, dir.cuid as uid, dir.name, dir.display_name, dir.parent, dir.workspace, dir.created, dir.modified
                                                           from directory as dir
                                                           where dir.id = {VALUE} and dir.deleted = 0
                                                           limit 1;";
                public const string COUNT_CHILDREN = $@"select count(*) from directory as dir where dir.workspace = {WSPACE} and dir.parent = {PARENT} and dir.deleted = 0;";
                public const string BROWSE_ITEMS =
                    $@"select *
                       from (
                            select
                                0 as sort_group,
                                'folder' as item_type,
                                dir.id,
                                dir.cuid as uid,
                                dir.display_name,
                                dir.parent as parent_id,
                                dir.created,
                                dir.modified,
                                null as version_id,
                                null as version_cuid,
                                null as version_no,
                                null as version_count,
                                null as version_created,
                                null as size,
                                null as storage_name,
                                null as storage_ref,
                                null as staging_ref,
                                null as flags,
                                null as hash,
                                null as synced_at
                            from directory as dir
                            where dir.workspace = {WSPACE} and dir.parent = {PARENT} and dir.deleted = 0

                            union all

                            select
                                1 as sort_group,
                                'file' as item_type,
                                d.id,
                                d.cuid as uid,
                                coalesce(di.display_name, '') as display_name,
                                d.parent as parent_id,
                                d.created,
                                d.modified,
                                dv.id as version_id,
                                dv.cuid as version_cuid,
                                dv.ver as version_no,
                                latest.version_count,
                                dv.created as version_created,
                                vi.size,
                                vi.storage_name,
                                vi.storage_ref,
                                vi.staging_ref,
                                vi.flags,
                                vi.hash,
                                vi.synced_at
                            from document as d
                            left join doc_info as di on di.file = d.id
                            inner join (
                                select dvi.parent, max(dvi.ver) as max_ver, count(*) as version_count
                                from doc_version as dvi
                                group by dvi.parent
                            ) as latest on latest.parent = d.id
                            inner join doc_version as dv on dv.parent = d.id and dv.ver = latest.max_ver
                            left join version_info as vi on vi.id = dv.id
                            where d.workspace = {WSPACE} and d.parent = {PARENT} and d.deleted = 0
                       ) as browse_items
                       order by browse_items.sort_group asc, browse_items.display_name asc, browse_items.id asc
                       limit {LIMIT_ROWS} offset {OFFSET_ROWS};";
                public const string GET_BY_DOC_VERSION_CUID = $@"SELECT dir.display_name,dir.cuid,dir.name FROM doc_version AS dv
                    JOIN document AS d ON d.id = dv.parent AND d.deleted = 0
                    JOIN directory AS dir ON dir.id = d.parent AND dir.deleted = 0
                    WHERE dv.cuid= {CUID}";
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
                public const string INSERT_INFO = $@"insert into doc_info (file,display_name) values ({PARENT}, {DNAME}) ON DUPLICATE KEY UPDATE display_name = VALUES(display_name);";
                public const string GET_BY_PARENT = $@"select doc.id from document as doc where doc.parent= {PARENT} and doc.name = {NAME} and doc.deleted = 0;";
                public const string GET_BY_CUID = $@"select doc.id from document as doc where doc.cuid = {CUID} and doc.deleted = 0;";
                public const string COUNT_BY_DIRECTORY = $@"select count(*) from document as doc where doc.workspace = {WSPACE} and doc.parent = {PARENT} and doc.deleted = 0;";
                public const string GET_DETAILS_BY_ID =
                    $@"select
                            d.id as document_id,
                            d.cuid as document_cuid,
                            d.workspace as workspace_id,
                            dir.id as directory_id,
                            dir.cuid as directory_cuid,
                            dir.display_name as directory_name,
                            dir.parent as directory_parent_id,
                            coalesce(di.display_name, '') as display_name
                       from document as d
                       left join doc_info as di on di.file = d.id
                       inner join directory as dir on dir.id = d.parent and dir.deleted = 0
                       where d.id = {ID} and d.deleted = 0
                       limit 1;";
                public const string GET_BY_NAME = $@"SELECT dv.id FROM document AS dv
                        INNER JOIN
                            (SELECT ns.id FROM name_store AS ns
                            INNER JOIN ( SELECT vin.id FROM vault AS vin WHERE vin.name = {NAME}) AS v ON v.id = ns.name
                            INNER JOIN extension AS ext ON ext.id = ns.extension
                            WHERE ext.name = {EXT}) AS ons ON ons.id = dv.name
                        INNER join
                            (select dir.id from directory as dir
                            where dir.workspace = {WSPACE} and dir.parent={PARENT} and dir.name ={DIRNAME} and dir.deleted = 0) AS odir ON odir.id = dv.parent
                        WHERE dv.deleted = 0";
            }
            
            public class DOCVERSION {
                public const string EXISTS = $@"select dv.id , dv.cuid as uid from doc_version as dv where dv.parent = {PARENT} and dv.ver = {VERSION};";
                public const string EXISTS_BY_CUID = $@"select dv.id from doc_version as dv where dv.cuid = {CUID};";
                public const string EXISTS_BY_ID = $@"select 1 from doc_version as dv where dv.id = {ID};";
                public const string INSERT = $@"insert ignore into doc_version (parent,ver) values({PARENT},{VERSION});";
                public const string FIND_LATEST = $@"select MAX(dv.ver) from doc_version as dv where dv.parent = {PARENT};";
                public const string GET_DOCUMENT_ID_BY_VERSION_ID = $@"select dv.parent from doc_version as dv where dv.id = {VALUE} limit 1;";
                public const string GET_DOCUMENT_ID_BY_VERSION_CUID = $@"select dv.parent from doc_version as dv where dv.cuid = {VALUE} limit 1;";

                // Writes -> storage_name/storage_ref/size/hash/synced_at
                // hash and synced_at are nullable — pass DBNull.Value when not available.
                public const string INSERT_INFO =
                    $@"insert into version_info (id, storage_name, storage_ref, size, hash, synced_at)
                       values({ID},{SAVENAME},{PATH},{SIZE},{HASH},{SYNCED_AT})
                       ON DUPLICATE KEY UPDATE
                            storage_name = VALUES(storage_name),
                            storage_ref = VALUES(storage_ref),
                            size = VALUES(size),
                            hash = COALESCE(VALUES(hash), hash),
                            synced_at = COALESCE(VALUES(synced_at), synced_at);";

                // Aliases: storage_name→saveas_name, storage_ref→path, staging_ref→staging_path (backward compat with PopulateFileFromDic)
                public const string GET_INFO =
                    $@"select
                            id,
                            storage_name as saveas_name,
                            storage_ref as path,
                            staging_ref as staging_path,
                            size,
                            hash,
                            synced_at,
                            metadata,
                            flags
                       from version_info
                       where id = {ID};";

                public const string GET_FULL_BY_CUID =
                    $@"SELECT
                            dv.id,
                            dv.cuid as uid,
                            dv.created,
                            dv.ver,
                            vi.storage_ref as path,
                            vi.size,
                            vi.storage_name as saveas_name,
                            vi.staging_ref as staging_path,
                            vi.hash,
                            vi.synced_at,
                            vi.flags,
                            vi.metadata,
                            di.display_name as dname
                      FROM doc_version AS dv
                      INNER JOIN version_info AS vi ON vi.id = dv.id
                      LEFT JOIN doc_info as di on di.file = dv.parent
                      WHERE dv.cuid = {VALUE};";

                public const string GET_FULL_BY_ID =
                    $@"SELECT
                            dv.id,
                            dv.cuid as uid,
                            dv.created,
                            dv.ver,
                            vi.storage_ref as path,
                            vi.size,
                            vi.storage_name as saveas_name,
                            vi.staging_ref as staging_path,
                            vi.hash,
                            vi.synced_at,
                            vi.flags,
                            vi.metadata,
                            di.display_name as dname
                      FROM doc_version AS dv
                      INNER JOIN version_info AS vi ON vi.id = dv.id
                      LEFT JOIN doc_info as di on di.file = dv.parent
                      WHERE dv.id = {VALUE};";

                public const string GET_LATEST_BY_PARENT =
                    $@"SELECT
                            dv.id,
                            dv.cuid as uid,
                            dv.created,
                            dv.ver,
                            vi.storage_ref as path,
                            vi.size,
                            vi.storage_name as saveas_name,
                            vi.staging_ref as staging_path,
                            vi.hash,
                            vi.synced_at,
                            vi.flags,
                            vi.metadata,
                            di.display_name as dname
                      FROM doc_version AS dv
                      INNER JOIN (select MAX(dvi.ver) AS ver from doc_version as dvi where dvi.parent = {PARENT}) AS dvo ON dvo.ver = dv.ver
                      INNER JOIN version_info AS vi ON vi.id = dv.id
                      LEFT JOIN doc_info as di on di.file = {PARENT}
                      WHERE dv.parent = {PARENT};";

                public const string GET_ALL_BY_PARENT =
                    $@"select
                            dv.id as version_id,
                            dv.cuid as version_cuid,
                            dv.ver as version_no,
                            dv.created as version_created,
                            vi.size,
                            vi.storage_name,
                            vi.storage_ref,
                            vi.staging_ref,
                            vi.flags,
                            vi.hash,
                            vi.synced_at,
                            vi.metadata
                       from doc_version as dv
                       left join version_info as vi on vi.id = dv.id
                       where dv.parent = {PARENT}
                       order by dv.ver desc;";

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
        }
    }
}
