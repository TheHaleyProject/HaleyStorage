namespace Haley.Models {
    /// <summary>Runtime visibility for one module adapter inside a running storage process.</summary>
    public sealed class StorageModuleRuntimeStatus {
        public string Client { get; set; } = string.Empty;
        public string Module { get; set; } = string.Empty;
        public string ModuleCuid { get; set; } = string.Empty;
        public string DatabaseName { get; set; } = string.Empty;
        public bool Registered { get; set; }
        public bool AdapterLoaded { get; set; }
        public bool Hydrated { get; set; }
        public string Platform { get; set; } = string.Empty;
        public string PathSeparator { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
