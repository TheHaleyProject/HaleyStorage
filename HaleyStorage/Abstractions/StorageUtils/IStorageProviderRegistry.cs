namespace Haley.Abstractions {
    /// <summary>
    /// Registry for managing named storage providers. Supports multiple providers
    /// (FileSystem, Backblaze, Azure, etc.) with a designated default.
    /// </summary>
    public interface IStorageProviderRegistry {
        IStorageProviderRegistry RegisterProvider(IStorageProvider provider, bool setAsDefault = false);
        bool TryGetProvider(string key, out IStorageProvider provider);
        IStorageProvider GetDefaultProvider();
    }
}