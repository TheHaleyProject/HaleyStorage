using Haley.Abstractions;
using Haley.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Haley.Models {
    public class StorageWorkspace : VaultComponent, IVaultWorkSpace {
        public IVaultInfo Client { get; set; }
        public IVaultInfo Module { get; set; }
        public string DatabaseName { get; set; }
        public VaultControlMode ContentControl { get; set; }
        public VaultParseMode ContentParse { get; set; }
        
        public void Assert() {
            if (string.IsNullOrWhiteSpace(DisplayName)) throw new ArgumentNullException("Name cannot be empty");
            if (!IsVirtual &&  (string.IsNullOrEmpty(StorageName)  || string.IsNullOrEmpty(Path))) throw new ArgumentNullException("Path Cannot be empty");
            if ( string.IsNullOrEmpty(Client?.Name) || string.IsNullOrWhiteSpace(Module?.Name)) throw new ArgumentNullException("Client & Module information cannot be empty");
        }
        public StorageWorkspace(string clientName, string moduleName, string displayName, bool is_virtual = false):base(displayName) {
            IsVirtual = is_virtual;
            Client = new StorageInfoBase(clientName) {  };
            Module = new StorageInfoBase(moduleName).UpdateCUID(clientName,moduleName);
            UpdateCUID(Client.Name, Module.Name, Name); //With all other names
        }
    }
}
