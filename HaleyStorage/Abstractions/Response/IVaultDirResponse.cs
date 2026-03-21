using System.Collections.Generic;

namespace Haley.Abstractions {
    public interface IVaultDirResponse : IFeedback {
        string StorageRef { get; set; }
        List<string> FoldersList { get; set; }
        List<string> FilesList { get; set; }
    }
}
