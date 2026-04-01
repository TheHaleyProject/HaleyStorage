using Haley.Abstractions;

namespace Haley.Models {
    /// <summary>
    /// Concrete mutable scope carrier used by storage read/write requests.
    /// Holds the client, module, workspace, and virtual folder context for a single operation.
    /// </summary>
    public class StorageScope : IVaultScope {
        public IVaultObject Client { get; set; }
        public IVaultObject Module { get; set; }
        public IVaultObject Workspace { get; set; }
        public IVaultFolderRoute Folder { get; set; }
    }
}
