using Haley.Abstractions;
using Haley.Models;
using Haley.Utils;
using System;
using System.Threading.Tasks;

namespace Haley.Services {
    public partial class StorageCoordinator : IStorageCoordinator {
        public async Task<IFeedback<VaultStatsSnapshot>> GetStats(IVaultReadRequest input, string extension = null) {
            var fb = new Feedback<VaultStatsSnapshot>();
            try {
                if (input == null) return fb.SetMessage("Input request cannot be empty.");
                if (Indexer == null) return fb.SetMessage("GetStats requires an indexer.");
                if (input.Scope?.Workspace == null) return fb.SetMessage("Workspace information is required.");

                input.Scope.Workspace.SetCuid(StorageUtils.GenerateCuid(input, Enums.VaultObjectType.WorkSpace));
                return await Indexer.GetStats(input, extension);
            } catch (Exception ex) {
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback> ProcessStatsEvents(IVaultReadRequest input, int batchSize = 1000) {
            var fb = new Feedback();
            try {
                if (!WriteMode) return fb.SetMessage("Application is in Read-Only mode.");
                if (input == null) return fb.SetMessage("Input request cannot be empty.");
                if (Indexer == null) return fb.SetMessage("ProcessStatsEvents requires an indexer.");
                if (input.Scope?.Module == null) return fb.SetMessage("Module information is required.");

                return await Indexer.ProcessStatsEvents(input.Scope.Module.Cuid.ToString("N"), batchSize);
            } catch (Exception ex) {
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback> RebuildStats(IVaultReadRequest input, long? workspaceId = null) {
            var fb = new Feedback();
            try {
                if (!WriteMode) return fb.SetMessage("Application is in Read-Only mode.");
                if (input == null) return fb.SetMessage("Input request cannot be empty.");
                if (Indexer == null) return fb.SetMessage("RebuildStats requires an indexer.");
                if (input.Scope?.Module == null) return fb.SetMessage("Module information is required.");

                return await Indexer.RebuildStats(input.Scope.Module.Cuid.ToString("N"), workspaceId);
            } catch (Exception ex) {
                return fb.SetMessage(ex.Message);
            }
        }
    }
}
