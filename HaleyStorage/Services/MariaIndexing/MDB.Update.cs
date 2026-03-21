using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using System.Security.AccessControl;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.XPath;
using static Haley.Internal.IndexingConstant;
using static Haley.Internal.IndexingQueries;
using static System.Net.Mime.MediaTypeNames;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Haley.Utils {
    /// <summary>
    /// Partial class — <c>version_info</c> update pipeline.
    /// Writes storage name, path, size, hash, staging path, flags, and metadata for a doc_version.
    /// Extended fields (staging_path, flags, metadata) are only written when the caller provides them.
    /// </summary>
    public partial class MariaDBIndexing : IVaultIndexing {
        /// <summary>
        /// Upserts <c>version_info</c> core fields (storage_name, storage_path, size, hash, synced_at)
        /// then conditionally updates extended fields (staging_path, flags, metadata) when present on
        /// <paramref name="file"/>. Reads back the updated row as a confirmation step.
        /// </summary>
        /// <param name="file">
        /// Must carry <see cref="IVaultFileRoute.Id"/> and <see cref="IVaultFileRoute.Cuid"/>.
        /// Additional properties (StagingRef, Flags, Hash, Metadata) are written when populated.
        /// </param>
        /// <param name="callId">Optional call ID used to look up an open transaction handler.</param>
        public async Task<IFeedback> UpdateDocVersionInfo(string moduleCuid, IVaultFileRoute file, string callId = null) {
            Feedback result = new Feedback();
            try {
                if (string.IsNullOrWhiteSpace(moduleCuid)) return result.SetMessage($@"Module CUID is mandatory to update document info");
                if (!_agw.ContainsKey(moduleCuid)) return result.SetMessage($@"No adapter found for the key {moduleCuid}");
                ITransactionHandler handler = GetTransactionHandlerCache(callId, moduleCuid);

                if (file == null || string.IsNullOrWhiteSpace(file.Cuid)) return result.SetMessage("No file info. Nothing to update");

                var docvExists = await _agw.Scalar(new AdapterArgs(moduleCuid) { Query = INSTANCE.DOCVERSION.EXISTS_BY_ID }.ForTransaction(handler), (ID, file.Id));
                if (docvExists == null) return result.SetMessage($@"Unable to find any document version with cuid {file.Cuid} and id {file.Id} in DB {moduleCuid}");

                // Extract hash and synced_at — nullable, pass DBNull when not provided.
                object hashVal = DBNull.Value;
                object syncedAtVal = DBNull.Value;
                if (file.TryGetProp<string>(out var hv, "Hash", "hash") && !string.IsNullOrWhiteSpace(hv)) hashVal = hv;
                if (file.TryGetProp<DateTime>(out var sav, "SyncedAt", "synced_at")) syncedAtVal = sav;

                // Core upsert: storage_name/storage_path/size/hash/synced_at
                await _agw.NonQuery(
                    new AdapterArgs(moduleCuid) { Query = INSTANCE.DOCVERSION.INSERT_INFO }.ForTransaction(handler),
                    (ID, file.Id),
                    (SAVENAME, file.StorageName),
                    (PATH, file.StorageRef),
                    (SIZE, file.Size),
                    (HASH, hashVal),
                    (SYNCED_AT, syncedAtVal)
                );

                // Optional extended update (only if caller provides these fields)
                // This avoids forcing existing apps to suddenly send flags/metadata etc.
                if (file.TryGetProp<string>(out var stagingPath, "StagingRef", "staging_path") ||
                    file.TryGetProp<string>(out var metadata, "Metadata", "metadata") ||
                    file.TryGetProp<int>(out var flags, "Flags", "flags") ||
                    !(hashVal is DBNull) || !(syncedAtVal is DBNull)) {

                    object sp = DBNull.Value;
                    object md = DBNull.Value;
                    object fl = DBNull.Value;

                    if (file.TryGetProp<string>(out var spv, "StagingRef", "staging_path") && !string.IsNullOrWhiteSpace(spv)) sp = spv;
                    if (file.TryGetProp<string>(out var mdv, "Metadata", "metadata") && mdv != null) md = mdv;
                    if (file.TryGetProp<int>(out var flv, "Flags", "flags")) fl = flv;

                    // Only run if at least one field is actually populated.
                    if (!(sp is DBNull) || !(md is DBNull) || !(fl is DBNull) || !(hashVal is DBNull) || !(syncedAtVal is DBNull)) {
                        await _agw.NonQuery(
                            new AdapterArgs(moduleCuid) { Query = INSTANCE.DOCVERSION.UPDATE_INFO_EXT }.ForTransaction(handler),
                            (ID, file.Id),
                            (STAGINGPATH, sp),
                            (METADATA, md),
                            (FLAGS, fl),
                            (HASH, hashVal),
                            (SYNCED_AT, syncedAtVal)
                        );
                    }
                }

                var updatedInfo = await _agw.Read(
                    new AdapterArgs(moduleCuid) { Query = INSTANCE.DOCVERSION.GET_INFO, Filter = ResultFilter.FirstDictionary }.ForTransaction(handler),
                    (ID, file.Id)
                );

                if (updatedInfo == null || !(updatedInfo is Dictionary<string, object> dic) || dic.Count < 1)
                    return result.SetMessage("Unable to confirm if the document version info is properly updated or not.");

                return result.SetStatus(true).SetMessage("Updated document info").SetResult(dic.ToJson());
            } catch (Exception ex) {
                return result.SetMessage(ex.StackTrace);
            }
        }

        /// <summary>
        /// Overwrites the human-readable <c>doc_info.display_name</c> for the document that owns
        /// <paramref name="versionId"/>. Used by <c>CreatePlaceholder</c> to apply a custom display
        /// name that differs from the raw file name registered during document creation.
        /// </summary>
        public async Task<IFeedback> UpdateDocDisplayName(string moduleCuid, long versionId, string displayName) {
            var fb = new Feedback();
            try {
                if (string.IsNullOrWhiteSpace(moduleCuid) || versionId < 1 || string.IsNullOrWhiteSpace(displayName))
                    return fb.SetMessage("moduleCuid, versionId, and displayName are all required.");
                if (!_agw.ContainsKey(moduleCuid))
                    return fb.SetMessage($"No adapter found for key {moduleCuid}.");

                var docId = await _agw.ScalarAsync<long?>(moduleCuid, INSTANCE.DOCVERSION.GET_DOCUMENT_ID_BY_VERSION_ID, default, (VALUE, versionId));
                if (!(docId > 0))
                    return fb.SetMessage($"Version {versionId} not found in module {moduleCuid}.");

                await _agw.ExecAsync(moduleCuid, INSTANCE.DOCUMENT.INSERT_INFO, default, (PARENT, docId!.Value), (DNAME, displayName));
                return fb.SetStatus(true);
            } catch (Exception ex) {
                return fb.SetMessage(ex.Message);
            }
        }
    }
}
