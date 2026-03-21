using Haley.Enums;
using System.Threading;
using System.Threading.Tasks;

namespace Haley.Abstractions {
    public interface IChunkedUploadOperations {
        // ── Chunked Upload ────────────────────────────────────────────────────
        /// <summary>
        /// Registers the document in DB, creates a temp chunk directory, and returns the
        /// versionId + versionCuid needed for subsequent part uploads and completion.
        /// </summary>
        Task<IFeedback<(long versionId, string versionCuid)>> InitiateChunkedUpload(IVaultFileWriteRequest request, long chunkSizeMb, int totalParts);

        /// <summary>Writes one chunk part to the temp directory and records it in DB.</summary>
        Task<IFeedback> UploadChunkPart(long versionId, int partNumber, Stream chunkStream, string hash = null);

        /// <summary>Assembles all parts into the final storage path, finalizes DB records, and cleans up temp files.</summary>
        Task<IFeedback> CompleteChunkedUpload(long versionId, string finalHash = null);

        /// <summary>Returns how many parts have been received for an active session.</summary>
        Task<IFeedback> GetChunkStatus(long versionId);

        /// <summary>
        /// Cancels an active chunk session: removes it from the in-memory cache and
        /// deletes the temp chunk directory. DB chunk records are left orphaned for
        /// offline cleanup. Returns success even when no session exists (idempotent).
        /// </summary>
        Task<IFeedback> AbortChunkedUpload(long versionId);
    }
}
