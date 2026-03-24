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
    /// <see cref="IStorageProvider"/> that stores bytes on a remote HaleyStorage instance via its
    /// standard vault API (<c>api/va/file</c>). Can be used as a staging provider (for StageAndMove /
    /// StageAndRetainCopy workflows) or as a primary provider when all writes should go to a
    /// centralised remote server.
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
    public class GenericApiProvider : IStorageProvider {
        public const string PROVIDER_KEY = "GenericApi";
        public string Key { get; set; } = PROVIDER_KEY;

        readonly HttpClient _http;
        readonly GenericApiProviderConfig _config;
        readonly ILogger _logger;

        public GenericApiProvider(HttpClient http, GenericApiProviderConfig config, ILogger logger = null) {
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

        /// <summary>Returns the configured base URL of the remote instance.</summary>
        public string GetBaseUrl() => _config.BaseUrl;

        // ── Build storage reference ───────────────────────────────────────────

        /// <summary>
        /// Returns the logical ID as-is — no directory sharding.
        /// The remote server stores files in a flat namespace keyed by this ID as the display name.
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
        /// Uploads <paramref name="dataStream"/> to the remote instance's standard file endpoint
        /// (<c>POST api/va/file</c>). The <paramref name="storagePath"/> (local version CUID) is
        /// used as the remote filename so the file can be retrieved by name later.
        /// Re-uploading the same ID creates a new version on the remote (Revise mode is default).
        /// </summary>
        public async Task<ProviderWriteResult> WriteAsync(string storagePath, Stream dataStream, int bufferSize, ExistConflictResolveMode conflictMode) {
            try {
                var scope = BuildScopeQuery();
                var url   = $"api/va/file?{scope}&fn={Uri.EscapeDataString(storagePath)}";

                var content = new MultipartFormDataContent();
                var streamContent = new StreamContent(dataStream, bufferSize > 0 ? bufferSize : 81920);
                streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                content.Add(streamContent, "file", storagePath);

                var resp = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Post, url) { Content = content });
                if (!resp.IsSuccessStatusCode)
                    return ProviderWriteResult.Fail($"Remote upload failed ({resp.StatusCode}) for key {storagePath}.");

                return ProviderWriteResult.Ok();
            } catch (Exception ex) {
                _logger?.LogError(ex, "GenericApiProvider.WriteAsync failed for {Key}", storagePath);
                return ProviderWriteResult.Fail(ex.Message);
            }
        }

        // ── Read ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Downloads bytes from the remote instance (<c>GET api/va/file</c>).
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
                _logger?.LogError(ex, "GenericApiProvider.ReadAsync failed for {Key}", storagePath);
                return ProviderReadResult.Fail(ex.Message);
            }
        }

        // ── Delete ────────────────────────────────────────────────────────────

        /// <summary>
        /// Deletes the remote file. Currently the remote delete endpoint is not exposed;
        /// this is a no-op that logs and returns <c>true</c> to avoid blocking callers.
        /// </summary>
        public Task<bool> DeleteAsync(string storagePath) {
            _logger?.LogWarning("GenericApiProvider.DeleteAsync called for {Key} — remote delete is not yet implemented.", storagePath);
            return Task.FromResult(true);
        }

        // ── Exists / GetSize ──────────────────────────────────────────────────

        /// <summary>Returns true when the file exists on the remote instance.</summary>
        public bool Exists(string storagePath)
            => GetRemoteSize(storagePath).GetAwaiter().GetResult() >= 0;

        /// <summary>Returns the byte count of the remote file, or 0 if unknown / not found.</summary>
        public long GetSize(string storagePath)
            => Math.Max(0, GetRemoteSize(storagePath).GetAwaiter().GetResult());

        // ── Access URL ────────────────────────────────────────────────────────

        /// <summary>
        /// Retrieves a temporary access URL from the remote instance's <c>GET api/va/file/url</c> endpoint.
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

        async Task<long> GetRemoteSize(string storagePath) {
            try {
                var scope = BuildScopeQuery();
                var url   = $"api/va/file/details?{scope}&fn={Uri.EscapeDataString(storagePath)}";
                var resp  = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, url));
                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return -1;
                if (!resp.IsSuccessStatusCode) return -1;
                var body  = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
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

        string BuildScopeQuery() {
            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(_config.Client))    parts.Add($"c={Uri.EscapeDataString(_config.Client)}");
            if (!string.IsNullOrWhiteSpace(_config.Module))    parts.Add($"m={Uri.EscapeDataString(_config.Module)}");
            if (!string.IsNullOrWhiteSpace(_config.Workspace)) parts.Add($"w={Uri.EscapeDataString(_config.Workspace)}");
            return string.Join("&", parts);
        }

        async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> messageFactory) {
            int attempts = Math.Max(1, _config.MaxRetries + 1);
            HttpResponseMessage last = null;
            for (int i = 0; i < attempts; i++) {
                try {
                    last = await _http.SendAsync(messageFactory(), HttpCompletionOption.ResponseHeadersRead);
                    if ((int)last.StatusCode < 500) return last;
                } catch (TaskCanceledException) when (i < attempts - 1) {
                } catch (HttpRequestException) when (i < attempts - 1) {
                }
                if (i < attempts - 1)
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, i)));
            }
            return last ?? throw new InvalidOperationException("No HTTP response received.");
        }
    }
}
