using Haley.Abstractions;
using Haley.Utils;
using System;
using System.Collections.Generic;
using System.Text;
using Haley.Enums;
using System.Linq;

namespace Haley.Models {
    public class StorageInfoBase : IVaultProfile {
        public string Name { get; private set; }
        public long Id { get; private set; }
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
                Guid = Name.CreateGUID(HashMethod.Sha256).ToString("N");
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
            Cuid = StorageUtils.GenerateCuid(DisplayName);
        }

        public virtual IVaultProfile UpdateCUID(params string[] parentNames) {
            if (parentNames == null) return this;
            var inputList = parentNames.ToList();
            if (inputList.Count == 0 || inputList.Last().ToDBName() != Name) {
                inputList.Add(Name); //I
            }
            Cuid = StorageUtils.GenerateCuid(inputList.ToArray());
            return this;
        }

        public IVaultProfile SetId(long setId) {
            Id = setId;
            return this;
        }

        public IVaultProfile SetCuid(System.Guid guid) {
            //if (guid == System.Guid.Empty) throw new Exception("Cannot set CUID. Input cannot be an empty GUID.");
            Cuid = guid.ToString("N");
            return this;
        }

        public IVaultProfile SetCuid(string guid) {
            if (string.IsNullOrWhiteSpace(guid)) throw new Exception("Cannot set CUID with empty value");
            var res = System.Guid.Empty;
            if (guid.IsCompactGuid(out res) || guid.IsValidGuid(out res)) {
                Cuid = res.ToString("N");
            } else {
                throw new Exception("Cannot set CUID. Input should be in a proper GUID format.");
            }
            return this;
        }

        public string Guid { get; private set; } //Sha256 generated from the name and a guid is created from there.
        [IgnoreMapping] //Important.. no should map this.
        public string Cuid { get; private set; } //Collision resistant Unique identifier
        public StorageInfoBase(string displayName) {
            DisplayName = displayName ?? VaultConstants.DEFAULT_NAME;
        }
    }
}
