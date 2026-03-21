using Microsoft.AspNetCore.Mvc;

namespace Haley.Models {
    /// <summary>
    /// HTTP query input for folder-scoped operations where the current folder is identified
    /// by id/cuid/name under an existing workspace.
    /// </summary>
    public class VaultFolderApiInput : VaultApiInput {
        [FromQuery(Name = "fid")]
        public long? FolderId { get; set; }

        [FromQuery(Name = "fuid")]
        public string? FolderCuid { get; set; }
    }
}
