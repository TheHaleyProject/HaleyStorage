using Haley.Abstractions;
using System.Collections.Concurrent;

namespace Haley.Models {
    /// <summary>
    /// Thread-safe dictionary of named <see cref="IStorageCoordinator"/> instances.
    /// Registered at DI startup (e.g. under the key <c>"mss"</c>) so controllers can
    /// retrieve a specific coordinator by name at runtime.
    /// </summary>
    public class VaultManager : ConcurrentDictionary<string, IStorageCoordinator>, IVaultManager {
        IStorageCoordinator IVaultManager.this[string key] => this[key];
        void IVaultManager.Add(string key, IStorageCoordinator value) => TryAdd(key, value);
        bool IVaultManager.TryGetValue(string key, out IStorageCoordinator value) => TryGetValue(key, out value);
    }
}
