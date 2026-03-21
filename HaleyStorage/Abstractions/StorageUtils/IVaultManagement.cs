using Haley.Enums;
using System.Threading.Tasks;
using Haley.Models;
using Microsoft.Extensions.Configuration;

namespace Haley.Abstractions {
    public interface IVaultManagement {
        //Onus of generating the path doesn't lie with the Storage service.
        //We need Store, Fetch, Delete
        IVaultRegistryConfig Config { get; }
        Task<IFeedback> RegisterClient(string client_name = null, string password = null);
        Task<IFeedback> RegisterModule(string module_name = null, string client_name = null); //If a client is not registered, we register it against "Default"
        Task<IFeedback> RegisterWorkSpace(string workspace_name = null, string client_name = null, string module_name = null, VaultNameMode content_control = VaultNameMode.Number, VaultNameParseMode content_pmode = VaultNameParseMode.Generate, bool is_virtual = false); //If a client is not registered, we register it against "Default"

        Task<IFeedback> RegisterClient(IVaultObject client, string password = null);
        Task<IFeedback> RegisterModule(IVaultObject module, IVaultObject client);
        Task<IFeedback> RegisterWorkSpace(IVaultObject wspace, IVaultObject client, IVaultObject module, VaultNameMode content_control = VaultNameMode.Number, VaultNameParseMode content_pmode = VaultNameParseMode.Generate, bool is_virtual = false);
        Task<IFeedback> RegisterFromSource(IConfigurationSection section = null);
        Task<IFeedback> AuthorizeClient(object clientInfo, object clientSecret);
    }
}
