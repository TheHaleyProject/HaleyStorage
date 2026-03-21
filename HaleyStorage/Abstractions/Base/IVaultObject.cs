
using Haley.Enums;
using System;

namespace Haley.Abstractions {
    public interface IVaultObject : IIdentityBase {
        Guid Cuid { get; } //Collision resistance Unique Identifier 
        string DisplayName { get; }
        bool TryValidate(out string message);
        IVaultObject UpdateCUID(params string[] parentNames);
        IVaultObject SetCuid(string guid);
        IVaultObject SetCuid(Guid guid);
    }
}
