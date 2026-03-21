using Haley.Abstractions;
using Haley.Models;
using Haley.Utils;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace Haley.Services {
    /// <summary>
    /// Partial class — authorization stubs. Full implementation is deferred;
    /// all requests are currently authorized by default.
    /// </summary>
    public partial class StorageCoordinator : IStorageCoordinator {
        /// <summary>
        /// Placeholder client authorization check.
        /// Always returns <c>Status=true</c> — no real validation is performed yet.
        /// </summary>
        public Task<IFeedback> AuthorizeClient(object clientInfo, object clientSecret) {
            //Take may be we take the password? no?
            //We can take the password for this client, and compare with the information available in the DB or in the folder. 
            //Whenever indexing is enabled, may be we need to take all the availalbe clients and fetch their password file and update the DB. Because during the time the indexing was down, may be system generated it's own files and stored it.
            IFeedback result = new Feedback();
            result.Status = true;
            result.Message = "No default implementation available. All requests authorized.";
            return Task.FromResult(result);
        }

        /// <summary>Stub — token validation not yet implemented. Always returns <c>false</c>.</summary>
        public static (bool Status, List<Claim> Claims) ValidateToken(string token) {
            return (false, null);
        }

        /// <summary>Stub — returns empty encryption and signing keys until auth is implemented.</summary>
        public static (string EncryptKey,string SigningKey) GetToken() {
            return (string.Empty,string.Empty);
        }
    }
}
