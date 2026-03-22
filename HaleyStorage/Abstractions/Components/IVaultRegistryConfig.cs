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
    }
}
