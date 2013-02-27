using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace WAMSDemo.Models
{
    [Serializable]
    public class RefreshDataModel
    {
        public int RefreshInterval { get; set; }
        public string StatusMessage { get; set; }
    }
}