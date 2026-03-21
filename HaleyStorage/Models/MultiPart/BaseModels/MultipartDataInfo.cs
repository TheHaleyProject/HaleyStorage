using Microsoft.Extensions.Primitives;

namespace Haley.Models {
    public class MultipartDataInfo : Dictionary<string,StringValues> {
        public List<string> ConsumedKeys { get; set; } = new List<string>();
        public MultipartDataInfo(IDictionary<string, StringValues> dictionary) : base(dictionary) {
        }
        public MultipartDataInfo() : base() {
        }
    }
}
