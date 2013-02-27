using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace WAMS.MediaLib.Models
{
    public class FileUploadStatus
    {
        public bool Error { get; set; }
        public bool IsLastBlock { get; set; }
        public string Message { get; set; }
    }
}