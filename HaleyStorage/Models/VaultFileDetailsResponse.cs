using System.Collections.Generic;

namespace Haley.Models {
    /// <summary>
    /// Full metadata view for one logical document, including all versions.
    /// </summary>
    public class VaultFileDetailsResponse {
        public long DocumentId { get; set; }
        public string DocumentCuid { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public long WorkspaceId { get; set; }
        public string WorkspaceCuid { get; set; } = string.Empty;
        public long DirectoryId { get; set; }
        public string DirectoryCuid { get; set; } = string.Empty;
        public string DirectoryName { get; set; } = string.Empty;
        public long DirectoryParentId { get; set; }
        public int VersionCount { get; set; }
        public List<VaultFileVersionInfo> Versions { get; set; } = new();
    }
}
