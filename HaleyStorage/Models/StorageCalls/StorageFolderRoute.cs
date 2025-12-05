using Haley.Abstractions;

namespace Haley.Models {
    //When using a struct, remember that it is a value type.
    //Thus, when you modify it, you need to return the modified struct.
    //This is useful for immutability and functional programming paradigms.
    public class StorageFolderRoute : StorageRoute, IStorageFolderRoute {
        public bool IsVirutal { get; set; }
        public bool CreateIfMissing { get; set; }
        public IStorageRoute Parent { get; set; }
        public StorageFolderRoute() { }
        public StorageFolderRoute(string name, string path) : base(name,path) { 
        }
    }
}
