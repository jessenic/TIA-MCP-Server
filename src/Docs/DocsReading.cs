// Siemens documentation access, ported from siemens-docs-mcp (MIT, Copyright (c) 2026 Łukasz Czarnacki).
// See THIRD_PARTY_NOTICES.md.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TiaOpennessMcp.Docs
{
    public sealed class OutlineRow
    {
        public OutlineRow(int number, string label, int chars, int codeBlocks)
        {
            Number = number;
            Label = label;
            Chars = chars;
            CodeBlocks = codeBlocks;
        }

        public int Number { get; }
        /// <summary>Heading text with # markers, "(before first heading)" or "(whole page)".</summary>
        public string Label { get; }
        public int Chars { get; }
        public int CodeBlocks { get; }
    }

    public sealed class Chunk
    {
        public Chunk(string text, int? nextOffset)
        {
            Text = text;
            NextOffset = nextOffset;
        }

        /// <summary>Ends with "\n" only when it reaches the end of the view.</summary>
        public string Text { get; }
        /// <summary>View position of the first character not returned; null when done.</summary>
        public int? NextOffset { get; }
    }

    /// <summary>
    /// Pure reading views over converter blocks: sections, outline rows, code-only views and chunking.
    /// Sections are numbered from 1 in heading order; 0 is the lead before the first heading.
    /// </summary>
    public static class DocsReading
    {
        public static readonly string[] ReadModes = { "full", "outline", "code" };

        private static readonly Regex LinkPattern = new Regex(@"!?\[([^\]]*)\]\([^)]*\)");
        private static readonly Regex MarkupPattern = new Regex(@"\*\*|\*|`");
        private const string Fence = "```";

        /// <summary>The text a view's blocks make: one blank line between blocks and a final newline ("" for none).</summary>
        public static string ViewText(IReadOnlyList<Block> blocks) =>
            blocks.Count == 0 ? "" : string.Join("\n\n", blocks.Select(b => b.Text)) + "\n";

        /// <summary>(number, start, end) block-index ranges: the lead, then one per heading including nested ones.</summary>
        private static List<(int Number, int Start, int End)> Spans(IReadOnlyList<Block> blocks)
        {
            var spans = new List<(int, int, int)>();
            int first = blocks.Count;
            for (int i = 0; i < blocks.Count; i++)
                if (blocks[i].Kind == BlockKind.Heading) { first = i; break; }
            if (first > 0) spans.Add((0, 0, first));
            int number = 0;
            for (int i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                if (block.Kind != BlockKind.Heading) continue;
                number++;
                // A section ends at the next heading of the same or a higher level.
                int end = blocks.Count;
                for (int j = i + 1; j < blocks.Count; j++)
                    if (blocks[j].Kind == BlockKind.Heading && blocks[j].Level <= block.Level) { end = j; break; }
                spans.Add((number, i, end));
            }
            return spans;
        }

        /// <summary>A heading's plain text for matching: no # markers, link syntax or emphasis; whitespace collapsed.</summary>
        public static string HeadingMatchText(Block block)
        {
            string text = block.Text.TrimStart('#');
            text = LinkPattern.Replace(text, "$1");
            text = MarkupPattern.Replace(text, "");
            return DocsText.CollapseWhitespace(text);
        }

        public static List<OutlineRow> OutlineRows(IReadOnlyList<Block> blocks)
        {
            bool anyHeading = blocks.Any(b => b.Kind == BlockKind.Heading);
            return Spans(blocks).Select(s =>
            {
                var sectionBlocks = blocks.Skip(s.Start).Take(s.End - s.Start).ToList();
                var heading = blocks[s.Start].Kind == BlockKind.Heading ? blocks[s.Start] : null;
                string label = heading != null ? heading.Text : anyHeading ? "(before first heading)" : "(whole page)";
                return new OutlineRow(s.Number, label, ViewText(sectionBlocks).Length, sectionBlocks.Count(b => b.Kind == BlockKind.Code));
            }).ToList();
        }

        private static string HeadingsList(IReadOnlyList<Block> blocks) =>
            string.Join("\n", OutlineRows(blocks).Select(r => $"{r.Number}. {r.Label}"));

        /// <summary>
        /// Blocks of the sections chosen by outline number or heading text. Text matches a heading equal to it, or
        /// starting with it followed by ":" (case-insensitive); all matches are returned in document order, and a
        /// match nested inside another is returned once, as part of the outer one.
        /// </summary>
        public static List<Block> SelectSections(IReadOnlyList<Block> blocks, object section)
        {
            var spans = Spans(blocks);
            List<(int Number, int Start, int End)> chosen;
            if (section is int number)
            {
                chosen = spans.Where(s => s.Number == number).ToList();
                if (chosen.Count == 0)
                    throw new ArgumentException($"No section number {number} on this page. Sections:\n{HeadingsList(blocks)}");
            }
            else
            {
                string text = (string)section;
                if (!blocks.Any(b => b.Kind == BlockKind.Heading))
                    throw new ArgumentException("This page has no headings; read it without section.");
                string query = DocsText.CollapseWhitespace(text).ToLowerInvariant();
                chosen = new List<(int, int, int)>();
                foreach (var span in spans)
                {
                    var heading = blocks[span.Start];
                    if (heading.Kind != BlockKind.Heading) continue;
                    string h = HeadingMatchText(heading).ToLowerInvariant();
                    if (h != query && !h.StartsWith(query + ":", StringComparison.Ordinal)) continue;
                    if (chosen.Count > 0 && span.Start < chosen[chosen.Count - 1].End) continue; // nested in the previous match
                    chosen.Add(span);
                }
                if (chosen.Count == 0) throw new ArgumentException(NoMatchMessage(blocks, spans, text));
            }
            return chosen.SelectMany(s => blocks.Skip(s.Start).Take(s.End - s.Start)).ToList();
        }

        private static string NoMatchMessage(IReadOnlyList<Block> blocks, List<(int Number, int Start, int End)> spans, string section)
        {
            var labels = new Dictionary<string, string>();
            foreach (var s in spans)
                if (blocks[s.Start].Kind == BlockKind.Heading)
                    labels[HeadingMatchText(blocks[s.Start]).ToLowerInvariant()] = $"{s.Number}. {blocks[s.Start].Text}";
            var close = DocsText.CloseMatches(section.ToLowerInvariant(), labels.Keys, 3);
            string hint = close.Count > 0 ? $" Close matches: {string.Join("; ", close.Select(c => labels[c]))}." : "";
            return $"No heading matches section '{section}'.{hint}\nSections:\n{HeadingsList(blocks)}";
        }

        /// <summary>Code blocks, each run preceded by its nearest heading (none for code before the first heading).</summary>
        public static List<Block> CodeView(IReadOnlyList<Block> blocks)
        {
            var output = new List<Block>();
            Block? heading = null;
            Block? emitted = null;
            foreach (var block in blocks)
            {
                if (block.Kind == BlockKind.Heading)
                {
                    heading = block;
                }
                else if (block.Kind == BlockKind.Code)
                {
                    if (heading != null && !ReferenceEquals(heading, emitted))
                    {
                        output.Add(heading);
                        emitted = heading;
                    }
                    output.Add(block);
                }
            }
            return output;
        }

        // --- chunking ---------------------------------------------------------

        private static List<int> LineStarts(string text)
        {
            var starts = new List<int> { 0 };
            for (int i = 0; i < text.Length; i++)
                if (text[i] == '\n') starts.Add(i + 1);
            return starts;
        }

        /// <summary>
        /// (rel, prefix, suffix, line starts, isCode) for a piece of a block starting at rel. A code piece is fenced on
        /// both ends; a table piece starting after the header repeats the header when the header and one row fit.
        /// </summary>
        private static (int Rel, string Prefix, string Suffix, List<int> Starts, bool IsCode) Frame(Block block, int rel, int budget)
        {
            string text = block.Text;
            var starts = LineStarts(text);
            bool isCode = block.Kind == BlockKind.Code && text.StartsWith(Fence, StringComparison.Ordinal) && starts.Count > 2;
            string prefix = "";
            if (isCode)
            {
                prefix = text.Substring(0, starts[1]);
                rel = Math.Max(rel, starts[1]);
            }
            else if (block.Kind == BlockKind.Table && starts.Count > 3 && rel >= starts[2])
            {
                int nextLineEnd = text.IndexOf('\n', Math.Min(rel, text.Length));
                string row = DocsText.Slice(text, rel, nextLineEnd == -1 ? text.Length : nextLineEnd);
                if (starts[2] + row.Length <= budget) prefix = text.Substring(0, starts[2]);
            }
            return (rel, prefix, isCode ? "\n" + Fence : "", starts, isCode);
        }

        private static (string Piece, int Next)? SplitAtLines(Block block, int rel, int budget, int finalExtra)
        {
            string text = block.Text;
            var f = Frame(block, rel, budget);
            string remainder = DocsText.Slice(text, f.Rel);
            if (f.Prefix.Length + remainder.Length + finalExtra <= budget) return (f.Prefix + remainder, text.Length);
            int room = budget - f.Prefix.Length - f.Suffix.Length;
            // A code piece must leave more than the closing fence for the next piece.
            var fitting = f.Starts
                .Where(b => b > f.Rel && !(f.IsCode && b == f.Starts[f.Starts.Count - 1]))
                .Where(b => (b - 1) - f.Rel <= room)
                .ToList();
            if (fitting.Count == 0) return null;
            int cut = fitting[fitting.Count - 1];
            return (f.Prefix + DocsText.Slice(text, f.Rel, cut - 1) + f.Suffix, cut);
        }

        /// <summary>Last resort for a line longer than any chunk: cut at its last space, else at the budget.</summary>
        private static (string Piece, int Next)? SplitInLine(Block block, int rel, int budget, int finalExtra)
        {
            string text = block.Text;
            var f = Frame(block, rel, budget);
            int room = budget - f.Prefix.Length - f.Suffix.Length;
            if (room <= 0) return null;
            int lineEnd = text.IndexOf('\n', Math.Min(f.Rel, text.Length));
            if (lineEnd == -1) lineEnd = text.Length;
            int limit = Math.Min(f.Rel + room, lineEnd);
            if (limit == text.Length && finalExtra > 0) limit -= 1; // leave room for the view's final newline
            if (f.IsCode && limit == lineEnd && lineEnd + 1 == f.Starts[f.Starts.Count - 1]) limit -= 1; // never leave only the closing fence
            if (limit <= f.Rel) return null;
            int space = DocsText.RFind(text, ' ', f.Rel + 1, limit + 1);
            int cut = space != -1 ? space : limit;
            return (f.Prefix + DocsText.Slice(text, f.Rel, cut) + f.Suffix, space != -1 ? cut + 1 : cut);
        }

        /// <summary>
        /// The largest valid piece of a block from rel within budget, or null meaning "start the next chunk here".
        /// Only a line that no chunk could hold is cut inside the line.
        /// </summary>
        private static (string Piece, int Next)? Split(Block block, int rel, int budget, int finalExtra, int maxChars)
        {
            var piece = SplitAtLines(block, rel, budget, finalExtra);
            if (piece != null) return piece;
            if (budget < maxChars && SplitAtLines(block, rel, maxChars, finalExtra) != null) return null;
            return SplitInLine(block, rel, budget, finalExtra);
        }

        /// <summary>Where to start inside a block that rel points into.</summary>
        private static int ResumePoint(Block block, int rel, int maxChars, int finalExtra)
        {
            string text = block.Text;
            if (text.Length + finalExtra <= maxChars) return 0;
            int lineStart = DocsText.RFind(text, '\n', 0, rel) + 1;
            return SplitAtLines(block, lineStart, maxChars, finalExtra) != null ? lineStart : rel;
        }

        /// <summary>
        /// One chunk of the view from offset, at most maxChars, cut between blocks. A block larger than maxChars is
        /// split at a line (or table row); a single line longer than that is split at its last space.
        /// </summary>
        public static Chunk ChunkView(IReadOnlyList<Block> blocks, int offset, int maxChars)
        {
            int total = ViewText(blocks).Length;
            if (total > 0 && offset >= total)
                throw new ArgumentException($"offset {offset} is past the end of this page (length {total}).");
            if (blocks.Count == 0) return new Chunk("", null);
            var starts = new List<int>();
            int pos = 0;
            foreach (var block in blocks)
            {
                starts.Add(pos);
                pos += block.Text.Length + 2;
            }

            // Locate the start block; an offset in a separator moves to the next block, the final "\n" to the last.
            int index = blocks.Count - 1;
            for (int i = 0; i < blocks.Count; i++)
            {
                if (offset < starts[i] + blocks[i].Text.Length + (i == blocks.Count - 1 ? 1 : 2))
                {
                    index = i;
                    break;
                }
            }
            int rel = Math.Max(0, offset - starts[index]);
            if (rel >= blocks[index].Text.Length)
            {
                if (index + 1 < blocks.Count)
                {
                    index++;
                }
                rel = 0;
            }
            else
            {
                rel = ResumePoint(blocks[index], rel, maxChars, index == blocks.Count - 1 ? 1 : 0);
            }

            var parts = new List<string>();
            int used = 0;
            while (index < blocks.Count)
            {
                var block = blocks[index];
                int sep = parts.Count > 0 ? 2 : 0;
                int finalExtra = index == blocks.Count - 1 ? 1 : 0;
                if (rel == 0 && used + sep + block.Text.Length + finalExtra <= maxChars)
                {
                    parts.Add(block.Text);
                    used += sep + block.Text.Length;
                    index++;
                    continue;
                }
                if (rel == 0 && block.Text.Length + finalExtra <= maxChars) break; // fits in a chunk of its own
                var piece = Split(block, rel, maxChars - used - sep, finalExtra, maxChars);
                if (piece == null) break;
                parts.Add(piece.Value.Piece);
                rel = piece.Value.Next;
                used += sep + piece.Value.Piece.Length;
                if (rel < block.Text.Length) break;
                index++;
                rel = 0;
            }

            if (parts.Count == 0)
                throw new ArgumentException($"max_chars {maxChars} is too small to make progress at offset {offset}.");
            if (index >= blocks.Count) return new Chunk(string.Join("\n\n", parts) + "\n", null);
            return new Chunk(string.Join("\n\n", parts), starts[index] + rel);
        }
    }
}
