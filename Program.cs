using System;
using System.Linq;
using System.Threading;
using System.Windows;

namespace BPGPUpdater
{
    internal static class Program
    {
        /// <summary>Start hidden in the tray (used by "Start with Windows")</summary>
        public const string TrayArgument = "--tray";
        /// <summary>Started by the self-update: wait for the old instance to exit</summary>
        public const string UpdatedArgument = "--updated";
        /// <summary>Test switch: also install alpha and beta releases</summary>
        public const string PreReleaseArgument = "--prerelease";
        /// <summary>Test switch: --test-zip &lt;path&gt; uses a local addon zip as the latest release</summary>
        public const string TestZipArgument = "--test-zip";
        /// <summary>Test switch: --screenshot &lt;path&gt; saves each tab as path-N.png and exits</summary>
        public const string ScreenshotArgument = "--screenshot";

        [STAThread]
        private static void Main(string[] args)
        {
            bool tray = HasArgument(args, TrayArgument);
            bool updated = HasArgument(args, UpdatedArgument);
            bool preReleases = HasArgument(args, PreReleaseArgument);
            string testZip = ArgumentValue(args, TestZipArgument);
            string screenshot = ArgumentValue(args, ScreenshotArgument);

            bool created;
            using (var mutex = new Mutex(true, @"Local\BPGPUpdater", out created))
            {
                bool owned = created || (updated && Acquire(mutex));
                if (!owned)
                {
                    if (!tray) MessageBox.Show("BPGP Updater is already running. Look for its icon next to the clock.", "BPGP Updater", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                try
                {
                    var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("pack://application:,,,/BPGPUpdater;component/Theme.xaml"),
                    });
                    TrayApp trayApp = null;
                    app.Startup += (s, e) => trayApp = new TrayApp(tray, preReleases, testZip, screenshot);
                    app.Run();
                    GC.KeepAlive(trayApp);
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }
        }

        private static bool HasArgument(string[] args, string name)
        {
            return args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        }

        private static string ArgumentValue(string[] args, string name)
        {
            int index = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        private static bool Acquire(Mutex mutex)
        {
            try
            {
                return mutex.WaitOne(TimeSpan.FromSeconds(15));
            }
            catch (AbandonedMutexException)
            {
                return true; // the old instance exited without releasing it: we own it now
            }
        }
    }
}
