using Haley.Abstractions;

namespace Haley.Models {
    /// <summary>
    /// Abstract base for file and folder route POCOs.
    /// Carries the storage reference <see cref="StorageRoute.StorageRef"/>, display <see cref="StorageRoute.DisplayName"/>,
    /// auto-increment <see cref="StorageRoute.Id"/>, and compact-N CUID string.
    /// Concrete subclasses are <see cref="StorageFileRoute"/> and <see cref="StorageFolderRoute"/>.
    /// </summary>
    // Lightweight route POCO — implements only IVaultRoute (just StorageRef).
    // Id, Cuid (string), DisplayName are plain public properties consumed by IVaultFileRoute / IVaultFolderRoute.
    public abstract class StorageRoute : IVaultRoute {
        public long Id { get; set; }
        public string Cuid { get; set; }  // compact-N guid string; kept as string on routes
        public string DisplayName { get; set; }
        public string StorageRef { get; set; }
        public StorageRoute() { }
        public StorageRoute(string displayName, string storageRef) {
            DisplayName = displayName;
            StorageRef = storageRef;
            Cuid = string.Empty;
        }
    }
}
