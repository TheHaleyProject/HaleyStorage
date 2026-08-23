using static Haley.Internal.IndexingConstant;

namespace Haley.Internal {
    internal partial class IndexingQueries {
        public class PROVIDER {
            public const string EXISTS = $@"select p.id from provider as p where p.name = {NAME} LIMIT 1;";
            /// <summary>Plain insert — called only after EXISTS confirms the provider does not yet exist.</summary>
            public const string INSERT = $@"insert ignore into provider (name, display_name, description) values ({NAME},{DNAME},{DESCRIPTION});";
            public const string UPDATE = $@"update provider set display_name = {DNAME}, description = {DESCRIPTION} where id = {ID};";
        }
    }
}
