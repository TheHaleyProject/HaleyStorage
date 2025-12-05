using Haley.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace Haley.Models {
    public abstract class StorageDirectory : StorageInfo , IStorageDirectory{
        public string Path { get; set; }
        public StorageDirectory(string displayName):base(displayName) { }
    }
}
