using Haley.Abstractions;
using Haley.Models;
using Microsoft.Extensions.Logging;
using static Haley.Internal.IndexingConstant;
using static Haley.Internal.IndexingQueries;

namespace Haley.Utils {
    /// <summary>
    /// Partial class — document and directory read queries.
    /// All public overloads ultimately delegate to the internal <c>GetDocVersionInfoInternal</c>
    /// or to the name-based workspace search path.
    /// </summary>
    internal partial class MariaDBIndexing {
        /// <summary>Fetches the latest <c>version_info</c> row for a doc_version identified by its auto-increment ID.</summary>
        public Task<IFeedback> GetDocVersionInfo(string moduleCuid, long id) {
            return GetDocVersionInfoInternal(moduleCuid, id, string.Empty);
        }
        /// <summary>Fetches the latest <c>version_info</c> row for a doc_version identified by its compact-N CUID.</summary>
        public Task<IFeedback> GetDocVersionInfo(string moduleCuid, string cuid) {
            return GetDocVersionInfoInternal(moduleCuid, 0, cuid);
        }
        /// <summary>
        /// Fetches the latest <c>version_info</c> row for a file identified by name within a specific
        /// workspace ID and directory. Looks up the document by joining <c>vault</c>, <c>name_store</c>,
        /// <c>extension</c>, and <c>directory</c> tables, then retrieves the latest version.
        /// </summary>
        /// <param name="wsId">Numeric workspace DB ID (must be &gt; 0).</param>
        /// <param name="file_name">Original file name including extension.</param>
        /// <param name="dir_name">Display name of the parent directory; defaults to <c>"default"</c>.</param>
        /// <param name="dir_parent_id">DB ID of the parent directory row (0 = root).</param>
        public async Task<IFeedback> GetDocVersionInfo(string moduleCuid, long wsId, string file_name, string dir_name = VaultConstants.DEFAULT_NAME, long dir_parent_id = 0) {
            Feedback result = new Feedback();
            try {
                if (string.IsNullOrWhiteSpace(moduleCuid) || wsId < 1) return result.SetMessage($@"Module CUID & non-zero worspace id are mandatory to fetch document info");
                if (string.IsNullOrWhiteSpace(file_name)) return result.SetMessage($@"For this approach, file name required for searching.");
                if (!_agw.ContainsKey(moduleCuid)) return result.SetMessage($@"No adapter found for the key {moduleCuid}");

                var name = Path.GetFileNameWithoutExtension(file_name).ToDBName();
                //if (!caseSensitive) name = name.ToDBName();

                var extension = Path.GetExtension(file_name)?.ToDBName() ?? VaultConstants.DEFAULT_NAME;

                var docId = await _agw.ScalarAsync<long?>(moduleCuid, INSTANCE.DOCUMENT.GET_BY_NAME, default, (NAME, name.ToDBName()), (EXT, extension), (WSPACE, wsId), (PARENT, dir_parent_id), (DIRNAME, dir_name.ToDBName()));
                if (docId == null || docId < 1) return result.SetMessage($@"Unable to fetch the document for the given inputs. FileName :  {file_name} ; WSID : {wsId} ; DirName : {dir_name}");

                var dic = await _agw.RowAsync(moduleCuid, INSTANCE.DOCVERSION.GET_LATEST_BY_PARENT, default, (PARENT, docId.Value));
                if (dic == null || dic.Count < 1) return result.SetMessage($@"Unable to fetch the document version info for the given inputs. Document Id : {docId} ; FileName :  {file_name} ; WSID : {wsId} ; DirName : {dir_name}");
                return result.SetStatus(true).SetMessage("Document version info obtained").SetResult(dic);
            } catch (Exception ex) {
                _logger?.LogError(ex.StackTrace);
                return result.SetMessage(ex.StackTrace);
            }

        }

        /// <summary>
        /// Overload that accepts a workspace CUID string instead of a numeric workspace ID.
        /// Resolves the workspace numeric ID from the core DB before delegating to the ID-based overload.
        /// </summary>
        public async Task<IFeedback> GetDocVersionInfo(string moduleCuid, string wsCuid, string file_name, string dir_name = VaultConstants.DEFAULT_NAME, long dir_parent_id = 0) {
            try {
                if (string.IsNullOrWhiteSpace(wsCuid)) return new Feedback() { Message = "Workspace CUID cannot be empty." };
                var wsId = await _agw.ScalarAsync<long?>(_key, WORKSPACE.EXISTS_BY_CUID, default, (CUID, wsCuid));
                if (wsId.HasValue) return await GetDocVersionInfo(moduleCuid, wsId.Value, file_name, dir_name, dir_parent_id);
                return new Feedback() { Message = "Unable to fetch the information for the given inputs." };
            } catch (Exception ex) {
                _logger?.LogError(ex.StackTrace);
                return new Feedback().SetMessage(ex.StackTrace);
            }
        }

        /// <summary>
        /// Returns the display name of the directory that contains the given file.
        /// Requires a non-empty file CUID; queries the module DB by joining <c>doc_version</c>,
        /// <c>document</c>, and <c>directory</c> tables.
        /// </summary>
        public async Task<IFeedback<string>> GetParentName(IVaultFileReadRequest request) {
            var fb = new Feedback<string>();
            try {
                if (request == null) return fb.SetMessage("Input request cannot be empty");
                if (request.Scope?.Workspace == null || request.Scope.Workspace.Cuid == Guid.Empty) return fb.SetMessage("Workspace CUID cannot be empty to find the parent name");
                if (request.Scope?.Module == null || request.Scope.Module.Cuid == Guid.Empty) return fb.SetMessage($@"Module CUID is mandatory to fetch parent info");
                if (string.IsNullOrWhiteSpace(request.File?.Cuid)) return fb.SetMessage($@"File CUID is mandatory to fetch parent info");
                var moduleCuid = request.Scope.Module.Cuid.ToString("N");

                if (!_agw.ContainsKey(moduleCuid)) return fb.SetMessage($@"No adapter found for the key {moduleCuid}");

                var row = await _agw.RowAsync(moduleCuid, INSTANCE.DIRECTORY.GET_BY_DOC_VERSION_CUID, default, (CUID, request.File.Cuid));
                if (row == null) return fb.SetMessage($@"Unable to fetch the parent information for {request.File.Cuid}");
                return fb.SetStatus(true).SetResult(row.GetString("display_name"));
            } catch (Exception ex) {
                var msg = ex.Message + Environment.NewLine + ex.StackTrace;
                _logger?.LogError(msg);
                return fb.SetStatus(false).SetMessage(msg);
            }
        }

        /// <summary>
        /// Internal implementation: fetches a full <c>GET_FULL_BY_ID</c> or <c>GET_FULL_BY_CUID</c>
        /// row from the per-module DB. Exactly one of <paramref name="id"/> or <paramref name="cuid"/> must be supplied.
        /// </summary>
        async Task<IFeedback> GetDocVersionInfoInternal(string moduleCuid, long id, string cuid) {
            Feedback result = new Feedback();
            try {
                if (string.IsNullOrWhiteSpace(moduleCuid)) return result.SetMessage($@"Module CUID is mandatory to fetch document info");
                if (id < 1 && string.IsNullOrWhiteSpace(cuid)) return result.SetMessage($@"Either Id or CUID value is required.");
                if (!_agw.ContainsKey(moduleCuid)) return result.SetMessage($@"No adapter found for the key {moduleCuid}");

                var query = id < 1 ? INSTANCE.DOCVERSION.GET_FULL_BY_CUID : INSTANCE.DOCVERSION.GET_FULL_BY_ID;
                var dic = await _agw.RowAsync(moduleCuid, query, default, (VALUE, id < 1 ? (object)cuid : id));
                if (dic == null || dic.Count < 1) return result.SetMessage($@"Unable to fetch the document version info with either cuid {cuid} or id {id}");
                return result.SetStatus(true).SetMessage("Document version info obtained").SetResult(dic);
            } catch (Exception ex) {
                _logger?.LogError(ex.StackTrace);
                return result.SetMessage(ex.StackTrace);
            }
        }
    }
}
