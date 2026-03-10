using Haley.Abstractions;
using System.Collections.Concurrent;

namespace Haley.Models {
    public class VaultManager : ConcurrentDictionary<string, IStorageCoordinator>, IVaultManager {
        IStorageCoordinator IVaultManager.this[string key] => this[key];
        void IVaultManager.Add(string key, IStorageCoordinator value) => TryAdd(key, value);
        bool IVaultManager.TryGetValue(string key, out IStorageCoordinator value) => TryGetValue(key, out value);
    }
}
