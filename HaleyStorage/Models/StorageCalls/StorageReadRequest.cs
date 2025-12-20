using Haley.Abstractions;
using System.Collections.Generic;
using Haley.Enums;
using Haley.Utils;

namespace Haley.Models {
    public class StorageReadRequest : IVaultReadRequest {
        bool callIdGenerated;
        public string CallID { get; protected set; } = Guid.NewGuid().ToString();
        public string TargetPath { get; protected set; }
        public string TargetName { get; protected set; }
        public IVaultInfo Client { get; protected set; } 
        public IVaultInfo Module { get; protected set; }
        public IVaultInfo Workspace { get; protected set; } 
        public IVaultFolderRoute Folder { get; protected set; }
        public bool ReadOnlyMode { get; protected set; }
        public bool GenerateCallId() {
            if (callIdGenerated) return false;
            CallID = Guid.NewGuid().ToString();
            callIdGenerated = true;
            return true;
        }

        public virtual IVaultReadRequest SetComponent(IVaultInfo input, Enums.VaultObjectType type) {
            switch (type) {
                case Enums.VaultObjectType.Client:
                Client = input;
                break;
                case Enums.VaultObjectType.Module:
                Module = input; 
                break;
                case Enums.VaultObjectType.WorkSpace:
                Workspace = input;
                break;
            }
            UpdateCUID();
            return this;
        }
        void UpdateCUID() {
            if (Client == null) return;
            if (Module != null) Module.UpdateCUID(Client.DisplayName);
            if (Workspace != null) Workspace.UpdateCUID(Client.DisplayName, Module?.DisplayName);
        }

        public IVaultReadRequest SetTargetName(string name) {
            if (string.IsNullOrWhiteSpace(name)) return this;
            TargetName = name;
            return this;
        }
        public IVaultReadRequest SetFolder(IVaultFolderRoute folder) {
            if (folder != null) Folder = folder;
            return this;
        }
        public IVaultReadRequest SetTargetPath(string path) {
            if (string.IsNullOrWhiteSpace(path)) return this;
            TargetPath = path;
            return this;
        }

        public IVaultReadRequest SetMode(bool readOnly) {
            ReadOnlyMode = readOnly;
            return this;
        }

        public StorageReadRequest() :this (null,null,null){ }
        public StorageReadRequest(string client_name) :this(client_name,null,null) { }
        public StorageReadRequest(string client_name,string module_name) :this(client_name, module_name, null) { }

        public  StorageReadRequest(string client_name, string module_name, string workspace_name) {
            Client = new VaultProfile(client_name);
            Module = new VaultProfile(module_name).UpdateCUID(Client.DisplayName,module_name);
            Workspace = new VaultProfile(workspace_name).UpdateCUID(Client.DisplayName,Module.DisplayName); //Here nothing matters, because it is an input request. // We need to fetch the information from database and then update this workspace information.
        }
    }
}
