namespace Haley.Models {
    public sealed class ChunkUploadBrowseItem {
        public long VersionId { get; set; }
        public string VersionCuid { get; set; } = string.Empty;
        public string RootCuid { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long WorkspaceId { get; set; }
        public long DirectoryId { get; set; }
        public int VersionNumber { get; set; }
        public long ChunkSizeBytes { get; set; }
        public int TotalParts { get; set; }
        public int ReceivedParts { get; set; }
        public int PendingParts { get; set; }
        public int[] MissingParts { get; set; } = Array.Empty<int>();
        public long? TotalBytes { get; set; }
        public long CommittedBytes { get; set; }
        public long SequentialOffset { get; set; }
        public DateTimeOffset Created { get; set; }
        public DateTimeOffset LastActivity { get; set; }
        public string State { get; set; } = "active";
        public bool StatusAvailable { get; set; }
    }
}
