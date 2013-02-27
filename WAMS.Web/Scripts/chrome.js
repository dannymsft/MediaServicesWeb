var hostweburl;
// Load the SharePoint resources.
$(document).ready(function () {
    // Get the URI decoded app web URL.
    hostweburl = decodeURIComponent(getQueryStringParameter("SPHostUrl") );
    // The SharePoint js files URL are in the form
    // web_url/_layouts/15/resource.js
    var scriptbase = hostweburl + "/_layouts/15/";
    // Load the js file and continue to the success handler.
    $.getScript(scriptbase + "SP.UI.Controls.js", renderChrome);
});

// Function to prepare the options and render the control.
function renderChrome() {
    // The Help, Account, and Contact pages receive the 
    // same query string parameters as the main page.
    var options = {
        "appIconUrl": "/Images/mediaservices.jpg",
        "appTitle": "Azure Media Services on Office 365",
        "settingsLinks": [
            {
                "linkUrl": "/Account/Register",
                "displayName": "Register"
            },
            {
                "linkUrl": "/Account/Login",
                "displayName": "Login"
            },
            {
                "linkUrl": "Home/VOD",
                "displayName": "Video On Demand"
            },
            {
                "linkUrl": "Home/LiveStreaming",
                "displayName": "Live Streaming"
            },
            {
                "linkUrl": "elmah.axd",
                "displayName": "Application Logs"
            }
        ]
    };

    var nav = new SP.UI.Controls.Navigation("chrome_ctrl_placeholder", options);
    nav.setVisible(true);
}

function getQueryStringParameter(paramToRetrieve) {
    var strParams = "";
    var params = document.URL.split("?");

    if (params.length == 1) {
        params = document.referrer.split("?");
    }

    if (params.length > 1) {
        params = params[1].split("&");
        for (var i = 0; i < params.length; i = i + 1) {
            var singleParam = params[i].split("=");
            if (singleParam[0] == paramToRetrieve)
                return singleParam[1];
        }
    }
    return strParams;
}
