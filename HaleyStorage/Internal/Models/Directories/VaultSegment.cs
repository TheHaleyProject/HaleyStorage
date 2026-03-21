using Haley.Abstractions;
using Haley.Enums;
using System;

namespace Haley.Models {
    /// <summary>
    /// Common base class for the middle and leaf tiers of the vault hierarchy
    /// (<see cref="VaultModule"/> and <see cref="VaultWorkSpace"/>).
    /// Extends <see cref="VaultObject"/> (identity) and implements <see cref="IStorageProfile"/>
    /// (mutable provider routing), eliminating duplicated profile fields across both classes.
    /// </summary>
    internal class VaultSegment : VaultObject, IStorageProfile {
        public string DatabaseName { get; set; }
        public string StorageProfileName { get; set; }
        /// <summary>Key of the primary <see cref="IStorageProvider"/>. Empty means the default provider.</summary>
        public string StorageProviderKey { get; set; }
        /// <summary>Key of the staging <see cref="IStorageProvider"/>. Empty or null disables staging.</summary>
        public string StagingProviderKey { get; set; }
        /// <summary>
        /// Determines where new uploads are first written.
        /// DirectSave (default): straight to primary provider.
        /// StageAndMove / StageAndRetainCopy: first to staging provider; background worker promotes to primary.
        /// </summary>
        public VaultProfileMode ProfileMode { get; set; } = VaultProfileMode.DirectSave;
        /// <summary>
        /// The dsscore.profile_info.id that backs the current provider configuration.
        /// 0 when configured via provider keys alone (no DB-backed profile_info link).
        /// Written to version_info.profile_info_id so files can always be resolved via their
        /// original profile rather than the current configuration.
        /// </summary>
        public long ProfileInfoId { get; set; } = 0;

        protected VaultSegment(string displayName) : base(displayName) { }
    }
}
