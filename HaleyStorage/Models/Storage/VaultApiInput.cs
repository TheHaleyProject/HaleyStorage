using Microsoft.AspNetCore.Mvc;

namespace Haley.Models {
    /// <summary>
    /// Vault scope with directory context (c, m, w, d, did, duid).
    /// Use this for endpoints that may target a specific folder within a workspace.
    /// For endpoints that only need scope (no folder), use <see cref="VaultScopeInput"/>.
    /// For endpoints that also need file identity, use <see cref="VaultFileApiInput"/>.
    /// </summary>
    public class VaultApiInput : VaultScopeInput {
        [FromQuery(Name = "d")]
        public string? DirectoryName { get; set; }
        [FromQuery(Name = "did")]
        public long? DirectoryParent { get; set; }
        [FromQuery(Name = "duid")]
        public string? ParentCuid { get; set; }

        public VaultApiInput() {
        }
    }
}
