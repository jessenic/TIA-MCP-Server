// Siemens documentation access, ported from siemens-docs-mcp (MIT, Copyright (c) 2026 Łukasz Czarnacki).
// See THIRD_PARTY_NOTICES.md.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using HtmlAgilityPack;
using TiaOpennessMcp.Config;

namespace TiaOpennessMcp.Docs
{
    /// <summary>
    /// The four documentation tools (search, read page, table of contents, list publications) over a
    /// <see cref="DocsCatalog"/>. Each returns Markdown text for the agent.
    /// </summary>
    public sealed class DocsTools
    {
        public const int MaxLimit = 50;
        public const int MaxPublications = 50;
        public const int MinChars = 1000;
        public const int MaxChars = 100_000;
        public const int MaxDepth = 6;
        public const int MaxTocLines = 500;
        // Scoped search: the metadata key holding a hit's map id.
        public const string PublicationFilterKey = "ft:publicationId";
        // Subtree scope filters publication hits locally: read at most this many pages of this size.
        private const int ScopePageSize = 100;
        private const int MaxScopePages = 3;

        private static readonly object InstanceLock = new object();
        private static DocsTools? _instance;
        private static DocsSettings _settings = new DocsSettings();

        public DocsTools(DocsCatalog catalog, string defaultHost, string defaultLocale)
        {
            Catalog = catalog;
            DefaultHost = defaultHost;
            DefaultLocale = defaultLocale;
        }

        public DocsCatalog Catalog { get; }
        public string DefaultHost { get; }
        public string DefaultLocale { get; }

        /// <summary>Sets the settings the shared instance is built from; call before the first tool use.</summary>
        public static void Configure(DocsSettings settings)
        {
            lock (InstanceLock)
            {
                _settings = settings ?? new DocsSettings();
                _instance = null;
            }
        }

        /// <summary>Shared instance; its catalog caches publications and TOCs for the life of the process.</summary>
        public static DocsTools Instance
        {
            get
            {
                lock (InstanceLock)
                {
                    if (_instance == null)
                    {
                        var s = _settings.WithEnvironment(Environment.GetEnvironmentVariable);
                        var hosts = s.AllHosts();
                        double interval = s.MinIntervalSeconds;
                        var catalog = new DocsCatalog(hosts, h => new FluidTopicsClient(h, interval));
                        _instance = new DocsTools(catalog, hosts[0], s.Locale);
                    }
                    return _instance;
                }
            }
        }

        public static string HostsDescription(DocsSettings? settings = null)
        {
            var hosts = (settings ?? _settings).AllHosts();
            return $"Allowed hosts: {string.Join(", ", hosts)} (default: {hosts[0]}).";
        }

        private static string ValidateReferenceFormat(string? value)
        {
            value = string.IsNullOrEmpty(value) ? "compact" : value;
            if (value == "compact" || value == "url") return value!;
            throw new ArgumentException($"reference_format must be 'compact' or 'url', got '{value}'");
        }

        private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

        // ------------------------------------------------------------------
        // Search
        // ------------------------------------------------------------------

        public string Search(string query, string? host = null, string? product = null, string? version = null,
            string? locale = null, int limit = 10, string? referenceFormat = "compact", string? scope = null)
        {
            if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("query is required.");
            string format = ValidateReferenceFormat(referenceFormat);
            if (limit < 1 || limit > MaxLimit) throw new ArgumentException($"limit must be 1-{MaxLimit}, got {limit}");
            host = Blank(host);
            locale = Blank(locale);
            product = Blank(product);
            version = Blank(version);
            if (Blank(scope) != null)
                return ScopedSearch(query, scope!.Trim(), host, locale, product, version, limit, format);

            host ??= DefaultHost;
            locale ??= DefaultLocale;
            var client = Catalog.Client(host);
            locale = Catalog.Canonical(host, "locale", locale);
            var filters = new List<SearchFilter>();
            if (product != null) filters.Add(new SearchFilter("Product", Catalog.Canonical(host, "product", product)));
            if (version != null)
            {
                var (key, value) = Catalog.CanonicalVersion(host, version);
                filters.Add(new SearchFilter(key, value));
            }
            var data = client.Search(query, locale, filters, 1, limit);
            var topics = new List<JsonElement>();
            foreach (var cluster in Array(data, "results"))
            {
                var topic = Array(cluster, "entries")
                    .Where(e => DocsToc.Str(e, "type") == "TOPIC" && e.TryGetProperty("topic", out var t) && t.ValueKind == JsonValueKind.Object)
                    .Select(e => (JsonElement?)e.GetProperty("topic"))
                    .FirstOrDefault();
                if (topic.HasValue) topics.Add(topic.Value);
            }
            if (topics.Count == 0) return DocsRendering.Search(query, Catalog.CheckHost(host), 0, new List<SearchHit>());
            int total = IntProp(Obj(data, "paging"), "totalResultsCount") ?? topics.Count;
            var hits = topics.Select(t => NormalizeHit(t, SearchReference(Catalog.CheckHost(host), t, format))).ToList();
            return DocsRendering.Search(query, Catalog.CheckHost(host), total, hits);
        }

        private string ScopedSearch(string query, string scope, string? host, string? locale, string? product, string? version, int limit, string format)
        {
            if (product != null || version != null)
                throw new ArgumentException("scope already selects one publication; omit product and version.");
            var target = Catalog.Resolve(scope);
            if (host != null && host.ToLowerInvariant() != target.Host)
                throw new ArgumentException($"host '{host}' does not match scope '{scope}', which is on {target.Host}; omit host or make them match.");
            var info = Catalog.Maps(target.Host)[target.MapId];
            if (locale != null && info.Locale.Length > 0 && !string.Equals(locale, info.Locale, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"locale '{locale}' does not match the scoped publication's locale '{info.Locale}'; omit locale.");
            if (info.Locale.Length == 0 && locale == null)
                throw new ArgumentException("The scoped publication has no locale metadata; pass locale.");
            string effectiveLocale = info.Locale.Length > 0 ? info.Locale : Catalog.Canonical(target.Host, "locale", locale!);
            TocPage? node = null;
            if (target.ContentId != null)
            {
                var path = TopicPath(target);
                if (path.Count > 1) node = path[path.Count - 1]; // the root topic stands for the whole publication
            }
            var client = Catalog.Client(target.Host);
            List<JsonElement> topics;
            int total;
            (int, int?)? scan = null;
            if (node == null)
            {
                var filters = new List<SearchFilter> { new SearchFilter(PublicationFilterKey, target.MapId) };
                var data = client.Search(query, effectiveLocale, filters, 1, limit);
                topics = ScopedTopics(data, target);
                total = ScopedTotal(Obj(data, "paging")) ?? topics.Count;
            }
            else
            {
                (topics, total, scan) = ScanSubtree(client, query, effectiveLocale, target, node, limit);
            }
            var hits = topics.Select(t => NormalizeHit(t, SearchReference(target.Host, t, format))).ToList();
            return DocsRendering.ScopedSearch(query, info, node?.Title, total, hits, scan);
        }

        private static int? ScopedTotal(JsonElement paging)
        {
            // Each cluster yields one scoped hit, so the cluster count is preferred over the result count.
            return IntProp(paging, "totalClustersCount") ?? IntProp(paging, "totalResultsCount");
        }

        /// <summary>
        /// One topic per cluster from the scoped publication. The API silently ignores filters it does not
        /// understand, so a cluster naming only other publications means the scope was not applied: fail.
        /// </summary>
        private static List<JsonElement> ScopedTopics(JsonElement data, Resolved target)
        {
            var topics = new List<JsonElement>();
            foreach (var cluster in Array(data, "results"))
            {
                var candidates = Array(cluster, "entries")
                    .Where(e => DocsToc.Str(e, "type") == "TOPIC" && e.TryGetProperty("topic", out var t) && t.ValueKind == JsonValueKind.Object)
                    .Select(e => e.GetProperty("topic"))
                    .ToList();
                var match = candidates.Where(t => DocsToc.Str(t, "mapId") == target.MapId).Select(t => (JsonElement?)t).FirstOrDefault();
                if (match.HasValue)
                {
                    topics.Add(match.Value);
                    continue;
                }
                string foreign = candidates.Select(t => DocsToc.Str(t, "mapId")).FirstOrDefault(m => m.Length > 0) ?? "";
                if (foreign.Length > 0)
                    throw new FluidTopicsException(null,
                        $"{target.Host} did not apply the publication filter (returned a hit from publication '{foreign}'); refusing to return results outside the scope.");
            }
            return topics;
        }

        private static (List<JsonElement>, int, (int, int?)?) ScanSubtree(IFluidTopicsClient client, string query, string locale,
            Resolved target, TocPage node, int limit)
        {
            var inside = new HashSet<string>(DocsToc.IterPages(node).Select(p => p.ContentId));
            var filters = new List<SearchFilter> { new SearchFilter(PublicationFilterKey, target.MapId) };
            var kept = new List<JsonElement>();
            int checkedCount = 0;
            int? publicationTotal = null;
            for (int page = 1; page <= MaxScopePages; page++)
            {
                var data = client.Search(query, locale, filters, page, ScopePageSize);
                var results = Array(data, "results").ToList();
                var paging = Obj(data, "paging");
                checkedCount += results.Count;
                publicationTotal = ScopedTotal(paging);
                kept.AddRange(ScopedTopics(data, target).Where(t => inside.Contains(DocsToc.Str(t, "contentId"))));
                bool isLast = paging.ValueKind == JsonValueKind.Object && paging.TryGetProperty("isLastPage", out var last) && last.ValueKind == JsonValueKind.True;
                // Exact only on evidence: the last-page flag, a reached total, or an empty page with no total.
                if (isLast || (publicationTotal != null && checkedCount >= publicationTotal) || (results.Count == 0 && publicationTotal == null))
                    return (kept.Take(limit).ToList(), kept.Count, null);
                if (results.Count == 0 || kept.Count >= limit) break;
            }
            return (kept.Take(limit).ToList(), kept.Count, (checkedCount, publicationTotal));
        }

        private string SearchReference(string host, JsonElement topic, string format)
        {
            string mapId = DocsToc.Str(topic, "mapId");
            string contentId = DocsToc.Str(topic, "contentId");
            string? readerUrl = Catalog.FallbackReaderUrl(DocsToc.Str(topic, "readerUrl"));
            if (mapId.Length > 0 && contentId.Length > 0)
            {
                var target = new Resolved(host, mapId, contentId);
                try
                {
                    string compact = Catalog.CompactRefOf(target);
                    return format == "compact" ? compact : readerUrl ?? Catalog.IdUrl(target);
                }
                catch (DocsCatalogException)
                {
                    // Malformed upstream ids may still carry a usable reader URL.
                }
            }
            return readerUrl ?? "(reference unavailable)";
        }

        private static SearchHit NormalizeHit(JsonElement topic, string reference)
        {
            string version = "";
            if (topic.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Array)
            {
                foreach (var key in new[] { DocsCatalog.TiaVersionKey, DocsCatalog.SwVersionKey })
                {
                    var values = metadata.EnumerateArray()
                        .Where(m => DocsToc.Str(m, "key") == key && m.TryGetProperty("values", out var v) && v.ValueKind == JsonValueKind.Array && v.GetArrayLength() > 0)
                        .Select(m => m.GetProperty("values")[0].GetString() ?? "")
                        .FirstOrDefault();
                    if (!string.IsNullOrEmpty(values))
                    {
                        version = values!;
                        break;
                    }
                }
            }
            string excerptHtml = DocsToc.Str(topic, "htmlExcerpt");
            string excerpt = "";
            if (excerptHtml.Length > 0)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(excerptHtml);
                // Highlight spans carry their own spacing; join text without a separator.
                excerpt = DocsText.CollapseWhitespace(HtmlEntity.DeEntitize(doc.DocumentNode.InnerText) ?? "");
            }
            var breadcrumb = Array(topic, "breadcrumb").Where(b => b.ValueKind == JsonValueKind.String).Select(b => b.GetString() ?? "").ToList();
            return new SearchHit
            {
                Title = DocsToc.Str(topic, "title"),
                MapTitle = DocsToc.Str(topic, "mapTitle"),
                Version = version,
                Breadcrumb = breadcrumb,
                Excerpt = excerpt,
                Reference = reference
            };
        }

        // ------------------------------------------------------------------
        // Publications and TOC
        // ------------------------------------------------------------------

        public string ListPublications(string? host = null, string? product = null, string? version = null, string? locale = null,
            string? titleContains = null, string? referenceFormat = "compact")
        {
            string format = ValidateReferenceFormat(referenceFormat);
            host = Catalog.CheckHost(Blank(host) ?? DefaultHost);
            string loc = Catalog.Canonical(host, "locale", Blank(locale) ?? DefaultLocale);
            var maps = Catalog.Filter(host, Blank(product), Blank(version), loc, Blank(titleContains));
            var rows = maps.Take(MaxPublications)
                .Select(m => (m, format == "compact" ? Catalog.CompactRefOf(new Resolved(host, m.Id, null)) : Catalog.MapUrl(host, m.Id)))
                .ToList();
            return DocsRendering.Publications(host, rows, maps.Count, MaxPublications);
        }

        public string GetToc(string url, int depth = 2, string? referenceFormat = "compact")
        {
            string format = ValidateReferenceFormat(referenceFormat);
            if (depth < 1 || depth > MaxDepth) throw new ArgumentException($"depth must be 1-{MaxDepth}, got {depth}");
            var res = Catalog.Resolve(url);
            var root = Catalog.Toc(res.Host, res.MapId);
            string pubTitle = Catalog.Maps(res.Host)[res.MapId].Title;
            var start = res.ContentId == null ? root : TopicPath(res).Last();
            var rows = new List<(int, string, string)>();

            void Walk(TocPage node, int level)
            {
                string reference = format == "compact"
                    ? Catalog.CompactRefOf(new Resolved(res.Host, res.MapId, node.ContentId.Length > 0 ? node.ContentId : null))
                    : Catalog.ReaderUrl(res.Host, res.MapId, node);
                rows.Add((level, node.Title.Length > 0 ? node.Title : pubTitle, reference));
                if (level < depth)
                    foreach (var child in node.Children) Walk(child, level + 1);
            }

            Walk(start, 0);
            return DocsRendering.Toc(rows.Take(MaxTocLines).ToList(), rows.Count > MaxTocLines, MaxTocLines);
        }

        private List<TocPage> TopicPath(Resolved res)
        {
            var path = DocsToc.FindPath(Catalog.Toc(res.Host, res.MapId), res.ContentId ?? "");
            if (path == null)
                throw new DocsCatalogException($"Topic {res.ContentId} is not in this publication's TOC. Use docs_search to find a valid reference.");
            return path;
        }

        // ------------------------------------------------------------------
        // Read page
        // ------------------------------------------------------------------

        /// <summary>Normalizes the section argument: digits become an outline number, other text a heading match.</summary>
        public static object? NormalizeSection(object? section)
        {
            if (section is string s)
            {
                s = s.Trim();
                if (s.Length == 0) return null;
                if (s.All(char.IsDigit) && int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out int n)) return n;
                return s;
            }
            if (section is int i)
            {
                if (i < 0) throw new ArgumentException($"section must be an outline number (0 or more) or heading text, got {i}");
                return i;
            }
            return null;
        }

        public string ReadPage(string url, int offset = 0, int maxChars = 20000, string? referenceFormat = "compact",
            object? section = null, string? mode = "full")
        {
            string format = ValidateReferenceFormat(referenceFormat);
            if (offset < 0) throw new ArgumentException($"offset must be >= 0, got {offset}");
            if (maxChars < MinChars || maxChars > MaxChars) throw new ArgumentException($"max_chars must be {MinChars}-{MaxChars}, got {maxChars}");
            mode = string.IsNullOrEmpty(mode) ? "full" : mode;
            if (!DocsReading.ReadModes.Contains(mode)) throw new ArgumentException($"mode must be one of: {string.Join(", ", DocsReading.ReadModes)}; got '{mode}'");
            section = NormalizeSection(section);
            if (mode == "outline" && (section != null || offset > 0))
                throw new ArgumentException("mode='outline' takes neither section nor offset; it lists the whole page.");

            var res = Catalog.Resolve(url);
            if (!Catalog.Maps(res.Host).TryGetValue(res.MapId, out var info))
                throw new DocsCatalogException($"Publication {res.MapId} not found on {res.Host}.");
            var root = Catalog.Toc(res.Host, res.MapId);
            string contentId = res.ContentId ?? (root.ContentId.Length > 0 ? root.ContentId : root.Children.FirstOrDefault()?.ContentId ?? "");
            var path = DocsToc.FindPath(root, contentId);
            string title;
            string source;
            List<string> titles;
            if (path != null)
            {
                var page = path[path.Count - 1];
                contentId = page.ContentId; // the link may have named the tocId
                title = page.Title;
                source = format == "compact"
                    ? Catalog.CompactRefOf(new Resolved(res.Host, res.MapId, contentId))
                    : Catalog.ReaderUrl(res.Host, res.MapId, page);
                titles = path.Select(p => p.Title.Length > 0 ? p.Title : info.Title).ToList();
            }
            else
            {
                title = "(untitled topic)";
                var target = new Resolved(res.Host, res.MapId, contentId);
                source = format == "compact" ? Catalog.CompactRefOf(target) : Catalog.IdUrl(target);
                titles = new List<string> { info.Title };
            }

            var html = DocsConverter.ParseAndClean(Catalog.Client(res.Host).GetContent(res.MapId, contentId));
            string host = res.Host;
            var body = html == null
                ? new List<Block>()
                : DocsConverter.HtmlToBlocks(html, href => BodyLink(href, format, host), images: false);
            var full = new List<Block> { new Block(BlockKind.Heading, "# " + title, 1) };
            full.AddRange(body.Count > 0 ? body : new List<Block> { new Block(BlockKind.Paragraph, "*No content available.*") });

            if (mode == "outline")
                return DocsRendering.Page(info, titles, source, DocsRendering.Outline(DocsReading.ViewText(full).Length, DocsReading.OutlineRows(body)));
            List<Block> view = section == null ? full : DocsReading.SelectSections(body, section);
            if (mode == "code")
            {
                view = DocsReading.CodeView(section == null ? body : view);
                if (view.Count == 0)
                {
                    string note = section == null ? "No code blocks on this page." : "No code blocks in the selected sections.";
                    return DocsRendering.Page(info, titles, source, note);
                }
            }
            var chunk = DocsReading.ChunkView(view, offset, maxChars);
            string text = chunk.Text;
            if (chunk.NextOffset.HasValue)
                text += "\n\n" + DocsRendering.Continuation(chunk.NextOffset.Value, section, mode!, format);
            return DocsRendering.Page(info, titles, source, text);
        }

        /// <summary>
        /// Page-body link policy: drop attachment URLs; topic links on the page's own host follow the reference
        /// format. Links to other hosts stay unchanged so the same page always renders the same text.
        /// </summary>
        private string? BodyLink(string href, string format, string host)
        {
            if (Catalog.IsAttachmentLink(href)) return null;
            return format == "compact" ? Catalog.CompactLink(href, host) ?? href : href;
        }

        // ------------------------------------------------------------------
        // JSON helpers
        // ------------------------------------------------------------------

        private static IEnumerable<JsonElement> Array(JsonElement obj, string name)
        {
            if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array)
                return arr.EnumerateArray();
            return Enumerable.Empty<JsonElement>();
        }

        private static JsonElement Obj(JsonElement obj, string name) =>
            obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v) ? v : default;

        private static int? IntProp(JsonElement obj, string name) =>
            obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n)
                ? n
                : (int?)null;
    }
}
