using Haley.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Haley.Abstractions {
    public interface IVaultModule : IVaultObject {
        IVaultObject Client { get; set; }
        string DatabaseName { get; set; }
        string StorageProfileName { get; set; }
        string StorageProviderKey { get; set; }
        string StagingProviderKey { get; set; }
        /// <summary>Upload routing mode for this module (DirectSave / StageAndMove / StageAndRetainCopy).</summary>
        StorageProfileMode ProfileMode { get; set; }
        /// <summary>
        /// The <c>dsscore.profile_info.id</c> that backs the current provider configuration.
        /// 0 when configured via provider keys alone (no DB-backed profile_info link).
        /// </summary>
        long ProfileInfoId { get; set; }
    }
}
