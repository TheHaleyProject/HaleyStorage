namespace Haley.Models {
    /// <summary>
    /// Lightweight projection returned by <c>IVaultIndexing.GetPendingStagedVersions</c>.
    /// Carries exactly the fields the <c>StagingPromotionWorker</c> needs to promote a
    /// staged file to primary storage without loading the full document graph.
    /// </summary>
    public class StagedVersionRef {
        /// <summary>Auto-increment <c>doc_version.id</c> (= <c>version_info.id</c>).</summary>
        public long VersionId { get; set; }
        /// <summary>Provider-level storage name (e.g. <c>"00001234.pdf"</c>).</summary>
        public string StorageName { get; set; }
        /// <summary>
        /// Primary storage target path (relative or absolute).
        /// This is where the file must land after promotion.
        /// </summary>
        public string StorageRef { get; set; }
        /// <summary>
        /// Staging provider reference (e.g. the cloud session ID).
        /// Passed to <c>stagingProvider.ReadAsync(stagingRef)</c> to pull the bytes.
        /// </summary>
        public string StagingRef { get; set; }
        /// <summary>
        /// <c>version_info.profile_info_id</c> — used to resolve both the primary and
        /// staging providers for this specific version via the profile cache.
        /// 0 means unknown (legacy row written before profile tracking).
        /// </summary>
        public long ProfileInfoId { get; set; }
        /// <summary>Compact-N CUID of the workspace that owns this version.</summary>
        public string WorkspaceCuid { get; set; }
        /// <summary>Compact-N CUID of the module that owns this version. Used to select the module DB.</summary>
        public string ModuleCuid { get; set; }
    }
}
