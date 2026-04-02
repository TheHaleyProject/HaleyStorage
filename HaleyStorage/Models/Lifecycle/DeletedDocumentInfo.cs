using System;
using System.Collections.Generic;

namespace Haley.Models {
    /// <summary>
    /// Deleted logical document information used for soft delete, archive-on-reupload, and restore flows.
    /// </summary>
    public class DeletedDocumentInfo {
        public long DocumentId { get; set; }
        public string DocumentCuid { get; set; } = string.Empty;
        public long WorkspaceId { get; set; }
        public long DirectoryId { get; set; }
        public long NameStoreId { get; set; }
        public long? OriginalNameStoreId { get; set; }
        public string CurrentFileName { get; set; } = string.Empty;
        public string RestoreFileName { get; set; } = string.Empty;
        public int DeleteState { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? Deleted { get; set; }
        public List<DeletedDocumentVersionInfo> Versions { get; set; } = new();
    }
}
