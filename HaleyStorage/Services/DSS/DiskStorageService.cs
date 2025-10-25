using Haley.Abstractions;
using Haley.Models;
using Haley.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Runtime.InteropServices;

namespace Haley.Services {
    public partial class DiskStorageService : IDiskStorageService {

        bool _isInitialized = false;
        ILogger _logger;
        const string METAFILE = ".dss.meta";
        const string CLIENTMETAFILE = ".client" + METAFILE;
        const string MODULEMETAFILE = ".module" + METAFILE;
        const string WORKSPACEMETAFILE = ".ws" + METAFILE;
        const string DEFAULTPWD = "admin";
        List<string> AllowedFormats = new List<string>();
        List<string> RestrictedFormats = new List<string>();

        public IDSSConfig Config { get; set; } = new DSSConfig();
        public DiskStorageService(bool write_mode = true, ILogger logger = null, bool throwExceptions = false) : this(null, null, write_mode,logger,throwExceptions) {
        }
        public DiskStorageService(string basePath, bool write_mode = true, ILogger logger = null, bool throwExceptions = false) : this(basePath, write_mode, null, throwExceptions, logger) { }
        public DiskStorageService(IAdapterGateway agw, string adapter_key, bool write_mode = true, ILogger logger = null, bool throwExceptions = false) : this(null, write_mode, new MariaDBIndexing(agw, adapter_key, logger,throwExceptions), throwExceptions, logger) { }
        public DiskStorageService(IAdapterGateway agw, string adapter_key, string basePath, bool write_mode = true, ILogger logger = null,bool throwExceptions = false) : this(basePath, write_mode, new MariaDBIndexing(agw, adapter_key, logger,throwExceptions) { }, throwExceptions, logger) { }
        public DiskStorageService(string basePath, bool write_mode, IDSSIndexing indexer, bool throwExceptions, ILogger logger =null) {
            BasePath = basePath?.Trim();
            WriteMode = write_mode;
            ThrowExceptions = throwExceptions;
            //This is supposedly the directory where all storage goes into.
            if (string.IsNullOrWhiteSpace(BasePath)) {
                BasePath = AssemblyUtils.GetBaseDirectory(parentFolder: "DataStore");
            }
            //BasePath = BasePath?.ToLower(); //In Linux, we might end up having case sensitivity issue.
            SetIndexer(indexer);
            _logger = logger;

            //If a client is not registered, do we need to register the default client?? and a default module??
        }
        async Task Initialize(bool force = false) {
            if (_isInitialized && !force) return;
            var defObj = new OSSControlled(OSSConstants.DEFAULT_NAME);
            await RegisterClient(defObj); //Registers defaul client, with default module and default workspace
            _isInitialized = true;
        }

        public static IDiskStorageService Create(IAdapterGateway agw, string adapter_key, out (string logpath,string respMode) data, bool throwExceptions = false) {
            data = (null, null);
            var cfgRoot = ResourceUtils.GenerateConfigurationRoot();
            var dirPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? OSSConstants.CONFIG_DIR_WIN : OSSConstants.CONFIG_DIR_LINUX;
            var dirSection = cfgRoot[$@"Seed:{OSSConstants.CONFIG_INFO}:{dirPath}"];
            var responseMode = cfgRoot[$@"Seed:{OSSConstants.CONFIG_INFO}:{OSSConstants.CONFIG_DIR_RESPONSEPATHMODE}"];
            bool writemode = false;
            bool.TryParse(ResourceUtils.FetchVariable($@"{OSSConstants.OSS_WRITEMODE}")?.Result?.ToString(), out writemode);
            var dirInfo = dirSection?.ToDictionarySplit();
            string logPath = null; // dirSection?["log"]
            string storagePath = string.Empty;
            if (dirInfo != null && dirInfo!.TryGetValue("log", out var logObj)) logPath = logObj.ToString();
            if (dirInfo != null && dirInfo!.TryGetValue("path", out var storageObj)) storagePath = storageObj.ToString();

            var dss = new DiskStorageService(agw, adapter_key, storagePath, writemode,throwExceptions:throwExceptions);
            var ossConfig = cfgRoot.GetSection($@"Seed:{OSSConstants.OSS_CONFIG}")?.Get<DSSConfig>();
            if (ossConfig != null) dss.SetConfig(ossConfig);
            dss.RegisterFromSource().Wait();

            var allowedFormats = cfgRoot[$@"Seed:{OSSConstants.OSS_FILEFORMATS}:{OSSConstants.Allowed}"];
            if (!string.IsNullOrWhiteSpace(allowedFormats)) {
                dss.AddAllowedFormatRange(allowedFormats.Split(',')?.ToList());
            }
            var restrictedFormats = cfgRoot[$@"Seed:{OSSConstants.OSS_FILEFORMATS}:{OSSConstants.Restricted}"];
            if (!string.IsNullOrWhiteSpace(restrictedFormats)) {
                dss.AddRestrictedFormatRange(restrictedFormats.Split(',')?.ToList());
            }
            data = (logPath, responseMode);
            return dss;
        }

        public bool ThrowExceptions { get; set; }
        public string BasePath { get; }
        public bool WriteMode { get; set; }
        IDSSIndexing Indexer;

        public DiskStorageService SetWriteMode(bool mode) {
            WriteMode = mode;
            return this;
        }
        public IDiskStorageService SetIndexer(IDSSIndexing service) {
            Indexer = service;
            Initialize(true)?.Wait();
            return this;
        }

        public IDiskStorageService SetConfig(IDSSConfig config) {
            Config = config ?? new DSSConfig();
            return this;
        }
    }
}
