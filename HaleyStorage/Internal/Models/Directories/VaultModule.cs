using Haley.Abstractions;
using Haley.Enums;
using System;

namespace Haley.Models {
    /// <summary>
    /// Represents a storage module in the vault hierarchy (client → module → workspace).
    /// Extends <see cref="VaultSegment"/> (identity + provider routing).
    /// The physical directory path is always derived from the module name — no StorageRef needed.
    /// </summary>
    internal class VaultModule : VaultSegment, IVaultModule {
        public IVaultObject Client { get; set; }
        public override bool TryValidate(out string message) {
            message = string.Empty;
            if (!base.TryValidate(out message)) return false;
            if (Client == null || string.IsNullOrEmpty(Client.Name)) {
                message = "Client information cannot be empty";
                return false;
            }
            return true;
        }

        public VaultModule(string clientName, string displayName) : base(displayName) {
            Client = new VaultObject(clientName);
            UpdateCUID(Client.Name, Name);
        }
    }
}
