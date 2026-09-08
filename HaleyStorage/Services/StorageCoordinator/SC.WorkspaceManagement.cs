using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;

namespace Haley.Services {
    public partial class StorageCoordinator {
        /// <summary>
        /// Changes only the physical/virtual routing behavior of an existing workspace.
        /// Existing bytes are deliberately not migrated; callers must explicitly accept that impact.
        /// </summary>
        public async Task<IFeedback> ChangeWorkspaceType(
            string workspace_name,
            string client_name,
            string module_name,
            bool is_virtual,
            bool force = false) {
            if (Indexer == null)
                return new Feedback(false, "Workspace type cannot be changed without a registry indexer.");
            if (string.IsNullOrWhiteSpace(client_name))
                return new Feedback(false, "Client name is mandatory.");
            if (string.IsNullOrWhiteSpace(module_name))
                return new Feedback(false, "Module name is mandatory.");
            if (string.IsNullOrWhiteSpace(workspace_name))
                return new Feedback(false, "Workspace name is mandatory.");

            var request = new StorageReadRequest(client_name, module_name, workspace_name);
            var access = CheckWriteAccess(request);
            if (!access.Status) return access;

            await Initialize();
            var workspaceCuid = request.Scope.Workspace.Cuid.ToString("N");
            if (!await Indexer.HydrateWorkspaceAsync(workspaceCuid)
                || !Indexer.TryGetComponentInfo(workspaceCuid, out VaultWorkSpace current))
                return new Feedback(false, $"Workspace '{client_name}/{module_name}/{workspace_name}' is not registered.");

            if (current.IsVirtual == is_virtual)
                return new Feedback(true, $"Workspace '{workspace_name}' is already {(is_virtual ? "virtual" : "physical")}.");

            if (!force)
                return new Feedback(false,
                    "Changing workspace type changes its base path. Existing bytes are not moved and may no longer be reachable through normal reads. Retry with force=true to continue.");

            var storageRef = current.StorageRef ?? string.Empty;
            if (!is_virtual && string.IsNullOrWhiteSpace(storageRef)) {
                var carrier = new VaultStorable(current.DisplayName, VaultNameMode.Guid, VaultNameParseMode.Generate);
                storageRef = GenerateBasePath(carrier, VaultObjectType.WorkSpace).path;
            }

            var updated = new VaultWorkSpace(client_name, module_name, current.DisplayName, is_virtual) {
                Id = current.Id,
                StorageRef = storageRef,
                Base = string.IsNullOrWhiteSpace(current.Base)
                    ? Path.Combine(
                        current.CaseSensitive ? client_name : client_name.ToDBName(),
                        current.CaseSensitive ? module_name : module_name.ToDBName())
                    : current.Base,
                NameMode = current.NameMode,
                ParseMode = current.ParseMode,
                CaseSensitive = current.CaseSensitive,
                StorageProfileName = current.StorageProfileName,
                StorageProviderKey = current.StorageProviderKey,
                StagingProviderKey = current.StagingProviderKey,
                ProfileMode = current.ProfileMode,
                ProfileInfoId = current.ProfileInfoId
            };
            updated.SetCuid(current.Cuid);

            string createdPath = null;
            if (!is_virtual) {
                var provider = ResolveProvider(request);
                if (provider is FileSystemStorageProvider) {
                    createdPath = GetContainedFileSystemPath(updated.Base, updated.StorageRef);
                    Directory.CreateDirectory(createdPath);
                }
            }

            var persistence = await Indexer.RegisterWorkspace(updated);
            if (!persistence.Status) return persistence;

            Indexer.TryAddInfo(updated, replace: true);
            _pathCache.TryRemove(workspaceCuid, out _);
            _workspaceRegistryRefresh.AddOrUpdate(workspaceCuid, DateTime.UtcNow, (_, _) => DateTime.UtcNow);

            return new Feedback(true,
                $"Workspace '{workspace_name}' changed from {(current.IsVirtual ? "virtual" : "physical")} to {(is_virtual ? "virtual" : "physical")}. Existing bytes were not moved.") {
                Result = new {
                    workspaceCuid,
                    isVirtual = is_virtual,
                    storageRef,
                    createdPath,
                    existingBytesMoved = false
                }
            };
        }
    }
}
