using Haley.Abstractions;
using Haley.Models;
using Microsoft.Extensions.Logging;
using static Haley.Internal.IndexingConstant;
using static Haley.Internal.IndexingQueries;

namespace Haley.Utils {
    /// <summary>
    /// Partial class — thumbnail version registration and retrieval.
    /// Thumbnails are stored as <c>doc_version</c> rows with <c>sub_ver &gt; 0</c> under
    /// the same (parent, ver) as their content version. No pointer columns are needed —
    /// the relationship is purely structural via the (parent, ver, sub_ver) unique key.
    /// </summary>
    internal partial class MariaDBIndexing {

        /// <summary>Returns the document.id (parent) for a given doc_version.id. Returns 0 when not found.</summary>
        public async Task<long> GetDocumentIdByVersionId(string moduleCuid, long versionId) {
            try {
                if (string.IsNullOrWhiteSpace(moduleCuid) || versionId < 1) return 0;
                if (!_agw.ContainsKey(moduleCuid)) return 0;
                var docId = await _agw.ScalarAsync<long?>(moduleCuid, INSTANCE.DOCVERSION.GET_DOCUMENT_ID_BY_VERSION_ID, default, (VALUE, versionId));
                return docId ?? 0;
            } catch (Exception ex) {
                _logger?.LogError(ex.Message + Environment.NewLine + ex.StackTrace);
                return 0;
            }
        }

        /// <summary>
        /// Inserts a new thumbnail <c>doc_version</c> row with <c>sub_ver = MAX(sub_ver)+1</c>
        /// for the given (documentId, contentVer). The ver is the same content version number —
        /// thumbnails share the ver with their content version and only differ in sub_ver.
        /// Returns the new thumbnail version's DB id and CUID.
        /// </summary>
        public async Task<(long id, Guid guid)> RegisterThumbnailVersion(string moduleCuid, long documentId, int contentVer, long? actor = null, string callId = null) {
            try {
                if (string.IsNullOrWhiteSpace(moduleCuid)) throw new ArgumentNullException(nameof(moduleCuid));
                if (documentId < 1) throw new ArgumentException("documentId must be a positive integer.");
                if (contentVer < 1) throw new ArgumentException("contentVer must be a positive integer.");
                if (!_agw.ContainsKey(moduleCuid)) throw new ArgumentException($"No adapter found for module {moduleCuid}.");

                var handlerKey = GetHandlerKey(callId, moduleCuid);
                var handler = _agw.GetTransactionHandler(moduleCuid);
                if (handler != null && !string.IsNullOrWhiteSpace(callId)) {
                    if (_handlers.ContainsKey(handlerKey)) throw new Exception($"A transaction with key {handlerKey} already exists.");
                    _handlers.TryAdd(handlerKey, (handler, DateTime.UtcNow));
                    handler.Begin();
                }

                var load = new DbExecutionLoad(default, handler);
                var actorValue = actor ?? 0L;

                // 1. Find the current max sub_ver for this (parent, ver) — 0 when no thumbnails exist yet.
                var currentMaxSubVer = await _agw.ScalarAsync<int?>(moduleCuid, INSTANCE.DOCVERSION.FIND_LATEST_SUB_VER, load,
                    (PARENT, documentId), (VERSION, contentVer));
                int nextSubVer = (currentMaxSubVer ?? 0) + 1;

                // 2. Insert the thumbnail doc_version row.
                await _agw.ExecAsync(moduleCuid, INSTANCE.DOCVERSION.INSERT_THUMBNAIL, load,
                    (PARENT, documentId), (VERSION, contentVer), (SUB_VER, nextSubVer), (ACTOR, actorValue));

                // 3. Fetch back to get the auto-generated id and cuid.
                var dvRow = await _agw.RowAsync(moduleCuid, INSTANCE.DOCVERSION.EXISTS_BY_VERSION_SUBVER, load,
                    (PARENT, documentId), (VERSION, contentVer), (SUB_VER, nextSubVer));
                if (dvRow == null || dvRow.Count < 1)
                    throw new Exception($"Unable to retrieve new thumbnail doc_version for document {documentId}, ver {contentVer}, sub_ver {nextSubVer}.");

                long newId = dvRow.GetLong("id");
                string newUid = dvRow.GetString("uid");

                if (newId < 1 || string.IsNullOrWhiteSpace(newUid))
                    throw new Exception("Thumbnail doc_version row has an invalid id or uid.");

                if (!newUid.IsValidGuid(out Guid newGuid) && !newUid.IsCompactGuid(out newGuid))
                    throw new Exception($"Unable to parse GUID from thumbnail doc_version uid '{newUid}'.");

                return (newId, newGuid);
            } catch (Exception ex) {
                _logger?.LogError(ex.Message + Environment.NewLine + ex.StackTrace);
                var handlerKey = GetHandlerKey(callId, moduleCuid);
                if (!string.IsNullOrWhiteSpace(handlerKey) && _handlers.ContainsKey(handlerKey)) {
                    _handlers[handlerKey].handler?.Rollback();
                    _handlers.Remove(handlerKey, out _);
                }
                if (ThrowExceptions) throw;
                return (0, Guid.Empty);
            }
        }

        /// <summary>
        /// Returns the storage info row for the latest thumbnail sub-version of a specific content version.
        /// Looks up by (documentId, contentVer) and returns the row with the highest sub_ver &gt; 0.
        /// Returns a failed <see cref="IFeedback"/> when no thumbnail exists.
        /// </summary>
        public async Task<IFeedback> GetLatestThumbInfo(string moduleCuid, long documentId, int contentVer) {
            var fb = new Feedback();
            try {
                if (string.IsNullOrWhiteSpace(moduleCuid)) return fb.SetMessage("Module CUID is required.");
                if (documentId < 1) return fb.SetMessage("documentId must be positive.");
                if (contentVer < 1) return fb.SetMessage("contentVer must be positive.");
                if (!_agw.ContainsKey(moduleCuid)) return fb.SetMessage($"No adapter found for module {moduleCuid}.");

                var row = await _agw.RowAsync(moduleCuid, INSTANCE.DOCVERSION.GET_LATEST_THUMB_BY_VERSION, default,
                    (PARENT, documentId), (VERSION, contentVer));
                if (row == null || row.Count < 1)
                    return fb.SetMessage($"No thumbnail found for document {documentId}, version {contentVer}.");
                return fb.SetStatus(true).SetMessage("Thumbnail info obtained.").SetResult(row);
            } catch (Exception ex) {
                var msg = ex.Message + Environment.NewLine + ex.StackTrace;
                _logger?.LogError(msg);
                return fb.SetMessage(msg);
            }
        }
    }
}
