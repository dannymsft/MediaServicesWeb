using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace WAMSDemo.Models
{
    public class FirewallModel
    {
        public string ConnectionString { get; set; }
        public string RuleName { get; set; }
        public string StartIPRange { get; set; }
        public string EndIPRange { get; set; }
    }
}