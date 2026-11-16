// Tests for the docs_* tools (Siemens online documentation). Compiled by Test-Docs.ps1 against the built exe.
// Offline tests use a fake Fluid Topics client; pass "live" as the first argument to also query the real site.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TiaOpennessMcp.Config;
using TiaOpennessMcp.Docs;

internal static class DocsTests
{
    private static int _passed;
    private static int _failed;

    private static int Main(string[] args)
    {
        bool live = args.Length > 0 && args[0] == "live";
        Run("converter: headings, paragraphs and Siemens code paragraphs", ConverterBasics);
        Run("converter: source-code table keeps one line per paragraph", ConverterCodeTable);
        Run("converter: safety table and admonition become quotes", ConverterQuotes);
        Run("converter: lists, nested lists and regular tables", ConverterListsAndTables);
        Run("converter: chrome removed and links rewritten", ConverterChromeAndLinks);
        Run("converter: definition list fallback", ConverterDefinitionList);
        Run("reading: outline rows and section numbers", ReadingOutline);
        Run("reading: section by heading text includes 'Text: ...' headings", ReadingSectionText);
        Run("reading: unknown section lists the headings", ReadingSectionErrors);
        Run("reading: code view keeps headings", ReadingCodeView);
        Run("reading: chunks of paragraphs rebuild the page", ReadingChunksRebuild);
        Run("reading: long code block split keeps fences", ReadingChunksCode);
        Run("reading: long table split repeats the header", ReadingChunksTable);
        Run("reading: offset past end is rejected", ReadingOffsetPastEnd);
        Run("text: close matches and ratio", TextCloseMatches);
        Run("catalog: compact references and reader URLs resolve", CatalogResolve);
        Run("catalog: unknown filter values suggest close matches", CatalogUnknownValues);
        Run("catalog: catalog is cached", CatalogCaching);
        Run("tools: search renders hits with compact references", ToolsSearch);
        Run("tools: scoped search filters by publication", ToolsScopedSearch);
        Run("tools: scoped search refuses foreign hits", ToolsScopedSearchForeign);
        Run("tools: read page full, outline, section, code and continuation", ToolsReadPage);
        Run("tools: argument validation", ToolsValidation);
        Run("tools: table of contents", ToolsToc);
        Run("tools: list publications", ToolsListPublications);
        Run("settings: environment variables", SettingsEnvironment);
        Run("client: Retry-After backoff parsing", ClientBackoff);
        if (live)
        {
            Run("live: search", LiveSearch);
            Run("live: list publications and table of contents", LiveListAndToc);
            Run("live: read page outline, section and code", LiveReadPage);
        }
        Console.WriteLine($"\n{_passed} passed, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            _passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine("FAIL " + name + "\n     " + ex.Message.Replace("\n", "\n     "));
        }
    }

    private static void Equal<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"{what}: expected <{expected}>, got <{actual}>");
    }

    private static void True(bool condition, string what)
    {
        if (!condition) throw new Exception(what);
    }

    private static void Contains(string haystack, string needle, string what)
    {
        if (haystack.IndexOf(needle, StringComparison.Ordinal) < 0)
            throw new Exception($"{what}: expected to contain <{needle}>, got:\n{haystack}");
    }

    private static T Throws<T>(Action action, string what) where T : Exception
    {
        try
        {
            action();
        }
        catch (T ex)
        {
            return ex;
        }
        throw new Exception(what + ": expected " + typeof(T).Name);
    }

    // ------------------------------------------------------------------
    // Converter
    // ------------------------------------------------------------------

    private static string Md(string html) => DocsConverter.HtmlToMarkdown(html, "T");

    private static void ConverterBasics()
    {
        Equal("# T\n\n## Section\n\nSome **bold** and `code` text.\n\n```\nx := 1;\n```\n\ntail\n",
            Md("<p class=\"BlocktitleFirst\">Section</p><p>Some <b>bold</b> and <code>code</code>\n  text.</p>" +
               "<p class=\"p_table_l_code\">x := 1;</p><p class=\"p_table_title\">Table 1</p><p>tail</p>"),
            "basic page");
        Equal("# T\n\n*No content available.*\n", Md("   "), "empty page");
        Equal("# T\n\nA & B < C\n", Md("<p>A &amp;amp; B &lt; C</p>"), "entities decoded twice like BeautifulSoup + html.unescape");
    }

    private static void ConverterCodeTable()
    {
        string html = "<table class=\"table_sourcecode\"><tr><td>" +
                      "<p class=\"p_table_l_code\">IF a THEN</p><p class=\"p_table_l_code\">&nbsp;&nbsp;b := 1;</p>" +
                      "<p class=\"p_table_l_code\">END_IF;</p></td></tr></table>";
        Equal("# T\n\n```\nIF a THEN\n  b := 1;\nEND_IF;\n```\n", Md(html), "code table");
        Equal("# T\n\n```\nint a;\n\nint b;\n```\n", Md("<pre>\n\nint a;\n\nint b;   \n\n</pre>"), "pre");
        Equal("# T\n\n```\nlet a = 1;\nlet b = 2;\n```\n",
            Md("<table class=\"table_sourcecode\"><tr><td><p class=\"p_table_l_code\">let a = 1;<br>let b = 2;</p></td></tr></table>"),
            "<br> line breaks in a code paragraph (WinCC Unified examples)");
    }

    private static void ConverterQuotes()
    {
        Equal("# T\n\n> **Note**\n>\n> Mind the gap.\n> Second line.\n",
            Md("<table class=\"safety\"><tr><td>NOTE</td></tr><tr><td>Mind the gap.</td></tr><tr><td>Second line.</td></tr></table>"),
            "safety note");
        Equal("# T\n\n> **Warning:** Hot surface.\n",
            Md("<div class=\"admonition warning\"><p>Hot surface.</p></div>"), "admonition without title");
    }

    private static void ConverterListsAndTables()
    {
        Equal("# T\n\n- top\n  1. one\n  2. two\n- Use **TON** here\n",
            Md("<ul><li>top<ol><li>one</li><li>two</li></ol></li><li>Use <b>TON</b> here</li></ul>"), "nested list");
        Equal("# T\n\n| A | B |  |\n| --- | --- | --- |\n| 1 |  |  |\n| x | y | z |\n",
            Md("<table><tr><th>A</th><th>B</th></tr><tr><td>1</td></tr><tr><td>x</td><td>y</td><td>z</td></tr></table>"),
            "regular table padded");
    }

    private static void ConverterChromeAndLinks()
    {
        string html = "<div><nav>Home &gt; Docs</nav><div class=\"ft-breadcrumb\">crumbs</div><p>Before " +
                      "<a href=\"https://docs.tia.siemens.cloud/r/MAP1/T2\">other topic</a> and " +
                      "<a href=\"https://docs.tia.siemens.cloud/viewer/attachment/x.png\">pic</a></p><button>Copy</button></div>";
        var blocks = DocsConverter.HtmlToBlocks(html, href => href.Contains("/viewer/attachment/") ? null : "tia:MAP1/T2", images: false);
        Equal(1, blocks.Count, "block count");
        Equal("Before [other topic](tia:MAP1/T2) and pic", blocks[0].Text, "link rewrite");
        Equal("# T\n\nInline data and ![Real](https://h/x.png)\n",
            Md("<p>Inline <img alt=\"data\" src=\"data:image/png;base64,AAA=\"> and <img alt=\"Real\" src=\"https://h/x.png\"></p>"),
            "images");
    }

    private static void ConverterDefinitionList()
    {
        Equal("# T\n\nTerm\n\n:   Definition\n", Md("<dl><dt>Term</dt><dd>Definition</dd></dl>"), "dl");
    }

    // ------------------------------------------------------------------
    // Reading
    // ------------------------------------------------------------------

    private static List<Block> SamplePage() => new List<Block>
    {
        new Block(BlockKind.Paragraph, "Lead text."),
        new Block(BlockKind.Heading, "## Description", 2),
        new Block(BlockKind.Paragraph, "About it."),
        new Block(BlockKind.Heading, "### Details", 3),
        new Block(BlockKind.Code, "```\nx := 1;\n```"),
        new Block(BlockKind.Heading, "## Example: **SCL**", 2),
        new Block(BlockKind.Code, "```\ny := 2;\n```"),
        new Block(BlockKind.Heading, "## Example", 2),
        new Block(BlockKind.Paragraph, "Plain example."),
    };

    private static void ReadingOutline()
    {
        var rows = DocsReading.OutlineRows(SamplePage());
        Equal(5, rows.Count, "row count");
        Equal("(before first heading)", rows[0].Label, "lead label");
        Equal(0, rows[0].Number, "lead number");
        Equal("## Description", rows[1].Label, "first heading");
        Equal(1, rows[1].CodeBlocks, "nested code counted in parent");
        Equal("(whole page)", DocsReading.OutlineRows(new List<Block> { new Block(BlockKind.Paragraph, "x") })[0].Label, "no headings");
        var section = DocsReading.SelectSections(SamplePage(), 1);
        Equal(4, section.Count, "section 1 includes nested section");
        string outline = DocsRendering.Outline(1234, rows);
        Contains(outline, "Outline (5 sections, 1,234 chars):", "outline header");
        Contains(outline, "1 code block", "outline code count");
    }

    private static void ReadingSectionText()
    {
        var blocks = DocsReading.SelectSections(SamplePage(), "example");
        Equal(4, blocks.Count, "both Example sections");
        Equal("## Example: **SCL**", blocks[0].Text, "first match");
        Equal("## Example", blocks[2].Text, "second match");
        var nested = DocsReading.SelectSections(SamplePage(), "Details");
        Equal(2, nested.Count, "nested section alone");
    }

    private static void ReadingSectionErrors()
    {
        var ex = Throws<ArgumentException>(() => DocsReading.SelectSections(SamplePage(), "Descripton"), "typo");
        Contains(ex.Message, "Close matches: 1. ## Description", "suggestion");
        Contains(ex.Message, "Sections:\n0. (before first heading)", "heading list");
        Throws<ArgumentException>(() => DocsReading.SelectSections(SamplePage(), 9), "bad number");
        Throws<ArgumentException>(() => DocsReading.SelectSections(new List<Block> { new Block(BlockKind.Paragraph, "x") }, "a"), "no headings");
    }

    private static void ReadingCodeView()
    {
        var code = DocsReading.CodeView(SamplePage());
        Equal(4, code.Count, "headings + code");
        Equal("### Details", code[0].Text, "heading before first code");
        Equal("## Example: **SCL**", code[2].Text, "heading before second code");
    }

    private static void ReadingChunksRebuild()
    {
        var blocks = Enumerable.Range(0, 40).Select(i => new Block(BlockKind.Paragraph, $"Paragraph {i} " + new string('x', 90))).ToList();
        string full = DocsReading.ViewText(blocks);
        int offset = 0;
        var pieces = new List<string>();
        for (int guard = 0; guard < 100; guard++)
        {
            var chunk = DocsReading.ChunkView(blocks, offset, 1000);
            True(chunk.Text.Length <= 1000, "chunk within budget");
            pieces.Add(chunk.Text);
            if (chunk.NextOffset == null) break;
            True(chunk.NextOffset.Value > offset, "progress");
            offset = chunk.NextOffset.Value;
        }
        Equal(full, string.Join("\n\n", pieces), "chunks joined rebuild the page");
    }

    private static void ReadingChunksCode()
    {
        var lines = Enumerable.Range(0, 200).Select(i => $"line_{i:000} := {i};");
        var blocks = new List<Block> { new Block(BlockKind.Code, "```\n" + string.Join("\n", lines) + "\n```") };
        int offset = 0;
        var seen = new List<string>();
        for (int guard = 0; guard < 100; guard++)
        {
            var chunk = DocsReading.ChunkView(blocks, offset, 1000);
            True(chunk.Text.Length <= 1000, "budget");
            True(chunk.Text.StartsWith("```\n", StringComparison.Ordinal), "opening fence");
            True(chunk.Text.TrimEnd('\n').EndsWith("```", StringComparison.Ordinal), "closing fence");
            seen.AddRange(chunk.Text.Split('\n').Where(l => l.StartsWith("line_", StringComparison.Ordinal)));
            if (chunk.NextOffset == null) break;
            offset = chunk.NextOffset.Value;
        }
        Equal(200, seen.Count, "every line exactly once");
        Equal(200, seen.Distinct().Count(), "no duplicates");
    }

    private static void ReadingChunksTable()
    {
        var rows = Enumerable.Range(0, 100).Select(i => $"| r{i} | value {i} |");
        var blocks = new List<Block> { new Block(BlockKind.Table, "| Name | Value |\n| --- | --- |\n" + string.Join("\n", rows)) };
        var first = DocsReading.ChunkView(blocks, 0, 1000);
        True(first.NextOffset.HasValue, "table needs two chunks");
        var second = DocsReading.ChunkView(blocks, first.NextOffset!.Value, 1000);
        True(second.Text.StartsWith("| Name | Value |\n| --- | --- |\n", StringComparison.Ordinal), "header repeated");
    }

    private static void ReadingOffsetPastEnd()
    {
        var blocks = new List<Block> { new Block(BlockKind.Paragraph, "short") };
        Throws<ArgumentException>(() => DocsReading.ChunkView(blocks, 100, 1000), "offset past end");
        Equal("short\n", DocsReading.ChunkView(blocks, 0, 1000).Text, "whole view");
    }

    private static void TextCloseMatches()
    {
        Equal(1.0, DocsText.Ratio("abc", "abc"), "identical");
        Equal(0.75, DocsText.Ratio("abcd", "bcde"), "difflib example");
        var m = DocsText.CloseMatches("appel", new[] { "ape", "apple", "peach", "puppy" });
        Equal("apple,ape", string.Join(",", m), "difflib docs example");
    }

    // ------------------------------------------------------------------
    // Fake client and catalog
    // ------------------------------------------------------------------

    private sealed class FakeClient : IFluidTopicsClient
    {
        public FakeClient(string host) { Host = host; }
        public string Host { get; }
        public int MapsCalls;
        public int PagesCalls;
        public readonly List<(string Query, string Locale, IList<SearchFilter>? Filters, int Page, int PerPage)> Searches = new List<(string, string, IList<SearchFilter>?, int, int)>();
        public Func<int, string>? SearchResponse;
        public Dictionary<string, string> Content = new Dictionary<string, string>();

        public JsonElement ListMaps()
        {
            MapsCalls++;
            return Parse(@"[
              {""id"":""MAP1"",""title"":""TIA Portal Openness"",""metadata"":[
                 {""key"":""ft:locale"",""values"":[""en-US""]},{""key"":""Product"",""values"":[""TIA Portal Openness""]},
                 {""key"":""tia:SoftwareVersionFilter"",""values"":[""V21""]},{""key"":""ft:prettyUrl"",""values"":[""/r/en-us/v21/openness""]}]},
              {""id"":""MAP2"",""title"":""WinCC Unified scripting"",""metadata"":[
                 {""key"":""ft:locale"",""values"":[""en-US""]},{""key"":""Product"",""values"":[""WinCC Unified""]},
                 {""key"":""tia:SoftwareVersionFilter"",""values"":[""V21""]},{""key"":""ft:prettyUrl"",""values"":[""/r/en-us/v21/unified""]}]},
              {""id"":""MAP3"",""title"":""STEP 7 Basic"",""metadata"":[
                 {""key"":""ft:locale"",""values"":[""de-DE""]},{""key"":""Product"",""values"":[""STEP 7""]},
                 {""key"":""tia:SoftwareVersionFilter"",""values"":[""V20""]}]}
            ]");
        }

        public JsonElement GetPages(string mapId)
        {
            PagesCalls++;
            return Parse(@"{""paginatedToc"":[{""tocId"":""toc0"",""contentId"":""ROOT"",""title"":""Openness"",""prettyUrl"":""/r/en-us/v21/openness"",
              ""pageToc"":[
                {""tocId"":""toc1"",""contentId"":""T1"",""title"":""Basics"",""prettyUrl"":""/r/en-us/v21/openness/basics"",""children"":[
                   {""tocId"":""toc2"",""contentId"":""T2"",""title"":""Connecting"",""prettyUrl"":""/r/en-us/v21/openness/basics/connecting"",""children"":[]}]},
                {""tocId"":""toc3"",""contentId"":""T3"",""title"":""Blocks"",""prettyUrl"":""/r/en-us/v21/openness/blocks"",""children"":[]}
              ]}]}");
        }

        public string GetContent(string mapId, string contentId) =>
            Content.TryGetValue(contentId, out var html) ? html : "";

        public JsonElement Search(string query, string locale, IList<SearchFilter>? filters, int page, int perPage)
        {
            Searches.Add((query, locale, filters, page, perPage));
            return Parse(SearchResponse != null ? SearchResponse(page) : @"{""results"":[],""paging"":{""totalResultsCount"":0,""isLastPage"":true}}");
        }

        private static JsonElement Parse(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
    }

    private static (DocsTools Tools, FakeClient Client) NewTools()
    {
        var client = new FakeClient("docs.tia.siemens.cloud");
        var catalog = new DocsCatalog(DocsSettings.DefaultHosts, h => client);
        return (new DocsTools(catalog, "docs.tia.siemens.cloud", "en-US"), client);
    }

    private static string Topic(string mapId, string contentId, string title) =>
        $@"{{""type"":""TOPIC"",""topic"":{{""mapId"":""{mapId}"",""contentId"":""{contentId}"",""title"":""{title}"",""mapTitle"":""TIA Portal Openness"",
            ""breadcrumb"":[""Openness"",""Basics""],""htmlExcerpt"":""Use <span class=\""kwicmatch\"">Connect</span>ion&amp;more"",
            ""readerUrl"":""https://docs.tia.siemens.cloud/r/en-us/v21/openness/x"",
            ""metadata"":[{{""key"":""tia:SoftwareVersionFilter"",""values"":[""V21""]}}]}}}}";

    private static void CatalogResolve()
    {
        var (tools, _) = NewTools();
        var c = tools.Catalog;
        Equal(new Resolved("docs.tia.siemens.cloud", "MAP1", "T2"), c.Resolve("tia:MAP1/T2"), "compact");
        Equal(new Resolved("docs.tia.siemens.cloud", "MAP1", null), c.Resolve("TIA:MAP1"), "compact prefix case");
        Equal(new Resolved("docs.tia.siemens.cloud", "MAP1", "T2"), c.Resolve("docs.tia.siemens.cloud:MAP1/T2"), "full host prefix");
        Equal(new Resolved("docs.tia.siemens.cloud", "MAP1", "T3"), c.Resolve("https://docs.tia.siemens.cloud/r/MAP1/T3"), "id URL");
        Equal(new Resolved("docs.tia.siemens.cloud", "MAP1", null), c.Resolve("https://docs.tia.siemens.cloud/r/en-us/v21/openness/"), "pretty publication URL");
        Equal(new Resolved("docs.tia.siemens.cloud", "MAP1", "T2"), c.Resolve("https://docs.tia.siemens.cloud/r/en-US/v21/openness/basics/connecting"), "pretty topic URL");
        Throws<DocsCatalogException>(() => c.Resolve("tia:NOPE/T1"), "unknown map");
        Throws<DocsCatalogException>(() => c.Resolve("https://evil.example.com/r/MAP1"), "host not allowed");
        Throws<DocsCatalogException>(() => c.Resolve("tia:MAP1/.."), "dot segment");
        Equal("tia:MAP1/T2", c.CompactLink("https://docs.tia.siemens.cloud/r/MAP1/T2"), "body link compacted");
        Equal(null, c.CompactLink("https://docs.tia.siemens.cloud/r/MAP1/T2#anchor"), "fragment kept");
        Equal(null, c.CompactLink("http://[IP address of the CPU]/"), "malformed URL ignored");
        True(c.IsAttachmentLink("https://docs.tia.siemens.cloud/api/khub/maps/MAP1/resources/x.png"), "attachment");
        Equal("https://docs.tia.siemens.cloud/r/en-us/v21/openness", c.MapUrl("docs.tia.siemens.cloud", "MAP1"), "pretty map URL");
        Equal("https://docs.tia.siemens.cloud/r/MAP3", c.MapUrl("docs.tia.siemens.cloud", "MAP3"), "id map URL fallback");
    }

    private static void CatalogUnknownValues()
    {
        var (tools, _) = NewTools();
        var ex = Throws<DocsCatalogException>(() => tools.Catalog.Canonical("docs.tia.siemens.cloud", "product", "WinCC Unifed"), "typo");
        Contains(ex.Message, "Close matches: ['WinCC Unified']", "product suggestion");
        Equal("WinCC Unified", tools.Catalog.Canonical("docs.tia.siemens.cloud", "product", "wincc unified"), "case-insensitive");
        Equal(("tia:SoftwareVersionFilter", "V21"), tools.Catalog.CanonicalVersion("docs.tia.siemens.cloud", "v21"), "version");
    }

    private static void CatalogCaching()
    {
        var (tools, client) = NewTools();
        tools.Catalog.Maps("docs.tia.siemens.cloud");
        tools.Catalog.Maps("DOCS.TIA.SIEMENS.CLOUD");
        tools.Catalog.Toc("docs.tia.siemens.cloud", "MAP1");
        tools.Catalog.Toc("docs.tia.siemens.cloud", "MAP1");
        Equal(1, client.MapsCalls, "maps fetched once");
        Equal(1, client.PagesCalls, "toc fetched once");
    }

    // ------------------------------------------------------------------
    // Tools
    // ------------------------------------------------------------------

    private static void ToolsSearch()
    {
        var (tools, client) = NewTools();
        client.SearchResponse = _ => $@"{{""results"":[{{""entries"":[{Topic("MAP1", "T2", "Connecting")}]}}],""paging"":{{""totalResultsCount"":7}}}}";
        string text = tools.Search("connect", product: "tia portal openness", version: "v21", limit: 5);
        Contains(text, "Found 7 results for \"connect\" on docs.tia.siemens.cloud. Showing 1:", "header");
        Contains(text, "1. **Connecting** — TIA Portal Openness (V21)", "hit");
        Contains(text, "   Openness > Basics", "breadcrumb");
        Contains(text, "   Use Connection&more", "excerpt");
        Contains(text, "   tia:MAP1/T2", "compact reference");
        var call = client.Searches.Single();
        Equal("en-US", call.Locale, "default locale");
        Equal(5, call.PerPage, "limit");
        Equal("Product=TIA Portal Openness;tia:SoftwareVersionFilter=V21",
            string.Join(";", call.Filters!.Select(f => f.Key + "=" + string.Join(",", f.Values))), "canonical filters");
        string urls = tools.Search("connect", referenceFormat: "url");
        Contains(urls, "https://docs.tia.siemens.cloud/r/en-us/v21/openness/x", "reader URL");
        client.SearchResponse = null;
        Equal("No results for \"zzz\" on docs.tia.siemens.cloud.", tools.Search("zzz"), "no results");
    }

    private static void ToolsScopedSearch()
    {
        var (tools, client) = NewTools();
        client.SearchResponse = _ => $@"{{""results"":[{{""entries"":[{Topic("MAP1", "T2", "Connecting")}]}},{{""entries"":[{Topic("MAP1", "T3", "Blocks")}]}}],
            ""paging"":{{""totalClustersCount"":2,""isLastPage"":true}}}}";
        string whole = tools.Search("x", scope: "tia:MAP1");
        Contains(whole, "Found 2 results for \"x\" in TIA Portal Openness (V21). Showing 2:", "publication scope");
        Equal("ft:publicationId", client.Searches.Last().Filters!.Single().Key, "publication filter");
        string subtree = tools.Search("x", scope: "tia:MAP1/T1");
        Contains(subtree, "Found 1 results for \"x\" in \"Basics\" — TIA Portal Openness (V21)", "subtree scope");
        Contains(subtree, "**Connecting**", "hit inside subtree");
        True(!subtree.Contains("**Blocks**"), "hit outside subtree dropped");
        Throws<ArgumentException>(() => tools.Search("x", product: "STEP 7", scope: "tia:MAP1"), "product with scope");
        Throws<ArgumentException>(() => tools.Search("x", locale: "de-DE", scope: "tia:MAP1"), "locale mismatch");
    }

    private static void ToolsScopedSearchForeign()
    {
        var (tools, client) = NewTools();
        client.SearchResponse = _ => $@"{{""results"":[{{""entries"":[{Topic("MAP2", "Z", "Other")}]}}],""paging"":{{""totalClustersCount"":1}}}}";
        var ex = Throws<FluidTopicsException>(() => tools.Search("x", scope: "tia:MAP1"), "filter not applied");
        Contains(ex.Message, "did not apply the publication filter", "message");
    }

    private static void ToolsReadPage()
    {
        var (tools, client) = NewTools();
        client.Content["T2"] = "<div><p>Intro.</p><h2>Program code</h2><pre>x := 1;</pre><h2>Program code: SCL</h2>" +
                               "<pre>y := 2;</pre><h2>Remarks</h2><p>See <a href=\"https://docs.tia.siemens.cloud/r/MAP1/T3\">Blocks</a>.</p></div>";
        string full = tools.ReadPage("tia:MAP1/T2");
        Contains(full, "> Publication: TIA Portal Openness (V21) [en-US]\n> Path: Openness > Basics > Connecting\n> Source: tia:MAP1/T2\n\n# Connecting\n\nIntro.", "header + body");
        Contains(full, "See [Blocks](tia:MAP1/T3).", "body link compacted");
        string outline = tools.ReadPage("tia:MAP1/T2", mode: "outline");
        Contains(outline, "Outline (4 sections,", "outline");
        Contains(outline, "2. ## Program code: SCL", "outline row");
        string section = tools.ReadPage("tia:MAP1/T2", section: "program code");
        Contains(section, "x := 1;", "first match");
        Contains(section, "y := 2;", "second match");
        True(!section.Contains("Remarks"), "other sections excluded");
        string numbered = tools.ReadPage("tia:MAP1/T2", section: "3");
        Contains(numbered, "## Remarks", "section by number string");
        string code = tools.ReadPage("tia:MAP1/T2", mode: "code");
        Contains(code, "## Program code\n\n```\nx := 1;\n```", "code view");
        string viaUrl = tools.ReadPage("https://docs.tia.siemens.cloud/r/en-us/v21/openness/basics/connecting", referenceFormat: "url");
        Contains(viaUrl, "> Source: https://docs.tia.siemens.cloud/r/en-us/v21/openness/basics/connecting", "url source");
        Contains(viaUrl, "[Blocks](https://docs.tia.siemens.cloud/r/MAP1/T3)", "url body link");
        string root = tools.ReadPage("tia:MAP1");
        Contains(root, "# Openness\n\n*No content available.*", "publication reads root topic");

        client.Content["T3"] = "<div>" + string.Concat(Enumerable.Range(0, 60).Select(i => $"<p>Paragraph {i} {new string('z', 80)}</p>")) + "</div>";
        string lead = tools.ReadPage("tia:MAP1/T3", maxChars: 1000, section: "0");
        Contains(lead, "Paragraph 0", "section 0 is the lead of a page without headings");
        string first = tools.ReadPage("tia:MAP1/T3", maxChars: 1000);
        Contains(first, "[truncated — call again with offset=", "continuation");
        int offset = int.Parse(first.Substring(first.LastIndexOf("offset=", StringComparison.Ordinal) + 7).TrimEnd(']'));
        string next = tools.ReadPage("tia:MAP1/T3", offset: offset, maxChars: 1000);
        True(!next.Contains("Paragraph 0 "), "second part moves on");
        string sectionPart = tools.ReadPage("tia:MAP1/T2", maxChars: 1000, section: "Program code", mode: "code");
        True(!sectionPart.Contains("truncated"), "short section not truncated");
        Equal("[truncated — call again with section=\"Program code\", mode=\"code\", reference_format=\"url\", offset=5]",
            DocsRendering.Continuation(5, "Program code", "code", "url"), "continuation arguments");
    }

    private static void ToolsValidation()
    {
        var (tools, _) = NewTools();
        Throws<ArgumentException>(() => tools.ReadPage("tia:MAP1/T2", maxChars: 10), "max_chars");
        Throws<ArgumentException>(() => tools.ReadPage("tia:MAP1/T2", offset: -1), "offset");
        Throws<ArgumentException>(() => tools.ReadPage("tia:MAP1/T2", mode: "xml"), "mode");
        Throws<ArgumentException>(() => tools.ReadPage("tia:MAP1/T2", mode: "outline", section: "1"), "outline with section");
        Throws<ArgumentException>(() => tools.ReadPage("tia:MAP1/T2", section: -1), "negative section");
        Throws<ArgumentException>(() => tools.Search("x", limit: 0), "limit");
        Throws<ArgumentException>(() => tools.Search("x", referenceFormat: "json"), "reference_format");
        Throws<ArgumentException>(() => tools.GetToc("tia:MAP1", depth: 7), "depth");
        Throws<DocsCatalogException>(() => tools.Search("x", host: "example.com"), "host");
        Throws<DocsCatalogException>(() => tools.GetToc("tia:MAP1/NOPE"), "topic not in TOC");
    }

    private static void ToolsToc()
    {
        var (tools, _) = NewTools();
        Equal("- Openness — tia:MAP1/ROOT\n  - Basics — tia:MAP1/T1\n  - Blocks — tia:MAP1/T3", tools.GetToc("tia:MAP1", depth: 1), "depth 1");
        Equal("- Basics — tia:MAP1/T1\n  - Connecting — tia:MAP1/T2", tools.GetToc("tia:MAP1/T1"), "subtree");
        Contains(tools.GetToc("tia:MAP1", 1, "url"), "- Basics — https://docs.tia.siemens.cloud/r/en-us/v21/openness/basics", "reader URLs");
    }

    private static void ToolsListPublications()
    {
        var (tools, _) = NewTools();
        Equal("- **TIA Portal Openness** — TIA Portal Openness V21 [en-US] tia:MAP1\n- **WinCC Unified scripting** — WinCC Unified V21 [en-US] tia:MAP2",
            tools.ListPublications(), "default locale filter, sorted by title");
        Equal("- **STEP 7 Basic** — STEP 7 V20 [de-DE] tia:MAP3", tools.ListPublications(locale: "de-de"), "locale");
        Equal("No publications match these filters on docs.tia.siemens.cloud.", tools.ListPublications(titleContains: "nothing"), "no rows");
        Contains(tools.ListPublications(version: "V21", product: "WinCC Unified"), "WinCC Unified scripting", "product + version");
    }

    private static void SettingsEnvironment()
    {
        var env = new Dictionary<string, string> { ["SIEMENS_DOCS_HOSTS"] = " Extra.Example.com ,docs.tia.siemens.cloud", ["SIEMENS_DOCS_LOCALE"] = "de-DE", ["SIEMENS_DOCS_MIN_INTERVAL"] = "1.5" };
        var s = new DocsSettings().WithEnvironment(k => env.TryGetValue(k, out var v) ? v : null);
        Equal("docs.tia.siemens.cloud,docs.industrial-operations-x.siemens.cloud,extra.example.com", string.Join(",", s.AllHosts()), "hosts");
        Equal("de-DE", s.Locale, "locale");
        Equal(1.5, s.MinIntervalSeconds, "interval");
        env["SIEMENS_DOCS_MIN_INTERVAL"] = "-1";
        Throws<ArgumentException>(() => new DocsSettings().WithEnvironment(k => env.TryGetValue(k, out var v) ? v : null), "negative interval");
    }

    private static void ClientBackoff()
    {
        var method = typeof(FluidTopicsClient).GetMethod("BackoffSeconds", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Equal(2.0, (double)method.Invoke(null, new object?[] { null })!, "missing header");
        Equal(5.0, (double)method.Invoke(null, new object?[] { "5" })!, "seconds");
        Equal(30.0, (double)method.Invoke(null, new object?[] { "600" })!, "capped");
        Equal(2.0, (double)method.Invoke(null, new object?[] { "soon" })!, "invalid");
    }

    // ------------------------------------------------------------------
    // Live
    // ------------------------------------------------------------------

    private static DocsTools LiveTools()
    {
        var hosts = DocsSettings.DefaultHosts;
        var catalog = new DocsCatalog(hosts, h => new FluidTopicsClient(h));
        return new DocsTools(catalog, hosts[0], "en-US");
    }

    private static readonly Lazy<DocsTools> Live = new Lazy<DocsTools>(LiveTools);

    private static void LiveSearch()
    {
        string text = Live.Value.Search("OpenScreenInPopup", product: "WinCC Unified", limit: 3);
        Console.WriteLine(Indent(text));
        Contains(text, "OpenScreenInPopup", "hit title");
        Contains(text, "tia:", "compact reference");
    }

    private static void LiveListAndToc()
    {
        string pubs = Live.Value.ListPublications(titleContains: "Openness", version: "V21");
        Console.WriteLine(Indent(pubs));
        string reference = pubs.Split('\n').First(l => l.Contains(" tia:")).Split(' ').Last();
        string toc = Live.Value.GetToc(reference, depth: 1);
        Console.WriteLine(Indent(string.Join("\n", toc.Split('\n').Take(8))));
        Contains(toc, "tia:", "toc references");
    }

    private static void LiveReadPage()
    {
        string search = Live.Value.Search("SysFct.OpenScreenInPopup", product: "WinCC Unified", limit: 1);
        string reference = search.Split('\n').Last().Trim();
        string outline = Live.Value.ReadPage(reference, mode: "outline");
        Console.WriteLine(Indent(outline));
        Contains(outline, "Outline (", "outline");
        string code = Live.Value.ReadPage(reference, mode: "code");
        Console.WriteLine(Indent(string.Join("\n", code.Split('\n').Take(20))));
        Contains(code, "```", "code block");
        string full = Live.Value.ReadPage(reference, maxChars: 3000);
        Contains(full, "> Source: " + reference, "source");
    }

    private static string Indent(string text) => "     | " + text.Replace("\n", "\n     | ");
}
