using Haley.Abstractions;
using Haley.Models;
using Haley.Utils;
using System;
using System.Threading.Tasks;

namespace Haley.Services {
    public partial class StorageCoordinator : IStorageCoordinator {
        public async Task<IFeedback<VaultMoveResult>> MoveFile(IVaultFileReadRequest source, IVaultReadRequest target, bool rename = false) {
            var fb = new Feedback<VaultMoveResult>();
            try {
                if (!WriteMode) return fb.SetMessage("Application is in Read-Only mode.");
                if (source == null || target == null) return fb.SetMessage("Source and target are required.");
                if (Indexer == null) return fb.SetMessage("MoveFile requires an indexer.");
                if (source.Scope?.Workspace == null || target.Scope?.Workspace == null)
                    return fb.SetMessage("Source and target workspace information is required.");

                source.Scope.Workspace.SetCuid(StorageUtils.GenerateCuid(source, Enums.VaultObjectType.WorkSpace));
                target.Scope.Workspace.SetCuid(StorageUtils.GenerateCuid(target, Enums.VaultObjectType.WorkSpace));
                return await Indexer.MoveDocument(source, target, rename);
            } catch (Exception ex) {
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<VaultMoveResult>> MoveDirectory(IVaultReadRequest source, IVaultReadRequest target, bool rename = false) {
            var fb = new Feedback<VaultMoveResult>();
            try {
                if (!WriteMode) return fb.SetMessage("Application is in Read-Only mode.");
                if (source == null || target == null) return fb.SetMessage("Source and target are required.");
                if (Indexer == null) return fb.SetMessage("MoveDirectory requires an indexer.");
                if (source.Scope?.Workspace == null || target.Scope?.Workspace == null)
                    return fb.SetMessage("Source and target workspace information is required.");

                source.Scope.Workspace.SetCuid(StorageUtils.GenerateCuid(source, Enums.VaultObjectType.WorkSpace));
                target.Scope.Workspace.SetCuid(StorageUtils.GenerateCuid(target, Enums.VaultObjectType.WorkSpace));
                return await Indexer.MoveDirectory(source, target, rename);
            } catch (Exception ex) {
                return fb.SetMessage(ex.Message);
            }
        }
    }
}
