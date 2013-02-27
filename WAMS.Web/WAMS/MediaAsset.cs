using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace MediaServicesPortal 
{
    public class MediaAsset : TableEntity
    {

        public string OriginalFile { get; set; }
        public string Url { get; set; }
        public string Encoding { get; set; }
        public string Protection { get; set; }
        public string Renderer { get; set; }
        public DateTime Created { get; set; }
        public string ProcessingTime { get; set; }
        public DateTime ExpireOn { get; set; }
        public string Status { get; set; }
        public string Size { get; set; }
        public string Thumbnail { get; set; }

        public MediaAsset()
        {
            SetKeys("Media");
        }

        public MediaAsset(string title)
        {
            SetKeys(title);
        }

        private void SetKeys(string key)
        {
            PartitionKey = key;
            RowKey = string.Format("{0:10}_{1}", DateTime.MaxValue.Ticks - DateTime.UtcNow.Ticks, Guid.NewGuid());
        }
    }
}