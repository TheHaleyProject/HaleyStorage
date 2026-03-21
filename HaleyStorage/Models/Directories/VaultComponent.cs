using Haley.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;
using Haley.Enums;

namespace Haley.Models {
    /// <summary>
    /// Abstract base for concrete vault hierarchy objects (client, module, workspace).
    /// Adds a physical <see cref="StorageRef"/> (cached path segment) on top of <see cref="VaultProfile"/>.
    /// </summary>
    public abstract class VaultComponent : VaultProfile {
        public string StorageRef { get; set; }
        public VaultComponent(string displayName) : base(displayName) { }
    }
}
