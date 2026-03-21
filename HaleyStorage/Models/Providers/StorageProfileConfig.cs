using Haley.Abstractions;
using Haley.Enums;

namespace Haley.Models {

    public class StorageProfileConfig  {
        public string Name { get; set; }                         // e.g. "khopu-primary"
        public StorageProfileMode Mode { get; set; }
        public string StorageProviderKey { get; set; }           // e.g. "fs-main"
        public string StagingProviderKey { get; set; }         // e.g. "b2-staging"
        public string Metadata { get; set; } //Preferably in JSON format.
        public StorageProfileConfig() {
        }
    }
}