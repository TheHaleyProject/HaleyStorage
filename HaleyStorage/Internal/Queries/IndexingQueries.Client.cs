using static Haley.Internal.IndexingConstant;

namespace Haley.Internal {
    internal partial class IndexingQueries {
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
    }
}
