// Siemens documentation access, ported from siemens-docs-mcp (MIT, Copyright (c) 2026 Łukasz Czarnacki).
// See THIRD_PARTY_NOTICES.md.
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace TiaOpennessMcp.Docs
{
    public sealed class SearchHit
    {
        public string Title { get; set; } = "";
        public string MapTitle { get; set; } = "";
        public string Version { get; set; } = "";
        public IReadOnlyList<string> Breadcrumb { get; set; } = new List<string>();
        public string Excerpt { get; set; } = "";
        public string Reference { get; set; } = "";
    }

    /// <summary>Pure Markdown rendering of prepared documentation results.</summary>
    public static class DocsRendering
    {
        public static string Search(string query, string host, int total, IReadOnlyList<SearchHit> hits)
        {
            if (hits.Count == 0) return $"No results for \"{query}\" on {host}.";
            var lines = new List<string> { $"Found {total} results for \"{query}\" on {host}. Showing {hits.Count}:" };
            for (int i = 0; i < hits.Count; i++)
            {
                var hit = hits[i];
                string suffix = hit.Version.Length > 0 ? $" ({hit.Version})" : "";
                lines.Add($"{i + 1}. **{hit.Title}** — {hit.MapTitle}{suffix}\n"
                          + $"   {string.Join(" > ", hit.Breadcrumb)}\n"
                          + $"   {hit.Excerpt}\n"
                          + $"   {hit.Reference}");
            }
            return string.Join("\n", lines);
        }

        /// <summary>
        /// Hits within one publication or topic subtree. <paramref name="scan"/> is (checked, publication total) when a
        /// subtree scan stopped before checking every publication hit; total is then only a lower bound and is not shown.
        /// </summary>
        public static string ScopedSearch(string query, MapInfo publication, string? topicTitle, int total,
            IReadOnlyList<SearchHit> hits, (int Checked, int? Total)? scan = null)
        {
            string scope = publication.Version.Length > 0 ? $"{publication.Title} ({publication.Version})" : publication.Title;
            if (topicTitle != null) scope = $"\"{topicTitle}\" — {scope}";
            string checkedText = scan == null ? "" : scan.Value.Checked + (scan.Value.Total == null ? "" : $" of {scan.Value.Total}");
            if (hits.Count == 0)
            {
                if (scan == null) return $"No results for \"{query}\" in {scope}.";
                return $"No results for \"{query}\" in {scope} among the first {checkedText} publication results. "
                       + "Try a narrower query or search the whole publication.";
            }
            string header = scan == null
                ? $"Found {total} results for \"{query}\" in {scope}. Showing {hits.Count}:"
                : $"Showing {hits.Count} results for \"{query}\" in {scope}; checked {checkedText} publication results, more may exist:";
            var lines = new List<string> { header };
            for (int i = 0; i < hits.Count; i++)
            {
                var hit = hits[i];
                lines.Add($"{i + 1}. **{hit.Title}**\n"
                          + $"   {string.Join(" > ", hit.Breadcrumb)}\n"
                          + $"   {hit.Excerpt}\n"
                          + $"   {hit.Reference}");
            }
            return string.Join("\n", lines);
        }

        public static string Publications(string host, IReadOnlyList<(MapInfo Info, string Reference)> rows, int total, int limit)
        {
            if (rows.Count == 0) return $"No publications match these filters on {host}.";
            var lines = rows.Select(r => $"- **{r.Info.Title}** — {r.Info.Product} {r.Info.Version} [{r.Info.Locale}] {r.Reference}").ToList();
            if (total > limit) lines.Add($"Showing {limit} of {total} — narrow your filters.");
            return string.Join("\n", lines);
        }

        public static string Page(MapInfo publication, IEnumerable<string> path, string source, string chunk)
        {
            return $"> Publication: {publication.Title} ({publication.Version}) [{publication.Locale}]\n"
                   + $"> Path: {string.Join(" > ", path)}\n"
                   + $"> Source: {source}\n\n{chunk}";
        }

        public static string Toc(IReadOnlyList<(int Level, string Title, string Reference)> rows, bool truncated, int limit)
        {
            var lines = rows.Select(r => $"{new string(' ', 2 * r.Level)}- {r.Title} — {r.Reference}").ToList();
            if (truncated) lines.Add($"… truncated at {limit} lines — use a deeper reference or a smaller depth.");
            return string.Join("\n", lines);
        }

        public static string Outline(int totalChars, IReadOnlyList<OutlineRow> rows)
        {
            if (rows.Count == 0) return "This page has no content.";
            var lines = new List<string> { $"Outline ({rows.Count} sections, {N(totalChars)} chars):" };
            foreach (var row in rows)
            {
                string code = row.CodeBlocks == 0 ? "" : $", {row.CodeBlocks} code block{(row.CodeBlocks > 1 ? "s" : "")}";
                lines.Add($"{row.Number}. {row.Label} — {N(row.Chars)} chars{code}");
            }
            return string.Join("\n", lines);
        }

        /// <summary>The exact arguments that return the next chunk of the same view (offsets depend on all of them).</summary>
        public static string Continuation(int nextOffset, object? section, string mode, string referenceFormat)
        {
            var args = new List<string>();
            if (section is int n) args.Add($"section={n}");
            else if (section is string s) args.Add($"section={JsonString(s)}");
            if (mode != "full") args.Add($"mode=\"{mode}\"");
            if (referenceFormat != "compact") args.Add($"reference_format=\"{referenceFormat}\"");
            args.Add($"offset={nextOffset}");
            return $"[truncated — call again with {string.Join(", ", args)}]";
        }

        private static string N(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

        private static string JsonString(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
