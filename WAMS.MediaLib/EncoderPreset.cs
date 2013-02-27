using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace WAMS.MediaLib
{
    public class EncoderPreset : TableEntity
    {
        public EncoderPreset() { }
        

        public EncoderPreset(string codec, string preset)
        {
            SetKeys(codec, preset);
        }

        private void SetKeys(string partitionkey, string rowkey)
        {
            PartitionKey = partitionkey;
            RowKey = rowkey;
        }

    }
}