using Haley.Abstractions;

namespace Haley.Models {
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
