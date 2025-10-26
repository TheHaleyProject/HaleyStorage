using Haley.Abstractions;
using Haley.Enums;
using Haley.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace Haley.Models {
    public class OSSWriteRequest : OSSReadFile, IOSSWrite {
        public string FileOriginalName { get; set; } //actual file name.
        public OSSResolveMode ResolveMode { get; set; } = OSSResolveMode.ReturnError;
        public int BufferSize { get; set; } = 1024 * 80; //Default to 80KB
        public string Id { get; set; }
        public Stream FileStream { get; set; }

        public new OSSWriteRequest SetComponent(OSSControlled input, OSSComponent type) {
             base.SetComponent(input,type);
            return this;
        }
        public OSSWriteRequest() : this(null, null, null) {

        }
        public OSSWriteRequest(string client_name) : this(client_name, null,null) {

        }
        public OSSWriteRequest(string client_name, string module_name) : this(client_name, module_name, null) {

        }
        public OSSWriteRequest(string client_name, string module_name,string workspace_name, bool isWsVirtual = false):base(client_name,module_name,workspace_name) {
        
        }

        public virtual object Clone() {
            var cloned = new OSSWriteRequest(this.Client.Name);
            //use map
            this.MapProperties(cloned);
            return cloned ;
        }

        public IOSSWrite SetFileOriginalName(string name) {
            if (string.IsNullOrWhiteSpace(name)) return this;
            FileOriginalName = name;
            return this;
        }
    }
}
