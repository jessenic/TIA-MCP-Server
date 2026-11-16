// Siemens documentation access, ported from siemens-docs-mcp (MIT, Copyright (c) 2026 Łukasz Czarnacki).
// See THIRD_PARTY_NOTICES.md.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TiaOpennessMcp.Docs
{
    /// <summary>One page (topic) in a publication's table of contents.</summary>
    public sealed class TocPage
    {
        public TocPage(string tocId, string contentId, string title, string prettyUrl, int depth)
        {
            TocId = tocId;
            ContentId = contentId;
            Title = title;
            PrettyUrl = prettyUrl;
            Depth = depth;
        }

        public string TocId { get; }
        /// <summary>Topic content id, used to fetch the HTML. Empty for the synthetic root of a multi-section TOC.</summary>
        public string ContentId { get; }
        public string Title { get; }
        /// <summary>Site-relative URL path, e.g. /r/en-us/v21/doc-name/chapter/page.</summary>
        public string PrettyUrl { get; }
        public int Depth { get; }
        public List<TocPage> Children { get; } = new List<TocPage>();
    }

    /// <summary>Parses the /api/khub/maps/{id}/pages response and walks the resulting tree.</summary>
    public static class DocsToc
    {
        public static TocPage Parse(JsonElement response)
        {
            if (response.ValueKind != JsonValueKind.Object
                || !response.TryGetProperty("paginatedToc", out var paginated)
                || paginated.ValueKind != JsonValueKind.Array
                || paginated.GetArrayLength() == 0)
            {
                string keys = response.ValueKind == JsonValueKind.Object
                    ? string.Join(", ", response.EnumerateObject().Select(p => p.Name))
                    : response.ValueKind.ToString();
                throw new InvalidOperationException(
                    $"Unexpected /pages response - 'paginatedToc' is missing or empty. Keys present: [{keys}]");
            }
            var nodes = paginated.EnumerateArray().ToList();
            if (nodes.Count > 1)
            {
                // No root topic: each top-level section is its own entry. Add an empty-id root
                // (pretty URL = the sections' common parent) so every section keeps a parent.
                string first = Str(nodes[0], "prettyUrl");
                int slash = first.LastIndexOf('/');
                var root = new TocPage("", "", "", slash >= 0 ? first.Substring(0, slash) : first, 0);
                foreach (var n in nodes) root.Children.Add(ParseNode(n, 1));
                return root;
            }
            return ParseNode(nodes[0], 0);
        }

        public static IEnumerable<TocPage> IterPages(TocPage root)
        {
            var stack = new Stack<TocPage>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var page = stack.Pop();
                yield return page;
                for (int i = page.Children.Count - 1; i >= 0; i--) stack.Push(page.Children[i]);
            }
        }

        /// <summary>Root-to-node path for a content id or toc id (in-page links use /r/{map}/{tocId}); null if absent.</summary>
        public static List<TocPage>? FindPath(TocPage root, string contentId)
        {
            if (contentId == root.ContentId || contentId == root.TocId) return new List<TocPage> { root };
            foreach (var child in root.Children)
            {
                var sub = FindPath(child, contentId);
                if (sub != null)
                {
                    sub.Insert(0, root);
                    return sub;
                }
            }
            return null;
        }

        private static TocPage ParseNode(JsonElement node, int depth)
        {
            var page = new TocPage(Str(node, "tocId"), Str(node, "contentId"), Str(node, "title"), Str(node, "prettyUrl"), depth);
            JsonElement children = default;
            bool has = (node.TryGetProperty("pageToc", out children) && children.ValueKind == JsonValueKind.Array && children.GetArrayLength() > 0)
                       || (node.TryGetProperty("children", out children) && children.ValueKind == JsonValueKind.Array);
            if (has)
                foreach (var child in children.EnumerateArray()) page.Children.Add(ParseNode(child, depth + 1));
            return page;
        }

        internal static string Str(JsonElement obj, string name)
        {
            return obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? ""
                : "";
        }
    }
}
