using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace Haley.Services {
    /// <summary>
    /// <see cref="IStorageProvider"/> that stores bytes on a remote FuDog instance via its standard
    /// vault API (<c>api/va/file</c>). Can be used as a staging provider (for StageAndMove /
    /// StageAndRetainCopy workflows) or as a primary provider when all writes should go to a
    /// centralised FuDog cloud.
    /// <para>
    /// <b>Storage reference model:</b> <see cref="BuildStorageRef"/> returns the logical GUID
    /// unchanged — no sharding. This flat ID is used as the <c>fn</c> filename on every remote
    /// request, making every stored object uniquely addressable by name within the configured scope.
    /// </para>
    /// <para>
    /// Every HTTP request carries an <c>X-Api-Key</c> header so the remote instance can
    /// optionally enforce per-tenant authentication.
    /// </para>
    /// </summary>
    public class FuDogApiProvider : IStorageProvider {
        public const string PROVIDER_KEY = "FuDogApi";
        public string Key { get; set; } = PROVIDER_KEY;

        readonly HttpClient _http;
        readonly FuDogApiProviderConfig _config;
        readonly ILogger _logger;

        public FuDogApiProvider(HttpClient http, FuDogApiProviderConfig config, ILogger logger = null) {
            _http   = http   ?? throw new ArgumentNullException(nameof(http));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger;

            _http.BaseAddress = new Uri(_config.BaseUrl.TrimEnd('/') + "/");
            _http.Timeout     = TimeSpan.FromSeconds(_config.TimeoutSeconds > 0 ? _config.TimeoutSeconds : 30);
            _http.DefaultRequestHeaders.Remove("X-Api-Key");
            if (!string.IsNullOrWhiteSpace(_config.ApiKey))
                _http.DefaultRequestHeaders.Add("X-Api-Key", _config.ApiKey);
        }

        // ── Base URL access ───────────────────────────────────────────────────

        /// <summary>Returns the configured base URL of the remote FuDog instance.</summary>
        public string GetBaseUrl() => _config.BaseUrl;

        // ── Build storage reference ───────────────────────────────────────────

        /// <summary>
        /// Returns the logical ID as-is — no directory sharding.
        /// The remote FuDog stores files in a flat namespace keyed by this ID as the display name.
        /// Extension is omitted; the remote derives it from the multipart filename on upload.
        /// </summary>
        public string BuildStorageRef(string logicalId, string extension, Func<bool, (int length, int depth)> splitProvider, string suffix)
            => logicalId;

        /// <summary>
        /// Joins key prefix with the ID using forward slashes. Rejects keys containing <c>..</c>.
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
        /// Uploads <paramref name="dataStream"/> to the remote FuDog instance's standard file
        /// endpoint (<c>POST api/va/file</c>). The <paramref name="storagePath"/> (local version
        /// CUID) is used as the remote filename so the file can be retrieved by name later.
        /// Re-uploading the same ID creates a new version on the remote (Revise mode is default).
        /// </summary>
        public async Task<ProviderWriteResult> WriteAsync(string storagePath, Stream dataStream, int bufferSize, ExistConflictResolveMode conflictMode) {
            try {
                var scope = BuildScopeQuery();
                var url   = $"api/va/file?{scope}&fn={Uri.EscapeDataString(storagePath)}";

                var content = new MultipartFormDataContent();
                var streamContent = new StreamContent(dataStream, bufferSize > 0 ? bufferSize : 81920);
                streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                // Use storagePath as both the form field name and the filename.
                // The remote VaultCoreControllerBase reads the Content-Disposition filename as the stored name.
                content.Add(streamContent, "file", storagePath);

                var resp = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Post, url) { Content = content });
                if (!resp.IsSuccessStatusCode)
                    return ProviderWriteResult.Fail($"Remote upload failed ({resp.StatusCode}) for key {storagePath}.");

                return ProviderWriteResult.Ok();
            } catch (Exception ex) {
                _logger?.LogError(ex, "FuDogApiProvider.WriteAsync failed for {Key}", storagePath);
                return ProviderWriteResult.Fail(ex.Message);
            }
        }

        // ── Read ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Downloads bytes from the remote FuDog instance (<c>GET api/va/file</c>).
        /// Looks up the file by the <paramref name="storagePath"/> used as the display name (<c>fn</c>).
        /// Returns a network stream — the caller is responsible for disposing it.
        /// </summary>
        public async Task<ProviderReadResult> ReadAsync(string storagePath, bool autoSearchExtension = true, StringComparison nameComparison = StringComparison.OrdinalIgnoreCase) {
            try {
                var scope = BuildScopeQuery();
                var url   = $"api/va/file?{scope}&fn={Uri.EscapeDataString(storagePath)}";
                var resp  = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, url));
                if (!resp.IsSuccessStatusCode)
                    return ProviderReadResult.Fail($"Remote read failed ({resp.StatusCode}) for key {storagePath}.");
                var stream = await resp.Content.ReadAsStreamAsync();
                return ProviderReadResult.Ok(stream, extension: string.Empty);
            } catch (Exception ex) {
                _logger?.LogError(ex, "FuDogApiProvider.ReadAsync failed for {Key}", storagePath);
                return ProviderReadResult.Fail(ex.Message);
            }
        }

        // ── Delete ────────────────────────────────────────────────────────────

        /// <summary>
        /// Deletes the remote file. Currently the remote FuDog delete endpoint is not exposed;
        /// this is a no-op that logs and returns <c>true</c> to avoid blocking callers.
        /// </summary>
        public Task<bool> DeleteAsync(string storagePath) {
            _logger?.LogWarning("FuDogApiProvider.DeleteAsync called for {Key} — remote delete is not yet implemented.", storagePath);
            return Task.FromResult(true); // non-fatal: caller logs but continues
        }

        // ── Exists / GetSize ──────────────────────────────────────────────────

        /// <summary>
        /// Returns true when the file exists on the remote instance.
        /// Checks via the file-details endpoint using the display-name (<c>fn</c>) lookup.
        /// </summary>
        public bool Exists(string storagePath)
            => GetRemoteSize(storagePath).GetAwaiter().GetResult() >= 0;

        /// <summary>
        /// Returns the byte count of the remote file, or 0 if unknown / not found.
        /// </summary>
        public long GetSize(string storagePath)
            => Math.Max(0, GetRemoteSize(storagePath).GetAwaiter().GetResult());

        // ── Access URL ────────────────────────────────────────────────────────

        /// <summary>
        /// Retrieves a temporary access URL from the remote FuDog instance's
        /// <c>GET api/va/file/url</c> endpoint.
        /// Returns <c>null</c> when the remote returns no URL (e.g. FS-backed remote).
        /// </summary>
        public async Task<string> GetAccessUrl(string storageRef, TimeSpan expiry) {
            try {
                var scope   = BuildScopeQuery();
                var seconds = (int)Math.Max(1, expiry.TotalSeconds);
                var url     = $"api/va/file/url?{scope}&fn={Uri.EscapeDataString(storageRef)}&expiry={seconds}";
                var resp    = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, url));
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
        /// Returns the remote file size via <c>GET api/va/file/details</c>.
        /// Returns -1 when the file is not found; 0 when size is unknown.
        /// </summary>
        async Task<long> GetRemoteSize(string storagePath) {
            try {
                var scope = BuildScopeQuery();
                var url   = $"api/va/file/details?{scope}&fn={Uri.EscapeDataString(storagePath)}";
                var resp  = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, url));
                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return -1;
                if (!resp.IsSuccessStatusCode) return -1;
                var body  = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                // Response is VaultFileDetailsResponse. Size lives on the latest version entry.
                if (doc.RootElement.TryGetProperty("versions", out var versions) && versions.ValueKind == JsonValueKind.Array) {
                    foreach (var v in versions.EnumerateArray()) {
                        if (v.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.Number)
                            return sz.GetInt64();
                    }
                }
                return 0;
            } catch {
                return -1;
            }
        }

        /// <summary>Builds the shared scope query string from the configured client/module/workspace.</summary>
        string BuildScopeQuery() {
            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(_config.Client))    parts.Add($"c={Uri.EscapeDataString(_config.Client)}");
            if (!string.IsNullOrWhiteSpace(_config.Module))    parts.Add($"m={Uri.EscapeDataString(_config.Module)}");
            if (!string.IsNullOrWhiteSpace(_config.Workspace)) parts.Add($"w={Uri.EscapeDataString(_config.Workspace)}");
            return string.Join("&", parts);
        }

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
