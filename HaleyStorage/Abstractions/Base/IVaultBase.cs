
using Haley.Enums;
using System;

namespace Haley.Abstractions {
    public interface IVaultBase : IIdentityBase {
        Guid Cuid { get; } //Collision resistance Unique Identifier 
        string DisplayName { get; }
        bool TryValidate(out string message);
        IVaultBase UpdateCUID(params string[] parentNames);
        IVaultBase SetCuid(string guid);
        IVaultBase SetCuid(Guid guid);
    }
}
