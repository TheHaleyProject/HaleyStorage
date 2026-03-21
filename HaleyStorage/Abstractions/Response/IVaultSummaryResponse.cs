using System.Collections.Generic;

namespace Haley.Abstractions {
    public interface IVaultSummaryResponse : IFeedback {
        int Passed { get; set; }
        int Failed { get; set; }
        string TotalSizeUploaded { get; set; }
        List<IVaultResponse> PassedObjects { get; set; }
        List<IVaultResponse> FailedObjects { get; set; }
    }
}
