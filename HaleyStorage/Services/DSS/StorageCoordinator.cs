using Haley.Abstractions;
using Haley.Models;
using Haley.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Runtime.InteropServices;
using Haley.Enums;

namespace Haley.Services {
    public partial class StorageCoordinator : IStorageCoordinator {

        bool _isInitialized = false;
        ILogger _logger;
        const string METAFILE = ".dss.meta";
        const string CLIENTMETAFILE = ".client" + METAFILE;
        const string MODULEMETAFILE = ".module" + METAFILE;
        const string WORKSPACEMETAFILE = ".ws" + METAFILE;
        const string DEFAULTPWD = "admin";
        List<string> AllowedExtensions = new List<string>();
        List<string> RestrictedExtensions = new List<string>();
        List<string> AllowedMimeTypes = new List<string>();
        List<string> RestrictedMimeTypes = new List<string>();

        public IVaultRegistryConfig Config { get; set; } = new StorageRegistryConfig();
        public StorageCoordinator(bool write_mode = true, ILogger logger = null, bool throwExceptions = false) : this(null, null, write_mode,logger,throwExceptions) {
        }
        public StorageCoordinator(string basePath, bool write_mode = true, ILogger logger = null, bool throwExceptions = false) : this(basePath, write_mode, null, throwExceptions, logger) { }
        public StorageCoordinator(IAdapterGateway agw, string adapter_key, bool write_mode = true, ILogger logger = null, bool throwExceptions = false) : this(null, write_mode, new MariaDBIndexing(agw, adapter_key, logger,throwExceptions), throwExceptions, logger) { }
        public StorageCoordinator(IAdapterGateway agw, string adapter_key, string basePath, bool write_mode = true, ILogger logger = null,bool throwExceptions = false) : this(basePath, write_mode, new MariaDBIndexing(agw, adapter_key, logger,throwExceptions) { }, throwExceptions, logger) { }
        public StorageCoordinator(string basePath, bool write_mode, IVaultIndexing indexer, bool throwExceptions, ILogger logger =null) {
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
            var defObj = new VaultProfile(VaultConstants.DEFAULT_NAME);
            await RegisterClient(defObj); //Registers defaul client, with default module and default workspace
            _isInitialized = true;
        }

        public static IStorageCoordinator Create(IAdapterGateway agw, string adapter_key, out (string logpath,string respMode) data, bool throwExceptions = false) {
            data = (null, null);
            var cfgRoot = ResourceUtils.GenerateConfigurationRoot();
            var dirPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? VaultConstants.CONFIG_DIR_WIN : VaultConstants.CONFIG_DIR_LINUX;
            var dirSection = cfgRoot[$@"Seed:{VaultConstants.CONFIG_INFO}:{dirPath}"];
            var responseMode = cfgRoot[$@"Seed:{VaultConstants.CONFIG_INFO}:{VaultConstants.CONFIG_DIR_RESPONSEPATHMODE}"];
            bool writemode = false;
            bool.TryParse(ResourceUtils.FetchVariable($@"{VaultConstants.OSS_WRITEMODE}")?.Result?.ToString(), out writemode);
            var dirInfo = dirSection?.ToDictionarySplit();
            string logPath = null; // dirSection?["log"]
            string storagePath = string.Empty;
            if (dirInfo != null && dirInfo!.TryGetValue("log", out var logObj)) logPath = logObj.ToString();
            if (dirInfo != null && dirInfo!.TryGetValue("path", out var storageObj)) storagePath = storageObj.ToString();

            var dss = new StorageCoordinator(agw, adapter_key, storagePath, writemode,throwExceptions:throwExceptions);
            var ossConfig = cfgRoot.GetSection($@"Seed:{VaultConstants.OSS_CONFIG}")?.Get<StorageRegistryConfig>();
            if (ossConfig != null) dss.SetConfig(ossConfig);
            dss.RegisterFromSource().Wait();

            //FILE FORMATS HANDLING
            var allowedFormats = cfgRoot[$@"Seed:{VaultConstants.OSS_FILEFORMATS}:{VaultConstants.ALLOWED}"];
            if (!string.IsNullOrWhiteSpace(allowedFormats)) dss.AddFormatRange(allowedFormats.Split(',')?.ToList(), FormatControlMode.Extension);
            var allowedMimes = cfgRoot[$@"Seed:{VaultConstants.OSS_FILEFORMATS}:{VaultConstants.ALLOWED_MIMETYPE}"];
            if (!string.IsNullOrWhiteSpace(allowedMimes)) dss.AddFormatRange(allowedMimes.Split(',')?.ToList(), FormatControlMode.MimeType);

            var restrictedFormats = cfgRoot[$@"Seed:{VaultConstants.OSS_FILEFORMATS}:{VaultConstants.RESTRICTED}"];
            if (!string.IsNullOrWhiteSpace(restrictedFormats)) dss.AddFormatRange(restrictedFormats.Split(',')?.ToList(),FormatControlMode.Extension,true);
            var restrictedMimes = cfgRoot[$@"Seed:{VaultConstants.OSS_FILEFORMATS}:{VaultConstants.RESTRICTED_MIMETYPE}"];
            if (!string.IsNullOrWhiteSpace(restrictedMimes)) dss.AddFormatRange(restrictedMimes.Split(',')?.ToList(), FormatControlMode.MimeType,true);

            var throwEx = cfgRoot[$@"Seed:{VaultConstants.THROW_EX}"];
            if (bool.TryParse(throwEx?.ToString(), out bool tex)) dss.ThrowExceptions = tex;

            data = (logPath, responseMode);
            return dss;
        }

        public bool ThrowExceptions { get; set; }
        public string BasePath { get; }
        public bool WriteMode { get; set; }
        IVaultIndexing Indexer;

        public StorageCoordinator SetWriteMode(bool mode) {
            WriteMode = mode;
            return this;
        }
        public IStorageCoordinator SetIndexer(IVaultIndexing service) {
            Indexer = service;
            Initialize(true)?.Wait();
            return this;
        }

        public IStorageCoordinator SetConfig(IVaultRegistryConfig config) {
            Config = config ?? new StorageRegistryConfig();
            return this;
        }
    }
}
