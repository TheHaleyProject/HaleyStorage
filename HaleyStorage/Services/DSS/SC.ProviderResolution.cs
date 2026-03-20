using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;

namespace Haley.Services {

    /// <summary>
    /// Partial class — provider resolution and module-level provider configuration.
    /// Resolves primary and staging <see cref="IStorageProvider"/> instances from the module's
    /// <see cref="StorageModule.StorageProviderKey"/> / <see cref="StorageModule.StagingProviderKey"/>,
    /// and exposes <see cref="ConfigureModuleProviders"/> for runtime reconfiguration.
    /// </summary>
    public partial class StorageCoordinator : IStorageCoordinator {

        // ── Primary provider ──────────────────────────────────────────────────

        /// <summary>
        /// Returns the effective primary <see cref="IStorageProvider"/> for the request.
        /// Resolution: StorageModule.StorageProviderKey → registered provider → default.
        /// </summary>
        internal IStorageProvider ResolveProvider(IVaultReadRequest request) {
            if (request?.Scope?.Module != null && Indexer != null)
                return ResolveProvider(request.Scope.Module.Cuid.ToString("N"));
            return GetDefaultProvider();
        }

        /// <summary>Overload when the module CUID string is already known.</summary>
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
        /// if no staging provider is configured for this module.
        /// A null return means staging is not available — fall back to primary.
        /// </summary>
        internal IStorageProvider ResolveStagingProvider(IVaultReadRequest request) {
            if (request?.Scope?.Module != null && Indexer != null)
                return ResolveStagingProvider(request.Scope.Module.Cuid.ToString("N"));
            return null;
        }

        /// <summary>Overload when the module CUID string is already known.</summary>
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
        /// Returns the upload routing mode for the module: DirectSave, StageAndMove,
        /// or StageAndRetainCopy. Defaults to DirectSave when not configured.
        /// </summary>
        internal StorageProfileMode ResolveProfileMode(string moduleCuid) {
            if (!string.IsNullOrWhiteSpace(moduleCuid)
                && Indexer != null
                && Indexer.TryGetComponentInfo<StorageModule>(moduleCuid, out StorageModule m))
                return m.ProfileMode;
            return StorageProfileMode.DirectSave;
        }

        // ── Runtime provider configuration ────────────────────────────────────

        /// <inheritdoc/>
        public bool ConfigureModuleProviders(string moduleCuid, string storageProviderKey,
            string stagingProviderKey = null, StorageProfileMode mode = StorageProfileMode.DirectSave) {

            if (string.IsNullOrWhiteSpace(moduleCuid)) return false;
            if (Indexer == null || !Indexer.TryGetComponentInfo<StorageModule>(moduleCuid, out StorageModule m) || m == null)
                return false;

            // Validate that provider keys refer to registered providers.
            if (!string.IsNullOrWhiteSpace(storageProviderKey) && !_providers.ContainsKey(storageProviderKey))
                throw new ArgumentException($"Provider key '{storageProviderKey}' is not registered. Call AddProvider first.", nameof(storageProviderKey));
            if (!string.IsNullOrWhiteSpace(stagingProviderKey) && !_providers.ContainsKey(stagingProviderKey))
                throw new ArgumentException($"Staging provider key '{stagingProviderKey}' is not registered. Call AddProvider first.", nameof(stagingProviderKey));

            // StorageModule is a reference type — mutating it updates the indexer cache in-place.
            m.StorageProviderKey = storageProviderKey ?? string.Empty;
            m.StagingProviderKey = stagingProviderKey ?? string.Empty;
            m.ProfileMode = mode;
            return true;
        }
    }
}
