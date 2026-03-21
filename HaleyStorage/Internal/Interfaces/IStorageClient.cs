using Haley.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Haley.Abstractions {
    internal interface IStorageClient : IVaultObject {
        //If created in 
        string SigningKey { get; set; }
        string EncryptKey { get; set; }
        string PasswordHash { get; set; }
    }
}
