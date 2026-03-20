using Haley.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;
using Haley.Enums;

namespace Haley.Models {
    /// <summary>
    /// Abstract base for concrete vault hierarchy objects (client, module, workspace).
    /// Adds a physical or virtual <see cref="StorageRef"/> and the <c>IsGuidControlled</c> convenience flag
    /// on top of <see cref="VaultProfile"/>.
    /// </summary>
    public abstract class VaultComponent : VaultProfile, IVaultObject {
        public string StorageRef { get; set; }
        public bool IsGuidControlled => ControlMode == VaultControlMode.Guid; // IVaultObject
        public VaultComponent(string displayName) : base(displayName) { }
    }
}
