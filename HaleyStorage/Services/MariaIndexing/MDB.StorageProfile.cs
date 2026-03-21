using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using System;
using System.Threading.Tasks;
using static Haley.Internal.IndexingConstant;
using static Haley.Internal.IndexingQueries;

namespace Haley.Utils {
    /// <summary>
    /// Partial class — storage provider and profile management.
    /// Manages the <c>provider</c>, <c>profile</c>, and <c>profile_info</c> tables
    /// in the core DB, and links profiles to modules and workspaces.
    /// </summary>
    public partial class MariaDBIndexing {

        /// <summary>
        /// Inserts or updates a provider record in the core DB's <c>provider</c> table.
        /// Returns the provider's numeric ID.
        /// </summary>
        public async Task<long> UpsertProvider(string name, string description = null) {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            await EnsureValidation();
            await _agw.ExecAsync(_key, PROVIDER.UPSERT, default, (NAME, name), (DESCRIPTION, description));
            var id = await _agw.ScalarAsync<long>(_key, PROVIDER.EXISTS, default, (NAME, name));
            if (id < 1) throw new Exception($"Unable to upsert provider '{name}'");
            return id;
        }

        /// <summary>
        /// Inserts or updates a profile record in the core DB's <c>profile</c> table.
        /// Returns the profile's numeric ID.
        /// </summary>
        public async Task<long> UpsertProfile(string name) {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            await EnsureValidation();
            await _agw.ExecAsync(_key, PROFILE.UPSERT, default, (NAME, name));
            var id = await _agw.ScalarAsync<long>(_key, PROFILE.EXISTS, default, (NAME, name));
            if (id < 1) throw new Exception($"Unable to upsert profile '{name}'");
            return id;
        }

        /// <summary>
        /// Inserts or updates a <c>profile_info</c> row that links a versioned profile to its
        /// primary storage provider, staging provider, profile mode, and arbitrary metadata JSON.
        /// Returns the <c>profile_info</c> numeric ID.
        /// </summary>
        /// <param name="mode">Integer representation of the <see cref="StorageProfileMode"/> enum value.</param>
        /// <param name="storageProviderId">FK to <c>provider.id</c> for the primary provider; nullable.</param>
        /// <param name="stagingProviderId">FK to <c>provider.id</c> for the staging provider; nullable.</param>
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
            if (metadataJson == null) throw new ArgumentNullException(nameof(metadataJson));
            await EnsureValidation();
            await _agw.ExecAsync(_key, PROFILE_INFO.UPSERT, default,
                (PROFILE_ID,        profileId),
                (VERSION,           version),
                (MODE,              mode),
                (STORAGE_PROVIDER,  (object?)storageProviderId),
                (STAGING_PROVIDER,  (object?)stagingProviderId),
                (METADATA,          metadataJson));
            var id = await _agw.ScalarAsync<long>(_key, PROFILE_INFO.EXISTS, default, (PROFILE_ID, profileId), (VERSION, version));
            if (id < 1) throw new Exception($"Unable to upsert profile_info for profileId={profileId}, version={version}");
            return id;
        }

        /// <summary>
        /// Associates a module with a storage profile by updating <c>module.storage_profile</c>.
        /// Returns <c>true</c> on success.
        /// </summary>
        public async Task<bool> SetModuleStorageProfile(string moduleCuid, int profileId) {
            if (string.IsNullOrWhiteSpace(moduleCuid)) throw new ArgumentNullException(nameof(moduleCuid));
            if (profileId < 1) throw new ArgumentException("profileId must be > 0");
            await EnsureValidation();
            await _agw.ExecAsync(_key, MODULE.UPDATE_STORAGE_PROFILE_BY_CUID, default,
                (CUID,          moduleCuid),
                (PROFILE_ID,    profileId));
            return true;
        }

        /// <summary>
        /// Associates a workspace with a storage profile by updating <c>workspace.storage_profile</c>,
        /// then immediately hydrates the in-memory cache entry with the resolved provider keys and mode
        /// so <see cref="StorageCoordinator"/> picks them up without a restart.
        /// Returns <c>true</c> on success.
        /// </summary>
        public async Task<bool> SetWorkspaceStorageProfile(string workspaceCuid, int profileInfoId) {
            if (string.IsNullOrWhiteSpace(workspaceCuid)) throw new ArgumentNullException(nameof(workspaceCuid));
            if (profileInfoId < 1) throw new ArgumentException("profileInfoId must be > 0");
            await EnsureValidation();
            await _agw.ExecAsync(_key, WORKSPACE.UPDATE_STORAGE_PROFILE_BY_CUID, default,
                (CUID,              workspaceCuid),
                (STORAGE_PROFILE,   profileInfoId));
            await HydrateWorkspaceProfileAsync(workspaceCuid, profileInfoId);
            return true;
        }

        /// <summary>
        /// Startup hydration: loads all workspaces that have a <c>storage_profile</c> set in the DB
        /// and populates <see cref="StorageWorkspace.StorageProviderKey"/>,
        /// <see cref="StorageWorkspace.StagingProviderKey"/>, and <see cref="StorageWorkspace.ProfileMode"/>
        /// on the matching in-memory cache entries.
        /// Call this once at startup after all workspace registrations are complete.
        /// </summary>
        public async Task RehydrateWorkspaceProfilesAsync() {
            await EnsureValidation();
            var rows = await _agw.RowsAsync(_key, WORKSPACE.GET_ALL_PROFILES_WITH_KEYS, default);
            foreach (var row in rows) {
                var cuid = row["cuid"]?.ToString();
                if (string.IsNullOrWhiteSpace(cuid)) continue;
                if (!_cache.TryGetValue(cuid, out var cached) || !(cached is StorageWorkspace ws)) continue;
                ApplyProfileRowToWorkspace(ws, row);
            }
        }

        // ── Private helpers ───────────────────────────────────────────────────

        async Task HydrateWorkspaceProfileAsync(string workspaceCuid, int profileInfoId) {
            var row = await _agw.RowAsync(_key, PROFILE_INFO.GET_WITH_PROVIDER_KEYS, default, (PROFILE_ID, profileInfoId));
            if (row != null && _cache.TryGetValue(workspaceCuid, out var cached) && cached is StorageWorkspace ws)
                ApplyProfileRowToWorkspace(ws, row);
        }

        void ApplyProfileRowToWorkspace(StorageWorkspace ws, DbRow row) {
            ws.StorageProviderKey = row.TryGetValue("storage_provider_key", out var spk) ? spk?.ToString() ?? string.Empty : string.Empty;
            ws.StagingProviderKey = row.TryGetValue("staging_provider_key", out var stk) ? stk?.ToString() ?? string.Empty : string.Empty;
            if (row.TryGetValue("mode", out var mode) && int.TryParse(mode?.ToString(), out var modeInt))
                ws.ProfileMode = (StorageProfileMode)modeInt;
        }
    }
}
