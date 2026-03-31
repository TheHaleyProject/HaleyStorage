using Haley.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Haley.Abstractions {
    public interface IVaultRegistryConfig {
        string SuffixFile { get; set; }
        int SplitLengthNumber { get; set; } 
        int DepthNumber { get; set; } 
        int SplitLengthHash { get; set; }
        int DepthHash { get; set; }
        int MaxRevisionCopies { get; set; }
        /// <summary>
        /// When <c>true</c>, metadata can be set on any version — not just the latest.
        /// Default <c>false</c>. Set to <c>true</c> only for bulk data migration scenarios.
        /// </summary>
        bool AllowMetadataOnOldVersions { get; set; }
    }
}
