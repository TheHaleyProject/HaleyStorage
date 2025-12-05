using Haley.Abstractions;
using Haley.Enums;
using Microsoft.IdentityModel.Tokens.Experimental;
using System;
using System.Collections.Generic;
using System.Text;

namespace Haley.Models {
    public class StorageClient : StorageDirectory, IStorageClient {
        public string SigningKey { get; set; }
        public string EncryptKey { get; set; }
        public string PasswordHash { get; set; }
       
        public override bool TryValidate(out string message) {
            message = string.Empty;
            if (!base.TryValidate(out message)) return false;
            if (string.IsNullOrEmpty(SigningKey) || string.IsNullOrEmpty(EncryptKey) || string.IsNullOrEmpty(PasswordHash)) {
                message = "Keys Cannot be empty for the OSSClient";
                return false;   
            }

            if (string.IsNullOrEmpty(SaveAsName) || string.IsNullOrEmpty(Path)) {
                message = "Name & Path Cannot be empty";
                return false;
            }

            return true;
        }
        public StorageClient(string password, string signingkey,string encryptkey, string displayName) :base(displayName){ 
            PasswordHash = password;
            SigningKey = signingkey;
            EncryptKey = encryptkey;
            ForceSetCuid(Guid);
        }
    }
}
