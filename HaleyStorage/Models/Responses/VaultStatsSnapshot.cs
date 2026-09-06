using Haley.Enums;
using System;
using System.Collections.Generic;

namespace Haley.Models {
    public class VaultStatsSnapshot {
        public VaultStatsNodeType NodeType { get; set; }
        public long NodeId { get; set; }
        public long WorkspaceId { get; set; }
        public string WorkspaceCuid { get; set; } = string.Empty;
        public long DirectoryId { get; set; }
        public string DirectoryCuid { get; set; } = string.Empty;
        public string DirectoryName { get; set; } = string.Empty;
        public string DirectoryPath { get; set; } = string.Empty;
        public VaultStatsCounters Direct { get; set; } = new();
        public VaultStatsCounters Recursive { get; set; } = new();
        public List<VaultExtensionStats> Extensions { get; set; } = new();
        public DateTimeOffset Generated { get; set; } = DateTimeOffset.UtcNow;
    }
}
