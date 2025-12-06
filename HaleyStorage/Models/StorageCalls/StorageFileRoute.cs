using Haley.Abstractions;

namespace Haley.Models {
    //When using a struct, remember that it is a value type.
    //Thus, when you modify it, you need to return the modified struct.
    //This is useful for immutability and functional programming paradigms.
    public class StorageFileRoute : StorageRoute, IVaultFileRoute {
        public int Version { get; set; } = 0;
        public long Size { get; set; } = 0;
        public string SaveAsName { get; set; }
        public StorageFileRoute() { }
        public StorageFileRoute(string name, string path) : base(name,path) { 
        }
    }
}
