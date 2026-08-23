using Haley.Enums;
using Haley.Models;
using System.Threading;
using System.Threading.Tasks;

namespace Haley.Abstractions {
    public interface IChunkedUploadOperations {
        // ── Chunked Upload ────────────────────────────────────────────────────
        /// <summary>
        /// Registers the document in DB, creates a temp chunk directory, and returns the
        /// versionId + versionCuid needed for subsequent part uploads and completion.
        /// </summary>
        Task<IFeedback<ChunkUploadSessionInfo>> InitiateChunkedUpload(IVaultFileWriteRequest request, long chunkSizeMb, int totalParts, long? totalBytes = null, CancellationToken cancellationToken = default);

        /// <summary>Writes one chunk part to the temp directory and records it in DB.</summary>
        Task<IFeedback<ChunkPartResult>> UploadChunkPart(long versionId, int partNumber, Stream chunkStream, string hash = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Appends a sequential byte range to a chunk session. This is transport-neutral and
        /// is used by offset-based API adapters without exposing protocol details to storage.
        /// </summary>
        Task<IFeedback<ChunkAppendResult>> AppendChunkData(long versionId, long expectedOffset, Stream dataStream, long contentLength, CancellationToken cancellationToken = default);

        /// <summary>Assembles all parts into the final storage path, finalizes DB records, and cleans up temp files.</summary>
        Task<IFeedback<ChunkUploadStatus>> CompleteChunkedUpload(long versionId, string finalHash = null, CancellationToken cancellationToken = default);

        /// <summary>Returns how many parts have been received for an active session.</summary>
        Task<IFeedback<ChunkUploadStatus>> GetChunkStatus(long versionId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Cancels an active chunk session, removes temporary bytes, and marks the
        /// incomplete version as deleted. Returns success when repeated (idempotent).
        /// </summary>
        Task<IFeedback> AbortChunkedUpload(long versionId, CancellationToken cancellationToken = default);
    }
}
