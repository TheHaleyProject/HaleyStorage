namespace Haley.Models {
    /// <summary>
    /// Configuration for <see cref="FuDogApiProvider"/>.
    /// Points to a remote FuDog instance and supplies the scope (client/module/workspace) under
    /// which files will be stored on that instance.
    /// </summary>
    public class FuDogApiProviderConfig {
        /// <summary>Base URL of the remote FuDog instance (e.g. <c>"https://cloud.haley.ai"</c>).</summary>
        public string BaseUrl { get; set; }
        /// <summary>API key sent as the <c>X-Api-Key</c> request header on every call.</summary>
        public string ApiKey { get; set; }
        /// <summary>Client name on the remote FuDog instance (maps to the <c>c</c> query param).</summary>
        public string Client { get; set; }
        /// <summary>Module name on the remote FuDog instance (maps to the <c>m</c> query param).</summary>
        public string Module { get; set; }
        /// <summary>Workspace name on the remote FuDog instance (maps to the <c>w</c> query param).</summary>
        public string Workspace { get; set; }
        /// <summary>HTTP request timeout in seconds. Default: 30.</summary>
        public int TimeoutSeconds { get; set; } = 30;
        /// <summary>Number of automatic retries on transient HTTP errors. Default: 2.</summary>
        public int MaxRetries { get; set; } = 2;
    }
}
