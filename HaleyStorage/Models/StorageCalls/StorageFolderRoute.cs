using Haley.Abstractions;

namespace Haley.Models {
    /// <summary>
    /// Represents a virtual folder route within a workspace.
    /// Folders are DB-only constructs — they do not correspond to physical directories.
    /// Nested folders are represented via the <see cref="StorageFolderRoute.Parent"/> reference.
    /// </summary>
    //When using a struct, remember that it is a value type.
    //Thus, when you modify it, you need to return the modified struct.
    //This is useful for immutability and functional programming paradigms.
    public class StorageFolderRoute : StorageRoute, IVaultFolderRoute {
        public IVaultFolderRoute Parent { get; set; }
        public StorageFolderRoute() { }
        public StorageFolderRoute(string name, string path) : base(name,path) { 
        }
    }
}
