using Haley.Enums;
using System.Threading;
using System.Threading.Tasks;

namespace Haley.Abstractions {
    public interface IStorageOperations {
        //Onus of generating the path doesn't lie with the Storage service.
        //We need Store, Fetch, Delete
        Task<IVaultResponse> Upload(IVaultFileWriteRequest input);
        Task<IVaultStreamResponse> Download(IVaultFileReadRequest input, bool auto_search_extension = true);
        Task<IVaultStreamResponse> Download(IVaultFileRoute input, bool auto_search_extension = true);
        Task<IFeedback> Delete(IVaultFileReadRequest input);
        IFeedback Exists(IVaultReadRequest input, bool isFilePath = false);
        long GetSize(IVaultReadRequest input);
    }
}
