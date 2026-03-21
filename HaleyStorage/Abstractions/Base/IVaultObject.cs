namespace Haley.Abstractions {
    /// <obsolete>Removed. Client and module use <see cref="IVaultBase"/>; workspace uses <see cref="IVaultStorable"/>.</obsolete>
    [System.Obsolete("IVaultObject is removed. Use IVaultBase or IVaultStorable directly.")]
    public interface IVaultObject : IVaultStorable { }
}
