using Haley.Abstractions;
using Haley.Utils;
using System;
using System.Collections.Generic;
using System.Text;
using Haley.Enums;
using System.Linq;

namespace Haley.Models {
    public class StorageUID : IStorageUID {
        public long Id { get; set; }
        public Guid Guid { get; set; }
        public StorageUID(long id, Guid uid) {
            Id = id;
            Guid = uid;
        }
    }
}
