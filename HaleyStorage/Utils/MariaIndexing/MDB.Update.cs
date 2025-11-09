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
    public partial class MariaDBIndexing : IDSSIndexing {
        public async Task<IFeedback> UpdateDocVersionInfo(string moduleCuid, IOSSFileRoute file, string callId = null) {
            Feedback result = new Feedback();
            try {
                if (string.IsNullOrWhiteSpace(moduleCuid)) return result.SetMessage($@"Module CUID is mandatory to update document info");
                if (!_agw.ContainsKey(moduleCuid)) return result.SetMessage($@"No adapter found for the key {moduleCuid}");
                ITransactionHandler handler = GetTransactionHandlerCache(callId, moduleCuid);

                if (file == null || string.IsNullOrWhiteSpace(file.Cuid)) return result.SetMessage("No file info. Nothing to update");
                //var docvExists = _agw.Scalar(new AdapterArgs(moduleCuid) { Query = INSTANCE.DOCVERSION.EXISTS_BY_CUID }, (CUID, file.Cuid));
                var docvExists = await _agw.Scalar(new AdapterArgs(moduleCuid) { Query = INSTANCE.DOCVERSION.EXISTS_BY_ID }.ForTransaction(handler), (ID, file.Id));
                if (docvExists == null) return result.SetMessage($@"Unable to find any document version with the cuid {file.Cuid} and id {file.Id}in the database {moduleCuid}");
                //If File exists, then we go ahead and update the info.
                await _agw.NonQuery(new AdapterArgs(moduleCuid) { Query = INSTANCE.DOCVERSION.INSERT_INFO }.ForTransaction(handler), (ID, file.Id), (SAVENAME, file.SaveAsName), (PATH, file.Path), (SIZE, file.Size));
                var updatedInfo = await _agw.Read(new AdapterArgs(moduleCuid) { Query = INSTANCE.DOCVERSION.GET_INFO, Filter = ResultFilter.FirstDictionary }.ForTransaction(handler), (ID, file.Id));
                if (updatedInfo == null || !(updatedInfo is Dictionary<string, object> dic) || dic.Count < 1) return result.SetMessage("Unable to confirm if the document version info is properly updated or not.");
                return result.SetStatus(true).SetMessage("Updated document info").SetResult(dic.ToJson());
            } catch (Exception ex) {
                return result.SetMessage(ex.StackTrace);
            }
        }
    }
}
