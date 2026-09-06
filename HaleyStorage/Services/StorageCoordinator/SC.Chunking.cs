using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

namespace Haley.Services {

    /// <summary>
    /// FileSystem-backed chunk lifecycle. Protocol adapters may use either numbered parts or
    /// sequential appends, while storage owns durability and final assembly.
    /// </summary>
    public partial class StorageCoordinator : IStorageCoordinator {

        const string SessionMetaFile = "_session.json";
        const string PendingPartFile = "_pending";
        const string AssembledFile = "_assembled";
        const int CopyBufferSize = 256 * 1024;

        readonly ConcurrentDictionary<long, ChunkSessionCache> _chunkSessions = new();
        readonly ConcurrentDictionary<string, long> _chunkSessionsByCuid = new(StringComparer.OrdinalIgnoreCase);

        string ChunkRoot => Path.Combine(BasePath, "_chunks");

        public async Task<IFeedback<ChunkUploadSessionInfo>> InitiateChunkedUpload(
            IVaultFileWriteRequest request,
            long chunkSizeMb,
            int totalParts,
            long? totalBytes = null,
            CancellationToken cancellationToken = default) {

            var fb = new Feedback<ChunkUploadSessionInfo>();
            try {
                if (!WriteMode) return fb.SetMessage("Application is in Read-Only mode.");
                if (request == null) return fb.SetMessage("Request cannot be null.");
                if (request.ReadOnlyMode) return fb.SetMessage("Request is in Read-Only mode.");
                if (string.IsNullOrWhiteSpace(request.OriginalName))
                    return fb.SetMessage("FileName is required for chunked upload initiation.");
                if (chunkSizeMb < 1) return fb.SetMessage("ChunkSizeMb must be >= 1.");
                if (totalParts < 1) return fb.SetMessage("TotalParts must be >= 1.");
                if (totalBytes.HasValue && totalBytes.Value < 1)
                    return fb.SetMessage("TotalBytes must be greater than zero when supplied.");

                var chunkSizeBytes = checked(chunkSizeMb * 1024L * 1024L);
                if (totalBytes.HasValue) {
                    var expectedParts = checked((int)(((totalBytes.Value - 1) / chunkSizeBytes) + 1));
                    if (expectedParts != totalParts)
                        return fb.SetMessage($"TotalParts must be {expectedParts} for {totalBytes.Value} bytes at {chunkSizeMb} MB per part.");
                }

                PrepareRequestContext(request);
                if (ResolveProvider(request) is not FileSystemStorageProvider)
                    return fb.SetMessage("Chunked uploads currently support only FileSystemStorageProvider.");
                if (ResolveProfileMode(request) != VaultProfileMode.DirectSave)
                    return fb.SetMessage("Chunked uploads currently support only DirectSave profiles.");

                request.GenerateCallId();
                ProcessAndBuildStoragePath(request, true);

                if (request.File is not StorageFileRoute file || file.Id < 1 || string.IsNullOrWhiteSpace(file.Cuid))
                    return fb.SetMessage("Failed to register document record. Check indexer configuration.");

                var finalPath = Path.GetFullPath(request.OverrideRef);
                if (!IsWithinStorageRoot(finalPath))
                    return fb.SetMessage("The generated chunk destination is outside the storage root.");

                var versionCuid = file.Cuid;
                var chunkDir = Path.Combine(ChunkRoot, versionCuid);
                Directory.CreateDirectory(chunkDir);

                var now = DateTimeOffset.UtcNow;
                var meta = new ChunkSessionMeta {
                    VersionId = file.Id,
                    VersionCuid = versionCuid,
                    RootCuid = file.RootCuid ?? string.Empty,
                    FinalPath = finalPath,
                    StorageRef = file.StorageRef ?? string.Empty,
                    StorageName = file.StorageName ?? string.Empty,
                    ModuleCuid = request.Scope?.Module?.Cuid.ToString("N") ?? string.Empty,
                    ProfileInfoId = file.ProfileInfoId,
                    ChunkSizeMb = chunkSizeMb,
                    TotalParts = totalParts,
                    TotalLength = totalBytes ?? checked(chunkSizeBytes * totalParts),
                    HasExactLength = totalBytes.HasValue,
                    CreatedUtc = now,
                    LastActivityUtc = now,
                    Lifecycle = "active"
                };

                if (Indexer != null && !string.IsNullOrWhiteSpace(meta.ModuleCuid)) {
                    var chunkResult = await Indexer.UpsertChunkInfo(
                        meta.ModuleCuid,
                        meta.VersionId,
                        chunkSizeMb,
                        totalParts,
                        versionCuid,
                        chunkDir,
                        isCompleted: false,
                        callId: request.CallID).ConfigureAwait(false);

                    if (!chunkResult.Status) {
                        if (Indexer is MariaDBIndexing failedIndexer)
                            failedIndexer.FinalizeTransaction(request.CallID, false);
                        TryDeleteDirectory(chunkDir);
                        return fb.SetMessage($"Failed to create chunk session in DB: {chunkResult.Message}");
                    }
                }

                try {
                    await WriteMetadataAsync(chunkDir, meta, cancellationToken).ConfigureAwait(false);
                } catch {
                    if (Indexer is MariaDBIndexing failedIndexer)
                        failedIndexer.FinalizeTransaction(request.CallID, false);
                    TryDeleteDirectory(chunkDir);
                    throw;
                }

                if (Indexer is MariaDBIndexing indexer)
                    indexer.FinalizeTransaction(request.CallID, true);

                var session = new ChunkSessionCache(chunkDir, meta);
                _chunkSessions[meta.VersionId] = session;
                _chunkSessionsByCuid[meta.VersionCuid] = meta.VersionId;

                return fb.SetStatus(true).SetResult(ToSessionInfo(meta));
            } catch (Exception ex) {
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<ChunkUploadSessionInfo>> InitiateChunkedUploadForPlaceholder(
            IVaultReadRequest request,
            string versionCuid,
            long chunkSizeMb,
            int totalParts,
            long? totalBytes = null,
            CancellationToken cancellationToken = default) {

            var fb = new Feedback<ChunkUploadSessionInfo>();
            try {
                if (!WriteMode) return fb.SetMessage("Application is in Read-Only mode.");
                if (request == null) return fb.SetMessage("Request cannot be null.");
                if (request.ReadOnlyMode) return fb.SetMessage("Request is in Read-Only mode.");
                if (Indexer == null) return fb.SetMessage("An indexer is required to create a chunk session.");
                if (!Guid.TryParse(versionCuid, out var parsedCuid))
                    return fb.SetMessage("A valid placeholder version CUID is required.");
                if (chunkSizeMb < 1) return fb.SetMessage("ChunkSizeMb must be >= 1.");
                if (totalParts < 1) return fb.SetMessage("TotalParts must be >= 1.");
                if (totalBytes.HasValue && totalBytes.Value < 1)
                    return fb.SetMessage("TotalBytes must be greater than zero when supplied.");

                var chunkSizeBytes = checked(chunkSizeMb * 1024L * 1024L);
                if (totalBytes.HasValue) {
                    var expectedParts = checked((int)(((totalBytes.Value - 1) / chunkSizeBytes) + 1));
                    if (expectedParts != totalParts)
                        return fb.SetMessage($"TotalParts must be {expectedParts} for {totalBytes.Value} bytes at {chunkSizeMb} MB per part.");
                }

                PrepareRequestContext(request);
                var moduleCuid = StorageUtils.GenerateCuid(request, VaultObjectType.Module);
                var normalizedCuid = parsedCuid.ToString("N");
                var existing = await Indexer.GetDocVersionInfo(moduleCuid, normalizedCuid).ConfigureAwait(false);
                if (existing?.Status != true || existing.Result is not Dictionary<string, object> dic || dic.Count < 1)
                    return fb.SetMessage($"Placeholder version {versionCuid} was not found.");

                long ReadLong(string key) => dic.TryGetValue(key, out var value) && long.TryParse(value?.ToString(), out var parsed) ? parsed : 0L;
                int ReadInt(string key) => dic.TryGetValue(key, out var value) && int.TryParse(value?.ToString(), out var parsed) ? parsed : 0;
                string ReadString(string key) => dic.TryGetValue(key, out var value) ? value?.ToString() ?? string.Empty : string.Empty;

                var versionId = ReadLong("id");
                var flags = ReadInt("flags");
                if (versionId < 1)
                    return fb.SetMessage($"Unable to resolve placeholder version id for {versionCuid}.");
                if ((flags & (int)VersionFlags.Placeholder) == 0)
                    return fb.SetMessage($"Ticket {versionCuid} is not an open placeholder.");

                var storagePath = ReadString("path");
                if (string.IsNullOrWhiteSpace(storagePath))
                    return fb.SetMessage($"Placeholder {versionCuid} does not have a storage path.");

                var file = new StorageFileRoute(ReadString("saveas_name"), storagePath) {
                    Id = versionId,
                    StorageName = ReadString("saveas_name"),
                    StorageRef = storagePath,
                    StagingRef = ReadString("staging_path"),
                    Flags = flags,
                    ProfileInfoId = ReadLong("profile_info_id"),
                    RootCuid = ReadString("ruid")
                };
                file.SetCuid(normalizedCuid);

                var fileRequest = new StorageReadFileRequest {
                    Scope = request.Scope,
                    Actor = request.Actor,
                    ReadOnlyMode = request.ReadOnlyMode
                };
                fileRequest.SetFile(file);

                var provider = ResolveProvider(fileRequest);
                if (provider is not FileSystemStorageProvider)
                    return fb.SetMessage("Chunked placeholder uploads currently support only FileSystemStorageProvider.");
                if (ResolveProfileMode(fileRequest) != VaultProfileMode.DirectSave)
                    return fb.SetMessage("Chunked placeholder uploads currently support only DirectSave profiles.");

                var finalPath = Path.IsPathRooted(storagePath)
                    ? Path.GetFullPath(storagePath)
                    : Path.GetFullPath(Path.Combine(FetchWorkspaceBasePath(fileRequest, provider), storagePath));
                if (!IsWithinStorageRoot(finalPath))
                    return fb.SetMessage("The placeholder chunk destination is outside the storage root.");

                var chunkDir = Path.Combine(ChunkRoot, normalizedCuid);
                if (Directory.Exists(chunkDir)) {
                    var existingSession = await TryRehydrateChunkSession(normalizedCuid, cancellationToken).ConfigureAwait(false);
                    if (existingSession != null && string.Equals(existingSession.State, "active", StringComparison.OrdinalIgnoreCase))
                        return fb.SetStatus(true).SetResult(new ChunkUploadSessionInfo {
                            VersionId = existingSession.VersionId,
                            VersionCuid = existingSession.VersionCuid,
                            RootCuid = file.RootCuid ?? string.Empty,
                            ChunkSizeBytes = chunkSizeBytes,
                            TotalParts = existingSession.TotalParts,
                            TotalBytes = existingSession.TotalBytes ?? 0
                        });

                    if (Directory.EnumerateFileSystemEntries(chunkDir).Any())
                        return fb.SetMessage($"Chunk directory already exists for placeholder {versionCuid}.");
                }

                Directory.CreateDirectory(chunkDir);

                var now = DateTimeOffset.UtcNow;
                var meta = new ChunkSessionMeta {
                    VersionId = versionId,
                    VersionCuid = normalizedCuid,
                    RootCuid = file.RootCuid ?? string.Empty,
                    FinalPath = finalPath,
                    StorageRef = file.StorageRef ?? string.Empty,
                    StorageName = file.StorageName ?? string.Empty,
                    ModuleCuid = moduleCuid,
                    ProfileInfoId = file.ProfileInfoId,
                    ChunkSizeMb = chunkSizeMb,
                    TotalParts = totalParts,
                    TotalLength = totalBytes ?? checked(chunkSizeBytes * totalParts),
                    HasExactLength = totalBytes.HasValue,
                    CreatedUtc = now,
                    LastActivityUtc = now,
                    Lifecycle = "active"
                };

                var chunkResult = await Indexer.UpsertChunkInfo(
                    meta.ModuleCuid,
                    meta.VersionId,
                    chunkSizeMb,
                    totalParts,
                    normalizedCuid,
                    chunkDir,
                    isCompleted: false,
                    callId: Guid.NewGuid().ToString("N")).ConfigureAwait(false);

                if (!chunkResult.Status) {
                    TryDeleteDirectory(chunkDir);
                    return fb.SetMessage($"Failed to create chunk session in DB: {chunkResult.Message}");
                }

                try {
                    await WriteMetadataAsync(chunkDir, meta, cancellationToken).ConfigureAwait(false);
                } catch {
                    TryDeleteDirectory(chunkDir);
                    throw;
                }

                var session = new ChunkSessionCache(chunkDir, meta);
                _chunkSessions[meta.VersionId] = session;
                _chunkSessionsByCuid[meta.VersionCuid] = meta.VersionId;

                return fb.SetStatus(true).SetResult(ToSessionInfo(meta));
            } catch (Exception ex) {
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<ChunkPartResult>> UploadChunkPart(
            long versionId,
            int partNumber,
            Stream chunkStream,
            string hash = null,
            CancellationToken cancellationToken = default) {

            var fb = new Feedback<ChunkPartResult>();
            if (!_chunkSessions.TryGetValue(versionId, out var session))
                return fb.SetMessage($"No active chunk session for versionId {versionId}. Rehydrate the session first.");
            if (partNumber < 1 || partNumber > session.Meta.TotalParts)
                return fb.SetMessage($"partNumber must be between 1 and {session.Meta.TotalParts}.");
            if (chunkStream == null || !chunkStream.CanRead)
                return fb.SetMessage("Chunk stream is null or unreadable.");
            if (!session.TryBeginWrite(out var stateMessage))
                return fb.SetMessage(stateMessage);

            var partGate = session.PartGates.GetOrAdd(partNumber, _ => new SemaphoreSlim(1, 1));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Cancellation.Token);
            try {
                await partGate.WaitAsync(linked.Token).ConfigureAwait(false);
                try {
                    var result = await WritePartCoreAsync(session, partNumber, chunkStream, hash, linked.Token)
                        .ConfigureAwait(false);
                    if (!result.Status) return fb.SetMessage(result.Message);
                    return fb.SetStatus(true).SetMessage(result.Message).SetResult(result.Result);
                } finally {
                    partGate.Release();
                }
            } catch (OperationCanceledException) {
                return fb.SetMessage("Chunk upload was cancelled.");
            } catch (Exception ex) {
                return fb.SetMessage(ex.Message);
            } finally {
                session.EndWrite();
            }
        }

        public async Task<IFeedback<ChunkAppendResult>> AppendChunkData(
            long versionId,
            long expectedOffset,
            Stream dataStream,
            long contentLength,
            CancellationToken cancellationToken = default) {

            var fb = new Feedback<ChunkAppendResult>();
            if (!_chunkSessions.TryGetValue(versionId, out var session))
                return fb.SetMessage($"No active chunk session for versionId {versionId}. Rehydrate the session first.");
            if (!session.Meta.HasExactLength)
                return fb.SetMessage("Sequential append requires an exact total byte length.");
            if (dataStream == null || !dataStream.CanRead)
                return fb.SetMessage("Upload stream is null or unreadable.");
            if (contentLength < 0)
                return fb.SetMessage("Content length cannot be negative.");

            await session.SequentialGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!session.TryBeginWrite(out var stateMessage)) {
                session.SequentialGate.Release();
                return fb.SetMessage(stateMessage);
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Cancellation.Token);
            try {
                var currentOffset = GetSequentialOffset(session);
                if (currentOffset != expectedOffset)
                    return fb.SetMessage($"Offset mismatch. Server expects {currentOffset}, client sent {expectedOffset}.")
                        .SetResult(new ChunkAppendResult { Offset = currentOffset, TotalBytes = session.Meta.TotalLength });
                if (expectedOffset + contentLength > session.Meta.TotalLength)
                    return fb.SetMessage("The supplied byte range exceeds the declared upload length.");

                var pendingPath = Path.Combine(session.TempDir, PendingPartFile);
                var remaining = contentLength;
                var buffer = new byte[CopyBufferSize];

                while (remaining > 0) {
                    long pendingLength;
                    await using (var pending = new FileStream(
                        pendingPath,
                        FileMode.OpenOrCreate,
                        FileAccess.Write,
                        FileShare.None,
                        CopyBufferSize,
                        FileOptions.Asynchronous | FileOptions.SequentialScan)) {

                        pending.Position = pending.Length;
                        while (remaining > 0 && pending.Position < session.ChunkSizeBytes) {
                            var room = session.ChunkSizeBytes - pending.Position;
                            var toRead = (int)Math.Min(Math.Min(buffer.Length, remaining), room);
                            var read = await dataStream.ReadAsync(buffer.AsMemory(0, toRead), linked.Token).ConfigureAwait(false);
                            if (read == 0) {
                                await pending.FlushAsync(linked.Token).ConfigureAwait(false);
                                await TouchSessionAsync(session, CancellationToken.None).ConfigureAwait(false);
                                return fb.SetMessage($"Request body ended with {remaining} byte(s) still expected.");
                            }

                            await pending.WriteAsync(buffer.AsMemory(0, read), linked.Token).ConfigureAwait(false);
                            remaining -= read;
                        }

                        await pending.FlushAsync(linked.Token).ConfigureAwait(false);
                        pendingLength = pending.Length;
                    }

                    var offsetAfterWrite = GetSequentialOffset(session);
                    if (pendingLength == session.ChunkSizeBytes || offsetAfterWrite == session.Meta.TotalLength) {
                        var nextPart = CountPartFiles(session.TempDir) + 1;
                        var committed = await CommitPendingPartAsync(session, nextPart, pendingPath, linked.Token)
                            .ConfigureAwait(false);
                        if (!committed.Status) return fb.SetMessage(committed.Message);
                    }
                }

                await TouchSessionAsync(session, linked.Token).ConfigureAwait(false);
                var finalOffset = GetSequentialOffset(session);
                return fb.SetStatus(true).SetResult(new ChunkAppendResult {
                    Offset = finalOffset,
                    TotalBytes = session.Meta.TotalLength,
                    ReadyToComplete = finalOffset == session.Meta.TotalLength,
                    CompletedParts = CountPartFiles(session.TempDir)
                });
            } catch (OperationCanceledException) {
                try { await TouchSessionAsync(session, CancellationToken.None).ConfigureAwait(false); } catch { }
                return fb.SetMessage("Sequential upload was cancelled.");
            } catch (Exception ex) {
                return fb.SetMessage(ex.Message);
            } finally {
                session.EndWrite();
                session.SequentialGate.Release();
            }
        }

        public async Task<IFeedback<ChunkUploadStatus>> CompleteChunkedUpload(
            long versionId,
            string finalHash = null,
            CancellationToken cancellationToken = default) {

            var fb = new Feedback<ChunkUploadStatus>();
            if (!WriteMode) return fb.SetMessage("Application is in Read-Only mode.");
            if (!_chunkSessions.TryGetValue(versionId, out var session))
                return fb.SetMessage($"No active chunk session for versionId {versionId}.");
            if (!session.TryBeginExclusive("assembling", out var writersDrained, out var stateMessage))
                return fb.SetMessage(stateMessage);

            try {
                await writersDrained.WaitAsync(cancellationToken).ConfigureAwait(false);
                var status = BuildStatus(session);
                if (status.MissingParts.Length > 0) {
                    session.ReturnToActive();
                    return fb.SetMessage($"Cannot assemble: missing parts [{string.Join(", ", status.MissingParts)}].")
                        .SetResult(status);
                }

                var partFiles = GetPartFiles(session.TempDir);
                if (!ValidatePartGeometry(session, partFiles, out var geometryError)) {
                    session.ReturnToActive();
                    return fb.SetMessage(geometryError).SetResult(status);
                }

                var assembledPath = Path.Combine(session.TempDir, AssembledFile);
                var (totalSize, actualHash) = await AssembleAsync(partFiles, assembledPath, cancellationToken)
                    .ConfigureAwait(false);

                if (session.Meta.HasExactLength && totalSize != session.Meta.TotalLength) {
                    File.Delete(assembledPath);
                    session.ReturnToActive();
                    return fb.SetMessage($"Assembled size {totalSize} does not match declared size {session.Meta.TotalLength}.");
                }

                if (!HashMatches(finalHash, actualHash)) {
                    File.Delete(assembledPath);
                    session.ReturnToActive();
                    return fb.SetMessage("Final SHA-256 hash does not match the assembled file.");
                }

                var finalDir = Path.GetDirectoryName(session.Meta.FinalPath);
                if (!string.IsNullOrWhiteSpace(finalDir)) Directory.CreateDirectory(finalDir);
                File.Move(assembledPath, session.Meta.FinalPath, true);

                var route = BuildCompletedRoute(session.Meta, totalSize, actualHash, chunksDeleted: false);
                if (Indexer != null && !string.IsNullOrWhiteSpace(session.Meta.ModuleCuid)) {
                    var callId = Guid.NewGuid().ToString("N");
                    var transaction = Indexer.BeginTransaction(session.Meta.ModuleCuid, callId);
                    if (!transaction.Status) {
                        session.ReturnToActive();
                        return fb.SetMessage($"Unable to start chunk finalization transaction: {transaction.Message}");
                    }
                    var update = await Indexer.UpdateDocVersionInfo(session.Meta.ModuleCuid, route, callId).ConfigureAwait(false);
                    var marked = await Indexer.MarkChunkCompleted(session.Meta.ModuleCuid, versionId, callId).ConfigureAwait(false);
                    var finalized = update.Status && marked.Status;
                    if (Indexer is MariaDBIndexing indexer) indexer.FinalizeTransaction(callId, finalized);
                    if (!finalized) {
                        session.ReturnToActive();
                        return fb.SetMessage($"Assembly succeeded but DB finalization failed: {update.Message} / {marked.Message}");
                    }
                }

                session.Meta.Lifecycle = "completed";
                session.Meta.LastActivityUtc = DateTimeOffset.UtcNow;
                await WriteMetadataAsync(session.TempDir, session.Meta, cancellationToken).ConfigureAwait(false);

                var chunksDeleted = TryDeleteDirectory(session.TempDir);
                if (chunksDeleted && Indexer != null && !string.IsNullOrWhiteSpace(session.Meta.ModuleCuid)) {
                    route.Flags = (int)(VersionFlags.ChunkedMode | VersionFlags.ChunkArea | VersionFlags.InStorage | VersionFlags.ChunksDeleted | VersionFlags.Completed);
                    var cleanupCall = Guid.NewGuid().ToString("N");
                    var cleanupUpdate = await Indexer.UpdateDocVersionInfo(session.Meta.ModuleCuid, route, cleanupCall).ConfigureAwait(false);
                    if (Indexer is MariaDBIndexing cleanupIndexer)
                        cleanupIndexer.FinalizeTransaction(cleanupCall, cleanupUpdate.Status);
                }

                _chunkSessions.TryRemove(versionId, out _);
                _chunkSessionsByCuid.TryRemove(session.Meta.VersionCuid, out _);

                status = BuildCompletedStatus(session.Meta, totalSize);
                return fb.SetStatus(true)
                    .SetMessage($"Chunked upload complete. Final size: {totalSize} bytes.")
                    .SetResult(status);
            } catch (OperationCanceledException) {
                session.ReturnToActive();
                return fb.SetMessage("Chunk completion was cancelled.");
            } catch (Exception ex) {
                session.ReturnToActive();
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback> AbortChunkedUpload(long versionId, CancellationToken cancellationToken = default) {
            var fb = new Feedback();
            if (!_chunkSessions.TryGetValue(versionId, out var session))
                return fb.SetStatus(true).SetMessage("No active session found; nothing to abort.");
            if (!session.TryBeginExclusive("aborting", out var writersDrained, out var stateMessage))
                return fb.SetMessage(stateMessage);

            string abortCallId = null;
            if (Indexer != null && !string.IsNullOrWhiteSpace(session.Meta.ModuleCuid)) {
                abortCallId = Guid.NewGuid().ToString("N");
                var transaction = Indexer.BeginTransaction(session.Meta.ModuleCuid, abortCallId);
                if (!transaction.Status) {
                    session.ReturnToActive();
                    return fb.SetMessage($"Unable to start chunk abort transaction: {transaction.Message}");
                }
            }

            session.Cancellation.Cancel();
            try {
                await writersDrained.WaitAsync(cancellationToken).ConfigureAwait(false);
                if (Indexer != null && !string.IsNullOrWhiteSpace(session.Meta.ModuleCuid)) {
                    var aborted = await Indexer.AbortChunkVersion(session.Meta.ModuleCuid, versionId, abortCallId).ConfigureAwait(false);
                    if (Indexer is MariaDBIndexing indexer) indexer.FinalizeTransaction(abortCallId, aborted.Status);
                    if (!aborted.Status) {
                        _chunkSessions.TryRemove(versionId, out _);
                        _chunkSessionsByCuid.TryRemove(session.Meta.VersionCuid, out _);
                        return fb.SetMessage($"Unable to soft-delete incomplete version: {aborted.Message}");
                    }
                }

                session.Meta.Lifecycle = "cancelled";
                session.Meta.LastActivityUtc = DateTimeOffset.UtcNow;
                await WriteMetadataAsync(session.TempDir, session.Meta, cancellationToken).ConfigureAwait(false);
                TryDeleteDirectory(session.TempDir);
                _chunkSessions.TryRemove(versionId, out _);
                _chunkSessionsByCuid.TryRemove(session.Meta.VersionCuid, out _);
                return fb.SetStatus(true).SetMessage($"Chunk session {versionId} aborted.");
            } catch (Exception ex) {
                if (Indexer is MariaDBIndexing indexer && !string.IsNullOrWhiteSpace(abortCallId))
                    indexer.FinalizeTransaction(abortCallId, false);
                _chunkSessions.TryRemove(versionId, out _);
                _chunkSessionsByCuid.TryRemove(session.Meta.VersionCuid, out _);
                return fb.SetMessage(ex.Message);
            }
        }

        public Task<IFeedback<ChunkUploadStatus>> GetChunkStatus(long versionId, CancellationToken cancellationToken = default) {
            var fb = new Feedback<ChunkUploadStatus>();
            cancellationToken.ThrowIfCancellationRequested();
            if (!_chunkSessions.TryGetValue(versionId, out var session))
                return Task.FromResult<IFeedback<ChunkUploadStatus>>(fb.SetMessage("No active session found. It may have completed, expired, or never existed."));
            return Task.FromResult<IFeedback<ChunkUploadStatus>>(fb.SetStatus(true).SetResult(BuildStatus(session)));
        }

        public async Task<ChunkUploadStatus?> TryRehydrateChunkSession(string versionCuid, CancellationToken cancellationToken = default) {
            if (string.IsNullOrWhiteSpace(versionCuid) || !Guid.TryParse(versionCuid, out _)) return null;

            if (_chunkSessionsByCuid.TryGetValue(versionCuid, out var existingId)
                && _chunkSessions.TryGetValue(existingId, out var existing))
                return BuildStatus(existing);

            var chunkDir = Path.Combine(ChunkRoot, versionCuid);
            var meta = await ReadMetadataAsync(chunkDir, cancellationToken).ConfigureAwait(false);
            if (meta == null) return null;
            if (string.IsNullOrWhiteSpace(meta.FinalPath) || !IsWithinStorageRoot(Path.GetFullPath(meta.FinalPath)))
                return null;

            if (string.Equals(meta.Lifecycle, "completed", StringComparison.OrdinalIgnoreCase))
                return BuildCompletedStatus(meta, File.Exists(meta.FinalPath) ? new FileInfo(meta.FinalPath).Length : meta.TotalLength);
            if (string.Equals(meta.Lifecycle, "cancelled", StringComparison.OrdinalIgnoreCase))
                return null;

            meta.Lifecycle = "active";
            var session = new ChunkSessionCache(chunkDir, meta);
            _chunkSessions[meta.VersionId] = session;
            _chunkSessionsByCuid[meta.VersionCuid] = meta.VersionId;
            return BuildStatus(session);
        }

        public async Task<int> CleanupExpiredChunkSessions(TimeSpan inactivity, CancellationToken cancellationToken = default) {
            if (inactivity <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(inactivity));
            if (!Directory.Exists(ChunkRoot)) return 0;

            var cutoff = DateTimeOffset.UtcNow - inactivity;
            var cleaned = 0;
            foreach (var directory in Directory.EnumerateDirectories(ChunkRoot)) {
                cancellationToken.ThrowIfCancellationRequested();
                var meta = await ReadMetadataAsync(directory, cancellationToken).ConfigureAwait(false);
                if (meta == null) continue;
                if (string.Equals(meta.Lifecycle, "completed", StringComparison.OrdinalIgnoreCase)) {
                    if (TryDeleteDirectory(directory)) cleaned++;
                    continue;
                }
                if (meta.LastActivityUtc > cutoff) continue;

                await TryRehydrateChunkSession(meta.VersionCuid, cancellationToken).ConfigureAwait(false);
                var result = await AbortChunkedUpload(meta.VersionId, cancellationToken).ConfigureAwait(false);
                if (result.Status) cleaned++;
            }
            return cleaned;
        }

        async Task<IFeedback<ChunkPartResult>> WritePartCoreAsync(
            ChunkSessionCache session,
            int partNumber,
            Stream source,
            string suppliedHash,
            CancellationToken cancellationToken) {

            var fb = new Feedback<ChunkPartResult>();
            var expectedBytes = ExpectedPartBytes(session.Meta, partNumber);
            var candidate = Path.Combine(session.TempDir, $"_{partNumber:D6}.{Guid.NewGuid():N}.uploading");
            var finalPart = PartPath(session.TempDir, partNumber);

            try {
                var (size, actualHash) = await CopyWithHashAsync(source, candidate, session.ChunkSizeBytes + 1, cancellationToken)
                    .ConfigureAwait(false);
                if (size < 1) return fb.SetMessage("Chunk part cannot be empty.");
                if (expectedBytes.HasValue && size != expectedBytes.Value)
                    return fb.SetMessage($"Part {partNumber} must contain exactly {expectedBytes.Value} bytes; received {size}.");
                if (!expectedBytes.HasValue && size > session.ChunkSizeBytes)
                    return fb.SetMessage($"Part {partNumber} exceeds the configured chunk size of {session.ChunkSizeBytes} bytes.");
                if (!HashMatches(suppliedHash, actualHash))
                    return fb.SetMessage($"SHA-256 hash mismatch for part {partNumber}.");

                if (File.Exists(finalPart)) {
                    var existingSize = new FileInfo(finalPart).Length;
                    var existingHash = await ComputeHashAsync(finalPart, cancellationToken).ConfigureAwait(false);
                    if (existingSize != size || !string.Equals(existingHash, actualHash, StringComparison.OrdinalIgnoreCase))
                        return fb.SetMessage($"Part {partNumber} already exists with different content.");

                    var reconciled = await PersistPartAsync(session, partNumber, size, actualHash).ConfigureAwait(false);
                    if (!reconciled.Status) return fb.SetMessage(reconciled.Message);
                    await TouchSessionAsync(session, cancellationToken).ConfigureAwait(false);
                    return fb.SetStatus(true).SetResult(new ChunkPartResult {
                        PartNumber = partNumber,
                        PartBytes = size,
                        CommittedBytes = CountCommittedBytes(session.TempDir),
                        Hash = actualHash,
                        AlreadyPresent = true
                    });
                }

                File.Move(candidate, finalPart, false);
                candidate = string.Empty;

                var persisted = await PersistPartAsync(session, partNumber, size, actualHash).ConfigureAwait(false);
                if (!persisted.Status)
                    return fb.SetMessage($"Part is durable on disk but DB reconciliation failed: {persisted.Message}");

                await TouchSessionAsync(session, cancellationToken).ConfigureAwait(false);
                return fb.SetStatus(true)
                    .SetMessage($"Part {partNumber}/{session.Meta.TotalParts} received ({size} bytes).")
                    .SetResult(new ChunkPartResult {
                        PartNumber = partNumber,
                        PartBytes = size,
                        CommittedBytes = CountCommittedBytes(session.TempDir),
                        Hash = actualHash,
                        AlreadyPresent = false
                    });
            } finally {
                if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate)) File.Delete(candidate);
            }
        }

        async Task<IFeedback<ChunkPartResult>> CommitPendingPartAsync(
            ChunkSessionCache session,
            int partNumber,
            string pendingPath,
            CancellationToken cancellationToken) {

            var fb = new Feedback<ChunkPartResult>();
            if (!File.Exists(pendingPath)) return fb.SetMessage("The pending TUS part is unavailable.");

            var size = new FileInfo(pendingPath).Length;
            var expectedBytes = ExpectedPartBytes(session.Meta, partNumber);
            if (size < 1) return fb.SetMessage("Chunk part cannot be empty.");
            if (expectedBytes.HasValue && size != expectedBytes.Value)
                return fb.SetMessage($"Part {partNumber} must contain exactly {expectedBytes.Value} bytes; received {size}.");
            if (!expectedBytes.HasValue && size > session.ChunkSizeBytes)
                return fb.SetMessage($"Part {partNumber} exceeds the configured chunk size of {session.ChunkSizeBytes} bytes.");

            var actualHash = await ComputeHashAsync(pendingPath, cancellationToken).ConfigureAwait(false);
            var finalPart = PartPath(session.TempDir, partNumber);
            var alreadyPresent = File.Exists(finalPart);
            if (alreadyPresent) {
                var existingSize = new FileInfo(finalPart).Length;
                var existingHash = await ComputeHashAsync(finalPart, cancellationToken).ConfigureAwait(false);
                if (existingSize != size || !string.Equals(existingHash, actualHash, StringComparison.OrdinalIgnoreCase))
                    return fb.SetMessage($"Part {partNumber} already exists with different content.");
                File.Delete(pendingPath);
            } else {
                File.Move(pendingPath, finalPart, false);
            }

            var persisted = await PersistPartAsync(session, partNumber, size, actualHash).ConfigureAwait(false);
            if (!persisted.Status)
                return fb.SetMessage($"Part is durable on disk but DB reconciliation failed: {persisted.Message}");

            await TouchSessionAsync(session, cancellationToken).ConfigureAwait(false);
            return fb.SetStatus(true)
                .SetMessage($"Part {partNumber}/{session.Meta.TotalParts} received ({size} bytes).")
                .SetResult(new ChunkPartResult {
                    PartNumber = partNumber,
                    PartBytes = size,
                    CommittedBytes = CountCommittedBytes(session.TempDir),
                    Hash = actualHash,
                    AlreadyPresent = alreadyPresent
                });
        }

        async Task<IFeedback> PersistPartAsync(ChunkSessionCache session, int partNumber, long size, string hash) {
            var fb = new Feedback();
            if (Indexer == null || string.IsNullOrWhiteSpace(session.Meta.ModuleCuid))
                return fb.SetStatus(true);
            var sizeMb = (int)Math.Ceiling((double)size / (1024 * 1024));
            return await Indexer.UpsertChunkPart(session.Meta.ModuleCuid, session.Meta.VersionId, partNumber, sizeMb, hash)
                .ConfigureAwait(false);
        }

        async Task TouchSessionAsync(ChunkSessionCache session, CancellationToken cancellationToken) {
            await session.MetadataGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                session.Meta.LastActivityUtc = DateTimeOffset.UtcNow;
                await WriteMetadataAsync(session.TempDir, session.Meta, cancellationToken).ConfigureAwait(false);
            } finally {
                session.MetadataGate.Release();
            }
        }

        static bool ValidatePartGeometry(ChunkSessionCache session, IReadOnlyList<string> partFiles, out string error) {
            for (var index = 0; index < partFiles.Count; index++) {
                var partNumber = index + 1;
                var size = new FileInfo(partFiles[index]).Length;
                var expected = ExpectedPartBytes(session.Meta, partNumber);
                if (expected.HasValue && size != expected.Value) {
                    error = $"Part {partNumber} contains {size} bytes; expected {expected.Value}.";
                    return false;
                }
                if (!expected.HasValue && (size < 1 || size > session.ChunkSizeBytes)) {
                    error = $"Part {partNumber} has an invalid size of {size} bytes.";
                    return false;
                }
            }
            error = string.Empty;
            return true;
        }

        static long? ExpectedPartBytes(ChunkSessionMeta meta, int partNumber) {
            var chunkSizeBytes = checked(meta.ChunkSizeMb * 1024L * 1024L);
            if (partNumber < meta.TotalParts) return chunkSizeBytes;
            if (!meta.HasExactLength) return null;
            return meta.TotalLength - checked(chunkSizeBytes * (meta.TotalParts - 1));
        }

        static async Task<(long size, string hash)> CopyWithHashAsync(Stream source, string destination, long maximumBytes, CancellationToken cancellationToken) {
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[CopyBufferSize];
            long total = 0;
            while (true) {
                var read = await source.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                total += read;
                if (total > maximumBytes)
                    throw new InvalidDataException($"Chunk exceeds the maximum allowed size of {maximumBytes - 1} bytes.");
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            return (total, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
        }

        static async Task<(long size, string hash)> AssembleAsync(IReadOnlyList<string> parts, string destination, CancellationToken cancellationToken) {
            await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[1024 * 1024];
            long total = 0;
            foreach (var part in parts) {
                await using var input = new FileStream(part, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
                while (true) {
                    var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                    if (read == 0) break;
                    total += read;
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            return (total, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
        }

        static async Task<string> ComputeHashAsync(string path, CancellationToken cancellationToken) {
            using var sha = SHA256.Create();
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var bytes = await sha.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        static bool HashMatches(string supplied, string actual) {
            if (string.IsNullOrWhiteSpace(supplied)) return true;
            var normalized = supplied.Trim();
            if (normalized.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) normalized = normalized[7..];
            return normalized.Length == 64 && normalized.All(Uri.IsHexDigit)
                && string.Equals(normalized, actual, StringComparison.OrdinalIgnoreCase);
        }

        static StorageFileRoute BuildCompletedRoute(ChunkSessionMeta meta, long size, string hash, bool chunksDeleted) {
            var flags = VersionFlags.ChunkedMode | VersionFlags.ChunkArea | VersionFlags.InStorage | VersionFlags.Completed;
            if (chunksDeleted) flags |= VersionFlags.ChunksDeleted;
            return new StorageFileRoute {
                Id = meta.VersionId,
                Cuid = meta.VersionCuid,
                RootCuid = meta.RootCuid,
                StorageRef = meta.StorageRef,
                StorageName = meta.StorageName,
                Size = size,
                Flags = (int)flags,
                Hash = hash,
                ProfileInfoId = meta.ProfileInfoId
            };
        }

        bool IsWithinStorageRoot(string path) {
            var root = Path.GetFullPath(BasePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return path.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }

        static ChunkUploadSessionInfo ToSessionInfo(ChunkSessionMeta meta) => new() {
            VersionId = meta.VersionId,
            VersionCuid = meta.VersionCuid,
            RootCuid = meta.RootCuid,
            ChunkSizeBytes = checked(meta.ChunkSizeMb * 1024L * 1024L),
            TotalParts = meta.TotalParts,
            TotalBytes = meta.HasExactLength ? meta.TotalLength : null
        };

        static ChunkUploadStatus BuildStatus(ChunkSessionCache session) {
            var partFiles = GetPartFiles(session.TempDir);
            var received = partFiles.Select(path => int.Parse(Path.GetFileName(path))).ToHashSet();
            var missing = Enumerable.Range(1, session.Meta.TotalParts).Where(part => !received.Contains(part)).ToArray();
            return new ChunkUploadStatus {
                VersionId = session.Meta.VersionId,
                VersionCuid = session.Meta.VersionCuid,
                TotalParts = session.Meta.TotalParts,
                ReceivedParts = received.Count,
                PendingParts = missing.Length,
                MissingParts = missing,
                TotalBytes = session.Meta.HasExactLength ? session.Meta.TotalLength : null,
                CommittedBytes = CountCommittedBytes(session.TempDir),
                SequentialOffset = GetSequentialOffset(session),
                LastActivity = session.Meta.LastActivityUtc,
                State = session.Meta.Lifecycle
            };
        }

        static ChunkUploadStatus BuildCompletedStatus(ChunkSessionMeta meta, long size) => new() {
            VersionId = meta.VersionId,
            VersionCuid = meta.VersionCuid,
            TotalParts = meta.TotalParts,
            ReceivedParts = meta.TotalParts,
            PendingParts = 0,
            MissingParts = Array.Empty<int>(),
            TotalBytes = meta.HasExactLength ? meta.TotalLength : size,
            CommittedBytes = size,
            SequentialOffset = size,
            LastActivity = meta.LastActivityUtc,
            State = "completed"
        };

        static int CountPartFiles(string directory) => GetPartFiles(directory).Count;
        static long CountCommittedBytes(string directory) => GetPartFiles(directory).Sum(path => new FileInfo(path).Length);

        static long GetSequentialOffset(ChunkSessionCache session) {
            long offset = 0;
            for (var part = 1; part <= session.Meta.TotalParts; part++) {
                var path = PartPath(session.TempDir, part);
                if (!File.Exists(path)) break;
                offset += new FileInfo(path).Length;
            }
            var pending = Path.Combine(session.TempDir, PendingPartFile);
            if (File.Exists(pending)) offset += new FileInfo(pending).Length;
            return offset;
        }

        static IReadOnlyList<string> GetPartFiles(string directory) {
            if (!Directory.Exists(directory)) return Array.Empty<string>();
            return Directory.EnumerateFiles(directory)
                .Where(path => int.TryParse(Path.GetFileName(path), out _))
                .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
                .ToArray();
        }

        static string PartPath(string directory, int partNumber) => Path.Combine(directory, partNumber.ToString("D6"));

        static bool TryDeleteDirectory(string path) {
            try {
                if (Directory.Exists(path)) Directory.Delete(path, true);
                return true;
            } catch {
                return false;
            }
        }

        static async Task WriteMetadataAsync(string directory, ChunkSessionMeta meta, CancellationToken cancellationToken) {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, SessionMetaFile);
            var pending = path + ".tmp";
            var json = JsonSerializer.Serialize(meta);
            await File.WriteAllTextAsync(pending, json, cancellationToken).ConfigureAwait(false);
            File.Move(pending, path, true);
        }

        static async Task<ChunkSessionMeta?> ReadMetadataAsync(string directory, CancellationToken cancellationToken) {
            var path = Path.Combine(directory, SessionMetaFile);
            if (!File.Exists(path)) return null;
            try {
                var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                var meta = JsonSerializer.Deserialize<ChunkSessionMeta>(json);
                if (meta == null || meta.VersionId < 1 || string.IsNullOrWhiteSpace(meta.VersionCuid)) return null;
                if (meta.TotalLength < 1) meta.TotalLength = checked(meta.ChunkSizeMb * 1024L * 1024L * meta.TotalParts);
                if (meta.CreatedUtc == default) meta.CreatedUtc = File.GetCreationTimeUtc(path);
                if (meta.LastActivityUtc == default) meta.LastActivityUtc = File.GetLastWriteTimeUtc(path);
                if (string.IsNullOrWhiteSpace(meta.Lifecycle)) meta.Lifecycle = "active";
                return meta;
            } catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) {
                return null;
            }
        }

        sealed class ChunkSessionCache {
            readonly object _stateLock = new();
            int _activeWriters;
            TaskCompletionSource _writersDrained = CompletedSource();

            public ChunkSessionCache(string tempDir, ChunkSessionMeta meta) {
                TempDir = tempDir;
                Meta = meta;
            }

            public string TempDir { get; }
            public ChunkSessionMeta Meta { get; }
            public long ChunkSizeBytes => checked(Meta.ChunkSizeMb * 1024L * 1024L);
            public ConcurrentDictionary<int, SemaphoreSlim> PartGates { get; } = new();
            public SemaphoreSlim SequentialGate { get; } = new(1, 1);
            public SemaphoreSlim MetadataGate { get; } = new(1, 1);
            public CancellationTokenSource Cancellation { get; } = new();

            public bool TryBeginWrite(out string message) {
                lock (_stateLock) {
                    if (!string.Equals(Meta.Lifecycle, "active", StringComparison.OrdinalIgnoreCase)) {
                        message = $"Chunk session is {Meta.Lifecycle}.";
                        return false;
                    }
                    if (_activeWriters++ == 0)
                        _writersDrained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    message = string.Empty;
                    return true;
                }
            }

            public void EndWrite() {
                lock (_stateLock) {
                    if (_activeWriters > 0 && --_activeWriters == 0)
                        _writersDrained.TrySetResult();
                }
            }

            public bool TryBeginExclusive(string state, out Task writersDrained, out string message) {
                lock (_stateLock) {
                    if (!string.Equals(Meta.Lifecycle, "active", StringComparison.OrdinalIgnoreCase)) {
                        writersDrained = Task.CompletedTask;
                        message = $"Chunk session is {Meta.Lifecycle}.";
                        return false;
                    }
                    Meta.Lifecycle = state;
                    writersDrained = _writersDrained.Task;
                    message = string.Empty;
                    return true;
                }
            }

            public void ReturnToActive() {
                lock (_stateLock) {
                    if (!string.Equals(Meta.Lifecycle, "cancelled", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(Meta.Lifecycle, "completed", StringComparison.OrdinalIgnoreCase))
                        Meta.Lifecycle = "active";
                }
            }

            static TaskCompletionSource CompletedSource() {
                var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                source.SetResult();
                return source;
            }
        }

        sealed class ChunkSessionMeta {
            public long VersionId { get; set; }
            public string VersionCuid { get; set; } = string.Empty;
            public string RootCuid { get; set; } = string.Empty;
            public string FinalPath { get; set; } = string.Empty;
            public string StorageRef { get; set; } = string.Empty;
            public string StorageName { get; set; } = string.Empty;
            public string ModuleCuid { get; set; } = string.Empty;
            public long ProfileInfoId { get; set; }
            public long ChunkSizeMb { get; set; }
            public int TotalParts { get; set; }
            public long TotalLength { get; set; }
            public bool HasExactLength { get; set; }
            public DateTimeOffset CreatedUtc { get; set; }
            public DateTimeOffset LastActivityUtc { get; set; }
            public string Lifecycle { get; set; } = "active";
        }
    }
}
