using Haley.Abstractions;
using Haley.Enums;
using Haley.Utils;
using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Text;
using System.Xml.Linq;

namespace Haley.Models {
    /// <summary>
    /// Extends <see cref="VaultInfo"/> with storage-name generation metadata:
    /// whether the object uses a numeric ID or GUID (<see cref="ControlMode"/>),
    /// whether the ID is parsed from input or auto-generated (<see cref="ParseMode"/>),
    /// whether the object is virtual (DB-only, no physical directory),
    /// and the current version number.
    /// </summary>
    public class VaultProfile : VaultInfo, IVaultStorable {
        public string StorageName { get; set; } //Should be the controlled name or a name compatible for the database 
        public bool IsVirtual { get; set; }
        public int Version { get; set; } = 0;
        public VaultControlMode ControlMode { get; set; } //Parsing or create mode is defined at application level?
        public VaultParseMode ParseMode { get; set; } //If false, we fall back to parsing.
        public override IVaultStorable UpdateCUID(params string[] parentNames) {
            return (IVaultStorable)base.UpdateCUID(parentNames);
        }
        public VaultProfile(string displayname, VaultControlMode control = VaultControlMode.Number, VaultParseMode parse = VaultParseMode.Parse, bool isVirtual = false) : base(displayname) {
            ControlMode = control;
            ParseMode = parse;
            GenerateCuid();
            IsVirtual = isVirtual;
        }
    }
}
