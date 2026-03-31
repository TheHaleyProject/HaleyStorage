using Haley.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Haley.Models {
    /// <summary>
    /// Configuration that controls path-sharding and directory-name suffixes for the vault hierarchy.
    /// All suffix properties apply only to controlled names (not raw filenames).
    /// Bind from the <c>Seed:OSSConfig</c> configuration section via <see cref="StorageCoordinator.SetConfig"/>.
    /// Short JSON key names are defined via <see cref="ConfigurationKeyNameAttribute"/>.
    /// </summary>
    internal class StorageRegistryConfig : IVaultRegistryConfig {
        /// <summary>Suffix appended to file storage names (default <c>"f"</c>). JSON: <c>suffixfile</c></summary>
        [ConfigurationKeyName("suffixfile")]
        public string SuffixFile { get; set; } = "f";

        /// <summary>Number of characters taken from the start of a numeric ID for each sharding level (default 2). JSON: <c>splitnum</c></summary>
        [ConfigurationKeyName("splitnum")]
        public int SplitLengthNumber { get; set; } = 2;

        /// <summary>Maximum sharding depth for numeric IDs; 0 means a single flat level (default 0). JSON: <c>depthnum</c></summary>
        [ConfigurationKeyName("depthnum")]
        public int DepthNumber { get; set; } = 0;

        /// <summary>Number of characters taken from a hash/GUID for each sharding level (default 2). JSON: <c>splithash</c></summary>
        [ConfigurationKeyName("splithash")]
        public int SplitLengthHash { get; set; } = 2;

        /// <summary>Maximum sharding depth for hash/GUID IDs (default 7). JSON: <c>depthhash</c></summary>
        [ConfigurationKeyName("depthhash")]
        public int DepthHash { get; set; } = 7;

        /// <summary>
        /// Number of revision backup copies to keep when a FileSystem file is overwritten.
        /// The most recent N revisions are retained as <c>&lt;filename&gt;.ext##R1</c>, <c>##R2</c>, … beside the live file.
        /// Set to 0 to disable revision backups entirely. Default: 3. JSON: <c>maxrev</c>
        /// </summary>
        [ConfigurationKeyName("maxrev")]
        public int MaxRevisionCopies { get; set; } = 3;

        /// <summary>When <c>true</c>, metadata can be set on any version — not just the latest. Default <c>false</c>. JSON: <c>oldvermeta</c></summary>
        [ConfigurationKeyName("oldvermeta")]
        public bool AllowMetadataOnOldVersions { get; set; } = false;

        /// <summary>Maximum allowed thumbnail file size in kilobytes. Default 500 KB. JSON: <c>thumbmaxkb</c></summary>
        [ConfigurationKeyName("thumbmaxkb")]
        public int ThumbMaxSizeKb { get; set; } = 500;

        /// <summary>Comma-separated allowed thumbnail extensions (lowercase, no dot). Default "jpeg,jpg,png,webp,gif". JSON: <c>thumbexts</c></summary>
        [ConfigurationKeyName("thumbexts")]
        public string ThumbAllowedExtensions { get; set; } = "jpeg,jpg,png,webp,gif";

        public StorageRegistryConfig() { }
    }
}
