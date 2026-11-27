// Offline tests for the runtime cluster: UTF-8 stdio/HTTP transport (#3), the SCL source encoding (#3)
// and the S7-PLCSIM Advanced runtime diagnosis (#21). Compiled by Test-Runtime.ps1 against the built exe.
// Never attaches to TIA Portal and never calls PLCSIM: the PLCSIM Advanced API assembly is deliberately
// not resolvable in this process.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using TiaOpennessMcp;
using TiaOpennessMcp.Server;
using TiaOpennessMcp.Tia;
using TiaOpennessMcp.Tools;

internal static class RuntimeTests
{
    private const string Unicode = "Säiliön_lämpö_°C_€_Å";
    private static int _passed;
    private static int _failed;
    private static string _serverExe = "";
    private static string _apiDirectory = "";

    private static int Main(string[] args)
    {
        _serverExe = args[0];
        _apiDirectory = args[1];
        AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;

        Run("stdio: UTF-8 arguments arrive intact through TiaOpennessMcp.exe --stdio", StdioProcessRoundTrip);
        Run("stdio: Run(reader, writer) writes to its own writer on every call", StdioInProcessRoundTrip);
        Run("http: request encoding defaults to UTF-8 and honours a known charset", HttpRequestEncoding);
        Run("http: raw UTF-8 bodies arrive intact through HttpListener (direct JSON-RPC and SSE /messages)", HttpListenerRoundTrip);
        Run("scl: external source file is UTF-8 with BOM", SclSourceEncoding);
        Run("plcsim: API folder selection accepts 8.0+ only", PlcSimApiFolderSelection);
        Run("plcsim: diagnosis messages", PlcSimDiagnosis);
        Run("plcsim: runtime errors are reported, not swallowed", PlcSimListResultErrors);
        Run("plcsim: list result JSON shape", PlcSimListResultJson);
        Run("plcsim: apiVersion is the API folder version, apiAssemblyVersion the assembly version", PlcSimApiVersionFields);
        Run("plcsim: tools without the PLCSIM Advanced API return a diagnosis, not a load error", PlcSimToolsWithoutApi);
        Run("plcsim: descriptions state Advanced-only support", PlcSimDescriptions);

        Console.WriteLine($"\n{_passed} passed, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }

    // Siemens.Engineering reference assemblies resolve from src\lib so ToolRegistry loads; the PLCSIM
    // Advanced API is never resolved, so no test can reach a simulator.
    private static Assembly? ResolveAssembly(object? sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name ?? "";
        if (name.StartsWith("Siemens.Simatic.Simulation", StringComparison.OrdinalIgnoreCase)) return null;
        string path = Path.Combine(_apiDirectory, name + ".dll");
        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
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
            Console.WriteLine("FAIL " + name + "\n     " + ex.ToString().Replace("\n", "\n     "));
        }
    }

    private static void True(bool condition, string what)
    {
        if (!condition) throw new Exception(what);
    }

    private static void Equal<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"{what}: expected <{expected}>, got <{actual}>");
    }

    private static string ToolCall(int id, string name, object arguments) =>
        JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method = "tools/call", @params = new { name, arguments } },
            new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    private static Dictionary<int, JsonElement> ParseResponses(string output)
    {
        var responses = new Dictionary<int, JsonElement>();
        foreach (string line in output.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement.Clone();
            if (root.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.Number)
                responses[id.GetInt32()] = root;
        }
        return responses;
    }

    private static string ToolText(JsonElement response) =>
        response.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString() ?? "";

    private static void StdioProcessRoundTrip()
    {
        string work = Path.Combine(Path.GetTempPath(), "tia-runtime-stdio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            string missingInput = Path.Combine(work, "puuttuu_" + Unicode);
            string output = Path.Combine(work, "ulos_" + Unicode);
            string unknownTool = "työkalu_" + Unicode;
            var lines = new[]
            {
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{}}",
                ToolCall(2, unknownTool, new { }),
                ToolCall(3, "hmi_render_json", new { input_directory = missingInput, output_directory = output }),
                "{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"metodi_" + Unicode + "\",\"params\":{}}"
            };
            // Raw UTF-8 bytes as Node-based MCP clients send them (no \u escapes).
            byte[] input = new UTF8Encoding(false).GetBytes(string.Join("\n", lines) + "\n");
            True(input.Any(b => b >= 0x80), "the request bytes must contain raw non-ASCII UTF-8");

            var start = new ProcessStartInfo(_serverExe, "--stdio")
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = work
            };
            string stdout;
            using (Process process = Process.Start(start)!)
            {
                var stderrTask = process.StandardError.ReadToEndAsync();
                var stdoutBytes = new MemoryStream();
                var copyTask = process.StandardOutput.BaseStream.CopyToAsync(stdoutBytes);
                process.StandardInput.BaseStream.Write(input, 0, input.Length);
                process.StandardInput.BaseStream.Flush();
                process.StandardInput.Close();
                if (!process.WaitForExit(60000))
                {
                    try { process.Kill(); } catch { }
                    throw new Exception("TiaOpennessMcp.exe --stdio did not exit after stdin closed.");
                }
                copyTask.Wait(10000);
                stderrTask.Wait(10000);
                byte[] raw = stdoutBytes.ToArray();
                stdout = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(raw);
                True(!(raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF), "stdout must not start with a BOM");
            }

            Dictionary<int, JsonElement> responses = ParseResponses(stdout);
            True(responses.ContainsKey(1), "initialize response missing. stdout:\n" + stdout);
            True(responses.ContainsKey(2) && responses.ContainsKey(3) && responses.ContainsKey(4), "responses missing. stdout:\n" + stdout);

            Equal($"Unknown tool: '{unknownTool}'", ToolText(responses[2]), "unknown tool name echoed");
            string renderError = ToolText(responses[3]);
            True(responses[3].GetProperty("result").GetProperty("isError").GetBoolean(), "hmi_render_json on a missing directory is an error");
            True(renderError.Contains(missingInput), "hmi_render_json error must echo the input_directory argument intact, got: " + renderError);
            True(!Directory.Exists(output), "hmi_render_json must not create the output directory when the input is missing");
            Equal($"Method 'metodi_{Unicode}' not found.", responses[4].GetProperty("error").GetProperty("message").GetString(),
                "unknown method name echoed");
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { }
        }
    }

    private static void StdioInProcessRoundTrip()
    {
        var server = new McpServer(new ToolRegistry());
        for (int round = 1; round <= 2; round++)
        {
            string tool = $"kierros{round}_{Unicode}";
            byte[] bytes = new UTF8Encoding(false).GetBytes(ToolCall(round, tool, new { arvo = "ä" }) + "\n");
            var writer = new StringWriter();
            using (TextReader reader = McpServer.CreateStdioReader(new MemoryStream(bytes)))
                server.Run(reader, writer);
            Dictionary<int, JsonElement> responses = ParseResponses(writer.ToString());
            True(responses.ContainsKey(round), $"round {round}: response must go to this call's writer, got: {writer}");
            Equal($"Unknown tool: '{tool}'", ToolText(responses[round]), $"round {round} tool name");
        }

        // A UTF-8 BOM on stdin is tolerated.
        byte[] withBom = new UTF8Encoding(true).GetPreamble()
            .Concat(new UTF8Encoding(false).GetBytes(ToolCall(7, "bom_" + Unicode, new { }) + "\n")).ToArray();
        var bomWriter = new StringWriter();
        using (TextReader reader = McpServer.CreateStdioReader(new MemoryStream(withBom)))
            server.Run(reader, bomWriter);
        Equal($"Unknown tool: 'bom_{Unicode}'", ToolText(ParseResponses(bomWriter.ToString())[7]), "BOM input");

        var buffer = new MemoryStream();
        using (TextWriter writer = McpServer.CreateStdioWriter(buffer))
            writer.WriteLine("x");
        byte[] written = buffer.ToArray();
        Equal("x\n", Encoding.ASCII.GetString(written), "stdio writer: no BOM, LF line ending");
    }

    private static void HttpRequestEncoding()
    {
        Equal(65001, HttpMcpServer.ResolveRequestEncoding(null).CodePage, "no Content-Type");
        Equal(65001, HttpMcpServer.ResolveRequestEncoding("application/json").CodePage, "no charset");
        Equal(65001, HttpMcpServer.ResolveRequestEncoding("application/json; charset=utf-8").CodePage, "utf-8");
        Equal(65001, HttpMcpServer.ResolveRequestEncoding("application/json; charset=\"UTF-8\"").CodePage, "quoted UTF-8");
        Equal(28591, HttpMcpServer.ResolveRequestEncoding("application/json; charset=iso-8859-1").CodePage, "iso-8859-1");
        Equal(65001, HttpMcpServer.ResolveRequestEncoding("application/json; charset=bogus-charset").CodePage, "unknown charset");
        Equal(65001, HttpMcpServer.ResolveRequestEncoding(";;;not a content type").CodePage, "malformed Content-Type");
        True(HttpMcpServer.ResolveRequestEncoding("application/json").GetPreamble().Length == 0, "default UTF-8 has no BOM");

        byte[] body = Encoding.UTF8.GetBytes("{\"text\":\"" + Unicode + "\"}");
        using var reader = new StreamReader(new MemoryStream(body), HttpMcpServer.ResolveRequestEncoding("application/json"), true);
        Equal("{\"text\":\"" + Unicode + "\"}", reader.ReadToEnd(), "UTF-8 body without charset decodes intact");

        byte[] latin1 = Encoding.GetEncoding(28591).GetBytes("ä°");
        using var latinReader = new StreamReader(new MemoryStream(latin1),
            HttpMcpServer.ResolveRequestEncoding("application/json; charset=iso-8859-1"), true);
        Equal("ä°", latinReader.ReadToEnd(), "explicit Latin-1 body decodes intact");
    }

    // Starts the real HttpMcpServer on a free loopback port (no bearer token, no TIA call) and posts raw
    // UTF-8 bodies without a charset, as MCP HTTP clients do.
    private static void HttpListenerRoundTrip()
    {
        int port;
        var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        try { port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port; }
        finally { probe.Stop(); }

        var config = new TiaOpennessMcp.Config.ServerConfig();
        config.Server.Host = $"http://localhost:{port}/";
        config.Server.Port = port;
        config.Server.RequireBearerToken = false;
        var server = new HttpMcpServer(config, new ToolRegistry());
        server.Start();
        try
        {
            string baseUrl = $"http://localhost:{port}";
            string directTool = "http_" + Unicode;
            byte[] directBody = new UTF8Encoding(false).GetBytes(ToolCall(11, directTool, new { arvo = Unicode }));
            True(directBody.Any(b => b >= 0x80), "the request bytes must contain raw non-ASCII UTF-8");
            string directResponse = Post(baseUrl + "/mcp", directBody, "application/json");
            using (JsonDocument document = JsonDocument.Parse(directResponse))
                Equal($"Unknown tool: '{directTool}'", ToolText(document.RootElement), "direct JSON-RPC tool name echoed");

            // SSE: GET /sse gives the endpoint; the POST /messages result arrives on the event stream.
            var sse = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(baseUrl + "/sse");
            sse.Method = "GET";
            sse.Timeout = 15000;
            sse.ReadWriteTimeout = 15000;
            using var sseResponse = (System.Net.HttpWebResponse)sse.GetResponse();
            using var sseReader = new StreamReader(sseResponse.GetResponseStream()!, new UTF8Encoding(false));
            string? endpoint = null;
            string? line;
            while ((line = sseReader.ReadLine()) != null)
            {
                if (line.StartsWith("data: ")) { endpoint = line.Substring(6).Trim(); break; }
            }
            True(endpoint != null && endpoint.StartsWith("/messages?sessionId="), "SSE endpoint event: " + endpoint);

            string sseTool = "sse_" + Unicode;
            Post(baseUrl + endpoint, new UTF8Encoding(false).GetBytes(ToolCall(12, sseTool, new { })), "application/json");
            JsonElement? sseResult = null;
            while (sseResult == null && (line = sseReader.ReadLine()) != null)
            {
                if (!line.StartsWith("data: ")) continue;
                using JsonDocument document = JsonDocument.Parse(line.Substring(6));
                if (document.RootElement.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == 12)
                    sseResult = document.RootElement.Clone();
            }
            True(sseResult != null, "SSE response for id 12 missing");
            Equal($"Unknown tool: '{sseTool}'", ToolText(sseResult!.Value), "SSE tool name echoed");
        }
        finally
        {
            server.Stop();
        }
    }

    private static string Post(string url, byte[] body, string contentType)
    {
        var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
        request.Method = "POST";
        request.ContentType = contentType;
        request.Timeout = 15000;
        request.ContentLength = body.Length;
        using (Stream stream = request.GetRequestStream())
            stream.Write(body, 0, body.Length);
        using var response = (System.Net.HttpWebResponse)request.GetResponse();
        using var reader = new StreamReader(response.GetResponseStream()!, new UTF8Encoding(false));
        return reader.ReadToEnd();
    }

    private static void SclSourceEncoding()
    {
        string path = Path.Combine(Path.GetTempPath(), "tia-runtime-scl-" + Guid.NewGuid().ToString("N") + ".scl");
        string code = "FUNCTION_BLOCK \"Sekoitin\"\nTITLE = Säiliön täyttö °C\n// Kommentti: ä ö å € Ä\n(* lämpö *)\nEND_FUNCTION_BLOCK\n";
        try
        {
            ExternalSourceFile.Write(path, code);
            byte[] bytes = File.ReadAllBytes(path);
            True(bytes.Length > 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "file starts with the UTF-8 BOM");
            Equal(code, new UTF8Encoding(false, true).GetString(bytes, 3, bytes.Length - 3), "content after the BOM");
            True(bytes.Skip(3).Take(3).SequenceEqual(new byte[] { (byte)'F', (byte)'U', (byte)'N' }), "exactly one BOM");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void PlcSimApiFolderSelection()
    {
        string root = Path.Combine(Path.GetTempPath(), "tia-runtime-plcsimadv-" + Guid.NewGuid().ToString("N"));
        void Folder(string version, bool withDll = true)
        {
            string directory = Path.Combine(root, version);
            Directory.CreateDirectory(directory);
            if (withDll) File.WriteAllText(Path.Combine(directory, PlcSimRuntimeProbe.ApiAssemblyName + ".dll"), "not a real assembly");
        }
        try
        {
            True(PlcSimRuntimeProbe.FindApiAssembly(root) == null, "missing root -> null");
            Directory.CreateDirectory(root);
            Folder("1.0"); Folder("4.1"); Folder("5.0");
            True(PlcSimRuntimeProbe.FindApiAssembly(root) == null, "only API < 8.0 -> null (5.0 is assembly 50.42.10.0)");
            Folder("9.0", withDll: false);
            True(PlcSimRuntimeProbe.FindApiAssembly(root) == null, "API folder without the assembly is ignored");
            Folder("10.0");
            Equal("10.0", PlcSimRuntimeProbe.FindApiAssembly(root)?.FolderVersion, "newest folder >= 8.0 without 8.0");
            Folder("8.0");
            PlcSimApiLocation? preferred = PlcSimRuntimeProbe.FindApiAssembly(root);
            Equal("8.0", preferred?.FolderVersion, "API 8.0 is preferred");
            Equal(Path.Combine(root, "8.0", PlcSimRuntimeProbe.ApiAssemblyName + ".dll"), preferred?.Path, "assembly path");
            Equal(Path.Combine(root, "8.0"), preferred?.Directory, "assembly directory");
            Folder("notaversion");
            Equal("8.0", PlcSimRuntimeProbe.FindApiAssembly(root)?.FolderVersion, "non-numeric folders are ignored");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static void PlcSimDiagnosis()
    {
        var none = new List<PlcSimProcessInfo>();
        var standard = new List<PlcSimProcessInfo> { new PlcSimProcessInfo { Name = "Siemens.Simatic.PlcSim.VplcHost", ProcessId = 4242 } };

        string missing = PlcSimRuntimeProbe.BuildDiagnosis(false, "the API could not be loaded", 0, standard);
        True(missing.Contains("S7-PLCSIM Advanced V8.0+ runtime not available: the API could not be loaded."), missing);
        True(missing.Contains("S7-1200") && missing.Contains("Standard S7-PLCSIM (V18-V21)"), "unsupported note: " + missing);
        True(missing.Contains("PID 4242") && missing.Contains("no public API"), "standard process named: " + missing);
        True(missing.Contains("plc_online_connect") && missing.Contains("read-only") && missing.Contains("not verified"),
            "conditional plc_online hint: " + missing);

        string empty = PlcSimRuntimeProbe.BuildDiagnosis(true, null, 0, none);
        True(empty.Contains("no instances are registered") && !empty.Contains("not available") && !empty.Contains("plc_online"), empty);

        string emptyWithStandard = PlcSimRuntimeProbe.BuildDiagnosis(true, null, 0, standard);
        True(emptyWithStandard.Contains("no instances are registered") && emptyWithStandard.Contains("S7-1200")
            && emptyWithStandard.Contains("PID 4242"), emptyWithStandard);

        string found = PlcSimRuntimeProbe.BuildDiagnosis(true, null, 2, none);
        Equal("Found 2 S7-PLCSIM Advanced instance(s).", found, "instances found");

        string connect = PlcSimRuntimeProbe.BuildConnectHint(standard);
        True(connect.Contains("plcsim_list_instances") && connect.Contains("S7-1200") && connect.Contains("PID 4242"), connect);
    }

    private static void PlcSimListResultErrors()
    {
        var none = new List<PlcSimProcessInfo>();
        PlcSimListResult init = PlcSimRuntimeProbe.BuildListResult(
            () => throw new TypeInitializationException("Siemens.Simatic.Simulation.Runtime.SimulationRuntimeManager",
                new InvalidOperationException("Runtime manager is not running (SREC_NOT_RUNNING).")), none);
        True(!init.RuntimeAvailable, "type initialization failure -> runtime not available");
        True(init.Error != null && init.Error.Contains("SREC_NOT_RUNNING") && !init.Error.Contains("TypeInitializationException"),
            "inner exception surfaced: " + init.Error);
        True(init.Message.Contains("SREC_NOT_RUNNING"), "message carries the error: " + init.Message);
        True(init.Instances != null && init.Instances.Count == 0, "instances is an empty list, not null");

        PlcSimListResult missing = PlcSimRuntimeProbe.BuildListResult(
            () => throw new FileNotFoundException("Could not load file or assembly",
                PlcSimRuntimeProbe.ApiAssemblyName + ", Version=8.0.0.0, Culture=neutral, PublicKeyToken=null"), none);
        True(!missing.RuntimeAvailable && missing.Error!.Contains("S7-PLCSIM Advanced API assembly")
            && missing.Error.Contains("8.0 or newer"), "missing API explained: " + missing.Error);

        PlcSimListResult ok = PlcSimRuntimeProbe.BuildListResult(() => new PlcSimListResult
        {
            RuntimeAvailable = true,
            RuntimeManagerAvailable = true,
            Instances = new List<PlcSimInstanceSummary> { new PlcSimInstanceSummary { ID = 1, Name = "PLC_1" } }
        }, none);
        True(ok.RuntimeAvailable && ok.Error == null && ok.Message == "Found 1 S7-PLCSIM Advanced instance(s).", ok.Message);
    }

    private static void PlcSimListResultJson()
    {
        PlcSimListResult result = PlcSimRuntimeProbe.BuildListResult(() => null!, new List<PlcSimProcessInfo>
        {
            new PlcSimProcessInfo { Name = "S7-PLCSIM", ProcessId = 7 }
        });
        // Same serializer settings as CallToolResult.Json.
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        JsonElement root = document.RootElement;
        foreach (string name in new[] { "runtimeAvailable", "apiPath", "apiVersion", "apiAssemblyVersion", "runtimeManagerAvailable", "runtimeVersion",
            "instances", "standardPlcSimProcesses", "message", "error" })
            True(root.TryGetProperty(name, out _), "property " + name);
        Equal(JsonValueKind.Array, root.GetProperty("instances").ValueKind, "instances is an array");
        Equal(JsonValueKind.False, root.GetProperty("runtimeAvailable").ValueKind, "runtimeAvailable");
        JsonElement process = root.GetProperty("standardPlcSimProcesses")[0];
        Equal("S7-PLCSIM", process.GetProperty("name").GetString(), "process name");
        Equal(7, process.GetProperty("processId").GetInt32(), "process id");
    }

    private static void PlcSimToolsWithoutApi()
    {
        bool apiLoaded = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == PlcSimRuntimeProbe.ApiAssemblyName);
        True(!apiLoaded, "precondition: the PLCSIM Advanced API must not be loaded in the test process");
        True(!File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, PlcSimRuntimeProbe.ApiAssemblyName + ".dll")),
            "precondition: the PLCSIM Advanced API must not sit beside the test exe");

        var registry = new ToolRegistry();
        var list = registry.Execute("plcsim_list_instances", null);
        True(!list.IsError, "plcsim_list_instances succeeds (runtimeAvailable carries the state) without the runtime");
        using (JsonDocument document = JsonDocument.Parse(list.Content[0].Text))
        {
            JsonElement root = document.RootElement;
            Equal(false, root.GetProperty("runtimeAvailable").GetBoolean(), "runtimeAvailable");
            Equal(0, root.GetProperty("instances").GetArrayLength(), "instances");
            string error = root.GetProperty("error").GetString() ?? "";
            True(error.Contains("S7-PLCSIM Advanced API assembly"), "error names the missing API: " + error);
            string message = root.GetProperty("message").GetString() ?? "";
            True(message.Contains("not available") && message.Contains("S7-1200"), "message: " + message);
        }

        var connect = registry.Execute("plcsim_connect", null);
        True(connect.IsError, "plcsim_connect is an error without the runtime");
        using (JsonDocument document = JsonDocument.Parse(connect.Content[0].Text))
        {
            string message = document.RootElement.GetProperty("Message").GetString() ?? "";
            True(message.Contains("'PLC_1'") && message.Contains("S7-PLCSIM Advanced API assembly")
                && message.Contains("plcsim_list_instances") && message.Contains("S7-1200"), "connect message: " + message);
        }

        // The other plcsim_* tools go through PlcSimManager directly; their generic error carries the diagnosis too.
        var calls = new (string Tool, string Args)[]
        {
            ("plcsim_get_status", "{}"),
            ("plcsim_read_tag", "{\"tag_name\":\"DB1.x\"}"),
            ("plcsim_set_operating_state", "{\"state\":\"Run\"}"),
            ("plcsim_disconnect", "{}")
        };
        foreach (var (tool, json) in calls)
        {
            using JsonDocument arguments = JsonDocument.Parse(json);
            var result = registry.Execute(tool, arguments.RootElement.Clone());
            string text = result.Content[0].Text;
            True(result.IsError, tool + " is an error without the API");
            True(text.StartsWith($"Error executing '{tool}': ") && text.Contains("S7-PLCSIM Advanced API assembly")
                && text.Contains("S7-1200") && text.Contains("plcsim_list_instances"), tool + " message: " + text);
        }

        // A plcsim_* failure that is not an API load failure keeps its plain message.
        var missingArgument = registry.Execute("plcsim_read_tag", null);
        Equal("Error executing 'plcsim_read_tag': tag_name required.", missingArgument.Content[0].Text, "plain argument error");
        Equal("boom", PlcSimRuntimeProbe.DescribeToolFailure(new InvalidOperationException("boom")), "non-load failure");
        True(PlcSimRuntimeProbe.IsApiLoadFailure(new TypeInitializationException("X",
            new FileNotFoundException("x", PlcSimRuntimeProbe.ApiAssemblyName + ", Version=8.0.0.0"))), "wrapped load failure detected");
        True(!PlcSimRuntimeProbe.IsApiLoadFailure(new FileNotFoundException("x", "Other.Assembly")), "other assembly is not an API failure");
    }

    private static void PlcSimApiVersionFields()
    {
        Equal("8.0", PlcSimRuntimeProbe.ApiFolderVersionOf(
            @"C:\Program Files (x86)\Common Files\Siemens\PLCSIMADV\API\8.0\" + PlcSimRuntimeProbe.ApiAssemblyName + ".dll"), "API\\8.0");
        Equal("9.0", PlcSimRuntimeProbe.ApiFolderVersionOf(@"D:\X\API\9\" + PlcSimRuntimeProbe.ApiAssemblyName + ".dll"), "API\\9");
        Equal<string?>(null, PlcSimRuntimeProbe.ApiFolderVersionOf(@"C:\repo\src\lib\" + PlcSimRuntimeProbe.ApiAssemblyName + ".dll"), "not an API folder");
        Equal<string?>(null, PlcSimRuntimeProbe.ApiFolderVersionOf(@"C:\x\8.0\" + PlcSimRuntimeProbe.ApiAssemblyName + ".dll"), "parent is not API");
        Equal<string?>(null, PlcSimRuntimeProbe.ApiFolderVersionOf(null), "null path");

        // Without a loaded API, apiVersion is the folder version and apiAssemblyVersion stays null.
        PlcSimListResult result = PlcSimRuntimeProbe.BuildListResult(() => null!, new List<PlcSimProcessInfo>());
        True(result.ApiAssemblyVersion == null, "apiAssemblyVersion is null while the API is not loaded");
        True(result.ApiVersion == null || Version.TryParse(result.ApiVersion, out Version? v) && v!.Build == -1,
            "apiVersion is a folder version (major.minor) or null: " + result.ApiVersion);
    }

    private static void PlcSimDescriptions()
    {
        var tools = new ToolRegistry().GetTools();
        foreach (string name in new[] { "plcsim_list_instances", "plcsim_connect" })
        {
            string description = tools.Single(t => t.Name == name).Description;
            True(description.Contains("S7-PLCSIM Advanced") && description.Contains("S7-1200") && description.Contains("standard S7-PLCSIM"),
                name + " description: " + description);
        }
        var plcsimTools = tools.Where(t => t.Name.StartsWith("plcsim_")).ToList();
        True(plcsimTools.Count >= 12, "plcsim tool count: " + plcsimTools.Count);
        foreach (var tool in plcsimTools)
            True(tool.Description.Contains("S7-PLCSIM Advanced V8.0+ only") && tool.Description.Contains("S7-1200"),
                tool.Name + " description: " + tool.Description);
    }
}
