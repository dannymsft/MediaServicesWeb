using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.MediaServices.Client;
using WAMS.MediaLib.Models;

namespace WAMS.MediaLib
{

    public class MediaJob
    {
        private readonly CloudMediaContext _context;
        private readonly MediaServicesAPI _msApi;
        private readonly FileUploadModel _file;


        public MediaStates State { get; private set; }
        public IAsset Asset { get; private set; }
        public IJob Job { get; private set; }
        public IJob ThumbnailJob { get; private set; }
        public Boolean IsCompleted { get; private set; }


        #region Events

        public delegate void AssetChangedEventHandler(object sender, MediaStatusArgs e);
        public event AssetChangedEventHandler AssetChanged;


        public delegate void JobChangedEventHandler(object sender, MediaStatusArgs e);
        public event JobChangedEventHandler JobChanged;

        protected virtual void OnAssetChanged(MediaStatusArgs e)
        {
            AssetChangedEventHandler handler = AssetChanged;
            if (handler != null) handler(this, e);
        }

        protected virtual void OnJobChanged(MediaStatusArgs e)
        {
            JobChangedEventHandler handler = JobChanged;
            if (handler != null) handler(this, e);
        }

        #endregion



        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="context"></param>
        /// <param name="file"></param>
        public MediaJob(CloudMediaContext context, FileUploadModel file)
        {
            //Create an instance of Media Service API
            _msApi = new MediaServicesAPI(context);
            _context = context;
            _file = file;
            
            //Event handler for the media asset status change
            _msApi.AssetStatusChanged += (sender, e) =>
            {
                Asset = e.Asset;
                State = MediaStates.Ingested;
                var args = new MediaStatusArgs { Asset = e.Asset, State = MediaStates.Ingested };
                OnAssetChanged(args);
            };
        }



        /// <summary>
        /// Ingest media asset to the Asset locator
        /// </summary>
        /// <param name="encryption"></param>
        /// <returns></returns>
        public string IngestAssetAsync(AssetCreationOptions encryption)
        {
            return _msApi.IngestAssetAsync(_context, _file, encryption);
        }


        public string IngestAsset(AssetCreationOptions encryption)
        {
            return _msApi.IngestAsset(_context, _file, encryption);
        }


        /// <summary>
        /// Creates a new encoding job
        /// </summary>
        /// <param name="mediaProcessor"></param>
        /// <param name="assetCollection"></param>
        /// <param name="options"></param>
        public IJob CreateJob(string mediaProcessor, IDictionary<string, string> assetCollection, AssetCreationOptions options)
        {
            Job = _msApi.CreateEncodingJob(Asset, assetCollection, mediaProcessor, options);

            //Set up the job's state change event handler
            Job.StateChanged += JobStateChanged;

            return Job;
        }



        /// <summary>
        /// Creates a thumbnail job
        /// </summary>
        /// <returns></returns>
        public IJob CreateThumbnailJob()
        {
            //Create a thumbnail job
            ThumbnailJob = _msApi.CreateThumbnailJob(Asset);
            ThumbnailJob.StateChanged += JobStateChanged;

            return Job;
        }




        /// <summary>
        /// Starts the job and waits until it finishes
        /// </summary>
        /// <param name="jobTask"></param>
        public void StartJob(Task jobTask)
        {
            jobTask.ContinueWith((antecedent) =>
                {
                    IsCompleted = true;
                });
            //jobTask.Wait();
        }

        /// <summary>
        /// Use the following event handler to check job progress.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void JobStateChanged(object sender, JobStateChangedEventArgs e)
        {
            // Cast sender as a job.
            var job = (IJob)sender;

            MediaServicesAPI.WriteLog(String.Format("DEBUG: Job {0} changed.", job.Id));
            MediaServicesAPI.WriteLog("DEBUG:   Previous state: " + e.PreviousState);
            MediaServicesAPI.WriteLog("DEBUG:   Current state: " + e.CurrentState);

            // Display or log error details as needed.
            if (e.CurrentState == JobState.Canceled || e.CurrentState == JobState.Error)
                MediaServicesAPI.LogJobStop(job);

            //Update current job's media state
            State = UpdateMediaState(e.CurrentState);

            //Fire job state change event
            OnJobChanged(new MediaStatusArgs {Asset = this.Asset, State = this.State, Job = job});
        }


        /// <summary>
        /// Publishes the output results by creating an URL locator
        /// </summary>
        /// <param name="expireOn"></param>
        /// <returns></returns>
        public IDictionary<string, KeyValuePair<string, string>> Publish(TimeSpan expireOn)
        {
            var accessPolicy = _context.AccessPolicies.Create("Default policy",
                                            expireOn, AccessPermissions.Read);


            return _msApi.GetUrls(Job, accessPolicy);
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="state"></param>
        private MediaStates UpdateMediaState(JobState state)
        {
            //Update the current state
            switch (state)
            {
                case JobState.Canceled:
                case JobState.Canceling:
                case JobState.Error:
                    return MediaStates.Canceled;
                case JobState.Finished:
                    return MediaStates.Processed;
                case JobState.Queued:
                case JobState.Scheduled:
                    return MediaStates.Queued;
            }
            return State;
        }


    }


    public class MediaStatusArgs : EventArgs
    {
        public MediaStates State { get; set; }
        public IAsset Asset { get; set; }
        public IJob Job { get; set; }

        public MediaStatusArgs()
        {
        }
    }

}
