using System.Globalization;
using System.IO;
using Microsoft.SharePoint.Client;
using Microsoft.WindowsAzure.MediaServices.Client;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Web.Mvc;
using System.Web.Script.Serialization;
using Microsoft.WindowsAzure.Storage.Blob;
using WAMSDemo.Helpers;
using WAMSDemo.Models;
using WAMS.MediaLib;

namespace WAMSDemo.Controllers
{
        [HandleErrorWithELMAH]
        public class HomeController : Controller
        {
            #region Static Fields
            public static MediaController MediaControllerSvc;

            //Static list of Media Service Clients
            public static Dictionary<Guid, MediaController> MediaDict;

            #endregion

            #region View Controllers

            public ActionResult VOD()
            {
                ViewBag.Message = "Video On Demand";

                ////Initialize the Media Service object
                Initialize();

                //Obtain the Sql DB Connection string
                if (String.IsNullOrEmpty(MediaControllerSvc.DbConnectionString)) SetConnectionString(null);

                ViewBag.Message = (MediaControllerSvc == null || String.IsNullOrEmpty(MediaControllerSvc.DbConnectionString)) ? String.Format("http://{0}.table.core.windows.net",
                    ConfigurationManager.AppSettings["accountName"]) : MediaControllerSvc.DbConnectionString;
#if O365
                ViewBag.Mode = "O365";
                return View();
#else
                ViewBag.Mode = "";
                return View();
#endif
            }




            public ActionResult LiveStreaming()
            {
                ViewBag.Message = "Live Streaming";

                return View();
            }



            #region FOR DEBUG PURPOSES ONLY!
            public ActionResult Index()
            {
                ViewBag.Message = "Test";

                //Initialize the Media Service object
                Initialize();

                var model = GetEncoders();

                var jsonModel = new JavaScriptSerializer().Serialize(model);

                return View("Index", "_Layout", jsonModel);
            }
            #endregion

            #endregion




            #region Load Data


            public void Initialize()
            {
                MediaControllerSvc =  new MediaController(WAMSConstants.AccountName, WAMSConstants.AccountKey, System.Web.HttpContext.Current.Server.MapPath("~/configuration/"));
               
                //Save into the session variable
                Session["MediaSvc"] = MediaControllerSvc;

                //Cross domain handling on Azure blob storage
                CreateSilverlightPolicy();
                CreateFlashPolicy();

            }




            /// <summary>
            /// 
            /// </summary>
            /// <param name="isSql"></param>
            private void SetConnectionString(bool? isSql)
            {
                if (MediaControllerSvc == null)
                {
                    throw new Exception("MediaControllerSvc is null.");
                }

#if O365
                var dataSource = (isSql) ?? ConfigurationManager.AppSettings["UseDatabase"].ToLower().Contains("sql");

                if (dataSource)
                {
                    if (Session["DataSource"] != null && !String.IsNullOrEmpty(Session["DataSource"].ToString()))
                    {
                        MediaControllerSvc.DbConnectionString = Session["DataSource"].ToString();
                        return;
                    }
                    var contextToken = TokenHelper.GetContextTokenFromRequest(System.Web.HttpContext.Current.Request);
                    var hostWeb = System.Web.HttpContext.Current.Request["SPHostUrl"];
                    if (contextToken != null && hostWeb != null)
                    {
                        using (var clientContext = TokenHelper.GetClientContextWithContextToken(hostWeb, contextToken, Request.Url.Authority))
                        {
                            //Retrieve SQL dynamic Connection sting
                            clientContext.Load(clientContext.Web, web => web.Title);
                            var connStringResult = AppInstance.RetrieveAppDatabaseConnectionString(clientContext);
                            clientContext.ExecuteQuery();

                            if (!String.IsNullOrEmpty(connStringResult.Value))
                            {
                                MediaControllerSvc.DbConnectionString = connStringResult.Value;
                            }
                            else
                            {
                                //connection string will be empty if we're in the debug mode
                                var connStrSetting = ConfigurationManager.ConnectionStrings["LocalDBInstanceForDebugging"];
                                MediaControllerSvc.DbConnectionString = (connStrSetting != null) ? connStrSetting.ConnectionString : "No Database Found";
                            }

                            if (String.IsNullOrEmpty(MediaControllerSvc.DbConnectionString))
                            {
                                MediaServicesAPI.WriteLog("SQL DB Connection string cannot be determined.");
                            }

                            //Save into the session variable
                            Session["MediaSvc"] = MediaControllerSvc;
                            Session["DataSource"] = MediaControllerSvc.DbConnectionString;

                            clientContext.Dispose();
                        }
                    }
                }
                else
                {
                    //Set connection to Azure Table
                    MediaControllerSvc.DbConnectionString = "";
                }
#else
                //Set connection to Azure Table
                MediaControllerSvc.DbConnectionString = "";
#endif

            }



            /// <summary>
            /// Returns data by the criterion
            /// </summary>
            /// <param name="param">Request sent by DataTables plugin</param>
            /// <returns>JSON text used to display data
            /// <list type="">
            /// <item>sEcho - same value as in the input parameter</item>
            /// <item>iTotalRecords - Total number of unfiltered data. This value is used in the message: 
            /// "Showing *start* to *end* of *iTotalDisplayRecords* entries (filtered from *iTotalDisplayRecords* total entries)
            /// </item>
            /// <item>iTotalDisplayRecords - Total number of filtered data. This value is used in the message: 
            /// "Showing *start* to *end* of *iTotalDisplayRecords* entries (filtered from *iTotalDisplayRecords* total entries)
            /// </item>
            /// <item>aoData - Twodimensional array of values that will be displayed in table. 
            /// Number of columns must match the number of columns in table and number of rows is equal to the number of records that should be displayed in the table</item>
            /// </list>
            /// </returns>
            public ActionResult DataHandler(JQueryDataTableParamModel param)
            {
                if (MediaControllerSvc == null) 
                {
                    MediaServicesAPI.WriteLog("MediaControllerSvc is null.");
                    Initialize();
                    //return Json(new
                    //{
                    //    param.sEcho,
                    //    iTotalRecords = 0,
                    //    iTotalDisplayRecords = 0,
                    //    aaData = new[] {"","","","","","","",""
                    //    }
                    //},
                    //JsonRequestBehavior.AllowGet);
                }

                //Retrieve all media records from the database
                var allAssets = MediaControllerSvc.GetAssets();
            
                IEnumerable<MediaAsset> filteredAssets;
                //Check whether the assets should be filtered by keyword
                var mediaAssets = allAssets as MediaAsset[] ?? allAssets.ToArray();
                if (!string.IsNullOrEmpty(param.sSearch))
                {
                    //Used if particulare columns are filtered 
                    //var titleFilter = Convert.ToString(Request["sSearch_1"]);
                    //var encodingFilter = Convert.ToString(Request["sSearch_2"]);
                    //var protectionFilter = Convert.ToString(Request["sSearch_3"]);

                    //Optionally check whether the columns are searchable at all 
                    var isTitleSearchable = Convert.ToBoolean(Request["bSearchable_1"]);
                    var isEncodingSearchable = Convert.ToBoolean(Request["bSearchable_2"]);
                    var isProtectionSearchable = Convert.ToBoolean(Request["bSearchable_3"]);
                    var isStatusSearchable = Convert.ToBoolean(Request["bSearchable_4"]);

                    filteredAssets = MediaControllerSvc.GetAssets()
                       .Where(c => isTitleSearchable && c.PartitionKey.ToLower().Contains(param.sSearch.ToLower())
                                   ||
                                   isEncodingSearchable && c.Encoding.ToLower().Contains(param.sSearch.ToLower())
                                   ||
                                   isProtectionSearchable && c.Protection.ToLower().Contains(param.sSearch.ToLower())
                                   ||
                                   isStatusSearchable && c.Status.ToLower().Contains(param.sSearch.ToLower()));
                }
                else
                {
                    filteredAssets = mediaAssets;
                }

                var isTitleSortable = true;
                var isEncodingSortable = Convert.ToBoolean(Request["bSortable_2"]);
                var isProtectionSortable = Convert.ToBoolean(Request["bSortable_3"]);
                var isStatusSortable = Convert.ToBoolean(Request["bSortable_4"]);
                var isCreatedSortable = true;
                var isExpiredSortable = Convert.ToBoolean(Request["bSortable_6"]);
                var isSizeSortable = Convert.ToBoolean(Request["bSortable_7"]);
                var isProcessingSortable = Convert.ToBoolean(Request["bSortable_8"]);

                var sortColumnIndex = Convert.ToInt32(Request["iSortCol_0"]);
                Func<MediaAsset, string> orderingFunction = (c => sortColumnIndex == 1 && isTitleSortable ? c.PartitionKey :
                                                               sortColumnIndex == 2 && isEncodingSortable ? c.Encoding :
                                                               sortColumnIndex == 3 && isProtectionSortable ? c.Protection :
                                                               sortColumnIndex == 4 && isStatusSortable ? c.Status :
                                                               sortColumnIndex == 5 && isCreatedSortable ? c.Created.ToShortDateString() :
                                                               sortColumnIndex == 6 && isExpiredSortable ? c.ExpireOn.ToShortDateString() :
                                                               sortColumnIndex == 7 && isSizeSortable ? c.Size.ToString() :
                                                               sortColumnIndex == 8 && isProcessingSortable ? c.ProcessingTime :
                                                               "");

                //var sortDirection = Request["sSortDir_0"]; // asc or desc
                var sortDirection = "desc"; // asc or desc
                filteredAssets = sortDirection == "asc" ? filteredAssets.OrderBy(orderingFunction) : filteredAssets.OrderByDescending(orderingFunction);

                var displayedAssets = (param.iDisplayLength > -1) ? filteredAssets.Skip(param.iDisplayStart).Take(param.iDisplayLength) :
                    filteredAssets.Skip(param.iDisplayStart);
                var result = from c in displayedAssets select new[] { 
                    GetThumbnailString(c),
                    GetTitleString(c), 
                    c.Encoding, 
                    c.Protection, 
                    FormatStatus(c.Status),
                    c.Created.ToString("MM-dd-yyyy"),
                    c.ProcessingTime,
                    c.ExpireOn.ToString("MM-dd-yyyy"),
                };
                return Json(new
                {
                    param.sEcho,
                    iTotalRecords = mediaAssets.Count(),
                    iTotalDisplayRecords = filteredAssets.Count(),
                    aaData = result
                },
                JsonRequestBehavior.AllowGet);
            }


            #endregion




            #region Partial Views
            /// <summary>
            /// 
            /// </summary>
            /// <returns></returns>
            public PartialViewResult EncodersAction()
            {
                var model = GetEncoders();

                var jsonModel = new JavaScriptSerializer().Serialize(model);

                return PartialView("_Encoders", jsonModel);
            }


            /// <summary>
            /// 
            /// </summary>
            /// <returns></returns>
            public JsTreeModel[] GetEncoders()
            {
                return MediaControllerSvc.GetPresetCategories().Select(category => new JsTreeModel
                {
                    data = category,
                    attr = new JsTreeAttribute { id = category },
                    children = MediaControllerSvc.GetEncoders(category).Select(preset => new JsTreeModel
                    {
                        data = preset.RowKey,
                        attr = new JsTreeAttribute { id = preset.RowKey }
                    }).ToArray()
                }).ToArray();
            }

            #endregion



            #region Help Methods

            /// <summary>
            /// Outputs the Title cell
            /// </summary>
            /// <param name="title"></param>
            /// <param name="asset"></param>
            /// <returns></returns>
            private string GetThumbnailString(MediaAsset asset)
            {
                var mediaLink = asset.Thumbnail;
                var onClick = "";
                if (asset.Url.Contains("manifest"))
                {
                    //onClick = "window.open('SmoothStreamingPlayer.html?mediaurl=" + asset.Url + "');";
                    onClick = "playSmoothStreaming('" + asset.Url + "');";
                }
                else
                    onClick = "playMedia('" + asset.Url + "', 'video/mp4');";

                if (asset.Status.ToLower().Equals("finished"))
                {
                    return "<img src=\"" +
                            mediaLink + "\" OnClick=\"" + onClick + "\" class=\"media-player\" onmouseover=\"this.style.cursor='pointer'\" onmouseout=\"this.style.cursor='default'\" style='width:48px; height:32px' />";
                }
                return String.Empty;
            }



            /// <summary>
            /// 
            /// </summary>
            /// <param name="asset"></param>
            /// <returns></returns>
            private string GetTitleString(MediaAsset asset)
            {
                if (asset.Status.ToLower().Equals("finished"))
                {
                    if (DateTime.UtcNow.AddDays(1).CompareTo(asset.ExpireOn) > -1)   //Will be expired in less than a day
                    {
                        return "<strong style='color:#f00'>" + asset.PartitionKey + "</strong>";
                    }
                    return "<strong style='color:#0094ff'>" + asset.PartitionKey + "</strong>";
                }
                return asset.PartitionKey;
            }


            /// <summary>
            /// 
            /// </summary>
            /// <param name="size"></param>
            /// <returns></returns>
            private string FormatSize(string size)
            {
                if (String.IsNullOrEmpty(size)) return String.Empty;

                var dSize = Double.Parse(size);

                if (dSize >= 1000)
                    return size.Substring(0, size.Length - 3) + "." + size.Substring(size.Length - 3, 2) + "GB";
                if (dSize > 0)
                    return size + "MB";

                return size + "KB";
            }




            private string FormatStatus(string status)
            {
                return status != "Finished" ? 
                    string.Format("{0}{1}", "<img src='Images/AnimatedHourGlass.gif' alt='Processing' /> ", status) : status;
            }

            #endregion


            [HttpPost]
            public ActionResult GetCurrentConnection()
            {
                if (MediaControllerSvc == null) 
                {
                    throw new Exception("MediaControllerSvc is null.");
                }
                return (!String.IsNullOrEmpty(MediaControllerSvc.DbConnectionString)) ? Json("on") : Json("off");
            }


            [HttpPost]
            public ActionResult SqlOn()
            {
                SetConnectionString(true);
                ViewBag.Message = (MediaControllerSvc == null || String.IsNullOrEmpty(MediaControllerSvc.DbConnectionString)) ? String.Format("http://{0}.table.core.windows.net",
                    ConfigurationManager.AppSettings["accountName"]) : MediaControllerSvc.DbConnectionString;
                MediaServicesAPI.WriteLog("DEBUG: Data Source has changed to Azure SQL Database");

                return Json(ViewBag.Message);
            }


            [HttpPost]
            public ActionResult TablesOn()
            {
                SetConnectionString(false);
                ViewBag.Message = (MediaControllerSvc == null || String.IsNullOrEmpty(MediaControllerSvc.DbConnectionString)) ? String.Format("http://{0}.table.core.windows.net",
                    ConfigurationManager.AppSettings["accountName"]) : MediaControllerSvc.DbConnectionString;
                MediaServicesAPI.WriteLog("DEBUG: Data Source has changed to Azure Table");

                return Json(ViewBag.Message);
            }


 

            #region Encoding

            /// <summary>
            /// Begin processing on the server side
            /// </summary>
            /// <param name="inputParams"></param>
            /// <returns></returns>
            [HttpPost]
            public ActionResult BeginEncoding(ServerParamModel inputParams)
            {
                try
                {
                    //State: Ready

                    //Encoding Presets
                    MediaControllerSvc.Encoders = GetEncodingPresets(inputParams.Presets, inputParams.PresetDelimeter);

                    //Media Encoder
                    MediaControllerSvc.MediaProcessor = inputParams.MediaProcessor;

                    //Protection
                    var status = SetProtectionLevel(inputParams.Protection);
                    if (!String.IsNullOrEmpty(status)) return Json(status);
                
                    //Set expiration date
                    DateTime expirationDate;
                    const int policyTimeout = 1; // in days

                    if (!String.IsNullOrEmpty(inputParams.ExpireOn) &&
                        DateTime.TryParse(inputParams.ExpireOn, out expirationDate))
                        MediaControllerSvc.ExpireOn = new TimeSpan(expirationDate.Ticks - DateTime.UtcNow.Ticks);
                    else
                        MediaControllerSvc.ExpireOn = TimeSpan.FromDays(policyTimeout);

                    //Set media status
                    MediaControllerSvc.SetReady();

                    return ConstructJsonResult(string.Format(CultureInfo.CurrentCulture, Resources.JobSubmitted), WAMSConstants.ProgressRefreshInteval, false);
                }
                catch (Exception ex)
                {
                    return Json(ex.Message);
                }
            
            }


            /// <summary>
            /// Checks the server processing status and updates the client data with the latest view
            /// </summary>
            /// <returns></returns>
            [HttpPost]
            public ActionResult UpdateData()
            {
                var status = String.Empty;
                var interval = WAMSConstants.DefaultRefreshInterval;

                if (MediaControllerSvc == null)
                    return ConstructJsonResult(string.Format(CultureInfo.CurrentCulture, Resources.SessonExpired), interval, false);

                //TODO: Finsh Switch

                switch (MediaControllerSvc.State)
                {
                    case MediaStates.Ready:
                        //Create media channel
                        MediaControllerSvc.CreateMediaJobs();

                        //Create Media entries
                        status = MediaControllerSvc.CreateMediaEntries();
                        interval = WAMSConstants.TasksDelay;
                        return ConstructJsonResult(status, interval, true);

                    case MediaStates.Created:
                        //Begin server processing
                        if (!MediaControllerSvc.ProcessMedia())
                            return ConstructJsonResult(MediaServicesAPI.LastMessage, WAMSConstants.DefaultRefreshInterval, true);
                        interval = WAMSConstants.ProgressRefreshInteval;
                        return ConstructJsonResult(status, interval, true);

                    case MediaStates.Ingesting:
                        return ConstructJsonResult(string.Format(CultureInfo.CurrentCulture, Resources.AssetUploading), WAMSConstants.ProgressRefreshInteval, false);

                    case MediaStates.Ingested:
                        //Execute Jobs
                        if (!MediaControllerSvc.BeginEncoding())
                            return ConstructJsonResult(MediaServicesAPI.LastMessage, WAMSConstants.DefaultRefreshInterval, true);
                        interval = WAMSConstants.ProgressRefreshInteval;
                        return ConstructJsonResult(status, interval, true);

                    case MediaStates.Started:
                        var refreshScreen = MediaControllerSvc.DirtyData;
                        MediaControllerSvc.DirtyData = false;
                        return ConstructJsonResult(string.Format(CultureInfo.CurrentCulture, Resources.JobStarted), WAMSConstants.ProgressRefreshInteval, refreshScreen);

                    case MediaStates.Queued:
                        refreshScreen = MediaControllerSvc.DirtyData;
                        MediaControllerSvc.DirtyData = false;
                        return ConstructJsonResult(string.Format(CultureInfo.CurrentCulture, Resources.JobInProcess), 
                            WAMSConstants.ProgressRefreshInteval, refreshScreen);
                
                    case MediaStates.Processed:
                        //Get Job Results
                        return ConstructJsonResult(!MediaControllerSvc.GetJobResults() ? MediaServicesAPI.LastMessage 
                            : string.Format(CultureInfo.CurrentCulture, Resources.JobFinished), WAMSConstants.DefaultRefreshInterval, true);

                    case MediaStates.Canceled:
                        //Reset the media controller
                        Initialize();
                        //Notify about the failure
                        return ConstructJsonResult(string.Format(CultureInfo.CurrentCulture, Resources.FailedToEncode), WAMSConstants.DefaultRefreshInterval, true);
                    default:
                        return ConstructJsonResult(status, interval, false);
                }
            }


            /// <summary>
            /// 
            /// </summary>
            /// <param name="message"></param>
            /// <param name="interval"></param>
            /// <param name="refreshData"></param>
            /// <returns></returns>
            private JsonResult ConstructJsonResult(string message, int interval, bool refreshData)
            {
                if (refreshData)
                {
                    var assets = MediaControllerSvc.GetAssetsInProgress();
                    if (!assets.Any())
                    {
                        return Json(new RefreshDataModel
                        {
                            RefreshInterval = WAMSConstants.DefaultRefreshInterval,
                            StatusMessage = message ?? string.Empty
                        });
                    }

                    var pluralJobs = (assets.Count() > 1);
                    return Json(new RefreshDataModel
                    {
                        RefreshInterval = WAMSConstants.ProgressRefreshInteval,
                        StatusMessage = message ?? string.Format("There {0} {1} encoding {2} still in progress...",
                                                                 pluralJobs ? "are" : "is", assets.Count(),
                                                                 pluralJobs ? "jobs" : "job")
                    });
                }

                return Json(new RefreshDataModel
                {
                    RefreshInterval = interval,
                    StatusMessage = message ?? string.Empty
                });
            }

            /// <summary>
            /// 
            /// </summary>
            /// <param name="protection"></param>
            /// <returns></returns>
            private string SetProtectionLevel(string protection)
            {
                if (MediaControllerSvc == null) return string.Format(CultureInfo.CurrentCulture, Resources.SessonExpired);

                if (String.IsNullOrEmpty(protection))
                {
                    MediaControllerSvc.Encryption = AssetCreationOptions.None;
                    return String.Empty;
                }

                switch (protection)
                {
                    case "1":
                        MediaControllerSvc.Encryption = AssetCreationOptions.StorageEncrypted;
                        break;
                    case "2":
                        MediaControllerSvc.Encryption = AssetCreationOptions.CommonEncryptionProtected;
                        break;
                    case "3":
                        MediaControllerSvc.Encryption = AssetCreationOptions.EnvelopeEncryptionProtected;
                        break;
                    default:
                        MediaControllerSvc.Encryption = AssetCreationOptions.None;
                        break;
                }

                return String.Empty;
            }





            /// <summary>
            /// 
            /// </summary>
            /// <param name="presets"></param>
            /// <param name="delimiter"></param>
            /// <returns></returns>
            private List<String> GetEncodingPresets(string presets, string delimiter)
            {
                var presetArray = presets.Split(delimiter[0]);

                return presetArray.ToList();
            }
            #endregion



            #region Client Access Policy
            private void CreateSilverlightPolicy()
            {
                var client = WAMSConstants.GetBlobClient();
                var rootContainer = client.GetRootContainerReference();
                rootContainer.CreateIfNotExists();
                rootContainer.SetPermissions( new BlobContainerPermissions 
                    {
                        PublicAccess = BlobContainerPublicAccessType.Blob
                    });
                var blob = rootContainer.GetBlockBlobReference("clientaccesspolicy.xml");
                blob.Properties.ContentType = "text/xml";
                const string content = @"<?xml version=""1.0"" encoding=""utf-8""?>" +
                                       @"<access-policy>" +
                                       @"<cross-domain-access>" +
                                       @"<policy>" +
                                       @"<allow-from http-methods=""*"" http-request-headers=""*"">" +
                                       @"<domain uri=""*"" />" +
                                       @"<domain uri=""http://*"" />" +
                                       @"</allow-from>" +
                                       @"<grant-to>" +
                                       @"<resource path=""/"" include-subpaths=""true"" />" +
                                       @"</grant-to>" +
                                       @"</policy>" +
                                       @"</cross-domain-access>" +
                                       @"</access-policy>";

                using (var writer = new StreamWriter(blob.OpenWrite()))
                {
                    writer.Write(content);
                }

            }
            #endregion

            #region Cross Domain Policy
            private void CreateFlashPolicy()
            {
                var client = WAMSConstants.GetBlobClient();
                var rootContainer = client.GetRootContainerReference();
                rootContainer.CreateIfNotExists();
                rootContainer.SetPermissions(new BlobContainerPermissions
                {
                    PublicAccess = BlobContainerPublicAccessType.Blob
                });
                var blob = rootContainer.GetBlockBlobReference("crossdomain.xml");
                blob.Properties.ContentType = "text/xml";
                const string content = @"<?xml version=""1.0"" encoding=""utf-8""?>" +
                                        @"<cross-domain-policy>" +  
                                        @"<allow-access-from domain=""*"" />" +  
                                        @"</cross-domain-policy>";  

                using (var writer = new StreamWriter(blob.OpenWrite()))
                {
                    writer.Write(content);
                }

            }
            #endregion

        }

}
