using Haley.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace Haley.Models {
    public abstract class VaultComponent : StorageInfo , IVaultComponent{
        public string Path { get; set; }
        public VaultComponent(string displayName):base(displayName) { }
    }
}
