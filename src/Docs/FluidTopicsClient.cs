// Siemens documentation access, ported from siemens-docs-mcp (MIT, Copyright (c) 2026 Łukasz Czarnacki).
// See THIRD_PARTY_NOTICES.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace TiaOpennessMcp.Docs
{
    /// <summary>A request to a Fluid Topics host failed; <see cref="Status"/> is null when no HTTP status applies.</summary>
    public class FluidTopicsException : Exception
    {
        public int? Status { get; }

        public FluidTopicsException(int? status, string message, Exception? inner = null) : base(message, inner)
        {
            Status = status;
        }
    }

    /// <summary>The calls the catalog and tools make to one Fluid Topics host (an interface so tests can fake it).</summary>
    public interface IFluidTopicsClient
    {
        string Host { get; }
        JsonElement ListMaps();
        JsonElement GetPages(string mapId);
        string GetContent(string mapId, string contentId);
        JsonElement Search(string query, string locale, IList<SearchFilter>? filters, int page, int perPage);
    }

    public sealed class SearchFilter
    {
        public SearchFilter(string key, params string[] values)
        {
            Key = key;
            Values = values;
        }

        public string Key { get; }
        public string[] Values { get; }
    }

    /// <summary>
    /// HTTP client for one Fluid Topics host (docs.tia.siemens.cloud and friends).
    /// Gets the anonymous session cookie the /api/khub endpoints need, throttles every request to the host,
    /// and retries once on session expiry (401/403), 429 and 5xx.
    /// </summary>
    public sealed class FluidTopicsClient : IFluidTopicsClient, IDisposable
    {
        private const string SessionPath = "/internal/api/webapp/authentication/session";
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan MapsTimeout = TimeSpan.FromSeconds(120);
        private const double BackoffDefault = 2.0;
        private const double BackoffMax = 30.0;

        private readonly string _base;
        private readonly TimeSpan _minInterval;
        private readonly HttpClient _http;
        private readonly object _throttleLock = new object();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly Action<TimeSpan> _sleep;
        private TimeSpan? _last;
        private volatile bool _sessionEstablished;

        public FluidTopicsClient(string host, double minIntervalSeconds = 0.3, HttpMessageHandler? handler = null, Action<TimeSpan>? sleep = null)
        {
            Host = host;
            _base = "https://" + host;
            _minInterval = TimeSpan.FromSeconds(minIntervalSeconds);
            _sleep = sleep ?? Thread.Sleep;
            if (handler == null)
            {
                // docs.tia.siemens.cloud accepts only TLS 1.3. SystemDefault (the .NET 4.7+ default) lets Windows
                // negotiate it; if something pinned explicit versions, add 1.2 and 1.3 rather than replace them.
                if (ServicePointManager.SecurityProtocol != SecurityProtocolType.SystemDefault)
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)12288; // 12288 = Tls13
                handler = new HttpClientHandler
                {
                    CookieContainer = new CookieContainer(),
                    UseCookies = true,
                    AllowAutoRedirect = true,
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                };
            }
            _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "TiaOpennessMcp-docs/1.0");
            _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/html, */*");
        }

        public string Host { get; }

        public JsonElement ListMaps() => ParseJson(Request(HttpMethod.Get, "/api/khub/maps", null, MapsTimeout), "/api/khub/maps");

        public JsonElement GetPages(string mapId)
        {
            string path = $"/api/khub/maps/{Uri.EscapeDataString(mapId)}/pages";
            return ParseJson(Request(HttpMethod.Get, path, null, DefaultTimeout), path);
        }

        public string GetContent(string mapId, string contentId)
        {
            string path = $"/api/khub/maps/{Uri.EscapeDataString(mapId)}/topics/{Uri.EscapeDataString(contentId)}/content?target=DESIGNED_READER";
            return Request(HttpMethod.Get, path, null, DefaultTimeout);
        }

        public JsonElement Search(string query, string locale, IList<SearchFilter>? filters, int page, int perPage)
        {
            var body = new Dictionary<string, object>
            {
                ["query"] = query,
                ["contentLocale"] = locale,
                ["paging"] = new Dictionary<string, int> { ["page"] = page, ["perPage"] = perPage }
            };
            if (filters != null && filters.Count > 0)
            {
                var list = new List<Dictionary<string, object>>();
                foreach (var f in filters)
                    list.Add(new Dictionary<string, object> { ["key"] = f.Key, ["values"] = f.Values });
                body["filters"] = list;
            }
            const string path = "/api/khub/clustered-search";
            return ParseJson(Request(HttpMethod.Post, path, JsonSerializer.Serialize(body), DefaultTimeout), path);
        }

        public void Dispose() => _http.Dispose();

        private static JsonElement ParseJson(string text, string path)
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                return doc.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                throw new FluidTopicsException(null, $"{path} returned invalid JSON: {ex.Message}", ex);
            }
        }

        private (int Status, string Body, string? RetryAfter) Send(HttpMethod method, string path, string? jsonBody, TimeSpan timeout)
        {
            lock (_throttleLock)
            {
                if (_last.HasValue)
                {
                    var wait = _last.Value + _minInterval - _clock.Elapsed;
                    if (wait > TimeSpan.Zero) _sleep(wait);
                }
                _last = _clock.Elapsed;
            }
            using var request = new HttpRequestMessage(method, _base + path);
            if (jsonBody != null) request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                using var response = _http.SendAsync(request, cts.Token).GetAwaiter().GetResult();
                string body = response.Content == null ? "" : response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                string? retryAfter = null;
                if (response.Headers.RetryAfter != null)
                {
                    retryAfter = response.Headers.RetryAfter.Delta.HasValue
                        ? response.Headers.RetryAfter.Delta.Value.TotalSeconds.ToString(CultureInfo.InvariantCulture)
                        : null;
                }
                return ((int)response.StatusCode, body, retryAfter);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException || ex is WebException)
            {
                string reason = ex is OperationCanceledException ? $"timed out after {timeout.TotalSeconds:0}s" : ex.Message;
                for (var inner = ex.InnerException; inner != null && ex is not OperationCanceledException; inner = inner.InnerException)
                    reason += " -> " + inner.Message;
                throw new FluidTopicsException(null, $"network error contacting {Host}: {reason}", ex);
            }
        }

        private void EnsureSession()
        {
            if (_sessionEstablished) return;
            var resp = Send(HttpMethod.Get, SessionPath, null, DefaultTimeout);
            if (!IsSuccess(resp.Status))
                throw new FluidTopicsException(resp.Status, $"GET {SessionPath} -> HTTP {resp.Status}");
            _sessionEstablished = true;
        }

        private string Request(HttpMethod method, string path, string? jsonBody, TimeSpan timeout)
        {
            EnsureSession();
            var resp = Send(method, path, jsonBody, timeout);
            if (resp.Status == 401 || resp.Status == 403)
            {
                _sessionEstablished = false;
                EnsureSession();
                resp = Send(method, path, jsonBody, timeout);
            }
            else if (resp.Status == 429 || resp.Status >= 500)
            {
                _sleep(TimeSpan.FromSeconds(BackoffSeconds(resp.RetryAfter)));
                resp = Send(method, path, jsonBody, timeout);
            }
            if (!IsSuccess(resp.Status))
                throw new FluidTopicsException(resp.Status, $"{method.Method} {path} -> HTTP {resp.Status}");
            return resp.Body;
        }

        internal static double BackoffSeconds(string? retryAfter)
        {
            if (!double.TryParse(retryAfter, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                || double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                return BackoffDefault;
            return Math.Min(value, BackoffMax);
        }

        private static bool IsSuccess(int status) => status >= 200 && status < 300;
    }
}
