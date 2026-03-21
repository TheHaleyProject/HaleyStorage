using Haley.Enums;

namespace Haley.Abstractions {
    public interface IStorageProfile  {
        string Name { get;  }
        int Version { get; }//Which version is being used for this specific module
        StorageProfileMode Mode { get; }
        IStorageProvider Primary { get; }
        IStorageProvider Staging { get;  }
    }
}
