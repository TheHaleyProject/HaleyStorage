
namespace Haley.Abstractions {
    // Lightweight path descriptor — does NOT inherit IVaultObject.
    // Entity identity (Cuid as Guid, Guid, Key, etc.) comes through IVaultStorable/IVaultObject.
    // Route identity (Id, Cuid as string, Name) is declared on the concrete route interfaces.
    public interface IVaultRoute {
        string StorageRef { get; set; } //Other name for Path
    }
}
