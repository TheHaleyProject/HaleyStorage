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
    }
}
