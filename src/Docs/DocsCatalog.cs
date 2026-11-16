// Siemens documentation access, ported from siemens-docs-mcp (MIT, Copyright (c) 2026 Łukasz Czarnacki).
// See THIRD_PARTY_NOTICES.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TiaOpennessMcp.Docs
{
    /// <summary>Raised for unknown hosts, unresolvable URLs and unknown filter values; the message goes to the agent.</summary>
    public class DocsCatalogException : ArgumentException
    {
        public DocsCatalogException(string message) : base(message) { }
    }

    public sealed class MapInfo
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Locale { get; set; } = "";
        public string Product { get; set; } = "";
        public string Version { get; set; } = "";
        public string VersionKey { get; set; } = "";
        /// <summary>Pretty URL without the leading /r/.</summary>
        public string PrettyUrl { get; set; } = "";
    }

    /// <summary>A host + publication (map) + optional topic, as resolved from a URL or compact reference.</summary>
    public sealed class Resolved : IEquatable<Resolved>
    {
        public Resolved(string host, string mapId, string? contentId)
        {
            Host = host;
            MapId = mapId;
            ContentId = contentId;
        }

        public string Host { get; }
        public string MapId { get; }
        public string? ContentId { get; }

        public bool Equals(Resolved? other) =>
            other != null && Host == other.Host && MapId == other.MapId && ContentId == other.ContentId;

        public override bool Equals(object? obj) => Equals(obj as Resolved);
        public override int GetHashCode() => (Host + "\n" + MapId + "\n" + ContentId).GetHashCode();
        public override string ToString() => $"Resolved({Host}, {MapId}, {ContentId ?? "None"})";
    }

    /// <summary>
    /// Per-host catalog of publications: listing, canonical filter values, URL and compact-reference
    /// resolution, and a TOC cache. Everything is cached in memory for <c>ttl</c> (24 h by default).
    /// </summary>
    public sealed class DocsCatalog
    {
        public static readonly IReadOnlyDictionary<string, string> HostAliases = new Dictionary<string, string>
        {
            ["tia"] = "docs.tia.siemens.cloud",
            ["iox"] = "docs.industrial-operations-x.siemens.cloud"
        };

        public const string TiaVersionKey = "tia:SoftwareVersionFilter";
        public const string SwVersionKey = "SoftwareVersion";
        private static readonly string[] VersionKeys = { TiaVersionKey, SwVersionKey };

        private static readonly Regex ContentIdPattern = new Regex(@"^[\w~.-]+\z", RegexOptions.CultureInvariant);
        private static readonly Regex AttachmentPath = new Regex(@"^(/viewer/attachment/|/api/khub/maps/[^/]+/resources/)", RegexOptions.CultureInvariant);
        private static readonly Regex CompactRef = new Regex(@"^([\w.-]+):([\w~.-]+)(?:/([\w~.-]+))?\z", RegexOptions.CultureInvariant);

        private readonly Func<string, IFluidTopicsClient> _factory;
        private readonly TimeSpan _ttl;
        private readonly Func<TimeSpan> _clock;
        private readonly object _lock = new object();
        private readonly Dictionary<string, IFluidTopicsClient> _clients = new Dictionary<string, IFluidTopicsClient>();
        private readonly Dictionary<string, (TimeSpan Stamp, Dictionary<string, MapInfo> Maps)> _maps = new Dictionary<string, (TimeSpan, Dictionary<string, MapInfo>)>();
        // Distinct raw version values per host per key, and per host per map id per key.
        private readonly Dictionary<string, Dictionary<string, List<string>>> _versions = new Dictionary<string, Dictionary<string, List<string>>>();
        private readonly Dictionary<string, Dictionary<string, Dictionary<string, string>>> _mapVersions = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();
        private readonly Dictionary<(string, string), (TimeSpan Stamp, TocPage Root)> _tocs = new Dictionary<(string, string), (TimeSpan, TocPage)>();
        // Lower-cased pretty URLs carried by more than one map per host (ambiguous as reader URLs).
        private readonly Dictionary<string, HashSet<string>> _shared = new Dictionary<string, HashSet<string>>();

        public DocsCatalog(IEnumerable<string> hosts, Func<string, IFluidTopicsClient> clientFactory, TimeSpan? ttl = null, Func<TimeSpan>? clock = null)
        {
            Hosts = hosts.Select(h => h.ToLowerInvariant()).ToList();
            _factory = clientFactory;
            _ttl = ttl ?? TimeSpan.FromHours(24);
            if (clock == null)
            {
                var sw = Stopwatch.StartNew();
                clock = () => sw.Elapsed;
            }
            _clock = clock;
        }

        public IReadOnlyList<string> Hosts { get; }

        public string CheckHost(string host)
        {
            host = (host ?? "").ToLowerInvariant();
            if (!Hosts.Contains(host))
                throw new DocsCatalogException($"Host '{host}' is not allowed. Allowed hosts: [{string.Join(", ", Hosts.Select(h => "'" + h + "'"))}]");
            return host;
        }

        public IFluidTopicsClient Client(string host)
        {
            host = CheckHost(host);
            lock (_lock)
            {
                if (!_clients.TryGetValue(host, out var client))
                {
                    client = _factory(host);
                    _clients[host] = client;
                }
                return client;
            }
        }

        private bool Fresh(TimeSpan stamp) => _clock() - stamp <= _ttl;

        public Dictionary<string, MapInfo> Maps(string host)
        {
            host = CheckHost(host);
            lock (_lock)
            {
                if (_maps.TryGetValue(host, out var cached) && Fresh(cached.Stamp)) return cached.Maps;
                var raws = Client(host).ListMaps();
                if (raws.ValueKind != JsonValueKind.Array)
                    throw new FluidTopicsException(null, $"{host} returned an unexpected publication list ({raws.ValueKind}).");
                var infos = new Dictionary<string, MapInfo>();
                var versions = VersionKeys.ToDictionary(k => k, k => new List<string>());
                var perMap = new Dictionary<string, Dictionary<string, string>>();
                foreach (var raw in raws.EnumerateArray())
                {
                    string id = DocsToc.Str(raw, "id");
                    if (id.Length == 0) continue;
                    var meta = Meta(raw);
                    string versionKey = VersionKeys.FirstOrDefault(k => First(meta, k).Length > 0) ?? "";
                    string pretty = First(meta, "ft:prettyUrl");
                    if (pretty.StartsWith("/r/", StringComparison.Ordinal)) pretty = pretty.Substring(3);
                    infos[id] = new MapInfo
                    {
                        Id = id,
                        Title = DocsToc.Str(raw, "title"),
                        Locale = First(meta, "ft:locale"),
                        Product = First(meta, "Product"),
                        Version = versionKey.Length > 0 ? First(meta, versionKey) : "",
                        VersionKey = versionKey,
                        PrettyUrl = pretty
                    };
                    perMap[id] = new Dictionary<string, string>();
                    foreach (var key in VersionKeys)
                    {
                        string v = First(meta, key);
                        if (v.Length == 0) continue;
                        perMap[id][key] = v;
                        if (!versions[key].Contains(v)) versions[key].Add(v);
                    }
                }
                _versions[host] = versions;
                _mapVersions[host] = perMap;
                _shared[host] = new HashSet<string>(
                    infos.Values.Where(m => m.PrettyUrl.Length > 0)
                        .GroupBy(m => m.PrettyUrl.ToLowerInvariant())
                        .Where(g => g.Count() > 1)
                        .Select(g => g.Key));
                _maps[host] = (_clock(), infos);
                return infos;
            }
        }

        private static Dictionary<string, List<string>> Meta(JsonElement raw)
        {
            var result = new Dictionary<string, List<string>>();
            if (raw.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in metadata.EnumerateArray())
                {
                    string key = DocsToc.Str(m, "key");
                    var values = new List<string>();
                    if (m.TryGetProperty("values", out var vs) && vs.ValueKind == JsonValueKind.Array)
                        foreach (var v in vs.EnumerateArray())
                            if (v.ValueKind == JsonValueKind.String) values.Add(v.GetString() ?? "");
                    result[key] = values;
                }
            }
            return result;
        }

        private static string First(Dictionary<string, List<string>> meta, string key) =>
            meta.TryGetValue(key, out var values) && values.Count > 0 ? values[0] : "";

        private static DocsCatalogException Unknown(string host, string label, string value, IList<string> known)
        {
            var close = DocsText.CloseMatches(value, known, 5, 0.5);
            string hint = close.Count > 0
                ? $"Close matches: [{string.Join(", ", close.Select(c => "'" + c + "'"))}]"
                : $"Valid values include: [{string.Join(", ", known.Take(10).Select(c => "'" + c + "'"))}]";
            return new DocsCatalogException($"Unknown {label} '{value}' on {host}. {hint}");
        }

        /// <summary>The catalog's spelling of a product or locale value (case-insensitive match).</summary>
        public string Canonical(string host, string field, string value)
        {
            var values = Maps(host).Values
                .Select(m => field == "product" ? m.Product : m.Locale)
                .Where(v => v.Length > 0)
                .Distinct()
                .OrderBy(v => v, StringComparer.Ordinal)
                .ToList();
            foreach (var v in values)
                if (string.Equals(v, value, StringComparison.OrdinalIgnoreCase)) return v;
            throw Unknown(host, field, value, values);
        }

        public (string Key, string Value) CanonicalVersion(string host, string value)
        {
            Maps(host);
            var versions = _versions[host.ToLowerInvariant()];
            foreach (var key in VersionKeys)
                foreach (var v in versions[key])
                    if (string.Equals(v, value, StringComparison.OrdinalIgnoreCase)) return (key, v);
            var known = versions.Values.SelectMany(v => v).Distinct().OrderBy(v => v, StringComparer.Ordinal).ToList();
            throw Unknown(host, "version", value, known);
        }

        public List<MapInfo> Filter(string host, string? product = null, string? version = null, string? locale = null, string? titleContains = null)
        {
            IEnumerable<MapInfo> result = Maps(host).Values.ToList();
            if (product != null)
            {
                string p = Canonical(host, "product", product);
                result = result.Where(m => m.Product == p);
            }
            if (locale != null)
            {
                string loc = Canonical(host, "locale", locale);
                result = result.Where(m => m.Locale == loc);
            }
            if (version != null)
            {
                var (key, v) = CanonicalVersion(host, version);
                var perMap = _mapVersions[host.ToLowerInvariant()];
                result = result.Where(m => perMap.TryGetValue(m.Id, out var mv) && mv.TryGetValue(key, out var x) && x == v);
            }
            if (!string.IsNullOrEmpty(titleContains))
            {
                string needle = titleContains!.ToLowerInvariant();
                result = result.Where(m => m.Title.ToLowerInvariant().Contains(needle));
            }
            return result.OrderBy(m => m.Title, StringComparer.Ordinal).ToList();
        }

        public TocPage Toc(string host, string mapId)
        {
            var key = (CheckHost(host), mapId);
            lock (_lock)
            {
                if (_tocs.TryGetValue(key, out var cached) && Fresh(cached.Stamp)) return cached.Root;
                var root = DocsToc.Parse(Client(host).GetPages(mapId));
                _tocs[key] = (_clock(), root);
                return root;
            }
        }

        /// <summary>Resolves a compact reference (tia:mapId/contentId) or a reader URL to host, map and topic.</summary>
        public Resolved Resolve(string url)
        {
            var failure = new DocsCatalogException(
                $"Could not resolve {url}. Use docs_search or docs_list_publications to find a valid reader URL.");
            string value = (url ?? "").Trim();
            if (!value.Contains("://") && !value.StartsWith("//", StringComparison.Ordinal))
            {
                var target = ParseCompactRef(value);
                if (!Maps(target.Host).ContainsKey(target.MapId)) throw failure;
                return target;
            }
            if (value.StartsWith("//", StringComparison.Ordinal)) value = "https:" + value;
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) throw failure;
            string host = CheckHost(uri.Host);
            string path = Uri.UnescapeDataString(uri.AbsolutePath).TrimEnd('/');
            if (!path.StartsWith("/r/", StringComparison.Ordinal)) throw failure;
            string rel = path.Substring(3);
            var segs = rel.Split('/');
            var maps = Maps(host);
            if (maps.ContainsKey(segs[0]))
            {
                if (segs.Length == 1) return new Resolved(host, segs[0], null);
                if (!ContentIdPattern.IsMatch(segs[1]) || segs[1] == "." || segs[1] == "..") throw failure;
                return new Resolved(host, segs[0], segs[1]);
            }
            string low = rel.ToLowerInvariant();
            // Several maps can share a pretty URL, and a longer prefix may lack the topic: try every
            // matching map, longest prefix first (stable on ties), until one's TOC holds the topic.
            var matches = maps.Values
                .Where(m => m.PrettyUrl.Length > 0
                            && (low == m.PrettyUrl.ToLowerInvariant()
                                || (low + "/").StartsWith(m.PrettyUrl.ToLowerInvariant() + "/", StringComparison.Ordinal)))
                .Select((m, i) => (m, i))
                .OrderByDescending(x => x.m.PrettyUrl.Length)
                .ThenBy(x => x.i)
                .Select(x => x.m)
                .ToList();
            string targetPath = "/r/" + low;
            foreach (var m in matches)
            {
                if (low == m.PrettyUrl.ToLowerInvariant()) return new Resolved(host, m.Id, null);
                foreach (var page in DocsToc.IterPages(Toc(host, m.Id)))
                    if (page.PrettyUrl.ToLowerInvariant() == targetPath) return new Resolved(host, m.Id, page.ContentId);
            }
            throw failure;
        }

        private Resolved ParseCompactRef(string value)
        {
            var match = CompactRef.Match(value);
            if (!match.Success
                || match.Groups[2].Value == "." || match.Groups[2].Value == ".."
                || (match.Groups[3].Success && (match.Groups[3].Value == "." || match.Groups[3].Value == "..")))
            {
                throw new DocsCatalogException(
                    $"Invalid compact reference '{value}'. Use <prefix>:<mapId>[/<contentId>]; find references with docs_search or docs_list_publications.");
            }
            string prefix = match.Groups[1].Value;
            string host = CheckHost(HostAliases.TryGetValue(prefix.ToLowerInvariant(), out var aliased) ? aliased : prefix);
            return new Resolved(host, match.Groups[2].Value, match.Groups[3].Success ? match.Groups[3].Value : null);
        }

        private Dictionary<string, MapInfo>? LoadedMaps(string host)
        {
            lock (_lock)
            {
                return _maps.TryGetValue(host, out var cached) && Fresh(cached.Stamp) ? cached.Maps : null;
            }
        }

        private static Uri? SplitBodyUrl(string url)
        {
            return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ? uri : null;
        }

        /// <summary>
        /// Compact reference for a page-body link to /r/&lt;mapId&gt;[/&lt;contentId&gt;], or null to leave it alone.
        /// No network: the map id must already be in a loaded publication list. Links with a query or
        /// fragment are left alone so in-page anchors are not lost. With <paramref name="host"/>, only links to that host change.
        /// </summary>
        public string? CompactLink(string url, string? host = null)
        {
            var parts = SplitBodyUrl(url);
            if (parts == null) return null;
            string linkHost = parts.Host.ToLowerInvariant();
            if ((parts.Scheme != "http" && parts.Scheme != "https") || parts.Query.Length > 1 || parts.Fragment.Length > 1
                || !Hosts.Contains(linkHost) || (host != null && linkHost != host.ToLowerInvariant()))
                return null;
            var segs = Uri.UnescapeDataString(parts.AbsolutePath).TrimEnd('/').Split('/').Skip(1).ToList();
            var maps = LoadedMaps(linkHost);
            if (maps == null || (segs.Count != 2 && segs.Count != 3) || segs[0] != "r") return null;
            var ids = segs.Skip(1).ToList();
            if (ids.Any(i => !ContentIdPattern.IsMatch(i) || i == "." || i == "..") || !maps.ContainsKey(ids[0])) return null;
            return CompactRefOf(new Resolved(linkHost, ids[0], ids.Count == 2 ? ids[1] : null));
        }

        /// <summary>True for image/attachment URLs on an allowed host.</summary>
        public bool IsAttachmentLink(string url)
        {
            var parts = SplitBodyUrl(url);
            return parts != null && Hosts.Contains(parts.Host.ToLowerInvariant()) && AttachmentPath.IsMatch(parts.AbsolutePath);
        }

        public string CompactRefOf(Resolved target)
        {
            string host = CheckHost(target.Host);
            string prefix = HostAliases.FirstOrDefault(kv => kv.Value == host).Key ?? host;
            string value = $"{prefix}:{target.MapId}{(target.ContentId == null ? "" : "/" + target.ContentId)}";
            if (!ParseCompactRef(value).Equals(new Resolved(host, target.MapId, target.ContentId)))
                throw new DocsCatalogException($"Invalid compact target {target}");
            return value;
        }

        public string IdUrl(Resolved target)
        {
            string host = CheckHost(target.Host);
            return $"https://{host}/r/{target.MapId}{(target.ContentId == null ? "" : "/" + target.ContentId)}";
        }

        /// <summary>A reader URL from a search hit, if it is an https URL on an allowed host; otherwise null.</summary>
        public string? FallbackReaderUrl(string? value)
        {
            if (value == null) return null;
            value = value.Trim();
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return null;
            if (uri.Scheme != "https" || !Hosts.Contains(uri.Host.ToLowerInvariant()) || uri.UserInfo.Length > 0
                || !uri.AbsolutePath.StartsWith("/r/", StringComparison.Ordinal) || uri.AbsolutePath.Substring(3).Trim('/').Length == 0)
                return null;
            return value;
        }

        private string PrettyOk(string host, string mapId)
        {
            if (!Maps(host).TryGetValue(mapId, out var info) || info.PrettyUrl.Length == 0
                || _shared[host.ToLowerInvariant()].Contains(info.PrettyUrl.ToLowerInvariant()))
                return "";
            return info.PrettyUrl;
        }

        /// <summary>Reader URL of a publication: its pretty URL, or the id form when missing or shared.</summary>
        public string MapUrl(string host, string mapId)
        {
            host = CheckHost(host);
            string pretty = PrettyOk(host, mapId);
            return pretty.Length > 0 ? $"https://{host}/r/{pretty}" : IdUrl(new Resolved(host, mapId, null));
        }

        public string ReaderUrl(string host, string mapId, TocPage page)
        {
            host = CheckHost(host);
            if (page.ContentId.Length == 0) return MapUrl(host, mapId); // synthetic root = the publication itself
            if (page.PrettyUrl.Length > 0 && PrettyOk(host, mapId).Length > 0) return $"https://{host}{page.PrettyUrl}";
            return IdUrl(new Resolved(host, mapId, page.ContentId));
        }
    }
}
