namespace Haley.Models {
    /// <summary>
    /// Returned by <see cref="Haley.Abstractions.IStorageCoordinator.CreatePlaceholder"/> after a
    /// placeholder DB record is reserved. The caller uses these values to copy the file out-of-band
    /// (USB, server-side move, cloud-native copy) and then calls
    /// <see cref="Haley.Abstractions.IStorageCoordinator.FinalizePlaceholder"/> to mark it complete.
    /// </summary>
    public class PlaceholderInfo {
        /// <summary>Auto-increment DB ID of the <c>doc_version</c> row. Required for FinalizePlaceholder.</summary>
        public long VersionId { get; set; }
        /// <summary>Compact-N GUID of the <c>doc_version</c> row.</summary>
        public string VersionCuid { get; set; }
        /// <summary>
        /// Provider-level storage identifier (e.g. <c>"1234567.mp4"</c> for Number mode,
        /// <c>"abcdef01.mp4"</c> for Guid mode). Used as the object key by cloud providers.
        /// </summary>
        public string StorageName { get; set; }
        /// <summary>
        /// Full target location in primary storage.
        /// FileSystem: absolute path on disk (parent directory is pre-created).
        /// Cloud: object key within the provider's bucket/container.
        /// Copy your file here, then call FinalizePlaceholder with toStaging=false.
        /// </summary>
        public string StorageRef { get; set; }
        /// <summary>
        /// Full target location in staging storage, or <c>null</c> if staging is not configured
        /// for this workspace/module.
        /// Copy your file here, then call FinalizePlaceholder with toStaging=true.
        /// </summary>
        public string StagingRef { get; set; }
    }
}
