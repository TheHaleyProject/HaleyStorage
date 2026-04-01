namespace Haley.Abstractions {
    public interface IVaultScope {
        //The upload or the download request is scoped to this specific request.
        IVaultObject Client { get; set; }
        IVaultObject Module { get; set; }
        IVaultObject Workspace { get; set; }
        IVaultFolderRoute Folder { get; set; }
    }
}
