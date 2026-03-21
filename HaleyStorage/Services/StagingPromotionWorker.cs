using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Haley.Services {
    /// <summary>
    /// Background worker that promotes staged files to primary storage on a configurable polling interval.
    /// <para>
    /// For each pending <see cref="StagedVersionRef"/> (flags bit 4 set, bit 8 not set, synced_at NULL):
    /// <list type="number">
    ///   <item>Resolves primary and staging providers from the version's <c>profile_info_id</c>.</item>
    ///   <item>Attempts <c>GetAccessUrl</c> on the staging provider; if a URL is returned the bytes
    ///         are fetched via <see cref="System.Net.Http.HttpClient"/> so the on-prem server is not
    ///         a bandwidth bottleneck. Otherwise falls back to <c>ReadAsync</c>.</item>
    ///   <item>Writes bytes to the primary provider at the pre-computed <c>storage_ref</c>.</item>
    ///   <item>Calls <c>UpdateVersionPromotion</c> with new flags based on <see cref="StorageProfileMode"/>:
    ///         <c>StageAndMove → 8|64</c>, <c>StageAndRetainCopy → 4|8|64</c>.</item>
    ///   <item>For <c>StageAndMove</c>: calls <c>DeleteAsync</c> on the staging provider (non-fatal on failure).</item>
    /// </list>
    /// </para>
    /// <para>
    /// An in-flight <see cref="ConcurrentDictionary{TKey,TValue}"/> prevents double-promotion
    /// within a single process. The <c>synced_at IS NULL</c> SQL guard prevents cross-process double-work.
    /// </para>
    /// </summary>
    public class StagingPromotionWorker : BackgroundService {

        readonly IStorageCoordinator _coordinator;
        readonly StagingPromotionConfig _config;
        readonly ILogger _logger;
        readonly ConcurrentDictionary<long, byte> _inFlight = new();

        public StagingPromotionWorker(
            IStorageCoordinator coordinator,
            StagingPromotionConfig config,
            ILogger<StagingPromotionWorker> logger = null) {
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _config      = config      ?? throw new ArgumentNullException(nameof(config));
            _logger      = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            _logger?.LogInformation("StagingPromotionWorker started. Module: {ModuleCuid}, interval: {Sec}s",
                _config.ModuleCuid, _config.PollingIntervalSeconds);

            while (!stoppingToken.IsCancellationRequested) {
                try {
                    await PromoteBatchAsync(stoppingToken);
                } catch (Exception ex) when (!(ex is OperationCanceledException)) {
                    _logger?.LogError(ex, "Unhandled error in StagingPromotionWorker tick.");
                }
                await Task.Delay(TimeSpan.FromSeconds(_config.PollingIntervalSeconds), stoppingToken);
            }
        }

        async Task PromoteBatchAsync(CancellationToken ct) {
            if (_coordinator is not StorageCoordinator sc) return;
            var indexer = sc.Indexer;
            if (indexer == null) return;

            var pending = await indexer.GetPendingStagedVersions(_config.ModuleCuid, _config.BatchSize);
            foreach (var ver in pending) {
                if (ct.IsCancellationRequested) break;
                if (ver.VersionId < 1 || string.IsNullOrWhiteSpace(ver.StagingRef)) continue;
                if (!_inFlight.TryAdd(ver.VersionId, 0)) continue;   // already being promoted in this process
                try {
                    await PromoteVersionAsync(sc, indexer, ver, ct);
                } finally {
                    _inFlight.TryRemove(ver.VersionId, out _);
                }
            }
        }

        async Task PromoteVersionAsync(StorageCoordinator sc, IVaultIndexing indexer, StagedVersionRef ver, CancellationToken ct) {
            var (primary, staging, mode) = sc.GetProvidersForProfile(ver.ProfileInfoId, ver.ModuleCuid);
            if (primary == null) {
                _logger?.LogWarning("No primary provider resolved for versionId={Vid}. Skipping.", ver.VersionId);
                return;
            }
            if (staging == null) {
                _logger?.LogWarning("No staging provider resolved for versionId={Vid}. Skipping.", ver.VersionId);
                return;
            }

            // Step 1: get bytes — try pre-signed URL first, then direct stream.
            Stream byteStream = null;
            try {
                var accessUrl = await staging.GetAccessUrl(ver.StagingRef, TimeSpan.FromMinutes(10));
                if (!string.IsNullOrWhiteSpace(accessUrl)) {
                    using var http = new System.Net.Http.HttpClient();
                    byteStream = await http.GetStreamAsync(accessUrl, ct);
                }
            } catch (Exception ex) {
                _logger?.LogDebug(ex, "Pre-signed URL fetch failed for versionId={Vid}; falling back to ReadAsync.", ver.VersionId);
                byteStream = null;
            }

            if (byteStream == null) {
                var readResult = await staging.ReadAsync(ver.StagingRef);
                if (!readResult.Success) {
                    _logger?.LogError("ReadAsync from staging failed for versionId={Vid}: {Msg}", ver.VersionId, readResult.Message);
                    return;
                }
                byteStream = readResult.Stream;
            }

            // Step 2: write to primary storage.
            bool writeOk = false;
            long writtenSize = 0;
            using (byteStream) {
                var writeResult = await primary.WriteAsync(ver.StorageRef, byteStream, 81920, ExistConflictResolveMode.Replace);
                writeOk = writeResult.Success;
                if (!writeOk) {
                    _logger?.LogError("Primary WriteAsync failed for versionId={Vid}: {Msg}", ver.VersionId, writeResult.Message);
                    return;
                }
            }

            // Get the actual written size from the primary provider after write completes.
            try { writtenSize = primary.GetSize(ver.StorageRef); } catch { /* non-fatal */ }

            // Step 3: update DB — set storage_ref, new flags, synced_at, and the confirmed size.
            int newFlags = mode == StorageProfileMode.StageAndRetainCopy
                ? 4 | 8 | 64    // InStaging | InStorage | Completed — cloud copy kept
                : 8 | 64;       // InStorage | Completed — cloud copy to be deleted

            var updateFb = await indexer.UpdateVersionPromotion(
                ver.ModuleCuid, ver.VersionId, ver.StorageRef, newFlags, DateTime.UtcNow, writtenSize);

            if (updateFb == null || !updateFb.Status) {
                _logger?.LogError("UpdateVersionPromotion failed for versionId={Vid}: {Msg}", ver.VersionId, updateFb?.Message);
                return;
            }

            _logger?.LogInformation("Promoted versionId={Vid} → {Ref} (flags={Flags})",
                ver.VersionId, ver.StorageRef, newFlags);

            // Step 4: delete from staging (StageAndMove only — failure is non-fatal).
            if (mode != StorageProfileMode.StageAndRetainCopy) {
                try {
                    await staging.DeleteAsync(ver.StagingRef);
                } catch (Exception ex) {
                    _logger?.LogWarning(ex, "Staging delete failed for versionId={Vid} ref={Ref} — file remains on staging (non-fatal).", ver.VersionId, ver.StagingRef);
                }
            }
        }
    }

    // ── DI extension ─────────────────────────────────────────────────────────

    /// <summary>DI registration helper for <see cref="StagingPromotionWorker"/>.</summary>
    public static class StagingPromotionExtensions {
        /// <summary>
        /// Registers <see cref="StagingPromotionWorker"/> as a hosted service.
        /// <paramref name="config"/> must specify the module CUID that owns the staged files.
        /// The worker resolves its <see cref="IStorageCoordinator"/> from the DI container.
        /// </summary>
        public static IServiceCollection AddStagingPromotion(
            this IServiceCollection services, StagingPromotionConfig config) {
            if (config == null) throw new ArgumentNullException(nameof(config));
            services.AddSingleton(config);
            services.AddHostedService<StagingPromotionWorker>();
            return services;
        }
    }
}
