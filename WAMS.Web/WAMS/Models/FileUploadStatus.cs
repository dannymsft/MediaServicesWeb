using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace WAMSDemo.WAMS.Models
{
    public class FileUploadStatus
    {
        public bool Error { get; set; }
        public bool IsLastBlock { get; set; }
        public string Message { get; set; }
    }
}