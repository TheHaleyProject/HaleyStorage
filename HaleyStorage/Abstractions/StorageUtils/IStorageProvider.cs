using Haley.Enums;
using Haley.Models;
using System.IO;
using System.Threading.Tasks;
using System;

namespace Haley.Abstractions {
    /// <summary>
    /// Pure byte-storage abstraction. A provider knows only how to store, retrieve,
    /// and delete bytes at a given storage reference (path or object key).
    ///
    /// The provider owns ALL implementation-specific I/O details including:
    ///   - Ensuring storage prerequisites exist (e.g., directories for FileSystem)
    ///   - Conflict resolution (Skip / ReturnError / Replace / Revise)
    ///   - Extension-search fallback during reads (FileSystem: scan dir; B2/Azure: list prefix)
    ///
    /// The coordinator owns:
    ///   - Path / key generation (sharding, CUID)
    ///   - Security validation (path is within storage root)
    ///   - Indexer (DB) operations
    ///   - Format policy
    /// </summary>
    public interface IStorageProvider {
        string Key { get; set; }

        /// <summary>
        /// Writes the stream to the given storage reference.
        /// The provider handles conflict resolution and any necessary prerequisites.
        /// </summary>
        Task<ProviderWriteResult> WriteAsync(string storagePath, Stream dataStream, int bufferSize, ExistConflictResolveMode conflictMode);

        /// <summary>
        /// Opens a read stream for the given storage reference.
        /// When <paramref name="autoSearchExtension"/> is true and the reference has no extension,
        /// the provider may search for a matching entry by name.
        /// </summary>
        Task<ProviderReadResult> ReadAsync(string storagePath, bool autoSearchExtension = true, StringComparison nameComparison = StringComparison.OrdinalIgnoreCase);

        /// <summary>Deletes the content at the given storage reference.</summary>
        Task<bool> DeleteAsync(string storagePath);

        /// <summary>Returns true if content exists at the given storage reference.</summary>
        bool Exists(string storagePath);

        /// <summary>Returns the size in bytes of the content at the given storage reference.</summary>
        long GetSize(string storagePath);

        /// <summary>
        /// Builds a provider-specific storage reference (relative path segment or object key)
        /// for the given logical storage identity.
        ///
        /// The coordinator generates the logical identity (numeric ID or GUID string).
        /// The provider decides the format:
        ///   - FileSystem: applies directory sharding (e.g. 00/00/0001234.mp4)
        ///   - Cloud (B2, S3, Azure): returns a flat key (e.g. 0001234.mp4)
        ///
        /// This is the seam that decouples "what the file is" from "how the provider stores it".
        /// </summary>
        string BuildStorageRef(string logicalId, string extension, Func<bool, (int length, int depth)> splitProvider, string suffix);

        /// <summary>
        /// Combines a workspace base path (or key prefix) with a file storage reference into
        /// the fully-qualified path / object key passed directly to WriteAsync / ReadAsync / DeleteAsync.
        ///
        /// The provider owns:
        ///   - Path separator (OS backslash for FileSystem, forward-slash for cloud)
        ///   - Any provider-specific prefix or suffix conventions
        ///   - Traversal-safety validation (rejects ".." segments)
        ///
        /// The coordinator owns building <paramref name="basePath"/> and <paramref name="fileRef"/> separately;
        /// this method is the single join point between them.
        /// </summary>
        string BuildFullPath(string basePath, string fileRef);

        /// <summary>
        /// Returns a time-limited access URL for the given storage reference, or <c>null</c>
        /// if this provider does not support URL-based access (e.g. local FileSystem).
        ///
        /// Cloud implementations (B2, S3, Azure) should return a pre-signed download URL.
        /// When a non-null URL is returned, callers should redirect the client to it rather
        /// than streaming bytes through the server.
        /// </summary>
        Task<string> GetAccessUrl(string storageRef, TimeSpan expiry);
    }
}
