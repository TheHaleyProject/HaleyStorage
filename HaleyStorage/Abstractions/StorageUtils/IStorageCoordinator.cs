using Haley.Enums;
using System.IO;
using System.Threading.Tasks;
using Haley.Models;

namespace Haley.Abstractions {
    public interface IStorageCoordinator : IStorageOperations, IVaultManagement, IFileFormatPolicy,IStorageProviderRegistry {
        IStorageCoordinator SetConfig(IVaultRegistryConfig config);
        bool ThrowExceptions { get; }
        string GetStorageRoot();
        Task<IVaultDirResponse> GetDirectoryInfo(IVaultReadRequest input);
        Task<IFeedback<string>> GetParent(IVaultFileReadRequest input);
        Task<IFeedback<VaultFolderBrowseResponse>> BrowseFolder(IVaultReadRequest input, int page = 1, int pageSize = 50);
        /// <summary>
        /// Searches for folders and files whose vault name matches <paramref name="searchTerm"/>
        /// (using <paramref name="searchMode"/>), optionally filtered by extension.
        /// Scope: entire workspace (<paramref name="directoryId"/> = 0), a single directory, or
        /// a full recursive subtree (<paramref name="recursive"/> = true).
        /// Returns the latest file version for each matching document. Paginated.
        /// </summary>
        Task<IFeedback<VaultFolderBrowseResponse>> SearchItems(IVaultReadRequest input, string searchTerm, VaultSearchMode searchMode, string extension = null, long directoryId = 0, bool recursive = false, int page = 1, int pageSize = 50);
        Task<IFeedback<VaultFileDetailsResponse>> GetFileDetails(IVaultFileReadRequest input);
        Task<IVaultResponse> CreateDirectory(IVaultReadRequest input, string rawname);
        Task<IFeedback> DeleteDirectory(IVaultReadRequest input, bool recursive);
        bool WriteMode { get; }

        // ── Provider / profile configuration ─────────────────────────────────
        /// <summary>
        /// Sets the runtime provider routing for a registered module without requiring a
        /// DB round-trip. Call this at startup (e.g. in Program.cs) after registration.
        /// <paramref name="storageProviderKey"/> — key of the primary provider (e.g. "FileSystem", "B2").
        /// <paramref name="stagingProviderKey"/> — key of the staging provider, or null for none.
        /// <paramref name="mode"/> — upload routing mode; defaults to DirectSave.
        /// Both provider keys must already be registered with <c>AddProvider</c>.
        /// Returns false if the module CUID is not found in the indexer cache.
        /// </summary>
        /// <summary>
        /// Returns the effective primary <see cref="IStorageProvider"/> for the given request scope.
        /// Applies the same resolution order as internal write/read paths:
        /// file profile_info_id → workspace override → module → global default.
        /// </summary>
        IStorageProvider GetPrimaryProvider(IVaultReadRequest request);

        bool ConfigureModuleProviders(string moduleCuid, string storageProviderKey,
            string stagingProviderKey = null, StorageProfileMode mode = StorageProfileMode.DirectSave,
            long profileInfoId = 0);

        /// <summary>
        /// Sets the runtime provider routing for a registered workspace, overriding the module-level
        /// profile for this workspace only. Pass null for <paramref name="stagingProviderKey"/> to
        /// disable staging at workspace level. Both keys must already be registered with <c>AddProvider</c>.
        /// Returns false if the workspace CUID is not found in the indexer cache.
        /// </summary>
        bool ConfigureWorkspaceProviders(string workspaceCuid, string storageProviderKey,
            string stagingProviderKey = null, StorageProfileMode mode = StorageProfileMode.DirectSave);

        // ── Placeholder / Background-Move ────────────────────────────────────

        /// <summary>
        /// Reserves a DB record (document + doc_version + version_info with flags=256 Placeholder)
        /// and returns the pre-computed target storage location so an external process can copy
        /// the file out-of-band (USB drop, server-side move, cloud-native copy, etc.).
        /// <para>
        /// For FileSystem providers the parent shard directory is created immediately,
        /// so the caller can start the copy without any extra setup.
        /// </para>
        /// <para>
        /// After the copy completes, call <see cref="FinalizePlaceholder"/> to mark the version
        /// as InStaging or InStorage|Completed and record the final size and hash.
        /// </para>
        /// </summary>
        /// <param name="request">Scope (client/module/workspace). File route is ignored.</param>
        /// <param name="fileName">File name including extension (e.g. <c>"archive.mp4"</c>).</param>
        /// <param name="displayName">Optional human-readable display name stored in doc_info.</param>
        Task<IFeedback<PlaceholderInfo>> CreatePlaceholder(IVaultReadRequest request, string fileName, string displayName = null);

        /// <summary>
        /// Marks a placeholder version as complete after the out-of-band copy lands.
        /// <list type="bullet">
        ///   <item>toStaging=false → sets flags = InStorage|Completed (8|64); updates storage_ref.</item>
        ///   <item>toStaging=true  → sets flags = InStaging (4); updates staging_ref.</item>
        /// </list>
        /// When <paramref name="size"/> is <c>null</c> and the provider is FileSystem and
        /// <paramref name="toStaging"/> is false, the file size is read directly from disk.
        /// </summary>
        /// <param name="request">Scope identifying the module DB (client/module/workspace).</param>
        /// <param name="versionId">The VersionId returned by <see cref="CreatePlaceholder"/>.</param>
        /// <param name="toStaging">True if the file was copied to staging rather than primary storage.</param>
        /// <param name="size">File size in bytes, or null to auto-detect (FS only).</param>
        /// <param name="hash">Optional SHA-256 hash of the copied file.</param>
        Task<IFeedback> FinalizePlaceholder(IVaultReadRequest request, long versionId,
            bool toStaging = false, long? size = null, string hash = null);

        // ── Chunked Upload ────────────────────────────────────────────────────
        /// <summary>
        /// Registers the document in DB, creates a temp chunk directory, and returns the
        /// versionId + versionCuid needed for subsequent part uploads and completion.
        /// </summary>
        Task<IFeedback<(long versionId, string versionCuid)>> InitiateChunkedUpload(IVaultFileWriteRequest request, long chunkSizeMb, int totalParts);

        /// <summary>Writes one chunk part to the temp directory and records it in DB.</summary>
        Task<IFeedback> UploadChunkPart(long versionId, int partNumber, Stream chunkStream, string hash = null);

        /// <summary>Assembles all parts into the final storage path, finalizes DB records, and cleans up temp files.</summary>
        Task<IFeedback> CompleteChunkedUpload(long versionId, string finalHash = null);

        /// <summary>Returns how many parts have been received for an active session.</summary>
        Task<IFeedback> GetChunkStatus(long versionId);

        /// <summary>
        /// Cancels an active chunk session: removes it from the in-memory cache and
        /// deletes the temp chunk directory. DB chunk records are left orphaned for
        /// offline cleanup. Returns success even when no session exists (idempotent).
        /// </summary>
        Task<IFeedback> AbortChunkedUpload(long versionId);
    }
}
