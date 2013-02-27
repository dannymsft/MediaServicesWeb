using MediaServicesPortal;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace WAMSDemo.WAMS
{
    public class EncoderPresetContext
    {
        public const string EncodersTableName = "Encoders";

        public CloudTable Table;


        public EncoderPresetContext()
        {
            Table = Constants.GetTable(EncodersTableName);
        }


        public IEnumerable<EncoderPreset> Encoders { get {
            var selectAllquery = new TableQuery<EncoderPreset>();
            return Table.ExecuteQuery<EncoderPreset>(selectAllquery);
        } }



        public IEnumerable<string> PresetCategories
        {
            get
            {
                var selectAllquery = new TableQuery<EncoderPreset>();
                var presetCategories = Table.ExecuteQuery(selectAllquery)
                                            .Select(presetCategory => presetCategory.PartitionKey).ToList();
                return presetCategories.Distinct();
            }
        }




        /// <summary>
        /// 
        /// </summary>
        /// <param name="partitionKey"></param>
        /// <param name="rowKey"></param>
        /// <returns></returns>
        public EncoderPreset GetPreset(string partitionKey, string rowKey)
        {
            try
            {
                var pkFilter = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, partitionKey);
                var rkFilter = TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, rowKey);


                var combinedFilter = TableQuery.CombineFilters(pkFilter, TableOperators.And, rkFilter);
                var query = new TableQuery<EncoderPreset>().Where(combinedFilter);
                var records = Table.ExecuteQuery(query);

                if (records != null && records.Any())
                    return records.First();
                return null;
            }
            catch (Exception e)
            {
                throw new Exception("Error accessing storage.", e);
            }
        }


        /// <summary>
        /// Get presets by the partition key
        /// </summary>
        /// <param name="partitionKey"></param>
        /// <returns></returns>
        public IEnumerable<EncoderPreset> GetPresets(string partitionKey)
        {
            try
            {
                var pkFilter = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, partitionKey);

                var query = new TableQuery<EncoderPreset>().Where(pkFilter);
                var records = Table.ExecuteQuery(query);

                return records;
            }
            catch (Exception e)
            {
                throw new Exception("Error accessing storage.", e);
            }
        }

    }
}