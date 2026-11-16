// Siemens documentation access, ported from siemens-docs-mcp (MIT, Copyright (c) 2026 Łukasz Czarnacki).
// See THIRD_PARTY_NOTICES.md.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace TiaOpennessMcp.Docs
{
    public enum BlockKind { Heading, Paragraph, Code, Table, List, Quote, Other }

    /// <summary>One top-level piece of a page: finished Markdown plus what the reading modes need to know.</summary>
    public sealed class Block
    {
        public Block(BlockKind kind, string text, int level = 0)
        {
            Kind = kind;
            Text = text;
            Level = level;
        }

        public BlockKind Kind { get; }
        public string Text { get; }
        /// <summary>1-6 for headings, 0 otherwise.</summary>
        public int Level { get; }
    }

    /// <summary>
    /// Converts Fluid Topics topic HTML into Markdown blocks. Understands the Siemens patterns: section-title
    /// paragraphs become headings, source-code tables and code paragraphs become fenced code, safety tables
    /// and admonitions become block quotes, other tables become GFM tables.
    /// </summary>
    public static class DocsConverter
    {
        /// <summary>Maps a link href to a replacement, or to null to keep only the link text.</summary>
        public delegate string? LinkRewrite(string href);

        private sealed class Options
        {
            public LinkRewrite? Link;
            public bool Images = true;
        }

        private static readonly HashSet<string> HeadingTags = new HashSet<string> { "h1", "h2", "h3", "h4", "h5", "h6" };
        private static readonly HashSet<string> InlineTags = new HashSet<string> { "a", "b", "strong", "i", "em", "code", "span", "img", "sub", "sup", "u", "br" };
        private static readonly HashSet<string> IgnoredTags = new HashSet<string> { "script", "style", "head", "nav", "button", "form" };
        private static readonly HashSet<string> SafetyLabels = new HashSet<string> { "note", "warning", "caution", "notice", "danger" };
        private static readonly Regex Whitespace = new Regex(@"\s+");

        static DocsConverter()
        {
            // HtmlAgilityPack treats <form> as an empty, overlapping element and hoists its content out;
            // parse it as a normal container like other HTML parsers do (it is then ignored as chrome).
            HtmlNode.ElementsFlags.Remove("form");
        }

        // ------------------------------------------------------------------
        // Parsing and chrome removal
        // ------------------------------------------------------------------

        private static readonly string[] ChromeClassFragments = { "breadcrumb", "feedback", "rating", "prev-next", "pagination" };
        private static readonly HashSet<string> ChromeTags = new HashSet<string>
        {
            "nav", "button", "ft-reader-history-back-action", "ft-reader-action", "ft-slotted-floating-menu"
        };

        /// <summary>Parses topic HTML and removes the navigation chrome Fluid Topics adds around the content.</summary>
        public static HtmlNode? ParseAndClean(string? html)
        {
            if (html == null || html.Trim().Length == 0) return null;
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var root = doc.DocumentNode;
            var doomed = root.Descendants().Where(n => n.NodeType == HtmlNodeType.Element && IsChrome(n)).ToList();
            foreach (var node in doomed)
                if (node.ParentNode != null) node.Remove();
            MergeAdjacentText(root);
            return root;
        }

        private static bool IsChrome(HtmlNode node)
        {
            if (ChromeTags.Contains(node.Name)) return true;
            if (node.GetAttributeValue("role", "") == "navigation") return true;
            string cls = node.GetAttributeValue("class", "");
            return cls.Length > 0 && ChromeClassFragments.Any(f => cls.IndexOf(f, StringComparison.Ordinal) >= 0);
        }

        /// <summary>Joins neighbouring text nodes left apart by removals, so text extraction does not split a sentence.</summary>
        private static void MergeAdjacentText(HtmlNode node)
        {
            var child = node.FirstChild;
            while (child != null)
            {
                var next = child.NextSibling;
                if (child is HtmlTextNode text && next is HtmlTextNode nextText)
                {
                    text.Text += nextText.Text;
                    nextText.Remove();
                    continue;
                }
                if (child.HasChildNodes) MergeAdjacentText(child);
                child = next;
            }
        }

        // ------------------------------------------------------------------
        // Public entry points
        // ------------------------------------------------------------------

        /// <summary>Converts cleaned HTML into Markdown blocks (without the page title).</summary>
        public static List<Block> HtmlToBlocks(HtmlNode? root, LinkRewrite? link = null, bool images = true)
        {
            if (root == null) return new List<Block>();
            var opts = new Options { Link = link, Images = images };
            var blocks = new List<Block>();
            foreach (var child in root.ChildNodes)
                if (child.NodeType == HtmlNodeType.Element) blocks.AddRange(RenderBlock(child, opts));
            var finished = blocks
                .Select(b => new Block(b.Kind, PostProcess(b.Text), b.Level))
                .Where(b => b.Text.Length > 0)
                .ToList();
            if (finished.Count > 0 && finished[0].Text != finished[0].Text.TrimStart())
                finished[0] = new Block(finished[0].Kind, finished[0].Text.TrimStart(), finished[0].Level);
            return finished.Where(b => b.Text.Length > 0).ToList();
        }

        public static List<Block> HtmlToBlocks(string html, LinkRewrite? link = null, bool images = true) =>
            HtmlToBlocks(ParseAndClean(html), link, images);

        /// <summary>A whole Markdown document starting with an H1 title.</summary>
        public static string HtmlToMarkdown(string html, string title, LinkRewrite? link = null, bool images = true)
        {
            if (html.Trim().Length == 0) return $"# {title}\n\n*No content available.*\n";
            string body = string.Join("\n\n", HtmlToBlocks(html, link, images).Select(b => b.Text));
            return $"# {title}\n\n{body}\n";
        }

        // ------------------------------------------------------------------
        // Text helpers
        // ------------------------------------------------------------------

        private static string Decode(string raw) => HtmlEntity.DeEntitize(raw) ?? "";

        private static string CleanText(string text)
        {
            text = WebUtility.HtmlDecode(text).Replace(' ', ' ');
            return Whitespace.Replace(text, " ").Trim();
        }

        private static string NormalizeCode(string code)
        {
            code = WebUtility.HtmlDecode(code).Replace("\r\n", "\n").Replace("\r", "\n").Replace(' ', ' ');
            var lines = code.Split('\n').ToList();
            while (lines.Count > 0 && lines[0].Trim().Length == 0) lines.RemoveAt(0);
            while (lines.Count > 0 && lines[lines.Count - 1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
            return string.Join("\n", lines.Select(l => l.TrimEnd()));
        }

        private static IEnumerable<string> TextNodes(HtmlNode node, bool brAsNewline = false)
        {
            if (node is HtmlTextNode t)
            {
                yield return Decode(t.Text);
                yield break;
            }
            if (brAsNewline && node.Name == "br")
            {
                yield return "\n";
                yield break;
            }
            if (node.NodeType != HtmlNodeType.Element && node.NodeType != HtmlNodeType.Document) yield break;
            foreach (var child in node.ChildNodes)
                foreach (var s in TextNodes(child, brAsNewline))
                    yield return s;
        }

        /// <summary>Text of a code element: like get_text(), but a &lt;br&gt; is a line break (WinCC Unified examples use them).</summary>
        private static string CodeText(HtmlNode node) => string.Concat(TextNodes(node, brAsNewline: true));

        /// <summary>Like BeautifulSoup get_text(separator, strip).</summary>
        private static string GetText(HtmlNode node, string separator = "", bool strip = false)
        {
            var parts = TextNodes(node);
            if (strip) parts = parts.Select(p => p.Trim()).Where(p => p.Length > 0);
            return string.Join(separator, parts);
        }

        private static IEnumerable<HtmlNode> Elements(HtmlNode node) => node.ChildNodes.Where(c => c.NodeType == HtmlNodeType.Element);

        private static HashSet<string> Classes(HtmlNode node) =>
            new HashSet<string>(node.GetAttributeValue("class", "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        private static string Attr(HtmlNode node, string name) => Decode(node.GetAttributeValue(name, ""));

        // ------------------------------------------------------------------
        // Inline rendering
        // ------------------------------------------------------------------

        private static string InlineChildren(HtmlNode node, Options opts)
        {
            var sb = new StringBuilder();
            foreach (var c in node.ChildNodes) sb.Append(InlineToMd(c, opts));
            return sb.ToString();
        }

        private static string InlineToMd(HtmlNode node, Options opts)
        {
            if (node is HtmlTextNode t) return Decode(t.Text);
            if (node.NodeType != HtmlNodeType.Element) return "";
            switch (node.Name)
            {
                case "br":
                    return "\n";
                case "a":
                {
                    string? href = Attr(node, "href");
                    string text = CleanText(InlineChildren(node, opts).Trim());
                    if (href.Length > 0 && opts.Link != null)
                    {
                        href = opts.Link(href);
                        if (href == null) return text; // the hook keeps only the label
                    }
                    if (text.Length == 0 && !opts.Images) return ""; // e.g. an image link whose image was dropped
                    string label = text.Length > 0 ? text : href;
                    return href.Length > 0 ? $"[{label}]({href})" : label;
                }
                case "b":
                case "strong":
                {
                    string inner = CleanText(InlineChildren(node, opts));
                    return inner.Length > 0 ? $"**{inner}**" : "";
                }
                case "i":
                case "em":
                {
                    string inner = CleanText(InlineChildren(node, opts));
                    return inner.Length > 0 ? $"*{inner}*" : "";
                }
                case "code":
                {
                    string inner = CleanText(InlineChildren(node, opts));
                    return inner.Length > 0 ? $"`{inner}`" : "";
                }
                case "img":
                    return RenderImg(node, opts);
                default:
                    return InlineChildren(node, opts);
            }
        }

        private static string RenderImg(HtmlNode tag, Options opts)
        {
            string src = Attr(tag, "src");
            string alt = Attr(tag, "alt");
            // Skip embedded data URIs (text-only output); with images off every image is its alt text.
            if (!opts.Images || src.StartsWith("data:image/", StringComparison.Ordinal)) return alt;
            return src.Length > 0 ? $"![{alt}]({src})" : alt;
        }

        // ------------------------------------------------------------------
        // Block rendering
        // ------------------------------------------------------------------

        private static List<Block> One(BlockKind kind, string text) =>
            text.Length > 0 ? new List<Block> { new Block(kind, text) } : new List<Block>();

        private static List<Block> RenderBlock(HtmlNode tag, Options opts)
        {
            string name = tag.Name;

            if (HeadingTags.Contains(name))
            {
                int level = name[1] - '0';
                string text = CleanText(InlineChildren(tag, opts));
                return text.Length > 0
                    ? new List<Block> { new Block(BlockKind.Heading, new string('#', level) + " " + text, level) }
                    : new List<Block>();
            }

            if (name == "p")
            {
                var cls = Classes(tag);
                string text = NormalizeCode(InlineChildren(tag, opts));
                if (text.Length == 0) return new List<Block>();
                if (cls.Contains("BlocktitleFirst") || cls.Contains("Blocktitle"))
                    return new List<Block> { new Block(BlockKind.Heading, "## " + CleanText(text), 2) };
                if (cls.Contains("p_table_l_code") || cls.Contains("table_sourcecode"))
                    return new List<Block> { new Block(BlockKind.Code, $"```\n{text}\n```") };
                if (cls.Contains("p_table_title")) return new List<Block>();
                return new List<Block> { new Block(BlockKind.Paragraph, CleanText(text)) };
            }

            if (name == "ul" || name == "ol") return One(BlockKind.List, RenderList(tag, 0, opts));

            if (name == "table")
            {
                var classes = Classes(tag);
                if (classes.Contains("table_sourcecode"))
                {
                    // One <p> per code line; joining all text would double-space every line.
                    var codeLines = tag.Descendants("p").Where(p => Classes(p).Contains("p_table_l_code")).Select(CodeText).ToList();
                    string raw = codeLines.Count > 0 ? string.Join("\n", codeLines) : GetText(tag, "\n");
                    string code = NormalizeCode(raw);
                    return code.Length > 0 ? new List<Block> { new Block(BlockKind.Code, $"```\n{code}\n```") } : new List<Block>();
                }
                if (classes.Contains("safety")) return One(BlockKind.Quote, RenderSafetyTable(tag));
                return One(BlockKind.Table, RenderRegularTable(tag, opts));
            }

            if (name == "div")
            {
                if (Classes(tag).Contains("admonition")) return One(BlockKind.Quote, RenderAdmonition(tag, opts));
                return RenderDiv(tag, opts);
            }

            if (name == "pre")
            {
                string code = NormalizeCode(CodeText(tag));
                return code.Length > 0 ? new List<Block> { new Block(BlockKind.Code, $"```\n{code}\n```") } : new List<Block>();
            }

            if (name == "img") return One(BlockKind.Paragraph, RenderImg(tag, opts));

            if (IgnoredTags.Contains(name)) return new List<Block>();

            // Unknown tag: render its element children.
            var pieces = Elements(tag).SelectMany(c => RenderBlock(c, opts)).ToList();
            if (pieces.Count > 0) return pieces;
            // Inline-only unknown tag (dt, dd, span, a, ...): keep its text.
            if (GetText(tag, "", true).Length > 0) return One(BlockKind.Other, RenderFallback(tag, opts));
            return new List<Block>();
        }

        /// <summary>Fallback for text-only tags the renderer has no rule for (definition lists and stray inline tags).</summary>
        private static string RenderFallback(HtmlNode tag, Options opts)
        {
            string text = CleanText(InlineToMd(tag, opts));
            if (tag.Name == "dd" && text.Length > 0) return ":   " + text;
            return text;
        }

        // ------------------------------------------------------------------
        // Lists
        // ------------------------------------------------------------------

        private static void FlushRun(StringBuilder run, List<string> texts)
        {
            string text = CleanText(run.ToString());
            if (text.Length > 0) texts.Add(text);
            run.Clear();
        }

        private static string RenderList(HtmlNode tag, int level, Options opts)
        {
            bool ordered = tag.Name == "ol";
            var lines = new List<string>();
            int index = 1;
            foreach (var li in Elements(tag).Where(e => e.Name == "li"))
            {
                string bullet = ordered ? $"{index}." : "-";
                index++;
                string prefix = new string(' ', 2 * level) + bullet + " ";
                string continuation = new string(' ', 2 * (level + 1));
                var texts = new List<string>();
                var nested = new List<string>();
                var run = new StringBuilder(); // consecutive inline nodes, joined into one paragraph

                foreach (var child in li.ChildNodes)
                {
                    if (child is HtmlTextNode || (child.NodeType == HtmlNodeType.Element && InlineTags.Contains(child.Name)))
                    {
                        run.Append(InlineToMd(child, opts));
                        continue;
                    }
                    if (child.NodeType != HtmlNodeType.Element) continue;
                    FlushRun(run, texts);
                    if (child.Name == "ul" || child.Name == "ol")
                        nested.Add(RenderList(child, level + 1, opts));
                    else if (child.Name == "table" && Classes(child).Contains("safety"))
                        nested.Add(RenderSafetyTable(child));
                    else
                    {
                        string text = CleanText(InlineChildren(child, opts));
                        if (text.Length > 0) texts.Add(text);
                    }
                }
                FlushRun(run, texts);

                string itemText = string.Join("\n\n", texts).Trim();
                if (itemText.Length > 0)
                {
                    var sublines = SplitLines(itemText);
                    lines.Add(prefix + sublines[0]);
                    foreach (var extra in sublines.Skip(1)) lines.Add(continuation + extra);
                }
                else
                {
                    lines.Add(prefix.TrimEnd());
                }
                foreach (var block in nested)
                    if (block.Length > 0) lines.Add(block);
            }
            return string.Join("\n", lines).TrimEnd();
        }

        private static List<string> SplitLines(string text) =>
            text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();

        // ------------------------------------------------------------------
        // Tables
        // ------------------------------------------------------------------

        private static string Capitalize(string s) =>
            s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();

        private static string RenderSafetyTable(HtmlNode table)
        {
            var lines = GetText(table, "\n", true).Split('\n').Select(CleanText).Where(l => l.Length > 0).ToList();
            if (lines.Count == 0) return "";
            string label = Capitalize(lines[0]);
            if (SafetyLabels.Contains(label.ToLowerInvariant()))
            {
                var body = lines.Skip(1).ToList();
                if (body.Count == 0) return $"> **{label}**";
                return $"> **{label}**\n>\n> " + string.Join("\n> ", body);
            }
            return "> " + string.Join("\n> ", lines);
        }

        private static string RenderRegularTable(HtmlNode table, Options opts)
        {
            var rows = new List<List<string>>();
            foreach (var tr in table.Descendants("tr"))
            {
                var cells = Elements(tr).Where(e => e.Name == "th" || e.Name == "td").ToList();
                if (cells.Count == 0) continue;
                rows.Add(cells.Select(c => CleanText(InlineChildren(c, opts))).ToList());
            }
            if (rows.Count == 0) return "";
            int colCount = rows.Max(r => r.Count);
            foreach (var r in rows) while (r.Count < colCount) r.Add("");
            if (rows.Count == 1) return "| " + string.Join(" | ", rows[0]) + " |";
            var lines = new List<string>
            {
                "| " + string.Join(" | ", rows[0]) + " |",
                "| " + string.Join(" | ", Enumerable.Repeat("---", colCount)) + " |"
            };
            lines.AddRange(rows.Skip(1).Select(r => "| " + string.Join(" | ", r) + " |"));
            return string.Join("\n", lines);
        }

        // ------------------------------------------------------------------
        // Divs
        // ------------------------------------------------------------------

        private static string RenderAdmonition(HtmlNode div, Options opts)
        {
            var title = div.Descendants().FirstOrDefault(n => n.NodeType == HtmlNodeType.Element && Classes(n).Contains("admonition-title"));
            string label;
            if (title != null)
            {
                label = CleanText(GetText(title));
            }
            else
            {
                var others = div.GetAttributeValue("class", "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Where(c => c != "admonition").ToList();
                label = others.Count > 0 ? Capitalize(others[0]) : "Note";
            }
            var body = Elements(div).Where(c => c != title).SelectMany(c => RenderBlock(c, opts)).Select(b => b.Text).ToList();
            if (body.Count == 0) return $"> **{label}:**";
            var lines = string.Join("\n\n", body).Split('\n');
            var output = new List<string> { $"> **{label}:** {lines[0]}" };
            output.AddRange(lines.Skip(1).Select(l => ("> " + l).TrimEnd()));
            return string.Join("\n", output);
        }

        private static List<Block> RenderDiv(HtmlNode div, Options opts)
        {
            var result = new List<Block>();
            string? previous = null;
            foreach (var child in Elements(div))
            {
                var blocks = RenderBlock(child, opts);
                if (blocks.Count == 0) continue;
                // Drop adjacent duplicates (wrapper divs sometimes repeat their content).
                string joined = string.Join("\n\n", blocks.Select(b => b.Text));
                if (joined != previous) result.AddRange(blocks);
                previous = joined;
            }
            return result;
        }

        // ------------------------------------------------------------------
        // Post-processing
        // ------------------------------------------------------------------

        /// <summary>Collapses runs of blank lines, strips trailing whitespace and surrounding blank lines.</summary>
        private static string PostProcess(string md)
        {
            var lines = SplitLines(md).Select(l => l.TrimEnd()).ToList();
            if (lines.Count > 0 && lines[lines.Count - 1].Length == 0 && md.EndsWith("\n", StringComparison.Ordinal))
                lines.RemoveAt(lines.Count - 1); // str.splitlines() drops the empty piece after a final newline
            var result = new List<string>();
            int blankRun = 0;
            foreach (var line in lines)
            {
                if (line.Length == 0)
                {
                    blankRun++;
                    if (blankRun <= 1) result.Add(line);
                }
                else
                {
                    blankRun = 0;
                    result.Add(line);
                }
            }
            return string.Join("\n", result).Trim('\n');
        }
    }
}
