using Haley.Abstractions;
using Haley.Enums;
using System;

namespace Haley.Models {
    /// <summary>
    /// Represents a workspace within the vault hierarchy (client → module → workspace).
    /// Extends <see cref="VaultSegment"/> (identity + provider routing) and implements
    /// <see cref="IVaultRoute"/> (physical StorageRef path).
    /// NameMode and ParseMode are fixed at creation — changing them requires a full migration.
    /// Virtual workspaces exist only in the DB and have no physical directory.
    /// </summary>
    internal class VaultWorkSpace : VaultSegment, IVaultWorkSpace {
        public IVaultObject Client { get; set; }
        public IVaultObject Module { get; set; }
        public bool IsVirtual { get; set; }
        /// <summary>Workspace segment only (_wsShardedPath). Persisted to storage_ref in DB.</summary>
        public string StorageRef { get; set; }
        /// <summary>Client/module relative path (clientDir/moduleDir). Computed at registration, cached in-memory only — never stored in DB.</summary>
        public string Base { get; set; }
        /// <summary>Defines whether stored files are identified by numeric IDs or compact-N GUIDs. Fixed at creation.</summary>
        public VaultNameMode NameMode { get; set; }
        /// <summary>Defines whether file identifiers are parsed from caller input or auto-generated. Fixed at creation.</summary>
        public VaultNameParseMode ParseMode { get; set; }
        /// <summary>
        /// When true, the parent client and module directory names preserve original display-name casing.
        /// When false, they are normalized via ToDBName(). The workspace segment itself is always
        /// system-generated (SHA-256 hash with underscore prefix) and is never affected by this flag.
        /// </summary>
        public bool CaseSensitive { get; set; }

        public void Assert() {
            if (string.IsNullOrWhiteSpace(DisplayName)) throw new ArgumentNullException("Name cannot be empty");
            if (!IsVirtual && string.IsNullOrWhiteSpace(StorageRef)) throw new ArgumentNullException("StorageRef cannot be empty");
            if (string.IsNullOrEmpty(Client?.Name) || string.IsNullOrWhiteSpace(Module?.Name)) throw new ArgumentNullException("Client & Module information cannot be empty");
        }

        public VaultWorkSpace(string clientName, string moduleName, string displayName, bool is_virtual = false) : base(displayName) {
            IsVirtual = is_virtual;
            Client = new VaultObject(clientName);
            Module = new VaultObject(moduleName).UpdateCUID(clientName, moduleName);
            UpdateCUID(Client.Name, Module.Name, Name);
        }
    }
}
