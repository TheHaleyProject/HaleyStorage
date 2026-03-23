using Haley.Enums;
using System;
using System.IO;

namespace Haley.Abstractions {
    public interface IVaultFileWriteRequest : IVaultFileReadRequest, ICloneable {
        int BufferSize { get; set; }
        Stream FileStream { get; set; }
        string OriginalName { get; set; }
        IVaultFileWriteRequest SetOriginalName(string name);
        ExistConflictResolveMode WriteConflictMode { get; }
        /// <summary>
        /// When <c>true</c>, the incoming CUID identifies the parent document family.
        /// A brand-new <c>doc_version</c> row (version+1) is created under that document,
        /// bypassing filename-based lookup. The document's original name is preserved in DB.
        /// </summary>
        bool CreateNewVersion { get; set; }
    }
}
