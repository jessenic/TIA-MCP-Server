using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using S7CommPlusDriver;
using S7CommPlusDriver.ClientApi;

namespace TiaOpennessMcp.PlcOnline
{
    /// <summary>
    /// Read-only online access to a real S7-1200/S7-1500 PLC over S7CommPlus, using the
    /// DotNetProjects.S7CommPlusDriver package (LGPL-3.0): TLS for TIA Portal V17+ projects, and HarpoS7-based
    /// legacy challenge login for older ones. One client is kept open across tool calls, like the TIA and PLCSIM
    /// connections. The client is created with writes disabled and this class exposes no write or
    /// operating-state functions.
    /// </summary>
    public sealed class PlcOnlineManager
    {
        public const int MaxTagsPerRead = 200;
        public const int MaxBrowseRows = 1000;

        private static readonly Lazy<PlcOnlineManager> LazyInstance = new Lazy<PlcOnlineManager>(() => new PlcOnlineManager());
        public static PlcOnlineManager Instance => LazyInstance.Value;

        /// <summary>Closes an open PLC connection at server shutdown.</summary>
        public static void ShutdownIfOpen()
        {
            if (!LazyInstance.IsValueCreated) return;
            try
            {
                LazyInstance.Value.Disconnect();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[PlcOnline] Shutdown disconnect failed: {ex.Message}");
            }
        }

        private readonly object _lock = new object();
        private S7CommPlusClient? _client;
        private string? _address;
        private DateTime? _connectedAt;
        private string? _lastError;
        private S7CommPlusCpuInfo? _cpuInfo;
        private IReadOnlyList<VarInfo>? _browseCache;

        public object Connect(string address, string? password, string? username, int timeoutMs, string? securityMode = null)
        {
            if (string.IsNullOrWhiteSpace(address)) throw new ArgumentException("address is required.");
            if (timeoutMs < 1000 || timeoutMs > 60000) throw new ArgumentException("timeout_ms must be 1000-60000.");
            var mode = ParseSecurityMode(securityMode);
            address = address.Trim();
            lock (_lock)
            {
                if (_client != null)
                {
                    if (string.Equals(_address, address, StringComparison.OrdinalIgnoreCase) && _client.IsConnected)
                        return Status("Already connected.");
                    CloseLocked();
                }
                var timeout = TimeSpan.FromMilliseconds(timeoutMs);
                var client = new S7CommPlusClient(new S7CommPlusClientOptions
                {
                    Address = address,
                    Password = password ?? "",
                    Username = username ?? "",
                    ConnectTimeout = timeout,
                    RequestTimeout = timeout,
                    BrowseTimeout = TimeSpan.FromMilliseconds(Math.Max(timeoutMs, 60000)),
                    AutoReconnect = true,
                    WriteEnabled = false,
                    SecurityMode = mode
                });
                try
                {
                    Run(client.ConnectAsync());
                }
                catch (Exception ex)
                {
                    DisposeQuietly(client);
                    _lastError = $"Connect to {address} failed: {Describe(ex)}";
                    throw new InvalidOperationException(_lastError +
                        " The PLC must be an S7-1200 or S7-1500 reachable on TCP port 102." +
                        (string.IsNullOrEmpty(password) ? " If the CPU's access protection requires a password for reading, pass password." : ""), ex);
                }
                _client = client;
                _address = address;
                _connectedAt = DateTime.Now;
                _lastError = null;
                _browseCache = null;
                try
                {
                    _cpuInfo = Run(client.GetCpuInfoAsync());
                }
                catch (Exception ex)
                {
                    _cpuInfo = null;
                    Console.Error.WriteLine($"[PlcOnline] CPU information not available: {ex.Message}");
                }
                return Status("Connected.");
            }
        }

        private static S7CommPlusSecurityMode ParseSecurityMode(string? value)
        {
            switch ((value ?? "auto").Trim().ToLowerInvariant())
            {
                case "":
                case "auto": return S7CommPlusSecurityMode.Auto;
                case "tls": return S7CommPlusSecurityMode.Tls;
                case "legacy": return S7CommPlusSecurityMode.LegacyChallenge;
                default: throw new ArgumentException("security_mode must be 'auto', 'tls' or 'legacy'.");
            }
        }

        public object Disconnect()
        {
            lock (_lock)
            {
                if (_client == null) return Status("Not connected.");
                CloseLocked();
                return Status("Disconnected.");
            }
        }

        public object GetStatus()
        {
            lock (_lock)
            {
                string? operatingState = null;
                if (_client != null && _client.IsConnected)
                {
                    try
                    {
                        operatingState = Run(_client.GetCpuStateAsync()).OperatingState.ToString();
                    }
                    catch (Exception ex)
                    {
                        operatingState = "unknown (" + Describe(ex) + ")";
                    }
                }
                return Status(null, operatingState);
            }
        }

        private object Status(string? message, string? operatingState = null) => new
        {
            message,
            connected = _client != null && _client.IsConnected,
            state = _client?.State.ToString(),
            address = _address,
            securityMode = _client?.Options.NegotiatedSecurityMode?.ToString(),
            connectedAt = _connectedAt?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            cpu = _cpuInfo == null ? null : new
            {
                plcName = _cpuInfo.PlcName,
                projectName = _cpuInfo.ProjectName,
                orderNumber = _cpuInfo.CpuMlfb,
                firmware = _cpuInfo.CpuFirmware?.ToString(),
                tiaVersion = _cpuInfo.VersionTia?.ToString()
            },
            operatingState,
            browsedVariables = _browseCache?.Count,
            lastError = _lastError
        };

        private void CloseLocked()
        {
            var client = _client;
            _client = null;
            _address = null;
            _connectedAt = null;
            _cpuInfo = null;
            _browseCache = null;
            if (client == null) return;
            try
            {
                Run(client.DisconnectAsync());
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[PlcOnline] Disconnect error ignored: {ex.Message}");
            }
            DisposeQuietly(client);
        }

        private static void DisposeQuietly(S7CommPlusClient client)
        {
            try
            {
                Run(client.DisposeAsync().AsTask());
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[PlcOnline] Dispose error ignored: {ex.Message}");
            }
        }

        private S7CommPlusClient RequireConnection()
        {
            return _client ?? throw new InvalidOperationException("Not connected to a PLC. Call plc_online_connect with the PLC's IP address first.");
        }

        public object ListDatablocks()
        {
            lock (_lock)
            {
                var client = RequireConnection();
                var blocks = Call("Reading the block list", () => Run(client.BrowseBlocksAsync()));
                var dbs = blocks.Where(b => b.Type == S7CommPlusBlockType.DB).OrderBy(b => b.Number).ToList();
                return new
                {
                    address = _address,
                    count = dbs.Count,
                    datablocks = dbs.Select(d => new { number = d.Number, name = d.Name }).ToList()
                };
            }
        }

        /// <summary>Lists browsed variables (data blocks, inputs, outputs, memory), filtered by name text.</summary>
        public object Browse(string? filter, int offset, int limit, bool refresh)
        {
            if (offset < 0) throw new ArgumentException("offset must be >= 0.");
            if (limit < 1 || limit > MaxBrowseRows) throw new ArgumentException($"limit must be 1-{MaxBrowseRows}.");
            lock (_lock)
            {
                var client = RequireConnection();
                if (_browseCache == null || refresh)
                {
                    if (refresh) Call("Refreshing the symbol catalog", () => { Run(client.InvalidateSymbolCatalogAsync()); return true; });
                    _browseCache = Call("Browsing the PLC variables", () => Run(client.BrowseAsync()));
                }
                IEnumerable<VarInfo> matches = _browseCache;
                if (!string.IsNullOrWhiteSpace(filter))
                {
                    string needle = filter!.Trim();
                    matches = matches.Where(v => v.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);
                }
                var list = matches.ToList();
                return new
                {
                    address = _address,
                    filter,
                    total = list.Count,
                    offset,
                    returned = Math.Max(0, Math.Min(limit, list.Count - offset)),
                    nextOffset = offset + limit < list.Count ? offset + limit : (int?)null,
                    variables = list.Skip(offset).Take(limit).Select(v => new
                    {
                        name = v.Name,
                        type = TypeName(v.Softdatatype),
                        array = ArrayBounds(v)
                    }).ToList()
                };
            }
        }

        /// <summary>Reads current values of variables given by their full symbolic names.</summary>
        public object ReadTags(IList<string> names)
        {
            if (names == null || names.Count == 0) throw new ArgumentException("tags must contain at least one symbolic name.");
            if (names.Count > MaxTagsPerRead) throw new ArgumentException($"At most {MaxTagsPerRead} tags per call.");
            lock (_lock)
            {
                var client = RequireConnection();
                var rows = names.Select(n => new Dictionary<string, object?> { ["name"] = (n ?? "").Trim() }).ToList();
                var wanted = rows.Select(r => (string)r["name"]!).Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).ToList();
                var resolved = wanted.Count == 0
                    ? new Dictionary<string, PlcTag>()
                    : Call("Resolving the symbols", () => Run(client.GetTagsBySymbolsAsync(wanted)));

                var tags = new List<PlcTag>();
                foreach (var row in rows)
                {
                    string name = (string)row["name"]!;
                    if (name.Length == 0)
                    {
                        row["error"] = "Empty tag name.";
                    }
                    else if (!resolved.TryGetValue(name, out var tag) || tag == null)
                    {
                        row["error"] = "Symbol not found in the PLC. Use plc_online_browse to find the exact name (data block variables are 'DB_Name.Member').";
                    }
                    else if (!tags.Contains(tag))
                    {
                        tags.Add(tag);
                    }
                }

                var errors = new Dictionary<PlcTag, ulong>();
                if (tags.Count > 0)
                {
                    var result = Call("Reading the values", () => Run(client.ReadAsync(tags)));
                    foreach (var item in result.Items) errors[item.Tag] = item.ItemError;
                }

                foreach (var row in rows)
                {
                    if (row.ContainsKey("error")) continue;
                    var tag = resolved[(string)row["name"]!];
                    row["type"] = TypeName(tag.Datatype);
                    errors.TryGetValue(tag, out ulong itemError);
                    bool good = itemError == 0 && (tag.Quality & PlcTagQC.TAG_QUALITY_MASK) == PlcTagQC.TAG_QUALITY_GOOD;
                    row["quality"] = good ? "good" : $"bad (0x{tag.Quality:X2})";
                    if (good) row["value"] = JsonValue(tag);
                    else row["error"] = itemError != 0 ? $"PLC returned error 0x{itemError:X}" : "No valid value returned.";
                }
                return new
                {
                    address = _address,
                    readAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                    tags = rows
                };
            }
        }

        /// <summary>The tag's typed Value property in a JSON-friendly form; byte arrays become number lists.</summary>
        public static object? JsonValue(PlcTag tag)
        {
            var property = tag.GetType().GetProperty("Value");
            object? value = property?.GetValue(tag);
            if (value == null) return StripQuality(tag.ToString());
            switch (value)
            {
                case byte[] bytes:
                    return bytes.Select(b => (int)b).ToArray();
                case DateTime dt:
                    return dt.ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture);
                case TimeSpan ts:
                    return ts.ToString();
                case string _:
                case bool _:
                case IConvertible _:
                    return value;
                case IEnumerable sequence:
                    return sequence.Cast<object?>().Select(o => o is IConvertible ? o : o?.ToString()).ToArray();
                default:
                    return StripQuality(tag.ToString());
            }
        }

        /// <summary>PlcTag.ToString() prefixes the quality code ("C0: value"); drop it.</summary>
        public static string StripQuality(string text)
        {
            int colon = text.IndexOf(": ", StringComparison.Ordinal);
            return colon == 2 ? text.Substring(4) : text;
        }

        /// <summary>Declared array bounds such as "[0..9]" or "[1..3, 0..7]"; null for scalars.</summary>
        private static string? ArrayBounds(VarInfo v)
        {
            var dims = v.ArrayDimensions;
            if (dims == null || dims.Count == 0) return null;
            return "[" + string.Join(", ", dims.Select(d => $"{d.LowerBound}..{d.LowerBound + (long)d.ElementCount - 1}")) + "]";
        }

        private static string TypeName(uint softdatatype) =>
            Softdatatype.Types.TryGetValue(softdatatype, out var name) ? name : $"type {softdatatype}";

        private T Call<T>(string what, Func<T> operation)
        {
            try
            {
                return operation();
            }
            catch (Exception ex) when (!(ex is ArgumentException))
            {
                _lastError = $"{what} failed: {Describe(ex)}";
                throw new InvalidOperationException(_lastError + " If the PLC connection was lost, call plc_online_connect again.", ex);
            }
        }

        /// <summary>A one-line description of a driver failure, including its error code and whether a retry may help.</summary>
        public static string Describe(Exception ex)
        {
            if (ex is AggregateException agg && agg.InnerExceptions.Count == 1) ex = agg.InnerExceptions[0];
            if (ex is S7CommPlusException s7)
                return $"{s7.Message} (operation {s7.Operation}, error 0x{s7.ErrorCode:X8}{(s7.IsTransient ? ", transient" : "")})";
            return ex.Message;
        }

        private static T Run<T>(Task<T> task) => task.GetAwaiter().GetResult();
        private static void Run(Task task) => task.GetAwaiter().GetResult();
    }
}
