using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.Storage;
//----------------------------------------------------------------------------------------------------------------------------
// <copyright file="Constants.cs" company="Microsoft Corporation">
//  Copyright 2011 Microsoft Corporation
// </copyright>
// Licensed under the MICROSOFT LIMITED PUBLIC LICENSE version 1.1 (the "License"); 
// You may not use this file except in compliance with the License. 
//---------------------------------------------------------------------------------------------------------------------------
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Table;
using System.Configuration;
namespace MediaServicesPortal
{
    /// <summary>
    /// Constants used by the application.
    /// </summary>
    internal static class Constants
    {
        /// <summary>
        /// Configuration section key containing connection string.
        /// </summary>
        internal const string ConfigurationSectionKey = "DataConnectionString";

        /// <summary>
        /// Container where to upload files
        /// </summary>
        internal const string ContainerName = "uploads";

        /// <summary>
        /// Number of bytes in a Kb.
        /// </summary>
        internal const int BytesPerKb = 1024;

        /// <summary>
        /// Name of session element where attributes of file to be uploaded are saved.
        /// </summary>
        internal const string FileAttributesSession = "FileClientAttributes";

        /// <summary>
        /// Partition Key names for the various coding standards in the Encoders table
        /// </summary>
        internal const string AudioPreset = "Audio Coding Standard";
        internal const string ThumbnailPreset = "Thumbnail";
        internal const string VideoVC1Preset = "VC-1 Coding Standard";
        internal const string VideoH264Preset = "H.264 Coding Standard";

        internal const int DefaultRefreshInterval = 60000;   //60 seconds
        internal const int ProgressRefreshInteval = 10000;   //10 seconds

        public static CloudBlobClient GetBlobClient()
        {
#if NO_EMULATOR            
            var blobClient = CloudStorageAccount.Parse(
                ConfigurationManager.AppSettings[Constants.ConfigurationSectionKey]).CreateCloudBlobClient();
#else
            var blobClient = (RoleEnvironment.IsAvailable) ? CloudStorageAccount.Parse(
                RoleEnvironment.GetConfigurationSettingValue(Constants.ConfigurationSectionKey)).CreateCloudBlobClient() :
                CloudStorageAccount.DevelopmentStorageAccount.CreateCloudBlobClient();
#endif
            return blobClient;
        }


        public static CloudTableClient GetTableClient()
        {
#if NO_EMULATOR            
            var tableClient = CloudStorageAccount.Parse(
                ConfigurationManager.AppSettings[Constants.ConfigurationSectionKey]).CreateCloudTableClient();
#else
            var tableClient = (RoleEnvironment.IsAvailable) ? CloudStorageAccount.Parse(
                RoleEnvironment.GetConfigurationSettingValue(Constants.ConfigurationSectionKey)).CreateCloudTableClient() :
                CloudStorageAccount.DevelopmentStorageAccount.CreateCloudTableClient();
#endif

            return tableClient;
        }

        public static CloudTable GetTable(string tablename)
        {
            return GetTableClient().GetTableReference(tablename);
        }


    }
}