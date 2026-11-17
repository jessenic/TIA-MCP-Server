// Tests for the plc_online_* tools. Compiled by Test-PlcOnline.ps1 against the built exe.
// Offline tests need no PLC. Pass a PLC address as the first argument to also run read-only live tests.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using S7CommPlusDriver;
using S7CommPlusDriver.ClientApi;
using TiaOpennessMcp.PlcOnline;

internal static class PlcOnlineTests
{
    private static int _passed;
    private static int _failed;

    private static int Main(string[] args)
    {
        Run("not connected: every read operation explains how to connect", NotConnected);
        Run("argument validation", ArgumentValidation);
        Run("status before any connection", StatusIdle);
        Run("quality prefix is stripped from display text", StripQuality);
        Run("driver errors are described with operation and code", DescribeError);
        Run("typed values become JSON-friendly values", JsonValues);
        Run("connect to a closed port fails with a clear message", ConnectRefused);
        if (args.Length > 0)
        {
            string address = args[0];
            Run($"live {address}: connect, list data blocks, browse, read, disconnect", () => Live(address));
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

    private static void True(bool condition, string what)
    {
        if (!condition) throw new Exception(what);
    }

    private static void Equal<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"{what}: expected <{expected}>, got <{actual}>");
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

    private static JsonElement Json(object value) => JsonDocument.Parse(JsonSerializer.Serialize(value)).RootElement;

    private static void NotConnected()
    {
        var m = new PlcOnlineManagerProbe().Manager;
        foreach (var (name, action) in new (string, Action)[]
                 {
                     ("list", () => m.ListDatablocks()),
                     ("browse", () => m.Browse(null, 0, 10, false)),
                     ("read", () => m.ReadTags(new[] { "DB_X.Y" })),
                 })
        {
            var ex = Throws<InvalidOperationException>(action, name);
            True(ex.Message.Contains("plc_online_connect"), name + " message names plc_online_connect: " + ex.Message);
        }
        Equal("Not connected.", Json(m.Disconnect()).GetProperty("message").GetString(), "disconnect when idle");
    }

    private static void ArgumentValidation()
    {
        var m = new PlcOnlineManagerProbe().Manager;
        Throws<ArgumentException>(() => m.Connect(" ", null, null, 8000), "empty address");
        Throws<ArgumentException>(() => m.Connect("192.0.2.1", null, null, 10), "timeout too small");
        Throws<ArgumentException>(() => m.Connect("192.0.2.1", null, null, 8000, "ssl"), "unknown security mode");
        Throws<ArgumentException>(() => m.ReadTags(new string[0]), "no tags");
        Throws<ArgumentException>(() => m.ReadTags(Enumerable.Repeat("a", PlcOnlineManager.MaxTagsPerRead + 1).ToList()), "too many tags");
        Throws<ArgumentException>(() => m.Browse(null, -1, 10, false), "negative offset");
        Throws<ArgumentException>(() => m.Browse(null, 0, 0, false), "limit 0");
        Throws<ArgumentException>(() => m.Browse(null, 0, PlcOnlineManager.MaxBrowseRows + 1, false), "limit too big");
    }

    private static void StatusIdle()
    {
        var status = Json(new PlcOnlineManagerProbe().Manager.GetStatus());
        True(!status.GetProperty("connected").GetBoolean(), "not connected");
        Equal(JsonValueKind.Null, status.GetProperty("address").ValueKind, "no address");
    }

    private static void StripQuality()
    {
        Equal("12.5", PlcOnlineManager.StripQuality("C0: 12.5"), "good value");
        Equal("a: b", PlcOnlineManager.StripQuality("00: a: b"), "keeps later colons");
        Equal("plain", PlcOnlineManager.StripQuality("plain"), "no prefix");
    }

    private static void DescribeError()
    {
        var s7 = new S7CommPlusConnectionException("Connect", "10.0.0.1:102", 3, true, "Connection refused");
        Equal("Connection refused (operation Connect, error 0x00000003, transient)", PlcOnlineManager.Describe(s7), "driver exception");
        Equal("plain", PlcOnlineManager.Describe(new Exception("plain")), "other exception");
        Equal("Connection refused (operation Connect, error 0x00000003, transient)",
            PlcOnlineManager.Describe(new AggregateException(s7)), "unwraps a single aggregate");
    }

    private static void JsonValues()
    {
        var address = new ItemAddress("8A0E0001.1");
        var b = new PlcTagBool("B", address, Softdatatype.S7COMMP_SOFTDATATYPE_BOOL);
        b.Value = true;
        Equal(true, PlcOnlineManager.JsonValue(b), "bool");
        var r = new PlcTagReal("R", address, Softdatatype.S7COMMP_SOFTDATATYPE_REAL);
        r.Value = 1.5f;
        Equal("1.5", Json(new { v = PlcOnlineManager.JsonValue(r) }).GetProperty("v").GetRawText(), "real");
        var s = new PlcTagString("S", address, Softdatatype.S7COMMP_SOFTDATATYPE_STRING);
        s.GetType().GetProperty("Value")!.SetValue(s, "Filter 1");
        Equal("Filter 1", PlcOnlineManager.JsonValue(s), "string");
    }

    private static void ConnectRefused()
    {
        var m = new PlcOnlineManagerProbe().Manager;
        var started = DateTime.Now;
        var ex = Throws<InvalidOperationException>(() => m.Connect("127.0.0.1", null, null, 2000), "connect to closed port");
        True(ex.Message.StartsWith("Connect to 127.0.0.1 failed:"), "message: " + ex.Message);
        True(ex.Message.Contains("TCP port 102"), "requirements explained");
        True((DateTime.Now - started).TotalSeconds < 30, "fails quickly");
        True(!Json(m.GetStatus()).GetProperty("connected").GetBoolean(), "still disconnected");
    }

    private static void Live(string address)
    {
        var m = new PlcOnlineManagerProbe().Manager;
        try
        {
            var status = Json(m.Connect(address, null, null, 8000));
            True(status.GetProperty("connected").GetBoolean(), "connected");
            Console.WriteLine("     | " + status.GetProperty("securityMode") + ", CPU " + status.GetProperty("cpu"));
            var live = Json(m.GetStatus());
            Console.WriteLine("     | operating state: " + live.GetProperty("operatingState"));
            Equal("Already connected.", Json(m.Connect(address, null, null, 8000)).GetProperty("message").GetString(), "second connect reuses the session");

            var dbs = Json(m.ListDatablocks());
            int dbCount = dbs.GetProperty("count").GetInt32();
            True(dbCount > 0, "data blocks listed");
            Console.WriteLine($"     | {dbCount} data blocks, e.g. " + string.Join(", ",
                dbs.GetProperty("datablocks").EnumerateArray().Take(3).Select(d => $"DB{d.GetProperty("number")} {d.GetProperty("name").GetString()}")));

            var browse = Json(m.Browse(null, 0, 20, false));
            int total = browse.GetProperty("total").GetInt32();
            True(total > 0, "variables browsed");
            var names = browse.GetProperty("variables").EnumerateArray().Select(v => v.GetProperty("name").GetString()!).ToList();
            var filtered = Json(m.Browse(names[0].Substring(0, Math.Min(8, names[0].Length)).ToLowerInvariant(), 0, 5, false));
            True(filtered.GetProperty("total").GetInt32() > 0, "case-insensitive filter");

            names.Add("DB_DoesNotExist_Test.Value");
            var read = Json(m.ReadTags(names));
            var rows = read.GetProperty("tags").EnumerateArray().ToList();
            Equal(names.Count, rows.Count, "one row per tag");
            foreach (var row in rows.Take(rows.Count - 1))
            {
                True(row.GetProperty("quality").GetString() == "good", $"{row.GetProperty("name").GetString()} read good: {row}");
                True(row.TryGetProperty("value", out _), "value present");
            }
            True(rows.Last().TryGetProperty("error", out _), "unknown symbol reported per tag");
            Console.WriteLine($"     | {total} variables; read {rows.Count - 1}, e.g. {rows[0].GetProperty("name").GetString()} = {rows[0].GetProperty("value")}");
        }
        finally
        {
            var after = Json(m.Disconnect());
            True(!after.GetProperty("connected").GetBoolean(), "disconnected");
        }
    }

    /// <summary>A fresh manager per test, not the process-wide singleton.</summary>
    private sealed class PlcOnlineManagerProbe
    {
        public PlcOnlineManager Manager { get; } =
            (PlcOnlineManager)Activator.CreateInstance(typeof(PlcOnlineManager), nonPublic: true)!;
    }
}
