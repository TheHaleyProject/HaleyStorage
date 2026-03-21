using Haley.Abstractions;
using Haley.Utils;
using System;
using System.Collections.Generic;
using System.Text;
using Haley.Enums;
using System.Linq;

namespace Haley.Models {
    /// <summary>
    /// Base implementation of <see cref="IVaultObject"/> that owns the display name, DB-safe name,
    /// deterministic <see cref="Guid"/> (SHA-256 hash of the name), and the mutable <see cref="Cuid"/>
    /// (composite SHA-256 GUID derived from parent names).
    /// Setting <see cref="DisplayName"/> automatically derives <see cref="VaultObject.Name"/> and <see cref="Guid"/>.
    /// </summary>
    internal class VaultObject : IVaultObject {
        public string Name { get; set; }
        public long Id { get; set; }                        // public set — required by IIdentityBase
        [IgnoreMapping] //Important.. should not map this.
        public Guid Cuid { get; protected set; }
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

        /// <summary>Derives the <see cref="VaultObject.Cuid"/> from <see cref="DisplayName"/> alone (single-component hash).</summary>
        protected virtual void GenerateCuid() {
            if (!string.IsNullOrWhiteSpace(DisplayName)) {
                var cuidStr = StorageUtils.GenerateCuid(DisplayName);
                if (System.Guid.TryParse(cuidStr, out var g)) Cuid = g;
            }
        }

        /// <summary>
        /// Recomputes <see cref="VaultObject.Cuid"/> as a SHA-256 GUID of the concatenated
        /// <paramref name="parentNames"/> (plus this object's own name appended if absent).
        /// Used to build a deterministic, hierarchy-scoped CUID.
        /// </summary>
        public virtual IVaultObject UpdateCUID(params string[] parentNames) {
            if (parentNames == null) return this;
            var inputList = parentNames.ToList();
            if (inputList.Count == 0 || inputList.Last().ToDBName() != Name) {
                inputList.Add(Name);
            }
            var cuidStr = StorageUtils.GenerateCuid(inputList.ToArray());
            if (System.Guid.TryParse(cuidStr, out var g)) Cuid = g;
            return this;
        }

        /// <summary>Directly sets the identity <see cref="VaultObject.Guid"/> without triggering name derivation.</summary>
        public IIdentityBase SetGuid(Guid guid) {
            Guid = guid;
            return this;
        }

        /// <summary>Sets the DB auto-increment ID on this vault object.</summary>
        public IVaultObject SetId(long setId) {
            Id = setId;
            return this;
        }

        /// <summary>Directly sets the DB-safe <see cref="VaultObject.Name"/> without triggering display-name derivation.</summary>
        public IVaultObject SetName(string name) {
            Name = name;
            return this;
        }

        /// <summary>Directly sets the CUID to the given <see cref="Guid"/> value.</summary>
        public IVaultObject SetCuid(Guid guid) {
            Cuid = guid;
            return this;
        }

        /// <summary>
        /// Parses and sets the CUID from a compact-N or standard-format GUID string.
        /// Throws when the value is empty or cannot be parsed as a GUID.
        /// </summary>
        public IVaultObject SetCuid(string guid) {
            if (string.IsNullOrWhiteSpace(guid)) throw new Exception("Cannot set CUID with empty value");
            System.Guid res;
            if (guid.IsCompactGuid(out res) || guid.IsValidGuid(out res)) {
                Cuid = res;
            } else {
                throw new Exception("Cannot set CUID. Input should be in a proper GUID format.");
            }
            return this;
        }
        public VaultObject(string displayName) {
            DisplayName = displayName ?? VaultConstants.DEFAULT_NAME;
        }
    }
}
