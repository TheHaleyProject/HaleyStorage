using static Haley.Internal.IndexingConstant;

namespace Haley.Internal {
    internal partial class IndexingQueries {
        public partial class INSTANCE {
            public class NAMESTORE {
                public const string EXISTS = $@"select ns.id from name_store as ns where ns.name = {NAME} and ns.extension = {EXT};";
                public const string INSERT = $@"insert ignore into name_store (name,extension) values ({NAME},{EXT});";
                public const string GET = $@"SELECT ns.id FROM name_store AS ns 
                                            INNER JOIN ( SELECT vin.id FROM vault AS vin WHERE vin.name = {NAME}) AS v ON v.id = ns.name
                                            INNER JOIN extension AS ext ON ext.id = ns.extension
                                            WHERE ext.name = {EXT};";
            }
        }
    }
}
