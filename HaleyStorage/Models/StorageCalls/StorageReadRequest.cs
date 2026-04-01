using Haley.Abstractions;
using System.Collections.Generic;
using Haley.Enums;
using Haley.Utils;

namespace Haley.Models {
    /// <summary>
    /// Base read request that carries scope information (client, module, workspace, folder),
    /// an optional resolved <see cref="StorageReadRequest.OverrideRef"/>, and a per-call unique ID.
    /// Implements both <see cref="IVaultReadRequest"/> and <see cref="IVaultScope"/> — the instance
    /// acts as its own scope. Client/Module/Workspace/Folder are accessible only via <see cref="Scope"/>.
    /// </summary>
    public class StorageReadRequest : IVaultReadRequest, IVaultScope {
        bool callIdGenerated;
        public string CallID { get; protected set; } = Guid.NewGuid().ToString();
        public string OverrideRef { get; protected set; }
        public string RequestedName { get; protected set; }
        public bool ReadOnlyMode { get; protected set; }

        // ── IVaultScope — explicit implementation ────────────────────────────
        // Access via request.Scope.Client / .Module / .Workspace / .Folder.
        IVaultObject _client;
        IVaultObject _module;
        IVaultObject _workspace;
        IVaultFolderRoute _folder;

        IVaultObject IVaultScope.Client => _client;
        IVaultObject IVaultScope.Module => _module;
        IVaultObject IVaultScope.Workspace => _workspace;
        IVaultFolderRoute IVaultScope.Folder => _folder;

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
        public virtual IVaultReadRequest SetComponent(IVaultObject input, Enums.VaultObjectType type) {
            switch (type) {
                case Enums.VaultObjectType.Client:
                _client = input;
                break;
                case Enums.VaultObjectType.Module:
                _module = input;
                break;
                case Enums.VaultObjectType.WorkSpace:
                _workspace = input;
                break;
            }
            UpdateCUID();
            return this;
        }
        void UpdateCUID() { if (_client == null) return; if (_module != null) _module.UpdateCUID(_client.DisplayName); if (_workspace != null) _workspace.UpdateCUID(_client.DisplayName, _module?.DisplayName); }

        /// <summary>Sets the caller-requested file name (used by path resolution to look up or generate the storage ref).</summary>
        public IVaultReadRequest SetRequestedName(string name) {
            if (string.IsNullOrWhiteSpace(name)) return this;
            RequestedName = name;
            return this;
        }
        /// <summary>Sets the virtual folder context for directory-scoped file operations.</summary>
        public IVaultReadRequest SetFolder(IVaultFolderRoute folder) {
            if (folder != null) _folder = folder;
            return this;
        }
        /// <summary>Sets an already-resolved provider-specific storage ref, bypassing the path-resolution pipeline.</summary>
        public IVaultReadRequest SetOverrideRef(string storageRef) {
            if (string.IsNullOrWhiteSpace(storageRef)) return this;
            OverrideRef = storageRef;
            return this;
        }

        /// <summary>Sets the workspace by name.</summary>
        public IVaultReadRequest SetWorkspace(string name, bool isVirtual = false) {
            _workspace = new VaultObject(name).UpdateCUID(_client?.DisplayName, _module?.DisplayName);
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

        public StorageReadRequest(string client_name, string module_name, string workspace_name) {
            _client = new VaultObject(client_name).UpdateCUID();
            _module = new VaultObject(module_name).UpdateCUID(_client.DisplayName);
            _workspace = new VaultObject(workspace_name).UpdateCUID(_client.DisplayName, _module.DisplayName);
        }
    }
}
