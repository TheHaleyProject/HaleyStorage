using Haley.Abstractions;
using Haley.Models;
using System.Collections.Concurrent;

namespace Haley.Utils {
    public partial class MariaDBIndexing : IVaultIndexing {
        //We also need to cache the results to avoid frequent calls to the DB.
        ConcurrentDictionary<string, IVaultObject> _cache = new ConcurrentDictionary<string, IVaultObject>();
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
        public bool TryGetComponentInfo<T>(string key, out T component) where T : IVaultObject {
            component = default;
            if (string.IsNullOrWhiteSpace(key) || !_cache.ContainsKey(key)) return false;
            var data = _cache[key];
            if (data == null || !(data is T)) return false;
            component = (T)data;
            return true;
        }
        async Task<IFeedback> ValidateAndCache(string query, string title, IVaultObject info, Func<IVaultObject, Task> preProcess, params (string key, object value)[] parameters) {
            var result = await _agw.Scalar(new AdapterArgs(_key) { Query = query }, parameters);
            if (result != null && result.IsNumericType()) {
                if (long.TryParse(result.ToString(), out var id)) info.Id = id;
                //Every time a client is sucessfully done. We validate if it is present or not.
                await AddComponentCache(info, preProcess);
                return new Feedback(true, $@"{title} - {info.Name} Indexed.") { Result = id };
            }
            return new Feedback(false, "Unable to index");
        }
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
