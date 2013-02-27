using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Threading;
//using System.Threading.Tasks;
using System.Web;
using Microsoft.WindowsAzure.MediaServices.Client;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Storage.Blob;
using System.Threading.Tasks;
using System.Text;

namespace MediaServicesPortal
{
    public class MediaServicesAPI
    {
        private const string PLAYREADYTASK = "PlayReady Protection Task";

        private const string traceConfig = "Trace";
        private CloudMediaContext _context = null;
        private static object dependSync = new object();

        private int _previousProgressPercentage;

        #region Events
        public delegate void UploadEventHandler(object sender, UploadStatusArgs e);
        public event UploadEventHandler UploadStatusChanged;

        public delegate void PublishAssetEventHandler(object sender, AssetStatusArgs e);
        public event PublishAssetEventHandler AssetStatusChanged;

        public delegate void MediaProcessEventHandler(object sender, MediaProcessStatusArgs e);
        public event MediaProcessEventHandler MediaProcessStatusChanged;

        #endregion


        //Public properties
        public static bool TraceEnabled { get; set; }
        public static string ConfigurationFolder { get; set; }

        public MediaServicesAPI(CloudMediaContext context)
        {
            _context = context;
            TraceEnabled = (RoleEnvironment.IsAvailable) ? ParseTraceConfig(RoleEnvironment.GetConfigurationSettingValue(traceConfig)) :
                                        ParseTraceConfig(ConfigurationManager.AppSettings[traceConfig]);

            //_context.Assets.OnUploadProgress += new
            //      EventHandler<UploadProgressEventArgs>(OnUploadProgress);
            
            //Set default values for concurrent and parallel transfers
            _context.NumberOfConcurrentTransfers = 10;
            _context.ParallelTransferThreadCount = 10;

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
            if (TraceEnabled)
            {
                System.Diagnostics.Trace.WriteLine(msg);
            }
        }

        #region Media Processing Tasks


        /// <summary>
        /// **********
        /// Optional code.  Code after this point is not required for 
        /// an encoding job, but shows how to access the assets that 
        /// are the output of a job, either by creating URLs to the 
        /// asset on the server, or by downloading. 
        /// **********
        /// Progress Job's process and gets output results
        /// </summary>
        /// <param name="jobId"></param>
        /// <param name="accessPolicy"></param>
        /// <returns></returns>
        public Dictionary<string, string> GetUrls(string jobId, IAccessPolicy accessPolicy)
        {
            Dictionary<string, string> sasUrlList = new Dictionary<string, string>();

            // Checks job progress and get results. 
            //if (!CheckJobProgress(jobId))
            //    throw new Exception("Job failed");

            lock (dependSync)
            {
                // Get a refreshed job reference after waiting on a thread.
                IJob job = GetJob(jobId);

                // Query for the decrypted output asset, which is the one 
                // that you set to not use storage encryption.
                var outputAssets =
                                     from a in job.OutputMediaAssets
                                     where a.Name == job.Name
                                     select a;

                try
                {
                    // Cast the reference as an IAsset.
                    foreach (IAsset outputAsset in outputAssets)
                    {
                        // Get a reference to the manifest file from the collection 
                        // of files in the asset. 
                        var theManifest =
                                            from f in outputAsset.AssetFiles
                                            where f.Name.EndsWith(".ism")
                                            select f;

                        if (theManifest != null && theManifest.Count() > 0)
                        {
                            // Cast the reference to a true IFileInfo type. 
                            IAssetFile manifestFile = theManifest.First();

                            // Create a SAS locator to enable direct access to the asset. 
                            // We set the optional startTime param as 5 minutes 
                            // earlier than Now to compensate for differences in time  
                            // between the client and server clocks. 
                            ILocator originLocator = _context.Locators.CreateLocator(LocatorType.Sas, outputAsset,
                                accessPolicy,
                                DateTime.UtcNow.AddMinutes(-5));

                            if (originLocator.Path == null) throw new Exception("Locator's Path cannot be null!");

                            // Create a full URL to the manifest file. Use this for playback
                            // in streaming media clients. 
                            string urlForClientStreaming = originLocator.Path + manifestFile.Name + "/manifest";
                            // Build a list of SAS URLs to each file in the asset. 
                            sasUrlList.Add(outputAsset.Id, urlForClientStreaming);
                        }
                        else
                        {
                            // Create a SAS locator to enable direct access to the asset. 
                            // We set the optional startTime param as 5 minutes 
                            // earlier than Now to compensate for differences in time  
                            // between the client and server clocks. 
                            ILocator originLocator = _context.Locators.CreateLocator(LocatorType.Sas, outputAsset,
                                                    accessPolicy,
                                                    DateTime.UtcNow.AddMinutes(-5));

                            if (originLocator.Path == null) throw new Exception("Locator's Path cannot be null!");

                            // Build a list of SAS URLs to each file in the asset. 
                            sasUrlList.Add(outputAsset.Id, CreateAssetSasUrl(outputAsset, originLocator));
                        }
                    }
                }
                catch (Exception ex)
                {
                    WriteLog(ex.Message);
                    throw ex;
                }
            }


            return sasUrlList;
        }


        ///// <summary>
        ///// 
        ///// </summary>
        ///// <param name="jobId"></param>
        ///// <param name="accessPolicy"></param>
        ///// <returns></returns>
        //public IEnumerable<IAsset> GetOutputAssets(string jobId, IAccessPolicy accessPolicy)
        //{
        //    // Checks job progress and get results. 
        //    if (!CheckJobProgress(jobId))
        //        throw new Exception("Job failed");

        //    // Get a refreshed job reference after waiting on a thread.
        //    IJob job = GetJob(jobId);

        //    // Query for the decrypted output asset, which is the one 
        //    // that you set to not use storage encryption.
        //    var decryptedAssets =
        //                         from a in job.OutputMediaAssets
        //                         where a.Options == AssetCreationOptions.None
        //                         select a;

        //    return decryptedAssets;

        //}



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
        /// <param name="context"></param>
        /// <param name="encryption"></param>
        /// <returns>IJob</returns>
        public static IJob CreateEncodingJob(IAsset asset, Dictionary<string,string> assetCollection, 
                                            string mediaProcessor, CloudMediaContext context,
                                            AssetCreationOptions encryption)
        {
            // Declare a new job.
            IJob job = context.Jobs.Create(asset.Name);

            // Set up the first task to encode the input file.
            // Get a media processor reference, and pass to it the name of the 
            // processor to use for the specific task.
            IMediaProcessor processor = GetMediaProcessor(mediaProcessor, context);

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
                ITask encodeTask = job.Tasks.AddNew(taskName,
                                        processor,
                                        configuration,
                                        TaskOptions.ProtectedConfiguration);

                // Specify the input asset to be encoded.
                encodeTask.InputAssets.Add(asset);

                // Specify the storage-encrypted output asset.
                encodeTask.OutputAssets.AddNew(taskName + "_OutputAsset",
                    encryption);

                ITask nextTask = null;

                switch (encryption)
                {
                    case AssetCreationOptions.CommonEncryptionProtected:
                        break;
                    case AssetCreationOptions.EnvelopeEncryptionProtected:
                        nextTask = AddPlayReadyProtectionTask(job, encodeTask, asset.Name, context);
                        break;
                    case AssetCreationOptions.StorageEncrypted:
                        nextTask = AddStorageDecryptionTask(job, encodeTask, asset.Name, context);
                        break;
                    case AssetCreationOptions.None:
                    default:
                        nextTask = encodeTask;
                        break;
                }


                //Create a thumbnail image from a video file
                nextTask = AddThumbnailTask(job, nextTask, asset.Name + "_Thumbnail", context);


            }


            return job;
        }



        #region Sync Ingest Asset Methods


        /// <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        /// <param name="filepath"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        //public static IAsset IngestAsset(CloudMediaContext context, string filepath, AssetCreationOptions options)
        //{
        //    IAsset inputAsset = null;
        //    RetryableAction.Execute(() =>
        //    {
        //        inputAsset = context.Assets.Create(filepath, options);
        //    }, ThrowIf.RetryCountIs(3), SleepFor.RandomExponential(250));

        //    return inputAsset;
        //}



        /// <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        /// <param name="files"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        //public static IAsset IngestAsset(CloudMediaContext context, string[] files, AssetCreationOptions options)
        //{
        //    IAsset inputAsset = null;
        //    RetryableAction.Execute(() =>
        //    {
        //        inputAsset = context.Assets.Create(files, options);
        //    }, ThrowIf.RetryCountIs(3), SleepFor.RandomExponential(250));

        //    return inputAsset;
        //}



        /// <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        /// <param name="fileName"></param>
        /// <param name="srcContainer"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        //public IAsset IngestAsset(CloudMediaContext context, string fileName, 
        //    CloudBlockBlob srcFileBlob, CloudBlobClient mediaStorageClient, AssetCreationOptions options)
        //{
        //    //Create an empty asset
        //    IAsset inputAsset = context.Assets.CreateEmptyAsset(fileName + "_" + Guid.NewGuid().ToString(), options);

        //    RetryableAction.Execute(() =>
        //    {
        //        //Create a locator to get the SAS url
        //        IAccessPolicy writePolicy = context.AccessPolicies.Create("Copy", 
        //            TimeSpan.FromHours(Int32.Parse(ConfigurationManager.AppSettings["MaxCopyTimeout"])), 
        //            AccessPermissions.Write | AccessPermissions.List);
        //        ILocator destLocator = context.Locators.CreateSasLocator(inputAsset, writePolicy, DateTime.UtcNow.AddMinutes(-5));

        //        //Create the reference to the destination container:
        //        string destContainerName = (new Uri(destLocator.Path)).Segments[1];
        //        CloudBlobContainer destContainer = mediaStorageClient.GetContainerReference(destContainerName);

        //        //Validate the source blob
        //        srcFileBlob.FetchAttributes();
        //        long sourceLength = srcFileBlob.Properties.Length;
        //        System.Diagnostics.Debug.Assert(sourceLength > 0);

        //        //If we got here then we can assume the source is valid and accessible.
        //        //Create destination blob for copy
        //        CloudBlockBlob destFileBlob = destContainer.GetBlobReference(fileName);
        //        destFileBlob.CopyFromBlob(srcFileBlob);  // Will fail here if project references are bad (the are lazy loaded).

        //        //Check destination blob:
        //        try
        //        {
        //            //If we got here then the copy worked.
        //            destFileBlob.FetchAttributes();
        //            System.Diagnostics.Debug.Assert(destFileBlob.Properties.Length == sourceLength);
        //        }
        //        catch { }
            
        //        //Publish the asset:
        //        inputAsset.Publish();
        //        inputAsset = RefreshAsset(context, inputAsset);

        //        //Fire the event for the publishing asset
        //        OnAssetStatusChanged(new AssetStatusArgs(inputAsset));


        //    }, ThrowIf.RetryCountIs(3), SleepFor.RandomExponential(250));

        //    return inputAsset;
        //}

        #endregion

        #region Async Ingest Methods


        /// <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        /// <param name="filepath"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        //public static System.Threading.Tasks.Task<IAsset> IngestAssetAsync(CloudMediaContext context, string filepath, AssetCreationOptions options)
        //{
        //    return context.Assets.CreateAsync(new string[] { filepath }, filepath, options, CancellationToken.None);
        //}



        /// <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        /// <param name="fileName"></param>
        /// <param name="srcFileBlob"></param>
        /// <param name="mediaStorageClient"></param>
        /// <param name="options"></param>
        /// <param name="tempBlobContainer">out</param>
        /// <returns></returns>
        public IAsset IngestAssetAsync(CloudMediaContext context, string fileName, 
            CloudBlockBlob srcFileBlob, CloudBlobClient mediaStorageClient, AssetCreationOptions options, out string tempBlobContainer)
        {
            //Create an empty asset
            IAsset inputAsset = context.Assets.Create(fileName + "_" + Guid.NewGuid().ToString(), options);

            
            var assetFile = inputAsset.AssetFiles.Create(fileName);

            var accessPolicy = context.AccessPolicies.Create(inputAsset.AssetFiles.First().Asset.Name, TimeSpan.FromDays(3),
                                AccessPermissions.Write | AccessPermissions.List);

            var locator = context.Locators.CreateLocator(LocatorType.Sas, inputAsset, accessPolicy);

            var blobTransferClient = new BlobTransferClient();

            blobTransferClient.TransferProgressChanged += blobTransferClient_TransferProgressChanged;


            //Save the temp blob container in the returning string
            tempBlobContainer = locator.Path.Split('?')[0];

            var uploadTasks = new List<Task>();

            uploadTasks.Add(assetFile.UploadAsync(srcFileBlob.Uri.AbsolutePath, blobTransferClient, locator,CancellationToken.None));

            Task.WaitAll(uploadTasks.ToArray());

            blobTransferClient.TransferProgressChanged -= blobTransferClient_TransferProgressChanged;


            //try
            //{
            //    //Create a locator to get the SAS url
            //    IAccessPolicy writePolicy = context.AccessPolicies.Create("Copy",
            //        TimeSpan.FromHours(Int32.Parse(ConfigurationManager.AppSettings["MaxCopyTimeout"])),
            //        AccessPermissions.Write | AccessPermissions.List);
            //    ILocator destLocator = context.Locators.CreateSasLocator(inputAsset, writePolicy, DateTime.UtcNow.AddMinutes(-5));

            //    //Save the temp blob container in the returning string
            //    tempBlobContainer = destLocator.Path.Split('?')[0];

            //    //Create the reference to the destination container:
            //    string destContainerName = (new Uri(destLocator.Path)).Segments[1];
            //    CloudBlobContainer destContainer = mediaStorageClient.GetContainerReference(destContainerName);

            //    //Validate the source blob
            //    srcFileBlob.FetchAttributes();
            //    long sourceLength = srcFileBlob.Properties.Length;
            //    System.Diagnostics.Debug.Assert(sourceLength > 0);

            //    //If we got here then we can assume the source is valid and accessible.
            //    //Create destination blob for copy
            //    CloudBlockBlob destFileBlob = destContainer.GetBlockBlobReference(fileName);

            //    //Begin async copy
            //    destFileBlob.BeginStartCopyFromBlob(srcFileBlob, (result) =>
            //        {
            //            CloudBlockBlob blobDest = (CloudBlockBlob)result.AsyncState;

            //            // End the operation.
            //            blobDest.EndStartCopyFromBlob(result);

            //            try
            //            {
            //                //Check destination blob:
            //                blobDest.FetchAttributes();
            //                System.Diagnostics.Debug.Assert(blobDest.Properties.Length == sourceLength);
            //                //If we got here then the copy worked.
            //            }
            //            catch {}

            //            //Publish asset, so it can be encoded next
            //            inputAsset.Publish();
            //            inputAsset = RefreshAsset(context, inputAsset);

            //            //Fire the event for the publishing asset
            //            OnAssetStatusChanged(new AssetStatusArgs(inputAsset));

            //        }, destFileBlob);  // Begin asynchronous copy of the blob
            //}
            //catch(Exception ex)
            //{
            //    WriteLog(ex.Message);
            //    throw ex;
            //}

            return inputAsset;
        }

        void blobTransferClient_TransferProgressChanged(object sender, BlobTransferProgressChangedEventArgs e)
        {
            if (e.ProgressPercentage > 4) // Avoid startup jitter, as the upload tasks are added.
            {
                // Only show progress if higher than 5% change.
                if (e.ProgressPercentage - _previousProgressPercentage > 5)
                {
                    _previousProgressPercentage = e.ProgressPercentage;
                    UploadStatusArgs eventArgs = new UploadStatusArgs(e.LocalFile, e.BytesTransferred, e.ProgressPercentage);
                    OnUploadStatusChanged(eventArgs);
                }
            }
        }




        #endregion


        ///// <summary>
        ///// 
        ///// </summary>
        ///// <param name="context"></param>
        ///// <param name="asset"></param>
        ///// <returns></returns>
        //public static IAsset RefreshAsset(CloudMediaContext context, IAsset asset)
        //{
        //    var assets = from a in context.Assets
        //                where a.Id == asset.Id
        //                select a;
        //    return assets.FirstOrDefault();
        //}
 
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
                                    TaskOptions.ProtectedConfiguration);

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
        /// <param name="context"></param>
        private static ITask AddThumbnailTask(IJob job, ITask chainedTask, string taskName, CloudMediaContext context)
        {
            //Configuration string for the Thumbnails
            string TNConfig = "Thumbnails";

            // Get a media processor reference, and pass to it the name of the 
            // processor to use for the specific task.
            IMediaProcessor processor = GetMediaProcessor("Windows Azure Media Encoder", context);

            // Create the PlayReady Task
            ITask task = job.Tasks.AddNew(taskName,
                                    processor,
                                    TNConfig,
                                    TaskOptions.ProtectedConfiguration);

            // Specify the input asset to be protected. This is the output 
            // asset from the last task in the job.
            task.InputAssets.Add(chainedTask.OutputAssets.Last());

            // Add an output asset to contain the results of the job. Persist the output by setting 
            // the shouldPersistOutputOnCompletion param to true.
            task.OutputAssets.AddNew(taskName,
                                    AssetCreationOptions.None);

            return task;
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
        /// <param name="jobId"></param>
        private bool CheckJobProgress(string jobId)
        {
            // Expected polling interval in milliseconds.  Adjust this 
            // interval as needed based on estimated job completion times.
            // Using 5 seconds as a default polling value.
            const int JobProgressInterval = 5000;

            IJob theJob = GetJob(jobId);
            if (theJob.State == JobState.Finished) return true;

            //Initialize the current state
            JobState curState = theJob.State;

            while (!(theJob.State == JobState.Error || theJob.State == JobState.Canceled))
            {
                // Get an updated reference to the job in case 
                // reference gets 'stale' while thread waits.
                theJob = GetJob(jobId);

                if (theJob.State != curState)   //State changed
                {
                    //Raise status changed event
                    MediaProcessStatusArgs procArgs = new MediaProcessStatusArgs(theJob);
                    OnMediaProcessStatusChanged(procArgs);

                    //Save the current state
                    curState = theJob.State;
                }


                if (theJob.State == JobState.Finished)
                    return true;

                if (theJob.State == JobState.Error || theJob.State == JobState.Canceled)
                    break;

                // Wait for the specified job interval before 
                // checking state again. 
                Thread.Sleep(JobProgressInterval);
            }
            return false;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="jobId"></param>
        /// <returns></returns>
        private IJob GetJob(string jobId)
        {
            // You sometimes need to query for a fresh 
            // reference to a job during threaded operations. 

            // Use a Linq select query to get an updated 
            // reference by Id. 
            var job =
                from j in _context.Jobs
                where j.Id == jobId
                select j;
            // Return the job reference as an Ijob. 
            IJob theJob = job.FirstOrDefault();

            // Confirm whether job exists, and return. 
            if (theJob != null)
            {
                return theJob;
            }
            else
                throw new EntryPointNotFoundException(String.Format("Job {0} does not exist.", theJob.Name));
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



        // Create a list of SAS URLs to all files in an asset.
        private string CreateAssetSasUrl(IAsset asset, ILocator locator)
        {
            string sasUrl = String.Empty;

            if (asset.AssetFiles.Count() > 0)
            {
                // If the asset has files, build a list of URLs to 
                // each file in the asset and return. 
                foreach (var file in asset.AssetFiles)
                {
                    if (!file.Name.Contains("metadata")) // Do not include metadata file url
                    {
                        sasUrl = BuildFileSasUrl(file.Name, locator);
                    }
                }
            }
            else
            {
                //If asset doesn't have files, let's try to pull out the files from the locator's blob container
                var blobpath = locator.Path.Split('?')[0];
                var blobClient = Constants.GetBlobClient();
                //var blobDir = blobClient.GetBlobDirectoryReference(blobpath);
                foreach (var file in blobClient.ListBlobs(blobpath))
                {
                    if (!file.Uri.AbsolutePath.Contains("metadata"))
                    {
                        sasUrl = BuildFileSasUrl(Path.GetFileName(file.Uri.AbsolutePath), locator);
                    }                    
                }

            }


            return sasUrl;
        }



        // Create and return a SAS URL to a single file in an asset. 
        private string BuildFileSasUrl(string filepath, ILocator locator)
        {
            // Take the locator path, add the file name, and build 
            // a full SAS URL to access this file. This is the only 
            // code required to build the full URL.
            var uriBuilder = new UriBuilder(locator.Path);
            uriBuilder.Path += "/" + filepath;

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
        // Display upload progress to the console. 
        protected void OnUploadStatusChanged(UploadStatusArgs e)
        {
            WriteLog(String.Format("{0} asset is being uploaded... {1} bytes sent. {2} % completed.",
                            e.Status.CurrentFile, e.Status.BytesSent, e.Status.Progress));
            UploadStatusChanged(this, e);
        }


        protected void OnMediaProcessStatusChanged(MediaProcessStatusArgs e)
        {
            WriteLog(String.Format("{0} job's state changed to {1}",
                            e.Name, e.JobStatus));
            MediaProcessStatusChanged(this, e);
        }


        protected void OnAssetStatusChanged(AssetStatusArgs e)
        {
            WriteLog(String.Format("{0} asset's state changed to {1}",
                            e.Asset.Name, e.Asset.State));
            AssetStatusChanged(this, e);
        }

        #endregion


    }



    public class UploadStatus
    {
        public string CurrentFile { get; set; }
        public long BytesSent { get; set; }
        public double Progress { get; set; }
    }




    public class UploadStatusArgs : EventArgs
    {
        public UploadStatus Status;

        public UploadStatusArgs(UploadStatus status)
        {
            Status = status;
        }

        public UploadStatusArgs(string file, long sent, double progress)
        {
            Status = new UploadStatus()
            {
                CurrentFile = file,
                BytesSent = sent,
                Progress = progress
            };
        }
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