using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using Elmah;
using Microsoft.WindowsAzure.MediaServices.Client;
using Microsoft.WindowsAzure.Storage.Blob;
using System.Text;
using WAMS.MediaLib.Models;
using ApplicationException = System.ApplicationException;

namespace WAMS.MediaLib
{
    internal enum AssetType
    {
        SingleBitrate,
        MultiBitrate,
        SmoothStream
    }


    public class MediaServicesAPI
    {
        private const string PLAYREADYTASK = "PlayReady Protection Task";

        private const string TraceConfig = "Trace";
        private readonly CloudMediaContext _context = null;
        private static readonly object DependSync = new object();


        #region Events
        public delegate void PublishAssetEventHandler(object sender, AssetStatusArgs e);
        public event PublishAssetEventHandler AssetStatusChanged;

        #endregion


        //Public properties
        public static bool TraceEnabled { get; set; }
        public static string ConfigurationFolder { get; set; }
        public static string LastMessage { get; set; }

        public MediaServicesAPI(CloudMediaContext context)
        {
            _context = context;
#if NO_EMULATOR            
            TraceEnabled = ParseTraceConfig(ConfigurationManager.AppSettings[TraceConfig]);
#else
            TraceEnabled = (RoleEnvironment.IsAvailable) ? ParseTraceConfig(RoleEnvironment.GetConfigurationSettingValue(TraceConfig)) :
                                        ParseTraceConfig(ConfigurationManager.AppSettings[TraceConfig]);
#endif
            //_context.Assets.OnUploadProgress += new
            //      EventHandler<UploadProgressEventArgs>(OnUploadProgress);
            
            //Set default values for concurrent and parallel transfers
            _context.NumberOfConcurrentTransfers = 10;
            _context.ParallelTransferThreadCount = 10;

#if ELMAH
            //DEBUG ONLY
            WriteLog("DEBUG: A new Media Services job has been created on: " + DateTime.UtcNow.ToLongTimeString());
#endif

        }

        private MediaServicesAPI()
        {
        }


        private bool ParseTraceConfig(string traceValue)
        {
            if (traceValue.ToLower().Equals("on") || traceValue.ToLower().Equals("true")) return true;
            return false;
        }

        public static void WriteLog(string msg)
        {
            try
            {
                if (TraceEnabled)
                {
                    System.Diagnostics.Trace.WriteLine(msg);
                }
                LastMessage = msg;

                ErrorSignal.FromCurrentContext().Raise(new Exception(msg));
            }
            catch
            {
            }
        }


        public static void WriteLog(Exception ex)
        {
            try
            {
                if (TraceEnabled)
                {
                    System.Diagnostics.Trace.WriteLine(ex.Message);
                }
                LastMessage = ex.Message;

                ErrorSignal.FromCurrentContext().Raise(ex);
            }
            catch
            {
            }
         }



        /// <summary>
        /// Get a media processor reference. 
        /// </summary>
        /// <param name="mediaProcessor"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public static IMediaProcessor GetMediaProcessor(string mediaProcessor, CloudMediaContext context)
        {

            // Here are the possible strings that can be passed into the 
            // method for the mediaProcessor parameter:
            //   MP4 to Smooth Streams Task
            //   Windows Azure Media Encoder
            //   PlayReady Protection Task
            //   Smooth Streams to HLS Task
            //   Storage Decryption

            // Query for a media processor to get a reference.
            var theProcessor =
                                from p in context.MediaProcessors
                                where p.Name == mediaProcessor
                                select p;
            // Cast the reference to an IMediaprocessor.
            IMediaProcessor processor = theProcessor.First();

            if (processor == null)
            {
                throw new ArgumentException(string.Format(System.Globalization.CultureInfo.CurrentCulture,
                    "Unknown processor",
                    mediaProcessor));
            }
            return processor;
        }



        /// <summary>
        /// Shows how to encode an input media file using a preset string, encrypt 
        /// the output file with storage encryption, and then decrypt it. The method
        /// uses a sequence of chained tasks. 
        /// </summary>
        /// <param name="asset"></param>
        /// <param name="assetCollection"></param>
        /// <param name="mediaProcessor"></param>
        /// <param name="encryption"></param>
        /// <returns>IJob</returns>
        public IJob CreateEncodingJob(IAsset asset, IDictionary<string,string> assetCollection, 
                                            string mediaProcessor, AssetCreationOptions encryption)
        {
#if ELMAH
            //DEBUG ONLY
            WriteLog("DEBUG: Entering CreateEncodingJob()");
#endif

            // Declare a new job.
            var job = _context.Jobs.Create(asset.Name);

            // Set up the first task to encode the input file.
            // Get a media processor reference, and pass to it the name of the 
            // processor to use for the specific task.
            var processor = GetMediaProcessor(mediaProcessor, _context);

            //Find all the encoders assigned to the current asset
            var assetKeys = assetCollection.Keys.Where(k => asset.Name.StartsWith(k.Substring(0, k.IndexOf(';'))));


            // Create a task with the encoding details, using a string preset.
            foreach (var taskName in assetKeys)
            {
                var configuration = assetCollection[taskName];

                if (!String.IsNullOrEmpty(ConfigurationManager.AppSettings[configuration]))
                {
                    //Read encoder preset from the configuration file
                    configuration = File.ReadAllText(Path.GetFullPath(ConfigurationFolder +
                                                ConfigurationManager.AppSettings[configuration]));
                }

                //Create a new Task
                var encodeTask = job.Tasks.AddNew(taskName,
                                        processor,
                                        configuration,
#if O365 || WEBSITES
                                        TaskOptions.None);
#else
                                        TaskOptions.ProtectedConfiguration);
#endif
                // Specify the input asset to be encoded.
                encodeTask.InputAssets.Add(asset);

                // Specify the storage-encrypted output asset.
                encodeTask.OutputAssets.AddNew(taskName + "_OutputAsset", encryption);

                switch (encryption)
                {
                    case AssetCreationOptions.CommonEncryptionProtected:
                        break;
                    case AssetCreationOptions.EnvelopeEncryptionProtected:
                        AddPlayReadyProtectionTask(job, encodeTask, asset.Name, _context);
                        break;
                    case AssetCreationOptions.StorageEncrypted:
                        AddStorageDecryptionTask(job, encodeTask, asset.Name, _context);
                        break;
                    case AssetCreationOptions.None:
                    default:
                        break;
                }

            }


            //Add a thumbnail task
            var task = job.Tasks.FirstOrDefault();
            if (task == null)
            {
                var newExc = new Exception("No tasks were created. Exiting...");
                WriteLog(newExc);
                throw newExc;
            }    
            CreateThumbnailTask(job, asset, task.Name);

            return job;
        }





        /// <summary>
        /// Create a Thumbnail job
        /// </summary>
        /// <param name="asset"></param>
        /// <returns></returns>
        public IJob CreateThumbnailJob(IAsset asset)
        {
            //Configuration string for the Thumbnails
            const string tnConfig = "Thumbnails";

            // Get a media processor reference, and pass to it the name of the 
            // processor to use for the specific task.
            var processor = GetMediaProcessor("Windows Azure Media Encoder", _context);

            // Declare a new job.
            var job = _context.Jobs.Create(asset.Name + ";" + tnConfig);

            // Create the Thumbnails task
            var task = job.Tasks.AddNew(asset.Name + ";" + tnConfig,
                                    processor,
                                    tnConfig,
#if O365 || WEBSITES
                                    TaskOptions.None);
#else
                                    TaskOptions.ProtectedConfiguration);
#endif

            // Specify the input asset to be encoded.
            task.InputAssets.Add(asset);

            // Specify the storage-encrypted output asset.
            task.OutputAssets.AddNew(asset.Name + ";" + tnConfig, AssetCreationOptions.None);

            
            return job;
        }



        /// <summary>
        /// Creates Thumbnail task
        /// </summary>
        /// <param name="job"></param>
        /// <param name="asset"></param>
        /// <param name="taskName"></param>
        /// <returns></returns>
        public IJob CreateThumbnailTask(IJob job, IAsset asset, string taskName)
        {
            //Configuration string for the Thumbnails
            const string imgName = "Thumbnails";

            // Get a media processor reference, and pass to it the name of the 
            // processor to use for the specific task.
            var processor = GetMediaProcessor("Windows Azure Media Encoder", _context);

            // Create the Thumbnails task
            var task = job.Tasks.AddNew(taskName + ";" + imgName,
                                    processor,
                                    imgName,
#if O365 || WEBSITES
                                    TaskOptions.None);
#else
                                    TaskOptions.ProtectedConfiguration);
#endif
            // Specify the input asset to be encoded.
            task.InputAssets.Add(asset);

            // Specify the storage-encrypted output asset.
            task.OutputAssets.AddNew(taskName + ";" + imgName, AssetCreationOptions.None);


            return job;
        }


        #region Async Ingest Methods

        /// <summary>
        /// Ingest media asset asyncrhonously
        /// </summary>
        /// <param name="context"></param>
        /// <param name="mediaAsset"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        public string IngestAssetAsync(CloudMediaContext context, FileUploadModel mediaAsset, AssetCreationOptions options)
        {
            //Create an empty asset
            var inputAsset = context.Assets.Create(mediaAsset.FileName + "_" + Guid.NewGuid(), options);

            try
            {
                //Create a locator to get the SAS url
                var writePolicy = context.AccessPolicies.Create(mediaAsset.FileName,
                    TimeSpan.FromHours(Int32.Parse(ConfigurationManager.AppSettings["MaxCopyTimeout"])),
                    AccessPermissions.Write | AccessPermissions.List);
                var destLocator = context.Locators.CreateSasLocator(inputAsset, writePolicy, DateTime.UtcNow.AddMinutes(-5));

                //Save the temp blob container in the returning string
                var tempBlobContainer = destLocator.Path.Split('?')[0];

                //Create the reference to the destination container:
                var blobClient = WAMSConstants.GetBlobClient();
                var destContainerName = (new Uri(destLocator.Path)).Segments[1];
                var destContainer = blobClient.GetContainerReference(destContainerName);

                //Validate the source blob
                mediaAsset.Blob.FetchAttributes();
                var sourceLength = mediaAsset.Blob.Properties.Length;
                if (sourceLength == 0)
                {
                    const string msg = "Source File cannot be empty!";  
                    WriteLog(msg); 
                    throw new ApplicationException(msg);
                }

                //If we got here then we can assume the source is valid and accessible.
                //Create destination blob for copy
                var destFileBlob = destContainer.GetBlockBlobReference(mediaAsset.FileName);


                //Begin async copy
                destFileBlob.BeginStartCopyFromBlob(mediaAsset.Blob, (result) =>
                    {
                        var blobDest = (CloudBlockBlob)result.AsyncState;

                        // End the operation.
                        blobDest.EndStartCopyFromBlob(result);

                        //Publish asset
                        PublishAsset(inputAsset, blobDest, mediaAsset.FileName, sourceLength);

                        //Clean up
                        destLocator.Delete();
                        writePolicy.Delete();

                    }, destFileBlob);  // Begin asynchronous copy of the blob


                return tempBlobContainer;
            }
            catch (Exception ex)
            {
                WriteLog(ex.Message);
                throw;
            }
        }



        /// <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        /// <param name="mediaAsset"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        public string IngestAsset(CloudMediaContext context, FileUploadModel mediaAsset, AssetCreationOptions options)
        {
            //Create an empty asset
            var inputAsset = context.Assets.Create(mediaAsset.FileName + "_" + Guid.NewGuid(), options);

            try
            {
                //Create a locator to get the SAS url
                var writePolicy = context.AccessPolicies.Create(mediaAsset.FileName,
                    TimeSpan.FromHours(Int32.Parse(ConfigurationManager.AppSettings["MaxCopyTimeout"])),
                    AccessPermissions.Write | AccessPermissions.List);
                var destLocator = context.Locators.CreateSasLocator(inputAsset, writePolicy, DateTime.UtcNow.AddMinutes(-5));

                //Save the temp blob container in the returning string
                var tempBlobContainer = destLocator.Path.Split('?')[0];

                //Create the reference to the destination container:
                var blobClient = WAMSConstants.GetBlobClient();
                var destContainerName = (new Uri(destLocator.Path)).Segments[1];
                var destContainer = blobClient.GetContainerReference(destContainerName);

                //Validate the source blob
                mediaAsset.Blob.FetchAttributes();
                var sourceLength = mediaAsset.Blob.Properties.Length;
                if (sourceLength == 0)
                {
                    const string msg = "Source File cannot be empty!";
                    WriteLog(msg);
                    throw new ApplicationException(msg);
                }

                //If we got here then we can assume the source is valid and accessible.
                //Create destination blob for copy
                var destFileBlob = destContainer.GetBlockBlobReference(mediaAsset.FileName);

                //Begin sync copy
                var opstatus = destFileBlob.StartCopyFromBlob(mediaAsset.Blob);
                //Publish asset
                PublishAsset(inputAsset, destFileBlob, mediaAsset.FileName, sourceLength);

                //Clean up
                destLocator.Delete();
                writePolicy.Delete();

                return tempBlobContainer;
            }
            catch (Exception ex)
            {
                WriteLog(ex.Message);
                throw;
            }
        }


        public static IAsset RefreshAsset(IAsset asset, CloudMediaContext context)
        {
            asset = context.Assets.Where(a => a.Id == asset.Id).FirstOrDefault();
            return asset;
        }


        private void PublishAsset(IAsset inputAsset, ICloudBlob blobDest, string filename, long sourceLength )
        {
            if (!blobDest.Exists())
            {
                const string msg = "Failed to copy Source blob";
                WriteLog(msg);
                throw new ApplicationException(msg);
            }

            //Publish asset, so it can be encoded next
            var destinationAssetFile = inputAsset.AssetFiles.Create(filename);
            destinationAssetFile.IsPrimary = true;
            destinationAssetFile.ContentFileSize = sourceLength; //setter only available in SDK > v2.0.0.5
            destinationAssetFile.Update();

            inputAsset = RefreshAsset(inputAsset, _context);

#if ELMAH
            //DEBUG ONLY!!!
            WriteLog("DEBUG: Successfullly completed ingesting");
#endif

            //Fire the event for the publishing asset
            OnAssetStatusChanged(new AssetStatusArgs(inputAsset));
        }

 
        #endregion


        #region Private Methods


        /// <summary>
        /// 
        /// </summary>
        /// <param name="job"></param>
        /// <param name="chainedTask"></param>
        /// <param name="taskName"></param>
        /// <param name="context"></param>
        private static ITask AddPlayReadyProtectionTask(IJob job, ITask chainedTask, string taskName, CloudMediaContext context)
        {
            // Read the configuration configuration data into a string. 
            string playReadyConfig = File.ReadAllText(Path.GetFullPath(ConfigurationFolder +
                                            ConfigurationManager.AppSettings[PLAYREADYTASK]));

            // Get a media processor reference, and pass to it the name of the 
            // processor to use for the specific task.
            IMediaProcessor playreadyProcessor = GetMediaProcessor("Windows Azure Media Encryptor", context);

            // Create the PlayReady Task
            ITask playreadyTask = job.Tasks.AddNew(taskName,
                                    playreadyProcessor,
                                    playReadyConfig,
#if O365 || WEBSITES
                                    TaskOptions.None);
#else
                                    TaskOptions.ProtectedConfiguration);
#endif

            // Specify the input asset to be protected. This is the output 
            // asset from the last task in the job.
            playreadyTask.InputAssets.Add(chainedTask.OutputAssets.Last());

            // Add an output asset to contain the results of the job. Persist the output by setting 
            // the shouldPersistOutputOnCompletion param to true.
            playreadyTask.OutputAssets.AddNew(taskName,
                                                AssetCreationOptions.None);

            return playreadyTask;
        }



 

        /// <summary>
        /// 
        /// </summary>
        /// <param name="job"></param>
        /// <param name="chainedTask"></param>
        /// <param name="taskName"></param>
        /// <returns></returns>
        private static ITask AddStorageDecryptionTask(IJob job, ITask chainedTask, string taskName, CloudMediaContext context)
        {
            // Declare another media proc for a storage decryption task.
            IMediaProcessor decryptProcessor = GetMediaProcessor("Storage Decryption", context);

            // Declare the decryption task.
            ITask decryptTask = job.Tasks.AddNew(taskName,
                                    decryptProcessor,
                                    string.Empty,
                                    TaskOptions.None);

            // Specify the input asset to be decrypted. This is the output 
            // asset from the last task in the job.
            decryptTask.InputAssets.Add(chainedTask.OutputAssets.Last());

            // Specify an output asset to contain the results of the job. 
            // This should have AssetCreationOptions.None. 
            decryptTask.OutputAssets.AddNew(taskName,
                                                AssetCreationOptions.None);

            return decryptTask;
        }

 


        /// <summary>
        /// 
        /// </summary>
        /// <param name="assetId"></param>
        /// <returns></returns>
        private IAsset GetAsset(string assetId)
        {
            // Use a LINQ Select query to get an asset.
            var asset =
                from a in _context.Assets
                where a.Id == assetId
                select a;
            // Reference the asset as an IAsset.
            IAsset theAsset = asset.FirstOrDefault();

            // Check whether asset exists.
            if (theAsset != null)
                return theAsset;
            else
                return null;
        }


        /// <summary>
        /// **********
        /// Optional code.  Code after this point is not required for 
        /// an encoding job, but shows how to access the assets that 
        /// are the output of a job, either by creating URLs to the 
        /// asset on the server, or by downloading. 
        /// **********
        /// Progress Job's process and gets output results
        /// </summary>
        /// <param name="job"></param>
        /// <param name="accessPolicy"></param>
        /// <returns></returns>
        public Dictionary<string, KeyValuePair<string, string>> GetUrls(IJob job, IAccessPolicy accessPolicy)
        {
            const string imgName = "Thumbnails";
            var urlList = new Dictionary<string, KeyValuePair<string, string>>();

            try
            {
                //Obtain the thumbnail output asset
                var thAsset = job.OutputMediaAssets.FirstOrDefault(a => a.Name.Contains(imgName));

                // Cast the reference as an IAsset.
                foreach (var outputAsset in job.OutputMediaAssets)
                {
                    if (outputAsset == thAsset) continue;

                    //Get locator for the thumbnail image
                    var thLocator = _context.Locators.CreateLocator(LocatorType.Sas, thAsset,
                                                                                accessPolicy,
                                                                                DateTime.UtcNow.AddMinutes(-5));

                    var excludedExtensionList = new List<string> {".xml"};

                    //Determine the asset type
                    var assetType = GetAssetType(outputAsset.AssetFiles);


                    switch (assetType)
                    {
                        case AssetType.SingleBitrate:
                            // Create a SAS locator to enable direct access to the asset. 
                            // We set the optional startTime param as 5 minutes 
                            // earlier than Now to compensate for differences in time  
                            // between the client and server clocks. 
                            var sasLocator = _context.Locators.CreateLocator(LocatorType.Sas, outputAsset,
                                                                                        accessPolicy,
                                                                                        DateTime.UtcNow.AddMinutes(-5));
                            if (sasLocator.Path == null) throw new Exception("Locator's Path cannot be null!");

                            // Build a list of SAS URLs to each file in the asset. 
                            urlList.Add(outputAsset.Id + "SAS",
                                new KeyValuePair<string, string>(CreateAssetUrl(outputAsset, sasLocator, excludedExtensionList),
                                    CreateAssetUrl(thAsset, thLocator, excludedExtensionList)));
                            break;
                        
                        case AssetType.MultiBitrate:
                            excludedExtensionList.Add(".ism");
                            excludedExtensionList.Add(".ismc");
                            excludedExtensionList.Add(".ismv");
                            // Create a SAS locator to enable direct access to the asset. 
                            var sasLocator1 = _context.Locators.CreateLocator(LocatorType.Sas, outputAsset,
                                                                                        accessPolicy,
                                                                                        DateTime.UtcNow.AddMinutes(-5));
                            if (sasLocator1.Path == null) throw new Exception("Locator's Path cannot be null!");

                            // Build a list of SAS URLs to each file in the asset. 
                            urlList.Add(outputAsset.Id + ";SAS",
                                new KeyValuePair<string, string>(CreateAssetUrl(outputAsset, sasLocator1, excludedExtensionList),
                                    CreateAssetUrl(thAsset, thLocator, excludedExtensionList)));

                            //Now, let's add an OnDemand Delivery locator to the list
                            excludedExtensionList.Remove(".ism");
                            excludedExtensionList.Add(".mp4");

                            //Get locator to the OnDemand origin
                            var originLocator2 = _context.Locators.CreateLocator(LocatorType.OnDemandOrigin, outputAsset,
                                                                                        accessPolicy,
                                                                                        DateTime.UtcNow.AddMinutes(-5));
                            if (originLocator2.Path == null) throw new Exception("Locator's Path cannot be null!");


                            // Build a list of OnDemand Delivery URLs to each file in the asset. 
                            urlList.Add(outputAsset.Id + ";ODO",
                                new KeyValuePair<string, string>(CreateAssetUrl(outputAsset, originLocator2, excludedExtensionList),
                                    CreateAssetUrl(thAsset, thLocator, excludedExtensionList)));

                            break;

                        case AssetType.SmoothStream:
                            excludedExtensionList.Add(".ismc");
                            excludedExtensionList.Add(".ismv");

                            //Get locator to the OnDemand origin
                            var originLocator = _context.Locators.CreateLocator(LocatorType.OnDemandOrigin, outputAsset,
                                                                                        accessPolicy,
                                                                                        DateTime.UtcNow.AddMinutes(-5));
                            if (originLocator.Path == null) throw new Exception("Locator's Path cannot be null!");


                            // Build a list of OnDemand Delivery URLs to each file in the asset. 
                            urlList.Add(outputAsset.Id + ";ODO",
                                new KeyValuePair<string, string>(CreateAssetUrl(outputAsset, originLocator, excludedExtensionList),
                                    CreateAssetUrl(thAsset, thLocator, excludedExtensionList)));

                            break;
                        default:
                            throw new ArgumentOutOfRangeException("assetType");
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog(ex.Message);
                throw;
            }


            return urlList;
        }



        /// <summary>
        /// 
        /// </summary>
        /// <param name="assets"></param>
        /// <returns></returns>
        private AssetType GetAssetType(AssetFileBaseCollection assets)
        {
            IQueryable<IAssetFile> asset;
            //Try to obtain the ism output asset (for Smooth and Adaptive streams)
            asset = from a in assets
                    where a.Name.Contains(".ismv")
                    select a;
            if (asset.Count() > 0) return AssetType.SmoothStream;

            asset = from a in assets
                    where a.Name.Contains(".ism")
                    select a;
            if (asset.Count() > 0) return AssetType.MultiBitrate;

            return AssetType.SingleBitrate;
        }


        // Create a list of URLs to all files in an asset.
        private static string CreateAssetUrl(IAsset asset, ILocator locator, IList<string> excludedFileExt )
        {
            var sasUrl = String.Empty;

            if (asset.AssetFiles.Count() > 0)
            {
                // If the asset has files, build a list of URLs to  each file in the asset and return. 
                foreach (var file in asset.AssetFiles)
                {
                    if(excludedFileExt.Contains(Path.GetExtension(file.Name))) continue;
                    sasUrl = BuildFileSasUrl(file.Name, locator);
                }
            }
            else
            {
                //If asset doesn't have files, let's try to pull out the files from the locator's blob container
                var blobpath = locator.Path.Split('?')[0];
                var blobClient = WAMSConstants.GetBlobClient();

                foreach (var file in blobClient.ListBlobs(blobpath))
                {
                    if (excludedFileExt.Contains(Path.GetExtension(file.Uri.AbsolutePath))) continue;
                    sasUrl = BuildFileSasUrl(file.Uri.AbsolutePath, locator);
                }

            }

            return sasUrl;
        }



        // Create and return a SAS URL to a single file in an asset. 
        private static string BuildFileSasUrl(string filepath, ILocator locator)
        {
            // Take the locator path, add the file name, and build 
            // a full SAS URL to access this file. This is the only 
            // code required to build the full URL.
            var uriBuilder = new UriBuilder(locator.Path);
            uriBuilder.Path += (Path.GetExtension(filepath).ToLower() == ".ism")?
                filepath + "/manifest" :
                "/" + filepath;

            //Return the SAS URL.
            return uriBuilder.Uri.AbsoluteUri;
        }



        // Create an origin locator URL to an Apple HLS streaming asset 
        // on an origin server.
        private string GetStreamingHLSOriginLocator(string targetAssetID)
        {
            // Get a reference to the asset you want to stream.
            IAsset assetToStream = GetAsset(targetAssetID);

            // Get a reference to the manifest file from the collection 
            // of files in the asset. 
            var theManifest =
                                from f in assetToStream.AssetFiles
                                where f.Name.EndsWith(".ism")
                                select f;
            // Cast the reference to a true IFileInfo type. 
            IAssetFile manifestFile = theManifest.First();

            // Create an 1-day readonly access policy. 
            IAccessPolicy streamingPolicy = _context.AccessPolicies.Create("Streaming policy",
                TimeSpan.FromDays(1),
                AccessPermissions.Read);
            ILocator originLocator = _context.Locators.CreateSasLocator(assetToStream,
                streamingPolicy,
                DateTime.UtcNow.AddMinutes(-5));

            // Create a full URL to the manifest file. Use this for playback
            // in streaming media clients. 
            string urlForClientStreaming = originLocator.Path
                + manifestFile.Name + "/manifest";

            return urlForClientStreaming;
        }





        /// <summary>
        /// 
        /// </summary>
        /// <param name="job"></param>
        public static void LogJobStop(IJob job)
        {
            StringBuilder builder = new StringBuilder();

            builder.AppendLine("\nThe job stopped due to cancellation or an error.");
            builder.AppendLine("***************************");
            builder.AppendLine("Job ID: " + job.Id);
            builder.AppendLine("Job Name: " + job.Name);
            builder.AppendLine("Job State: " + job.State.ToString());
            builder.AppendLine("Job started (server UTC time): " + job.StartTime.ToString());
            // Log job errors if they exist.  
            if (job.State == JobState.Error)
            {
                builder.Append("Error Details: \n");
                foreach (ITask task in job.Tasks)
                {
                    foreach (ErrorDetail detail in task.ErrorDetails)
                    {
                        builder.AppendLine("  Task Id: " + task.Id);
                        builder.AppendLine("    Error Code: " + detail.Code);
                        builder.AppendLine("    Error Message: " + detail.Message + "\n");
                    }
                }
            }
            builder.AppendLine("***************************\n");
            // Write the output to a default output stream.
            WriteLog(builder.ToString());
        }





        #endregion


        #region Event Handlers

        protected void OnAssetStatusChanged(AssetStatusArgs e)
        {
            WriteLog(String.Format("{0} asset's state changed to {1}",
                            e.Asset.Name, e.Asset.State));
            AssetStatusChanged(this, e);
        }

        #endregion


    }





    public class AssetStatusArgs : EventArgs
    {
        public IAsset Asset;

        public AssetStatusArgs(IAsset asset)
        {
            Asset = asset;
        }

        public AssetStatusArgs() { }
    }



    public class MediaProcessStatusArgs : EventArgs
    {
        public DateTime Created { get; private set; }
        public DateTime? EndTime { get; private set; }
        public string Id { get; private set; }
        public DateTime LastModified { get; private set; }
        public string Name { get; private set; }
        public int Priority { get; private set; }
        public TimeSpan RunningDuration { get; private set; }
        public DateTime? StartTime { get; private set; }
        public JobState JobState { get; private set; }
        public TaskCollection Tasks { get; private set; }
        public string TemplateId { get; private set; }
        public string JobStatus { get; private set; }

        public MediaProcessStatusArgs(IJob job)
        {
            this.Created = job.Created;
            EndTime = job.EndTime;
            Id = job.Id;
            LastModified = job.LastModified;
            Name = job.Name;
            Priority = job.Priority;
            RunningDuration = job.RunningDuration;
            StartTime = job.StartTime;
            Tasks = job.Tasks;
            TemplateId = job.TemplateId;

            switch (job.State)
            {
                case JobState.Canceled:
                    JobStatus = "Canceled";
                    break;
                case JobState.Canceling:
                    JobStatus = "Canceling";
                    break;
                case JobState.Error:
                    JobStatus = "Error";
                    break;
                case JobState.Finished:
                    JobStatus = "Finished";
                    break;
                case JobState.Processing:
                    JobStatus = "Processing";
                    break;
                case JobState.Queued:
                    JobStatus = "Queued";
                    break;
                case JobState.Scheduled:
                    JobStatus = "Scheduled";
                    break;
                default:
                    break;
            }
        }
    }


}