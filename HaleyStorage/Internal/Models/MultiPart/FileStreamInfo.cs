using Microsoft.AspNetCore.Mvc;

namespace Haley.Models {
    public class FileStreamInfo {
        public string ContentType { get; set; }
        public string FileName { get; set; }
        public Stream Stream { get; set; }
    }
}
