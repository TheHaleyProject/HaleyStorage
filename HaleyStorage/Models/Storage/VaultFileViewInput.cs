using Microsoft.AspNetCore.Mvc;

namespace Haley.Models {
    /// <summary>
    /// Lean input for file read/view endpoints (download, view, revisions, details, parent lookup).
    /// Carries only what is needed to identify a file: scope (c, m, w) + identity (uid, ruid, pn).
    /// No folder context and no version number — file read endpoints always resolve by identity.
    /// <para>
    /// Resolution priority inside the coordinator:
    /// <list type="number">
    ///   <item><c>uid</c> — exact version CUID.</item>
    ///   <item><c>ruid</c> — resolves to the latest version of the document.</item>
    ///   <item><c>pn</c> — storage name (globally unique path key).</item>
    /// </list>
    /// </para>
    /// </summary>
    public class VaultFileViewInput : VaultScopeInput {
        /// <summary>Version CUID (uid). Resolves to the exact version.</summary>
        [FromQuery(Name = "uid")]
        public string? Cuid { get; set; }

        /// <summary>Document CUID (ruid). Resolves to the latest version of the document.</summary>
        [FromQuery(Name = "ruid")]
        public string? RootCuid { get; set; }

        /// <summary>Storage/processed name (pn). Globally unique — resolves to the exact version that owns this storage key.</summary>
        [FromQuery(Name = "pn")]
        public string? SanitizedName { get; set; }

        public VaultFileViewInput() {
        }
    }
}
