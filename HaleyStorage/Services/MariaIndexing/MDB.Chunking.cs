using Haley.Abstractions;
using Haley.Models;
using Haley.Enums;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Haley.Internal.IndexingConstant;
using static Haley.Internal.IndexingQueries;

namespace Haley.Utils {
    /// <summary>
    /// Partial class — chunked-upload DB persistence.
    /// Manages <c>chunk_info</c> (session metadata) and <c>chunked_files</c> (per-part records)
    /// in the per-module MariaDB instance.
    /// </summary>
    internal partial class MariaDBIndexing {

        /// <summary>
        /// Inserts or updates a <c>chunk_info</c> row for a doc_version, recording the expected
        /// chunk size, total parts, temp folder name/path, and completion state.
        /// </summary>
        /// <param name="moduleCuid">CUID of the module whose per-module DB is targeted.</param>
        /// <param name="versionId">DB ID of the <c>doc_version</c> row this chunk session belongs to.</param>
        /// <param name="chunkFolderName">Short folder name (usually the versionCuid) used as the chunk dir identifier.</param>
        /// <param name="chunkFolderPath">Absolute path to the temp chunk directory on the FS.</param>
        public async Task<IFeedback> UpsertChunkInfo(string moduleCuid, long versionId, long chunkSizeMb, int totalParts, string chunkFolderName, string chunkFolderPath, bool isCompleted = false, string callId = null) {
            var fb = new Feedback();
            try {
                if (string.IsNullOrWhiteSpace(moduleCuid)) return fb.SetMessage("Module CUID is mandatory.");
                if (!_agw.ContainsKey(moduleCuid)) return fb.SetMessage($@"No adapter found for the key {moduleCuid}");
                if (versionId < 1) return fb.SetMessage("versionId must be > 0");
                if (chunkSizeMb < 1) return fb.SetMessage("chunkSizeMb must be > 1");
                if (totalParts < 2) return fb.SetMessage("totalParts must be >= 2");
                if (string.IsNullOrWhiteSpace(chunkFolderName)) return fb.SetMessage("chunkFolderName cannot be empty");
                if (string.IsNullOrWhiteSpace(chunkFolderPath)) return fb.SetMessage("chunkFolderPath cannot be empty");

                var handler = GetTransactionHandlerCache(callId, moduleCuid);

                // Ensure doc_version exists (cheap guard)
                var dv = await _agw.Scalar( new AdapterArgs(moduleCuid) { Query = INSTANCE.DOCVERSION.EXISTS_BY_ID }.ForTransaction(handler), (ID, versionId) );
                if (dv == null) return fb.SetMessage($@"doc_version {versionId} not found in {moduleCuid}");

                await _agw.NonQuery(new AdapterArgs(moduleCuid) { Query = INSTANCE.CHUNK.INFO_UPSERT }.ForTransaction(handler), (ID, versionId), (CHUNK_SIZE, chunkSizeMb), (CHUNK_PARTS, totalParts), (CHUNK_NAME, chunkFolderName), (PATH, chunkFolderPath), (IS_COMPLETED, isCompleted ? 1 : 0));

                return fb.SetStatus(true).SetMessage("chunk_info upserted.");
            } catch (Exception ex) {
                return fb.SetMessage(ex.Message + Environment.NewLine + ex.StackTrace);
            }
        }

        /// <summary>
        /// Records that a specific part has been received by upserting a row in <c>chunked_files</c>.
        /// Requires the parent <c>chunk_info</c> row to exist (FK guard).
        /// </summary>
        /// <param name="partNumber">1-based part index.</param>
        /// <param name="sizeMb">Rounded size of the part in MB.</param>
        /// <param name="hash">Optional SHA-256 hash of the part bytes.</param>
        // 2) Record that a specific chunk part was uploaded (chunked_files)
        public async Task<IFeedback> UpsertChunkPart(string moduleCuid, long versionId, long partNumber, int sizeMb, string hash = null, string callId = null) {
            var fb = new Feedback();
            try {
                if (string.IsNullOrWhiteSpace(moduleCuid)) return fb.SetMessage("Module CUID is mandatory.");
                if (!_agw.ContainsKey(moduleCuid)) return fb.SetMessage($@"No adapter found for the key {moduleCuid}");
                if (versionId < 1) return fb.SetMessage("versionId must be > 0");
                if (partNumber < 1) return fb.SetMessage("partNumber must be > 0");
                if (sizeMb < 0) return fb.SetMessage("sizeMb must be >= 0");

                var handler = GetTransactionHandlerCache(callId, moduleCuid);

                // Ensure chunk_info exists first (FK)
                var ck = await _agw.Scalar( new AdapterArgs(moduleCuid) { Query = INSTANCE.CHUNK.INFO_EXISTS }.ForTransaction(handler), (ID, versionId) );
                if (ck == null) return fb.SetMessage($@"chunk_info not found for versionId {versionId} in {moduleCuid}");

                await _agw.NonQuery( new AdapterArgs(moduleCuid) { Query = INSTANCE.CHUNK.FILE_UPSERT }.ForTransaction(handler), (ID, versionId), (PART, partNumber), (FILESIZE_MB, sizeMb), (HASH, string.IsNullOrWhiteSpace(hash) ? (object)DBNull.Value : hash) );

                return fb.SetStatus(true).SetMessage("chunked_files upserted.");
            } catch (Exception ex) {
                return fb.SetMessage(ex.Message + Environment.NewLine + ex.StackTrace);
            }
        }

        /// <summary>Sets <c>chunk_info.is_completed = 1</c> for the given <paramref name="versionId"/>.</summary>
        // 3) Mark chunk upload completed
        public async Task<IFeedback> MarkChunkCompleted(string moduleCuid, long versionId, string callId = null) {
            var fb = new Feedback();
            try {
                if (string.IsNullOrWhiteSpace(moduleCuid)) return fb.SetMessage("Module CUID is mandatory.");
                if (!_agw.ContainsKey(moduleCuid)) return fb.SetMessage($@"No adapter found for the key {moduleCuid}");
                if (versionId < 1) return fb.SetMessage("versionId must be > 0");

                var handler = GetTransactionHandlerCache(callId, moduleCuid);

                await _agw.NonQuery( new AdapterArgs(moduleCuid) { Query = INSTANCE.CHUNK.MARK_COMPLETED }.ForTransaction(handler), (ID, versionId) );

                return fb.SetStatus(true).SetMessage("Chunk marked completed.");
            } catch (Exception ex) {
                return fb.SetMessage(ex.Message + Environment.NewLine + ex.StackTrace);
            }
        }
    }
}
