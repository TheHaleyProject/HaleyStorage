using Haley.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Haley.Abstractions {
    public interface IVaultModule : IVaultBase {
        IVaultBase Client { get; set; }
        string DatabaseName { get; set; } //Needed because based on this name is the separate database is created.
        string StorageProfileName { get; set; } //Storage profile is also for the Module itself and not for the workspace.
        string StorageProviderKey { get; set;  }
        string StagingProviderKey { get; set; }
    }
}
