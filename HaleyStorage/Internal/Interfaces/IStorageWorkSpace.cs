using Haley.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Haley.Abstractions {
    internal interface IStorageWorkSpace : IVaultStorable {
        /// <summary>Physical path segment (FS) or key prefix (cloud) for this workspace. Populated during registration.</summary>
        string StorageRef { get; set; }
        IVaultObject Client { get; set; }
        IVaultObject Module { get; set; }
        VaultNameMode NameMode { get; set; }
        VaultNameParseMode ParseMode { get; set; }
        string DatabaseName { get; set; }
        /// <summary>
        /// Base segment for this workspace's storage area.
        /// For FS: sharded sub-directory. For cloud: key prefix within the module's bucket.
        /// Empty for virtual workspaces.
        /// </summary>
        string Base { get; set; }
        /// <summary>Optional override primary provider key. When set, takes precedence over the module's provider.</summary>
        string StorageProviderKey { get; set; }
        /// <summary>Optional override staging provider key. When set, takes precedence over the module's staging provider.</summary>
        string StagingProviderKey { get; set; }
        /// <summary>Upload routing mode override. Only effective when <see cref="StorageProviderKey"/> is set.</summary>
        VaultProfileMode ProfileMode { get; set; }
        /// <summary>
        /// The <c>dsscore.profile_info.id</c> that was used to configure this workspace's provider settings.
        /// Set during storage-profile hydration. 0 when configured via provider keys alone.
        /// </summary>
        long ProfileInfoId { get; set; }
        void Assert();
    }
}
