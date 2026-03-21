using System;

namespace Haley.Models {
    /// <summary>
    /// Returned by <c>POST /api/stage/session</c> when a new cloud staging session is created.
    /// On-prem code stores <see cref="SessionId"/> as the <c>StagingRef</c> for the DB record.
    /// </summary>
    internal class CloudStageSession {
        /// <summary>Opaque session identifier that the on-prem side stores as <c>StagingRef</c>.</summary>
        public string SessionId { get; set; }
        /// <summary>UTC timestamp after which this session is considered expired by the cloud service.</summary>
        public DateTime ExpiresAt { get; set; }
    }
}
