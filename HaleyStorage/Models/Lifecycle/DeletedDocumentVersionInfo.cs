using System;

namespace Haley.Models {
    /// <summary>
    /// Physical and logical version data for a deleted document, used during archive and restore.
    /// </summary>
    public class DeletedDocumentVersionInfo {
        public long VersionId { get; set; }
        public string VersionCuid { get; set; } = string.Empty;
        public int VersionNumber { get; set; }
        public int SubVersionNumber { get; set; }
        public int DeleteState { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? Deleted { get; set; }
        public string StorageName { get; set; } = string.Empty;
        public string StorageRef { get; set; } = string.Empty;
        public string StagingRef { get; set; } = string.Empty;
        public int Flags { get; set; }
        public long? ProfileInfoId { get; set; }
    }
}
