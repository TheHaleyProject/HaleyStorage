using Haley.Abstractions;
using Haley.Models;
using Haley.Utils;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace Haley.Services {
    public partial class StorageCoordinator : IStorageCoordinator {
        public Task<IFeedback> AuthorizeClient(object clientInfo, object clientSecret) {
            //Take may be we take the password? no?
            //We can take the password for this client, and compare with the information available in the DB or in the folder. 
            //Whenever indexing is enabled, may be we need to take all the availalbe clients and fetch their password file and update the DB. Because during the time the indexing was down, may be system generated it's own files and stored it.
            IFeedback result = new Feedback();
            result.Status = true;
            result.Message = "No default implementation available. All requests authorized.";
            return Task.FromResult(result);
        }

        public static (bool Status, List<Claim> Claims) ValidateToken(string token) {
            return (false, null);
        }

        public static (string EncryptKey,string SigningKey) GetToken() {
            return (string.Empty,string.Empty);
        }
    }
}
