using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.IndexingConstant;
using Microsoft.Extensions.Logging;
using static Haley.Internal.IndexingQueries;

namespace Haley.Utils {
    /// <summary>
    /// Partial class — version-level and document-level metadata read/write operations.
    /// </summary>
    internal partial class MariaDBIndexing {

        public async Task<bool> IsLatestVersion(string moduleCuid, string versionCuid) {
            try {
                if (string.IsNullOrWhiteSpace(moduleCuid) || string.IsNullOrWhiteSpace(versionCuid)) return false;
                if (!_agw.ContainsKey(moduleCuid)) return false;
                var result = await _agw.ScalarAsync<int?>(moduleCuid, INSTANCE.DOCVERSION.IS_LATEST_BY_CUID, default, (VALUE, ToDbCuid(versionCuid)));
                return result.HasValue && result.Value == 1;
            } catch (Exception ex) {
                _logger?.LogError(ex.StackTrace);
                return false;
            }
        }

        public async Task<IFeedback<string>> GetVersionMetadata(string moduleCuid, string versionCuid) {
            var fb = new Feedback<string>();
            try {
                if (string.IsNullOrWhiteSpace(moduleCuid) || string.IsNullOrWhiteSpace(versionCuid))
                    return fb.SetMessage("Module CUID and version CUID are required.");
                if (!_agw.ContainsKey(moduleCuid))
                    return fb.SetMessage($"No adapter found for key {moduleCuid}");

                var versionId = await _agw.ScalarAsync<long?>(moduleCuid, INSTANCE.DOCVERSION.EXISTS_ACTIVE_BY_CUID, default, (CUID, ToDbCuid(versionCuid)));
                if (versionId == null || versionId < 1)
                    return fb.SetMessage($"Version not found: {versionCuid}");

                var metadata = await _agw.ScalarAsync<string>(moduleCuid, INSTANCE.DOCVERSION.GET_META_BY_CUID, default, (VALUE, ToDbCuid(versionCuid)));
                return fb.SetStatus(true).SetResult(metadata ?? string.Empty);
            } catch (Exception ex) {
                _logger?.LogError(ex.StackTrace);
                return fb.SetMessage(ex.StackTrace);
            }
        }

        public async Task<IFeedback> SetVersionMetadata(string moduleCuid, string versionCuid, string metadata) {
            var fb = new Feedback();
            try {
                if (string.IsNullOrWhiteSpace(moduleCuid) || string.IsNullOrWhiteSpace(versionCuid))
                    return fb.SetMessage("Module CUID and version CUID are required.");
                if (!_agw.ContainsKey(moduleCuid))
                    return fb.SetMessage($"No adapter found for key {moduleCuid}");

                var versionId = await _agw.ScalarAsync<long?>(moduleCuid, INSTANCE.DOCVERSION.EXISTS_ACTIVE_BY_CUID, default, (CUID, ToDbCuid(versionCuid)));
                if (versionId == null || versionId < 1)
                    return fb.SetMessage($"Version not found: {versionCuid}");

                object mdVal = string.IsNullOrEmpty(metadata) ? DBNull.Value : (object)metadata;
                await _agw.ExecAsync(moduleCuid, INSTANCE.DOCVERSION.UPDATE_META_BY_ID, default, (ID, versionId.Value), (METADATA, mdVal));
                return fb.SetStatus(true);
            } catch (Exception ex) {
                _logger?.LogError(ex.StackTrace);
                return fb.SetMessage(ex.StackTrace);
            }
        }

        public async Task<IFeedback<string>> GetDocumentMetadata(string moduleCuid, string documentCuid) {
            var fb = new Feedback<string>();
            try {
                if (string.IsNullOrWhiteSpace(moduleCuid) || string.IsNullOrWhiteSpace(documentCuid))
                    return fb.SetMessage("Module CUID and document CUID are required.");
                if (!_agw.ContainsKey(moduleCuid))
                    return fb.SetMessage($"No adapter found for key {moduleCuid}");

                var documentId = await _agw.ScalarAsync<long?>(moduleCuid, INSTANCE.DOCUMENT.EXISTS_BY_CUID, default, (CUID, ToDbCuid(documentCuid)));
                if (documentId == null || documentId < 1)
                    return fb.SetMessage($"Document not found: {documentCuid}");

                var metadata = await _agw.ScalarAsync<string>(moduleCuid, INSTANCE.DOCUMENT.GET_META_BY_CUID, default, (CUID, ToDbCuid(documentCuid)));
                return fb.SetStatus(true).SetResult(metadata ?? string.Empty);
            } catch (Exception ex) {
                _logger?.LogError(ex.StackTrace);
                return fb.SetMessage(ex.StackTrace);
            }
        }

        public async Task<IFeedback> SetDocumentMetadata(string moduleCuid, string documentCuid, string metadata) {
            var fb = new Feedback();
            try {
                if (string.IsNullOrWhiteSpace(moduleCuid) || string.IsNullOrWhiteSpace(documentCuid))
                    return fb.SetMessage("Module CUID and document CUID are required.");
                if (!_agw.ContainsKey(moduleCuid))
                    return fb.SetMessage($"No adapter found for key {moduleCuid}");
                object mdVal = string.IsNullOrEmpty(metadata) ? DBNull.Value : (object)metadata;
                await _agw.ExecAsync(moduleCuid, INSTANCE.DOCUMENT.UPSERT_META, default, (CUID, ToDbCuid(documentCuid)), (METADATA, mdVal));
                return fb.SetStatus(true);
            } catch (Exception ex) {
                _logger?.LogError(ex.StackTrace);
                return fb.SetMessage(ex.StackTrace);
            }
        }
    }
}
