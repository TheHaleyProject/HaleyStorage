using System.IO;

namespace Haley.Models {
    /// <summary>
    /// Result returned by IStorageProvider.WriteAsync.
    /// The provider owns the conflict resolution details; the coordinator reads the outcome.
    /// </summary>
    public struct ProviderWriteResult {
        public bool Success { get; init; }
        public bool AlreadyExisted { get; init; }
        public string Message { get; init; }

        public static ProviderWriteResult Ok(bool alreadyExisted = false, string message = null) =>
            new ProviderWriteResult { Success = true, AlreadyExisted = alreadyExisted, Message = message };

        public static ProviderWriteResult Skipped() =>
            new ProviderWriteResult { Success = true, AlreadyExisted = true, Message = "File exists. Skipped." };

        public static ProviderWriteResult Fail(string message) =>
            new ProviderWriteResult { Success = false, Message = message };

        public static ProviderWriteResult ExistsError() =>
            new ProviderWriteResult { Success = false, AlreadyExisted = true, Message = "File exists. Returned error." };
    }
}