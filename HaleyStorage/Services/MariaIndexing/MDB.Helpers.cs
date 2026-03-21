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
    /// Partial class — internal DB helpers for the registration and document-insert pipeline.
    /// Contains the workspace/directory/namestore "ensure" helpers and the low-level
    /// <c>InsertAndFetchID*</c> check-then-insert pattern.
    /// </summary>
    internal partial class MariaDBIndexing {
        /// <summary>
        /// Thread-safe one-shot validation: runs <see cref="Validate"/> the first time it is called.
        /// Subsequent calls are no-ops, guarded by a <see cref="SemaphoreSlim"/>.
        /// </summary>
        async Task EnsureValidation() {
            if (isValidated) return;
            await _validateLock.WaitAsync();
            try {
                if (!isValidated) {
                    await Validate();
                    isValidated = true;
                }
            } finally {
                _validateLock.Release();
            }
        }

        /// <summary>
        /// Ensures the workspace row exists in the per-module DB. If absent, inserts it
        /// (insert-ignore by workspace ID). Returns the workspace numeric ID.
        /// Throws <see cref="ArgumentNullException"/> when the workspace is not in the cache.
        /// </summary>
        async Task<(bool status, long id)> EnsureWorkSpace(IVaultReadRequest request) {
            var wsCuidKey = request.Scope.Workspace.Cuid.ToString("N");
            if (!_cache.ContainsKey(wsCuidKey)) throw new ArgumentNullException($@"Unable to find any workspace for {wsCuidKey}");
            var dbid = request.Scope.Module.Cuid.ToString("N");
            var wspace = _cache[wsCuidKey];
            //Check if workspace exists in the database.
            var ws = await InsertAndFetchIDScalar(dbid,
                () => (INSTANCE.WORKSPACE.EXISTS, Consolidate((ID, wspace.Id))),
                () => (INSTANCE.WORKSPACE.INSERT, Consolidate((ID, wspace.Id))),
                readOnly:request.ReadOnlyMode,
                $@"Unable to insert the workspace  {wspace.Id}"); //Workspace registration can happen without transaction, as it might be needed for other items later.
            return (true, wspace.Id);
        }

        /// <summary>
        /// Ensures a directory row exists in the per-module DB for the request's folder scope.
        /// Falls back to "default" when no folder is specified on the request.
        /// Returns the directory numeric ID and its compact-N CUID.
        /// </summary>
        async Task<(bool status, (long id, string uid) result)> EnsureDirectory(IVaultReadRequest request, long ws_id) {
            if (ws_id == 0) return (false, (0, string.Empty));
            var dbid = request.Scope.Module.Cuid.ToString("N");
            //If directory name is not provided, then go for "default" as usual
            var dirId = request.Scope.Folder?.Id ?? 0;
            var dirCuid = request.Scope.Folder?.Cuid ?? string.Empty;

            var dirParent = request.Scope.Folder?.Parent?.Id ?? 0;
            var dirName = request.Scope.Folder?.DisplayName ?? VaultConstants.DEFAULT_NAME;
            var dirDbName = dirName.ToDBName();

            var dirInfo = await InsertAndFetchIDRead(dbid,
                () => {
                    if (dirId < 1 && string.IsNullOrWhiteSpace(dirCuid))
                        return (INSTANCE.DIRECTORY.EXISTS, Consolidate((WSPACE, ws_id), (PARENT, dirParent), (NAME, dirDbName)));
                    var query = dirId < 1 ? INSTANCE.DIRECTORY.EXISTS_BY_CUID : INSTANCE.DIRECTORY.EXISTS_BY_ID;
                    return (query, Consolidate((VALUE, dirId < 1 ? dirCuid : dirId)));
                },
                () => (INSTANCE.DIRECTORY.INSERT, Consolidate((WSPACE, ws_id), (PARENT, dirParent), (NAME, dirDbName), (DNAME, dirName))),
                readOnly: request.ReadOnlyMode,
                $@"Unable to insert the directory {dirName} to the workspace : {ws_id}"); //Directory registration, same as the workspace doesn't need any transaction as it might be needed by other systems.

            return (true, (dirInfo.id, dirInfo.uid));
        }

        /// <summary>
        /// Retrieves an open <see cref="ITransactionHandler"/> from the in-flight handler cache
        /// by <c>callId###dbId</c> key. Returns <c>null</c> when not found (auto-commit mode).
        /// </summary>
        ITransactionHandler GetTransactionHandlerCache(string callId, string dbid) {
            if (string.IsNullOrWhiteSpace(callId) || string.IsNullOrWhiteSpace(dbid)) return null;
            var key = GetHandlerKey(callId, dbid);
            if (!_handlers.ContainsKey(key)) return null;
            //If handler was created long back, then remove it.
            return _handlers[key].handler;
        }

        /// <summary>
        /// Check-then-insert pattern that returns a single numeric ID.
        /// Runs <paramref name="check"/> first; if the row is absent and <paramref name="readOnly"/> is false,
        /// runs <paramref name="insert"/> then re-checks. Throws on failure.
        /// </summary>
        async Task<long> InsertAndFetchIDScalar(string dbid, Func<(string query,(string key,object value)[] parameters)> check, Func<(string query, (string key, object value)[] parameters)> insert = null, bool readOnly = false, string failureMessage = "Error", bool preCheck = true,string callId = null) {
            if (check == null) return 0;
            ITransactionHandler handler = GetTransactionHandlerCache(callId, dbid);

            var checkInput = check.Invoke();
            object info = null;
            if (preCheck) info = await _agw.Scalar(new AdapterArgs(dbid) { Query = checkInput.query }.ForTransaction(handler,false) , checkInput.parameters);

            if (info == null) {
                if (insert == null || readOnly) return 0;
                var insertInput = insert.Invoke();
                await _agw.NonQuery(new AdapterArgs(dbid) { Query = insertInput.query }.ForTransaction(handler,false), insertInput.parameters);

                info = await _agw.Scalar(new AdapterArgs(dbid) { Query = checkInput.query }.ForTransaction(handler,false), checkInput.parameters);
            }
            long id = 0;
            if (info == null || !long.TryParse(info.ToString(), out id)) throw new Exception($@"{failureMessage} from the database {dbid}");
            return id;
        }

        /// <summary>
        /// Check-then-insert pattern that returns both a numeric ID and a CUID string.
        /// Used for rows that have a <c>cuid</c> column (directory, document, doc_version).
        /// Throws when the row cannot be found or inserted.
        /// </summary>
        async Task<(long id, string uid)> InsertAndFetchIDRead(string dbid, Func<(string query, (string key, object value)[] parameters)> check = null, Func<(string query, (string key, object value)[] parameters)> insert = null, bool readOnly = false, string failureMessage = "Error", bool preCheck = true, string callId = null) {
            if (check == null) return (0, string.Empty);
            var checkInput = check.Invoke();
            ITransactionHandler handler = GetTransactionHandlerCache(callId, dbid);

            object info = null;
            if (preCheck) info = await _agw.Read(new AdapterArgs(dbid) { Query = checkInput.query, Filter = ResultFilter.FirstDictionary }.ForTransaction(handler,false), checkInput.parameters);

            if (info == null || !(info is Dictionary<string, object> dic1) || dic1.Count < 1) {
                if (insert == null || readOnly) return (0, string.Empty);
                var insertInput = insert.Invoke();
                await _agw.NonQuery(new AdapterArgs(dbid) { Query = insertInput.query }.ForTransaction(handler, false), insertInput.parameters);
                info = await _agw.Read(new AdapterArgs(dbid) { Query = checkInput.query, Filter = ResultFilter.FirstDictionary }.ForTransaction(handler, false), checkInput.parameters);
            }
            long id = 0;
            if (info == null || !(info is Dictionary<string, object> dic) || dic.Count < 1) throw new Exception($@"{failureMessage} from the database {dbid}");
            return ((long)dic["id"], (string)dic["uid"]);
        }

        /// <summary>Convenience helper that converts a params array of named parameters to an array, matching the expected signature.</summary>
        (string key,object value)[] Consolidate(params (string, object)[] parameters) {
            return parameters;
        }

        /// <summary>
        /// Ensures the name-store chain exists: extension → vault name → name_store composite row.
        /// Returns the <c>name_store</c> ID, which is used as the FK when inserting a document row.
        /// </summary>
       async Task<(bool status, long id)> EnsureNameStore(IVaultReadRequest request) {
            if (string.IsNullOrWhiteSpace(request.RequestedName)) return (false, 0);
            var name = Path.GetFileNameWithoutExtension(request.RequestedName)?.Trim();
            var ext = Path.GetExtension(request.RequestedName)?.Trim();
            if (string.IsNullOrWhiteSpace(ext)) ext = VaultConstants.DEFAULT_NAME;
            if (string.IsNullOrWhiteSpace(name)) return (false, 0);
            name = name.ToDBName();
            ext = ext.ToDBName();

            var dbid = request.Scope.Module.Cuid.ToString("N");

            //Extension Exists?
            long extId = await InsertAndFetchIDScalar(dbid, () => (INSTANCE.EXTENSION.EXISTS, Consolidate((NAME, ext))), () => (INSTANCE.EXTENSION.INSERT, Consolidate((NAME, ext))), readOnly: request.ReadOnlyMode, $@"Unable to fetch extension id for {ext}");

            // Name Exists ?
            long nameId = await InsertAndFetchIDScalar(dbid, () => (INSTANCE.VAULT.EXISTS, Consolidate((NAME, name))), () => (INSTANCE.VAULT.INSERT, Consolidate((NAME, name))), readOnly: request.ReadOnlyMode, $@"Unable to fetch name id for {name}");

            //Namestore Exists?
            long nsId = await InsertAndFetchIDScalar(dbid, () => (INSTANCE.NAMESTORE.EXISTS, Consolidate((NAME, nameId), (EXT, extId))), () => (INSTANCE.NAMESTORE.INSERT, Consolidate((NAME, nameId), (EXT, extId))), readOnly: request.ReadOnlyMode, $@"Unable to fetch name store id for name : {name} and extension : {ext}");

            return (true, nsId);
        }

        /// <summary>Builds the composite cache key used to store transaction handlers: <c>callid###dbid</c> (lowercase dbid).</summary>
        string GetHandlerKey(string callid, string dbid) {
            return $@"{callid}###{dbid.ToLower()}";
        }

        /// <summary>
        /// Core document-registration flow: ensures workspace → directory → name-store → document → doc_version,
        /// opens a transaction, sets the resulting ID and CUID on <paramref name="holder"/>, and returns both.
        /// Rolls back the transaction on any exception.
        /// </summary>
        async Task<(long id, Guid guid)> RegisterDocumentsInternal(IVaultReadRequest request, IVaultStorable holder) {
            try {
                if (request.ReadOnlyMode) throw new ArgumentException("Cannot register a document in readonly mode");
                CleanupStaleHandlers();
                //If we are in ParseMode, we still do all the process, but, store the file as is with Parsing information.
                //For parse mode, let us not throw any exception.
                //Generate a handler.
                var dbid = request.Scope.Module.Cuid.ToString("N");
                var handlerKey = GetHandlerKey(request.CallID, dbid);
                var handler = _agw.GetTransactionHandler(dbid);

                if (handler != null) {
                    if (_handlers.ContainsKey(handlerKey)) throw new Exception($@"A similar transaction with same key already exists: {handlerKey}");
                    _handlers.TryAdd(handlerKey, (handler, DateTime.UtcNow));
                    handler.Begin();
                }

                (long id, Guid guid) result = (0, Guid.Empty);
                var ws = await EnsureWorkSpace(request);
                if (!ws.status) return result;
                var dir = await EnsureDirectory(request, ws.id);
                if (!dir.status) return result;
                var ns = await EnsureNameStore(request);
                if (!ns.status) return result;
              
                var docInfo = await InsertAndFetchIDRead(dbid,() => (INSTANCE.DOCUMENT.EXISTS, Consolidate((PARENT, dir.result.id), (NAME, ns.id))), callId: request.CallID);
                bool docExists = docInfo.id != 0;
                if (!docExists) {
                    // Insert it.
                    docInfo = await InsertAndFetchIDRead(dbid, 
                        () => (INSTANCE.DOCUMENT.EXISTS, Consolidate((PARENT, dir.result.id), (NAME, ns.id))),
                        ()=> (INSTANCE.DOCUMENT.INSERT, Consolidate((WSPACE,ws.id), (PARENT, dir.result.id), (NAME, ns.id))),
                         readOnly: request.ReadOnlyMode,
                        $@"Unable to insert document with name {request.RequestedName}",false, callId: request.CallID);
                    var dname = Path.GetFileName(request.RequestedName);
                    await _agw.NonQuery(new AdapterArgs(dbid) { Query = INSTANCE.DOCUMENT.INSERT_INFO }.ForTransaction(handler), (PARENT, docInfo.id), (DNAME, dname));
                }

                int version = 1;
                //If Doc exists.. we just need to revise the version.
                if (docExists) {
                    //Assuming that there is a version. Get the latest version.
                    var currentVersion = await _agw.Scalar(new AdapterArgs(dbid) { Query = INSTANCE.DOCVERSION.FIND_LATEST }.ForTransaction(handler), (PARENT, docInfo.id));
                    if (currentVersion != null && int.TryParse(currentVersion.ToString(),out int cver)) {
                        version = ++cver;
                    }
                }

                var dvInfo = await InsertAndFetchIDRead(dbid,
                    () => (INSTANCE.DOCVERSION.EXISTS, Consolidate((PARENT, docInfo.id), (VERSION, version))),
                    () => (INSTANCE.DOCVERSION.INSERT, Consolidate((PARENT, docInfo.id), (VERSION, version))),
                     readOnly: request.ReadOnlyMode,
                    $@"Unable to insert document version for the document {docInfo.id}", false, callId: request.CallID);

                if (dvInfo.id > 0 && !string.IsNullOrWhiteSpace(dvInfo.uid)) {
                    //Check if the incoming uid is in proper GUID format.
                    Guid dvId = Guid.Empty;
                    if (dvInfo.uid.IsValidGuid(out dvId) || dvInfo.uid.IsCompactGuid(out dvId)) {
                        result = (dvInfo.id, dvId);
                    }
                }
                
                if (holder != null) {
                    holder.SetCuid(result.guid);
                    holder.Id = result.id;   // IIdentityBase.Id has public setter
                    holder.Version = version;
                }

                return result;
            } catch (Exception ex) {
                _logger?.LogError(ex.Message + Environment.NewLine + ex.StackTrace);
                var dbid = request.Scope.Module.Cuid.ToString("N");
                var handlerKey = GetHandlerKey(request.CallID, dbid);
                if (!string.IsNullOrWhiteSpace(handlerKey) && _handlers.ContainsKey(handlerKey)) {
                    _handlers[handlerKey].handler?.Rollback(); //roll everything back.
                    _handlers.Remove(handlerKey,out _);
                }
                if (ThrowExceptions) throw ex; //For Parse mode, let us not throw any exceptions.
                return (0, Guid.Empty);
            }
        }
        /// <summary>
        /// Creates a per-module MariaDB schema from the <c>dssclient.sql</c> template if it does not already exist,
        /// then duplicates the adapter gateway entry under the module's CUID so all subsequent per-module
        /// queries target the correct database.
        /// </summary>
        async Task CreateModuleDBInstance(IVaultBase dirInfo) {
            if (!(dirInfo is IVaultModule info)) return;
            if (string.IsNullOrWhiteSpace(info.DatabaseName)) info.DatabaseName = $@"{DB_MODULE_NAME_PREFIX}{info.Cuid.ToString("N")}";
            //What if the CUID is changed? Should we use the guid instead? 
            //But, guid is not unique across clients. So, we use cuid.
            //So, when we create the module, we use the cuid as the database name.
            //TODO : IF A CUID IS CHANGED, THEN WE NEED TO UPDATE THE DATABASE NAME IN THE DB.
            var sqlFile = Path.Combine(AssemblyUtils.GetBaseDirectory(), DB_SQL_FILE_LOCATION, DB_CLIENT_SQL_FILE);
            if (!File.Exists(sqlFile)) throw new ArgumentException($@"Master sql for client file is not found. Please check : {DB_CLIENT_SQL_FILE}");
            //if the file exists, then run this file against the adapter gateway but ignore the db name.
            var content = File.ReadAllText(sqlFile);
            //We know that the file itself contains "dss_core" as the schema name. Replace that with new one.
            var exists = await _agw.Scalar(new AdapterArgs(_key) { ExcludeDBInConString = true, Query = GENERAL.SCHEMA_EXISTS }, (NAME, info.DatabaseName));
            if (exists == null || !exists.IsNumericType() || !double.TryParse(exists.ToString(),out var id) || id < 1) {
                content = content.Replace(DB_CLIENT_SEARCH_TERM, info.DatabaseName);
                //?? Should we run everything in one go or run as separate statements ???
                var result = await _agw.NonQuery(new AdapterArgs(_key) { ExcludeDBInConString = true, Query = content });
            }
            exists = await _agw.Scalar(new AdapterArgs(_key) { ExcludeDBInConString = true, Query = GENERAL.SCHEMA_EXISTS }, (NAME, info.DatabaseName));
            if (exists == null) throw new ArgumentException($@"Unable to generate the database {info.DatabaseName}");
            //We create an adapter with this Cuid and store them.
            _agw.DuplicateAdapter(_key, info.Cuid.ToString("N"), ("database",info.DatabaseName));
            
        }
    }
}
