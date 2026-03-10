using Haley.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;
using Haley.Enums;

namespace Haley.Models {
    public abstract class VaultComponent : VaultProfile, IVaultObject {
        public string Path { get; set; }
        public bool IsGuidControlled => ControlMode == VaultControlMode.Guid; // IVaultObject
        public VaultComponent(string displayName) : base(displayName) { }
    }
}
