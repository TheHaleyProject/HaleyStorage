namespace Haley.Models {
    /// <summary>Runtime write policy resolved for one module or workspace scope.</summary>
    public sealed class StorageWritePolicyStatus {
        public string Client { get; set; } = string.Empty;
        public string Module { get; set; } = string.Empty;
        public string? Workspace { get; set; }
        public string Level { get; set; } = string.Empty;
        public bool GlobalWriteEnabled { get; set; }
        public bool? ConfiguredWrite { get; set; }
        public bool? ModuleWrite { get; set; }
        public bool? WorkspaceWrite { get; set; }
        public bool EffectiveWrite { get; set; }
    }
}
