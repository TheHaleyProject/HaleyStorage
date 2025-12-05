using Haley.Abstractions;
using Haley.Enums;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Haley.Models {
    public class StorageRegistryConfig : IStorageRegistryConfig{
        //All suffix are applicable only when dealing with controlled names.
        public string SuffixClient { get; set; } = "c";
        public string SuffixModule { get; set; } = "m";
        public string SuffixWorkSpace { get; set; } = "w";
        public string SuffixFile { get; set; } = "f";
        public int SplitLengthNumber { get; set; } = 2; //For numbers
        public int DepthNumber { get; set; } = 0;
        public int SplitLengthHash { get; set; } = 2; //For Hash
        public int DepthHash { get; set; } = 7;

        public StorageRegistryConfig() { }
    }
}
