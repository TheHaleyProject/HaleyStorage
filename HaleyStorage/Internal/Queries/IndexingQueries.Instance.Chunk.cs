using static Haley.Internal.IndexingConstant;

namespace Haley.Internal {
    internal partial class IndexingQueries {
        public partial class INSTANCE {
            public class CHUNK {
                public const string INFO_EXISTS = $@"select 1 from chunk_info where id = {ID};";

                public const string INFO_UPSERT =
                    $@"insert into chunk_info (id, size, parts, name, path, is_completed)
                       values ({ID},{CHUNK_SIZE},{CHUNK_PARTS},{CHUNK_NAME},{PATH},{IS_COMPLETED})
                       ON DUPLICATE KEY UPDATE
                            size = VALUES(size),
                            parts = VALUES(parts),
                            name = VALUES(name),
                            path = VALUES(path),
                            is_completed = VALUES(is_completed);";

                public const string FILE_UPSERT =
                    $@"insert into chunked_files (id, part, size, hash)
                       values ({ID},{PART},{FILESIZE_MB},{HASH})
                       ON DUPLICATE KEY UPDATE
                            size = VALUES(size),
                            hash = COALESCE(VALUES(hash), hash),
                            uploaded = current_timestamp();";

                public const string MARK_COMPLETED =
                    $@"update chunk_info set is_completed = b'1' where id = {ID};";

                public const string ABORT_VERSION =
                    $@"update doc_version
                          set delete_state = 1,
                              deleted = coalesce(deleted, current_timestamp())
                        where id = {ID}
                          and delete_state = 0;";

                public const string ABORT_EMPTY_DOCUMENT =
                    $@"update document d
                         join doc_version target on target.parent = d.id and target.id = {ID}
                          set d.delete_state = 1,
                              d.deleted = coalesce(d.deleted, current_timestamp())
                        where d.delete_state = 0
                          and not exists (
                              select 1
                                from doc_version active_version
                               where active_version.parent = d.id
                                 and active_version.id <> target.id
                                 and active_version.sub_ver = 0
                                 and active_version.delete_state = 0
                          );";
            }
        }
    }
}
