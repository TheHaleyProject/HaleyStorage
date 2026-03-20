using Haley.Abstractions;
using System.Collections.Generic;
using Haley.Enums;

namespace Haley.Models {
    /// <summary>
    /// Extends <see cref="StorageReadRequest"/> with a typed <see cref="StorageReadFileRequest.File"/> route.
    /// Used for all file-level read operations (download, exists, get-parent).
    /// </summary>
    public class StorageReadFileRequest : StorageReadRequest, IVaultFileReadRequest {
        public IVaultFileRoute File { get; private set; }
        /// <summary>Attaches a pre-populated file route (e.g. with a known CUID or SaveAsName) to the request.</summary>
        public IVaultFileReadRequest SetFile(IVaultFileRoute file) {
            if (file != null) File = file;
            return this;
        }
        public StorageReadFileRequest() : base(){ }
        public StorageReadFileRequest(string client_name) : base(client_name) { }
        public StorageReadFileRequest(string client_name,string module_name) : base(client_name, module_name) { }
        public StorageReadFileRequest(string client_name, string module_name, string workspace_name) : base(client_name,module_name,workspace_name) {
        }
    }
}
