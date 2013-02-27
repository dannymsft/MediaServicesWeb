using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using Microsoft.WindowsAzure.MediaServices.Client;
using System.Threading;
using Microsoft.WindowsAzure.Storage.Table;
using WAMS.MediaLib.Models;
using Microsoft.WindowsAzure.Storage.Blob;

namespace WAMS.MediaLib
{
    public enum MediaStates
    {
        Initialized,
        Ready,
        Created,
        Started,
        Ingesting,
        Ingested,
        Queued,
        InProcess,
        Processed,
        Canceled
    }

    public class MediaController
    {
        private readonly string _accountName;
        private readonly string _accountKey;
        private static readonly object EventSync = new object();
        
        //Public Properties
        public MediaStates State { get; private set; }
        public List<FileUploadModel> FileAssets { get; set; }
        public AssetCreationOptions Encryption { get; set; }
        public string MediaProcessor { get; set; }
        public List<string> Encoders { get; set; }
        public DateTime StartedOn { get; set; }
        public Dictionary<string, string> CurrentAssetCollection = new Dictionary<string,string>(); // Keys: partitionkey;rowkey Values:FileAsset
        public List<MediaJob> MediaJobs = new List<MediaJob>();
        public List<string> TempBlobContainers { get; set; }
        public TimeSpan ExpireOn { get; set; }
        public Boolean DirtyData { get; set; }
        public string DbConnectionString { get; set; }

        public MediaController(string account, string key)
        {
            _accountName = account;
            _accountKey = key;
            Init();
        }

        public MediaController(string account, string key, string configurationPath)
        {
            _accountName = account;
            _accountKey = key;

            Init();
            MediaServicesAPI.ConfigurationFolder = configurationPath;
        }


        private void Init()
        {
            FileAssets = new List<FileUploadModel>();
            Encoders = new List<string>();
            DirtyData = false;
            State = MediaStates.Initialized;
        }


        public void SetReady()
        {
            State = MediaStates.Ready;
        }


        public void SetInProcess()
        {
            State = MediaStates.InProcess;
        }


        private static CloudMediaContext GetContext(string accountName, string accountKey)
        {

            // Gets the service context. Note that the installed Media 
            // Services SDK supplies three other values for you 
            // automatically: the URI of the REST API server, a Media Services 
            // server scope value, and the URL of an access control server. 

            return new CloudMediaContext(accountName, accountKey);
        }




        /// <summary>
        /// 
        /// </summary>
        public void Reset()
        {
            if (State == MediaStates.Ready || State == MediaStates.Canceled)
            {
                if (Encoders != null) Encoders.Clear();
                if (FileAssets != null) FileAssets.Clear();
                if (CurrentAssetCollection != null) CurrentAssetCollection.Clear();
            }
        }

        /// <summary>
        /// State: Ready
        /// Initializes the Media Jobs
        /// </summary>
        public void CreateMediaJobs()
        {
            try
            {
                foreach (var job in FileAssets.Select(asset => new MediaJob(GetContext(_accountName, _accountKey), asset)))
                {
                    job.AssetChanged += (sender, e) =>
                        {
                            //If all assets have been ingested, update the media controller state to Ingested as well.
                            if (MediaJobs.Count(m => m.State == MediaStates.Ingested) == FileAssets.Count()) State = MediaStates.Ingested;

#if ELMAH
                            //DEBUG ONLY
                            MediaServicesAPI.WriteLog("DEBUG: Current media status: " + ((State == MediaStates.Ingested) ? "Ingested" : "Ingesting"));
#endif
                        };

                    MediaJobs.Add(job);
                }
            }
            catch (Exception ex)
            {
                MediaServicesAPI.WriteLog(ex.Message);
                State = MediaStates.Canceled;
                throw;
            }


        }


 


        #region Workflow Tasks

        /// <summary>
        /// In State: Ready
        /// Out State: Created
        /// </summary>
        /// <returns></returns>
        public string CreateMediaEntries()
        {
            const string clientMessage = "Encoding of media assets has started successfully...";
            CurrentAssetCollection.Clear();

            try
            {
                foreach (var asset in FileAssets)
                {
                    var fileName = Path.GetFileName(asset.FileName);
                    var title = Path.GetFileNameWithoutExtension(asset.FileName);

                    var collection = new List<string>();
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
                                Size = 0,
                                Url = "",
                                ProcessingTime = new DateTime(0).ToString("HH:mm:ss"),
                                ExpireOn = new DateTime(DateTime.UtcNow.Ticks + this.ExpireOn.Ticks),
                                Status = "Created"
                            };

                        //Save the media asset
                        var status = AddMediaAsset(mediaAsset);

                        //Add the processing media asset to the collection
                        //Key: PartitionKey + ";" + RowKey
                        //Value: Encoding
                        if (status == String.Empty)
                            CurrentAssetCollection.Add(
                                String.Format("{0};{1}", mediaAsset.PartitionKey, mediaAsset.RowKey),
                                encoder);
                    }

                    //Update Media State
                    State = MediaStates.Created;

                }
            }
            catch (Exception ex)
            {
                MediaServicesAPI.WriteLog(ex.Message);
                return ex.Message;
            }

            return clientMessage;
        }




        /// <summary>
        /// In State: Created
        /// Out State: Ingesting
        /// Processes the media assets and gets the results
        /// </summary>
        /// <returns></returns>
        public bool ProcessMedia()
        {
            try
            {
                TempBlobContainers = new List<string>();

#if O365
                //Ingest all files syncronously
                foreach (var tempBlobContainer in MediaJobs.Select(mediaJob => mediaJob.IngestAsset(Encryption)))
                {
                    TempBlobContainers.Add(tempBlobContainer);
                }
#else
                //Ingest all files asyncronously
                foreach (var tempBlobContainer in MediaJobs.Select(mediaJob => mediaJob.IngestAssetAsync(Encryption)))
                {
                    //Change the state
                    State = MediaStates.Ingesting;

                    TempBlobContainers.Add(tempBlobContainer);
                }
#endif
            }
            catch (Exception ex)
            {
                MediaServicesAPI.WriteLog(ex.Message);
                State = MediaStates.Canceled;
                return false;
            }
            return true;
        }



        /// <summary>
        /// In State: Ingested
        /// Out State: Started
        /// </summary>
        public bool BeginEncoding()
        {
#if ELMAH
            //DEBUG ONLY
            MediaServicesAPI.WriteLog("DEBUG: Entering BeginEncoding()");
#endif
            
            try
            {
                StartedOn = DateTime.UtcNow;



                //All files have been uploaded, let's update the status
                foreach (var item in CurrentAssetCollection.Keys)
                {
                    //Retrieve and Update the MediaAsset
                    UpdateStatus(item, "100% Uploaded");
                }


                //Update the current state
                State = MediaStates.Started;
                DirtyData = true;

                //2. Now, create the encoding job for each asset
                //Create encoding job based on the encoder settings
                foreach (var mediaJob in MediaJobs)
                {
                    //switch (Encryption)
                    //{
                    //    case AssetCreationOptions.StorageEncrypted:
                    //        //Storage encryption
                    //        throw new NotImplementedException("Storage encryption has not  been implemented yet");
                    //        break;
                    //    case AssetCreationOptions.CommonEncryptionProtected:
                    //        //UltraViolet DRM
                    //        throw new NotImplementedException("Common Encryption DRM has not  been implemented yet");
                    //        break;
                    //    case AssetCreationOptions.EnvelopeEncryptionProtected:
                    //        //Play Ready DRM
                    //        break;
                    //    default:
                    //}

                    //Create a new encoding job
                    var job = mediaJob.CreateJob(MediaProcessor, CurrentAssetCollection, Encryption);

                    //Subscribe to the job change event
                    mediaJob.JobChanged += StateChanged;

                    //Get current job status description
                    var mediaArgs = new MediaProcessStatusArgs(job);

#if ELMAH
                    //DEBUG ONLY
                    MediaServicesAPI.WriteLog("DEBUG: Updating the status to: " + mediaArgs.JobStatus);
#endif
                    //Retrieve and Update the MediaAsset
                    UpdateStatus(job.Tasks[0].Name, mediaArgs.JobStatus);


#if ELMAH
                    //DEBUG ONLY
                    MediaServicesAPI.WriteLog("DEBUG: About to submit a job with " + job.Tasks.Count().ToString() + " tasks...");
#endif
                    // Launch the encoding job. 
                    job.Submit();

#if ELMAH
                    //DEBUG ONLY
                    MediaServicesAPI.WriteLog("DEBUG: Job Id: " + job.Id + " has been submitted");
#endif
                    //Launch the thumbnail job.
                    //tJob.Submit();

                    // Check job execution and wait for job to finish. 
                    mediaJob.StartJob(job.GetExecutionProgressTask(CancellationToken.None));
                    //mediaJob.StartJob(tJob.GetExecutionProgressTask(CancellationToken.None));




                }


            }
            catch (Exception ex)
            {
                MediaServicesAPI.WriteLog(ex);
                State = MediaStates.Canceled;
                return false;
            }
            return true;
        }

        #endregion


        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void StateChanged(object sender, MediaStatusArgs e)
        {
            // Cast sender as a job.
            var job = (MediaJob)sender;

            lock (EventSync)
            {
#if ELMAH
                //DEBUG ONLY
                MediaServicesAPI.WriteLog("State has changed to " + e.State);
#endif

                //Update the current state of the controller if all jobs have the same state
                if (MediaJobs.Count(m => m.State == e.State) == MediaJobs.Count())
                    State = e.State;

                var procArgs = new MediaProcessStatusArgs(job.Job);

                //Update the record's status
                foreach (var task in CurrentAssetCollection.Keys.Select(item => job.Job.Tasks.Where(t => t.Name == item)).Where(task => task.Any()))
                {
                    //Retrieve and Update the MediaAsset
                    UpdateStatus(task.FirstOrDefault().Name, procArgs.JobStatus);
                }
            }
        }


        /// <summary>
        /// Obtains the job results
        /// </summary>
        public bool GetJobResults()
        {
            var status = false;
            var assetsDict = new Dictionary<string, string>();

            try
            {
                //Obtain the results in parallel threads
                foreach (var mediaJob in MediaJobs)
                {
                    if (!mediaJob.IsCompleted) continue;

                    var job = mediaJob.Job;
                    var outputFiles = mediaJob.Publish(ExpireOn);

                    //If we get here, then the process is complete
                    foreach (var assetKey in outputFiles.Keys)
                    {
                        var taskId = assetKey.Split(';')[0];

                        //Get current job status description
                        var mediaArgs = new MediaProcessStatusArgs(job);

                        //For each output file retrieve the matching task
                        var task = job.Tasks.FirstOrDefault(t => t.OutputAssets.Count(a => a.Id == taskId) > 0);

                        if (task != null)
                        {
                            var nameKeys = task.Name.Split(';');
                            if (nameKeys.Length < 2) continue;

                            var partitionKey = nameKeys[0];
                            var rowKey = nameKeys[1];

                            //Retrieve and Update the MediaAsset
                            var mediaAsset = GetAsset(partitionKey, rowKey);
                            if (mediaAsset == null)
                            {
                                MediaServicesAPI.WriteLog(String.Format("Can't fetch the media record from the MediaAssets table with partition key={0} and rowKey={1}",
                                    partitionKey, rowKey));
                                continue;
                            }

                            mediaAsset.Url = outputFiles[assetKey].Key;
                            mediaAsset.Thumbnail = outputFiles[assetKey].Value;
                            mediaAsset.Size = GetFileSize(mediaAsset.Url);
                            mediaAsset.ProcessingTime =
                                new DateTime(DateTime.UtcNow.Ticks - StartedOn.Ticks).ToString("HH:mm:ss");
                            mediaAsset.Status = mediaArgs.JobStatus;

                            if (assetsDict.Contains(new KeyValuePair<string, string>(rowKey, partitionKey)))
                            {
                                //Duplicate is found! We will add a new media asset cloned from the current media asset record while changing the row key
                                mediaAsset.RowKey = string.Format("{0:10}_{1}",
                                                                  DateTime.MaxValue.Ticks - DateTime.UtcNow.Ticks,
                                                                  Guid.NewGuid());
                                AddMediaAsset(mediaAsset);
                            }
                            else
                            {
                                //Update the media asset
                                UpdateMediaAsset(mediaAsset);
                            }


                            //Store the partition and row keys in the list of updated media assets
                            assetsDict.Add(mediaAsset.RowKey, mediaAsset.PartitionKey);
                        }
                        status = true;

                        //Refresh screen
                        DirtyData = true;

                        //Change the content type of the final asset
                        //ChangeContentType(outputFiles[assetKey]);
                    }

                    //Clean the unused blobs
                    CleanTempBlobs();
                }

            }
            catch (Exception ex)
            {
                MediaServicesAPI.WriteLog(ex.Message);
                State = MediaStates.Canceled;
                return false;
            }
            finally
            {
                //Reset the state
                Init();
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
                    return "PlayReady DRM";
                case AssetCreationOptions.CommonEncryptionProtected:
                    return "Ultra-Violet DRM";
                case AssetCreationOptions.StorageEncrypted:
                    return "Storage Encrypted";
                case AssetCreationOptions.None:
                    return "HTTPS & SAS";
                default:
                    return "";
            }
        }




 




        #region Private Utility Methods


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
                mediaAsset.ProcessingTime = new DateTime(DateTime.UtcNow.Ticks - StartedOn.Ticks).ToString("HH:mm:ss");

                //Update the media asset
                UpdateMediaAsset(mediaAsset);

                //Refresh screen
                DirtyData = true;
            }
        }

        #endregion

        #region MediaAsset Azure Storage Table CRUD Methods

        /// <summary>
        /// 
        /// </summary>
        /// <param name="asset"></param>
        /// <returns></returns>
        public string AddMediaAsset(MediaAsset asset)
        {
            var statusMessage = String.Empty;
            try
            {
                if (String.IsNullOrEmpty(DbConnectionString))
                {
                    //Use Azure Tables
                    var context = new MediaAssetContext();

                    context.AddMediaAssets(asset);
                }
                else
                {
                    //Use Sql Azure Db
                    var context = new MediaAssetsDataContext(DbConnectionString);

                    var dbMediaAsset = new Models.MediaAsset(asset);

                    context.AddMediaAssets(dbMediaAsset);
                }
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
                if (String.IsNullOrEmpty(DbConnectionString))
                {
                    //Use Azure Tables
                    var context = new MediaAssetContext();

                    return context.MediaAssets;
                }
                else
                {
                    //Use Sql Azure Db
                    var context = new MediaAssetsDataContext(DbConnectionString);
                    

                    return context.MediaAssets.Select(record => new MediaAsset(record)).ToList();
                }

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
        public MediaAsset GetAsset(string partitionKey, string rowKey)
        {
            try
            {
                if (String.IsNullOrEmpty(DbConnectionString))
                {
                    //Use Azure Tables
                    var context = new MediaAssetContext();

                    return context.GetMediaAsset(partitionKey, rowKey);
                }
                else
                {
                    //Use Sql Azure Db
                    var context = new MediaAssetsDataContext(DbConnectionString);

                    Models.MediaAsset dbMediaAsset = context.GetMediaAsset(partitionKey, rowKey);
                    return new MediaAsset(dbMediaAsset);
                }
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
        public string UpdateMediaAsset(MediaAsset asset)
        {
            string statusMessage = String.Empty;
            try
            {
                if (String.IsNullOrEmpty(DbConnectionString))
                {
                    //Use Azure Tables
                    var context = new MediaAssetContext();

                    context.UpdateMediaAsset(asset);
                }
                else
                {
                    //Use Sql Azure Db
                    var context = new MediaAssetsDataContext(DbConnectionString);

                    var dbMediaAsset = new Models.MediaAsset(asset);

                    context.UpdateStatus(dbMediaAsset);
                }
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
        public IList<MediaAsset> GetAssetsInProgress(CloudTableClient tableClient)
        {
            try
            {
                if (String.IsNullOrEmpty(DbConnectionString))
                {
                    //Use Azure Tables
                    var context = new MediaAssetContext(tableClient);

                    var assets = context.GetAssetsInProgress();
                    var mediaAssets = assets as IList<MediaAsset> ?? assets.ToList();

                    return mediaAssets;
                }
                else
                {
                    //Use Sql Azure Db
                    var context = new MediaAssetsDataContext(DbConnectionString);

                    var dbAssets = context.GetAssetsInProgress();

                    return dbAssets.Select(asset => new MediaAsset(asset)).ToList();
                }
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
        public IList<MediaAsset> GetAssetsInProgress()
        {
            try
            {
                if (String.IsNullOrEmpty(DbConnectionString))
                {
                    //Use Azure Tables
                    var context = new MediaAssetContext();

                    var assets = context.GetAssetsInProgress();
                    var mediaAssets = assets as IList<MediaAsset> ?? assets.ToList();

                    return mediaAssets;
                }
                else
                {
                    //Use Sql Azure Db
                    var context = new MediaAssetsDataContext(DbConnectionString);

                    var dbAssets = context.GetAssetsInProgress();

                    return dbAssets.Select(asset => new MediaAsset(asset)).ToList();
                }
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
        /// <param name="tableClient"></param>
        /// <param name="asset"></param>
        /// <returns></returns>
        public bool DeleteAsset(CloudTableClient tableClient, MediaAsset asset)
        {
            try
            {
                if (String.IsNullOrEmpty(DbConnectionString))
                {
                    //Use Azure Tables
                    var context = new MediaAssetContext(tableClient);

                    return context.DeleteAsset(asset);
                }
                else
                {
                    //Use Sql Azure Db
                    var context = new MediaAssetsDataContext(DbConnectionString);

                    var dbMediaAsset = new Models.MediaAsset(asset);

                    context.DeleteMediaAssetExt(dbMediaAsset);
                    return true;
                }
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
                if (String.IsNullOrEmpty(DbConnectionString))
                {
                    //Use Azure Tables
                    var context = new MediaAssetContext();

                    return context.DeleteAsset(asset);
                }
                else
                {
                    //Use Sql Azure Db
                    var context = new MediaAssetsDataContext(DbConnectionString);

                    var dbMediaAsset = new Models.MediaAsset(asset);

                    context.DeleteMediaAssetExt(dbMediaAsset);
                    return true;
                }
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
        private void CleanTempBlobs()
        {
            try
            {
                var blobClient = WAMSConstants.GetBlobClient();
                foreach (var tempContainer in TempBlobContainers.Select(blobClient.GetContainerReference))
                {
                    tempContainer.Delete();
                }
            }
            catch
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
                CloudBlobClient blobClient = WAMSConstants.GetBlobClient();
                var blob = blobClient.GetBlobReferenceFromServer(new Uri(asseturi.Split('?')[0]));
                string contentType = "video/" + Path.GetExtension(filepath.Split('?')[0]).Substring(1);

                //Set the content type for the media asset
                blob.Properties.ContentType = contentType;
                BlobRequestOptions options = new BlobRequestOptions { RetryPolicy =  new Microsoft.WindowsAzure.Storage.RetryPolicies.ExponentialRetry(TimeSpan.FromSeconds(1), 5) };
                blob.SetProperties(null, options);
            }
            catch
            {
                //Do nothing
            }
        }


        private static int? GetFileSize(string url)
        {
            return 0;

            try
            {
                var blobClient = WAMSConstants.GetBlobClient();

                //Replace HTTPS with HTTP
                var asseturi = url.ToLower().Replace("https", "http");
                var blobName = asseturi.Split('?')[0];
                foreach (var blobItem in blobClient.ListBlobs(blobName))
                {
                    var blob = blobItem.Parent.Container.GetBlockBlobReference(blobName);
                    //Set the content type for the media asset
                    return Int32.Parse(blob.Properties.Length.ToString());
                }
            }
            catch
            {
                //Do nothing
            }
            return 0;
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
                case WAMSConstants.AudioPreset:
                    return GetAudioPresets();

                case WAMSConstants.ThumbnailPreset:
                    return GetThumbnailPresets();

                case WAMSConstants.VideoVC1Preset:
                    return GetVC1Presets();

                case WAMSConstants.VideoH264Preset:
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
            return GetEncoderPresets(WAMSConstants.AudioPreset);
        }



        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private IEnumerable<EncoderPreset> GetThumbnailPresets()
        {
            return GetEncoderPresets(WAMSConstants.ThumbnailPreset);
        }



        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private IEnumerable<EncoderPreset> GetVC1Presets()
        {
            return GetEncoderPresets(WAMSConstants.VideoVC1Preset);
        }


        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private IEnumerable<EncoderPreset> GetH264Presets()
        {
            return GetEncoderPresets(WAMSConstants.VideoH264Preset);
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