using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using g3;

namespace SliceViewer
{
    public static class GtkUtil
    {
        /// <summary>
        /// Fix some issues w/ GtkSharp DLLs on windows.
        /// see: https://github.com/picoe/Eto/issues/442
        ///      https://forums.xamarin.com/discussion/15568/unable-to-load-dll-libgtk-win32-2-0-0-dll
        ///      https://forums.xamarin.com/discussion/2091/cannot-run-gtk-project
        ///      https://github.com/mono/monodevelop/commit/ad672ce79a50ce844398ae30cce8005163e41d0e
        /// </summary>
        public static bool CheckWindowsGtk()
        {
            if (Util.IsRunningOnMono())
                return true;

            string location = null;
            Version version = null;
            Version minVersion = new Version(2, 12, 22);
            using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Xamarin\GtkSharp\InstallFolder")) {
                if (key != null)
                    location = key.GetValue(null) as string;
            }
            using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Xamarin\GtkSharp\Version")) {
                if (key != null)
                    Version.TryParse(key.GetValue(null) as string, out version);
            }
            //TODO: check build version of GTK# dlls in GAC
            if (version == null || version < minVersion || location == null || !File.Exists(Path.Combine(location, "bin", "libgtk-win32-2.0-0.dll"))) {
                Console.WriteLine("Did not find required GTK# installation");
                //  string url = "http://monodevelop.com/Download";
                //  string caption = "Fatal Error";
                //  string message =
                //      "{0} did not find the required version of GTK#. Please click OK to open the download page, where " +
                //      "you can download and install the latest version.";
                //  if (DisplayWindowsOkCancelMessage (
                //      string.Format (message, BrandingService.ApplicationName, url), caption)
                //  ) {
                //      Process.Start (url);
                //  }
                return false;
            }
            Console.WriteLine("Found GTK# version " + version);
            var path = Path.Combine(location, @"bin");
            Console.WriteLine("SetDllDirectory(\"{0}\") ", path);
            try {
                if (SetDllDirectory(path)) {
                    return true;
                }
            } catch (EntryPointNotFoundException) {
            }
            // this shouldn't happen unless something is weird in Windows
            Console.WriteLine("Unable to set GTK+ dll directory");
            return true;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        static extern bool SetDllDirectory(string lpPathName);
    }


}

