using Haley.Abstractions;
using Haley.Models;
using Haley.Utils;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Haley.Services {

    /// <summary>
    /// Partial class — multi-part (chunked) upload pipeline.
    /// Manages in-memory chunk sessions, writes individual parts to a temp directory, and
    /// assembles them into the final storage path once all parts arrive.
    /// </summary>
    public partial class StorageCoordinator : IStorageCoordinator {

        // ── In-memory session caches ──────────────────────────────────────────
        // Primary: keyed by versionId (long) — used by UploadChunkPart / CompleteChunkedUpload.
        // Secondary: keyed by versionCuid (string) — used by TUS PATCH/HEAD/DELETE which receive
        // the CUID from the Location URL.
        sealed record ChunkSessionCache(string FinalPath, string TempDir, string ModuleCuid, string FileCuid, int TotalParts, long ChunkSizeMb, long TotalLength);
        readonly ConcurrentDictionary<long, ChunkSessionCache> _chunkSessions = new();
        readonly ConcurrentDictionary<string, long> _chunkSessionsByCuid = new();

        // ── Chunk temp directory root ─────────────────────────────────────────
        string ChunkRoot => Path.Combine(BasePath, "_chunks");

        // ── Session metadata filename (written inside each chunk directory) ───
        // Enables crash-recovery rehydration: read session info from disk without a DB round-trip.
        const string SessionMetaFile = "_session.json";

        // ── 1. Initiate ───────────────────────────────────────────────────────

        /// <summary>
        /// Initiates a multi-part upload session. Registers a doc/doc_version record in the DB,
        /// creates a temp chunk directory under <c>{BasePath}/_chunks/{versionCuid}/</c>,
        /// writes a <c>_session.json</c> metadata file for crash-recovery rehydration,
        /// and registers the session in the in-memory caches.
        /// </summary>
        public async Task<IFeedback<(long versionId, string versionCuid)>> InitiateChunkedUpload(IVaultFileWriteRequest request, long chunkSizeMb, int totalParts) {

            var fb = new Feedback<(long, string)>();
            try {
                if (!WriteMode) return fb.SetMessage("Application is in Read-Only mode.");
                if (request == null) return fb.SetMessage("Request cannot be null.");
                if (request.ReadOnlyMode) return fb.SetMessage("Request is in Read-Only mode.");
                if (string.IsNullOrWhiteSpace(request.OriginalName))
                    return fb.SetMessage("FileName is required for chunked upload initiation.");
                if (chunkSizeMb < 1) return fb.SetMessage("ChunkSizeMb must be >= 1.");
                if (totalParts < 1) return fb.SetMessage("TotalParts must be >= 1.");

                request.GenerateCallId();

                // Registers doc + doc_version in DB and generates the final storage path.
                ProcessAndBuildStoragePath(request, true);

                if (request.File == null || request.File.Id < 1 || string.IsNullOrWhiteSpace(request.File.Cuid))
                    return fb.SetMessage("Failed to register document record. Check indexer configuration.");

                long versionId = request.File.Id;
                string versionCuid = request.File.Cuid;
                string finalPath = request.OverrideRef;

                // Create temp chunk directory: {BasePath}/_chunks/{versionCuid}/
                var chunkDir = Path.Combine(ChunkRoot, versionCuid);
                Directory.CreateDirectory(chunkDir);

                // Derive total file length from the chunk geometry for rehydration.
                long totalLength = chunkSizeMb * 1024 * 1024 * totalParts; // upper-bound estimate

                // Write a metadata file into the chunk directory so sessions can be rehydrated
                // after a process restart without any DB queries.
                var meta = new ChunkSessionMeta {
                    VersionId = versionId,
                    VersionCuid = versionCuid,
                    FinalPath = finalPath,
                    ModuleCuid = null, // filled below
                    ChunkSizeMb = chunkSizeMb,
                    TotalParts = totalParts,
                    TotalLength = totalLength
                };

                // Register chunk session in DB via indexer.
                string moduleCuid = null;
                if (Indexer != null && request.Scope?.Module != null) {
                    moduleCuid = request.Scope.Module.Cuid.ToString("N");
                    meta.ModuleCuid = moduleCuid;

                    var chunkResult = await Indexer.UpsertChunkInfo(moduleCuid, versionId, chunkSizeMb, totalParts, versionCuid, chunkDir, isCompleted: false, callId: request.CallID);

                    if (!chunkResult.Status) {
                        if (Indexer is MariaDBIndexing mdIdx) mdIdx.FinalizeTransaction(request.CallID, false);
                        try { Directory.Delete(chunkDir, true); } catch { }
                        return fb.SetMessage($"Failed to create chunk session in DB: {chunkResult.Message}");
                    }

                    if (Indexer is MariaDBIndexing idx) idx.FinalizeTransaction(request.CallID, true);
                }

                // Persist metadata now that we have moduleCuid.
                try {
                    var metaJson = JsonSerializer.Serialize(meta);
                    File.WriteAllText(Path.Combine(chunkDir, SessionMetaFile), metaJson);
                } catch { /* best-effort */ }

                _chunkSessions[versionId] = new ChunkSessionCache(finalPath, chunkDir, moduleCuid, versionCuid, totalParts, chunkSizeMb, totalLength);
                _chunkSessionsByCuid[versionCuid] = versionId;

                return fb.SetStatus(true).SetResult((versionId, versionCuid));

            } catch (Exception ex) {
                return fb.SetMessage(ex.Message);
            }
        }

        // ── 2. Upload Part ────────────────────────────────────────────────────

        /// <summary>
        /// Writes a single chunk part to the temp directory as a zero-padded file (e.g. <c>000003</c>)
        /// and records it in the <c>chunked_files</c> DB table.
        /// </summary>
        public async Task<IFeedback> UploadChunkPart(long versionId, int partNumber, Stream chunkStream, string hash = null) {
            var fb = new Feedback();
            try {
                if (!_chunkSessions.TryGetValue(versionId, out var session))
                    return fb.SetMessage($"No active chunk session for versionId {versionId}. Initiate first or session has expired.");
                if (partNumber < 1 || partNumber > session.TotalParts)
                    return fb.SetMessage($"partNumber must be between 1 and {session.TotalParts}.");
                if (chunkStream == null || !chunkStream.CanRead)
                    return fb.SetMessage("Chunk stream is null or unreadable.");

                var partFile = Path.Combine(session.TempDir, partNumber.ToString("D6"));
                long sizeBytes = 0;
                using (var fs = new FileStream(partFile, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 256)) {
                    await chunkStream.CopyToAsync(fs);
                    sizeBytes = fs.Length;
                }

                int sizeMb = (int)Math.Ceiling((double)sizeBytes / (1024 * 1024));

                if (Indexer != null && !string.IsNullOrWhiteSpace(session.ModuleCuid)) {
                    var dbResult = await Indexer.UpsertChunkPart(session.ModuleCuid, versionId, partNumber, sizeMb, hash);
                    if (!dbResult.Status)
                        return fb.SetMessage($"Part written to disk but DB record failed: {dbResult.Message}");
                }

                return fb.SetStatus(true).SetMessage($"Part {partNumber}/{session.TotalParts} received ({sizeBytes} bytes).");

            } catch (Exception ex) {
                return fb.SetMessage(ex.Message);
            }
        }

        // ── 3. Complete ───────────────────────────────────────────────────────

        /// <summary>
        /// Validates all parts are present, assembles them into the final storage path,
        /// updates DB, and deletes the temp chunk directory.
        /// </summary>
        public async Task<IFeedback> CompleteChunkedUpload(long versionId, string finalHash = null) {
            var fb = new Feedback();
            try {
                if (!WriteMode) return fb.SetMessage("Application is in Read-Only mode.");
                if (!_chunkSessions.TryGetValue(versionId, out var session))
                    return fb.SetMessage($"No active chunk session for versionId {versionId}.");

                var partFiles = Directory.GetFiles(session.TempDir)
                    .Where(f => !Path.GetFileName(f).StartsWith("_"))
                    .OrderBy(f => Path.GetFileName(f))
                    .ToList();

                if (partFiles.Count < 1)
                    return fb.SetMessage("No chunk parts found in temp directory. Nothing to assemble.");

                var received = new System.Collections.Generic.HashSet<int>();
                foreach (var f in partFiles) {
                    if (int.TryParse(Path.GetFileName(f), out int pn))
                        received.Add(pn);
                }

                var missing = new System.Collections.Generic.List<int>();
                for (int i = 1; i <= session.TotalParts; i++) {
                    if (!received.Contains(i)) missing.Add(i);
                }

                if (missing.Count > 0)
                    return fb.SetMessage(
                        $"Cannot assemble: {missing.Count} part(s) missing out of {session.TotalParts}. " +
                        $"Missing parts: [{string.Join(", ", missing)}].");

                if (received.Count > session.TotalParts)
                    return fb.SetMessage(
                        $"Cannot assemble: received {received.Count} parts but session expects {session.TotalParts}. " +
                        "Abort and re-initiate the session.");

                var finalDir = Path.GetDirectoryName(session.FinalPath);
                if (!string.IsNullOrWhiteSpace(finalDir))
                    Directory.CreateDirectory(finalDir);

                long totalSize = 0;
                using (var finalStream = new FileStream(session.FinalPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024)) {
                    foreach (var partFile in partFiles) {
                        using var partStream = File.OpenRead(partFile);
                        await partStream.CopyToAsync(finalStream);
                        totalSize += new FileInfo(partFile).Length;
                    }
                }

                if (Indexer != null && !string.IsNullOrWhiteSpace(session.ModuleCuid)) {
                    var callId = Guid.NewGuid().ToString("N");

                    // Flags: ChunkedMode(1) | ChunkArea(2) | InStorage(8) | ChunksDeleted(16) | Completed(64) = 91
                    var fileRoute = new StorageFileRoute {
                        Id = versionId,
                        Cuid = session.FileCuid,
                        StorageRef = session.FinalPath,
                        StorageName = Path.GetFileName(session.FinalPath),
                        Size = totalSize,
                        Flags = 1 | 2 | 8 | 16 | 64,
                        Hash = finalHash
                    };

                    var updateResult = await Indexer.UpdateDocVersionInfo(session.ModuleCuid, fileRoute, callId);
                    var markResult = await Indexer.MarkChunkCompleted(session.ModuleCuid, versionId, callId);

                    bool allOk = updateResult.Status && markResult.Status;
                    if (Indexer is MariaDBIndexing idx) idx.FinalizeTransaction(callId, allOk);

                    if (!allOk)
                        return fb.SetMessage($"Assembly succeeded but DB finalization failed — update: {updateResult.Message} / mark: {markResult.Message}");
                }

                try { Directory.Delete(session.TempDir, true); } catch { }

                _chunkSessions.TryRemove(versionId, out _);
                _chunkSessionsByCuid.TryRemove(session.FileCuid, out _);

                return fb.SetStatus(true).SetMessage($"Chunked upload complete. Final size: {totalSize} bytes.");

            } catch (Exception ex) {
                return fb.SetMessage(ex.Message);
            }
        }

        // ── 4. Abort ──────────────────────────────────────────────────────────

        /// <summary>
        /// Cancels an active chunk session. Removes from caches and deletes the temp directory.
        /// Idempotent — returns success when no session exists.
        /// </summary>
        public Task<IFeedback> AbortChunkedUpload(long versionId) {
            var fb = new Feedback();
            try {
                if (!_chunkSessions.TryRemove(versionId, out var session))
                    return Task.FromResult<IFeedback>(fb.SetStatus(true).SetMessage("No active session found; nothing to abort."));

                _chunkSessionsByCuid.TryRemove(session.FileCuid, out _);

                try {
                    if (Directory.Exists(session.TempDir))
                        Directory.Delete(session.TempDir, true);
                } catch { /* best-effort */ }

                return Task.FromResult<IFeedback>(fb.SetStatus(true)
                    .SetMessage($"Chunk session {versionId} aborted and temp directory deleted."));
            } catch (Exception ex) {
                return Task.FromResult<IFeedback>(fb.SetMessage(ex.Message));
            }
        }

        // ── 5. Status ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns TotalParts, ReceivedParts, and Pending for an active chunk session.
        /// </summary>
        public Task<IFeedback> GetChunkStatus(long versionId) {
            var fb = new Feedback();
            if (!_chunkSessions.TryGetValue(versionId, out var session))
                return Task.FromResult<IFeedback>(fb.SetMessage("No active session found. It may have already completed or never been initiated."));

            int received = Directory.Exists(session.TempDir)
                ? Directory.GetFiles(session.TempDir).Count(f => !Path.GetFileName(f).StartsWith("_"))
                : 0;

            return Task.FromResult<IFeedback>(fb
                .SetStatus(true)
                .SetResult(new { TotalParts = session.TotalParts, ReceivedParts = received, Pending = session.TotalParts - received }.ToJson()));
        }

        // ── 6. Rehydration ────────────────────────────────────────────────────

        /// <summary>
        /// Attempts to rehydrate a chunk session from the <c>_session.json</c> file written
        /// inside the chunk temp directory during <see cref="InitiateChunkedUpload"/>.
        /// <para>
        /// Called by the TUS controller when a HEAD/PATCH request arrives after a process restart
        /// and the in-memory <c>_chunkSessions</c> cache is empty. Returns information needed
        /// to reconstruct a <see cref="TusUploadSession"/> without any DB round-trip.
        /// </para>
        /// Returns a record indicating whether the session was found, with all fields needed
        /// to reconstruct the TUS session. On success, also restores the entry in
        /// <c>_chunkSessions</c> so subsequent coordinator calls (UploadChunkPart, etc.) work.
        /// </summary>
        public async Task<(bool found, long versionId, string versionCuid, long chunkSizeMb, int totalParts, int completedParts, long totalLength)>
            TryRehydrateChunkSession(string versionCuid) {

            var notFound = (false, 0L, string.Empty, 0L, 0, 0, 0L);
            if (string.IsNullOrWhiteSpace(versionCuid)) return notFound;

            // Fast path: already in memory (secondary index lookup).
            if (_chunkSessionsByCuid.TryGetValue(versionCuid, out long existingId) &&
                _chunkSessions.TryGetValue(existingId, out var existingSession)) {

                int completedInMem = Directory.Exists(existingSession.TempDir)
                    ? Directory.GetFiles(existingSession.TempDir).Count(f => !Path.GetFileName(f).StartsWith("_"))
                    : 0;

                return (true, existingId, versionCuid, existingSession.ChunkSizeMb, existingSession.TotalParts, completedInMem, existingSession.TotalLength);
            }

            // Slow path: read _session.json from disk.
            var chunkDir = Path.Combine(ChunkRoot, versionCuid);
            var metaPath = Path.Combine(chunkDir, SessionMetaFile);

            if (!File.Exists(metaPath)) return notFound;

            ChunkSessionMeta meta;
            try {
                var json = await File.ReadAllTextAsync(metaPath);
                meta = JsonSerializer.Deserialize<ChunkSessionMeta>(json);
                if (meta == null) return notFound;
            } catch {
                return notFound;
            }

            // Count how many parts are already on disk (exclude the metadata file).
            int completed = Directory.GetFiles(chunkDir)
                .Count(f => !Path.GetFileName(f).StartsWith("_"));

            // Restore the in-memory cache so subsequent coordinator calls work.
            var restored = new ChunkSessionCache(
                meta.FinalPath,
                chunkDir,
                meta.ModuleCuid,
                meta.VersionCuid,
                meta.TotalParts,
                meta.ChunkSizeMb,
                meta.TotalLength);

            _chunkSessions[meta.VersionId] = restored;
            _chunkSessionsByCuid[meta.VersionCuid] = meta.VersionId;

            return (true, meta.VersionId, meta.VersionCuid, meta.ChunkSizeMb, meta.TotalParts, completed, meta.TotalLength);
        }

        // ── Internal: chunk session metadata record ───────────────────────────

        /// <summary>
        /// JSON-serialisable payload written to <c>_session.json</c> inside each chunk directory.
        /// Enables full session recovery after a process restart without any DB queries.
        /// </summary>
        private sealed class ChunkSessionMeta {
            public long VersionId { get; set; }
            public string VersionCuid { get; set; }
            public string FinalPath { get; set; }
            public string ModuleCuid { get; set; }
            public long ChunkSizeMb { get; set; }
            public int TotalParts { get; set; }
            public long TotalLength { get; set; }
        }

    }
}
