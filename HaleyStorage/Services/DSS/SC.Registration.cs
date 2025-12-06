using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Xml;

namespace Haley.Services {
    public partial class StorageCoordinator : IStorageCoordinator {
        List<(string client, string module)> _caseSensitivePairs = new List<(string client, string module)>();
        public Task<IFeedback> RegisterClient(string client_name, string password = null) {
            return RegisterClient(new StorageInfo(client_name) { });
        }
        public Task<IFeedback> RegisterModule(string module_name=null, string client_name = null) {
            return RegisterModule(new StorageInfo(module_name), new StorageInfo(client_name));
        }
        public Task<IFeedback> RegisterWorkSpace(string workspace_name=null, string client_name = null, string module_name = null, VaultControlMode content_control = VaultControlMode.Number, VaultParseMode content_pmode = VaultParseMode.Generate, bool is_virtual = false) {
            return RegisterWorkSpace(new StorageInfo(workspace_name, VaultControlMode.Guid, VaultParseMode.Generate, isVirtual:is_virtual), new StorageInfo(client_name), new StorageInfo(module_name), content_control, content_pmode);
        }

        public async Task<IFeedback> RegisterClient(IVaultProfileControlled client, string password = null) {
            var result = new Feedback();
            //Password will be stored in the .dss.meta file
            if (client == null) return new Feedback(false, "Name cannot be empty");
            if (!client.TryValidate(out var msg)) return new Feedback(false, msg);
            client.ControlMode = VaultControlMode.Guid; //Either we allow as is, or we go with GUID. no numbers allowed.
            if (string.IsNullOrWhiteSpace(password)) password = DEFAULTPWD;
            var cInput = GenerateBasePath(client, Enums.VaultComponent.Client); //For client, we only prefer hash mode.
            var path = Path.Combine(BasePath, cInput.path);

            //Thins is we are not allowing any path to be provided by user. Only the name is allowed.

            //Create these folders and then register them.
            if (!Directory.Exists(path) && WriteMode) {
                Directory.CreateDirectory(path); //Create the directory only if write mode is enabled or else, we just try to store the information in cache.
            }
            
            if (!Directory.Exists(path)) result.SetStatus(false).SetMessage("Directory was not created. Check if WriteMode is ON Or make sure proper access is availalbe");
            var signing = RandomUtils.GetString(512);
            var encrypt = RandomUtils.GetString(512);
            var pwdHash = HashUtils.ComputeHash(password, HashMethod.Sha256);
            

            var clientInfo = client.MapProperties(new VaultClient(pwdHash, signing, encrypt,client.DisplayName) { Path = cInput.path });
            if (WriteMode) {
                var metaFile = Path.Combine(path, CLIENTMETAFILE);
                File.WriteAllText(metaFile, clientInfo.ToJson());   // Over-Write the keys here.
            }
            result.SetStatus(true).SetMessage($@"Client {client.DisplayName} is registered");

            if (!result.Status || Indexer == null) return result;
            var idxResult = await Indexer.RegisterClient(clientInfo);
            result.Result = idxResult.Result;

            //Whenever  we register a client, we immediately register default module and default workspace.
            await RegisterModule(new StorageInfo(null), client);
            return result;
        }
        public async Task<IFeedback> RegisterModule(IVaultProfileControlled module, IVaultProfileControlled client) {
            //AssertValues(true, (client_name,"client name"), (name,"module name")); //uses reflection and might carry performance penalty
            string msg = string.Empty;
            if (!module.TryValidate(out msg)) new Feedback(false, msg);
            if (!client.TryValidate(out msg)) new Feedback(false, msg);

            var client_path = GenerateBasePath(client, Enums.VaultComponent.Client).path; //For client, we only prefer hash mode.
            var bPath = Path.Combine(BasePath, client_path);
            if (!Directory.Exists(bPath)) return new Feedback(false, $@"Directory not found for the client {client.DisplayName}");
            if (client_path.Contains("..")) return new Feedback(false, "Client Path contains invalid characters");

            //MODULE INFORMATION BASIC VALIDATION
            var modPath = GenerateBasePath(module, Enums.VaultComponent.Module).path; //For client, we only prefer hash mode.
            bPath = Path.Combine(bPath, modPath); //Including Client Path

            //Create these folders and then register them.
            if (!Directory.Exists(bPath) && WriteMode) {
                Directory.CreateDirectory(bPath); //Create the directory.
            }

            var moduleInfo = module.MapProperties(new StorageModule(client.Name,module.DisplayName) { Path = modPath });
            if (WriteMode) {
                var metaFile = Path.Combine(bPath, MODULEMETAFILE);
                File.WriteAllText(metaFile, moduleInfo.ToJson());
            }

            var result = new Feedback(true, $@"Module {module.DisplayName} is registered");
            if (!Directory.Exists(bPath)) result.SetStatus(false).SetMessage("Directory is not created. Please ensure if the WriteMode is turned ON or proper access is availalbe.");

            if (Indexer == null) return result;
            var idxResult = await Indexer.RegisterModule(moduleInfo);
            result.Result = idxResult.Result;

            //if (!string.IsNullOrWhiteSpace(moduleInfo.DatabaseName)) module.SetCUID(moduleInfo.DatabaseName);
            await RegisterWorkSpace(new StorageInfo(null, VaultControlMode.Guid, VaultParseMode.Generate, isVirtual:true), client, module);
            return result;
        }
        public async Task<IFeedback> RegisterWorkSpace(IVaultProfileControlled wspace, IVaultProfileControlled client, IVaultProfileControlled module, VaultControlMode content_control = VaultControlMode.Number, VaultParseMode content_pmode = VaultParseMode.Generate) {
            string msg = string.Empty;
            if (!wspace.TryValidate(out msg)) throw new Exception(msg);
            if (!client.TryValidate(out msg)) throw new Exception(msg);
            if (!module.TryValidate(out msg)) throw new Exception(msg);
            module.UpdateCUID(client.Name,module.Name);

            var cliPath = GenerateBasePath(client, Enums.VaultComponent.Client).path;
            var modPath = GenerateBasePath(module, Enums.VaultComponent.Module).path;

            var path = Path.Combine(BasePath, cliPath, modPath);
            if (!Directory.Exists(path)) return new Feedback(false, $@"Unable to lcoate the basepath for the Client : {client.DisplayName}, Module : {module.DisplayName}");
            if (path.Contains("..")) return new Feedback(false, "Invalid characters found in the base path.");
            string wsPath = string.Empty;
            if (!wspace.IsVirtual) {
                //MODULE INFORMATION BASIC VALIDATION
                wsPath = GenerateBasePath(wspace, Enums.VaultComponent.WorkSpace).path; //For client, we only prefer hash mode.
                path = Path.Combine(path, wsPath); //Including Base Paths

                //Create these folders and then register them.
                if (!Directory.Exists(path) && WriteMode) {
                    Directory.CreateDirectory(path); //Create the directory.
                }
            }

            var wsInfo = wspace.MapProperties(new StorageWorkspace(client.Name, module.Name, wspace.DisplayName) { Path = wsPath ,ContentControl = content_control, ContentParse = content_pmode });
            if (WriteMode) {
                var metaFile = Path.Combine(path, WORKSPACEMETAFILE);
                File.WriteAllText(metaFile, wsInfo.ToJson());
            }

            var result = new Feedback(true, $@"Workspace {wspace.DisplayName} is registered");
            if (!Directory.Exists(path)) result.SetStatus(false).SetMessage("Directory is not created. Please ensure if the WriteMode is turned ON or proper access is availalbe.");

            if (Indexer == null) return result;
            var idxResult = await Indexer.RegisterWorkspace(wsInfo);
            result.Result = idxResult.Result;
            return result;
        }
       
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
