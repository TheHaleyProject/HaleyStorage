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
    /// <summary>
    /// Static utility methods for the HaleyStorage system.
    /// Covers path sanitization, sharded path generation (<see cref="PreparePath"/>),
    /// deterministic CUID generation, and the controlled-ID population pipeline
    /// used by <see cref="StorageCoordinator"/> during file registration.
    /// </summary>
    public static class StorageUtils {

        static (int length,int depth) defaultSplitProvider(bool isInputNumber) {
            if (!isInputNumber) return (1, 8); //Split by 1 and go upto 8 depth for non numbers.
            return (2, 0); //For number go full round
        }
        /// <summary>
        /// Trims whitespace and rejects paths that contain <c>".."</c> segments,
        /// preventing directory-traversal attacks. Returns the trimmed value unchanged otherwise.
        /// </summary>
        public static string SanitizePath(this string input) {
            if (string.IsNullOrWhiteSpace(input)) return input;
            input = input.Trim();
            if (input == "/" || input == @"\") input = string.Empty; //We cannot have single '/' as path.
            //if (input.StartsWith("/") || input.StartsWith(@"\")) input = input.Substring(1); //We cannot have something start with / as well
            //Reason is the user can then directly add .. and go to previous folders and access root path which is not allowed.
            if (input.Contains("..")) throw new ArgumentException("Path Contains invalid segment. Access to parent directory is not allowed.");
            return input;
        }

        /// <summary>
        /// Generates the storage name (logical ID) and sharded relative path for a <see cref="VaultStorable"/>.
        /// For virtual profiles returns the raw name and an empty path.
        /// Calls <paramref name="uidManager"/> to register/resolve the ID from the indexer, then
        /// invokes <see cref="PreparePath"/> to build the sharded directory path.
        /// </summary>
        /// <param name="uidManager">
        /// Optional delegate that registers the object in the DB and returns <c>(id, guid)</c>.
        /// Pass <c>null</c> for GUID-controlled paths (GUID is derived deterministically from the name).
        /// </param>
        public static (string name, string path) GenerateFileSystemSavePath(this IVaultStorable nObj, VaultNameParseMode? parse_overwrite = null, Func<bool, (int length, int depth)> splitProvider = null, string suffix = null, Func<IVaultStorable, (long id, Guid guid)> uidManager = null, bool throwExceptions = false, bool caseSensitive = false) {
            if (nObj == null || !nObj.TryValidate(out _)) return (string.Empty, string.Empty);
            // ControlMode, ParseMode, IsVirtual live on VaultStorable (not on IVaultStorable).
            if (!(nObj is VaultStorable profile)) return (nObj.Name, nObj.Name); //See client , module should return here itself but return the name as is and path as is.
            if (profile.IsVirtual) return (nObj.Name, "");
            IVaultObject uidInfo = null;

            //Partially or fully managed
            if (nObj.DisplayName.TryPopulateControlledID(out uidInfo, profile.NameMode, parse_overwrite ?? profile.ParseMode, uidManager, nObj, throwExceptions)) {
                nObj.StorageName = (profile.NameMode == VaultNameMode.Number) ? uidInfo.Id.ToString() : uidInfo.Guid.ToString("N");
            }

            var result = PreparePath(nObj.StorageName, splitProvider, profile.NameMode, suffix, Path.GetExtension(nObj.Name));

            //We add suffix for all controlled paths.
            return (nObj.StorageName, result);
        }

        /// <summary>
        /// Applies directory sharding to a logical ID and appends an optional suffix and extension.
        /// Example (numeric, depth=2, len=2): <c>"1234567"</c> → <c>"12/34/1234567f.mp4"</c>.
        /// Used by both <see cref="FileSystemStorageProvider.BuildStorageRef"/> (FS provider) and by
        /// <see cref="StorageCoordinator"/> when reconstructing paths for cloud providers.
        /// </summary>
        public static string PreparePath(string input, Func<bool, (int length, int depth)> splitProvider = null, VaultNameMode control_mode = VaultNameMode.Number, string suffix = null, string extension = null) {
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

        /// <summary>
        /// Generates a deterministic CUID (compact-N SHA-256 GUID) for a vault hierarchy object
        /// by hashing the relevant component names from the request scope.
        /// </summary>
        /// <param name="type">
        /// Determines which names are included: Client uses only client name;
        /// Module adds module name; WorkSpace adds workspace name.
        /// </param>
        public static string GenerateCuid(this IVaultReadRequest input, Enums.VaultObjectType type) {
            if (input == null) throw new ArgumentNullException("Inputs cannot be null or empty for CUID generation.");
            List<string> names = new List<string>();
            if (type == Enums.VaultObjectType.Client) {
                names.Add(input.Scope.Client.Name);
            } else if (type == Enums.VaultObjectType.Module) {
                names.Add(input.Scope.Client.Name);
                names.Add(input.Scope.Module.Name);
            } else if (type == Enums.VaultObjectType.WorkSpace) {
                names.Add(input.Scope.Client.Name);
                names.Add(input.Scope.Module.Name);
                names.Add(input.Scope.Workspace.Name);
            }
            if (names.Any(p=> string.IsNullOrWhiteSpace(p))) {
                throw new ArgumentNullException("Unable to generate CUID. One of the inputs is null or empty.");
            }
            return GenerateCuid(names.ToArray());
        }

        /// <summary>
        /// Generates a deterministic compact-N CUID by joining the input strings with <c>"##"</c>
        /// and hashing the result with SHA-256.
        /// </summary>
        public static string GenerateCuid(params string[] inputs) {
            if (inputs == null || inputs.Length < 1) throw new ArgumentNullException("Inputs cannot be null or empty for CUID generation.");
            string separator = "##";
            //Join the inputs with the separator and generate a GUID.
            string joined = string.Join(separator, inputs.Where(q=> !string.IsNullOrWhiteSpace(q)).Select(p=> p));
            return joined.CreateGUID(HashMethod.Sha256).ToString("N");
        }

        /// <param name="checkDirectories">
        /// Pass <c>true</c> only for the FileSystem provider. Cloud providers use base as a
        /// key prefix — there are no real directories to verify.
        /// </param>
        public static string BuildStoragePath(this IVaultReadRequest input, string basePath, bool allowRootAccess = false, bool checkDirectories = false) {

            bool readOnlyMode = input.ReadOnlyMode || !(input is IVaultFileWriteRequest);
            if (input == null || !(input is StorageReadRequest req))
                throw new ArgumentNullException($@"{nameof(IVaultReadRequest)} cannot be null. It has to be of type {nameof(StorageReadRequest)}");
            StorageReadFileRequest fileReq = input as StorageReadFileRequest;

            if (basePath.Contains(".."))
                throw new ArgumentOutOfRangeException("The base path contains invalid segments. Parent directory access is not allowed.");

            // Directory existence only applies to the FileSystem provider.
            if (checkDirectories && !Directory.Exists(basePath))
                throw new DirectoryNotFoundException("Base directory not found. Please ensure it is present.");

            if (string.IsNullOrWhiteSpace(req.OverrideRef)) {
                req.SetOverrideRef(basePath);
                // All folders are virtual (DB-only); they never contribute a physical path segment.
                if (fileReq?.File != null) {
                    req.SetOverrideRef(JoinStoragePath(req.OverrideRef, fileReq.File.FetchRoutePath(req.OverrideRef, true, allowRootAccess, readOnlyMode, checkDirectories), checkDirectories));
                }
            } else {
                req.SetOverrideRef(JoinStoragePath(basePath, req.OverrideRef, checkDirectories));
            }

            if (string.IsNullOrWhiteSpace(req.OverrideRef))
                throw new ArgumentNullException($@"Unable to generate a full object path for the request.");

            if (req.OverrideRef.Contains(".."))
                throw new ArgumentOutOfRangeException("The generated path contains invalid segments. Parent directory access is not allowed.");

            return req.OverrideRef;
        }

        /// <summary>
        /// Joins two path segments. For the FileSystem provider (<paramref name="useOsSeparator"/>
        /// = true) the OS-native <see cref="Path.Combine"/> is used so Windows paths get
        /// backslashes. For cloud providers the segments are joined with a forward slash so
        /// object keys are always well-formed regardless of the host OS.
        /// </summary>
        static string JoinStoragePath(string left, string right, bool useOsSeparator) {
            if (string.IsNullOrEmpty(right)) return left;
            if (useOsSeparator) return Path.Combine(left, right);
            return left.TrimEnd('/') + "/" + right.TrimStart('/');
        }

        static string FetchRoutePath(this IVaultRoute route, string basePath, bool finalDestination, bool allow_root_access, bool readonlyMode, bool checkDirectories = false) {

            if (checkDirectories && !Directory.Exists(basePath))
                throw new ArgumentException("BasePath directory does not exist.");

            if (route == null) return string.Empty;
            string value = SanitizePath(route.StorageRef);
            // Files use finalDestination=true; all folder routes are virtual (DB-only) — return sanitized path as-is.
            return value;
        }

        /// <summary>
        /// Parses or generates a controlled ID (number or GUID) from <paramref name="value"/>
        /// and returns it in a <see cref="IVaultObject"/> carrier.
        /// In <c>Parse</c> mode, extracts the number/GUID from the string.
        /// In <c>Generate</c> mode, calls <paramref name="idManager"/> (or SHA-256 hashes for GUID mode).
        /// </summary>
        /// <param name="idManager">Delegate to the indexer for registering/resolving the ID.</param>
        /// <param name="holder">The parent <see cref="IVaultStorable"/> passed through to the idManager.</param>
        public static bool TryPopulateControlledID(this string value, out IVaultObject result, VaultNameMode cmode, VaultNameParseMode pmode , Func<IVaultStorable, (long id, Guid guid)> idManager, IVaultStorable holder, bool throwExceptions = false) {
            result = null;

            if (string.IsNullOrWhiteSpace(value)) {
                if (throwExceptions) throw new ArgumentNullException("Unable to generate the ID. The provided input is null or empty.");
                return false;
            }
            string workingValue = Path.GetFileNameWithoutExtension(value); //WITHOUT EXTENSION, ONLY FILE NAME

            var data = (pmode == VaultNameParseMode.Parse) ? HandleParseUID(workingValue, cmode, idManager,holder, throwExceptions) : HandleGenerateUID(workingValue, cmode,idManager,holder,throwExceptions);

            if (!data.status) return false; //Dont' proceed.

            var uidObj = new VaultObject(VaultConstants.DEFAULT_NAME);
            uidObj.Id = data.id;
            uidObj.SetGuid(data.guid);
            result = uidObj;

            if (cmode == VaultNameMode.Number && data.id < 1) {
                if (throwExceptions) throw new ArgumentNullException("The final generated id is less than 1. Not acceptable. Please check the inputs.");
                return false;
            } else if (cmode == VaultNameMode.Guid && data.guid == Guid.Empty) {
                if (throwExceptions) throw new ArgumentNullException("The final generated guid is an empty value. Not acceptable. Please check the inputs.");
                return false;
            }
            return true;
        }

        static (bool status, long id, Guid guid) HandleParseUID(this string value, VaultNameMode cmode, Func<IVaultStorable,(long id, Guid guid)> idManager, IVaultStorable holder, bool throwExceptions = false) {
            //PARTIALLY MANAGED. IT SHOULD ALSO ALLOW ME TO STORE THE INFORMATION IN THE DATABASE??

            long resNumber = 0;
            Guid resGuid = Guid.Empty;
            if (cmode == VaultNameMode.Number) {
                if (!long.TryParse(value, out resNumber)) {
                    if (throwExceptions) throw new ArgumentNullException($@"The provided input is not in the number format. Unable to parse a long value. ID Manager status : {idManager != null}");
                    return (false, resNumber, resGuid);
                }
            } else if (cmode == VaultNameMode.Guid) {
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

        static (bool status, long id, Guid guid) HandleGenerateUID(this string value, VaultNameMode cmode, Func<IVaultStorable,(long id, Guid guid)> idManager, IVaultStorable holder, bool throwExceptions = false) {
            long resNumber = 0;
            Guid resGuid = Guid.Empty;
            (long id, Guid guid)? dbInfo = null;

            if (idManager == null) {
                if (cmode == VaultNameMode.Guid) {
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
