using Haley.Models;
using Haley.Abstractions;
using System;
using System.Threading.Tasks;
using static Haley.Internal.IndexingConstant;
using static Haley.Internal.IndexingQueries;

namespace Haley.Utils {
    public partial class MariaDBIndexing {

        public async Task<long> UpsertProvider(string name, string description = null) {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            await EnsureValidation();

            await _agw.NonQuery(new AdapterArgs(_key) { Query = PROVIDER.UPSERT }, (NAME, name), (DESCRIPTION, description));
            var id = await _agw.Scalar(new AdapterArgs(_key) { Query = PROVIDER.EXISTS }, (NAME, name));
            if (id == null || !long.TryParse(id.ToString(), out var pid) || pid < 1)
                throw new Exception($@"Unable to upsert provider '{name}'");
            return pid;
        }

        public async Task<long> UpsertProfile(string name) {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            await EnsureValidation();

            await _agw.NonQuery(new AdapterArgs(_key) { Query = PROFILE.UPSERT }, (NAME, name));
            var id = await _agw.Scalar(new AdapterArgs(_key) { Query = PROFILE.EXISTS }, (NAME, name));
            if (id == null || !long.TryParse(id.ToString(), out var pfid) || pfid < 1)
                throw new Exception($@"Unable to upsert profile '{name}'");
            return pfid;
        }

        public async Task<long> UpsertProfileInfo(
            int profileId,
            int version,
            int mode,
            int? storageProviderId,
            int? stagingProviderId,
            string metadataJson
        ) {
            if (profileId < 1) throw new ArgumentException("profileId must be > 0");
            if (version < 1) throw new ArgumentException("version must be > 0");
            if (metadataJson == null) throw new ArgumentNullException(nameof(metadataJson)); // SQL says NOT NULL
            await EnsureValidation();

            await _agw.NonQuery(
                new AdapterArgs(_key) { Query = PROFILE_INFO.UPSERT },
                (PROFILE_ID, profileId),
                (VERSION, version),
                (MODE, mode),
                (STORAGE_PROVIDER, storageProviderId.HasValue ? storageProviderId.Value : (object)DBNull.Value),
                (STAGING_PROVIDER, stagingProviderId.HasValue ? stagingProviderId.Value : (object)DBNull.Value),
                (METADATA, metadataJson)
            );

            var id = await _agw.Scalar(new AdapterArgs(_key) { Query = PROFILE_INFO.EXISTS }, (PROFILE_ID, profileId), (VERSION, version));
            if (id == null || !long.TryParse(id.ToString(), out var piid) || piid < 1)
                throw new Exception($@"Unable to upsert profile_info for profileId={profileId}, version={version}");
            return piid;
        }

        public async Task<bool> SetModuleStorageProfile(string moduleCuid, int profileId) {
            if (string.IsNullOrWhiteSpace(moduleCuid)) throw new ArgumentNullException(nameof(moduleCuid));
            if (profileId < 1) throw new ArgumentException("profileId must be > 0");

            await EnsureValidation();
            await _agw.NonQuery(
                new AdapterArgs(_key) { Query = MODULE.UPDATE_STORAGE_PROFILE_BY_CUID },
                (CUID, moduleCuid),
                (STORAGE_PROFILE, profileId)
            );
            return true;
        }
    }
}
