using System.IO;

namespace Haley.Abstractions {
    public interface IVaultStreamResponse : IFeedback {
        Stream Stream { get; set; }
        string Extension { get; set; }
        string SaveName { get; set; }
        /// <summary>
        /// When non-null, the caller should redirect the client to this URL rather than
        /// streaming <see cref="Stream"/>. Returned by cloud staging providers (B2, S3, Azure)
        /// as a pre-signed download URL. <see cref="Stream"/> will be <see cref="Stream.Null"/>
        /// when this is populated.
        /// </summary>
        string AccessUrl { get; set; }
    }
}
