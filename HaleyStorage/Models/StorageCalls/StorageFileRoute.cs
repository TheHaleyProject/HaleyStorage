using Haley.Abstractions;
using System;

namespace Haley.Models {
    /// <summary>
    /// Concrete file route carrying version, size, save-as name, lifecycle flags, optional hash,
    /// and the staging provider path. Used by the coordinator to pass file metadata between
    /// the path-resolution pipeline and the CRUD methods.
    /// </summary>
    public class StorageFileRoute : StorageRoute, IVaultFileRoute {
        public int Version { get; set; } = 0;
        public long Size { get; set; } = 0;
        public string SaveAsName { get; set; }
        /// <summary>Bitwise lifecycle flags stored in version_info.flags (0=None, 1=ChunkedMode, 4=InStaging, 8=InStorage, 64=Completed, etc.).</summary>
        public int Flags { get; set; } = 0;
        /// <summary>SHA-256 hash of the final assembled file. Null until computed.</summary>
        public string Hash { get; set; }
        /// <summary>
        /// Object key or path on the staging provider (e.g. B2 bucket key).
        /// Non-null only while flags bit 4 (InStaging) is set and bit 8 (InStorage) is not.
        /// </summary>
        public string StagingPath { get; set; }

        public IVaultFileRoute SetId(long id) { Id = id; return this; }
        public IVaultFileRoute SetName(string name) { Name = name; return this; }
        public IVaultFileRoute SetCuid(Guid cuid) { Cuid = cuid.ToString("N"); return this; }
        public IVaultFileRoute SetCuid(string cuid) { Cuid = cuid; return this; }

        public StorageFileRoute() { }
        public StorageFileRoute(string name, string path) : base(name, path) { }
    }
}
