using Haley.Abstractions;
using Haley.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Haley.Utils {
    public partial class MariaDBIndexing : IVaultIndexing {
        const string DB_CORE_SQL_FILE = "dsscore.sql";
        const string DB_CLIENT_SQL_FILE = "dssclient.sql";
        const string DB_CORE_FALLBACK_NAME = "mss_core";
        const string DB_CORE_SEARCH_TERM = "dss_core";
        const string DB_CLIENT_SEARCH_TERM = "dss_client";
        const string DB_SQL_FILE_LOCATION = "Resources";
        public const string DB_MODULE_NAME_PREFIX = "dssm_";
        ConcurrentDictionary<string, (ITransactionHandler handler, DateTime created)> _handlers = new ConcurrentDictionary<string, (ITransactionHandler handler, DateTime created)>();
        ILogger _logger;
        string _key;
        IAdapterGateway _agw;
        bool isValidated = false;
        SemaphoreSlim _validateLock = new SemaphoreSlim(1, 1);
        static readonly TimeSpan _handlerMaxAge = TimeSpan.FromMinutes(30);
        public bool ThrowExceptions { get; private set; }
        public MariaDBIndexing(IAdapterGateway agw, string key, ILogger logger) : this(agw, key, logger, false) { }
        public MariaDBIndexing(IAdapterGateway agw, string key, ILogger logger, bool throwExceptions) {
            _key = key;
            _agw = agw;
            _logger = logger;
            ThrowExceptions = throwExceptions;
        }
        public IFeedback FinalizeTransaction(string callId, bool commit = true) {
            Feedback result = new Feedback();
            List<string> toremove = new List<string>();
            try {
                //All handlers are stored in below format : callId###dbid
                //because one call can be using multiple db as well.
                if (string.IsNullOrWhiteSpace(callId)) return result.SetMessage("callID cannot be empty for this operation");
                var keyPrefix = callId + "###";

                foreach (var key in _handlers.Keys.Where(p => p.StartsWith(keyPrefix))) {
                    if (commit) {
                        _handlers[key].handler?.Commit();
                    } else {
                        _handlers[key].handler?.Rollback();
                    }
                    toremove.Add(key);
                }

                CleanupStaleHandlers(); // prune any leaked handlers while we're here
                result.SetStatus(true).SetMessage(commit ? "Commited Successfully" : "Rolled back successfully");
                return result;
            } catch (Exception ex) {
                _logger?.LogError(ex.StackTrace);
                return result.SetStatus(false).SetMessage(ex.StackTrace);
            } finally {
                foreach (var key in toremove) {
                    if (_handlers.ContainsKey(key)) _handlers.Remove(key, out _);
                }
            }
        }
        void CleanupStaleHandlers() {
            var staleKeys = _handlers
                .Where(kvp => DateTime.UtcNow - kvp.Value.created > _handlerMaxAge)
                .Select(kvp => kvp.Key)
                .ToList();
            foreach (var key in staleKeys) {
                if (_handlers.TryRemove(key, out var entry))
                    entry.handler?.Rollback();
            }
        }
    }
}
