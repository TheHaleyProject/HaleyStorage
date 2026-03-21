using Microsoft.AspNetCore.Mvc;

namespace Haley.Models {

    public class ChunkInitInput : VaultApiInput {

        [FromQuery(Name = "fn")]
        public string FileName { get; set; }

        /// <summary>Size of each individual chunk in MB. Default 5.</summary>
        [FromQuery(Name = "cs")]
        public long ChunkSizeMb { get; set; } = 5;

        /// <summary>Total number of parts the file will be split into. Must be >= 1.</summary>
        [FromQuery(Name = "tp")]
        public int TotalParts { get; set; }
    }
}
