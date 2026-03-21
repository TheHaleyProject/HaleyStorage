namespace Haley.Abstractions {
    /// <summary>
    /// A named registry of storage coordinators. Allows an application to host multiple
    /// StorageCoordinator instances (e.g. one per server/location) and resolve them by name.
    /// </summary>
    public interface IVaultManager {
        IStorageCoordinator this[string key] { get; }
        void Add(string key, IStorageCoordinator value);
        bool TryGetValue(string key, out IStorageCoordinator value);
    }
}
