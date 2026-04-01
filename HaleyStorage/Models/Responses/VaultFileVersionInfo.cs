using Haley.Enums;
using System;
using System.Collections.Generic;

namespace Haley.Models {
    /// <summary>
    /// One concrete version of a logical document.
    /// </summary>
    public class VaultFileVersionInfo {
        public long VersionId { get; set; }
        public string VersionCuid { get; set; } = string.Empty;
        public int VersionNumber { get; set; }
        public long ActorId { get; set; }
        public DateTime? Created { get; set; }
        public long? Size { get; set; }
        public string StorageName { get; set; } = string.Empty;
        public string StorageRef { get; set; } = string.Empty;
        public string StagingRef { get; set; } = string.Empty;
        public int Flags { get; set; }
        /// <summary>Human-readable representation of <see cref="Flags"/>. Example: "InStorage, Completed".</summary>
        public string FlagsText => ResolveFlags(Flags);
        public string Hash { get; set; } = string.Empty;
        public DateTime? SyncedAt { get; set; }
        public string Metadata { get; set; } = string.Empty;

        private static string ResolveFlags(int flags) {
            if (flags == 0) return VersionFlags.None.ToString();
            var parts = new List<string>();
            if ((flags & (int)VersionFlags.Placeholder)      != 0) parts.Add(nameof(VersionFlags.Placeholder));
            if ((flags & (int)VersionFlags.ChunkedMode)      != 0) parts.Add(nameof(VersionFlags.ChunkedMode));
            if ((flags & (int)VersionFlags.ChunkArea)        != 0) parts.Add(nameof(VersionFlags.ChunkArea));
            if ((flags & (int)VersionFlags.InStaging)        != 0) parts.Add(nameof(VersionFlags.InStaging));
            if ((flags & (int)VersionFlags.InStorage)        != 0) parts.Add(nameof(VersionFlags.InStorage));
            if ((flags & (int)VersionFlags.ChunksDeleted)    != 0) parts.Add(nameof(VersionFlags.ChunksDeleted));
            if ((flags & (int)VersionFlags.StagingDeleted)   != 0) parts.Add(nameof(VersionFlags.StagingDeleted));
            if ((flags & (int)VersionFlags.Completed)        != 0) parts.Add(nameof(VersionFlags.Completed));
            if ((flags & (int)VersionFlags.SyncedToInternal) != 0) parts.Add(nameof(VersionFlags.SyncedToInternal));
            return string.Join(", ", parts);
        }
    }
}
