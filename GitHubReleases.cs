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

        /// <summary>Updater releases are tagged updater-v1.2.3, separate from the addon's v1.2.3</summary>
        public const string UpdaterTagPrefix = "updater-";

        public bool IsUpdater
        {
            get { return Tag != null && Tag.StartsWith(UpdaterTagPrefix, StringComparison.OrdinalIgnoreCase); }
        }

        public SemVersion Version
        {
            get { return SemVersion.Parse(IsUpdater ? Tag.Substring(UpdaterTagPrefix.Length) : Tag); }
        }

        /// <summary>The addon zip the BigWigs packager uploads: BPGP-v1.2.3.zip</summary>
        public ReleaseAsset AddonZip
        {
            get { return FindAsset(a => a.Name.StartsWith("BPGP-", StringComparison.OrdinalIgnoreCase) && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)); }
        }

        /// <summary>This app, attached to its own updater-v* release</summary>
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
    /// Reads releases of the public GitHub repository. No token needed: the anonymous limit
    /// (60 requests per hour per IP) is far more than a check every few hours.
    /// </summary>
    internal static class GitHubReleases
    {
        public const string Repository = "jurrebakker/bpgp";
        public const string ReleasesPage = "https://github.com/" + Repository + "/releases";
        private const string Api = "https://api.github.com/repos/" + Repository;

        static GitHubReleases()
        {
            // .NET Framework doesn't always offer TLS 1.2 by default; GitHub requires it
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        /// <summary>
        /// The newest addon release (tag v*) and updater release (tag updater-v*), from one request.
        /// GitHub's own "latest release" can't be used: it doesn't know the two kinds apart.
        /// </summary>
        /// <param name="includePreReleases">Test switch (--prerelease): also consider alpha and beta releases</param>
        public static LatestReleases GetLatest(bool includePreReleases)
        {
            List<Release> releases;
            using (var client = CreateClient(true))
            {
                releases = Deserialize<List<Release>>(client.DownloadData(Api + "/releases?per_page=50"));
            }
            var usable = releases.Where(r => !r.Draft && (includePreReleases || !r.PreRelease) && r.Version != null).ToList();
            return new LatestReleases
            {
                Addon = usable.Where(r => !r.IsUpdater && r.AddonZip != null).OrderByDescending(r => r.Version).FirstOrDefault(),
                Updater = usable.Where(r => r.IsUpdater && r.UpdaterExe != null).OrderByDescending(r => r.Version).FirstOrDefault(),
            };
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
