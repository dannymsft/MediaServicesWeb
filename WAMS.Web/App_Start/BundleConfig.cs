using System.Web;
using System.Web.Optimization;

namespace WAMSDemo
{
    public class BundleConfig
    {
        // For more information on Bundling, visit http://go.microsoft.com/fwlink/?LinkId=254725
        public static void RegisterBundles(BundleCollection bundles)
        {
            BundleTable.EnableOptimizations = true;

            ////////////////////////////////////////////////////////////////////////////////////////////////
            //Styles
            bundles.Add(new StyleBundle("~/Content/css").Include("~/Content/Site.css"));

            bundles.Add(new StyleBundle("~/Content/themes/base/css").Include(
                        "~/Content/themes/base/jquery.ui.core.css",
                        "~/Content/themes/base/jquery.ui.resizable.css",
                        "~/Content/themes/base/jquery.ui.selectable.css",
                        "~/Content/themes/base/jquery.ui.accordion.css",
                        "~/Content/themes/base/jquery.ui.autocomplete.css",
                        "~/Content/themes/base/jquery.ui.button.css",
                        "~/Content/themes/base/jquery.ui.dialog.css",
                        "~/Content/themes/base/jquery.ui.slider.css",
                        "~/Content/themes/base/jquery.ui.tabs.css",
                        "~/Content/themes/base/jquery.ui.datepicker.css",
                        "~/Content/themes/base/jquery.ui.progressbar.css",
                        "~/Content/themes/base/jquery.ui.theme.css",
                        "~/Scripts/themes/apple/style.css"));

            //bundles.Add(new StyleBundle("~/Content/Bootstrap/css").Include(
            //            "~/Content/bootstrap.min.css"));


            bundles.Add(new StyleBundle("~/Content/SimpleModal/css").Include("~/Content/SimpleModal/osx.css"));

            bundles.Add(new StyleBundle("~/Content/SmartWizard/css").Include("~/Content/smartWizard/smart_wizard.css"));


            bundles.Add(new StyleBundle("~/Content/PlUpload/css").Include(
                "~/Scripts/plupload/1.5.4/jquery.plupload.queue/css/jquery.plupload.queue.css"));

            bundles.Add(new StyleBundle("~/Content/Player/css").Include("~/Content/player/playerframework.min.css",
                        "~/Content/player/style.css"));

            bundles.Add(new StyleBundle("~/Content/DataTables/css").Include(
                "~/Content/dataTables/jquery.dataTables_themeroller.css"));

            bundles.Add(new StyleBundle("~/Content/select2/css").Include(
                "~/Content/select2/select2.css"));

            bundles.Add(new StyleBundle("~/Content/pickdate/css").Include(
                "~/Content/pickdate/pickadate.01.default.css"));


            ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////
            //JQuery scripts
            bundles.Add(new ScriptBundle("~/bundles/jquery").Include(
                            "~/Scripts/jquery-{version}.js",
                            "~/Scripts/jquery-ui-{version}.js"));


            // Use the development version of Modernizr to develop with and learn from. Then, when you're
            // ready for production, use the build tool at http://modernizr.com to pick only the tests you need.
            bundles.Add(new ScriptBundle("~/bundles/modernizr").Include(
                        "~/Scripts/modernizr-*"));



            //UI scripts
            bundles.Add(new ScriptBundle("~/bundles/ui").Include(
                        "~/Scripts/ui.core*",
                        "~/Scripts/ui.progressbar*"));

            //Smart Wizard
            bundles.Add(new ScriptBundle("~/bundles/smartWizard").Include(
                        "~/Scripts/smartWizard/jquery.smartWizard-*"));

            //Media Player scripts
            bundles.Add(new ScriptBundle("~/bundles/mediaPlayerFramework").Include(
                        "~/Scripts/html5_ie.js",
                        "~/Scripts/player/playerframework.min.js",
                        "~/Scripts/Silverlight.js"));

            //PlUpload scripts
#if PLUPLOAD_2
            bundles.Add(new ScriptBundle("~/bundles/PlUpload").Include(
                        "~/Scripts/plupload/2.0/plupload.full.min.js"));
            bundles.Add(new ScriptBundle("~/bundles/PlUpload/UI").Include(
                        "~/Scripts/plupload/2.0/jquery.plupload.queue/jquery.plupload.queue.js"));
#else
            bundles.Add(new ScriptBundle("~/bundles/PlUpload").Include(
                        "~/Scripts/plupload/1.5.4/plupload.full.js"));
            bundles.Add(new ScriptBundle("~/bundles/PlUpload/UI").Include(
                        "~/Scripts/plupload/1.5.4/jquery.plupload.queue/jquery.plupload.queue.js"));
#endif


            //Data Tables scripts
            bundles.Add(new ScriptBundle("~/bundles/DataTables").Include(
                        "~/Scripts/dataTables/jquery.dataTables.min.js",
                        "~/Scripts/dataTables/dataTablesSort.js"));


            //Select2 scripts
            bundles.Add(new ScriptBundle("~/bundles/Select2").Include(
                        "~/Scripts/select2/select2.js"));

            //SlimScroll scripts
            bundles.Add(new ScriptBundle("~/bundles/SlimScroll").Include(
                        "~/Scripts/slimScroll/slimScroll.js"));

            //Tinycarousel scripts
            bundles.Add(new ScriptBundle("~/bundles/TinyCarousel").Include(
                        "~/Scripts/tinyCarousel/jquery.tinycarousel.min.js"));


            //Encoder script
            bundles.Add(new ScriptBundle("~/bundles/Encoders").Include(
                        "~/Scripts/encoders.js"));

            //Protection script
            bundles.Add(new ScriptBundle("~/bundles/Protection").Include(
                        "~/Scripts/protection.js"));


            //Pick a Date scripts
            bundles.Add(new ScriptBundle("~/bundles/PickDate").Include(
                        "~/Scripts/pickdate/rainbow.js",
                        "~/Scripts/pickdate/pickadate.legacy.js"));

            bundles.Add(new ScriptBundle("~/bundles/moment").Include(
                                "~/Scripts/moment_min.js"));


            //Main script
            bundles.Add(new ScriptBundle("~/bundles/Main").Include(
                        "~/Scripts/main.js"));


            //Uploader script
            bundles.Add(new ScriptBundle("~/bundles/Uploader").Include(
                        "~/Scripts/uploader.js"));



        }
    }
}