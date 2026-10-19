using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace BPGPUpdater
{
    /// <summary>Puts release notes (parsed by Markdown) into a TextBlock, in the theme's fonts and colours</summary>
    internal static class MarkdownView
    {
        /// <summary>A release's tag above its notes, when several releases are shown</summary>
        public static void AddTitle(InlineCollection inlines, string title, FrameworkElement theme)
        {
            if (inlines.Count > 0)
            {
                inlines.Add(new LineBreak());
                inlines.Add(new LineBreak());
            }
            inlines.Add(new Run(title)
            {
                FontFamily = (FontFamily)theme.FindResource("TitleFont"),
                FontSize = 17,
                Foreground = (Brush)theme.FindResource("GoldBrush"),
            });
        }

        public static void Add(InlineCollection inlines, string markdown, FrameworkElement theme)
        {
            foreach (var block in Markdown.Parse(markdown))
            {
                if (inlines.Count > 0) inlines.Add(new LineBreak());
                switch (block.Kind)
                {
                    case MarkdownBlockKind.Blank:
                        break; // the line break above leaves an empty line
                    case MarkdownBlockKind.Heading:
                        inlines.Add(new Run(block.PlainText)
                        {
                            FontFamily = (FontFamily)theme.FindResource("TitleFont"),
                            FontSize = block.Level <= 2 ? 15 : 14,
                            Foreground = (Brush)theme.FindResource("PaleGoldBrush"),
                        });
                        break;
                    case MarkdownBlockKind.Bullet:
                        inlines.Add(new Run(new string(' ', block.Level * 4) + block.Marker + "  ") { Foreground = (Brush)theme.FindResource("PaleGoldBrush") });
                        AddSpans(inlines, block, theme);
                        break;
                    default:
                        AddSpans(inlines, block, theme);
                        break;
                }
            }
        }

        private static void AddSpans(InlineCollection inlines, MarkdownBlock block, FrameworkElement theme)
        {
            foreach (var span in block.Spans)
            {
                switch (span.Kind)
                {
                    case MarkdownSpanKind.Bold:
                        inlines.Add(new Run(span.Text) { FontWeight = FontWeights.SemiBold });
                        break;
                    case MarkdownSpanKind.Code:
                        inlines.Add(new Run(span.Text) { FontFamily = new FontFamily("Consolas"), Foreground = (Brush)theme.FindResource("PaleGoldBrush") });
                        break;
                    case MarkdownSpanKind.Link:
                        var url = span.Url;
                        var link = new Hyperlink(new Run(span.Text)) { ToolTip = url };
                        link.Click += (s, e) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                        inlines.Add(link);
                        break;
                    default:
                        inlines.Add(new Run(span.Text));
                        break;
                }
            }
        }
    }
}
