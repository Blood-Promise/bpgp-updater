using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BPGPUpdater.Tests
{
    [TestClass]
    public class MarkdownTests
    {
        [TestMethod]
        public void ReadsHeadingsBulletsAndParagraphs()
        {
            var blocks = Markdown.Parse("## What's new\r\n\r\n- Bidding window\n  - Tier buttons\n1. First\n\nPlain text");
            CollectionAssert.AreEqual(new[]
            {
                MarkdownBlockKind.Heading, MarkdownBlockKind.Blank, MarkdownBlockKind.Bullet, MarkdownBlockKind.Bullet,
                MarkdownBlockKind.Bullet, MarkdownBlockKind.Blank, MarkdownBlockKind.Paragraph,
            }, blocks.Select(b => b.Kind).ToArray());
            Assert.AreEqual(2, blocks[0].Level);
            Assert.AreEqual("What's new", blocks[0].PlainText);
            Assert.AreEqual("•", blocks[2].Marker);
            Assert.AreEqual(0, blocks[2].Level);
            Assert.AreEqual(1, blocks[3].Level);
            Assert.AreEqual("1.", blocks[4].Marker);
            Assert.AreEqual("Plain text", blocks[6].PlainText);
        }

        [TestMethod]
        public void CollapsesBlankLinesAndSkipsCommentsAndRules()
        {
            var blocks = Markdown.Parse("\n\n<!-- template\nhidden -->\nOne\n\n\n---\n\nTwo\n\n");
            CollectionAssert.AreEqual(new[] { "One", "", "Two" }, blocks.Select(b => b.PlainText).ToArray());
        }

        [TestMethod]
        public void ReadsBoldCodeAndLinks()
        {
            var spans = Markdown.ParseSpans("**Fix** for `/bpgp bid`, see [notes](https://example.com/a) or https://github.com/x/y.");
            CollectionAssert.AreEqual(new[]
            {
                MarkdownSpanKind.Bold, MarkdownSpanKind.Text, MarkdownSpanKind.Code, MarkdownSpanKind.Text,
                MarkdownSpanKind.Link, MarkdownSpanKind.Text, MarkdownSpanKind.Link, MarkdownSpanKind.Text,
            }, spans.Select(s => s.Kind).ToArray());
            Assert.AreEqual("Fix", spans[0].Text);
            Assert.AreEqual("/bpgp bid", spans[2].Text);
            Assert.AreEqual("notes", spans[4].Text);
            Assert.AreEqual("https://example.com/a", spans[4].Url);
            Assert.AreEqual("https://github.com/x/y", spans[6].Url); // without the full stop
            Assert.AreEqual(".", spans[7].Text);
        }

        [TestMethod]
        public void OnlyWebLinksAreClickable()
        {
            var spans = Markdown.ParseSpans("[run me](file:///C:/Windows/notepad.exe) and [mail](mailto:x@y.z)");
            Assert.IsTrue(spans.All(s => s.Kind == MarkdownSpanKind.Text));
            Assert.AreEqual("run me and mail", string.Concat(spans.Select(s => s.Text)));
        }

        [TestMethod]
        public void UnclosedMarkersStayText()
        {
            var spans = Markdown.ParseSpans("2 ** 3 and a ` tick");
            Assert.AreEqual(1, spans.Count);
            Assert.AreEqual("2 ** 3 and a ` tick", spans[0].Text);
        }
    }
}
