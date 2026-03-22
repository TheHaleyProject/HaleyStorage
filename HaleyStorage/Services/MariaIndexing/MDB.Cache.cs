using Haley.Abstractions;
using Haley.Models;
using System.Collections.Concurrent;

namespace Haley.Utils {
    /// <summary>
    /// Partial class — in-memory vault object cache.
    /// All registered clients, modules, and workspaces are stored here by their CUID (compact-N)
    /// to avoid repeated DB round-trips during path resolution.
    /// </summary>
    internal partial class MariaDBIndexing {
        //We also need to cache the results to avoid frequent calls to the DB.
        ConcurrentDictionary<string, IVaultObject> _cache = new ConcurrentDictionary<string, IVaultObject>();

        /// <summary>
        /// Adds or replaces an <see cref="IVaultObject"/> in the cache keyed by its CUID.
        /// Returns <c>false</c> when the entry already exists and <paramref name="replace"/> is <c>false</c>.
        /// </summary>
        public bool TryAddInfo(IVaultObject dirInfo, bool replace = false) {
            if (dirInfo == null || !dirInfo.Name.AssertValue(false) || dirInfo.Cuid == Guid.Empty) return false;
            var key = dirInfo.Cuid.ToString("N");
            if (_cache.ContainsKey(key)) {
                if (!replace) return false;
                return _cache.TryUpdate(key, dirInfo, _cache[key]);
            } else {
                return _cache.TryAdd(key, dirInfo);
            }
        }
        /// <summary>Returns all cached objects of type <typeparamref name="T"/>.</summary>
        public IEnumerable<T> GetAllComponents<T>() where T : IVaultObject {
            return _cache.Values.OfType<T>();
        }

        /// <summary>
        /// Retrieves a strongly-typed <see cref="IVaultObject"/> from the cache by CUID.
        /// Returns <c>false</c> when not found or when the cached object is not of type <typeparamref name="T"/>.
        /// </summary>
        public bool TryGetComponentInfo<T>(string key, out T component) where T : IVaultObject {
            component = default;
            if (string.IsNullOrWhiteSpace(key) || !_cache.ContainsKey(key)) return false;
            var data = _cache[key];
            if (data == null || !(data is T)) return false;
            component = (T)data;
            return true;
        }
        /// <summary>
        /// Executes <paramref name="query"/> as a scalar, parses the returned ID into <paramref name="info"/>,
        /// runs an optional <paramref name="preProcess"/> action (e.g. <c>CreateModuleDBInstance</c>),
        /// and adds the object to the cache. Returns a success <see cref="IFeedback"/> with the ID as <c>Result</c>.
        /// </summary>
        async Task<IFeedback> ValidateAndCache(string query, string title, IVaultObject info, Func<IVaultObject, Task> preProcess, params (string key, object value)[] parameters) {
            var id = await _agw.ScalarAsync<long?>(_key, query, default, Array.ConvertAll(parameters, p => (DbArg)p));
            if (id.HasValue) {
                info.Id = id.Value;
                await AddComponentCache(info, preProcess);
                return new Feedback(true, $@"{title} - {info.Name} Indexed.") { Result = id.Value };
            }
            return new Feedback(false, "Unable to index");
        }
        /// <summary>
        /// Adds an <see cref="IVaultObject"/> to the cache after running the optional pre-process action.
        /// No-op if the CUID already has a non-null cache entry, preventing duplicate DB instance creation.
        /// </summary>
        async Task AddComponentCache(IVaultObject info, Func<IVaultObject,Task> preProcess = null) {
            if (info == null) return;
            var key = info.Cuid.ToString("N");
            if (_cache.ContainsKey(key) && _cache[key] != null) return;

            if (preProcess != null) {
                await preProcess(info);
            }

            if (_cache.ContainsKey(key)) {
                _cache.TryUpdate(key, info, null); //Gives the schema name
            } else {
                _cache.TryAdd(key, info);
            }
        }
    }
}
