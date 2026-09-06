namespace Haley.Models {

    public sealed class ChunkUploadSessionInfo {
        public long VersionId { get; set; }
        public string VersionCuid { get; set; } = string.Empty;
        public string RootCuid { get; set; } = string.Empty;
        public long ChunkSizeBytes { get; set; }
        public int TotalParts { get; set; }
        public long? TotalBytes { get; set; }
    }

    public sealed class ChunkPartResult {
        public int PartNumber { get; set; }
        public long PartBytes { get; set; }
        public long CommittedBytes { get; set; }
        public string Hash { get; set; } = string.Empty;
        public bool AlreadyPresent { get; set; }
    }

    public sealed class ChunkAppendResult {
        public long Offset { get; set; }
        public long TotalBytes { get; set; }
        public bool ReadyToComplete { get; set; }
        public int CompletedParts { get; set; }
    }

    public sealed class ChunkUploadStatus {
        public long VersionId { get; set; }
        public string VersionCuid { get; set; } = string.Empty;
        public string RootCuid { get; set; } = string.Empty;
        public int TotalParts { get; set; }
        public int ReceivedParts { get; set; }
        public int PendingParts { get; set; }
        public int[] MissingParts { get; set; } = Array.Empty<int>();
        public long? TotalBytes { get; set; }
        public long CommittedBytes { get; set; }
        public long SequentialOffset { get; set; }
        public DateTimeOffset LastActivity { get; set; }
        public string State { get; set; } = "active";
    }
}
