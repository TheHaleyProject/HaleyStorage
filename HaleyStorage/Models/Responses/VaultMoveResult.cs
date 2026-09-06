namespace Haley.Models {
    public class VaultMoveResult {
        public string ItemType { get; set; } = string.Empty;
        public long Id { get; set; }
        public string Cuid { get; set; } = string.Empty;
        public long SourceWorkspaceId { get; set; }
        public long SourceParentId { get; set; }
        public long TargetWorkspaceId { get; set; }
        public long TargetParentId { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool Renamed { get; set; }
    }
}
