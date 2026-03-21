using Haley.Abstractions;
using Haley.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Haley.Models {
    /// <summary>
    /// Represents a storage module in the vault hierarchy (client → module → workspace).
    /// Carries the per-module DB name, the keys used to resolve the primary and staging
    /// <see cref="IStorageProvider"/> instances, and the <see cref="StorageProfileMode"/>
    /// that controls upload routing for files in this module.
    /// </summary>
    public class StorageModule : VaultComponent, IVaultModule {
        /// <summary>Parent client reference required by <see cref="IVaultModule"/>.</summary>
        public IVaultBase Client { get; set; }      // IVaultModule requires IVaultBase
        /// <summary>Name of the per-module MariaDB schema (e.g. <c>dssm_{cuid}</c>).</summary>
        public string DatabaseName { get; set; }
        /// <summary>Logical profile name associated with this module's storage configuration.</summary>
        public string StorageProfileName { get; set; }
        /// <summary>Key of the primary <see cref="IStorageProvider"/> used for this module. Empty means the default provider.</summary>
        public string StorageProviderKey { get; set; }
        /// <summary>Key of the staging <see cref="IStorageProvider"/> for this module. Empty or null disables staging.</summary>
        public string StagingProviderKey { get; set; }
        /// <summary>
        /// Determines where new uploads are first written.
        /// DirectSave (default): straight to primary provider.
        /// StageAndMove / StageAndRetainCopy: first to staging provider; background worker promotes to primary.
        /// </summary>
        public StorageProfileMode ProfileMode { get; set; } = StorageProfileMode.DirectSave;
        /// <summary>
        /// The dsscore.profile_info.id that backs the current provider configuration for this module.
        /// Set by <see cref="ConfigureModuleProviders"/> when the caller supplies a profileInfoId.
        /// 0 when configured via provider keys alone (no profile_info link).
        /// </summary>
        public long ProfileInfoId { get; set; } = 0;
        /// <summary>
        /// Validates that the storage name, path, and client reference are all populated.
        /// Returns <c>false</c> with a descriptive message when any required field is missing.
        /// </summary>
        public override bool TryValidate(out string message) {
            message = string.Empty;
            if (!base.TryValidate(out message)) return false;
            if (string.IsNullOrEmpty(StorageName) || string.IsNullOrEmpty(StorageRef)) {
                message = "Name & Path Cannot be empty";
                return false;
            }
            if (Client == null || string.IsNullOrEmpty(Client.Name)) {
                message = "Client Information cannot be empty";
                return false;
            }
            return true;
        }
        /// <summary>
        /// Creates a <see cref="StorageModule"/> and computes the CUID from the client and module names.
        /// </summary>
        public StorageModule(string clientName, string displayName) : base(displayName) {
            Client = new VaultInfo(clientName);
            UpdateCUID(Client.Name, Name);
        }
    }
}
