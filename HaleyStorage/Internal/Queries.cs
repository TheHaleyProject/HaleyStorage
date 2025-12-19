using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml.Linq;
using static Haley.Internal.IndexingConstant;

namespace Haley.Internal {
    internal class IndexingQueries {
        public class GENERAL {
            public const string SCHEMA_EXISTS = $@"select 1 from information_schema.schemata where schema_name = {NAME};";
        }
        public class CLIENT {
            public const string EXISTS = $@"select c.id from client as c where c.name = {NAME} LIMIT 1;";
            public const string UPSERTKEYS = $@"insert into client_keys (client,signing,encrypt,password) values ({ID},{SIGNKEY},{ENCRYPTKEY},{PASSWORD}) ON DUPLICATE KEY UPDATE signing =  VALUES(signing), encrypt = VALUES(encrypt), password = VALUES(password);";
            public const string UPSERT = $@"insert into client (name,display_name, guid,path) values ({NAME},{DNAME},{GUID},{PATH}) ON DUPLICATE KEY UPDATE display_name = VALUES(display_name), path = VALUES(path);";
            public const string UPDATE = $@"update client set display_name = {DNAME}, path = {PATH} where id = {ID};";
            public const string GETKEYS = $@"select * from client_keys as c where c.client = {ID} LIMIT 1;";
        }

        public class MODULE {
            public const string EXISTS = $@"select m.id from module as m where m.name = {NAME} and m.parent = {PARENT} LIMIT 1;";
            public const string EXISTS_BY_CUID = $@"select m.id from module as m where m.cuid = {CUID} LIMIT 1;";
            public const string UPSERT = $@"insert into module (parent,name, display_name,guid,path,cuid) values ({PARENT}, {NAME},{DNAME},{GUID},{PATH},{CUID}) ON DUPLICATE KEY UPDATE display_name = VALUES(display_name), path = VALUES(path);";
            public const string UPDATE = $@"update module set display_name = {DNAME}, path = {PATH} where id = {ID};";

            public const string UPDATE_STORAGE_PROFILE_BY_ID = $@"update module set storage_profile = {PROFILE_ID} where id = {ID};";
            public const string UPDATE_STORAGE_PROFILE_BY_CUID = $@"update module set storage_profile = {PROFILE_ID} where cuid = {CUID};";
        }

        public class WORKSPACE {
            public const string EXISTS = $@"select ws.id from workspace as ws where ws.name = {NAME} and ws.parent = {PARENT} LIMIT 1;";
            public const string EXISTS_BY_CUID = $@"select ws.id from workspace as ws where ws.cuid = {CUID} LIMIT 1;";
            public const string UPSERT = $@"insert into workspace (parent,name, display_name,guid,path,cuid,control_mode,parse_mode) values ({PARENT}, {NAME},{DNAME},{GUID},{PATH},{CUID},{CONTROLMODE},{PARSEMODE}) ON DUPLICATE KEY UPDATE display_name = VALUES(display_name), path = VALUES(path),control_mode=VALUES(control_mode),parse_mode=VALUES(parse_mode);";
            public const string UPDATE = $@"update workspace set display_name = {DNAME}, path = {PATH},control_mode={CONTROLMODE},parse_mode={PARSEMODE} where id = {ID};";
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
                public const string EXISTS = $@"select dir.id, dir.cuid as uid from directory as dir where dir.workspace = {WSPACE} and dir.parent = {PARENT} and dir.name = {NAME};";
                public const string EXISTS_BY_CUID = $@"select dir.id, dir.cuid as uid from directory as dir where dir.cuid = {VALUE};";
                public const string EXISTS_BY_ID = $@"select dir.id, dir.cuid as uid from directory as dir where dir.id = {VALUE};";
                public const string INSERT = $@"insert ignore into directory (workspace,parent,name,display_name) values ({WSPACE},{PARENT},{NAME},{DNAME});";
                public const string GET = $@"select dir.id from directory as dir where dir.workspace = {WSPACE} and dir.parent={PARENT} and dir.name ={NAME};";
                public const string GET_BY_CUID = $@"select dir.id from directory as dir where dir.cuid = {CUID};";
                public const string GET_BY_DOC_VERSION_CUID = $@"SELECT dir.display_name,dir.cuid,dir.name FROM doc_version AS dv
                    JOIN document AS d ON d.id = dv.parent
                    JOIN directory AS dir ON dir.id = d.parent
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
                public const string EXISTS = $@"select doc.id , doc.cuid as uid from document as doc where doc.parent = {PARENT} and doc.name = {NAME};";
                public const string EXISTS_BY_CUID = $@"select doc.id from document as doc where doc.cuid = {CUID};";
                public const string INSERT = $@"insert ignore into document (workspace,parent,name) values ({WSPACE},{PARENT},{NAME});";
                public const string INSERT_INFO = $@"insert into doc_info (file,display_name) values ({PARENT}, {DNAME}) ON DUPLICATE KEY UPDATE display_name = VALUES(display_name);";
                public const string GET_BY_PARENT = $@"select doc.id from document as doc where doc.parent= {PARENT} and doc.name = {NAME};";
                public const string GET_BY_CUID = $@"select doc.id from document as doc where doc.cuid = {CUID};";
                public const string GET_BY_NAME = $@"SELECT dv.id FROM document AS dv
                        INNER JOIN 
                            (SELECT ns.id FROM name_store AS ns 
                            INNER JOIN ( SELECT vin.id FROM vault AS vin WHERE vin.name = {NAME}) AS v ON v.id = ns.name
                            INNER JOIN extension AS ext ON ext.id = ns.extension
                            WHERE ext.name = {EXT}) AS ons ON ons.id = dv.name
                        INNER join
                            (select dir.id from directory as dir 
                            where dir.workspace = {WSPACE} and dir.parent={PARENT} and dir.name ={DIRNAME}) AS odir ON odir.id = dv.parent";
            }
            
            public class DOCVERSION {
                public const string EXISTS = $@"select dv.id , dv.cuid as uid from doc_version as dv where dv.parent = {PARENT} and dv.ver = {VERSION};";
                public const string EXISTS_BY_CUID = $@"select dv.id from doc_version as dv where dv.cuid = {CUID};";
                public const string EXISTS_BY_ID = $@"select 1 from doc_version as dv where dv.id = {ID};";
                public const string INSERT = $@"insert ignore into doc_version (parent,ver) values({PARENT},{VERSION});";
                public const string FIND_LATEST = $@"select MAX(dv.ver) from doc_version as dv where dv.parent = {PARENT};";

                // UPDATED: version_info column rename. Keep old param names (SAVENAME/PATH/SIZE) to avoid touching callers.
                // Writes -> storage_name/storage_path/size
                public const string INSERT_INFO =
                    $@"insert into version_info (id, storage_name, storage_path, size)
                       values({ID},{SAVENAME},{PATH},{SIZE})
                       ON DUPLICATE KEY UPDATE
                            storage_name = VALUES(storage_name),
                            storage_path = VALUES(storage_path),
                            size = VALUES(size);";

                // UPDATED: select with aliases so old readers still see `path` and `saveas_name`
                public const string GET_INFO =
                    $@"select
                            id,
                            storage_name as saveas_name,
                            storage_path as path,
                            staging_path,
                            size,
                            metadata,
                            flags
                       from version_info
                       where id = {ID};";

                // UPDATED: aliases used (`storage_path as path`)
                public const string GET_FULL_BY_CUID =
                    $@"SELECT
                            dv.id,
                            dv.cuid as uid,
                            dv.created,
                            dv.ver,
                            vi.storage_path as path,
                            vi.size,
                            vi.storage_name as saveas_name,
                            vi.staging_path,
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
                            vi.storage_path as path,
                            vi.size,
                            vi.storage_name as saveas_name,
                            vi.staging_path,
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
                            vi.storage_path as path,
                            vi.size,
                            vi.storage_name as saveas_name,
                            vi.staging_path,
                            vi.flags,
                            vi.metadata,
                            di.display_name as dname
                      FROM doc_version AS dv
                      INNER JOIN (select MAX(dvi.ver) AS ver from doc_version as dvi where dvi.parent = {PARENT}) AS dvo ON dvo.ver = dv.ver
                      INNER JOIN version_info AS vi ON vi.id = dv.id
                      LEFT JOIN doc_info as di on di.file = {PARENT}
                      WHERE dv.parent = {PARENT};";

                // NEW: optional extended updates (only call when you actually want to set these)
                public const string UPDATE_INFO_EXT =
                    $@"update version_info
                       set staging_path = {STAGINGPATH},
                           metadata     = {METADATA},
                           flags        = {FLAGS}
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
                    $@"insert into chunked_files (id, part, size)
                       values ({ID},{PART},{FILESIZE_MB})
                       ON DUPLICATE KEY UPDATE
                            size = VALUES(size),
                            uplodaed = current_timestamp();";

                public const string MARK_COMPLETED =
                    $@"update chunk_info set is_completed = b'1' where id = {ID};";
            }
        }
    }
}
