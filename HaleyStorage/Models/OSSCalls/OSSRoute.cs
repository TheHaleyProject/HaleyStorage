using Haley.Abstractions;

namespace Haley.Models {
    //When using a struct, remember that it is a value type.
    //Thus, when you modify it, you need to return the modified struct.
    //This is useful for immutability and functional programming paradigms.
    public abstract class OSSRoute : IStorageRoute {
        public long Id { get; set; } //Database ID ??
        public string Cuid { get; set; } //Collision resistance Unique Identifier 
        public string Name { get; set; }
        public string Path { get; set; }
        public OSSRoute() { }
        public OSSRoute(string name, string path) { 
            Name = name;
            Path = path;
            Cuid = string.Empty; //CUID should be set later.
        }
    }
}
