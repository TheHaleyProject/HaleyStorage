using Haley.Abstractions;
using Haley.Utils;
using System;
using System.Collections.Generic;
using System.Text;
using Haley.Enums;
using System.Linq;
using System.Text.Json.Serialization;

namespace Haley.Models {
    /// <summary>
    /// Deserialized from the <c>Seed:sources</c> configuration section.
    /// Describes a single client/module/workspace registration entry consumed by
    /// <see cref="StorageCoordinator.RegisterFromSource"/>.
    /// </summary>
    internal class DSSRegInfo {
        [JsonPropertyName("client")]
        public string Client { get; set; }
        [JsonPropertyName("module")]
        public string Module { get; set; }
        [JsonPropertyName("password")]
        public string Password { get; set; }
        [JsonPropertyName("space")]
        public string Workspace { get; set; }
        [JsonPropertyName("control")]
        public VaultNameMode Control { get; set; }
        [JsonPropertyName("parse")]
        public VaultNameParseMode Parse { get; set; }
        [JsonPropertyName("virtual")]
        public bool IsVirtual { get; set; }
        [JsonPropertyName("case-sensitive")]
        public bool CaseSensitive { get; set; } 
    }
}
