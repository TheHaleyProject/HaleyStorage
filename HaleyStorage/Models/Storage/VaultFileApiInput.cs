using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace Haley.Models {

    public class VaultFileApiInput : VaultApiInput {

        [FromQuery(Name = "tn")] //tn is target name.
        public string? RequestedName { get; set; }
        [FromQuery(Name = "pn")]
        public string? SanitizedName { get; set; } //pn is processed name.
        [FromQuery(Name = "uid")]
        public string? Cuid { get; set; }
        [FromQuery(Name = "ruid")]
        public string? RootCuid { get; set; }
        public VaultFileApiInput() {
        }
    }
}
