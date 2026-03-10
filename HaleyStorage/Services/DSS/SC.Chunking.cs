using Haley.Abstractions;
using Haley.Models;
using Haley.Utils;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Haley.Services {

    public partial class StorageCoordinator : IStorageCoordinator {

        // ── In-memory session cache ───────────────────────────────────────────
        // Keyed by docVersionId. Survives only while the process is running.
        // If the server restarts between initiation and completion, the session
        // is lost. Persistent sessions can be added later using chunk_info.path.
        sealed record ChunkSessionCache(string FinalPath, string TempDir, string ModuleCuid, string FileCuid, int TotalParts);
        readonly ConcurrentDictionary<long, ChunkSessionCache> _chunkSessions = new();

        // ── Chunk temp directory root ─────────────────────────────────────────
        string ChunkRoot => Path.Combine(BasePath, "_chunks");

        // ── 1. Initiate ───────────────────────────────────────────────────────

        public async Task<IFeedback<(long docVersionId, string fileCuid)>> InitiateChunkedUpload(
            IVaultFileWriteRequest request,
            long chunkSizeMb,
            int totalParts) {

            var fb = new Feedback<(long, string)>();
            try {
                if (!WriteMode) return fb.SetMessage("Application is in Read-Only mode.");
                if (request == null) return fb.SetMessage("Request cannot be null.");
                if (string.IsNullOrWhiteSpace(request.FileOriginalName))
                    return fb.SetMessage("FileName is required for chunked upload initiation.");
                if (chunkSizeMb < 1) return fb.SetMessage("ChunkSizeMb must be >= 1.");
                if (totalParts < 2) return fb.SetMessage("TotalParts must be >= 2.");

                request.GenerateCallId();

                // Registers doc + doc_version in DB and generates the final storage path.
                // After this call: request.File.Id = docVersionId, request.File.Cuid = fileCuid,
                // request.TargetPath = final assembled file destination.
                ProcessAndBuildStoragePath(request, true);

                if (request.File == null || request.File.Id < 1 || string.IsNullOrWhiteSpace(request.File.Cuid))
                    return fb.SetMessage("Failed to register document record. Check indexer configuration.");

                long docVersionId = request.File.Id;
                string fileCuid   = request.File.Cuid;
                string finalPath  = request.TargetPath;

                // Create temp chunk directory: {BasePath}/_chunks/{fileCuid}/
                // Using fileCuid (globally unique) rather than docVersionId (per-module auto-increment)
                // to avoid directory collisions across different modules.
                var chunkDir = Path.Combine(ChunkRoot, fileCuid);
                Directory.CreateDirectory(chunkDir);

                // Register chunk session in DB via indexer.
                string moduleCuid = null;
                if (Indexer != null && request.Scope?.Module != null) {
                    moduleCuid = request.Scope.Module.Cuid.ToString("N");

                    var chunkResult = await Indexer.UpsertChunkInfo(
                        moduleCuid, docVersionId,
                        chunkSizeMb, totalParts,
                        fileCuid, chunkDir,
                        isCompleted: false,
                        callId: request.CallID);

                    if (!chunkResult.Status) {
                        if (Indexer is MariaDBIndexing mdIdx) mdIdx.FinalizeTransaction(request.CallID, false);
                        try { Directory.Delete(chunkDir, true); } catch { }
                        return fb.SetMessage($"Failed to create chunk session in DB: {chunkResult.Message}");
                    }

                    if (Indexer is MariaDBIndexing idx) idx.FinalizeTransaction(request.CallID, true);
                }

                _chunkSessions[docVersionId] = new ChunkSessionCache(finalPath, chunkDir, moduleCuid, fileCuid, totalParts);

                return fb.SetStatus(true).SetResult((docVersionId, fileCuid));

            } catch (Exception ex) {
                return fb.SetMessage(ex.Message);
            }
        }

        // ── 2. Upload Part ────────────────────────────────────────────────────

        public async Task<IFeedback> UploadChunkPart(long docVersionId, int partNumber, Stream chunkStream, string hash = null) {
            var fb = new Feedback();
            try {
                if (!_chunkSessions.TryGetValue(docVersionId, out var session))
                    return fb.SetMessage($"No active chunk session for docVersionId {docVersionId}. Initiate first or session has expired.");
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
                    var dbResult = await Indexer.UpsertChunkPart(session.ModuleCuid, docVersionId, partNumber, sizeMb, hash);
                    if (!dbResult.Status)
                        return fb.SetMessage($"Part written to disk but DB record failed: {dbResult.Message}");
                }

                return fb.SetStatus(true).SetMessage($"Part {partNumber}/{session.TotalParts} received ({sizeBytes} bytes).");

            } catch (Exception ex) {
                return fb.SetMessage(ex.Message);
            }
        }

        // ── 3. Complete ───────────────────────────────────────────────────────

        public async Task<IFeedback> CompleteChunkedUpload(long docVersionId, string finalHash = null) {
            var fb = new Feedback();
            try {
                if (!WriteMode) return fb.SetMessage("Application is in Read-Only mode.");
                if (!_chunkSessions.TryGetValue(docVersionId, out var session))
                    return fb.SetMessage($"No active chunk session for docVersionId {docVersionId}.");

                var partFiles = Directory.GetFiles(session.TempDir)
                    .OrderBy(f => Path.GetFileName(f))
                    .ToList();

                if (partFiles.Count < 1)
                    return fb.SetMessage("No chunk parts found in temp directory. Nothing to assemble.");

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
                        Id         = docVersionId,
                        Cuid       = session.FileCuid,
                        Path       = session.FinalPath,
                        SaveAsName = Path.GetFileName(session.FinalPath),
                        Size       = totalSize,
                        Flags      = 1 | 2 | 8 | 16 | 64,
                        Hash       = finalHash
                    };

                    var updateResult = await Indexer.UpdateDocVersionInfo(session.ModuleCuid, fileRoute, callId);
                    var markResult   = await Indexer.MarkChunkCompleted(session.ModuleCuid, docVersionId, callId);

                    bool allOk = updateResult.Status && markResult.Status;
                    if (Indexer is MariaDBIndexing idx) idx.FinalizeTransaction(callId, allOk);

                    if (!allOk)
                        return fb.SetMessage($"Assembly succeeded but DB finalization failed — update: {updateResult.Message} / mark: {markResult.Message}");
                }

                // Clean up temp chunks (best-effort).
                try { Directory.Delete(session.TempDir, true); } catch { }

                _chunkSessions.TryRemove(docVersionId, out _);

                return fb.SetStatus(true).SetMessage($"Chunked upload complete. Final size: {totalSize} bytes.");

            } catch (Exception ex) {
                return fb.SetMessage(ex.Message);
            }
        }

        // ── 4. Status ─────────────────────────────────────────────────────────

        public Task<IFeedback> GetChunkStatus(long docVersionId) {
            var fb = new Feedback();
            if (!_chunkSessions.TryGetValue(docVersionId, out var session))
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
