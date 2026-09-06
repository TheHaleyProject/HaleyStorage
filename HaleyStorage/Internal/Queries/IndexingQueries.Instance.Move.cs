using static Haley.Internal.IndexingConstant;

namespace Haley.Internal {
    internal partial class IndexingQueries {
        public partial class INSTANCE {
            public class MOVE {
                public const string GET_DOCUMENT =
                    $@"select d.id, d.cuid, d.workspace, d.parent, d.name, coalesce(di.display_name, '') as display_name,
                              concat(v.name, case when ext.name = 'default' then '' else concat('.', ext.name) end) as file_name
                       from document as d
                       inner join name_store as ns on ns.id = d.name
                       inner join vault as v on v.id = ns.name
                       inner join extension as ext on ext.id = ns.extension
                       left join doc_info as di on di.file = d.id
                       where d.id = {ID} and d.delete_state = 0
                       limit 1;";

                public const string DOCUMENT_CONFLICT =
                    $@"select d.id from document as d where d.parent = {TARGET_PARENT} and d.name = {NAME} and d.delete_state = 0 limit 1;";

                public const string UPDATE_DOCUMENT =
                    $@"update document
                       set workspace = {TARGET_WORKSPACE},
                           parent = {TARGET_PARENT},
                           name = {NAME}
                       where id = {ID} and delete_state = 0;";

                public const string DIRECTORY_CONFLICT =
                    $@"select dir.id from directory as dir
                       where dir.workspace = {TARGET_WORKSPACE}
                         and dir.parent = {TARGET_PARENT}
                         and dir.name = {NAME}
                         and dir.delete_state = 0
                       limit 1;";

                public const string UPDATE_DIRECTORY =
                    $@"update directory
                       set workspace = {TARGET_WORKSPACE},
                           parent = {TARGET_PARENT},
                           name = {NAME},
                           display_name = {DNAME}
                       where id = {ID} and delete_state = 0;";

                public const string UPDATE_DIRECTORY_WORKSPACE =
                    $@"update directory
                       set workspace = {TARGET_WORKSPACE}
                       where id = {ID};";

                public const string UPDATE_DOCUMENTS_IN_DIRECTORY_WORKSPACE =
                    $@"update document
                       set workspace = {TARGET_WORKSPACE}
                       where parent = {PARENT};";
            }
        }
    }
}
