using static Haley.Internal.IndexingConstant;

namespace Haley.Internal {
    internal partial class IndexingQueries {
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
    }
}
