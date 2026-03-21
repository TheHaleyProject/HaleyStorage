using Haley.Enums;
using Haley.Models;
using System.Collections.Generic;

namespace Haley.Abstractions {
    public interface IVaultScope {
        //The upload or the download request is scoped to this specific request.
        IVaultObject Client { get; }
        IVaultObject Module { get; }
        IVaultObject Workspace { get; }
        IVaultFolderRoute Folder { get; }
    }
}
