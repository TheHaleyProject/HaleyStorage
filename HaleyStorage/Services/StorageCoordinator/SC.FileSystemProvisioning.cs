using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;

namespace Haley.Services {
    public partial class StorageCoordinator {
        /// <summary>
        /// Idempotently provisions the filesystem paths required by a registered module.
        /// Virtual workspaces create no workspace directory; non-filesystem workspace overrides are skipped.
        /// </summary>
        public async Task<IFeedback> EnsureFileSystemPaths(string client_name, string module_name) {
            if (Indexer == null)
                return new Feedback(false, "Filesystem paths cannot be ensured without a registry indexer.");

            var request = new StorageReadRequest(client_name, module_name);
            request.Scope.Workspace = null;
            var access = CheckWriteAccess(request);
            if (!access.Status) return access;
            var moduleCuid = request.Scope.Module.Cuid.ToString("N");
            await Initialize();

            if (!await Indexer.HydrateModuleAsync(moduleCuid))
                return new Feedback(false, $"Module '{client_name}/{module_name}' is not registered.");

            var moduleProvider = ResolveProvider(moduleCuid);
            if (moduleProvider is not FileSystemStorageProvider)
                return new Feedback(false,
                    $"Module '{client_name}/{module_name}' uses provider '{moduleProvider?.Key ?? "unknown"}'. Ensure is available only for FileSystem storage.");

            var workspaceCuids = await Indexer.GetWorkspaceCuidsAsync(moduleCuid);
            foreach (var workspaceCuid in workspaceCuids)
                await Indexer.HydrateWorkspaceAsync(workspaceCuid);

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                GetContainedFileSystemPath(client_name.ToDBName(), module_name.ToDBName())
            };
            var virtualWorkspaces = new List<string>();
            var skippedProviders = new List<string>();
            var readOnlyWorkspaces = new List<string>();

            foreach (var workspaceCuid in workspaceCuids) {
                if (!Indexer.TryGetComponentInfo(workspaceCuid, out VaultWorkSpace workspace))
                    continue;

                if (!IsWriteAllowed(client_name, module_name, workspace.DisplayName)) {
                    readOnlyWorkspaces.Add(workspace.Name);
                    continue;
                }

                var workspaceRequest = new StorageReadRequest(client_name, module_name, workspace.DisplayName);
                workspaceRequest.Scope.Workspace.SetCuid(workspace.Cuid);
                var workspaceProvider = ResolveProvider(workspaceRequest);
                if (workspaceProvider is not FileSystemStorageProvider) {
                    skippedProviders.Add($"{workspace.Name}:{workspaceProvider?.Key ?? "unknown"}");
                    continue;
                }

                if (workspace.IsVirtual) {
                    virtualWorkspaces.Add(workspace.Name);
                    if (!string.IsNullOrWhiteSpace(workspace.Base))
                        paths.Add(GetContainedFileSystemPath(workspace.Base));
                    continue;
                }

                var storageRef = workspace.StorageRef;
                if (string.IsNullOrWhiteSpace(storageRef)) {
                    var carrier = new VaultStorable(workspace.DisplayName, VaultNameMode.Guid, VaultNameParseMode.Generate);
                    storageRef = GenerateBasePath(carrier, VaultObjectType.WorkSpace).path;
                }

                paths.Add(GetContainedFileSystemPath(workspace.Base, storageRef));
            }

            var created = new List<string>();
            var existing = new List<string>();
            foreach (var path in paths.OrderBy(path => path.Length)) {
                if (Directory.Exists(path)) {
                    existing.Add(path);
                    continue;
                }

                Directory.CreateDirectory(path);
                created.Add(path);
            }

            var message = $"Filesystem paths ensured for '{client_name}/{module_name}': {created.Count} created, {existing.Count} already present.";
            return new Feedback(true, message) {
                Result = new {
                    providerKey = moduleProvider.Key,
                    createdPaths = created,
                    existingPaths = existing,
                    virtualWorkspaces,
                    skippedProviders,
                    readOnlyWorkspaces
                }
            };
        }

        string GetContainedFileSystemPath(params string[] segments) {
            var root = Path.GetFullPath(BasePath);
            var candidate = Path.GetFullPath(Path.Combine(new[] { root }.Concat(segments.Where(segment => !string.IsNullOrWhiteSpace(segment))).ToArray()));
            var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!candidate.Equals(root, comparison) && !candidate.StartsWith(rootPrefix, comparison))
                throw new InvalidOperationException($"Resolved storage path '{candidate}' is outside storage root '{root}'.");
            return candidate;
        }
    }
}
