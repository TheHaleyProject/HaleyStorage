using Haley.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Haley.Abstractions {
    /// <summary>
    /// Internal contract for a workspace — the base storage area in the hierarchy.
    /// Extends <see cref="IVaultObject"/> (identity), <see cref="IVaultRoute"/> (physical StorageRef path),
    /// and <see cref="IStorageProfile"/> (mutable provider routing).
    /// <see cref="NameMode"/> and <see cref="ParseMode"/> are fixed at workspace creation.
    /// </summary>
    internal interface IVaultWorkSpace : IVaultObject, IVaultRoute, IStorageProfile {
        IVaultObject Client { get; set; }
        IVaultObject Module { get; set; }
        bool IsVirtual { get; set; }
        /// <summary>Defines whether stored files use numeric IDs or compact-N GUIDs. Fixed at creation.</summary>
        VaultNameMode NameMode { get; set; }
        /// <summary>Defines whether file identifiers are auto-generated or parsed from caller input. Fixed at creation.</summary>
        VaultNameParseMode ParseMode { get; set; }
        string Base { get; set; }
        void Assert();
    }
}
