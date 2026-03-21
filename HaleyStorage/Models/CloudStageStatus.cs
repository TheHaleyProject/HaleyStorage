using System;

namespace Haley.Models {
    /// <summary>
    /// Response body of <c>GET /api/stage/status/{sessionId}</c>.
    /// Used by <c>CloudApiStagingProvider.Exists</c> and <c>GetSize</c> to avoid two round-trips.
    /// </summary>
    public class CloudStageStatus {
        /// <summary>Session identifier.</summary>
        public string SessionId { get; set; }
        /// <summary>Byte count of the stored object. 0 until <c>upload</c> completes.</summary>
        public long Size { get; set; }
        /// <summary>Optional SHA-256 hash of the stored object. Null when not computed.</summary>
        public string Hash { get; set; }
        /// <summary>
        /// Lifecycle state string as returned by the cloud service
        /// (<c>"pending"</c>, <c>"uploaded"</c>, <c>"deleted"</c>).
        /// </summary>
        public string State { get; set; }
        /// <summary>UTC timestamp when bytes were last successfully written. Null while pending.</summary>
        public DateTime? UploadedAt { get; set; }

        /// <summary>Convenience: returns <c>true</c> when bytes have been stored and are available.</summary>
        public bool IsAvailable => string.Equals(State, "uploaded", StringComparison.OrdinalIgnoreCase);
        /// <summary>Convenience: returns <c>true</c> when the session has been deleted.</summary>
        public bool IsDeleted   => string.Equals(State, "deleted",  StringComparison.OrdinalIgnoreCase);
    }
}
