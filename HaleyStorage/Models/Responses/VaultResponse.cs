using Haley.Abstractions;

namespace Haley.Models {

    public class VaultResponse : Feedback, IVaultResponse {

        //Object can be a folder object or a file object.
        public string SavedName { get; set; } //We are not going to show this anymore.. not required for user to know
        public string OriginalName { get; set; }
        public long Size { get; set; }
        public string SizeHR { get; set; }
        public bool PhysicalObjectExists { get; set; } = false;
        /// <summary>Document-level CUID (ruid) — stable across all versions. Use this as ruid for permanent links.</summary>
        public string? RootCuid { get; set; }
        /// <summary>Version-level CUID (uid) — identifies this specific version.</summary>
        public string? VersionCuid { get; set; }

        public VaultResponse() {
        }
    }
}