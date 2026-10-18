using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace BPGPUpdater
{
    /// <summary>
    /// Replaces this exe with the BPGPUpdater.exe of a newer release in the updater's own repository
    /// (GitHubReleases.UpdaterRepository). Windows lets a running exe be
    /// renamed, so: download .new, rename the running exe to .old, move .new in place, restart.
    /// The .old file is removed on the next start.
    /// </summary>
    internal static class SelfUpdater
    {
        public static string ExePath
        {
            get { return Assembly.GetExecutingAssembly().Location; }
        }

        public static string CurrentVersionText
        {
            get
            {
                var attribute = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                return attribute != null ? attribute.InformationalVersion : "0.0.0-dev";
            }
        }

        public static SemVersion CurrentVersion
        {
            get { return SemVersion.Parse(CurrentVersionText); }
        }

        /// <summary>A local build (0.0.0-dev) never replaces itself</summary>
        public static bool IsDevBuild
        {
            get { var v = CurrentVersion; return v == null || (v.Major == 0 && v.Minor == 0 && v.Patch == 0); }
        }

        public static bool HasUpdate(Release release)
        {
            return release != null && release.UpdaterExe != null && !IsDevBuild && SemVersion.IsNewer(release.Version, CurrentVersion);
        }

        public static void CleanUp()
        {
            try
            {
                if (File.Exists(ExePath + ".old")) File.Delete(ExePath + ".old");
                if (File.Exists(ExePath + ".new")) File.Delete(ExePath + ".new");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
            }
        }

        /// <summary>Downloads and swaps in the new exe, then starts it. The caller exits right after.</summary>
        public static void Apply(Release release, bool startInTray)
        {
            var exe = ExePath;
            var newExe = exe + ".new";
            var oldExe = exe + ".old";
            GitHubReleases.Download(release.UpdaterExe.DownloadUrl, newExe);
            if (File.Exists(oldExe)) File.Delete(oldExe);
            File.Move(exe, oldExe);
            try
            {
                File.Move(newExe, exe);
            }
            catch
            {
                File.Move(oldExe, exe);
                throw;
            }
            var arguments = Program.UpdatedArgument + (startInTray ? " " + Program.TrayArgument : "");
            Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = false });
        }
    }
}
