namespace Haley.Enums {

    /// <summary>
    /// Bitmask tracking the lifecycle state of a stored file version.
    /// Multiple flags can be active simultaneously.
    /// </summary>
    [Flags]
    public enum VersionFlags {
        /// <summary>No flags set.</summary>
        None = 0,
        /// <summary>File is being uploaded via the chunked-upload protocol.</summary>
        ChunkedMode = 1,
        /// <summary>Chunks have been written to the temporary chunk staging area.</summary>
        ChunkArea = 2,
        /// <summary>File has been written to the (optional) staging provider.</summary>
        InStaging = 4,
        /// <summary>File has been written to the primary storage area.</summary>
        InStorage = 8,
        /// <summary>Temporary chunk files have been deleted after assembly.</summary>
        ChunksDeleted = 16,
        /// <summary>The staging copy has been deleted after promotion (StageAndMove mode).</summary>
        StagingDeleted = 32,
        /// <summary>The upload process has completed successfully.</summary>
        Completed = 64,
        /// <summary>File was originally on an external/staging store and has been pulled to local disk.</summary>
        SyncedToInternal = 128,
        /// <summary>DB record reserved but no content has been written yet (ticket/placeholder).</summary>
        Placeholder = 256,
    }
}
