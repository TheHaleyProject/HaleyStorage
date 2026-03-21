using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
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
        public async Task<long> UpsertProvider(string displayName, string description = null) {
            if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentNullException(nameof(displayName));
            var name = displayName.ToDBName();
            await EnsureValidation();
            var existingId = await _agw.ScalarAsync<long>(_key, PROVIDER.EXISTS, default, (NAME, name));
            if (existingId > 0) {
                await _agw.ExecAsync(_key, PROVIDER.UPDATE, default, (ID, existingId), (DNAME, displayName), (DESCRIPTION, description));
                return existingId;
            }
            await _agw.ExecAsync(_key, PROVIDER.INSERT, default, (NAME, name), (DNAME, displayName), (DESCRIPTION, description));
            var id = await _agw.ScalarAsync<long>(_key, PROVIDER.EXISTS, default, (NAME, name));
            if (id < 1) throw new Exception($"Unable to insert provider '{displayName}'");
            return id;
        }

        /// <summary>
        /// Inserts or updates a profile record in the core DB's <c>profile</c> table.
        /// Returns the profile's numeric ID.
        /// </summary>
        public async Task<long> UpsertProfile(string displayName) {
            if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentNullException(nameof(displayName));
            var name = displayName.ToDBName();
            await EnsureValidation();
            var existingId = await _agw.ScalarAsync<long>(_key, PROFILE.EXISTS, default, (NAME, name));
            if (existingId > 0) {
                await _agw.ExecAsync(_key, PROFILE.UPDATE, default, (ID, existingId), (DNAME, displayName));
                return existingId;
            }
            await _agw.ExecAsync(_key, PROFILE.INSERT, default, (NAME, name), (DNAME, displayName));
            var id = await _agw.ScalarAsync<long>(_key, PROFILE.EXISTS, default, (NAME, name));
            if (id < 1) throw new Exception($"Unable to insert profile '{displayName}'");
            return id;
        }

        /// <summary>
        /// Inserts or updates a <c>profile_info</c> row that links a versioned profile to its
        /// primary storage provider, staging provider, profile mode, and arbitrary metadata JSON.
        /// Returns the <c>profile_info</c> numeric ID.
        /// <para>
        /// Deduplication: a SHA-256 hash of (storageProviderKey, stagingProviderKey, mode, canonicalized metadata)
        /// is computed first. If a row with that hash already exists — regardless of (profile, version) — its id
        /// is returned immediately; no insert or update is performed.
        /// </para>
        /// </summary>
        /// <param name="storageProviderKey">Normalized name key of the primary provider (from <c>provider.name</c>); null = no provider.</param>
        /// <param name="stagingProviderKey">Normalized name key of the staging provider; null = no staging.</param>
        public async Task<long> UpsertProfileInfo(
            int profileId,
            int version,
            int mode,
            string storageProviderKey,
            string stagingProviderKey,
            string metadataJson
        ) {
            if (profileId < 1) throw new ArgumentException("profileId must be > 0");
            if (version < 1) throw new ArgumentException("version must be > 0");
            if (metadataJson == null) throw new ArgumentNullException(nameof(metadataJson));
            await EnsureValidation();

            var hash = ComputeProfileInfoHash(storageProviderKey, stagingProviderKey, mode, metadataJson);

            // Deduplication: if an identical config already exists anywhere, reuse it.
            var hashId = await _agw.ScalarAsync<long>(_key, PROFILE_INFO.EXISTS_BY_HASH, default, (HASH, hash));
            if (hashId > 0) return hashId;

            // Resolve provider IDs from keys.
            long? storageProviderId = null;
            if (!string.IsNullOrWhiteSpace(storageProviderKey)) {
                var sid = await _agw.ScalarAsync<long>(_key, PROVIDER.EXISTS, default, (NAME, storageProviderKey));
                if (sid > 0) storageProviderId = sid;
            }
            long? stagingProviderId = null;
            if (!string.IsNullOrWhiteSpace(stagingProviderKey)) {
                var stid = await _agw.ScalarAsync<long>(_key, PROVIDER.EXISTS, default, (NAME, stagingProviderKey));
                if (stid > 0) stagingProviderId = stid;
            }

            var existingId = await _agw.ScalarAsync<long>(_key, PROFILE_INFO.EXISTS, default, (PROFILE_ID, profileId), (VERSION, version));
            if (existingId > 0) {
                await _agw.ExecAsync(_key, PROFILE_INFO.UPDATE, default,
                    (ID,                existingId),
                    (MODE,              mode),
                    (STORAGE_PROVIDER,  (object?)storageProviderId),
                    (STAGING_PROVIDER,  (object?)stagingProviderId),
                    (METADATA,          metadataJson),
                    (HASH,              hash));
                return existingId;
            }
            await _agw.ExecAsync(_key, PROFILE_INFO.INSERT, default,
                (PROFILE_ID,        profileId),
                (VERSION,           version),
                (MODE,              mode),
                (STORAGE_PROVIDER,  (object?)storageProviderId),
                (STAGING_PROVIDER,  (object?)stagingProviderId),
                (METADATA,          metadataJson),
                (HASH,              hash));
            var id = await _agw.ScalarAsync<long>(_key, PROFILE_INFO.EXISTS, default, (PROFILE_ID, profileId), (VERSION, version));
            if (id < 1) throw new Exception($"Unable to insert profile_info for profileId={profileId}, version={version}");
            return id;
        }

        static string ComputeProfileInfoHash(string storageProviderKey, string stagingProviderKey, int mode, string metadataJson) {
            var canonical = (metadataJson ?? string.Empty).ToCompactJson();
            var input = $"{storageProviderKey ?? "0"}|{stagingProviderKey ?? "0"}|{mode}|{canonical}";
            return input.CreateGUID(HashMethod.Sha256).ToString("N");
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
            if (row.TryGetValue("profile_info_id", out var pid) && long.TryParse(pid?.ToString(), out var pidLong) && pidLong > 0)
                ws.ProfileInfoId = pidLong;
        }

        /// <inheritdoc/>
        public async Task<IFeedback> GetProfileInfo(long profileInfoId) {
            var fb = new Feedback();
            try {
                if (profileInfoId < 1) return fb.SetMessage("profileInfoId must be > 0.");
                var row = await _agw.RowAsync(_key, PROFILE_INFO.GET_WITH_PROVIDER_KEYS, default, (PROFILE_ID, profileInfoId));
                if (row == null || row.Count == 0)
                    return fb.SetMessage($"profile_info id={profileInfoId} not found.");
                return fb.SetStatus(true).SetResult(row);
            } catch (Exception ex) {
                return fb.SetMessage(ex.Message);
            }
        }
    }
}
