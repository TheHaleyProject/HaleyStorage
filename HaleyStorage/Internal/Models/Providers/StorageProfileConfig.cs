using Haley.Abstractions;
using Haley.Enums;

namespace Haley.Models {

    internal class StorageProfileConfig  {
        public string Name { get; set; }                         // e.g. "khopu-primary"
        public VaultProfileMode Mode { get; set; }
        public string StorageProviderKey { get; set; }           // e.g. "fs-main"
        public string StagingProviderKey { get; set; }         // e.g. "b2-staging"
        public string Metadata { get; set; } //Preferably in JSON format.
        public StorageProfileConfig() {
        }
    }
}