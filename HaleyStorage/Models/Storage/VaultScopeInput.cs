using Microsoft.AspNetCore.Mvc;

namespace Haley.Models {
    /// <summary>
    /// Minimal vault scope: client, module, and workspace only (c, m, w).
    /// Use this as the parameter type for endpoints that never need folder or file context.
    /// Subclasses add directory context (<see cref="VaultApiInput"/>) and file context
    /// (<see cref="VaultFileApiInput"/>).
    /// </summary>
    public class VaultScopeInput {
        [FromQuery(Name = "c")]
        public string? ClientName { get; set; }
        [FromQuery(Name = "m")]
        public string? ModuleName { get; set; }
        [FromQuery(Name = "w")]
        public string? WorkSpaceName { get; set; }
    }
}
