using System;

namespace Haley.Models {
    /// <summary>
    /// One concrete version of a logical document.
    /// </summary>
    public class VaultFileVersionInfo {
        public long VersionId { get; set; }
        public string VersionCuid { get; set; } = string.Empty;
        public int VersionNumber { get; set; }
        public DateTime? Created { get; set; }
        public long? Size { get; set; }
        public string StorageName { get; set; } = string.Empty;
        public string StorageRef { get; set; } = string.Empty;
        public string StagingRef { get; set; } = string.Empty;
        public int Flags { get; set; }
        public string Hash { get; set; } = string.Empty;
        public DateTime? SyncedAt { get; set; }
        public string Metadata { get; set; } = string.Empty;
    }
}
