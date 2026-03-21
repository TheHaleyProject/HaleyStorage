using System.IO;
using Haley.Abstractions;

namespace Haley.Models {
    public class VaultStreamResponse : Feedback,IVaultStreamResponse {
        public Stream Stream { get; set; }
        public string Extension { get; set; }
        public string SaveName { get; set; }
        public string AccessUrl { get; set; }
        public VaultStreamResponse() {  }
    }
}
