// Global variables
var uploadedFiles = null;

$(function () {
    function log() {
        var str = "";

        $("#uploaderLog").show();

        plupload.each(arguments, function (arg) {
            var row = "";

            if (typeof (arg) != "string") {
                plupload.each(arg, function (value, key) {
                    // Convert items in File objects to human readable form
                    if (arg instanceof plupload.File) {
                        // Convert status to human readable
                        switch (value) {
                            case plupload.QUEUED:
                                value = 'QUEUED';
                                break;

                            case plupload.UPLOADING:
                                value = 'UPLOADING';
                                break;

                            case plupload.FAILED:
                                value = 'FAILED';
                                break;

                            case plupload.DONE:
                                value = 'DONE';
                                break;
                        }
                    }

                    if (typeof (value) != "function") {
                        row += (row ? ', ' : '') + key + '=' + value;
                    }
                });

                str += row + " ";
            } else {
                str += arg + " ";
            }
        });

        $('#log').val($('#log').val() + str + "\r\n");
    }


    // Convert divs to PLUpload queue widgets when the DOM is ready
    $("#uploader").pluploadQueue({
        // General settings
        runtimes: 'silverlight,html5,flash,html4',
        //url: 'WAMS/Handlers/FileUploadHandler.ashx',
        url: '/FileUploader/Upload',
        max_file_size: '8096mb',
        chunk_size: '512kb',
        max_file_count: 20, // user can add no more then 20 files at a time
        unique_names: true,
        multiple_queues: false,

        // Resize images on clientside if we can
        resize: { width: 320, height: 240, quality: 90 },

        // Specify what files to browse for
        filters: [
            { title: "Media files", extensions: "wmv,mp4,avi,mov,vod,divx,wma,mp3,mpeg" },
            { title: "All files", extensions: "*" }
        ],

        // Post init events, bound after the internal events
        init: {
            UploadComplete: function (files) {
                // Called when the all files upload is complete
                window.uploadedFiles = files;
            },

            Error: function (up, args) {
                // Called when a error has occured
                // Handle file specific error and general error
                if (args.file) {
                    log('[error]', args, "File:", args.file);
                } else {
                    log('[error]', args);
                }
            }
        },


        /*@cc_on 
            @set @PLUPLOAD_VER = 1.0  

            @if (@PLUPLOAD_VER == 2.0)
                // Flash settings
	            flash_swf_url: '/Scripts/plupload/2.0/Moxie.swf',

                // Silverlight settings
	            silverlight_xap_url: '/Scripts/plupload/2.0/Moxie.xap'
            @else
                // Flash settings
	            flash_swf_url: '/Scripts/plupload/1.5.4/plupload.flash.swf',

                // Silverlight settings
	            silverlight_xap_url: '/Scripts/plupload/1.5.4/plupload.silverlight.xap'
            @end
        @*/
    });

    $('#log').val('');
    $('#uploaderLog').hide();


    // Client side form validation
    $('form').submit(function (e) {
        var uploader = $('#uploader').pluploadQueue();

        // Files in queue upload them first
        if (uploader.files.length > 0) {
            // When all files are uploaded submit form
            uploader.bind('StateChanged', function () {
                if (uploader.files.length === (uploader.total.uploaded + uploader.total.failed)) {
                    $('form')[0].submit();
                }
            });


            uploader.start();
        } else
            alert('You must at least upload one file.');

        e.preventDefault();

        return false;
    });

});

