namespace Haley.Models {
    /// <summary>
    /// Merged into <see cref="VaultApiInput"/> (dp / pc fields).
    /// Kept as an empty alias so any stale references still compile.
    /// </summary>
    [System.Obsolete("Use VaultApiInput directly. fid → dp, fuid → pc.")]
    public class VaultFolderApiInput : VaultApiInput { }
}
