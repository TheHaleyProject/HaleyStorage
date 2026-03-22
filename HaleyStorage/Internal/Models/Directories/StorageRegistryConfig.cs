using Haley.Abstractions;
using Haley.Enums;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Haley.Models {
    /// <summary>
    /// Configuration that controls path-sharding and directory-name suffixes for the vault hierarchy.
    /// All suffix properties apply only to controlled names (not raw filenames).
    /// Bind from the <c>Seed:oss</c> configuration section via <see cref="StorageCoordinator.SetConfig"/>.
    /// </summary>
    internal class StorageRegistryConfig : IVaultRegistryConfig{
        /// <summary>Suffix appended to workspace directory names (default <c>"w"</c>).</summary>
        public string SuffixWorkSpace { get; set; } = "w";
        /// <summary>Suffix appended to file storage names (default <c>"f"</c>).</summary>
        public string SuffixFile { get; set; } = "f";
        /// <summary>Number of characters taken from the start of a numeric ID for each sharding level (default 2).</summary>
        public int SplitLengthNumber { get; set; } = 2;
        /// <summary>Maximum sharding depth for numeric IDs; 0 means a single flat level (default 0).</summary>
        public int DepthNumber { get; set; } = 0;
        /// <summary>Number of characters taken from a hash/GUID for each sharding level (default 2).</summary>
        public int SplitLengthHash { get; set; } = 2;
        /// <summary>Maximum sharding depth for hash/GUID IDs (default 7).</summary>
        public int DepthHash { get; set; } = 7;
        /// <summary>
        /// Number of revision backup copies to keep when a FileSystem file is overwritten
        /// (upload with an existing CUID). The most recent N revisions are retained as
        /// <c>&lt;filename&gt;.ext##R1</c>, <c>##R2</c>, … beside the live file.
        /// Set to 0 to disable revision backups entirely. Default: 3.
        /// </summary>
        public int MaxRevisionCopies { get; set; } = 3;

        public StorageRegistryConfig() { }
    }
}
