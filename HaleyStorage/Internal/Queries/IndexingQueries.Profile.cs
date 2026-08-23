using static Haley.Internal.IndexingConstant;

namespace Haley.Internal {
    internal partial class IndexingQueries {
        public class PROFILE {
            public const string EXISTS = $@"select pr.id from profile as pr where pr.name = {NAME} LIMIT 1;";
            /// <summary>Plain insert — called only after EXISTS confirms the profile does not yet exist.</summary>
            public const string INSERT = $@"insert ignore into profile (name, display_name) values ({NAME},{DNAME});";
            public const string UPDATE = $@"update profile set display_name = {DNAME} where id = {ID};";
        }
    }
}
