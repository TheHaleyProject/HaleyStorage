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
    public partial class MariaDBIndexing : IStorageIndexing {
        //We also need to cache the results to avoid frequent calls to the DB.
        ConcurrentDictionary<string, IStorageDirectory> _cache = new ConcurrentDictionary<string, IStorageDirectory>();
        public bool TryAddInfo(IStorageDirectory dirInfo, bool replace = false) {
            if (dirInfo == null || !dirInfo.Name.AssertValue(false) || !dirInfo.Cuid.AssertValue(false)) return false;
            if (_cache.ContainsKey(dirInfo.Cuid)) {
                if (!replace) return false;
                return _cache.TryUpdate(dirInfo.Cuid, dirInfo, _cache[dirInfo.Cuid]);
            } else {
                return _cache.TryAdd(dirInfo.Cuid, dirInfo);
            }
        }
        public bool TryGetComponentInfo<T>(string key, out T component) where T : IStorageDirectory {
            component = default;
            if (string.IsNullOrWhiteSpace(key) || !_cache.ContainsKey(key)) return false;
            var data = _cache[key];
            if (data == null || !(data is T)) return false;
            component = (T)data;
            return true;
        }
        async Task<IFeedback> ValidateAndCache(string query, string title, IStorageDirectory info, Func<IStorageDirectory, Task> preProcess, params (string key, object value)[] parameters) {
            var result = await _agw.Scalar(new AdapterArgs(_key) { Query = query }, parameters);
            if (result != null && result.IsNumericType()) {
                if (long.TryParse(result.ToString(), out var id)) info.ForceSetId(id);
                //Every time a client is sucessfully done. We validate if it is present or not.
                await AddComponentCache(info, preProcess);
                return new Feedback(true, $@"{title} - {info.Name} Indexed.") { Result = id };
            }
            return new Feedback(false, "Unable to index");
        }
        async Task AddComponentCache(IStorageDirectory info, Func<IStorageDirectory,Task> preProcess = null) {
            if (info == null) return;
            if (_cache.ContainsKey(info.Cuid) && _cache[info.Cuid] != null) return; 
            
            if (preProcess != null) {
                await preProcess(info);
            }   

            if (_cache.ContainsKey(info.Cuid)) {
                _cache.TryUpdate(info.Cuid, info, null); //Gives the schema name
            } else {
                _cache.TryAdd(info.Cuid, info);
            }
        }
    }
}
