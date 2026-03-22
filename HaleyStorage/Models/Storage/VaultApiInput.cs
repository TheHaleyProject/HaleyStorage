using Microsoft.AspNetCore.Mvc;

namespace Haley.Models {

    public class VaultApiInput {

        [FromQuery(Name = "c")]
        public string? ClientName { get; set; } 
        [FromQuery(Name = "m")]
        public string? ModuleName { get; set; } 
        [FromQuery(Name = "w")]
        public string? WorkSpaceName { get; set; } 
        [FromQuery(Name = "dn")]
        public string? DirectoryName { get; set; }
        [FromQuery(Name = "did")]
        public long? DirectoryParent { get; set; }
        [FromQuery(Name = "duid")]
        public string? ParentCuid { get; set; }
        public VaultApiInput() {
        }
    }
}