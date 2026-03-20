using Haley.Abstractions;
using Haley.Utils;
using System;
using System.Collections.Generic;
using System.Text;
using Haley.Enums;
using System.Linq;

namespace Haley.Models {
    /// <summary>
    /// Lightweight carrier used only to pass the auto-increment <see cref="VaultUID.Id"/> and
    /// compact-N <see cref="VaultUID.Guid"/> out of <c>StorageUtils.TryPopulateControlledID</c>.
    /// All other <see cref="IVaultBase"/> / <see cref="IIdentityBase"/> members are no-op stubs.
    /// </summary>
    // Lightweight carrier used only to pass Id and Guid out of TryPopulateControlledID.
    // All other IVaultBase / IIdentityBase members are stubs.
    public class VaultUID : IVaultBase {
        public long Id { get; set; }
        public Guid Guid { get; set; }
        public Guid Cuid { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Key => Id.ToString();
        public string DisplayName => string.Empty;
        public bool TryValidate(out string message) { message = string.Empty; return true; }
        public IVaultBase UpdateCUID(params string[] parentNames) => this;
        public IVaultBase SetCuid(string guid) {
            if (System.Guid.TryParse(guid, out var g)) Cuid = g;
            return this;
        }
        public IVaultBase SetCuid(Guid guid) { Cuid = guid; return this; }
        public IIdentityBase SetGuid(Guid guid) { Guid = guid; return this; }
        public VaultUID(long id, Guid uid) {
            Id = id;
            Guid = uid;
        }
    }
}
