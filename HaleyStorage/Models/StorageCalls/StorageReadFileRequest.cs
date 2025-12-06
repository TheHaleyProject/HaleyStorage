using Haley.Abstractions;
using System.Collections.Generic;
using Haley.Enums;

namespace Haley.Models {
    public class StorageReadFileRequest : StorageReadRequest, IVaultFileReadRequest {
        public IVaultFileRoute File { get; private set; }
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
