using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace BPGPUpdater
{
    /// <summary>GitHub's anonymous limit is used up and there's no saved answer to fall back on</summary>
    internal sealed class RateLimitException : Exception
    {
        public DateTime ResetAt { get; private set; }

        public RateLimitException(DateTime resetAt)
            : base("GitHub's limit for this connection is used up until " + resetAt.ToString("HH:mm") + ".")
        {
            ResetAt = resetAt;
        }
    }

    /// <summary>What one fetch round ran into, shown in the status line</summary>
    internal sealed class FetchContext
    {
        /// <summary>Set when GitHub's limit was hit and a saved answer was used instead</summary>
        public DateTime? RateLimitedUntil { get; set; }
        /// <summary>Set when a server other than GitHub couldn't be reached and its saved answer was used</summary>
        public bool UsedSavedCopy { get; set; }
    }

    /// <summary>
    /// GitHub API reads with a local cache, so the anonymous limit (60 requests per hour per IP) is hard
    /// to reach. Every answer is saved with its ETag in %APPDATA%\BPGPUpdater\cache. The next request
    /// sends If-None-Match; an unchanged list comes back as 304 Not Modified, which GitHub doesn't count
    /// against the limit. When the limit is hit anyway, the saved answer is used.
    /// The raid schedule (Google Sheets) goes through here too; its saved answer is also used offline.
    /// </summary>
    internal static class ApiCache
    {
        private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BPGPUpdater", "cache");
        private const string GitHubApiHost = "api.github.com";

        public static byte[] Get(string url, FetchContext context)
        {
            return Get(url, context, null);
        }

        /// <param name="contentType">
        /// When set, an answer of another type is refused with InvalidDataException and not saved. Google
        /// answers a sheet that isn't shared with its sign-in page instead of the CSV.
        /// </param>
        public static byte[] Get(string url, FetchContext context, string contentType)
        {
            bool gitHub = string.Equals(new Uri(url).Host, GitHubApiHost, StringComparison.OrdinalIgnoreCase);
            var key = Key(url);
            var bodyFile = Path.Combine(Folder, key + (gitHub ? ".json" : ".dat"));
            var etagFile = Path.Combine(Folder, key + ".etag");
            byte[] cached = TryRead(bodyFile);
            string etag = cached != null ? TryReadText(etagFile) : null;

            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "BPGPUpdater/" + SelfUpdater.CurrentVersionText; // GitHub rejects requests without one
            if (gitHub) request.Accept = "application/vnd.github+json";
            request.Timeout = 60000;
            if (!string.IsNullOrEmpty(etag)) request.Headers[HttpRequestHeader.IfNoneMatch] = etag;

            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var stream = response.GetResponseStream())
                using (var memory = new MemoryStream())
                {
                    if (contentType != null && !(response.ContentType ?? "").StartsWith(contentType, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Expected " + contentType + ", got " + (string.IsNullOrEmpty(response.ContentType) ? "something else" : response.ContentType) + ".");
                    stream.CopyTo(memory);
                    var body = memory.ToArray();
                    Save(bodyFile, etagFile, body, response.Headers[HttpResponseHeader.ETag]);
                    return body;
                }
            }
            catch (WebException e) when (e.Response is HttpWebResponse response)
            {
                // Unchanged since last time: free, and the saved answer is still right
                if (response.StatusCode == HttpStatusCode.NotModified && cached != null) return cached;
                var resetAt = RateLimitReset(response);
                if (resetAt != null)
                {
                    if (cached == null) throw new RateLimitException(resetAt.Value);
                    context.RateLimitedUntil = resetAt;
                    return cached;
                }
                throw;
            }
            catch (WebException) when (!gitHub && cached != null)
            {
                // No answer at all (offline, timeout): the last schedule beats none. GitHub keeps saying it can't be reached.
                context.UsedSavedCopy = true;
                return cached;
            }
        }

        /// <summary>When GitHub refused because the limit is used up: the time it resets, else null</summary>
        private static DateTime? RateLimitReset(HttpWebResponse response)
        {
            if (response.StatusCode != HttpStatusCode.Forbidden && (int)response.StatusCode != 429) return null;
            if (response.Headers["X-RateLimit-Remaining"] != "0") return null;
            long seconds;
            if (!long.TryParse(response.Headers["X-RateLimit-Reset"], out seconds)) return DateTime.Now.AddHours(1);
            return DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime;
        }

        /// <summary>A file name for the URL: the repository and query (other hosts: also the host), without characters Windows refuses</summary>
        private static string Key(string url)
        {
            var uri = new Uri(url);
            var path = string.Equals(uri.Host, GitHubApiHost, StringComparison.OrdinalIgnoreCase)
                ? uri.PathAndQuery.Replace("/repos/", "")
                : uri.Host + uri.PathAndQuery;
            return new string(path.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '.' ? c : '_').ToArray());
        }

        private static void Save(string bodyFile, string etagFile, byte[] body, string etag)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllBytes(bodyFile, body);
                if (string.IsNullOrEmpty(etag)) File.Delete(etagFile);
                else File.WriteAllText(etagFile, etag, Encoding.ASCII);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // Not fatal: the next check just costs a full request
            }
        }

        private static byte[] TryRead(string file)
        {
            try { return File.Exists(file) ? File.ReadAllBytes(file) : null; }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return null; }
        }

        private static string TryReadText(string file)
        {
            try { return File.Exists(file) ? File.ReadAllText(file, Encoding.ASCII).Trim() : null; }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return null; }
        }
    }
}
