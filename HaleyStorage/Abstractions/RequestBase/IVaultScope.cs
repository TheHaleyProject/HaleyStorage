using Haley.Enums;
using Haley.Models;
using System.Collections.Generic;

namespace Haley.Abstractions {
    public interface IVaultScope {
        //The upload or the download request is scoped to this specific request.
        IVaultBase Client { get; }
        IVaultBase Module { get; }
        IVaultBase Workspace { get; }
        IVaultFolderRoute Folder { get; }
    }
}
