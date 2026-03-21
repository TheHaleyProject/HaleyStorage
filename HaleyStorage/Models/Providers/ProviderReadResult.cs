using System.IO;

namespace Haley.Models {
    /// <summary>
    /// Result returned by IStorageProvider.ReadAsync.
    /// The provider owns the extension-search and stream-opening details.
    /// </summary>
    public struct ProviderReadResult {
        public bool Success { get; init; }
        public Stream Stream { get; init; }
        public string Extension { get; init; }
        public string Message { get; init; }

        public static ProviderReadResult Ok(Stream stream, string extension) =>
            new ProviderReadResult { Success = true, Stream = stream, Extension = extension };

        public static ProviderReadResult Fail(string message) =>
            new ProviderReadResult { Success = false, Stream = Stream.Null, Message = message };
    }
}