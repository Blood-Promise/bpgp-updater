using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace BPGPUpdater
{
    [DataContract]
    internal sealed class ReleaseAsset
    {
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "browser_download_url")] public string DownloadUrl { get; set; }
    }

    [DataContract]
    internal sealed class Release
    {
        [DataMember(Name = "tag_name")] public string Tag { get; set; }
        [DataMember(Name = "body")] public string Body { get; set; }
        [DataMember(Name = "html_url")] public string PageUrl { get; set; }
        [DataMember(Name = "draft")] public bool Draft { get; set; }
        [DataMember(Name = "prerelease")] public bool PreRelease { get; set; }
        [DataMember(Name = "assets")] public List<ReleaseAsset> Assets { get; set; }

        public SemVersion Version
        {
            get { return SemVersion.Parse(Tag); }
        }

        /// <summary>The addon zip the BigWigs packager uploads: BPGP-v1.2.3.zip</summary>
        public ReleaseAsset AddonZip
        {
            get { return FindAsset(a => a.Name.StartsWith("BPGP-", StringComparison.OrdinalIgnoreCase) && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)); }
        }

        /// <summary>This app, attached to a release of the updater's own repository</summary>
        public ReleaseAsset UpdaterExe
        {
            get { return FindAsset(a => string.Equals(a.Name, "BPGPUpdater.exe", StringComparison.OrdinalIgnoreCase)); }
        }

        private ReleaseAsset FindAsset(Func<ReleaseAsset, bool> match)
        {
            return (Assets ?? new List<ReleaseAsset>()).FirstOrDefault(a => a.Name != null && match(a));
        }
    }

    /// <summary>The newest addon release and the newest updater release (either can be null)</summary>
    internal sealed class LatestReleases
    {
        public Release Addon { get; set; }
        public Release Updater { get; set; }
    }

    /// <summary>
    /// Reads releases of two public GitHub repositories: the addon's and this app's. No token needed:
    /// the anonymous limit (60 requests per hour per IP) is far more than a check every few hours.
    /// </summary>
    internal static class GitHubReleases
    {
        /// <summary>Where the addon is released (tags v*, each with a BPGP-v*.zip). Change this when the addon moves.</summary>
        public const string AddonRepository = "jurrebakker/bpgp";
        /// <summary>Where this app is released (tags v*, each with BPGPUpdater.exe)</summary>
        public const string UpdaterRepository = "Blood-Promise/bpgp-updater";

        public const string AddonReleasesPage = "https://github.com/" + AddonRepository + "/releases";
        public const string UpdaterReleasesPage = "https://github.com/" + UpdaterRepository + "/releases";

        static GitHubReleases()
        {
            // .NET Framework doesn't always offer TLS 1.2 by default; GitHub requires it
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        /// <summary>
        /// The newest addon release (with a zip) and the newest updater release (with the exe).
        /// The release list is used instead of GitHub's "latest release", so --prerelease can include betas.
        /// </summary>
        /// <param name="includePreReleases">Test switch (--prerelease): also consider alpha and beta releases</param>
        public static LatestReleases GetLatest(bool includePreReleases)
        {
            return new LatestReleases
            {
                Addon = Newest(AddonRepository, includePreReleases, r => r.AddonZip != null),
                Updater = TryNewest(UpdaterRepository, includePreReleases, r => r.UpdaterExe != null),
            };
        }

        /// <summary>The self-update is optional: if its repository can't be read, addon updates still work</summary>
        private static Release TryNewest(string repository, bool includePreReleases, Func<Release, bool> usable)
        {
            try
            {
                return Newest(repository, includePreReleases, usable);
            }
            catch (Exception e) when (e is WebException || e is IOException || e is SerializationException)
            {
                return null;
            }
        }

        private static Release Newest(string repository, bool includePreReleases, Func<Release, bool> usable)
        {
            List<Release> releases;
            using (var client = CreateClient(true))
            {
                releases = Deserialize<List<Release>>(client.DownloadData("https://api.github.com/repos/" + repository + "/releases?per_page=50"));
            }
            return releases
                .Where(r => !r.Draft && (includePreReleases || !r.PreRelease) && r.Version != null && usable(r))
                .OrderByDescending(r => r.Version)
                .FirstOrDefault();
        }

        /// <summary>
        /// Test switch (--test-zip): a local addon zip posing as the latest release, so install and
        /// update can be tried without publishing anything. Its version comes from BPGP\BPGP.toc.
        /// </summary>
        public static Release FromLocalZip(string path)
        {
            var full = Path.GetFullPath(path);
            string version = null;
            using (var zip = System.IO.Compression.ZipFile.OpenRead(full))
            {
                var toc = zip.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').Equals("BPGP/BPGP.toc", StringComparison.OrdinalIgnoreCase));
                if (toc != null)
                {
                    using (var reader = new StreamReader(toc.Open()))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null && version == null)
                        {
                            if (line.StartsWith("## Version:", StringComparison.OrdinalIgnoreCase)) version = line.Substring(11).Trim();
                        }
                    }
                }
            }
            return new Release
            {
                Tag = version ?? "v0.0.1-test",
                Body = "Local test zip: " + full,
                Assets = new List<ReleaseAsset> { new ReleaseAsset { Name = "BPGP-test.zip", DownloadUrl = new Uri(full).AbsoluteUri } },
            };
        }

        public static void Download(string url, string path)
        {
            using (var client = CreateClient(false))
            {
                client.DownloadFile(url, path);
            }
        }

        private static TimeoutWebClient CreateClient(bool api)
        {
            var client = new TimeoutWebClient { Encoding = Encoding.UTF8 };
            // GitHub rejects requests without a User-Agent
            client.Headers[HttpRequestHeader.UserAgent] = "BPGPUpdater/" + SelfUpdater.CurrentVersionText;
            if (api) client.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            return client;
        }

        private static T Deserialize<T>(byte[] json)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new MemoryStream(json))
            {
                return (T)serializer.ReadObject(stream);
            }
        }

        private sealed class TimeoutWebClient : WebClient
        {
            protected override WebRequest GetWebRequest(Uri address)
            {
                var request = base.GetWebRequest(address);
                if (request != null) request.Timeout = 60000;
                return request;
            }
        }
    }
}
