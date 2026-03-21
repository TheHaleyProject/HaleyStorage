using Microsoft.AspNetCore.Mvc;

namespace Haley.Models {

    public class VaultApiInput {

        [FromQuery(Name = "c")]
        public string? ClientName { get; set; } //Sometimes we dont' want any root dir to be specified. We direclty start uploading.
        [FromQuery(Name = "m")]
        public string? ModuleName { get; set; } //Sometimes we dont' want any root dir to be specified. We direclty start uploading.
        [FromQuery(Name = "w")]
        public string? WorkSpaceName { get; set; } //Sometimes we dont' want any root dir to be specified. We direclty start uploading.
        [FromQuery(Name = "d")]
        public string? DirectoryName { get; set; } //Sometimes we dont' want any root dir to be specified. We direclty start uploading.
        [FromQuery(Name = "dp")]
        public long? DirectoryParent { get; set; } //Sometimes we dont' want any root dir to be specified. We direclty start uploading.
        public VaultApiInput() {
        }
    }
}