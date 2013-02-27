using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Web;
using System.Data.Services.Client;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Table;

namespace MediaServicesPortal
{
    public class MediaAssetContext
    {
        public const string MediaAssetTableName = "MediaAssets";

        public CloudTable Table;


        public MediaAssetContext()
        {
            Table = Constants.GetTable(MediaAssetTableName);
        }


        public IEnumerable<MediaAsset> MediaAssets { get {
            var selectAllquery = new TableQuery<MediaAsset>();
            return Table.ExecuteQuery<MediaAsset>(selectAllquery);
        } }

        public void AddMediaAssets(MediaAsset asset)
        {
            var insertOperation = TableOperation.InsertOrReplace(asset);
            Table.Execute(insertOperation);

        }


        public void UpdateMediaAsset(MediaAsset asset)
        {
            var mergeOperation = TableOperation.Merge(asset);
            Table.Execute(mergeOperation);

            // Notice how the AttachTo method is called with a null Etag which indicates that this is an Upsert Command
            //this.AttachTo(MediaAssetTableName, asset, null);
            //this.UpdateObject(asset);

            //// No SaveChangeOptions is used, which indicates that a MERGE verb will be used. This set of steps will result in an InsertOrMerge command to be sent to Windows Azure Table
            //this.SaveChanges();
        }

        public MediaAsset GetMediaAsset(string partitionKey, string rowKey)
        {
            try
            {
                var pkFilter = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, partitionKey);
                var rkFilter = TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, rowKey);


                var combinedFilter = TableQuery.CombineFilters(pkFilter, TableOperators.And, rkFilter);
                var query = new TableQuery<MediaAsset>().Where(combinedFilter);
                var records = Table.ExecuteQuery(query);

                if (records != null && records.Any())
                    return records.First();
                return null;
            }
            catch (Exception e)
            {
                throw new Exception("Error accessing MediaAsset table.", e);
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public IEnumerable<MediaAsset> GetAssetsInProgress()
        {
            try
            {
                var sFilter = TableQuery.GenerateFilterCondition("Status", QueryComparisons.NotEqual, "Finished");

                var query = new TableQuery<MediaAsset>().Where(sFilter);
                var records = Table.ExecuteQuery(query);

                return records ?? null;
            }
            catch (Exception e)
            {
                throw new Exception("Error accessing MediaAsset table.", e);
            }
        }



        /// <summary>
        /// 
        /// </summary>
        /// <param name="asset"></param>
        /// <returns></returns>
        public bool DeleteAsset(MediaAsset asset)
        {
            try
            {
                //Get the asset's blob container
                var blobUrl = asset.Url;
                if (!String.IsNullOrEmpty(blobUrl))
                {
                    var urlArray = blobUrl.Split('/'); //following https://...blob.core.windows.net/asset- wildcard
                    var assetBlobContainer = urlArray.First(e => e.StartsWith("asset-"));
                    var blobClient = Constants.GetBlobClient();
                    //Delete the temp container
                    var assetContainer = blobClient.GetContainerReference(assetBlobContainer);
                    assetContainer.Delete();
                }

                //Delete the asset from the MediaAssets table
                var deleteOperation = TableOperation.Delete(asset);
                //Submit the operation to the table service
                var tableResult = Table.Execute(deleteOperation);
                if (tableResult.HttpStatusCode == 200) return true;
            }
            catch (Exception e)
            {
                throw new Exception("Error deleting an asset.", e);
            }
            return false;
        }
    }
}