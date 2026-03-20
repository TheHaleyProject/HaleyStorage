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
    public class StorageRegistryConfig : IVaultRegistryConfig{
        /// <summary>Suffix appended to client directory names (default <c>"c"</c>).</summary>
        public string SuffixClient { get; set; } = "c";
        /// <summary>Suffix appended to module directory names (default <c>"m"</c>).</summary>
        public string SuffixModule { get; set; } = "m";
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

        public StorageRegistryConfig() { }
    }
}
