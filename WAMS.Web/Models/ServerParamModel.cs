using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace WAMSDemo.Models
{
    [Serializable]
    public class ServerParamModel
    {
        public string MediaProcessor { get; set; }
        public string Presets { get; set; }
        public string Protection { get; set; }
        public string ExpireOn { get; set; }
        public string PresetDelimeter { get; set; }
    }
}