namespace Haley.Models {
    public class VaultExtensionStats {
        public string Extension { get; set; } = string.Empty;
        public VaultStatsCounters Direct { get; set; } = new();
        public VaultStatsCounters Recursive { get; set; } = new();
    }
}
