using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Xml;

namespace Haley.Services {
    /// <summary>
    /// Partial class — vault hierarchy registration (client, module, workspace) and seed-config loading.
    /// On the FileSystem provider, each registration creates a physical directory and writes a
    /// <c>.dss.meta</c> file; with the MariaDB indexer the record is also persisted in the database.
    /// </summary>
    public partial class StorageCoordinator : IStorageCoordinator {
        List<(string client, string module)> _caseSensitivePairs = new List<(string client, string module)>();

        /// <summary>Convenience overload — registers a client by name with an optional password.</summary>
        public Task<IFeedback> RegisterClient(string client_name, string password = null) {
            return RegisterClient(new VaultProfile(client_name) { });
        }
        /// <summary>Convenience overload — registers a module by name under the given client.</summary>
        public Task<IFeedback> RegisterModule(string module_name=null, string client_name = null) {
            return RegisterModule(new VaultProfile(module_name), new VaultProfile(client_name));
        }
        /// <summary>Convenience overload — registers a workspace by name under the given client and module.</summary>
        public Task<IFeedback> RegisterWorkSpace(string workspace_name=null, string client_name = null, string module_name = null, VaultControlMode content_control = VaultControlMode.Number, VaultParseMode content_pmode = VaultParseMode.Generate, bool is_virtual = false) {
            return RegisterWorkSpace(new VaultProfile(workspace_name, VaultControlMode.Guid, VaultParseMode.Generate, isVirtual:is_virtual), new VaultProfile(client_name), new VaultProfile(module_name), content_control, content_pmode);
        }

        /// <summary>
        /// Registers a client: creates the physical directory (FS), writes <c>.client.dss.meta</c>,
        /// persists the record in the indexer, and registers a default module under it.
        /// </summary>
        /// <param name="client">Client info; <see cref="VaultProfile.ControlMode"/> is forced to <c>Guid</c>.</param>
        /// <param name="password">Plaintext password; defaults to <c>"admin"</c> when null.</param>
        public async Task<IFeedback> RegisterClient(IVaultInfo client, string password = null) {
            var result = new Feedback();
            if (client == null) return new Feedback(false, "Name cannot be empty");
            if (!client.TryValidate(out var msg)) return new Feedback(false, msg);
            if (client is VaultProfile cp) cp.ControlMode = VaultControlMode.Guid;
            if (string.IsNullOrWhiteSpace(password)) password = DEFAULTPWD;
            var cInput = GenerateBasePath(client, Enums.VaultObjectType.Client);
            var path = Path.Combine(BasePath, cInput.path);

            bool isFs = GetDefaultProvider() is FileSystemStorageProvider;

            if (isFs) {
                if (!Directory.Exists(path) && WriteMode)
                    Directory.CreateDirectory(path);
                if (!Directory.Exists(path))
                    result.SetStatus(false).SetMessage("Directory was not created. Check if WriteMode is ON or ensure proper access is available.");
            }

            var signing = RandomUtils.GetString(512);
            var encrypt = RandomUtils.GetString(512);
            var pwdHash = HashUtils.ComputeHash(password, HashMethod.Sha256);

            var clientInfo = client.MapProperties(new VaultClient(pwdHash, signing, encrypt, client.DisplayName) { Path = cInput.path });
            if (WriteMode && isFs) {
                var metaFile = Path.Combine(path, CLIENTMETAFILE);
                File.WriteAllText(metaFile, clientInfo.ToJson());
            }

            if (isFs && !Directory.Exists(path)) return result; // false status already set above
            result.SetStatus(true).SetMessage($@"Client {client.DisplayName} is registered");

            if (Indexer == null || !WriteMode) return result;
            var idxResult = await Indexer.RegisterClient(clientInfo);
            result.Result = idxResult.Result;

            await RegisterModule(new VaultProfile(null), client);
            return result;
        }

        /// <summary>
        /// Registers a module under an existing client: creates the physical directory (FS),
        /// writes <c>.module.dss.meta</c>, persists in the indexer, and registers a default workspace.
        /// </summary>
        public async Task<IFeedback> RegisterModule(IVaultInfo module, IVaultInfo client) {
            string msg = string.Empty;
            if (!module.TryValidate(out msg)) new Feedback(false, msg);
            if (!client.TryValidate(out msg)) new Feedback(false, msg);

            bool isFs = GetDefaultProvider() is FileSystemStorageProvider;

            var client_path = GenerateBasePath(client, Enums.VaultObjectType.Client).path;
            var bPath = Path.Combine(BasePath, client_path);
            if (isFs && !Directory.Exists(bPath)) return new Feedback(false, $@"Directory not found for the client {client.DisplayName}");
            if (client_path.Contains("..")) return new Feedback(false, "Client Path contains invalid characters");

            var modPath = GenerateBasePath(module, Enums.VaultObjectType.Module).path;
            bPath = Path.Combine(bPath, modPath);

            if (isFs) {
                if (!Directory.Exists(bPath) && WriteMode)
                    Directory.CreateDirectory(bPath);
            }

            var moduleInfo = module.MapProperties(new StorageModule(client.Name, module.DisplayName) { Path = modPath });
            if (WriteMode && isFs) {
                var metaFile = Path.Combine(bPath, MODULEMETAFILE);
                File.WriteAllText(metaFile, moduleInfo.ToJson());
            }

            var result = new Feedback(true, $@"Module {module.DisplayName} is registered");
            if (isFs && !Directory.Exists(bPath))
                result.SetStatus(false).SetMessage("Directory is not created. Please ensure if the WriteMode is turned ON or proper access is available.");

            if (Indexer == null || !WriteMode || !result.Status) return result;
            var idxResult = await Indexer.RegisterModule(moduleInfo);
            result.Result = idxResult.Result;

            await RegisterWorkSpace(new VaultProfile(null, VaultControlMode.Guid, VaultParseMode.Generate, isVirtual: true), client, module);
            return result;
        }

        /// <summary>
        /// Registers a workspace under an existing client+module: creates the physical directory (FS,
        /// unless virtual), writes <c>.ws.dss.meta</c>, and persists in the indexer.
        /// </summary>
        /// <param name="content_control">Whether file identifiers are auto-increment numbers (<c>Number</c>) or compact-N GUIDs (<c>Guid</c>).</param>
        /// <param name="content_pmode">Whether file names are parsed from caller input or auto-generated by the indexer.</param>
        public async Task<IFeedback> RegisterWorkSpace(IVaultInfo wspace, IVaultInfo client, IVaultInfo module, VaultControlMode content_control = VaultControlMode.Number, VaultParseMode content_pmode = VaultParseMode.Generate) {
            string msg = string.Empty;
            if (!wspace.TryValidate(out msg)) throw new Exception(msg);
            if (!client.TryValidate(out msg)) throw new Exception(msg);
            if (!module.TryValidate(out msg)) throw new Exception(msg);
            module.UpdateCUID(client.Name, module.Name);

            bool isFs = GetDefaultProvider() is FileSystemStorageProvider;

            var cliPath = GenerateBasePath(client, Enums.VaultObjectType.Client).path;
            var modPath = GenerateBasePath(module, Enums.VaultObjectType.Module).path;

            var path = Path.Combine(BasePath, cliPath, modPath);
            if (isFs && !Directory.Exists(path)) return new Feedback(false, $@"Unable to locate the base path for Client: {client.DisplayName}, Module: {module.DisplayName}");
            if (path.Contains("..")) return new Feedback(false, "Invalid characters found in the base path.");

            string wsPath = string.Empty;
            if (!(wspace is VaultProfile wp && wp.IsVirtual)) {
                wsPath = GenerateBasePath(wspace, Enums.VaultObjectType.WorkSpace).path;
                path = Path.Combine(path, wsPath);

                if (isFs && !Directory.Exists(path) && WriteMode)
                    Directory.CreateDirectory(path);
            }

            var wsInfo = wspace.MapProperties(new StorageWorkspace(client.Name, module.Name, wspace.DisplayName) { Path = wsPath, ContentControl = content_control, ContentParse = content_pmode });
            if (WriteMode && isFs) {
                var metaFile = Path.Combine(path, WORKSPACEMETAFILE);
                File.WriteAllText(metaFile, wsInfo.ToJson());
            }

            var result = new Feedback(true, $@"Workspace {wspace.DisplayName} is registered");
            if (isFs && !Directory.Exists(path))
                result.SetStatus(false).SetMessage("Directory is not created. Please ensure if the WriteMode is turned ON or proper access is available.");

            if (Indexer == null || !WriteMode || !result.Status) return result;
            var idxResult = await Indexer.RegisterWorkspace(wsInfo);
            result.Result = idxResult.Result;
            return result;
        }
       
        /// <summary>
        /// Reads the <c>Seed:sources</c> configuration section (or the provided <paramref name="section"/>)
        /// and registers all clients, modules, and workspaces declared there.
        /// Deduplicates registrations within a single call. Also populates the case-sensitive client/module pairs list.
        /// </summary>
        /// <param name="section">Optional override section; reads from app config when null.</param>
        public async Task<IFeedback> RegisterFromSource(IConfigurationSection section =null) {
            try {
                var result = new Feedback();
                if (section == null) {
                    section = ResourceUtils.GenerateConfigurationRoot()?.GetSection($@"Seed:{VaultConstants.CONFIG_SOURCE}");
                    if (section == null) return result.SetMessage("Cannot proceed with empty configuration");
                }
                var sources = section.AsDictionaryList();
                var sourceList = sources
                    .Where(p => p.Count > 0 && p.First().Value is Dictionary<string, object>)
                    .Select(q => ((Dictionary<string, object>)q.First().Value).Map<DSSRegInfo>())
                    .ToList();
                if (sourceList == null || sourceList.Count < 0) return result.SetMessage("Unable to parse registration info from the given configuration section.");

                //First get all the case sensitive clients.
                 _caseSensitivePairs = sourceList.Where(p => p.CaseSensitive).Select(p => (p.Client.ToDBName(), p.Module.ToDBName())).Distinct().ToList(); //We refer and make sure, these clients are always respected of their case sensitivity.

                var clients = new List<string>();
                var modules = new List<string>();
                var wspaces = new List<string>();

                foreach (var source in sourceList) {
                    //Register client
                    if (string.IsNullOrWhiteSpace(source.Client)) continue; //Client is mandatory
                    var cliKey = source.Client.ToDBName();
                    if (!clients.Contains(cliKey)) {
                        if (!(await RegisterClient(source.Client, source.Password)).Status) continue;
                        clients.Add(cliKey);
                    }

                    //Register Module
                    if (string.IsNullOrWhiteSpace(source.Module)) continue; //Module is mandatory
                    var modKey = $"{cliKey}_{source.Module.ToDBName()}";
                    if (!modules.Contains(modKey)) {
                        if (!(await RegisterModule(source.Module, source.Client)).Status) continue;
                        modules.Add(modKey);
                    }

                    //Register Workspace
                    if (string.IsNullOrWhiteSpace(source.Workspace)) continue; //Workspace is mandatory
                    var wsKey = $"{modKey}_{source.Workspace.ToDBName()}";
                    if (!wspaces.Contains(wsKey)) {
                        if (!(await RegisterWorkSpace(source.Workspace, source.Client, source.Module, source.Control, source.Parse, source.IsVirtual)).Status) continue;
                        wspaces.Add(wsKey);
                    }
                }
                return result.SetStatus(true).SetMessage("Successfully registered.");
            } catch (Exception ex) {
                return new Feedback().SetMessage(ex.StackTrace);
            }
        }
    }
}
