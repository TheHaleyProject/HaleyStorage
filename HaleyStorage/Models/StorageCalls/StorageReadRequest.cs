using Haley.Abstractions;
using System.Collections.Generic;
using Haley.Enums;
using Haley.Utils;

namespace Haley.Models {
    /// <summary>
    /// Base read request that carries scope information (client, module, workspace, folder),
    /// an optional resolved <see cref="StorageReadRequest.TargetPath"/>, and a per-call unique ID.
    /// Implements both <see cref="IVaultReadRequest"/> and <see cref="IVaultScope"/> — the instance
    /// acts as its own scope to avoid an extra allocation.
    /// </summary>
    public class StorageReadRequest : IVaultReadRequest, IVaultScope {
        bool callIdGenerated;
        public string CallID { get; protected set; } = Guid.NewGuid().ToString();
        public string TargetPath { get; protected set; }
        public string TargetName { get; protected set; }
        public IVaultInfo Client { get; protected set; }
        public IVaultInfo Module { get; protected set; }
        public IVaultInfo Workspace { get; protected set; }
        public IVaultFolderRoute Folder { get; protected set; }
        public bool ReadOnlyMode { get; protected set; }

        // IVaultReadRequest.Scope — this class is itself the scope implementation.
        public IVaultScope Scope => this;

        /// <summary>
        /// Regenerates a fresh <see cref="StorageReadRequest.CallID"/> for this request.
        /// Can only be called once per request instance; subsequent calls are no-ops and return <c>false</c>.
        /// </summary>
        public bool GenerateCallId() {
            if (callIdGenerated) return false;
            CallID = Guid.NewGuid().ToString();
            callIdGenerated = true;
            return true;
        }

        /// <summary>
        /// Sets the client, module, or workspace component and recomputes all CUIDs to reflect
        /// the updated hierarchy.
        /// </summary>
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

        /// <summary>Sets the logical target file name (used by path resolution to look up or generate the storage path).</summary>
        public IVaultReadRequest SetTargetName(string name) {
            if (string.IsNullOrWhiteSpace(name)) return this;
            TargetName = name;
            return this;
        }
        /// <summary>Sets the virtual folder context for directory-scoped file operations.</summary>
        public IVaultReadRequest SetFolder(IVaultFolderRoute folder) {
            if (folder != null) Folder = folder;
            return this;
        }
        /// <summary>Sets an already-resolved absolute or provider-specific target path, bypassing the path-resolution pipeline.</summary>
        public IVaultReadRequest SetTargetPath(string path) {
            if (string.IsNullOrWhiteSpace(path)) return this;
            TargetPath = path;
            return this;
        }

        /// <summary>When <paramref name="readOnly"/> is <c>true</c>, prevents DB writes during path resolution (e.g. no document registration).</summary>
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
            Workspace = new VaultProfile(workspace_name).UpdateCUID(Client.DisplayName,Module.DisplayName);
        }
    }
}