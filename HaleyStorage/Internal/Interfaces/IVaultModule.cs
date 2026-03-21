using Haley.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Haley.Abstractions {
    internal interface IVaultModule : IVaultObject, IStorageProfile {
        IVaultObject Client { get; set; }
    }
}
