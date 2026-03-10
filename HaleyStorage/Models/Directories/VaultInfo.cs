using Haley.Abstractions;
using Haley.Utils;
using System;
using System.Collections.Generic;
using System.Text;
using Haley.Enums;
using System.Linq;

namespace Haley.Models {
    public class VaultInfo : IVaultBase {
        public string Name { get; set; }
        public long Id { get; set; }                        // public set — required by IIdentityBase
        [IgnoreMapping] //Important.. should not map this.
        public Guid Cuid { get; protected set; }            // Guid — matches IVaultBase.Cuid
        public Guid Guid { get; private set; }              // Guid — matches IIdentityBase.Guid
        public string Key => Cuid == System.Guid.Empty ? Name : Cuid.ToString("N"); // IIdentityBase.Key

        private string _displayName;
        public string DisplayName {
            get { return _displayName; }
            set {
                if (!string.IsNullOrWhiteSpace(value)) {
                    _displayName = value.Trim();
                } else {
                    _displayName = VaultConstants.DEFAULT_NAME;
                }
                if (!ValidateInternal(out var msg)) throw new Exception(msg);
                Name = _displayName.ToDBName(); //Db compatible name
                Guid = Name.CreateGUID(HashMethod.Sha256); // returns System.Guid
            }
        }

        public virtual bool TryValidate(out string message) {
            return ValidateInternal(out message);
        }

        bool ValidateInternal(out string message) {
            message = string.Empty;
            if (string.IsNullOrWhiteSpace(DisplayName)) {
                message = "Display Name cannot be empty";
                return false;
            }
            if (DisplayName.Contains("..") || DisplayName.Contains(@"\") || DisplayName.Contains(@"/")) {
                message = "Name contains invalid characters";
                return false;
            }
            return true;
        }

        protected virtual void GenerateCuid() {
            if (!string.IsNullOrWhiteSpace(DisplayName)) {
                var cuidStr = StorageUtils.GenerateCuid(DisplayName);
                if (System.Guid.TryParse(cuidStr, out var g)) Cuid = g;
            }
        }

        public virtual IVaultBase UpdateCUID(params string[] parentNames) {
            if (parentNames == null) return this;
            var inputList = parentNames.ToList();
            if (inputList.Count == 0 || inputList.Last().ToDBName() != Name) {
                inputList.Add(Name);
            }
            var cuidStr = StorageUtils.GenerateCuid(inputList.ToArray());
            if (System.Guid.TryParse(cuidStr, out var g)) Cuid = g;
            return this;
        }

        public IIdentityBase SetGuid(Guid guid) {
            Guid = guid;
            return this;
        }

        public IVaultBase SetId(long setId) {
            Id = setId;
            return this;
        }

        public IVaultBase SetName(string name) {
            Name = name;
            return this;
        }

        public IVaultBase SetCuid(Guid guid) {
            Cuid = guid;
            return this;
        }

        public IVaultBase SetCuid(string guid) {
            if (string.IsNullOrWhiteSpace(guid)) throw new Exception("Cannot set CUID with empty value");
            System.Guid res;
            if (guid.IsCompactGuid(out res) || guid.IsValidGuid(out res)) {
                Cuid = res;
            } else {
                throw new Exception("Cannot set CUID. Input should be in a proper GUID format.");
            }
            return this;
        }
        public VaultInfo(string displayName) {
            DisplayName = displayName ?? VaultConstants.DEFAULT_NAME;
        }
    }
}
