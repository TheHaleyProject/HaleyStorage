using System;

namespace Haley.Models {
    /// <summary>
    /// Metadata for one ##v{n}## filesystem revision backup of a stored file.
    /// Returned by <c>FileSystemStorageProvider.GetRevisions</c> and the coordinator's
    /// <c>GetRevisions</c> method. No DB query is involved — all values come from the filesystem.
    /// </summary>
    public class VaultRevisionInfo {
        /// <summary>The version number parsed from the ##v{n}## suffix.</summary>
        public int Version { get; set; }
        /// <summary>File size in bytes.</summary>
        public long Size { get; set; }
        /// <summary>Human-readable file size (e.g. "1.2 MB").</summary>
        public string SizeHR { get; set; } = string.Empty;
        /// <summary>Last-write timestamp (UTC) of the revision backup file.</summary>
        public DateTime LastModifiedUtc { get; set; }
    }
}
