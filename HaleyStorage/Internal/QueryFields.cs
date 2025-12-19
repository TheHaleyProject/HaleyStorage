using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml.Linq;
using static Haley.Internal.IndexingConstant;

namespace Haley.Internal {
    internal class IndexingConstant {
        public const string VAULT_DEFCLIENT = "admin";
        public const string NAME = $@"@{nameof(NAME)}";
        public const string DNAME = $@"@{nameof(DNAME)}";
        public const string SAVENAME = $@"@{nameof(SAVENAME)}";
        public const string GUID = $@"@{nameof(GUID)}";
        public const string CUID = $@"@{nameof(CUID)}";
        public const string PATH = $@"@{nameof(PATH)}";
        public const string SUFFIX_DIR = $@"@{nameof(SUFFIX_DIR)}";
        public const string SUFFIX_FILE = $@"@{nameof(SUFFIX_FILE)}";
        public const string ID = $@"@{nameof(ID)}";
        public const string FULLNAME = $@"@{nameof(FULLNAME)}";
        public const string SIGNKEY = $@"@{nameof(SIGNKEY)}";
        public const string ENCRYPTKEY = $@"@{nameof(ENCRYPTKEY)}";
        public const string VALUE = $@"@{nameof(VALUE)}";
        public const string PASSWORD = $@"@{nameof(PASSWORD)}";
        public const string DATETIME = $@"@{nameof(DATETIME)}";
        public const string PARENT = $@"@{nameof(PARENT)}";
        public const string DIRNAME = $@"@{nameof(DIRNAME)}";
        public const string CONTROLMODE = $@"@{nameof(CONTROLMODE)}";
        public const string PARSEMODE = $@"@{nameof(PARSEMODE)}";
        public const string WSPACE = $@"@{nameof(WSPACE)}";
        public const string EXT = $@"@{nameof(EXT)}";
        public const string VERSION = $@"@{nameof(VERSION)}";
        public const string SIZE = $@"@{nameof(SIZE)}";

        public const string STORAGE_PROFILE = $@"@{nameof(STORAGE_PROFILE)}";

        // CORE : PROFILE / PROVIDER
        public const string PROFILE_ID = $@"@{nameof(PROFILE_ID)}";
        public const string PROVIDER_ID = $@"@{nameof(PROVIDER_ID)}";
        public const string STORAGE_PROVIDER = $@"@{nameof(STORAGE_PROVIDER)}";
        public const string STAGING_PROVIDER = $@"@{nameof(STAGING_PROVIDER)}";
        public const string MODE = $@"@{nameof(MODE)}";
        public const string DESCRIPTION = $@"@{nameof(DESCRIPTION)}";
        public const string METADATA = $@"@{nameof(METADATA)}";

        // CLIENT : VERSION INFO
        public const string STAGINGPATH = $@"@{nameof(STAGINGPATH)}";
        public const string FLAGS = $@"@{nameof(FLAGS)}";

        // CLIENT : CHUNKING
        public const string CHUNK_SIZE = $@"@{nameof(CHUNK_SIZE)}";     // chunk_info.size (MB)
        public const string CHUNK_PARTS = $@"@{nameof(CHUNK_PARTS)}";   // chunk_info.parts
        public const string CHUNK_NAME = $@"@{nameof(CHUNK_NAME)}";     // chunk_info.name
        public const string IS_COMPLETED = $@"@{nameof(IS_COMPLETED)}"; // chunk_info.is_completed
        public const string PART = $@"@{nameof(PART)}";                 // chunked_files.part
        public const string FILESIZE_MB = $@"@{nameof(FILESIZE_MB)}";   // chunked_files.size (MB)
    }
}