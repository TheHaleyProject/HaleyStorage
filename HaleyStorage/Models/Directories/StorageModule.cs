using Haley.Abstractions;
using Haley.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Haley.Models {
    public class StorageModule : VaultComponent, IVaultModule {
        public IVaultBase Client { get; set; }      // IVaultModule requires IVaultBase
        public string DatabaseName { get; set; }
        public string StorageProfileName { get; set; }
        public string StorageProviderKey { get; set; }
        public string StagingProviderKey { get; set; }
        /// <summary>
        /// Determines where new uploads are first written.
        /// DirectSave (default): straight to primary provider.
        /// StageAndMove / StageAndRetainCopy: first to staging provider; background worker promotes to primary.
        /// </summary>
        public StorageProfileMode ProfileMode { get; set; } = StorageProfileMode.DirectSave;
        public override bool TryValidate(out string message) {
            message = string.Empty;
            if (!base.TryValidate(out message)) return false;
            if (string.IsNullOrEmpty(StorageName) || string.IsNullOrEmpty(Path)) {
                message = "Name & Path Cannot be empty";
                return false;
            }
            if (Client == null || string.IsNullOrEmpty(Client.Name)) {
                message = "Client Information cannot be empty";
                return false;
            }
            return true;
        }
        public StorageModule(string clientName, string displayName) : base(displayName) {
            Client = new VaultInfo(clientName);
            UpdateCUID(Client.Name, Name);
        }
    }
}
