using Microsoft.WindowsAzure.Storage;
//----------------------------------------------------------------------------------------------------------------------------
// <copyright file="WAMSConstants.cs" company="Microsoft Corporation">
//  Copyright 2011 Microsoft Corporation
// </copyright>
// Licensed under the MICROSOFT LIMITED PUBLIC LICENSE version 1.1 (the "License"); 
// You may not use this file except in compliance with the License. 
//---------------------------------------------------------------------------------------------------------------------------
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Table;
using System.Configuration;
namespace WAMS.MediaLib
{
    /// <summary>
    /// Constants used by the application.
    /// </summary>
    public static class WAMSConstants
    {
        /// <summary>
        /// Configuration section key containing connection string.
        /// </summary>
        public const string ConfigurationSectionKey = "DataConnectionString";

        /// <summary>
        /// Container where to upload files
        /// </summary>
        public const string ContainerName = "uploads";

        /// <summary>
        /// Number of bytes in a Kb.
        /// </summary>
        public const int BytesPerKb = 1024;

        /// <summary>
        /// Name of session element where attributes of file to be uploaded are saved.
        /// </summary>
        public const string FileAttributesSession = "FileClientAttributes";

        /// <summary>
        /// Partition Key names for the various coding standards in the Encoders table
        /// </summary>
        public const string AudioPreset = "Audio Coding Standard";
        public const string ThumbnailPreset = "Thumbnail";
        public const string VideoVC1Preset = "VC-1 Coding Standard";
        public const string VideoH264Preset = "H.264 Coding Standard";

        public const int DefaultRefreshInterval = 60000;   //60 seconds
        public const int ProgressRefreshInteval = 10000;   //10 seconds
        public const int TasksDelay = 100;   //100 milliseconds

        // ********************************
        // Authentication and connection settings.  These settings are pulled from 
        // the App.Config file and are required to connect to Media Services, 
        // authenticate, and get a token so that you can access the server context. 

#if NO_EMULATOR            
        public static readonly string AccountName = ConfigurationManager.AppSettings["accountName"];
        public static readonly string AccountKey = ConfigurationManager.AppSettings["accountKey"];
#else
        public static readonly string AccountName = (RoleEnvironment.IsAvailable) ?
                                                        RoleEnvironment.GetConfigurationSettingValue("MediaAccountName")
                                                        : ConfigurationManager.AppSettings["accountName"];
        public static readonly string AccountKey = (RoleEnvironment.IsAvailable) ?
                                                        RoleEnvironment.GetConfigurationSettingValue("MediaAccountKey")
                                                        : ConfigurationManager.AppSettings["accountKey"];
#endif

        public static CloudBlobClient GetBlobClient()
        {
#if NO_EMULATOR            
            var blobClient = CloudStorageAccount.Parse(
                ConfigurationManager.AppSettings[WAMSConstants.ConfigurationSectionKey]).CreateCloudBlobClient();
#else
            var blobClient = (RoleEnvironment.IsAvailable) ? CloudStorageAccount.Parse(
                RoleEnvironment.GetConfigurationSettingValue(WAMSConstants.ConfigurationSectionKey)).CreateCloudBlobClient() :
                CloudStorageAccount.DevelopmentStorageAccount.CreateCloudBlobClient();
#endif
            return blobClient;
        }


        public static CloudTableClient GetTableClient()
        {
#if NO_EMULATOR            
            var tableClient = CloudStorageAccount.Parse(
                ConfigurationManager.AppSettings[WAMSConstants.ConfigurationSectionKey]).CreateCloudTableClient();
#else
            var tableClient = (RoleEnvironment.IsAvailable) ? CloudStorageAccount.Parse(
                RoleEnvironment.GetConfigurationSettingValue(WAMSConstants.ConfigurationSectionKey)).CreateCloudTableClient() :
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