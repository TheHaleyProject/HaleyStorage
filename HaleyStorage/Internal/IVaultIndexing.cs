using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using Haley.Models;
using Haley.Enums;
using Haley.Abstractions;

namespace Haley.Services {
    /// <summary>
    /// Internal contract for DB-backed vault indexing.
    /// Consumers never implement or inject this — the implementation is wired up internally
    /// by <see cref="StorageCoordinator"/> based on the DB choice (currently MariaDB only).
    /// </summary>
    internal interface IVaultIndexing {
        bool ThrowExceptions { get; }
        Task<IFeedback> RegisterClient(IVaultClient info);
        Task<IFeedback> RegisterModule(IVaultModule info);
        Task<IFeedback> RegisterWorkspace(IVaultWorkSpace info);
        Task<(long id, Guid guid)> RegisterDocuments(IVaultReadRequest request, IVaultStorable holder);
        Task<IFeedback> UpdateDocVersionInfo(string moduleCuid, IVaultFileRoute file, string callId = null);
        Task<IFeedback> UpdateDocDisplayName(string moduleCuid, long versionId, string displayName);
        Task<IFeedback> GetDocVersionInfo(string moduleCuid, long id);
        Task<IFeedback> GetDocVersionInfo(string moduleCuid, string cuid);
        Task<IFeedback> GetDocVersionInfo(string moduleCuid, string wsCuid, string file_name, string dir_name = VaultConstants.DEFAULT_NAME, long dir_parent_id = 0);
        Task<IFeedback> GetDocVersionInfo(string moduleCuid, long wsId, string file_name, string dir_name = VaultConstants.DEFAULT_NAME, long dir_parent_id = 0);
        Task<IFeedback<VaultFolderBrowseResponse>> BrowseFolder(IVaultReadRequest request, int page = 1, int pageSize = 50);
        Task<IFeedback<(long id, string cuid)>> RegisterDirectory(IVaultReadRequest request, string folderName);
        /// <summary>
        /// Searches for matching folders and files (latest version only) across the workspace.
        /// The term is matched against vault names (filename stems); extension is a separate filter.
        /// </summary>
        Task<IFeedback<VaultFolderBrowseResponse>> SearchItems(IVaultReadRequest request, string searchTerm, VaultSearchMode searchMode, string extension = null, long directoryId = 0, bool recursive = false, int page = 1, int pageSize = 50);
        Task<IFeedback<VaultFileDetailsResponse>> GetFileDetails(IVaultFileReadRequest request);
        Task EnsureValidation();
        bool TryGetComponentInfo<T>(string key, out T component) where T : IVaultObject;
        bool TryAddInfo(IVaultObject dirInfo, bool replace = false);
        IEnumerable<T> GetAllComponents<T>() where T : IVaultObject;
        Task<IFeedback<string>> GetParentName(IVaultFileReadRequest request);
        // Chunking
        Task<IFeedback> UpsertChunkInfo(string moduleCuid, long versionId, long chunkSizeMb, int totalParts, string chunkFolderName, string chunkFolderPath, bool isCompleted = false, string callId = null);
        Task<IFeedback> UpsertChunkPart(string moduleCuid, long versionId, long partNumber, int sizeMb, string hash = null, string callId = null);
        Task<IFeedback> MarkChunkCompleted(string moduleCuid, long versionId, string callId = null);
        // Storage profiles
        Task<long> UpsertProvider(string displayName, string description = null);
        Task<long> UpsertProfile(string displayName);
        Task<long> UpsertProfileInfo(int profileId, int version, int mode, string storageProviderKey, string stagingProviderKey, string metadataJson);
        Task<bool> SetModuleStorageProfile(string moduleCuid, int profileId);
        Task<bool> SetWorkspaceStorageProfile(string workspaceCuid, int profileInfoId);
        /// <summary>
        /// Walks all cached workspaces and restores any persisted storage-profile overrides from the DB.
        /// Call once at startup after all registrations are complete.
        /// </summary>
        Task RehydrateWorkspaceProfilesAsync();
        /// <summary>
        /// Fetches the resolved provider keys and mode for a specific <c>profile_info.id</c>.
        /// Returns a dictionary with keys: <c>storage_provider_key</c>, <c>staging_provider_key</c>, <c>mode</c>.
        /// </summary>
        Task<IFeedback> GetProfileInfo(long profileInfoId);

        // ── Staging promotion ─────────────────────────────────────────────────

        /// <summary>
        /// Returns the next batch of <see cref="StagedVersionRef"/> rows whose bytes are on a
        /// staging provider but have not yet been promoted to primary storage.
        /// Filter: <c>(flags &amp; 4) &gt; 0 AND (flags &amp; 8) = 0 AND synced_at IS NULL</c>.
        /// </summary>
        Task<IEnumerable<StagedVersionRef>> GetPendingStagedVersions(string moduleCuid, int batchSize = 20);

        /// <summary>
        /// Records the outcome of a successful promotion: writes the final <c>storage_ref</c>,
        /// updates <c>flags</c> (e.g. <c>8|64</c> for StageAndMove), and stamps <c>synced_at</c>.
        /// </summary>
        Task<IFeedback> UpdateVersionPromotion(string moduleCuid, long versionId, string storageRef, int newFlags, DateTime syncedAt, long size = 0, string hash = null);
    }
}
