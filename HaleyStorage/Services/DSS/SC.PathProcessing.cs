using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using Microsoft.Identity.Client;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Haley.Services {
    public partial class StorageCoordinator : IStorageCoordinator {
        ConcurrentDictionary<string, string> _pathCache = new ConcurrentDictionary<string, string>(); //Let us store the paths of all places.
        public (string name, string path) GenerateBasePath(IVaultProfileControlled input, Enums.VaultComponent component) {
            string suffix = string.Empty;
            int length = 2;
            int depth = 0;
            bool case_sensitive = false;

            switch (component) {
                case Enums.VaultComponent.Client: //We might have very limited number of clients.
                suffix = Config.SuffixClient;
                length = 0; depth = 0;
                case_sensitive = _caseSensitivePairs.Any(p => input.Name.ToDBName().Equals(p.client, StringComparison.OrdinalIgnoreCase));
                break;
                case Enums.VaultComponent.Module:
                suffix = Config.SuffixModule;
                length = 0; depth = 0;
                case_sensitive = _caseSensitivePairs.Any(p => input.Name.ToDBName().Equals(p.module, StringComparison.OrdinalIgnoreCase));
                break;

                case Enums.VaultComponent.WorkSpace:
                //Only if the parase mode is generate, it is managed.
                string suffixAddon =string.Empty;

                if (input.ParseMode == VaultParseMode.Generate) {
                    suffixAddon = "f"; //Fully unmanagaed. //Ids are generated from the database and everthing is controlled there.
                } else {
                    suffixAddon = "p"; //Partially managed. Folder structures are properly managed with split, but, will not store any other file information. File conflicts and other things are possible. No directory structure allowed.
                }

                suffix = suffixAddon + Config.SuffixWorkSpace;
                length = 1; depth = 5;
                break;
                case Enums.VaultComponent.File:
                suffix = Config.SuffixFile;
                throw new NotImplementedException("No method implemented for handling BasePath generation for File Component type.");
            }
            return StorageUtils.GenerateFileSystemSavePath(input, VaultParseMode.Generate, (n) => { return (length, depth); }, suffix: suffix, throwExceptions: false,caseSensitive:case_sensitive);
        }
        public string GetStorageRoot() {
            return BasePath;
        }

        (IVaultProfileControlled target, Enums.VaultComponent type, string metaFilePath, string cuid) GetTargetInfo<T>(IVaultReadRequest input) where T : IVaultComponent {
            IVaultProfileControlled target = null;
            Enums.VaultComponent targetType = Enums.VaultComponent.Client;
            string metaFilePath = string.Empty;

            if (typeof(IVaultClient).IsAssignableFrom(typeof(T))) {
                targetType = Enums.VaultComponent.Client;
                metaFilePath = CLIENTMETAFILE;
                target = input.Client;
            } else if (typeof(IVaultModule).IsAssignableFrom(typeof(T))) {
                targetType = Enums.VaultComponent.Module;
                metaFilePath = MODULEMETAFILE;
                target = input.Module;
            } else if (typeof(IVaultWorkSpace).IsAssignableFrom(typeof(T))) {
                targetType = Enums.VaultComponent.WorkSpace;
                metaFilePath = WORKSPACEMETAFILE;
                target = input.Workspace;
            }

            string cuid = StorageUtils.GenerateCuid(input, targetType);
            return (target, targetType, metaFilePath, cuid);
        }

        void AddBasePath<T>(IVaultReadRequest input, List<string> paths) where T : IVaultComponent {
            //We try to take the paths for all the components.
            if (paths == null) paths = new List<string>();

            if (Indexer == null) return;
            var info = GetTargetInfo<T>(input);

            if (info.target != null) {
                if (Indexer.TryGetComponentInfo(info.cuid, out T obj)) {
                    if (!string.IsNullOrWhiteSpace(obj.Path)) paths.Add(obj.Path);  //Because sometimes we might have modules or clients where we dont' ahve any path specified. So , in those cases, we just ignore them.
                } else {
                    var tuple = GenerateBasePath(info.target, info.type);//here, we are merely generating a path based on what the user has provided. It doesn't mean that such a path really exists . 
                    paths.Add(tuple.path);
                    try {
                        var metafile = Path.Combine(BasePath, tuple.path, info.metaFilePath);
                        if (File.Exists(metafile)) {
                            //File exists , gives us the password, encrypt key and everything.. if not available already in the database cache.
                            var mfileInfo = File.ReadAllText(metafile).FromJson<T>();
                            if (mfileInfo != null) {
                                Indexer?.TryAddInfo(mfileInfo);
                            }
                        }
                    } catch (Exception) {
                    }
                }

                if (!_pathCache.ContainsKey(info.cuid)) _pathCache.TryAdd(info.cuid, string.Empty);
                var partialPath = Path.Combine(paths.ToArray());
                _pathCache.TryUpdate(info.cuid, partialPath, string.Empty);
            }
        }

        string FetchBasePath(IVaultReadRequest request, bool ignoreCache = false) {
            Initialize().Wait(); //To ensure base folders are created.
            string result = string.Empty;
            if (!ignoreCache && _pathCache.ContainsKey(request.Workspace.Cuid) && !string.IsNullOrWhiteSpace(_pathCache[request.Workspace.Cuid])) {
                result = _pathCache[request.Workspace.Cuid];
            } else {
                result = BasePath;
                List<string> paths = new List<string>();
                paths.Add(BasePath);
                AddBasePath<VaultClient>(request, paths);
                AddBasePath<StorageModule>(request, paths);
                AddBasePath<StorageWorkspace>(request, paths);
                if (paths.Count > 0) result = Path.Combine(paths.ToArray());
            }
            Console.WriteLine($"Base Path : {result}");
            if (!Directory.Exists(result)) throw new DirectoryNotFoundException($@"The base path  {result} doesn't exists.. Unable to build the base path from given input.");
            return result;
        }
        public (int length, int depth) SplitProvider(bool isNumber) {
            if (isNumber) return (Config.SplitLengthNumber, Config.DepthNumber);
            return (Config.SplitLengthHash, Config.DepthHash);
        }

        async Task<bool> GetPathFromIndexer(IVaultFileReadRequest input, bool forupload, string workspaceCuid) {
            if (!string.IsNullOrWhiteSpace(input.File?.Cuid) || input.File?.Id > 0) {
                //So, the workspace is partially or fully managed.
                var existing = input.File.Id > 0 ?
                await Indexer.GetDocVersionInfo(input.Module.Cuid, input.File.Id) :
                await Indexer.GetDocVersionInfo(input.Module.Cuid, input.File.Cuid);
                if (existing != null && existing.Status && existing.Result is Dictionary<string, object> dic) {
                    //We retrieved the information from DB. Just fetch the path.
                    if (dic.ContainsKey("path") && dic["path"] != null && !string.IsNullOrWhiteSpace(dic["path"].ToString())) {
                        input.File.Path = dic["path"].ToString();
                        if (long.TryParse(dic["size"].ToString(), out var size)) input.File.Size = size;
                        input.File.SaveAsName = dic["dname"]?.ToString() ?? string.Empty;
                        return true; //No need to proceed further.
                    }
                }
            } else if (!string.IsNullOrWhiteSpace(input.File?.Name) || !string.IsNullOrWhiteSpace(input.TargetName)) {
                //We try to search by the target name. For a target name, we also need the parent directory information as well.
                var searchName = input.File?.Name ?? input.TargetName;
                var dirName = input.Folder?.Name ?? VaultConstants.DEFAULT_NAME;
                var dirParent = input.Folder?.Parent?.Id ?? 0;
                var existing = await Indexer.GetDocVersionInfo(input.Module.Cuid, workspaceCuid, searchName, dirName, dirParent);
                if (existing != null && existing.Status && existing.Result is Dictionary<string, object> dic) {
                    //We retrieved the information from DB. Just fetch the path.
                    if (dic.ContainsKey("path") && dic["path"] != null && !string.IsNullOrWhiteSpace(dic["path"].ToString())) {
                        if (input.File == null) input.SetFile(new StorageFileRoute(input.TargetName, string.Empty) { Cuid = dic["uid"]?.ToString() });
                        if (string.IsNullOrWhiteSpace(input.File.Cuid)) input.File.SetCuid(dic["uid"]?.ToString());
                        if (string.IsNullOrWhiteSpace(input.File.Name)) input.File.SetName(searchName);
                        input.File.Path = dic["path"].ToString();
                        if (long.TryParse(dic["size"].ToString(), out var size)) input.File.Size = size;
                        input.File.SaveAsName = dic["dname"]?.ToString() ?? string.Empty;
                        return true; //No need to proceed further.
                    }
                } else {
                    //We were not able to find the file.. Remember, we are only trying to see if the file exists in the indexer or not. We are not even worrying about searching the folders. Because, we assume everything is managed in the indexer.
                    //to-do : Fix later, find ways to handle differently.
                    if (!forupload) throw new ArgumentException("File not found in the Indexer");
                }
            }
            return false;
        }
        bool PopulateFromSavedPath(IVaultFileReadRequest input, bool forupload, StorageWorkspace wInfo) {
            if (forupload || string.IsNullOrWhiteSpace(input?.File?.SaveAsName)) return false;
            //If the save as name is provided, then we need to check if workspace mode and generate the path accordingly.
            var sname = Path.GetFileNameWithoutExtension(input.File.SaveAsName);
            var extension = Path.GetExtension(input.File.SaveAsName);
            string workingName = sname;
            if (wInfo.ContentControl == VaultControlMode.Number && !sname.IsNumber()) {
                throw new ArgumentException("Provided Saveas Name is expected to be in a numberic format");
            } else if(wInfo.ContentControl == VaultControlMode.Guid) {
                Guid fileGuid;
                if (sname.IsCompactGuid(out fileGuid) || sname.IsValidGuid(out fileGuid)) {
                    workingName = fileGuid.ToString("N");
                } else {
                    throw new ArgumentException("Provided Saveas Name should be a valid GUID");
                }
            }
            input.File.Path =  StorageUtils.PreparePath(sname, SplitProvider, wInfo.ContentControl, Config.SuffixFile, extension);
            return true;
        }

        public async Task ProcessFileRoute(IVaultFileReadRequest input) {

            //If the input is OSSWrite, then we are tyring to upload or else, we are merely trying to check.
            if (input == null) return;
            if (!string.IsNullOrWhiteSpace(input.TargetPath)) return; // End goal is to have this path defined.
            if (input.File != null && !string.IsNullOrWhiteSpace(input.File.Path)) return; //Our end goal is to generate this path.

            IVaultWriteRequest inputW = input as IVaultWriteRequest;
            bool forupload = inputW != null;

            //If a component information is not avaialble for the workspace, we should not proceed.
            if (!Indexer.TryGetComponentInfo<StorageWorkspace>(input.Workspace.Cuid, out StorageWorkspace wInfo) && forupload) {
                throw new Exception($@"Unable to find the workspace information for the given input. Workspace name : {input.Workspace.Name} - Cuid : {input.Workspace.Cuid}.");
            }
            
            //If the workspace is managed, then we have the possibility to get the path from the database or from generation as well.
            if ((!forupload || ! string.IsNullOrWhiteSpace(input.File?.Cuid)) && input.File != null) {
                if (PopulateFromSavedPath(input, forupload, wInfo)) return;
                if (await GetPathFromIndexer(input, forupload, input.Workspace.Cuid)) return; //Path is retrieved.
            }

            string targetFileName = string.Empty;
            string targetFilePath = string.Empty;
            

            if (!string.IsNullOrWhiteSpace(input.TargetName)) {
                targetFileName = Path.GetFileName(input.TargetName);
            } 
            else if (forupload) {
                //We need to see if the filestream is present and take the name from there.
                if (!string.IsNullOrWhiteSpace(inputW!.FileOriginalName)) {
                    targetFileName = Path.GetFileName(inputW.FileOriginalName);
                } else if (inputW.FileStream != null && inputW.FileStream is FileStream fs) {
                    targetFileName = Path.GetFileName(fs.Name);
                    if (string.IsNullOrWhiteSpace(inputW.FileOriginalName)) inputW.SetFileOriginalName(targetFileName);
                } 
            }

            string targetExtension = Path.GetExtension(targetFileName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(targetExtension) && forupload) {
                if (!string.IsNullOrWhiteSpace(inputW.FileOriginalName)) {
                    targetExtension = Path.GetExtension(inputW.FileOriginalName);
                } else if (inputW.FileStream != null && inputW.FileStream is FileStream fs) {
                    targetExtension = Path.GetExtension(fs.Name);
                }
                if (!string.IsNullOrWhiteSpace(targetExtension)) targetFileName += targetExtension;
                input.SetTargetName(targetFileName);
            }

            if (forupload && !IsFormatAllowed(targetExtension,FormatControlMode.Extension)) throw new ArgumentException("Uploading of this file format is not allowed");

            if (string.IsNullOrWhiteSpace(input.TargetName) && !string.IsNullOrWhiteSpace(targetFileName)) input.SetTargetName(targetFileName);
            
            //If we are trying to upload
            if (input.File == null || string.IsNullOrWhiteSpace(input.File.Path)) {
                //If we reach this point, then it means we are trying to prepare the paths based on names.
                //We are trying to upload or read a file but the last storage route is not in the format of a file.
                if (string.IsNullOrWhiteSpace(targetFileName)) throw new ArgumentNullException("For the given file no target name is specified.");

                //TODO: USE THE INDEXER TO GET THE PATH FOR THIS SPECIFIC FILE WITH MODULE AND CLIENT NAME.
                //TODO: IF THE PATH IS OBTAINED, THEN JUST JOIN THE PATHS.
                var holder = new StorageInfo(targetFileName, wInfo.ContentControl, wInfo.ContentParse, isVirtual: false);
                targetFilePath = StorageUtils.GenerateFileSystemSavePath(
                    holder,
                    uidManager: (h) => {
                        if (Indexer == null || !forupload) return (0, Guid.Empty); //When we are not uploading, then no point in registering in the database.
                        //TODO: All registration should happen only inside a transaction and should be properly disposed at the end of execution.
                        //IMPLEMENT A CALL ID IN THE BASE, SO THAT WE HAVE A REFERENCE THROUGHOUTABOUT WHICH CALL IS BEING REFERENCED.
                        return Indexer.RegisterDocuments(input, h);
                    },
                    splitProvider: SplitProvider,
                    suffix: Config.SuffixFile,
                    throwExceptions: true)
                    .path;

                if (input.File == null) {
                    input.SetFile(new StorageFileRoute(targetFileName, targetFilePath) { Id = holder.Id, Cuid = holder.Cuid, Version = holder.Version, SaveAsName = holder.SaveAsName });
                }

                input.File.Path = targetFilePath;
                if (string.IsNullOrWhiteSpace(input.File.Name)) input.File.SetName(input.TargetName);
                if (string.IsNullOrWhiteSpace(input.File.Cuid)) input.File.SetCuid(holder.Cuid);
                if (string.IsNullOrWhiteSpace(input.File.SaveAsName)) input.File.SaveAsName = holder.SaveAsName;
                if (input.File.Id < 1) input.File.SetId(holder.Id);
                if (forupload) {
                    input.File.Size = inputW!.FileStream?.Length ?? 0;
                }
            }
        }



        public (string basePath, string targetPath) ProcessAndBuildStoragePath(IVaultReadRequest input, bool allowRootAccess = false) {
            var bpath = FetchBasePath(input);
            //For the Virtual or default workspaces, the CUID could end up being wrong. So, ensure and set it here.
            input.Workspace.SetCuid(StorageUtils.GenerateCuid(input, Enums.VaultComponent.WorkSpace)); //CUID might be wrong before this. Let us force set it.
            if (input is IVaultFileReadRequest fileRead) ProcessFileRoute(fileRead).Wait();
            if (input.Folder != null) {
                //Find out if the workspace is managed or not. So that, we can set the folder as Virtual
                //If a component information is not avaialble for the workspace, we should not proceed.
                if (Indexer.TryGetComponentInfo<StorageWorkspace>(input.Workspace.Cuid, out StorageWorkspace wInfo)) input.Folder.IsVirutal = true;
            }
            
            var path = input?.BuildStoragePath(bpath, allowRootAccess); //This will also ensure we are not trying to delete something 
            return (bpath, path);
        }
    }
}
