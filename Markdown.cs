using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace BPGPUpdater
{
    internal enum MarkdownBlockKind
    {
        Paragraph,
        Heading,
        Bullet,
        /// <summary>An empty line between paragraphs (several in a row become one)</summary>
        Blank,
    }

    internal enum MarkdownSpanKind
    {
        Text,
        Bold,
        Code,
        /// <summary>Only http and https links; other links become plain text</summary>
        Link,
    }

    internal sealed class MarkdownSpan
    {
        public MarkdownSpanKind Kind { get; set; }
        public string Text { get; set; }
        public string Url { get; set; }
    }

    internal sealed class MarkdownBlock
    {
        public MarkdownBlockKind Kind { get; set; }
        /// <summary>Heading level (1 for #), or indent level of a bullet (0 at the margin)</summary>
        public int Level { get; set; }
        /// <summary>A bullet's marker: • or the number of a numbered list ("1.")</summary>
        public string Marker { get; set; }
        public List<MarkdownSpan> Spans { get; set; }

        public string PlainText
        {
            get { return string.Concat((Spans ?? new List<MarkdownSpan>()).Select(s => s.Text)); }
        }
    }

    /// <summary>
    /// The bit of Markdown that release notes use: headings, bullets, **bold**, `code` and links.
    /// Everything else shows as plain text. MarkdownView puts the result in a TextBlock.
    /// </summary>
    internal static class Markdown
    {
        private static readonly Regex HeadingLine = new Regex(@"^(#{1,6})\s+(.*?)[\s#]*$");
        private static readonly Regex BulletLine = new Regex(@"^(\s*)([-*+]|\d+[.)])\s+(.*)$");
        private static readonly Regex RuleLine = new Regex(@"^\s*([-*_])(\s*\1){2,}\s*$");
        private static readonly Regex Link = new Regex(@"\G\[([^\]]+)\]\(([^)\s]+)\)");
        private static readonly Regex Url = new Regex(@"\Ghttps?://[^\s<>()\[\]]+");

        public static List<MarkdownBlock> Parse(string text)
        {
            var blocks = new List<MarkdownBlock>();
            bool inComment = false;
            foreach (var raw in (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var line = raw.TrimEnd();
                // HTML comments (GitHub's release note templates) aren't shown
                if (inComment)
                {
                    if (line.Contains("-->")) inComment = false;
                    continue;
                }
                if (line.TrimStart().StartsWith("<!--", StringComparison.Ordinal))
                {
                    inComment = !line.Contains("-->");
                    continue;
                }
                if (line.Trim().Length == 0 || RuleLine.IsMatch(line))
                {
                    if (blocks.Count > 0 && blocks[blocks.Count - 1].Kind != MarkdownBlockKind.Blank)
                        blocks.Add(new MarkdownBlock { Kind = MarkdownBlockKind.Blank, Spans = new List<MarkdownSpan>() });
                    continue;
                }
                var heading = HeadingLine.Match(line.TrimStart());
                if (heading.Success)
                {
                    blocks.Add(new MarkdownBlock { Kind = MarkdownBlockKind.Heading, Level = heading.Groups[1].Length, Spans = ParseSpans(heading.Groups[2].Value) });
                    continue;
                }
                var bullet = BulletLine.Match(line);
                if (bullet.Success)
                {
                    var marker = bullet.Groups[2].Value;
                    blocks.Add(new MarkdownBlock
                    {
                        Kind = MarkdownBlockKind.Bullet,
                        Level = bullet.Groups[1].Value.Replace("\t", "    ").Length / 2,
                        Marker = char.IsDigit(marker[0]) ? marker : "•",
                        Spans = ParseSpans(bullet.Groups[3].Value),
                    });
                    continue;
                }
                blocks.Add(new MarkdownBlock { Kind = MarkdownBlockKind.Paragraph, Spans = ParseSpans(line.Trim()) });
            }
            while (blocks.Count > 0 && blocks[blocks.Count - 1].Kind == MarkdownBlockKind.Blank) blocks.RemoveAt(blocks.Count - 1);
            return blocks;
        }

        public static List<MarkdownSpan> ParseSpans(string text)
        {
            var spans = new List<MarkdownSpan>();
            var plain = new StringBuilder();
            Action<MarkdownSpanKind, string, string> add = (kind, value, url) =>
            {
                if (plain.Length > 0) spans.Add(new MarkdownSpan { Kind = MarkdownSpanKind.Text, Text = plain.ToString() });
                plain.Clear();
                spans.Add(new MarkdownSpan { Kind = kind, Text = value, Url = url });
            };

            int i = 0;
            while (i < text.Length)
            {
                if (string.CompareOrdinal(text, i, "**", 0, 2) == 0)
                {
                    int end = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                    if (end > i + 2)
                    {
                        add(MarkdownSpanKind.Bold, text.Substring(i + 2, end - i - 2), null);
                        i = end + 2;
                        continue;
                    }
                }
                else if (text[i] == '`')
                {
                    int end = text.IndexOf('`', i + 1);
                    if (end > i + 1)
                    {
                        add(MarkdownSpanKind.Code, text.Substring(i + 1, end - i - 1), null);
                        i = end + 1;
                        continue;
                    }
                }
                else if (text[i] == '[')
                {
                    var link = Link.Match(text, i);
                    if (link.Success)
                    {
                        var url = link.Groups[2].Value;
                        if (IsWebUrl(url)) add(MarkdownSpanKind.Link, link.Groups[1].Value, url);
                        else plain.Append(link.Groups[1].Value);
                        i += link.Length;
                        continue;
                    }
                }
                else if (text[i] == 'h' && (i == 0 || !char.IsLetterOrDigit(text[i - 1])))
                {
                    var url = Url.Match(text, i);
                    if (url.Success)
                    {
                        // A sentence's full stop isn't part of the link
                        var value = url.Value.TrimEnd('.', ',', ';', ':', '!', '?');
                        add(MarkdownSpanKind.Link, value, value);
                        i += value.Length;
                        continue;
                    }
                }
                plain.Append(text[i]);
                i++;
            }
            if (plain.Length > 0) spans.Add(new MarkdownSpan { Kind = MarkdownSpanKind.Text, Text = plain.ToString() });
            return spans;
        }

        private static bool IsWebUrl(string url)
        {
            Uri uri;
            return Uri.TryCreate(url, UriKind.Absolute, out uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }
    }
}
