using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.Schema;

namespace Haley.Utils
{
    public static class StorageUtils {
 
        static (int length,int depth) defaultSplitProvider(bool isInputNumber) {
            if (!isInputNumber) return (1, 8); //Split by 1 and go upto 8 depth for non numbers.
            return (2, 0); //For number go full round
        }
        public static string SanitizePath(this string input) {
            if (string.IsNullOrWhiteSpace(input)) return input;
            input = input.Trim();
            if (input == "/" || input == @"\") input = string.Empty; //We cannot have single '/' as path.
            //if (input.StartsWith("/") || input.StartsWith(@"\")) input = input.Substring(1); //We cannot have something start with / as well
            //Reason is the user can then directly add .. and go to previous folders and access root path which is not allowed.
            if (input.Contains("..")) throw new ArgumentException("Path Contains invalid segment. Access to parent directory is not allowed.");
            return input;
        }

        public static (string name, string path) GenerateFileSystemSavePath(this IVaultInfo nObj,VaultParseMode? parse_overwrite = null, Func<bool,(int length,int depth)> splitProvider = null, string suffix = null, Func<IVaultInfo,(long id, Guid guid)> uidManager = null,bool throwExceptions = false, bool caseSensitive = false) {
            if (nObj == null || !nObj.TryValidate(out _)) return (string.Empty, string.Empty);
            //If We are dealing with virutal item. No need to think a lot, as there is no path.
            if (nObj.IsVirtual) return (nObj.Name, "");
            IVaultBase uidInfo = null;

            //Partially or fully managed
            if (nObj.DisplayName.TryPopulateControlledID(out uidInfo, nObj.ControlMode, parse_overwrite ?? nObj.ParseMode, uidManager, nObj, throwExceptions)) {
                nObj.StorageName = (nObj.ControlMode == VaultControlMode.Number) ? uidInfo.Id.ToString() : uidInfo.Guid.ToString("N");
            }

            var result = PreparePath(nObj.StorageName, splitProvider, nObj.ControlMode,suffix,Path.GetExtension(nObj.Name));

            //We add suffix for all controlled paths.
            return (nObj.StorageName, result);
        }

        public static string PreparePath(string input, Func<bool, (int length, int depth)> splitProvider = null, VaultControlMode control_mode = VaultControlMode.Number, string suffix = null, string extension = null) {
            if (string.IsNullOrWhiteSpace(input)) return input;
            if (splitProvider == null) splitProvider = defaultSplitProvider;
            bool isNumber = input.IsNumber();
            var sinfo = splitProvider(isNumber);
            var result = input.Separate(sinfo.length, sinfo.depth, addPadding: isNumber ? true : false, resultAsPath: true);
            //If extension is missing, check for extension. (only for controlled paths, extension would be missing)
            if (!string.IsNullOrWhiteSpace(suffix)) {
                //If we are dealing with number and also inside some kind of control mode, add suffix.
                result += $@"{suffix}"; //Never get _ as suffix.
            }

            //Add extension if exists.
            if (!string.IsNullOrWhiteSpace(extension)) {
                result += $@"{extension}";
            }
            return result;
        }

        public static string GenerateCuid(this IVaultReadRequest input, Enums.VaultObjectType type) {
            if (input == null) throw new ArgumentNullException("Inputs cannot be null or empty for CUID generation.");
            List<string> names = new List<string>();
            if (type == Enums.VaultObjectType.Client) {
                names.Add(input.Client.Name);
            } else if (type == Enums.VaultObjectType.Module) {
                names.Add(input.Client.Name);
                names.Add(input.Module.Name);
            } else if (type == Enums.VaultObjectType.WorkSpace) {
                names.Add(input.Client.Name);
                names.Add(input.Module.Name);
                names.Add(input.Workspace.Name);
            }
            if (names.Any(p=> string.IsNullOrWhiteSpace(p))) {
                throw new ArgumentNullException("Unable to generate CUID. One of the inputs is null or empty.");
            }
            return GenerateCuid(names.ToArray());
        }

        public static string GenerateCuid(params string[] inputs) {
            if (inputs == null || inputs.Length < 1) throw new ArgumentNullException("Inputs cannot be null or empty for CUID generation.");
            string separator = "##";
            //Join the inputs with the separator and generate a GUID.
            string joined = string.Join(separator, inputs.Where(q=> !string.IsNullOrWhiteSpace(q)).Select(p=> p));
            return joined.CreateGUID(HashMethod.Sha256).ToString("N");
        }

        public static string BuildStoragePath(this IVaultReadRequest input, string basePath, bool allowRootAccess = false) {
            bool readOnlyMode = input.ReadOnlyMode || !(input is IVaultFileWriteRequest); //If the input is osswrite, then we are trying to upload a file or else we deliberately set the input as readonly
            bool forFile = false;
            //While building storage path, may be we are building only the 
            if (input == null || !(input is StorageReadRequest req)) throw new ArgumentNullException($@"{nameof(IVaultReadRequest)} cannot be null. It has to be of type {nameof(StorageReadRequest)}");
            StorageReadFileRequest fileReq = input as StorageReadFileRequest;
            if (fileReq != null) forFile = true;

            if (basePath.Contains("..")) throw new ArgumentOutOfRangeException("The base path contains invalid segments. Parent directory access is not allowed. Please fix");
            if (!Directory.Exists(basePath)) throw new DirectoryNotFoundException("Base directory not found. Please ensure it is present");
            if (string.IsNullOrWhiteSpace(req.TargetPath)) {
                //Now we have two items to build. Directory path and file path. May be we are just building a directory here.
                  req.SetTargetPath(basePath);
                if (input.Folder != null && !input.Folder.IsVirutal) {
                    req.SetTargetPath(Path.Combine(req.TargetPath, input.Folder.FetchRoutePath(req.TargetPath,!forFile,allowRootAccess, readOnlyMode)));
                }
                if (fileReq != null && fileReq.File != null) {
                    req.SetTargetPath(Path.Combine(req.TargetPath, fileReq.File.FetchRoutePath(req.TargetPath, true, allowRootAccess, readOnlyMode)));
                }
            } else {
                req.SetTargetPath(Path.Combine(basePath, req.TargetPath));
            }

            //What if, the user provided no value and we end up with only the Basepath.
            if (string.IsNullOrWhiteSpace(req.TargetPath)) throw new ArgumentNullException($@"Unable to generate a full object path for the request");

            if (req.TargetPath.Contains("..")) throw new ArgumentOutOfRangeException("The generated path contains invalid segments. Parent directory access is not allowed. Please fix");

            return req.TargetPath;
        }

        static string FetchRoutePath(this IVaultRoute route, string basePath,bool finalDestination, bool allow_root_access, bool readonlyMode) {
            //SEND ONLY THE PATH FROM THE ROUTE.. NOT THE FULL PATH INCLUDING THE BASE PATH.
            //THE BASE PATH EXISTS HERE ONLY FOR TESTING PURPOSE.
            string path = basePath;  
            if (!Directory.Exists(path)) throw new ArgumentException("BasePath Directory doesn't exists.");
             
            if (route == null) return string.Empty; //Directly create inside the basepath (applicable in few cases);
            //If one of the path is trying to make a root access, should we allow or deny?
            //Route is expected to have one or more parents.
            // So we loop through the routes and reach the last route without any parent and start building from there.
            string value = SanitizePath(route.Path);
            if (finalDestination || !(route is IVaultFolderRoute fldrRoute) || fldrRoute.IsVirutal) return value; //If the route is for file or else the folder route is only for virtual situation then we just return as is.

            if (string.IsNullOrWhiteSpace(value) && !allow_root_access) throw new AccessViolationException("Root directory access is not allowed."); //We should not access the root folder. It's like the path was kept deliberately empty so that the workspace location can be accessed.

            path = Path.Combine(path, value); //Combing with the base path.
            
            //1. a) Dir Creation disallowed b) Dir doesn't exists 
            if (!Directory.Exists(path) && !fldrRoute.CreateIfMissing) {
                //Whether it is a file or a directory, if user doesn't have access to create it, throw exception.
                //We cannot allow to create Client & Module paths.
                string errMsg = $@"Directory doesn't exists : {route.Name ?? route.Path}";
                //2.1 ) Are we in the middle, trying to ensure some directory exists?
                if (!readonlyMode) errMsg = $@"Access denied to create/delete the directory :{route.Name ?? route.Path}";
                throw new ArgumentException(errMsg);
            }

            //3. Are we trying to create a directory as our main goal?? If yes, then we should not try to create here.. It will be created outside of this .

            if (!(path?.TryCreateDirectory().Result ?? false)) throw new ArgumentException($@"Unable to create the directory : {route.Name ?? route.Path}");

            if (!path.StartsWith(basePath)) throw new ArgumentOutOfRangeException("The generated path is not accessible. Please check the inputs.");
            return value; //Dont' return the full path as we will be joining this result with other base path outside this function.
        }
       
        public static bool TryPopulateControlledID(this string value, out IVaultBase result, VaultControlMode cmode, VaultParseMode pmode , Func<IVaultInfo, (long id, Guid guid)> idManager, IVaultInfo holder, bool throwExceptions = false) {
            result = null;
            
            if (string.IsNullOrWhiteSpace(value)) {
                if (throwExceptions) throw new ArgumentNullException("Unable to generate the ID. The provided input is null or empty.");
                return false;
            }
            string workingValue = Path.GetFileNameWithoutExtension(value); //WITHOUT EXTENSION, ONLY FILE NAME
           
            var data = (pmode == VaultParseMode.Parse) ? HandleParseUID(workingValue, cmode, idManager,holder, throwExceptions) : HandleGenerateUID(workingValue, cmode,idManager,holder,throwExceptions);

            if (!data.status) return false; //Dont' proceed.

            result = new VaultUID(data.id, data.guid);

            if (cmode == VaultControlMode.Number && data.id < 1) {
                if (throwExceptions) throw new ArgumentNullException("The final generated id is less than 1. Not acceptable. Please check the inputs.");
                return false;
            } else if (cmode == VaultControlMode.Guid && data.guid == Guid.Empty) {
                if (throwExceptions) throw new ArgumentNullException("The final generated guid is an empty value. Not acceptable. Please check the inputs.");
                return false;
            }
            return true;
        }
        
        static (bool status, long id, Guid guid) HandleParseUID(this string value, VaultControlMode cmode, Func<IVaultInfo,(long id, Guid guid)> idManager, IVaultInfo holder, bool throwExceptions = false) {
            //PARTIALLY MANAGED. IT SHOULD ALSO ALLOW ME TO STORE THE INFORMATION IN THE DATABASE??

            long resNumber = 0;
            Guid resGuid = Guid.Empty;
            if (cmode == VaultControlMode.Number) {
                if (!long.TryParse(value, out resNumber)) {
                    if (throwExceptions) throw new ArgumentNullException($@"The provided input is not in the number format. Unable to parse a long value. ID Manager status : {idManager != null}");
                    return (false, resNumber, resGuid);
                }
            } else if (cmode == VaultControlMode.Guid) {
                if (value.IsValidGuid(out resGuid)) { //Parse
                } else if (value.IsCompactGuid(out resGuid)) { //Parse
                } else {
                    if (throwExceptions) throw new ArgumentNullException("Unable to parse the GUID from the given input. Please check the input.");
                    return (false, resNumber, resGuid);
                }
            }
            idManager?.Invoke(holder); //Just to get the stored info, if available 
            return (true, resNumber, resGuid);
        }
        
        static (bool status, long id, Guid guid) HandleGenerateUID(this string value, VaultControlMode cmode, Func<IVaultInfo,(long id, Guid guid)> idManager, IVaultInfo holder, bool throwExceptions = false) {
            long resNumber = 0;
            Guid resGuid = Guid.Empty;
            (long id, Guid guid)? dbInfo = null;

            if (idManager == null) {
                if (cmode == VaultControlMode.Guid) {
                    //Only for GUID, we can autogenerate the hash based on the input. So, we can go ahead and create it.
                    resGuid = value.ToDBName().CreateGUID(HashMethod.Sha256);
                } else {
                    if (throwExceptions) throw new ArgumentNullException("Id Generator should be provided to fetch and generate ID");
                    return (false, resNumber, resGuid);
                }
            } else {
                dbInfo = idManager.Invoke(holder);
                resNumber = dbInfo?.id ?? 0; //Regardless of whatever we generate, we set for both.
                resGuid = dbInfo?.guid ?? Guid.Empty;
            }
            return (true, resNumber, resGuid);
        }
    }
}
