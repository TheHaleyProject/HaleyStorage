using Haley.Enums;
using System;
using System.IO;

namespace Haley.Abstractions {
    public interface IVaultFileWriteRequest : IVaultFileReadRequest, ICloneable {
        int BufferSize { get; set; }
        Stream FileStream { get; set; }
        string OriginalName { get; set; }
        IVaultFileWriteRequest SetOriginalName(string name);
        ExistConflictResolveMode WriteConflictMode { get;  }
    }
}
