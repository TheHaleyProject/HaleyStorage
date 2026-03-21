using Haley.Enums;
using Haley.Models;
using System.Collections.Generic;

namespace Haley.Abstractions {
    public interface IVaultReadRequest {
        string CallID { get;  } //Needed for tracking purpose
        bool GenerateCallId();
        IVaultScope Scope { get; }
        string OverrideRef { get; }
        string RequestedName { get;  } //This could be like "a32fbc213..." but override ref could be like "a3/2f/bc/..."
        bool ReadOnlyMode { get; }
        IVaultReadRequest SetMode(bool readOnly);
        IVaultReadRequest SetRequestedName(string name);
        IVaultReadRequest SetOverrideRef(string storageRef);
        IVaultReadRequest SetFolder(IVaultFolderRoute folder);
        IVaultReadRequest SetComponent(IVaultObject input, VaultObjectType type);
    }
}
