using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Haley.Services {

    /// <summary>
    /// Partial class — provider resolution and provider configuration.
    /// Resolution order (writes): workspace profile_info_id → module profile_info_id → workspace key → module key → default.
    /// Resolution order (reads): version_info.profile_info_id → workspace override → module → default.
    /// When a file version carries <c>version_info.profile_info_id</c>, that profile_info always wins on reads,
    /// ensuring old files are never reinterpreted through a changed module/workspace profile.
    /// </summary>
    public partial class StorageCoordinator : IStorageCoordinator {

        // ── Profile-info cache ────────────────────────────────────────────────
        // Keyed by profile_info.id → (storageProviderKey, stagingProviderKey, mode).
        // Populated lazily on the first resolution for each profile_info_id.
        readonly ConcurrentDictionary<long, (string storageKey, string stagingKey, VaultProfileMode mode)>
            _profileInfoCache = new();

        // ── Primary provider ──────────────────────────────────────────────────

        /// <summary>
        /// Returns the effective primary <see cref="IStorageProvider"/> for the request.
        /// Resolution order:
        ///   1. file's stored profile_info_id (version_info.profile_info_id) — preserves history
        ///   2. workspace StorageProviderKey — workspace-level override
        ///   3. module StorageProviderKey    — module default
        ///   4. global default provider
        /// </summary>
        internal IStorageProvider ResolveProvider(IVaultReadRequest request) {
            // Priority 1: file's stored profile_info_id (read path only — write path stamps it after resolution)
            if (request is IVaultFileReadRequest fr && fr.File is StorageFileRoute sfr && sfr.ProfileInfoId > 0) {
                var p = ResolveByProfileInfoId(sfr.ProfileInfoId);
                if (p != null) return p;
            }
            if (Indexer != null) {
                if (TryResolveWorkspaceProvider(request, out var wp)) return wp;
                if (request?.Scope?.Module != null)
                    return ResolveProvider(request.Scope.Module.Cuid.ToString("N"));
            }
            return GetDefaultProvider();
        }

        /// <summary>Overload when the module CUID string is already known (module-level only).</summary>
        internal IStorageProvider ResolveProvider(string moduleCuid) {
            if (!string.IsNullOrWhiteSpace(moduleCuid)
                && Indexer != null
                && Indexer.TryGetComponentInfo<VaultModule>(moduleCuid, out VaultModule m)
                && !string.IsNullOrWhiteSpace(m?.StorageProviderKey)
                && _providers.TryGetValue(m.StorageProviderKey, out var p))
                return p;
            return GetDefaultProvider();
        }

        /// <inheritdoc/>
        public IStorageProvider GetPrimaryProvider(IVaultReadRequest request) => ResolveProvider(request);

        /// <inheritdoc/>
        public IStorageProvider GetStagingProvider(IVaultReadRequest request) => ResolveStagingProvider(request);

        // ── Staging provider ──────────────────────────────────────────────────

        /// <summary>
        /// Returns the staging <see cref="IStorageProvider"/> for the request, or <c>null</c>
        /// if staging is not configured or is explicitly disabled.
        /// Resolution order:
        ///   1. file's stored profile_info_id → use its staging provider (null = staging disabled for that profile)
        ///   2. workspace has StorageProviderKey set → workspace owns its complete profile;
        ///      use workspace StagingProviderKey (null/empty = staging explicitly disabled, do NOT fall to module).
        ///   3. workspace has no StorageProviderKey → inherit module staging provider.
        /// </summary>
        internal IStorageProvider ResolveStagingProvider(IVaultReadRequest request) {
            // Priority 1: file's stored profile
            if (request is IVaultFileReadRequest fr && fr.File is StorageFileRoute sfr && sfr.ProfileInfoId > 0) {
                TryGetProfileInfoCached(sfr.ProfileInfoId, out _, out var stagingKey, out _);
                if (!string.IsNullOrWhiteSpace(stagingKey) && _providers.TryGetValue(stagingKey, out var pp))
                    return pp;
                // Profile exists but no staging key = staging disabled for this profile; don't fall through
                if (_profileInfoCache.ContainsKey(sfr.ProfileInfoId)) return null;
            }
            if (Indexer != null && TryGetWorkspace(request, out VaultWorkSpace ws)
                && !string.IsNullOrWhiteSpace(ws.StorageProviderKey)) {
                // Workspace owns its full profile — honor its staging setting; empty = disabled.
                if (!string.IsNullOrWhiteSpace(ws.StagingProviderKey)
                    && _providers.TryGetValue(ws.StagingProviderKey, out var wsp))
                    return wsp;
                return null; // workspace explicitly has no staging — do not fall through to module
            }
            // No workspace profile override → inherit from module.
            if (request?.Scope?.Module != null && Indexer != null)
                return ResolveStagingProvider(request.Scope.Module.Cuid.ToString("N"));
            return null;
        }

        /// <summary>Overload when the module CUID string is already known (module-level only).</summary>
        internal IStorageProvider ResolveStagingProvider(string moduleCuid) {
            if (!string.IsNullOrWhiteSpace(moduleCuid)
                && Indexer != null
                && Indexer.TryGetComponentInfo<VaultModule>(moduleCuid, out VaultModule m)
                && !string.IsNullOrWhiteSpace(m?.StagingProviderKey)
                && _providers.TryGetValue(m.StagingProviderKey, out var sp))
                return sp;
            return null;
        }

        // ── Profile mode ──────────────────────────────────────────────────────

        /// <summary>
        /// Returns the upload routing mode for the request.
        /// Resolution order:
        ///   1. file's stored profile_info_id (read path) → use its mode
        ///   2. workspace ProfileMode (when workspace has an explicit StorageProviderKey)
        ///   3. module ProfileMode → DirectSave.
        /// </summary>
        internal VaultProfileMode ResolveProfileMode(IVaultReadRequest request) {
            // Priority 1: file's stored profile
            if (request is IVaultFileReadRequest fr && fr.File is StorageFileRoute sfr && sfr.ProfileInfoId > 0
                && TryGetProfileInfoCached(sfr.ProfileInfoId, out _, out _, out var pMode))
                return pMode;
            if (Indexer != null && TryGetWorkspace(request, out VaultWorkSpace ws)
                && !string.IsNullOrWhiteSpace(ws.StorageProviderKey))
                return ws.ProfileMode;
            return ResolveProfileMode(request?.Scope?.Module?.Cuid.ToString("N"));
        }

        /// <summary>Module-level profile mode fallback. Returns DirectSave when not configured.</summary>
        internal VaultProfileMode ResolveProfileMode(string moduleCuid) {
            if (!string.IsNullOrWhiteSpace(moduleCuid)
                && Indexer != null
                && Indexer.TryGetComponentInfo<VaultModule>(moduleCuid, out VaultModule m))
                return m.ProfileMode;
            return VaultProfileMode.DirectSave;
        }

        // ── Workspace resolution helpers ──────────────────────────────────────

        bool TryGetWorkspace(IVaultReadRequest request, out VaultWorkSpace ws) {
            ws = null;
            var wsCuid = request?.Scope?.Workspace?.Cuid.ToString("N");
            return !string.IsNullOrWhiteSpace(wsCuid)
                && Indexer.TryGetComponentInfo<VaultWorkSpace>(wsCuid, out ws)
                && ws != null;
        }

        bool TryResolveWorkspaceProvider(IVaultReadRequest request, out IStorageProvider provider) {
            provider = null;
            return TryGetWorkspace(request, out VaultWorkSpace ws)
                && !string.IsNullOrWhiteSpace(ws.StorageProviderKey)
                && _providers.TryGetValue(ws.StorageProviderKey, out provider);
        }

        bool TryResolveWorkspaceStagingProvider(IVaultReadRequest request, out IStorageProvider provider) {
            provider = null;
            return TryGetWorkspace(request, out VaultWorkSpace ws)
                && !string.IsNullOrWhiteSpace(ws.StagingProviderKey)
                && _providers.TryGetValue(ws.StagingProviderKey, out provider);
        }

        // ── Profile-info resolution helpers ──────────────────────────────────

        /// <summary>
        /// Tries to retrieve provider keys and mode for a <c>profile_info.id</c> from the local cache.
        /// On a cache miss, fetches from the DB via the indexer and populates the cache.
        /// Returns <c>true</c> when the profile was found (even if both provider keys are empty).
        /// </summary>
        bool TryGetProfileInfoCached(long profileInfoId, out string storageKey, out string stagingKey, out VaultProfileMode mode) {
            storageKey = null; stagingKey = null; mode = VaultProfileMode.DirectSave;
            if (profileInfoId < 1 || Indexer == null) return false;

            if (_profileInfoCache.TryGetValue(profileInfoId, out var cached)) {
                storageKey = cached.storageKey; stagingKey = cached.stagingKey; mode = cached.mode;
                return true;
            }

            // Cache miss — fetch from DB (sync, expected to be rare).
            var fb = Indexer.GetProfileInfo(profileInfoId).GetAwaiter().GetResult();
            if (fb?.Status != true || fb.Result is not Dictionary<string, object> row)
                return false;

            var sk = row.TryGetValue("storage_provider_key", out var spk) ? spk?.ToString() ?? string.Empty : string.Empty;
            var stk = row.TryGetValue("staging_provider_key", out var spv) ? spv?.ToString() ?? string.Empty : string.Empty;
            var m2 = VaultProfileMode.DirectSave;
            if (row.TryGetValue("mode", out var mv) && int.TryParse(mv?.ToString(), out var mInt))
                m2 = (VaultProfileMode)mInt;

            var entry = (sk, stk, m2);
            _profileInfoCache.TryAdd(profileInfoId, entry);
            storageKey = sk; stagingKey = stk; mode = m2;
            return true;
        }

        /// <summary>
        /// Resolves the primary provider from a specific <c>profile_info.id</c>.
        /// Returns <c>null</c> when the profile is not found or its storage key is not registered.
        /// </summary>
        IStorageProvider ResolveByProfileInfoId(long profileInfoId) {
            if (!TryGetProfileInfoCached(profileInfoId, out var storageKey, out _, out _))
                return null;
            if (!string.IsNullOrWhiteSpace(storageKey) && _providers.TryGetValue(storageKey, out var p))
                return p;
            return null;
        }

        // ── Promotion worker helper ────────────────────────────────────────────

        /// <summary>
        /// Returns the primary provider, staging provider, and profile mode for a specific
        /// <c>profile_info.id</c>. Falls back to the module-level configuration when the
        /// profile is not found or when <paramref name="profileInfoId"/> is 0.
        /// Used internally by background promotion services.
        /// </summary>
        internal (IStorageProvider primary, IStorageProvider staging, VaultProfileMode mode) GetProvidersForProfile(long profileInfoId, string moduleCuid) {
            if (profileInfoId > 0 && TryGetProfileInfoCached(profileInfoId, out var sk, out var stk, out var pMode)) {
                var primary  = (!string.IsNullOrEmpty(sk)  && _providers.TryGetValue(sk,  out var pp)) ? pp : GetDefaultProvider();
                var staging  = (!string.IsNullOrEmpty(stk) && _providers.TryGetValue(stk, out var sp)) ? sp : null;
                return (primary, staging, pMode);
            }
            return (ResolveProvider(moduleCuid), ResolveStagingProvider(moduleCuid), ResolveProfileMode(moduleCuid));
        }

        // ── Runtime provider configuration ────────────────────────────────────

        /// <inheritdoc/>
        public bool ConfigureModuleProviders(string moduleCuid, string storageProviderKey, string stagingProviderKey = null, VaultProfileMode mode = VaultProfileMode.DirectSave, long profileInfoId = 0) {

            if (string.IsNullOrWhiteSpace(moduleCuid)) return false;
            if (Indexer == null || !Indexer.TryGetComponentInfo<VaultModule>(moduleCuid, out VaultModule m) || m == null)
                return false;

            if (!string.IsNullOrWhiteSpace(storageProviderKey) && !_providers.ContainsKey(storageProviderKey))
                throw new ArgumentException($"Provider key '{storageProviderKey}' is not registered. Call AddProvider first.", nameof(storageProviderKey));
            if (!string.IsNullOrWhiteSpace(stagingProviderKey) && !_providers.ContainsKey(stagingProviderKey))
                throw new ArgumentException($"Staging provider key '{stagingProviderKey}' is not registered. Call AddProvider first.", nameof(stagingProviderKey));

            // VaultModule is a reference type — mutating updates the indexer cache in-place.
            m.StorageProviderKey = storageProviderKey ?? string.Empty;
            m.StagingProviderKey = stagingProviderKey ?? string.Empty;
            m.ProfileMode = mode;
            if (profileInfoId > 0) m.ProfileInfoId = profileInfoId;
            return true;
        }

        /// <inheritdoc/>
        public bool ConfigureWorkspaceProviders(string workspaceCuid, string storageProviderKey, string stagingProviderKey = null, VaultProfileMode mode = VaultProfileMode.DirectSave) {

            if (string.IsNullOrWhiteSpace(workspaceCuid)) return false;
            if (Indexer == null || !Indexer.TryGetComponentInfo<VaultWorkSpace>(workspaceCuid, out VaultWorkSpace ws) || ws == null)
                return false;

            if (!string.IsNullOrWhiteSpace(storageProviderKey) && !_providers.ContainsKey(storageProviderKey))
                throw new ArgumentException($"Provider key '{storageProviderKey}' is not registered. Call AddProvider first.", nameof(storageProviderKey));
            if (!string.IsNullOrWhiteSpace(stagingProviderKey) && !_providers.ContainsKey(stagingProviderKey))
                throw new ArgumentException($"Staging provider key '{stagingProviderKey}' is not registered. Call AddProvider first.", nameof(stagingProviderKey));

            // VaultWorkSpace is a reference type — mutating updates the indexer cache in-place.
            ws.StorageProviderKey = storageProviderKey ?? string.Empty;
            ws.StagingProviderKey = stagingProviderKey ?? string.Empty;
            ws.ProfileMode = mode;
            return true;
        }
    }
}
