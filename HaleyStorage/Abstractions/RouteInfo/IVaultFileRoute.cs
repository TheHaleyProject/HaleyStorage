
using System;

namespace Haley.Abstractions {
    public interface IVaultFileRoute : IVaultRoute {
        long Id { get; }
        string Cuid { get; set; }   // stored as compact-N string
        string DisplayName { get; }
        int Version { get; set; }
        long Size { get; set; }
        string StorageName { get; set; }
        /// <summary>
        /// Temporary reference on the staging provider (e.g. B2 object key).
        /// Populated only while the file is in staging (flags bit 4 set, bit 8 not yet set).
        /// Cleared by the background sync worker once the file is promoted to primary storage.
        /// </summary>
        string StagingRef { get; set; }
        IVaultFileRoute SetId(long id);
        IVaultFileRoute SetDisplayName(string name);
        IVaultFileRoute SetCuid(Guid cuid);
        IVaultFileRoute SetCuid(string cuid);
    }
}
