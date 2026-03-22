using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Haley.Services {
    /// <summary>
    /// <see cref="IStorageProvider"/> implementation that stores bytes on the FuDog cloud
    /// staging API (<c>api/stage/*</c>) instead of local disk.
    /// <para>
    /// Every HTTP request carries an <c>X-Stage-Key</c> header so the cloud service can
    /// resolve the tenant workspace from the per-tenant API key.
    /// </para>
    /// <para>
    /// <b>Storage reference model:</b> <see cref="BuildStorageRef"/> returns a flat, unsharded
    /// session ID (the logical GUID without any directory structure). This ID is used as the
    /// <c>sessionId</c> parameter for all cloud endpoints.
    /// </para>
    /// </summary>
    public class CloudApiStagingProvider : IStorageProvider {
        public const string PROVIDER_KEY = "CloudApiStaging";
        public string Key { get; set; } = PROVIDER_KEY;

        readonly HttpClient _http;
        readonly CloudApiProviderConfig _config;
        readonly ILogger _logger;

        public CloudApiStagingProvider(HttpClient http, CloudApiProviderConfig config, ILogger logger = null) {
            _http   = http   ?? throw new ArgumentNullException(nameof(http));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger;

            _http.BaseAddress = new Uri(_config.BaseUrl.TrimEnd('/') + "/");
            _http.Timeout     = TimeSpan.FromSeconds(_config.TimeoutSeconds > 0 ? _config.TimeoutSeconds : 30);
            _http.DefaultRequestHeaders.Remove("X-Stage-Key");
            _http.DefaultRequestHeaders.Add("X-Stage-Key", _config.ApiKey);
        }

        // ── Build storage reference ───────────────────────────────────────────

        /// <summary>
        /// Returns the logical ID as-is — no directory sharding.
        /// The cloud service uses flat session IDs as object keys.
        /// Extension is intentionally omitted; the cloud session is content-agnostic.
        /// </summary>
        public string BuildStorageRef(string logicalId, string extension, Func<bool, (int length, int depth)> splitProvider, string suffix)
            => logicalId;

        /// <summary>
        /// Joins the key prefix with the session ID using forward slashes (cloud object key style).
        /// An empty prefix is valid — staging sessions can be flat top-level keys.
        /// Rejects keys containing ".." to prevent object-key injection.
        /// </summary>
        public string BuildFullPath(string basePath, string fileRef) {
            if (string.IsNullOrEmpty(fileRef)) return basePath;
            var result = string.IsNullOrEmpty(basePath)
                ? fileRef
                : basePath.TrimEnd('/') + "/" + fileRef.TrimStart('/');
            if (result.Contains(".."))
                throw new ArgumentOutOfRangeException(nameof(fileRef), "Key contains invalid traversal segments.");
            return result;
        }

        // ── Write ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a cloud staging session for <paramref name="storagePath"/> (the session ID),
        /// then POSTs the byte stream to the upload endpoint.
        /// Re-uploading the same session ID is idempotent (cloud uses Replace mode).
        /// </summary>
        public async Task<ProviderWriteResult> WriteAsync(string storagePath, Stream dataStream, int bufferSize, ExistConflictResolveMode conflictMode) {
            try {
                // Step 1: create session (idempotent — server uses Replace if session exists).
                var sessionBody = JsonSerializer.Serialize(new { sessionId = storagePath });
                var sessionReq  = new StringContent(sessionBody, Encoding.UTF8, "application/json");
                var sessionResp = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Post, "api/stage/session") { Content = new StringContent(sessionBody, Encoding.UTF8, "application/json") });
                if (!sessionResp.IsSuccessStatusCode)
                    return ProviderWriteResult.Fail($"Failed to create staging session: {sessionResp.StatusCode}");

                // Step 2: upload bytes.
                var uploadContent = new StreamContent(dataStream, bufferSize > 0 ? bufferSize : 81920);
                uploadContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                var uploadResp = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Post, $"api/stage/upload/{Uri.EscapeDataString(storagePath)}") { Content = uploadContent });

                if (!uploadResp.IsSuccessStatusCode)
                    return ProviderWriteResult.Fail($"Failed to upload to staging session {storagePath}: {uploadResp.StatusCode}");

                return ProviderWriteResult.Ok();
            } catch (Exception ex) {
                _logger?.LogError(ex, "CloudApiStagingProvider.WriteAsync failed for {SessionId}", storagePath);
                return ProviderWriteResult.Fail(ex.Message);
            }
        }

        // ── Read ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Streams bytes from the cloud staging session.
        /// Returns a network stream — the caller is responsible for disposing it.
        /// </summary>
        public async Task<ProviderReadResult> ReadAsync(string storagePath, bool autoSearchExtension = true, StringComparison nameComparison = StringComparison.OrdinalIgnoreCase) {
            try {
                var resp = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, $"api/stage/stream/{Uri.EscapeDataString(storagePath)}"));
                if (!resp.IsSuccessStatusCode)
                    return ProviderReadResult.Fail($"Cloud stream failed for session {storagePath}: {resp.StatusCode}");
                var stream = await resp.Content.ReadAsStreamAsync();
                return ProviderReadResult.Ok(stream, extension: string.Empty);
            } catch (Exception ex) {
                _logger?.LogError(ex, "CloudApiStagingProvider.ReadAsync failed for {SessionId}", storagePath);
                return ProviderReadResult.Fail(ex.Message);
            }
        }

        // ── Delete ────────────────────────────────────────────────────────────

        public async Task<bool> DeleteAsync(string storagePath) {
            try {
                var resp = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Delete, $"api/stage/object/{Uri.EscapeDataString(storagePath)}"));
                return resp.IsSuccessStatusCode;
            } catch (Exception ex) {
                _logger?.LogError(ex, "CloudApiStagingProvider.DeleteAsync failed for {SessionId}", storagePath);
                return false;
            }
        }

        // ── Exists / GetSize ──────────────────────────────────────────────────

        /// <summary>
        /// Returns true when the session exists and has not been deleted.
        /// Blocking call — the underlying status check is async, but <see cref="IStorageProvider"/>
        /// requires a synchronous signature.
        /// </summary>
        public bool Exists(string storagePath)
            => GetStatusAsync(storagePath).GetAwaiter().GetResult()?.IsDeleted == false;

        /// <summary>
        /// Returns the byte count of the stored object, or 0 if unknown/not yet uploaded.
        /// Blocking call — see <see cref="Exists"/> for rationale.
        /// </summary>
        public long GetSize(string storagePath)
            => GetStatusAsync(storagePath).GetAwaiter().GetResult()?.Size ?? 0L;

        // ── Access URL ────────────────────────────────────────────────────────

        /// <summary>
        /// Retrieves a temporary access URL from the cloud service.
        /// v1 cloud implementations backed by FileSystem always return null.
        /// Future cloud-native providers may return a pre-signed URL here.
        /// </summary>
        public async Task<string> GetAccessUrl(string storageRef, TimeSpan expiry) {
            try {
                var seconds = (int)Math.Max(1, expiry.TotalSeconds);
                var resp = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, $"api/stage/access/{Uri.EscapeDataString(storageRef)}?expiry={seconds}"));
                if (!resp.IsSuccessStatusCode) return null;
                var body = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("url", out var urlEl) && urlEl.ValueKind != JsonValueKind.Null)
                    return urlEl.GetString();
                return null;
            } catch {
                return null;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Fetches the session status JSON from the cloud and deserialises it.
        /// Returns null on any error (HTTP or deserialization).
        /// Shared by <see cref="Exists"/> and <see cref="GetSize"/>.
        /// </summary>
        async Task<CloudStageStatus> GetStatusAsync(string sessionId) {
            try {
                var resp = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, $"api/stage/status/{Uri.EscapeDataString(sessionId)}"));
                if (!resp.IsSuccessStatusCode) return null;
                var body = await resp.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<CloudStageStatus>(body, _jsonOptions);
            } catch (Exception ex) {
                _logger?.LogWarning(ex, "CloudApiStagingProvider.GetStatusAsync failed for {SessionId}", sessionId);
                return null;
            }
        }

        static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true,
        };

        /// <summary>
        /// Sends an HTTP request with simple linear retry on transient failures.
        /// The message factory is called for each attempt because <see cref="HttpRequestMessage"/>
        /// cannot be sent more than once.
        /// </summary>
        async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> messageFactory) {
            int attempts = Math.Max(1, _config.MaxRetries + 1);
            HttpResponseMessage last = null;
            for (int i = 0; i < attempts; i++) {
                try {
                    last = await _http.SendAsync(messageFactory(), HttpCompletionOption.ResponseHeadersRead);
                    // Retry only on 5xx or request-timeout.
                    if ((int)last.StatusCode < 500) return last;
                } catch (TaskCanceledException) when (i < attempts - 1) {
                    // timeout — retry
                } catch (HttpRequestException) when (i < attempts - 1) {
                    // transient — retry
                }
                if (i < attempts - 1)
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, i)));
            }
            return last ?? throw new InvalidOperationException("No HTTP response received.");
        }
    }
}
