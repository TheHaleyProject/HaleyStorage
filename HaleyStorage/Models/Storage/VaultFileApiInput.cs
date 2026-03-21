using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace Haley.Models {

    public class VaultFileApiInput : VaultFolderApiInput { //YES.. Read base is based on write base.

        [FromQuery(Name = "tn")]
        public string? RequestedName { get; set; }
        [FromQuery(Name = "pn")]
        public string? SanitizedName { get; set; }
        [FromQuery(Name = "vid")]
        public long? VersionId { get; set; }
        [FromQuery(Name = "uid")]
        public string? Cuid { get; set; }
        public VaultFileApiInput() {
        }
    }
}
