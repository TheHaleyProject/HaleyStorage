using Haley.Abstractions;
using Haley.Enums;
using Haley.Utils;
using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Text;
using System.Xml.Linq;

namespace Haley.Models {
    public class StorageInfo :StorageInfoBase , IStorageInfo{
        public string SaveAsName { get; set; } //Should be the controlled name or a name compatible for the database 
        public bool IsVirtual { get; set; }
        public int Version { get; set; } = 0;
        public StorageControlMode ControlMode { get; set; } //Parsing or create mode is defined at application level?
        public StorageParseMode ParseMode { get; set; } //If false, we fall back to parsing.
        public override IStorageInfo UpdateCUID(params string[] parentNames) {
            return (IStorageInfo)base.UpdateCUID(parentNames);
        }
        public StorageInfo(string displayname, StorageControlMode control = StorageControlMode.Number, StorageParseMode parse = StorageParseMode.Parse, bool isVirtual = false) : base(displayname) {
            ControlMode = control;
            ParseMode = parse;
            GenerateCuid();
            IsVirtual = isVirtual;
        }
    }
}
