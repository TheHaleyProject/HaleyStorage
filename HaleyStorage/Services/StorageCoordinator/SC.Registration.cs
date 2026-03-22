using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.Xml;

namespace Haley.Services {
    /// <summary>
    /// Partial class — vault hierarchy registration (client, module, workspace) and seed-config loading.
    /// Clients and modules are DB-only hierarchy nodes — they have no physical directory or path.
    /// Only workspaces have a physical directory: <c>BasePath / workspace-sharded-path</c>.
    /// </summary>
    public partial class StorageCoordinator : IStorageCoordinator {
        /// <summary>Convenience overload — registers a client by name with an optional password.</summary>
        public Task<IFeedback> RegisterClient(string client_name, string password = null, bool addDefaultModule = false, string providerKey = null, bool caseSensitive = false) {
            return RegisterClient(new VaultObject(client_name), password, addDefaultModule, providerKey, caseSensitive);
        }
        /// <summary>Convenience overload — registers a module by name under the given client.</summary>
        public Task<IFeedback> RegisterModule(string module_name = null, string client_name = null, string providerKey = null, bool caseSensitive = false) {
            return RegisterModule(new VaultObject(module_name), new VaultObject(client_name), providerKey, caseSensitive);
        }
        /// <summary>Convenience overload — registers a workspace by name under the given client and module.</summary>
        public Task<IFeedback> RegisterWorkSpace(string workspace_name = null, string client_name = null, string module_name = null, VaultNameMode content_control = VaultNameMode.Number, VaultNameParseMode content_pmode = VaultNameParseMode.Generate, bool? is_virtual = null, string providerKey = null, bool caseSensitive = false) {
            return RegisterWorkSpace(new VaultObject(workspace_name), new VaultObject(client_name), new VaultObject(module_name), content_control, content_pmode, is_virtual, providerKey, caseSensitive);
        }

        /// <summary>
        /// Registers a client in the indexer (DB-only — no physical directory is created).
        /// Clients are purely a hierarchy node; all physical storage is owned by workspaces.
        /// </summary>
        /// <param name="password">Plaintext password; defaults to <c>"admin"</c> when null.</param>
        public async Task<IFeedback> RegisterClient(IVaultObject client, string password = null, bool addDefaultModule = false, string providerKey = null, bool caseSensitive = false) {
            if (client == null) return new Feedback(false, "Name cannot be empty");
            if (!client.TryValidate(out var msg)) return new Feedback(false, msg);
            if (string.IsNullOrWhiteSpace(password)) password = DEFAULTPWD;

            var signing = RandomUtils.GetString(512);
            var encrypt = RandomUtils.GetString(512);
            var pwdHash = HashUtils.ComputeHash(password, HashMethod.Sha256);
            var clientInfo = client.MapProperties(new VaultClient(pwdHash, signing, encrypt, client.DisplayName));

            var result = new Feedback(true, $"Client {client.DisplayName} is registered");
            if (Indexer == null || !WriteMode) return result;
            var idxResult = await Indexer.RegisterClient(clientInfo);
            result.Result = idxResult.Result;

            if (addDefaultModule) await RegisterModule(client_name: client.DisplayName, providerKey: providerKey, caseSensitive: caseSensitive);

            return result;
        }

        /// <summary>
        /// Registers a module under an existing client (DB-only — no physical directory is created).
        /// Also auto-registers a virtual default workspace under this module.
        /// </summary>
        public async Task<IFeedback> RegisterModule(IVaultObject module, IVaultObject client, string providerKey = null, bool caseSensitive = false) {
            string msg = string.Empty;
            if (!module.TryValidate(out msg)) return new Feedback(false, msg);
            if (!client.TryValidate(out msg)) return new Feedback(false, msg);

            var moduleInfo = module.MapProperties(new VaultModule(client.Name, module.DisplayName));
            var result = new Feedback(true, $"Module {module.DisplayName} is registered");
            if (Indexer == null || !WriteMode) return result;
            var idxResult = await Indexer.RegisterModule(moduleInfo);
            result.Result = idxResult.Result;

            await RegisterWorkSpace(new VaultObject(null), client, module, VaultNameMode.Guid, VaultNameParseMode.Generate, providerKey: providerKey, caseSensitive: caseSensitive);
            return result;
        }

        /// <summary>
        /// Registers a workspace under an existing client+module.
        /// For physical workspaces (non-virtual), creates the directory at
        /// <c>BasePath / clientDir / moduleDir / _wsShardedPath</c>.
        /// Virtual workspaces are DB-only — no directory is created.
        /// </summary>
        /// <param name="content_control">Whether file identifiers are auto-increment numbers (<c>Number</c>) or compact-N GUIDs (<c>Guid</c>).</param>
        /// <param name="content_pmode">Whether file names are parsed from caller input or auto-generated by the indexer.</param>
        /// <param name="is_virtual">Explicit override. When null, derived from name: null/empty/default name → virtual.</param>
        /// <param name="providerKey">Provider to use for this workspace. Null = use the registered default.</param>
        /// <param name="caseSensitive">When true, client and module directory names preserve original casing; otherwise normalized via ToDBName().</param>
        public async Task<IFeedback> RegisterWorkSpace(IVaultObject wspace, IVaultObject client, IVaultObject module, VaultNameMode content_control = VaultNameMode.Number, VaultNameParseMode content_pmode = VaultNameParseMode.Generate, bool? is_virtual = null, string providerKey = null, bool caseSensitive = false) {
            string msg = string.Empty;
            bool isVirtual = is_virtual ?? (string.IsNullOrWhiteSpace(wspace.Name) || wspace.Name.Equals(VaultConstants.DEFAULT_NAME, StringComparison.OrdinalIgnoreCase));
            if (!isVirtual && !wspace.TryValidate(out msg)) throw new Exception(msg);
            if (!client.TryValidate(out msg)) throw new Exception(msg);
            if (!module.TryValidate(out msg)) throw new Exception(msg);
            module.UpdateCUID(client.Name, module.Name);

            var provider = (!string.IsNullOrWhiteSpace(providerKey) && _providers.TryGetValue(providerKey, out var p)) ? p : GetDefaultProvider();
            bool isFs = provider is FileSystemStorageProvider;
            string wsPath = string.Empty;

            // hasRealName: distinguishes a named workspace from the auto-created default (null name).
            // Client/module base dirs are tied to the provider, not to virtual status —
            // but only when there is a real workspace name. The default auto-workspace
            // created by RegisterModule (null name) must not trigger any directory creation.
            bool hasRealName = !string.IsNullOrWhiteSpace(wspace.Name) && !wspace.Name.Equals(VaultConstants.DEFAULT_NAME, StringComparison.OrdinalIgnoreCase);
            var clientDir = caseSensitive ? client.DisplayName : client.Name.ToDBName();
            var moduleDir = caseSensitive ? module.DisplayName : module.Name.ToDBName();
            var baseDir = Path.GetFullPath(Path.Combine(BasePath, clientDir, moduleDir));
            if (isFs && WriteMode) {
                // Always create client/module base dirs for any named workspace on FS.
                if (!Directory.Exists(baseDir)) Directory.CreateDirectory(baseDir);
            }

            if (!isVirtual && hasRealName) {
                var wsCarrier = new VaultStorable(wspace.DisplayName, VaultNameMode.Guid, VaultNameParseMode.Generate);
                var wsSegment = GenerateBasePath(wsCarrier, VaultObjectType.WorkSpace).path;
                wsPath = wsSegment; //We dont care about what is the base
                if (isFs && WriteMode) {
                    var wsFullPath = Path.GetFullPath(Path.Combine(baseDir, wsPath));
                    if (!Directory.Exists(wsFullPath)) Directory.CreateDirectory(wsFullPath);
                }
            }

            var wsInfo = wspace.MapProperties(new VaultWorkSpace(client.Name, module.Name, wspace.DisplayName, isVirtual) { Base = wsPath, NameMode = content_control, ParseMode = content_pmode, CaseSensitive = caseSensitive });

            var result = new Feedback(true, $"Workspace {wspace.DisplayName} is registered");
            if (!isVirtual && hasRealName && isFs && !Directory.Exists(Path.GetFullPath(Path.Combine(baseDir, wsPath))))
                result.SetStatus(false).SetMessage("Directory is not created. Please ensure if the WriteMode is turned ON or proper access is available.");

            if (Indexer == null || !WriteMode || !result.Status) return result;
            var idxResult = await Indexer.RegisterWorkspace(wsInfo);
            result.Result = idxResult.Result;
            return result;
        }

        /// <summary>
        /// Reads the <c>Seed:sources</c> configuration section (or the provided <paramref name="section"/>)
        /// and registers all clients, modules, and workspaces declared there.
        /// Deduplicates registrations within a single call.
        /// </summary>
        /// <param name="section">Optional override section; reads from app config when null.</param>
        public async Task<IFeedback> RegisterFromSource(IConfigurationSection section = null) {
            try {
                var result = new Feedback();
                if (section == null) {
                    section = ResourceUtils.GenerateConfigurationRoot()?.GetSection($@"Seed:{VaultConstants.CONFIG_SOURCE}");
                    if (section == null) return result.SetMessage("Cannot proceed with empty configuration");
                }
                var sources = section.AsDictionaryList();
                var sourceList = sources.Where(p => p.Count > 0 && p.First().Value is Dictionary<string, object>).Select(q => ((Dictionary<string, object>)q.First().Value).Map<DSSRegInfo>()).ToList();
                if (sourceList == null || sourceList.Count < 0) return result.SetMessage("Unable to parse registration info from the given configuration section.");

                var clients = new List<string>();
                var modules = new List<string>();
                var wspaces = new List<string>();

                foreach (var source in sourceList) {
                    if (string.IsNullOrWhiteSpace(source.Client)) continue;
                    var cliKey = source.Client.ToDBName();
                    if (!clients.Contains(cliKey)) {
                        if (!(await RegisterClient(source.Client, source.Password, providerKey: source.ProviderKey, caseSensitive: source.CaseSensitive)).Status) continue;
                        clients.Add(cliKey);
                    }

                    if (string.IsNullOrWhiteSpace(source.Module)) continue;
                    var modKey = $"{cliKey}_{source.Module.ToDBName()}";
                    if (!modules.Contains(modKey)) {
                        if (!(await RegisterModule(source.Module, source.Client, source.ProviderKey, source.CaseSensitive)).Status) continue;
                        modules.Add(modKey);
                    }

                    if (string.IsNullOrWhiteSpace(source.Workspace)) continue;
                    var wsKey = $"{modKey}_{source.Workspace.ToDBName()}";
                    if (!wspaces.Contains(wsKey)) {
                        if (!(await RegisterWorkSpace(source.Workspace, source.Client, source.Module, source.Control, source.Parse, source.IsVirtual, source.ProviderKey, source.CaseSensitive)).Status) continue;
                        wspaces.Add(wsKey);
                    }
                }

                // Restore any persisted workspace profile overrides from the DB so provider
                // resolution is correct after a process restart.
                if (Indexer != null) await Indexer.RehydrateWorkspaceProfilesAsync();

                // Link any module that still has no profile to the default provider profile.
                // Must run after RehydrateWorkspaceProfilesAsync so explicit profiles are not overwritten.
                await EnsureModulesHaveDefaultProfileAsync();

                return result.SetStatus(true).SetMessage("Successfully registered.");
            } catch (Exception ex) {
                return new Feedback().SetMessage(ex.StackTrace);
            }
        }
    }
}
