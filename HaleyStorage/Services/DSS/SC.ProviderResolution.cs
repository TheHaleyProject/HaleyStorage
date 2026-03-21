using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;

namespace Haley.Services {

    /// <summary>
    /// Partial class — provider resolution and provider configuration.
    /// Resolution order: workspace override → module → default.
    /// Workspace override is only active when <see cref="StorageWorkspace.StorageProviderKey"/> is set
    /// (populated via <see cref="ConfigureWorkspaceProviders"/> or loaded from the DB profile).
    /// </summary>
    public partial class StorageCoordinator : IStorageCoordinator {

        // ── Primary provider ──────────────────────────────────────────────────

        /// <summary>
        /// Returns the effective primary <see cref="IStorageProvider"/> for the request.
        /// Resolution order: workspace StorageProviderKey → module StorageProviderKey → default.
        /// </summary>
        internal IStorageProvider ResolveProvider(IVaultReadRequest request) {
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
                && Indexer.TryGetComponentInfo<StorageModule>(moduleCuid, out StorageModule m)
                && !string.IsNullOrWhiteSpace(m?.StorageProviderKey)
                && _providers.TryGetValue(m.StorageProviderKey, out var p))
                return p;
            return GetDefaultProvider();
        }

        // ── Staging provider ──────────────────────────────────────────────────

        /// <summary>
        /// Returns the staging <see cref="IStorageProvider"/> for the request, or <c>null</c>
        /// if staging is not configured or is explicitly disabled.
        /// Resolution order:
        ///   — workspace has StorageProviderKey set → workspace owns its complete profile;
        ///     use workspace StagingProviderKey (null/empty = staging explicitly disabled, do NOT fall to module).
        ///   — workspace has no StorageProviderKey → inherit module staging provider.
        /// </summary>
        internal IStorageProvider ResolveStagingProvider(IVaultReadRequest request) {
            if (Indexer != null && TryGetWorkspace(request, out StorageWorkspace ws)
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
                && Indexer.TryGetComponentInfo<StorageModule>(moduleCuid, out StorageModule m)
                && !string.IsNullOrWhiteSpace(m?.StagingProviderKey)
                && _providers.TryGetValue(m.StagingProviderKey, out var sp))
                return sp;
            return null;
        }

        // ── Profile mode ──────────────────────────────────────────────────────

        /// <summary>
        /// Returns the upload routing mode for the request.
        /// Resolution order: workspace ProfileMode (when workspace has an explicit StorageProviderKey)
        /// → module ProfileMode → DirectSave.
        /// Note: workspace ProfileMode is only honoured when StorageProviderKey is also set on the
        /// workspace. To override only the mode while keeping the module's primary provider, set
        /// StorageProviderKey to the same key as the module — this makes the workspace own its full
        /// profile and all three fields (primary, staging, mode) are read from the workspace.
        /// </summary>
        internal StorageProfileMode ResolveProfileMode(IVaultReadRequest request) {
            if (Indexer != null && TryGetWorkspace(request, out StorageWorkspace ws)
                && !string.IsNullOrWhiteSpace(ws.StorageProviderKey))
                return ws.ProfileMode;
            return ResolveProfileMode(request?.Scope?.Module?.Cuid.ToString("N"));
        }

        /// <summary>Module-level profile mode fallback. Returns DirectSave when not configured.</summary>
        internal StorageProfileMode ResolveProfileMode(string moduleCuid) {
            if (!string.IsNullOrWhiteSpace(moduleCuid)
                && Indexer != null
                && Indexer.TryGetComponentInfo<StorageModule>(moduleCuid, out StorageModule m))
                return m.ProfileMode;
            return StorageProfileMode.DirectSave;
        }

        // ── Workspace resolution helpers ──────────────────────────────────────

        bool TryGetWorkspace(IVaultReadRequest request, out StorageWorkspace ws) {
            ws = null;
            var wsCuid = request?.Scope?.Workspace?.Cuid.ToString("N");
            return !string.IsNullOrWhiteSpace(wsCuid)
                && Indexer.TryGetComponentInfo<StorageWorkspace>(wsCuid, out ws)
                && ws != null;
        }

        bool TryResolveWorkspaceProvider(IVaultReadRequest request, out IStorageProvider provider) {
            provider = null;
            return TryGetWorkspace(request, out StorageWorkspace ws)
                && !string.IsNullOrWhiteSpace(ws.StorageProviderKey)
                && _providers.TryGetValue(ws.StorageProviderKey, out provider);
        }

        bool TryResolveWorkspaceStagingProvider(IVaultReadRequest request, out IStorageProvider provider) {
            provider = null;
            return TryGetWorkspace(request, out StorageWorkspace ws)
                && !string.IsNullOrWhiteSpace(ws.StagingProviderKey)
                && _providers.TryGetValue(ws.StagingProviderKey, out provider);
        }

        // ── Runtime provider configuration ────────────────────────────────────

        /// <inheritdoc/>
        public bool ConfigureModuleProviders(string moduleCuid, string storageProviderKey,
            string stagingProviderKey = null, StorageProfileMode mode = StorageProfileMode.DirectSave) {

            if (string.IsNullOrWhiteSpace(moduleCuid)) return false;
            if (Indexer == null || !Indexer.TryGetComponentInfo<StorageModule>(moduleCuid, out StorageModule m) || m == null)
                return false;

            if (!string.IsNullOrWhiteSpace(storageProviderKey) && !_providers.ContainsKey(storageProviderKey))
                throw new ArgumentException($"Provider key '{storageProviderKey}' is not registered. Call AddProvider first.", nameof(storageProviderKey));
            if (!string.IsNullOrWhiteSpace(stagingProviderKey) && !_providers.ContainsKey(stagingProviderKey))
                throw new ArgumentException($"Staging provider key '{stagingProviderKey}' is not registered. Call AddProvider first.", nameof(stagingProviderKey));

            // StorageModule is a reference type — mutating updates the indexer cache in-place.
            m.StorageProviderKey = storageProviderKey ?? string.Empty;
            m.StagingProviderKey = stagingProviderKey ?? string.Empty;
            m.ProfileMode = mode;
            return true;
        }

        /// <inheritdoc/>
        public bool ConfigureWorkspaceProviders(string workspaceCuid, string storageProviderKey,
            string stagingProviderKey = null, StorageProfileMode mode = StorageProfileMode.DirectSave) {

            if (string.IsNullOrWhiteSpace(workspaceCuid)) return false;
            if (Indexer == null || !Indexer.TryGetComponentInfo<StorageWorkspace>(workspaceCuid, out StorageWorkspace ws) || ws == null)
                return false;

            if (!string.IsNullOrWhiteSpace(storageProviderKey) && !_providers.ContainsKey(storageProviderKey))
                throw new ArgumentException($"Provider key '{storageProviderKey}' is not registered. Call AddProvider first.", nameof(storageProviderKey));
            if (!string.IsNullOrWhiteSpace(stagingProviderKey) && !_providers.ContainsKey(stagingProviderKey))
                throw new ArgumentException($"Staging provider key '{stagingProviderKey}' is not registered. Call AddProvider first.", nameof(stagingProviderKey));

            // StorageWorkspace is a reference type — mutating updates the indexer cache in-place.
            ws.StorageProviderKey = storageProviderKey ?? string.Empty;
            ws.StagingProviderKey = stagingProviderKey ?? string.Empty;
            ws.ProfileMode = mode;
            return true;
        }
    }
}
