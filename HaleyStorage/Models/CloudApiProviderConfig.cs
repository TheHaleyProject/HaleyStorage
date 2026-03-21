namespace Haley.Models {
    /// <summary>
    /// Configuration for <c>CloudApiStagingProvider</c>.
    /// Loaded from the <c>CloudApiStaging</c> section of <c>appsettings.json</c>
    /// and injected into the provider constructor.
    /// </summary>
    public class CloudApiProviderConfig {
        /// <summary>Base URL of the FuDog cloud service (e.g. <c>"https://cloud.haley.ai"</c>).</summary>
        public string BaseUrl { get; set; }
        /// <summary>Per-tenant API key sent as the <c>X-Stage-Key</c> request header.</summary>
        public string ApiKey { get; set; }
        /// <summary>HTTP request timeout in seconds. Default: 30.</summary>
        public int TimeoutSeconds { get; set; } = 30;
        /// <summary>Number of automatic retries on transient HTTP errors. Default: 2.</summary>
        public int MaxRetries { get; set; } = 2;
    }
}
