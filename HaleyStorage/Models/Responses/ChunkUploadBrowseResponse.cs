namespace Haley.Models {
    public sealed class ChunkUploadBrowseResponse {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public long TotalItems { get; set; }
        public List<ChunkUploadBrowseItem> Items { get; set; } = new();
    }
}
