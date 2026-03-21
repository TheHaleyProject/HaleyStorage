using Haley.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Haley.Abstractions {
    internal interface IStorageProfile  {
        string DatabaseName { get; set; }
        string StorageProfileName { get; set; }
        string StorageProviderKey { get; set; }
        string StagingProviderKey { get; set; }
        /// <summary>Upload routing mode for this module (DirectSave / StageAndMove / StageAndRetainCopy).</summary>
        VaultProfileMode ProfileMode { get; set; }
        /// <summary>
        /// The <c>dsscore.profile_info.id</c> that backs the current provider configuration.
        /// 0 when configured via provider keys alone (no DB-backed profile_info link).
        /// </summary>
        long ProfileInfoId { get; set; }
    }
}
