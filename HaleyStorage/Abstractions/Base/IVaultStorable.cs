using System;

namespace Haley.Abstractions {
    /// <summary>
    /// Extends <see cref="IVaultBase"/> with the storage-system name (<see cref="StorageName"/>)
    /// and schema version. Implemented by <c>VaultProfile</c> and anything that participates
    /// directly in path generation (workspaces, registration profiles).
    /// Clients and modules use <see cref="IVaultBase"/> only — they do not own a storage name
    /// at the interface level.
    /// </summary>
    public interface IVaultStorable : IVaultBase {
        string StorageName { get; set; }
        int Version { get; set; }
    }
}
