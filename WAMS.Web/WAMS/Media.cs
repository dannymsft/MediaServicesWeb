using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Text;
using System.Linq;
using System.Web;
using Microsoft.WindowsAzure.MediaServices.Client;
using System.Threading;
using Microsoft.WindowsAzure;
using System.Data.Services.Client;
using Microsoft.WindowsAzure.ServiceRuntime;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;
using MediaServicesPortal.Models;
using Microsoft.WindowsAzure.Storage.Blob;
using WAMSDemo.WAMS;

namespace MediaServicesPortal
{
    public enum MediaStates
    {
        Ready,
        Created,
        Started,
        Ingested,
        Queued,
        Processed,
        Canceled
    }

    public class Media
    {
        #region Events
        public delegate void UploadEventHandler(object sender, UploadStatusArgs e);
        public event UploadEventHandler UploadStatusChanged;

        public delegate void PublishAssetEventHandler(object sender, AssetStatusArgs e);
        //public event PublishAssetEventHandler AssetStatusChanges;

        public delegate void MediaProcessEventHandler(object sender, MediaProcessStatusArgs e);
        public event MediaProcessEventHandler MediaProcessStatusChanged;

        #endregion

        private const int PolicyTimeout = 1; // in days
        private static object dependSync = new object();
        private static object eventSync = new object();
        
        // Class-level field used to keep a reference to the service context.
        private CloudMediaContext _context = null;
        private string _configurationPath;
        private IAccessPolicy _accessPolicy;
        private List<IJob> _jobCollection = new List<IJob>();
        private MediaServicesAPI _msApi;

        // ********************************
        // Authentication and connection settings.  These settings are pulled from 
        // the App.Config file and are required to connect to Media Services, 
        // authenticate, and get a token so that you can access the server context. 
        private static readonly string _accountName = (RoleEnvironment.IsAvailable) ?
                                                        RoleEnvironment.GetConfigurationSettingValue("MediaAccountName")
                                                        : ConfigurationManager.AppSettings["accountName"];
        private static readonly string _accountKey = (RoleEnvironment.IsAvailable) ?
                                                        RoleEnvironment.GetConfigurationSettingValue("MediaAccountKey")
                                                        : ConfigurationManager.AppSettings["accountKey"];


        //Public Properties
        public MediaStates State { get; private set; }
        public Guid Id { get; private set; }
        public List<FileUploadModel> FileAssets { get; set; }
        public AssetCreationOptions Encryption { get; set; }
        public string MediaProcessor { get; set; }
        public List<string> Encoders { get; set; }
        public List<string> AssetLocators { get; set; }
        public List<string> SASUrlList { get; set; }
        public DateTime StartedOn { get; set; }
        public Dictionary<string, string> CurrentAssetCollection = new Dictionary<string,string>(); // Keys: partitionkey;rowkey Values:FileAsset
        public List<IAsset> IngestAssets;
        public List<string> TempBlobContainers { get; set; }

        public Media()
        {
            Init();
        }

        public Media(string configurationPath)
        {
            Init();
            _configurationPath = configurationPath;
            MediaServicesAPI.ConfigurationFolder = configurationPath;
        }


        private void Init()
        {
            Id = Guid.NewGuid();
            FileAssets = new List<FileUploadModel>();
            Encoders = new List<string>();
            State = MediaStates.Ready;
        }

        #region GetContext
        private CloudMediaContext GetContext()
        {

            // Gets the service context. Note that the installed Media 
            // Services SDK supplies three other values for you 
            // automatically: the URI of the REST API server, a Media Services 
            // server scope value, and the URL of an access control server. 

            return new CloudMediaContext(_accountName, _accountKey);
        }


        #endregion


        /// <summary>
        /// 
        /// </summary>
        public void Reset()
        {
            if (State == MediaStates.Ready || State == MediaStates.Canceled)
            {
                if (Encoders != null) Encoders.Clear();
                if (AssetLocators != null) AssetLocators.Clear();
                if (SASUrlList != null) SASUrlList.Clear();
                if (FileAssets != null) FileAssets.Clear();
                if (CurrentAssetCollection != null) CurrentAssetCollection.Clear();
            }
        }

        /// <summary>
        /// Initializes the Media Services Channel
        /// </summary>
        public void CreateMediaChannel()
        {
            try
            {
                // Get server context.  This assignment should be left uncommented as it
                // creates the static context object used by all other methods. 
                _context = GetContext();

                //Create an instance of Media Service API
                _msApi = new MediaServicesAPI(_context);

                //Initialize async task collection
                IngestAssets = new List<IAsset>();

                //Update the state
                State = MediaStates.Ready;

                //Subscribe to the events and bubble them up
                _msApi.UploadStatusChanged += (sender, e) =>
                {
                    lock (eventSync)
                    {
                        var updatedFileName = Path.GetFileNameWithoutExtension(e.Status.CurrentFile);

                        //Update the record's status
                        foreach (var item in CurrentAssetCollection.Keys)
                        {
                            var keys = item.Split(';');

                            if (keys[0] != updatedFileName) continue;   //Skip not currently uploaded asset

                            //Retrieve and Update the MediaAsset
                            var mediaAsset = GetAsset(keys[0], keys[1]);
                            if (mediaAsset != null && IsProgressMatch(mediaAsset.Status, e.Status.Progress))
                            {
                                mediaAsset.Status = String.Format("{0:0}% Uploaded", e.Status.Progress);

                                //Update the media asset
                                UpdateMediaAsset(mediaAsset);
                                break;
                            }
                        }
                    }
                    //Asset Ingest
                    UploadStatusChanged(sender, e);
                };
                _msApi.AssetStatusChanged += (sender, e) =>
                {
                    //Add asset to the asset container
                    IngestAssets.Add(e.Asset);
                };

                //Event handler for the media process status change
                _msApi.MediaProcessStatusChanged += (sender, e) =>
                {
                    lock (eventSync)
                    {
                        //Update the current state
                        UpdateMediaState(e.JobState);

                        //Update the record's status
                        foreach (var item in CurrentAssetCollection.Keys)
                        {
                            //Find the current task
                            var task = e.Tasks.Where(t => t.Name == item);
                            if (task.Count() > 0)
                            {
                                //Retrieve and Update the MediaAsset
                                UpdateStatus(task.FirstOrDefault().Name, e.JobStatus);
                            }
                        }
                    }

                    //Media Process Status
                    MediaProcessStatusChanged(sender, e);
                };


            }
            catch (Exception ex)
            {
                State = MediaStates.Canceled;
                throw ex;
            }


        }


        public void SetAccessPolicy(TimeSpan duration, AccessPermissions permissions)
        {
            // Declare an access policy for permissions on the asset. 
            this._accessPolicy =
                _context.AccessPolicies.Create("Access policy",
                    duration,
                    permissions);
        }


        public void SetAccessPolicy()
        {
            // Declare a default access policy for permissions on the asset if it hasn't been set up by the user. 
            this._accessPolicy = _context.AccessPolicies.Create("Default policy",
                                    TimeSpan.FromDays(PolicyTimeout),
                                    AccessPermissions.Read);
        }




        /// <summary>
        /// Start Processing
        /// </summary>
        //public void BeginEncoding()
        //{
        //    _jobCollection.Clear();

        //    try
        //    {
        //        StartedOn = DateTime.UtcNow;

        //        if (!String.IsNullOrEmpty(ConfigurationManager.AppSettings[MediaProcessor]))
        //        {
        //            //Read encoder preset from the configuration file
        //            string configuration = File.ReadAllText(Path.GetFullPath(_configurationPath +
        //                                        ConfigurationManager.AppSettings[MediaProcessor]));
        //            //If other than native Windows Azure Media Encoder is selected, overwrite the encoder lists with the configuration file
        //            this.Encoders.Clear();
        //            this.Encoders.Add(configuration);
        //        }

        //        List<IAsset> assetCol = new List<IAsset>();

        //        //1. Ingest all files syncronously
        //        foreach (var file in FileAssets)
        //        {
        //            //Upload each file for encoding
        //            assetCol.Add(MediaServicesAPI.IngestAsset(this._context, file.Blob.Uri.AbsoluteUri, this.Encryption));
        //        }


        //        //All files have been uploaded, let's update the status
        //        foreach (var item in CurrentAssetCollection.Keys)
        //        {
        //            //Retrieve and Update the MediaAsset
        //            UpdateStatus(item, "100% Uploaded");
        //        }

        //        //Update the current state
        //        State = MediaStates.Ingested;

        //        //2. Now, create the encoding job for each asset
        //        foreach(var asset in assetCol)
        //        {
        //            //Create encoding job based on the encoder settings
        //            IJob theJob = MediaServicesAPI.CreateEncodingJob(asset,
        //                                    CurrentAssetCollection,
        //                                    this.MediaProcessor,
        //                                    this._context);

        //            if (IsPlayReady)
        //            {
        //                // Read the encryption configuration data into a string. 
        //                string presetTask = "PlayReady Protection Task";
        //                string playReadyConfig = File.ReadAllText(Path.GetFullPath(_configurationPath +
        //                                                ConfigurationManager.AppSettings[presetTask]));

        //                MediaServicesAPI.AddNewPlayReadyTask(playReadyConfig,
        //                                                      presetTask,  
        //                                                      theJob,
        //                                                      asset,
        //                                                      this._context);
        //            }

        //            //Add job to the collection
        //            _jobCollection.Add(theJob);
        //        }

        //        //3. Execute each job in the collection
        //        //Parallel.ForEach(_jobCollection, new ParallelOptions() { MaxDegreeOfParallelism = 20 }, (job) =>
        //        foreach (var job in _jobCollection)
        //        {
        //            //Get current job status description
        //            var mediaArgs = new MediaProcessStatusArgs(job);

        //            foreach (var task in job.Tasks)
        //            {
        //                //Retrieve and Update the MediaAsset
        //                UpdateStatus(task.Name, mediaArgs.JobStatus);
        //            }

        //            // Launch the job. 
        //            job.Submit();
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        MediaServicesAPI.WriteLog(ex.Message);
        //        State = MediaStates.Canceled;
        //        throw new Exception("Failed to process media. Exception: " + ex.Message);
        //    }
        //}




        /// <summary>
        /// 
        /// </summary>
        /// <param name="asset"></param>
        public IList<Task> BeginEncoding(IEnumerable<IAsset> assets)
        {
            _jobCollection.Clear();

            try
            {
                //Update the current state
                State = MediaStates.Ingested;

                StartedOn = DateTime.UtcNow;

                //if (!String.IsNullOrEmpty(ConfigurationManager.AppSettings[MediaProcessor]))
                //{
                //    //Read encoder preset from the configuration file
                //    string configuration = File.ReadAllText(Path.GetFullPath(_configurationPath +
                //                                ConfigurationManager.AppSettings[MediaProcessor]));
                //    //If other than native Windows Azure Media Encoder is selected, add the configuration file to the encoder list
                //    this.Encoders.Add(configuration);
                //}

                //All files have been uploaded, let's update the status
                foreach (var item in CurrentAssetCollection.Keys)
                {
                    //Retrieve and Update the MediaAsset
                    UpdateStatus(item, "100% Uploaded");
                }

                //2. Now, create the encoding job for each asset
                //Create encoding job based on the encoder settings
                foreach (var asset in assets)
                {
                    //Set up the first task
                    IJob theJob = MediaServicesAPI.CreateEncodingJob(asset,
                                            CurrentAssetCollection,
                                            this.MediaProcessor,
                                            this._context, Encryption);

                    //Add job to the collection
                    _jobCollection.Add(theJob);
                }

                //3. Execute each job in the collection
                var jobTasks = new List<Task>();
                //Parallel.ForEach(_jobCollection, new ParallelOptions() { MaxDegreeOfParallelism = 20 }, (job) =>
                foreach (var job in _jobCollection)
                {
                    //Get current job status description
                    var mediaArgs = new MediaProcessStatusArgs(job);

                    //Retrieve and Update the MediaAsset
                    UpdateStatus(job.Tasks[0].Name, mediaArgs.JobStatus);

                    // Use the following event handler to check job progress.
                    job.StateChanged += new EventHandler<JobStateChangedEventArgs>(StateChanged);

                    // Launch the job. 
                    job.Submit();

                    // Check job execution and wait for job to finish. 
                    jobTasks.Add(job.GetExecutionProgressTask(CancellationToken.None));

                }

                return jobTasks;

            }
            catch (Exception ex)
            {
                MediaServicesAPI.WriteLog(ex.Message);
                State = MediaStates.Canceled;
                throw new Exception("Failed to process media. Exception: " + ex.Message);
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void StateChanged(object sender, JobStateChangedEventArgs e)
        {
            // Cast sender as a job.
            IJob job = (IJob)sender;

            MediaServicesAPI.WriteLog("Job state changed event:");
            MediaServicesAPI.WriteLog("  Previous state: " + e.PreviousState);
            MediaServicesAPI.WriteLog("  Current state: " + e.CurrentState);

            switch (e.CurrentState)
            {
                case JobState.Finished:
                    Console.WriteLine();
                    Console.WriteLine("********************");
                    Console.WriteLine("Job is finished.");
                    Console.WriteLine("Please wait while local tasks or downloads complete...");
                    Console.WriteLine("********************");
                    Console.WriteLine();
                    Console.WriteLine();
                    break;
                case JobState.Canceling:
                case JobState.Queued:
                case JobState.Scheduled:
                case JobState.Processing:
                    //Raise status changed event
                    MediaProcessStatusArgs procArgs = new MediaProcessStatusArgs(job);
                    MediaProcessStatusChanged(sender, procArgs);

                    break;
                case JobState.Canceled:
                case JobState.Error:
                    // Display or log error details as needed.
                    MediaServicesAPI.LogJobStop(job);
                    break;
                default:
                    break;
            }
        }


        /// <summary>
        /// Obtains the job results
        /// </summary>
        /// <param name="jobTasks"></param>
        public bool GetJobResults(IList<Task> jobTasks)
        {
            bool status = false;
            SASUrlList = new List<string>();

            try
            {
                //Wait for all job tasks to finish
                Task.WaitAll(jobTasks.ToArray());


                //Obtain the results in parallel threads
                Parallel.ForEach(_jobCollection, new ParallelOptions() { MaxDegreeOfParallelism = 16 }, (job) =>
                {
                    //Wait until the processing is finished
                    var outputFiles = _msApi.GetUrls(job.Id, this._accessPolicy);

                    //If we get here, then the process is complete
                    foreach (var assetKey in outputFiles.Keys)
                    {
                        //Get current job status description
                        var mediaArgs = new MediaProcessStatusArgs(job);

                        //For each output file retrieve the matching task
                        var task = job.Tasks.Where(t => t.OutputAssets.Where(a => a.Id == assetKey).Count() > 0).FirstOrDefault();

                        //Retrieve and Update the MediaAsset
                        var mediaAsset = GetAsset(task.Name.Split(';')[0], task.Name.Split(';')[1]);
                        if (mediaAsset != null)
                        {
                            mediaAsset.Url = outputFiles[assetKey];
                            mediaAsset.ProcessingTime = new DateTime(DateTime.UtcNow.Ticks - StartedOn.Ticks).ToString("HH:mm:ss");
                            mediaAsset.Status = mediaArgs.JobStatus;

                            //Update the media asset
                            UpdateMediaAsset(mediaAsset);
                            status = true;
                        }

                        //Change the content type of the final asset
                        ChangeContentType(outputFiles[assetKey]);
                    }

                    //Clean the unused blobs
                    CleanTempBlobs();
                });
            }
            catch (Exception ex)
            {
                MediaServicesAPI.WriteLog(ex.Message);
                State = MediaStates.Canceled;
                return false;
            }

            return status;
        }



 

        /// <summary>
        /// 
        /// </summary>
        /// <param name="protection"></param>
        /// <returns></returns>
        public static string GetAssetCreationDescription(AssetCreationOptions protection)
        {
            switch (protection)
            {
                case AssetCreationOptions.EnvelopeEncryptionProtected:
                    return "PlayReady Encryption";
                case AssetCreationOptions.CommonEncryptionProtected:
                    return "Ultra-Violet Encryption";
                case AssetCreationOptions.StorageEncrypted:
                    return "Storage Encrypted";
                case AssetCreationOptions.None:
                    return "No Encryption";
                default:
                    return "";
            }
        }


        /// <summary>
        /// Processes the media assets and gets the results
        /// </summary>
        /// <returns></returns>
        public bool ProcessMedia()
        {
            try
            {
                var blobClient = Constants.GetBlobClient();

                TempBlobContainers = new List<string>();

                //If a PlayReady Protection has been selected, make storage encryption mandatory
                //if (IsPlayReady) Encryption = AssetCreationOptions.CommonEncryptionProtected;

                //1 Ingest all files asyncronously
                foreach (var file in FileAssets)
                {
                    string tempBlobContainer;
                    //Upload each file for encoding
                    var asset = _msApi.IngestAssetAsync(_context, file.FileName, file.Blob, blobClient, Encryption, out tempBlobContainer);
                    
                    System.Diagnostics.Debug.Assert(asset != null);

                    //IngestAssets.Add(asset);
                    TempBlobContainers.Add(tempBlobContainer);
                }

                //Update the state
                State = MediaStates.Started;

                ////Get and save the results
                //ThreadPool.QueueUserWorkItem(
                //    delegate
                //    {

                //        ////Execute Jobs
                //        //BeginEncoding();

                //        ////Get the job results and update the status
                //        //State = (GetJobResults()) ? MediaStates.Ready : MediaStates.Canceled;
                            
                //    });
            }
            catch (Exception ex)
            {
                MediaServicesAPI.WriteLog(ex.Message);
                State = MediaStates.Canceled;
                throw new Exception("Failed to process media asset. Exception: " + ex.Message);
            }
            return true;
        }


        /// <summary> This method will wait for completion of the media processing. </summary>
        /// <returns>false if encoding has been completed,
        /// true if you must wait a bit longer.</returns>
        public static bool WaitMediaDataAsync(Media media)
        {
            lock (dependSync)
            {
                if (media.State == MediaStates.Started && media.IngestAssets.Count > 0)
                {
                    //foreach (var asset in media.IngestAssets)
                    //    if (!(asset.State == AssetState.Published)) return false;

                    //Ingest is complete. Time to begin encoding.
                    ThreadPool.QueueUserWorkItem(
                        delegate
                        {
                            //Execute Jobs
                            var jobTasks = media.BeginEncoding(media.IngestAssets);
                            if (jobTasks == null) { media.State = MediaStates.Canceled; return; }

                            //Get the job results and update the status
                            media.State = (media.GetJobResults(jobTasks)) ? MediaStates.Ready : MediaStates.Canceled;
                        });
                }

                //return false;
                // "true" means you must wait for result (asynchronous)
                return (media.State == MediaStates.Created);
            }
        }



        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public string CreateMediaEntries()
        {
            string clientMessage = "Encoding of media assets has started successfully...";
            CurrentAssetCollection.Clear();

            try
            {
                foreach (var asset in FileAssets)
                {
                    var fileName = Path.GetFileName(asset.FileName);
                    var title = Path.GetFileNameWithoutExtension(asset.FileName);

                    List<string> collection = new List<string>();
                    if (this.Encoders.Count > 0)
                        collection = this.Encoders;
                    else
                        collection.Add(this.MediaProcessor);

                    foreach (var encoder in collection)
                    {
                        //Create MediaAsset
                        var mediaAsset = new MediaAsset(title)
                        {
                            OriginalFile = fileName,
                            Encoding = encoder,
                            Protection = GetAssetCreationDescription(this.Encryption),
                            Renderer = this.MediaProcessor,
                            Created = DateTime.UtcNow,
                            Url = "",
                            ProcessingTime = "",
                            ExpireOn = new DateTime(DateTime.UtcNow.Ticks + this._accessPolicy.Duration.Ticks),
                            Status = "Created"
                        };

                        //Save the media asset
                        var status = AddMediaAsset(mediaAsset);

                        //Add the processing media asset to the collection
                        //Key: PartitionKey + ";" + RowKey
                        //Value: Encoding
                        if (status == String.Empty) CurrentAssetCollection.Add(
                                String.Format("{0};{1}", mediaAsset.PartitionKey, mediaAsset.RowKey),
                                encoder);
                    }

                    //Update Media State
                    State = MediaStates.Created;

                }
            }
            catch(Exception ex)
            {
                MediaServicesAPI.WriteLog(ex.Message);
                return ex.Message;
            }

            return clientMessage;
        }


        #region Private Utility Methods
        private bool IsProgressMatch(string status, double progress)
        {
            if (!status.Contains("%")) return true;

            string currentProgress = status.Substring(0, status.IndexOf("%"));
            double currentPercent;

            if (Double.TryParse(currentProgress, out currentPercent) && currentPercent < progress) return true;

            return false;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="recordId"></param>
        /// <param name="status"></param>
        private void UpdateStatus(string recordId, string status)
        {
            var keys = recordId.Split(';');
            //Retrieve and Update the MediaAsset
            var mediaAsset = GetAsset(keys[0], keys[1]);
            if (mediaAsset != null)
            {
                mediaAsset.Status = status;

                //Update the media asset
                UpdateMediaAsset(mediaAsset);
            }
        }

        #endregion

        #region MediaAsset Azure Storage Table CRUD Methods

        /// <summary>
        /// 
        /// </summary>
        /// <param name="asset"></param>
        /// <returns></returns>
        public static string AddMediaAsset(MediaAsset asset)
        {
            string statusMessage = String.Empty;
            try
            {
                var context = new MediaAssetContext();

                context.AddMediaAssets(asset);
            }
            catch (Exception ex)
            {
                MediaServicesAPI.WriteLog(ex.Message);
                throw new Exception("Unable to connect to the table storage server. Please check that the service is running."
                                 + ex.Message);
            }

            return statusMessage;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public IEnumerable<MediaAsset> GetAssets()
        {
            try
            {
                var context = new MediaAssetContext();

                return context.MediaAssets;
            }
            catch (Exception ex)
            {
                MediaServicesAPI.WriteLog(ex.Message);
                throw new Exception("Unable to connect to the table storage server. Please check that the service is running. Error: "
                                    + ex.Message);
            }
        }



        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public static MediaAsset GetAsset(string partitionKey, string rowKey)
        {
            try
            {
                var context = new MediaAssetContext();

                return context.GetMediaAsset(partitionKey, rowKey);
            }
            catch (Exception ex)
            {
                MediaServicesAPI.WriteLog(ex.Message);
                throw new Exception("Unable to connect to the table storage server. Please check that the service is running. Error: "
                                    + ex.Message);
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="asset"></param>
        /// <returns></returns>
        public static string UpdateMediaAsset(MediaAsset asset)
        {
            string statusMessage = String.Empty;
            try
            {
                var context = new MediaAssetContext();

                context.UpdateMediaAsset(asset);
            }
            catch (Exception ex)
            {
                MediaServicesAPI.WriteLog(ex.Message);
                throw new Exception("Unable to connect to the table storage server. Please check that the service is running."
                                 + ex.Message);
            }

            return statusMessage;
        }




        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public IList<MediaAsset> GetAssetsInProgress()
        {
            try
            {
                var context = new MediaAssetContext();

                var assets = context.GetAssetsInProgress();
                var mediaAssets = assets as IList<MediaAsset> ?? assets.ToList();

                return mediaAssets;
            }
            catch (Exception ex)
            {
                MediaServicesAPI.WriteLog(ex.Message);
                throw new Exception("Unable to connect to the table storage server. Please check that the service is running. Error: "
                                    + ex.Message);
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
                var context = new MediaAssetContext();

                return context.DeleteAsset(asset);
            }
            catch (Exception ex)
            {
                MediaServicesAPI.WriteLog(ex.Message);
                throw new Exception("Unable to connect to the table storage server. Please check that the service is running. Error: "
                                    + ex.Message);
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 
        /// </summary>
        /// <param name="state"></param>
        private void UpdateMediaState(JobState state)
        {
            //Update the current state
            switch (state)
            {
                case JobState.Canceled:
                case JobState.Canceling:
                case JobState.Error:
                    State = MediaStates.Canceled;
                    break;
                case JobState.Finished:
                    State = MediaStates.Ready;
                    break;
                case JobState.Processing:
                    State = MediaStates.Processed;
                    break;
                case JobState.Queued:
                case JobState.Scheduled:
                    State = MediaStates.Queued;
                    break;
                default:
                    break;
            }

        }



        /// <summary>
        /// 
        /// </summary>
        private void CleanTempBlobs()
        {
            try
            {
                CloudBlobClient blobClient = Constants.GetBlobClient();
                foreach (var container in TempBlobContainers)
                {
                    //Delete the temp container
                    CloudBlobContainer tempContainer = blobClient.GetContainerReference(container);
                    tempContainer.Delete();
                }
            }
            catch (Exception)
            {
                //Do nothing
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="filepath"></param>
        private void ChangeContentType(string filepath)
        {
            try
            {

                //// obtain a shared access signature (looks like a query param: ?se=...)
                //var sas = filepath.Split('?')[1];
                //var sasCreds = new StorageCredentialsSharedAccessSignature(sas);
                //CloudBlobClient blobClient = new CloudBlobClient(account.BlobEndpoint, sasCreds);

                //Replace HTTPS with HTTP
                var asseturi = filepath.ToLower().Replace("https", "http");
                CloudBlobClient blobClient = Constants.GetBlobClient();
                var blob = blobClient.GetBlobReferenceFromServer(new Uri(asseturi.Split('?')[0]));
                string contentType = "video/" + Path.GetExtension(filepath.Split('?')[0]).Substring(1);

                //Set the content type for the media asset
                blob.Properties.ContentType = contentType;
                BlobRequestOptions options = new BlobRequestOptions { RetryPolicy =  new Microsoft.WindowsAzure.Storage.RetryPolicies.ExponentialRetry(TimeSpan.FromSeconds(1), 5) };
                blob.SetProperties(null, options);
            }
            catch (Exception)
            {
                //Do nothing
            }
        }



        #endregion


        #region Encoder Presets
        /// <summary>
        /// Get all presets
        /// </summary>
        /// <returns></returns>
        public IEnumerable<EncoderPreset> GetEncoders()
        {
            try
            {
                var context = new EncoderPresetContext();

                return context.Encoders;
            }
            catch (Exception ex)
            {
                MediaServicesAPI.WriteLog(ex.Message);
                throw new Exception("Unable to connect to the table storage server. Please check that the service is running. Error: "
                                    + ex.Message);
            }
        }


        public IEnumerable<string> GetPresetCategories()
        {
            try
            {
                var context = new EncoderPresetContext();

                return context.PresetCategories;
            }
            catch (Exception ex)
            {
                MediaServicesAPI.WriteLog(ex.Message);
                throw new Exception("Unable to connect to the table storage server. Please check that the service is running. Error: "
                                    + ex.Message);
            }
        }

        /// <summary>
        /// Returns the list of encoder presets by their category (coding standard)
        /// </summary>
        /// <param name="category"></param>
        /// <returns></returns>
        public IEnumerable<EncoderPreset> GetEncoders(string category)
        {
            switch (category)
            {   
                case Constants.AudioPreset:
                    return GetAudioPresets();

                case Constants.ThumbnailPreset:
                    return GetThumbnailPresets();

                case Constants.VideoVC1Preset:
                    return GetVC1Presets();

                case Constants.VideoH264Preset:
                    return GetH264Presets();

                default:
                    return null;
            }
        }


        /// <summary>
        /// Get Audio Presets
        /// </summary>
        /// <returns></returns>
        private IEnumerable<EncoderPreset> GetAudioPresets()
        {
            return GetEncoderPresets(Constants.AudioPreset);
        }



        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private IEnumerable<EncoderPreset> GetThumbnailPresets()
        {
            return GetEncoderPresets(Constants.ThumbnailPreset);
        }



        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private IEnumerable<EncoderPreset> GetVC1Presets()
        {
            return GetEncoderPresets(Constants.VideoVC1Preset);
        }


        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private IEnumerable<EncoderPreset> GetH264Presets()
        {
            return GetEncoderPresets(Constants.VideoH264Preset);
        }


        /// <summary>
        /// Gets encoder presets by the category name
        /// </summary>
        /// <param name="category"></param>
        /// <returns></returns>
        private IEnumerable<EncoderPreset> GetEncoderPresets(string category)
        {
            try
            {
                var context = new EncoderPresetContext();

                return context.GetPresets(category);
            }
            catch (Exception ex)
            {
                MediaServicesAPI.WriteLog(ex.Message);
                throw new Exception("Unable to connect to the table storage server. Please check that the service is running. Error: "
                                    + ex.Message);
            }
        }
        
        #endregion
    }

}