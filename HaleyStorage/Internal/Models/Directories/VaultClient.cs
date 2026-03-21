using Haley.Abstractions;
using System;

namespace Haley.Models {
    /// <summary>
    /// Represents a vault client — the top level of the storage hierarchy.
    /// Extends <see cref="VaultObject"/> directly. Holds the signing/encryption keys
    /// used for JWT and content encryption. Physical path is always derived from the client name.
    /// </summary>
    internal class VaultClient : VaultObject, IVaultClient {
        public string SigningKey { get; set; }
        public string EncryptKey { get; set; }
        public string PasswordHash { get; set; }

        public override bool TryValidate(out string message) {
            message = string.Empty;
            if (!base.TryValidate(out message)) return false;
            if (string.IsNullOrEmpty(SigningKey) || string.IsNullOrEmpty(EncryptKey) || string.IsNullOrEmpty(PasswordHash)) {
                message = "Keys cannot be empty for the client";
                return false;
            }
            return true;
        }

        /// <summary>Creates a <see cref="VaultClient"/> with pre-hashed credentials. Sets CUID to the object's own Guid.</summary>
        public VaultClient(string password, string signingkey, string encryptkey, string displayName) : base(displayName) {
            PasswordHash = password;
            SigningKey = signingkey;
            EncryptKey = encryptkey;
            SetCuid(Guid);
        }
    }
}
