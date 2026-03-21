using Haley.Abstractions;
using System.Collections.Generic;

namespace Haley.Models {
    public class VaultSummaryResponse :Feedback, IVaultSummaryResponse {
        public int Passed { get; set; }
        public int Failed { get; set; }
        public string TotalSizeUploaded { get; set; }
        public List<IVaultResponse> PassedObjects { get; set; } = new List<IVaultResponse>();
        public List<IVaultResponse> FailedObjects { get; set; } = new List<IVaultResponse>();
        public VaultSummaryResponse() {  }
    }
}
