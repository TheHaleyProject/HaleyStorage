using Haley.Enums;
using Haley.Models;
using System.Collections.Generic;

namespace Haley.Abstractions {
    public interface IVaultFileReadRequest : IVaultReadRequest {
        IVaultFileRoute File { get; }
        IVaultFileReadRequest SetFile(IVaultFileRoute file);
    }
}
