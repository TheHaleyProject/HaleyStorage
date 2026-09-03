using Haley.Models;
using Haley.Utils;

namespace Haley.Services {
    public partial class StorageCoordinator {
        public Task<StorageModuleRuntimeStatus> GetModuleRuntimeStatusAsync(string client, string module) {
            ArgumentException.ThrowIfNullOrWhiteSpace(client);
            ArgumentException.ThrowIfNullOrWhiteSpace(module);

            var moduleCuid = StorageUtils.GenerateCuid(client, module);
            VaultModule? cachedModule = null;
            var registered = Indexer?.TryGetComponentInfo<VaultModule>(moduleCuid, out cachedModule) == true;
            var adapterLoaded = Indexer?.IsModuleAdapterRegistered(moduleCuid) == true;
            return Task.FromResult(new StorageModuleRuntimeStatus {
                Client = client,
                Module = module,
                ModuleCuid = moduleCuid,
                DatabaseName = cachedModule?.DatabaseName ?? $"dssm_{moduleCuid}",
                Registered = registered,
                AdapterLoaded = adapterLoaded,
                Hydrated = registered && adapterLoaded,
                Message = adapterLoaded
                    ? "Module adapter is loaded in this Storage API process."
                    : "Module adapter is not loaded in this Storage API process."
            });
        }

        public async Task<StorageModuleRuntimeStatus> ActivateModuleRuntimeAsync(string client, string module) {
            ArgumentException.ThrowIfNullOrWhiteSpace(client);
            ArgumentException.ThrowIfNullOrWhiteSpace(module);

            var moduleCuid = StorageUtils.GenerateCuid(client, module);
            var hydrated = Indexer != null && await Indexer.HydrateModuleAsync(moduleCuid).ConfigureAwait(false);
            var status = await GetModuleRuntimeStatusAsync(client, module).ConfigureAwait(false);
            status.Hydrated = hydrated && status.AdapterLoaded;
            status.Message = status.Hydrated
                ? "Module adapter was loaded in this Storage API process."
                : "Module is not registered in the storage registry or could not be loaded.";
            return status;
        }
    }
}
