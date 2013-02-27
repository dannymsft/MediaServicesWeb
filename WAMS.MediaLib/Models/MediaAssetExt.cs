using System;
using System.Collections.Generic;
using System.Linq;

namespace WAMS.MediaLib.Models
{
    public partial class MediaAsset
    {
        public MediaAsset(WAMS.MediaLib.MediaAsset asset)
        {
            Title = asset.PartitionKey;
            Id = asset.RowKey;
            OriginalFile = asset.OriginalFile;
            Url = asset.Url;
            Encoding = asset.Encoding;
            Protection = asset.Protection;
            Renderer = asset.Renderer;
            Created = asset.Created;
            ProcessingTime = asset.ProcessingTime;
            ExpireOn = asset.ExpireOn;
            Status = asset.Status;
            Size = asset.Size;
            Thumbnail = asset.Thumbnail;
        }


        public MediaAsset CopyFrom()
        {
            var newAsset = new MediaAsset
                {
                    Title = this.Title,
                    Id = this.Id,
                    OriginalFile = this.OriginalFile,
                    Url = this.Url,
                    Encoding = this.Encoding,
                    Protection = this.Protection,
                    Renderer = this.Renderer,
                    Created = this.Created,
                    ProcessingTime = this.ProcessingTime,
                    ExpireOn = this.ExpireOn,
                    Status = this.Status,
                    Size = this.Size,
                    Thumbnail = this.Thumbnail
                };

            return newAsset;
        }

    }

    public partial class MediaAssetsDataContext
    {
        public void AddMediaAssets(MediaAsset asset)
        {
            this.MediaAssets.InsertOnSubmit(asset);
            this.SubmitChanges();
        }


        public MediaAsset GetMediaAsset(string partitionKey, string rowKey)
        {
            var asset = this.MediaAssets.Single(m => m.Title == partitionKey && m.Id == rowKey);
            return asset;
        }


        public void UpdateStatus(MediaAsset asset)
        {
            var dbAsset = this.MediaAssets.Single(m => m.Title == asset.Title && m.Id == asset.Id);

            if (dbAsset != null)
            {
                dbAsset.Status = asset.Status;
                dbAsset.ProcessingTime = asset.ProcessingTime;
                dbAsset.Url = asset.Url;
                dbAsset.Thumbnail = asset.Thumbnail;
                dbAsset.Size = asset.Size;
                this.SubmitChanges();
            }
        }


        public void DeleteMediaAssetExt(MediaAsset asset)
        {
            var dbAsset = this.MediaAssets.Single(m => m.Title == asset.Title && m.Id == asset.Id);

            if (dbAsset != null)
            {
                this.MediaAssets.DeleteOnSubmit(dbAsset);
                this.SubmitChanges();
            }
        }


        public IEnumerable<MediaAsset> GetAssetsInProgress()
        {
            try
            {
                var assets = from m in this.MediaAssets
                             where m.Status != "Finished"
                             select m;
                //var records = this.MediaAssets.Take(100).Where(m => m.Status != "Finished").ToList();

                //return records ?? null;

                return assets.Take(10).ToList();
            }
            catch (Exception e)
            {
                throw new Exception("Error accessing MediaAsset table.", e);
            }
            
        }

    }
}
