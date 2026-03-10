using Haley.Abstractions;
using System;

namespace Haley.Models {
    public class StorageFileRoute : StorageRoute, IVaultFileRoute {
        public int Version { get; set; } = 0;
        public long Size { get; set; } = 0;
        public string SaveAsName { get; set; }
        /// <summary>Bitwise lifecycle flags stored in version_info.flags (0=None, 1=ChunkedMode, 8=InStorage, 64=Completed, etc.).</summary>
        public int Flags { get; set; } = 0;
        /// <summary>SHA-256 hash of the final assembled file. Null until computed.</summary>
        public string Hash { get; set; }

        public IVaultFileRoute SetId(long id) { Id = id; return this; }
        public IVaultFileRoute SetName(string name) { Name = name; return this; }
        public IVaultFileRoute SetCuid(Guid cuid) { Cuid = cuid.ToString("N"); return this; }
        public IVaultFileRoute SetCuid(string cuid) { Cuid = cuid; return this; }

        public StorageFileRoute() { }
        public StorageFileRoute(string name, string path) : base(name, path) { }
    }
}
