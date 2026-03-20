using Haley.Abstractions;

namespace Haley.Models {
    /// <summary>
    /// Abstract base for file and folder route POCOs.
    /// Carries the storage reference <see cref="StorageRoute.Path"/>, display <see cref="StorageRoute.Name"/>,
    /// auto-increment <see cref="StorageRoute.Id"/>, and compact-N CUID string.
    /// Concrete subclasses are <see cref="StorageFileRoute"/> and <see cref="StorageFolderRoute"/>.
    /// </summary>
    // Lightweight route POCO — implements only IVaultRoute (just Path).
    // Id, Cuid (string), Name are plain public properties consumed by IVaultFileRoute / IVaultFolderRoute.
    public abstract class StorageRoute : IVaultRoute {
        public long Id { get; set; }
        public string Cuid { get; set; }  // compact-N guid string; kept as string on routes
        public string Name { get; set; }
        public string Path { get; set; }
        public StorageRoute() { }
        public StorageRoute(string name, string path) {
            Name = name;
            Path = path;
            Cuid = string.Empty;
        }
    }
}
