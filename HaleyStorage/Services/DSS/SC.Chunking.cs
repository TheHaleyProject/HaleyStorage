using Haley.Abstractions;
using Haley.Models;
using Haley.Utils;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Haley.Services {

    /// <summary>
    /// Partial class — multi-part (chunked) upload pipeline.
    /// Manages in-memory chunk sessions, writes individual parts to a temp directory, and
    /// assembles them into the final storage path once all parts arrive.
    /// </summary>
    public partial class StorageCoordinator : IStorageCoordinator {

        // ── In-memory session cache ───────────────────────────────────────────
        // Keyed by versionId. Survives only while the process is running.
        // If the server restarts between initiation and completion, the session
        // is lost. Persistent sessions can be added later using chunk_info.path.
        sealed record ChunkSessionCache(string FinalPath, string TempDir, string ModuleCuid, string FileCuid, int TotalParts);
        readonly ConcurrentDictionary<long, ChunkSessionCache> _chunkSessions = new();

        // ── Chunk temp directory root ─────────────────────────────────────────
        string ChunkRoot => Path.Combine(BasePath, "_chunks");

        // ── 1. Initiate ───────────────────────────────────────────────────────

        /// <summary>
        /// Initiates a multi-part upload session. Registers a doc/doc_version record in the DB,
        /// creates a temp chunk directory under <c>{BasePath}/_chunks/{versionCuid}/</c>, and
        /// registers the session in <c>chunk_info</c> via the indexer.
        /// </summary>
        /// <param name="request">Write request providing scope, file name, and conflict mode.</param>
        /// <param name="chunkSizeMb">Expected size of each individual part in megabytes (must be ≥ 1).</param>
        /// <param name="totalParts">Total number of parts expected (must be ≥ 2).</param>
        /// <returns>
        /// On success: the <c>versionId</c> (used for subsequent part and complete calls)
        /// and the <c>versionCuid</c> (compact-N GUID identifying the file version).
        /// </returns>
        public async Task<IFeedback<(long versionId, string versionCuid)>> InitiateChunkedUpload(
            IVaultFileWriteRequest request,
            long chunkSizeMb,
            int totalParts) {

            var fb = new Feedback<(long, string)>();
            try {
                if (!WriteMode) return fb.SetMessage("Application is in Read-Only mode.");
                if (request == null) return fb.SetMessage("Request cannot be null.");
                if (string.IsNullOrWhiteSpace(request.OriginalName))
                    return fb.SetMessage("FileName is required for chunked upload initiation.");
                if (chunkSizeMb < 1) return fb.SetMessage("ChunkSizeMb must be >= 1.");
                if (totalParts < 1) return fb.SetMessage("TotalParts must be >= 1.");

                request.GenerateCallId();

                // Registers doc + doc_version in DB and generates the final storage path.
                // After this call: request.File.Id = versionId, request.File.Cuid = versionCuid,
                // request.OverrideRef = final assembled file destination.
                ProcessAndBuildStoragePath(request, true);

                if (request.File == null || request.File.Id < 1 || string.IsNullOrWhiteSpace(request.File.Cuid))
                    return fb.SetMessage("Failed to register document record. Check indexer configuration.");

                long versionId = request.File.Id;
                string versionCuid   = request.File.Cuid;
                string finalPath  = request.OverrideRef;

                // Create temp chunk directory: {BasePath}/_chunks/{versionCuid}/
                // Using versionCuid (globally unique) rather than versionId (per-module auto-increment)
                // to avoid directory collisions across different modules.
                var chunkDir = Path.Combine(ChunkRoot, versionCuid);
                Directory.CreateDirectory(chunkDir);

                // Register chunk session in DB via indexer.
                string moduleCuid = null;
                if (Indexer != null && request.Scope?.Module != null) {
                    moduleCuid = request.Scope.Module.Cuid.ToString("N");

                    var chunkResult = await Indexer.UpsertChunkInfo(
                        moduleCuid, versionId,
                        chunkSizeMb, totalParts,
                        versionCuid, chunkDir,
                        isCompleted: false,
                        callId: request.CallID);

                    if (!chunkResult.Status) {
                        if (Indexer is MariaDBIndexing mdIdx) mdIdx.FinalizeTransaction(request.CallID, false);
                        try { Directory.Delete(chunkDir, true); } catch { }
                        return fb.SetMessage($"Failed to create chunk session in DB: {chunkResult.Message}");
                    }

                    if (Indexer is MariaDBIndexing idx) idx.FinalizeTransaction(request.CallID, true);
                }

                _chunkSessions[versionId] = new ChunkSessionCache(finalPath, chunkDir, moduleCuid, versionCuid, totalParts);

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
        /// <param name="versionId">Session identifier returned by <see cref="InitiateChunkedUpload"/>.</param>
        /// <param name="partNumber">1-based index of this part (must be between 1 and totalParts).</param>
        /// <param name="chunkStream">Raw bytes for this part.</param>
        /// <param name="hash">Optional SHA-256 hash of the part bytes for integrity checking.</param>
        public async Task<IFeedback> UploadChunkPart(long versionId, int partNumber, Stream chunkStream, string hash = null) {
            var fb = new Feedback();
            try {
                if (!_chunkSessions.TryGetValue(versionId, out var session))
                    return fb.SetMessage($"No active chunk session for versionId {versionId}. Initiate first or session has expired.");
                if (partNumber < 1 || partNumber > session.TotalParts)
                    return fb.SetMessage($"partNumber must be between 1 and {session.TotalParts}.");
                if (chunkStream == null || !chunkStream.CanRead)
                    return fb.SetMessage("Chunk stream is null or unreadable.");

                // Part files are named as zero-padded integers: 000001, 000002, ...
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
        /// Validates all parts are present, assembles them sequentially into the final storage path,
        /// updates <c>version_info</c> with flags=91 (ChunkedMode|ChunkArea|InStorage|ChunksDeleted|Completed),
        /// marks <c>chunk_info</c> complete, and deletes the temp chunk directory.
        /// </summary>
        /// <param name="versionId">Session identifier returned by <see cref="InitiateChunkedUpload"/>.</param>
        /// <param name="finalHash">Optional SHA-256 of the fully assembled file.</param>
        public async Task<IFeedback> CompleteChunkedUpload(long versionId, string finalHash = null) {
            var fb = new Feedback();
            try {
                if (!WriteMode) return fb.SetMessage("Application is in Read-Only mode.");
                if (!_chunkSessions.TryGetValue(versionId, out var session))
                    return fb.SetMessage($"No active chunk session for versionId {versionId}.");

                var partFiles = Directory.GetFiles(session.TempDir)
                    .OrderBy(f => Path.GetFileName(f))
                    .ToList();

                if (partFiles.Count < 1)
                    return fb.SetMessage("No chunk parts found in temp directory. Nothing to assemble.");

                // ── Integrity: validate every expected part is present ────────────
                // Parse filenames as integers (zero-padded, e.g. "000003" → 3).
                // Report explicit gaps rather than silently assembling an incomplete file.
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

                // Ensure final destination directory exists.
                var finalDir = Path.GetDirectoryName(session.FinalPath);
                if (!string.IsNullOrWhiteSpace(finalDir))
                    Directory.CreateDirectory(finalDir);

                // Assemble all parts sequentially into the final file.
                long totalSize = 0;
                using (var finalStream = new FileStream(session.FinalPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024)) {
                    foreach (var partFile in partFiles) {
                        using var partStream = File.OpenRead(partFile);
                        await partStream.CopyToAsync(finalStream);
                        totalSize += new FileInfo(partFile).Length;
                    }
                }

                // Finalize DB: update version_info + mark chunk_info complete.
                if (Indexer != null && !string.IsNullOrWhiteSpace(session.ModuleCuid)) {
                    var callId = Guid.NewGuid().ToString("N");

                    // Flags: ChunkedMode(1) | ChunkArea(2) | InStorage(8) | ChunksDeleted(16) | Completed(64) = 91
                    var fileRoute = new StorageFileRoute {
                        Id         = versionId,
                        Cuid       = session.FileCuid,
                        StorageRef = session.FinalPath,
                        StorageName = Path.GetFileName(session.FinalPath),
                        Size       = totalSize,
                        Flags      = 1 | 2 | 8 | 16 | 64,
                        Hash       = finalHash
                    };

                    var updateResult = await Indexer.UpdateDocVersionInfo(session.ModuleCuid, fileRoute, callId);
                    var markResult   = await Indexer.MarkChunkCompleted(session.ModuleCuid, versionId, callId);

                    bool allOk = updateResult.Status && markResult.Status;
                    if (Indexer is MariaDBIndexing idx) idx.FinalizeTransaction(callId, allOk);

                    if (!allOk)
                        return fb.SetMessage($"Assembly succeeded but DB finalization failed — update: {updateResult.Message} / mark: {markResult.Message}");
                }

                // Clean up temp chunks (best-effort).
                try { Directory.Delete(session.TempDir, true); } catch { }

                _chunkSessions.TryRemove(versionId, out _);

                return fb.SetStatus(true).SetMessage($"Chunked upload complete. Final size: {totalSize} bytes.");

            } catch (Exception ex) {
                return fb.SetMessage(ex.Message);
            }
        }

        // ── 4. Abort ──────────────────────────────────────────────────────────

        /// <summary>
        /// Cancels an active chunk session: removes it from <c>_chunkSessions</c> and
        /// deletes the temp chunk directory. DB <c>chunk_info</c> and <c>chunked_files</c>
        /// records are left for offline cleanup. Idempotent — returns success when no session exists.
        /// </summary>
        public Task<IFeedback> AbortChunkedUpload(long versionId) {
            var fb = new Feedback();
            try {
                if (!_chunkSessions.TryRemove(versionId, out var session))
                    return Task.FromResult<IFeedback>(fb.SetStatus(true).SetMessage("No active session found; nothing to abort."));

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
        /// Returns a JSON result with <c>TotalParts</c>, <c>ReceivedParts</c>, and <c>Pending</c>
        /// for an active chunk session, or an error if the session is not found.
        /// </summary>
        public Task<IFeedback> GetChunkStatus(long versionId) {
            var fb = new Feedback();
            if (!_chunkSessions.TryGetValue(versionId, out var session))
                return Task.FromResult<IFeedback>(fb.SetMessage("No active session found. It may have already completed or never been initiated."));

            int received = Directory.Exists(session.TempDir)
                ? Directory.GetFiles(session.TempDir).Length
                : 0;

            return Task.FromResult<IFeedback>(fb
                .SetStatus(true)
                .SetResult(new { TotalParts = session.TotalParts, ReceivedParts = received, Pending = session.TotalParts - received }.ToJson()));
        }
    }
}
