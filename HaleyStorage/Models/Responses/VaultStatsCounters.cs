namespace Haley.Models {
    public class VaultStatsCounters {
        public long ActiveFolders { get; set; }
        public long DeletedFolders { get; set; }
        public long ActiveDocuments { get; set; }
        public long DeletedDocuments { get; set; }
        public long ActiveVersions { get; set; }
        public long DeletedVersions { get; set; }
        public long ActiveThumbnails { get; set; }
        public long DeletedThumbnails { get; set; }
        public long ActiveBytes { get; set; }
        public long DeletedBytes { get; set; }
        public long ArchivedBytes { get; set; }
        public long PurgedBytes { get; set; }
    }
}
