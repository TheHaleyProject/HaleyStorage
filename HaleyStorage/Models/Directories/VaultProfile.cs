using Haley.Abstractions;
using Haley.Enums;
using Haley.Utils;
using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Text;
using System.Xml.Linq;

namespace Haley.Models {
    public class VaultProfile :VaultInfo , IVaultProfile{
        public string SaveAsName { get; set; } //Should be the controlled name or a name compatible for the database 
        public bool IsVirtual { get; set; }
        public int Version { get; set; } = 0;
        public VaultControlMode ControlMode { get; set; } //Parsing or create mode is defined at application level?
        public VaultParseMode ParseMode { get; set; } //If false, we fall back to parsing.
        public override IVaultProfile UpdateCUID(params string[] parentNames) {
            return (IVaultProfile)base.UpdateCUID(parentNames);
        }
        public VaultProfile(string displayname, VaultControlMode control = VaultControlMode.Number, VaultParseMode parse = VaultParseMode.Parse, bool isVirtual = false) : base(displayname) {
            ControlMode = control;
            ParseMode = parse;
            GenerateCuid();
            IsVirtual = isVirtual;
        }
    }
}
