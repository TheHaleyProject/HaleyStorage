namespace Haley.Models {
    /// <summary>
    /// Configuration for <see cref="Haley.Services.StagingPromotionWorker"/>.
    /// Passed to <c>AddStagingPromotion(config)</c> during DI registration.
    /// </summary>
    public class StagingPromotionConfig {
        /// <summary>
        /// Compact-N CUID of the module whose <c>version_info</c> table is polled for
        /// staged-but-not-yet-promoted rows. Required.
        /// </summary>
        public string ModuleCuid { get; set; }
        /// <summary>
        /// How often the promotion worker wakes up to check for pending staged versions.
        /// Default: 60 seconds.
        /// </summary>
        public int PollingIntervalSeconds { get; set; } = 60;
        /// <summary>
        /// Maximum number of staged versions promoted per worker tick.
        /// Prevents a single tick from monopolising the storage layer under backlog.
        /// Default: 20.
        /// </summary>
        public int BatchSize { get; set; } = 20;
    }
}
