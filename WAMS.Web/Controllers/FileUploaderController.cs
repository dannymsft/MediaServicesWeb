using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.Mvc;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using WAMS.MediaLib;
using WAMS.MediaLib.Models;

namespace WAMSDemo.Controllers
{
    public class FileUploaderController : Controller
    {

        #region File Upload


        /// <summary>
        /// Handles chuncked file uploads like the ones from plupload.
        /// </summary>
        /// <param name="chunk"></param>
        /// <param name="chunks"></param>
        /// <returns></returns>
        [HttpPost]
        public ActionResult Upload(int chunk, long chunks)
        {
            HttpPostedFileBase fileData = Request.Files[0];
            if (fileData != null && fileData.ContentLength == 0) return Content("File Length cannot be zero!", "text/plain");

            //Get the file name
            if (fileData != null)
            {
                var fileName = Path.GetFileName(fileData.FileName);


                //Reset file existence indicator
                if (chunk == 0) if (HttpContext.Session != null) HttpContext.Session["FileExist"] = null;

                var fileExists = ((HttpContext.Session != null) ? HttpContext.Session["FileExist"] : null) ?? false;
                var isFileExist = Boolean.Parse(fileExists.ToString()) ||
                                        PrepareMetadata(chunks, fileName, (fileData.ContentLength * chunks));


                if (isFileExist)
                {
                    new FileUploadStatus
                    {
                        Error = false,
                        IsLastBlock = true,
                        Message = "Already exists"
                    };
                    return Content("Success", "text/plain");
                }
            }

            //Upload the next chunk
            UploadFileBlob(fileData, ++chunk);


            return Content("Success", "text/plain");
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockCount"></param>
        /// <param name="fileName"></param>
        /// <param name="fileSize"></param>
        private bool PrepareMetadata(long blockCount, string fileName, long fileSize)
        {
            var container = WAMSConstants.GetBlobClient().GetContainerReference(WAMSConstants.ContainerName);
            container.CreateIfNotExists();
            var fileToUpload = new FileUploadModel()
            {
                BlockCount = blockCount,
                FileName = fileName,
                FileSize = fileSize,
                Blob = container.GetBlockBlobReference(fileName),
                StartTime = DateTime.Now,
                IsUploadCompleted = false,
                UploadStatusMessage = string.Empty
            };

            //Check if the file blob is already exist
            if (fileToUpload.Blob.Exists())
            {
                //File exists! Update the file model info
                HttpContext.Session["FileExist"] = fileToUpload.IsUploadCompleted = true;
                fileToUpload.Blob = container.GetBlockBlobReference(fileToUpload.Blob.Uri.AbsoluteUri);

                //Update File Asset
                var mediaSvc = HttpContext.Session["MediaSvc"] as MediaController;
                if (mediaSvc != null)
                {
                    mediaSvc.FileAssets = mediaSvc.FileAssets ?? new List<FileUploadModel>();

                    if (!mediaSvc.FileAssets.Contains(fileToUpload, new FileUploadModelComparer()))
                    {
                        //Add a new asset
                        mediaSvc.FileAssets.Add(fileToUpload);
                    }
                }

                return true;
            }
            else
            {
                //Add the current model to the Session object
                HttpContext.Session.Add(String.Format("{0}_{1}", WAMSConstants.FileAttributesSession, fileName), fileToUpload);
                HttpContext.Session["FileExist"] = false;
            }

            return false;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="fileUpload"></param>
        /// <param name="chunkCount"></param>
        /// <returns></returns>
        private FileUploadStatus UploadFileBlob(HttpPostedFileBase fileUpload, int chunkCount)
        {
            var sessionId = String.Format("{0}_{1}", WAMSConstants.FileAttributesSession, fileUpload.FileName);

            if (HttpContext.Session[sessionId] != null)
            {
                //Retrieve the file upload model
                var model = (FileUploadModel)HttpContext.Session[sessionId];

                //Read data stream into a buffer
                var buffer = new byte[fileUpload.InputStream.Length];
                fileUpload.InputStream.Read(buffer, 0, buffer.Length);

                //Use memory stream to upload data to the Azure storage
                using (var chunkStream = new MemoryStream(buffer))
                {
                    var blockId = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                        string.Format(CultureInfo.InvariantCulture, "{0:D4}", chunkCount)));

                    model.Blob.StreamWriteSizeInBytes = 16 * 1024; //16KB
                    WAMSConstants.GetBlobClient().ParallelOperationThreadCount = 8;

                    //Set up the blob's content type
                    model.Blob.Properties.ContentType = fileUpload.ContentType;

                    try
                    {
                        //Upload the block
                        model.Blob.PutBlock(blockId, chunkStream, null, null, new BlobRequestOptions()
                        {
                            RetryPolicy = new Microsoft.WindowsAzure.Storage.RetryPolicies.ExponentialRetry(TimeSpan.FromSeconds(10), 3)
                        });
                    }
                    catch (StorageException e)
                    {
                        model.IsUploadCompleted = true;
                        model.UploadStatusMessage = string.Format(CultureInfo.CurrentCulture, Resources.FailedToUploadFileMessage, e.Message);

                        return new FileUploadStatus
                        {
                            Error = true,
                            IsLastBlock = false,
                            Message = model.UploadStatusMessage
                        };
                    }
                }

                //Check if it was the last chunk of the file
                //And, if it is the last one - commit the PutBlock transaction and 
                //add the new file into the FileAsset array
                if (chunkCount == model.BlockCount)
                {
                    model.IsUploadCompleted = true;
                    var errorInOperation = false;
                    try
                    {
                        //Commit upload on Azure
                        var blockList = Enumerable.Range(1, (int)model.BlockCount).ToList<int>()
                            .ConvertAll(rangeElement => Convert.ToBase64String(Encoding.UTF8.GetBytes(
                                string.Format(CultureInfo.InvariantCulture, "{0:D4}", rangeElement))));
                        model.Blob.PutBlockList(blockList);

                        //Generate upload info (could be used later)
                        //var duration = DateTime.Now - model.StartTime;
                        //float fileSizeInKb = model.FileSize / WAMSConstants.BytesPerKb;
                        //string fileSizeMessage = fileSizeInKb > WAMSConstants.BytesPerKb ?
                        //    string.Concat((fileSizeInKb / WAMSConstants.BytesPerKb).ToString(CultureInfo.CurrentCulture), " MB") :
                        //    string.Concat(fileSizeInKb.ToString(CultureInfo.CurrentCulture), " KB");
                        //model.UploadStatusMessage = string.Format(CultureInfo.CurrentCulture, Resources.FileUploadedMessage, fileSizeMessage, duration.TotalSeconds);
                        //var container = WAMSConstants.GetBlobClient().GetContainerReference(WAMSConstants.ContainerName);
                        //model.Blob = container.GetBlockBlobReference(model.Blob.Uri.AbsoluteUri);

                        //Set the content type of the asset to the original
                        //model.Blob.Properties.ContentType = fileUpload.ContentType;
                        //BlobRequestOptions options = new BlobRequestOptions { RetryPolicy = new Microsoft.WindowsAzure.Storage.RetryPolicies.ExponentialRetry(TimeSpan.FromSeconds(1), 5) };
                        //model.Blob.SetProperties(null, options);


                        //Update File Asset
                        var mediaSvc = HttpContext.Session["MediaSvc"] as MediaController;
                        if (mediaSvc != null)
                        {
                            mediaSvc.FileAssets = mediaSvc.FileAssets ?? new List<FileUploadModel>();

                            if (!mediaSvc.FileAssets.Contains(model, new FileUploadModelComparer()))
                            {
                                //Add a new asset
                                mediaSvc.FileAssets.Add(model);
                            }
                        }
                    }
                    catch (StorageException e)
                    {
                        model.UploadStatusMessage = string.Format(CultureInfo.CurrentCulture, Resources.FailedToUploadFileMessage, e.Message);
                        errorInOperation = true;
                    }

                    return new FileUploadStatus
                    {
                        Error = errorInOperation,
                        IsLastBlock = model.IsUploadCompleted,
                        Message = model.UploadStatusMessage
                    };
                }
            }
            else
            {
                return new FileUploadStatus
                {
                    Error = true,
                    IsLastBlock = false,
                    Message = string.Format(CultureInfo.CurrentCulture, Resources.FailedToUploadFileMessage, Resources.SessonExpired)
                };
            }

            return new FileUploadStatus
            {
                Error = false,
                IsLastBlock = false,
                Message = String.Empty
            };

        }

        #endregion


    }
}
