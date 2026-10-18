using System;
using System.Text.RegularExpressions;

namespace BPGPUpdater
{
    /// <summary>
    /// A release version like v1.2.3 or v1.2.3-beta2, as used in our git tags.
    /// A release ranks above its pre-releases; pre-releases compare by name, then by their number.
    /// </summary>
    internal sealed class SemVersion : IComparable<SemVersion>
    {
        private static readonly Regex Pattern = new Regex(@"^v?(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?(?:\+.*)?$");
        private static readonly Regex PreRelease = new Regex(@"^(.*?)(\d*)$");

        public int Major { get; private set; }
        public int Minor { get; private set; }
        public int Patch { get; private set; }
        public string Pre { get; private set; }

        public bool IsPreRelease { get { return !string.IsNullOrEmpty(Pre); } }

        public static SemVersion Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var match = Pattern.Match(text.Trim());
            if (!match.Success) return null;
            return new SemVersion
            {
                Major = int.Parse(match.Groups[1].Value),
                Minor = int.Parse(match.Groups[2].Value),
                Patch = int.Parse(match.Groups[3].Value),
                Pre = match.Groups[4].Success ? match.Groups[4].Value : null,
            };
        }

        public int CompareTo(SemVersion other)
        {
            if (other == null) return 1;
            int result = Major.CompareTo(other.Major);
            if (result == 0) result = Minor.CompareTo(other.Minor);
            if (result == 0) result = Patch.CompareTo(other.Patch);
            if (result != 0) return result;
            if (!IsPreRelease) return other.IsPreRelease ? 1 : 0;
            if (!other.IsPreRelease) return -1;
            // beta2 < beta10: compare the name part, then the trailing number
            var a = PreRelease.Match(Pre);
            var b = PreRelease.Match(other.Pre);
            result = string.Compare(a.Groups[1].Value, b.Groups[1].Value, StringComparison.OrdinalIgnoreCase);
            if (result != 0) return result;
            return ParseOrZero(a.Groups[2].Value).CompareTo(ParseOrZero(b.Groups[2].Value));
        }

        public static bool IsNewer(SemVersion candidate, SemVersion current)
        {
            return candidate != null && (current == null || candidate.CompareTo(current) > 0);
        }

        private static int ParseOrZero(string digits)
        {
            int value;
            return int.TryParse(digits, out value) ? value : 0;
        }

        public override string ToString()
        {
            return string.Format("{0}.{1}.{2}{3}", Major, Minor, Patch, IsPreRelease ? "-" + Pre : "");
        }
    }
}
