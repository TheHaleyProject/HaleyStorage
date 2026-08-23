using static Haley.Internal.IndexingConstant;

namespace Haley.Internal {
    internal partial class IndexingQueries {
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
    }
}
