using Haley.Abstractions;
using Haley.Models;
using Haley.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Runtime.InteropServices;
using Haley.Enums;

namespace Haley.Services {
    /// <summary>
    /// Orchestrates all storage operations across registered <see cref="IStorageProvider"/> instances,
    /// the <see cref="IVaultIndexing"/> indexer, and the path-resolution pipeline.
    /// Split into partial classes: CRUD, PathProcessing, Registration, Chunking,
    /// ProviderResolution, Authorization, and FileFormats.
    /// </summary>
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

        /// <summary>Registry configuration controlling path sharding depths, suffixes, etc.</summary>
        public IVaultRegistryConfig Config { get; set; } = new StorageRegistryConfig();

        /// <summary>Initializes with the default file-system provider and no indexer.</summary>
        public StorageCoordinator(bool write_mode = true, ILogger logger = null, bool throwExceptions = false) : this(null, null, write_mode,logger,throwExceptions) {
        }

        /// <summary>Initializes with an explicit storage root path and no indexer.</summary>
        /// <param name="basePath">Root directory for all file storage. Defaults to an assembly-relative DataStore folder when null.</param>
        public StorageCoordinator(string basePath, bool write_mode = true, ILogger logger = null, bool throwExceptions = false) : this(basePath, write_mode, null, throwExceptions, logger) { }

        /// <summary>Initializes with a <see cref="MariaDBIndexing"/> indexer constructed from the given adapter gateway.</summary>
        public StorageCoordinator(IAdapterGateway agw, string adapter_key, bool write_mode = true, ILogger logger = null, bool throwExceptions = false) : this(null, write_mode, new MariaDBIndexing(agw, adapter_key, logger,throwExceptions), throwExceptions, logger) { }

        /// <summary>Initializes with an explicit base path and a <see cref="MariaDBIndexing"/> indexer.</summary>
        public StorageCoordinator(IAdapterGateway agw, string adapter_key, string basePath, bool write_mode = true, ILogger logger = null,bool throwExceptions = false) : this(basePath, write_mode, new MariaDBIndexing(agw, adapter_key, logger,throwExceptions) { }, throwExceptions, logger) { }

        /// <summary>Primary constructor. Registers the default <see cref="FileSystemStorageProvider"/> and calls <see cref="SetIndexer"/>.</summary>
        /// <param name="basePath">Root storage directory; falls back to assembly-relative DataStore when null or empty.</param>
        /// <param name="write_mode">When <c>false</c>, all mutating operations are rejected.</param>
        /// <param name="indexer">Optional DB-backed indexer. Pass <c>null</c> to run in file-system-only mode.</param>
        /// <param name="throwExceptions">When <c>true</c>, exceptions propagate instead of being swallowed as error messages.</param>
        internal StorageCoordinator(string basePath, bool write_mode, IVaultIndexing indexer, bool throwExceptions, ILogger logger =null) {
            BasePath = basePath?.Trim();
            WriteMode = write_mode;
            ThrowExceptions = throwExceptions;
            //This is supposedly the directory where all storage goes into.
            if (string.IsNullOrWhiteSpace(BasePath)) {
                BasePath = AssemblyUtils.GetBaseDirectory(parentFolder: "DataStore");
            }
            //BasePath = BasePath?.ToLower(); //In Linux, we might end up having case sensitivity issue.
            RegisterProvider(new FileSystemStorageProvider(), setAsDefault: true);
            SetIndexer(indexer);
            _logger = logger;

            //If a client is not registered, do we need to register the default client?? and a default module??
        }
        async Task Initialize(bool force = false) {
            if (_isInitialized && !force) return;
            // In ReadOnly mode, skip creating default structures.
            // Path resolution falls back to existing .meta files and the indexer.
            if (WriteMode) {
                var defObj = new VaultProfile(VaultConstants.DEFAULT_NAME);
                await RegisterClient(defObj); //Registers default client, module and workspace
            }
            _isInitialized = true;
        }

        /// <summary>
        /// Factory that creates a fully-configured <see cref="StorageCoordinator"/> from the
        /// application's <c>Seed</c> configuration section. Reads the storage root path,
        /// write-mode flag, file-format allow/deny lists, and OSS registry config.
        /// </summary>
        /// <param name="data">
        /// Outputs the configured log-file path and response-path mode string read from <c>Seed</c> config.
        /// </param>
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

        /// <summary>When <c>true</c>, exceptions propagate to callers; when <c>false</c> they are captured as error messages.</summary>
        public bool ThrowExceptions { get; set; }
        /// <summary>Root directory under which all storage paths are resolved for the FileSystem provider.</summary>
        public string BasePath { get; }
        /// <summary>When <c>false</c>, all mutating operations (upload, delete, register) are rejected.</summary>
        public bool WriteMode { get; set; }
        IVaultIndexing Indexer;

        // --- IStorageProviderRegistry ---
        readonly Dictionary<string, IStorageProvider> _providers = new Dictionary<string, IStorageProvider>(StringComparer.OrdinalIgnoreCase);
        string _defaultProviderKey;

        /// <summary>
        /// Registers an <see cref="IStorageProvider"/> under its <see cref="IStorageProvider.Key"/>.
        /// The first registered provider (or any registered with <paramref name="setAsDefault"/>) becomes the default.
        /// </summary>
        public IStorageProviderRegistry RegisterProvider(IStorageProvider provider, bool setAsDefault = false) {
            if (provider == null || string.IsNullOrWhiteSpace(provider.Key)) return this;
            _providers[provider.Key] = provider;
            if (setAsDefault || _defaultProviderKey == null) _defaultProviderKey = provider.Key;
            return this;
        }

        /// <summary>Looks up a registered provider by key. Returns <c>false</c> if not found.</summary>
        public bool TryGetProvider(string key, out IStorageProvider provider) {
            return _providers.TryGetValue(key, out provider);
        }

        /// <summary>Returns the default <see cref="IStorageProvider"/>, or <c>null</c> if none is registered.</summary>
        public IStorageProvider GetDefaultProvider() {
            if (_defaultProviderKey != null && _providers.TryGetValue(_defaultProviderKey, out var p)) return p;
            return null;
        }

        /// <summary>Fluent write-mode toggle.</summary>
        public StorageCoordinator SetWriteMode(bool mode) {
            WriteMode = mode;
            return this;
        }

        /// <summary>
        /// Replaces the active indexer and re-runs initialization so default client/module/workspace
        /// structures are created against the new indexer.
        /// </summary>
        private void SetIndexer(IVaultIndexing service) {
            Indexer = service;
            Initialize(true)?.Wait();
        }

        /// <summary>Replaces the registry configuration. Passing <c>null</c> resets to the default <see cref="StorageRegistryConfig"/>.</summary>
        public IStorageCoordinator SetConfig(IVaultRegistryConfig config) {
            Config = config ?? new StorageRegistryConfig();
            return this;
        }
    }
}
