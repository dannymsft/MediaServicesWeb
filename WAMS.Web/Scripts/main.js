// Global variables
var timerId;
var defaultInterval = 10000; // set the default interval between client calls to the server to 10 seconds
var statusMsgInterval = 4000;   // 4 seconds
var oTable;
var selectedPresets;
var protectionMode = 0; // HTTPS by default
var statusbar = null;
//Set default expiration date to the min date (current + 1)
var expirationDate = moment().add('days', 1)._d;



$(document).ready(function () {
    /* Init Media Assets Table */
    //Load the data grid
    oTable = $('#mediaLibrary').dataTable({
        "iDisplayLength": 8,
        "aLengthMenu": [[8, 15, 25, 50, -1], [8, 15, 25, 50, "All"]], //Changing the Show XXXX items per page drop-down
        "bServerSide": true, //By setting the bServerSide parameter to true, DataTables is initialized to work with the server-side page.
        "sAjaxSource": "Home/DataHandler", //point to an arbitrary URL of the page that will provide data to client-side table
        "bProcessing": true, //tells DataTables to show the "Processing..." message while the data is being fetched from the server
        "bStateSave": false, //length, filtering, pagination and sorting are saved in the user's cookie
        "iCookieDuration": 60 * 60 * 24, // cookies will expire in 1 day
        "bJQueryUI": true, // ThemeRoller theme
        "sPaginationType": "full_numbers", //pagination display
        "oLanguage": {
            "sLengthMenu": "Display _MENU_ media assets per page",
            "sZeroRecords": "No matching media assets found",
            "sInfo": "Showing _START_ to _END_ of _TOTAL_ media assets",
            "sInfoEmpty": "Showing 0 to 0 of 0 media assets",
            "sInfoFiltered": "(filtered from _MAX_ total media assets)"
        },
        "sDom": '<"toolbar">flti<"bottom"p>r',
        "aaSorting": [[5, "desc"], [1, "asc"]],
        "aoColumns": [
                        {
                            "sName": "THUMBNAIL",
                            "bSearchable": false,
                            "bSortable": false,
                            "fnRender": function (oObj) {
                                return oObj.aData[0];
                            }
                        },
			            { "sName": "TITLE" },
			            { "sName": "ENCODING" },
			            { "sName": "PROTECTION" },
			            { "sName": "PROGRESS" },
			            {
			                "sName": "STARTED",
			                "sType": "date-string-desc"
			            },
			            { "sName": "COMPLETED" },
                        { "sName": "EXPIRED" }

        ]
    });

    $("div.toolbar").html('<table class="legend-table" title=\"Legend\">' +
                '<tr><td><label class=\"legend-ready\">Movie</label></td><td>Asset is ready</td></tr>' +
                '<tr><td><label class=\"legend-soon-expire\">Movie</label><td>Will expire soon</td></tr>' + 
                '<tr><td><label class=\"legend-expired\" >Movie</label><td>Expired or is not ready</td></tr>' +
                '</table>');



    

    //Init status bar
    statusbar = new StatusBar(null, {
        showCloseButton: true,
        additive: false,
        afterTimeoutText: ""
    });
    statusbar.hide();


    // Initialize Smart Wizard
    $('#wizard').smartWizard(
        {
            // Properties
            selected: 0,  // Selected Step, 0 = first step   
            keyNavigation: true, // Enable/Disable key navigation(left and right keys are used if enabled)
            enableAllSteps: false,  // Enable/Disable all steps on first load
            transitionEffect: 'slide', // Effect on navigation, none/fade/slide/slideleft
            contentURL: null, // specifying content url enables ajax content loading
            contentCache: false, // cache step contents, if false content is fetched always from ajax url
            cycleSteps: false, // cycle step navigation
            enableFinishButton: false, // makes finish button enabled always
            errorSteps: [],    // array of step numbers to highlighting as error steps
            labelNext: 'Next', // label for Next button
            labelPrevious: 'Back', // label for Previous button
            labelFinish: 'Finish',  // label for Finish button        
            // Events
            onLeaveStep: leaveStepCallback, // triggers when leaving a step
            onShowStep: null,  // triggers when showing a step
            onFinish: onFinishCallback  // triggers when Finish button is clicked
        });




    
    //Set up the timer to refresh the screen with the server data
    if (timerId) window.clearInterval(timerId);
    window.timerId = window.setInterval(refreshData, defaultInterval);
    
    //Close SmoothStreaming player window
    closeSSPlayer();
});


//////////// End of Page Load Event ////////////////////////////////////////////////


//Refresh Data
function refreshData() {
    $.post("Home/UpdateData", null, function (data) {
        //clear the current interval
        window.clearInterval(timerId);
        //reset the timer to the new interval value
        timerId = window.setInterval(refreshData, data.RefreshInterval);

        //Set Switch control
        //setSwitcher();

        if (data.StatusMessage !== "") {
            //Update status message
            statusbar.show(data.StatusMessage, statusMsgInterval);
            //Update the data table
            window.oTable.fnDraw();
        }


    });
}



//Set the SQL Switcher
function setSwitcher() {
    $.post("Home/GetCurrentConnection", null, function (switchPosition) {
        $('#switch').iphoneSwitch(switchPosition,
            function () {
                $.post("Home/SqlOn", null, function (data) {
                    location.reload();
                });
            },
            function () {
                $.post("Home/TablesOn", null, function (data) {
                    location.reload();
                });
            },
            {
                switch_on_container_path: '/Images/iphone_switch_container_off.png'
            });
    });
}


function leaveStepCallback(obj) {
    var stepNum = obj.attr('rel'); // get the current step number


    if (stepNum == 3) //Stepping into Finish
    {
        $("#summaryPanel").html(function () {
            var html = "<table class='panel-summary' border=4>";
            var assetArray;

            //Header row
            html += "<thead><tr><th>Media Assets</th><th>Media Processor</th><th>Encoding Presets</th><th>Encryption</th><th>Expires On</th></tr></thead>";

            //Body content
            html += "<tbody>";

            //Get the list of all selected presets
            var presetArray = $("#taskPresets").jstree("get_checked", null, true);

            //Get the list of all uploaded assets
            if (window.uploadedFiles !== null) {
                assetArray = window.uploadedFiles.files;
            } else {
                assetArray = {};
            }

            //Determine the number of rows
            var rowCount = Math.max(assetArray.length, presetArray.length);

            for (var i = 0; i < rowCount; i++) {
                html += "<tr>";

                //Media Assets
                html += "<td>";
                if (i < assetArray.length) { html += assetArray[i].name; }
                html += "</td>";

                //Media Processor
                html += "<td>";
                if (i == 0) { html += $("#mediaProcessor").select2("val"); }
                html += "</td>";

                //Encoding Presets
                html += "<td>";
                if (i < presetArray.length) { html += $(presetArray[i]).attr("id"); }
                html += "</td>";

                //Encryption
                html += "<td>";
                if (i == 0) { html += displaySelectedProtection(window.protectionMode); }
                html += "</td>";

                //Expires on
                html += "<td>";
                if (i == 0) { html += window.expirationDate; }
                html += "</td>";

                //End of row
                html += "</tr>";
            }
            //End of the table
            html += "</tbody></table>";

            //Assign to the Summary Panel's innerHTML attribute
            return html;

        });
    }
    return true; // return false to stay on step and true to continue navigation 
}



//Called upon form submit
function onFinishCallback() {
    if (validateAllSteps()) {
        var serverData = getServerData();

        //Begin Encoding
        $.post("Home/BeginEncoding", serverData, function (data) {
            //clear the current interval
            window.clearInterval(timerId);
            //reset the timer to the new interval value
            timerId = window.setInterval(refreshData, data.RefreshInterval);

            if (data.StatusMessage !== "") {
                //Update status message
                statusbar.show(data.StatusMessage, statusMsgInterval);
            }

            //Close Wizard On Finish
            closeWizardOnFinish();
        });
    }
}


function getServerData() {
    var protection = window.protectionMode;
    var expire = window.expirationDate;
    var presetDelimiter = ";";
    var processor = $("#mediaProcessor").select2("val");
    var presets = $("#taskPresets").jstree("get_checked", null, true);
    var presetString = presetToString(presets, presetDelimiter);
    //var presetArray = presetToArray(presets);

    return { MediaProcessor: processor, Presets: presetString, Protection: protection, ExpireOn: expire, PresetDelimeter: presetDelimiter };
}



function presetToArray(presets) {
    var presetArray = new Array();
    $.each(presets, function (index, data) {
        presetArray.push($(data).attr("id"));
    });
    return presetArray;
}

function presetToString(arr, del) {
    var str = "";
    $.each(arr, function (index, value) {
        str += $(value).attr("id") + del;
    });
    return str.substring(0, str.length - 1);
}

function closeWizardOnFinish() {
    //$('formMain').submit();
    $('.simplemodal-close').click();
    $('form').submit();
    //Update the data table
    window.oTable.fnDraw();
}


function displaySelectedProtection(protectionMode) {
    switch (protectionMode) {
        case 0:
            return "HTTPS & SAS";
        case 1:
            return "Storage Encryption";
        case 2:
            return "UltraViolet DRM";
        case 3:
            return "PlayReady DRM";
        default:
            return "";
    }
}


function displayError(status) {
    $("#divIngestResult").text(status.responseText);
}

// Your Step validation logic
function validateSteps(stepnumber) {
    var isStepValid = true;
    // validate step 1
    if (stepnumber == 1) {
        // Your step validation logic
        // set isStepValid = false if has errors
    }

    // ...      
}


function validateAllSteps() {
    var isStepValid = true;
    // all step validation logic     
    return isStepValid;
}




// use this as a global instance to customize constructor
// or do nothing and get a default status bar
function showStatus(message, timeout, additive, isError) {
    if (!statusbar)
        statusbar = new StatusBar();
    statusbar.show(message, timeout, additive, isError);
}



function closeWizard() {
    //$('#uploader').innerHTML = "";
}


function addEventDelegate() {
    try {
        // Setup the event listeners.
        get('mediafiles').addEventListener('change', handleFileSelect, false);
    } catch (e) {
        setTimeout(addEventDelegate, 1);
    }
}






function checkDate(sender, args) {
    if (sender._selectedDate < new Date()) {
        alert("You cannot select a day earlier than today!");
        sender._selectedDate = new Date();
        // set the date back to the current date
        sender._textbox.set_Value(sender._selectedDate.format(sender._format))
    }
}




function playMedia(url, type) {
    var mediaSrc = new Object();
    mediaSrc.src = url;
    mediaSrc.type = type;

    var mediaPlayerPanel = document.getElementById("mediaPlayerPanel");
    if (mediaPlayerPanel != null) {
        mediaPlayerPanel.style.display = "";
        mediaPlayerPanel.style.width = "840px";
        mediaPlayerPanel.style.height = "520px";
        mediaPlayerPanel.style.position = "absolute";
        mediaPlayerPanel.style.left = "200px";
    }
    var video = document.getElementById("myVideo");
    if (video != null) {
        video.src = url;
        video.style.display = "";
        video.style.width = "820px";
        video.style.height = "480px";
    }



}






function closeMediaPlayer() {
    var video = document.getElementById("myVideo");
    if (video != null) {
        video.src = "";
        video.style.display = "none";
    }

    //var videoControl = PlayerFramework.getElementsByClass("pf-video");
    //if (videoControl != null && videoControl.length > 0) {
    //    videoControl[0].volume = 0;
    //}

    var mediaPlayerPanel = document.getElementById("mediaPlayerPanel");
    if (mediaPlayerPanel != null)
        mediaPlayerPanel.style.display = "none";
}


//////////////////////////////////////////////////////////////////////////////////
//Silverlight Control Functions

function onSilverlightError(sender, args) {
    var appSource = "";
    if (sender != null && sender != 0) {
        appSource = sender.getHost().Source;
    }

    var errorType = args.ErrorType;
    var iErrorCode = args.ErrorCode;

    if (errorType == "ImageError" || errorType == "MediaError") {
        return;
    }

    var errMsg = "Unhandled Error in Silverlight Application " + appSource + "\n";

    errMsg += "Code: " + iErrorCode + "    \n";
    errMsg += "Category: " + errorType + "       \n";
    errMsg += "Message: " + args.ErrorMessage + "     \n";

    if (errorType == "ParserError") {
        errMsg += "File: " + args.xamlFile + "     \n";
        errMsg += "Line: " + args.lineNumber + "     \n";
        errMsg += "Position: " + args.charPosition + "     \n";
    }
    else if (errorType == "RuntimeError") {
        if (args.lineNumber != 0) {
            errMsg += "Line: " + args.lineNumber + "     \n";
            errMsg += "Position: " + args.charPosition + "     \n";
        }
        errMsg += "MethodName: " + args.methodName + "     \n";
    }

    throw new Error(errMsg);
}


function onSilverlightLoad(plugIn, userContext, sender) {
    window.status +=
        plugIn.id + " loaded into " + userContext + ". ";
}


var slCtl = null;
function pluginLoaded(sender, args) {
    try {
        slCtl = sender.getHost().Content;
    } catch (e) {
        alert(e);
    }
}


function Stop() {
    if (slCtl !== null) {
        slCtl.Player.Stop();
    }
}

function createNewPlaylistItem(url) {
    try {
        if (slCtl !== null) {
            var newPlaylistItem = slCtl.Player.CreatePlaylistItem(url, '', 'Title', 'Description');
            newPlaylistItem.DeliveryMethod = 'AdaptiveStreaming';
            var newPlaylist = slCtl.Player.CreatePlaylist();
            newPlaylist.AddPlaylistItem(newPlaylistItem);
            slCtl.Player.SetPlaylist(newPlaylist);
            slCtl.Player.Play();
        } else {
            alert("Can't play the media content. Smooth Streaming Player was not properly loaded.");
        }
    } catch (e) {
        alert(e);
    }
}


function playSmoothStreaming(url)
{
    var silverlightControlHost = document.getElementById("silverlightControlHost");
    if (silverlightControlHost != null) {
        silverlightControlHost.style.display = "";
        silverlightControlHost.style.width = "840px";
        silverlightControlHost.style.height = "520px";
        silverlightControlHost.style.position = "absolute";
        silverlightControlHost.style.left = "200px";
        createNewPlaylistItem(url);
    }
}


function closeSSPlayer() {
    var silverlightControlHost = document.getElementById("silverlightControlHost");
    if (silverlightControlHost != null) {
        Stop();
        silverlightControlHost.style.display = "none";
    }
}
