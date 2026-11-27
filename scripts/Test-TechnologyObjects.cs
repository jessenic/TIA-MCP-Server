// Offline tests for the technology-object tools (tia_*_technology_object*). Compiled by Test-TechnologyObjects.ps1
// against the built exe. No TIA Portal is started or attached; only the pure input and value rules are tested.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using TiaOpennessMcp.Tia;
using TiaOpennessMcp.Tools;

internal static class TechnologyObjectTests
{
    private static int _passed;
    private static int _failed;

    private static int Main()
    {
        Run("type names: TO_ aliases map to Openness names, others pass through", TypeNames);
        Run("version: major.minor with optional V prefix", Versions);
        Run("version match compares major and minor only", VersionMatch);
        Run("value conversion follows the current value's CLR type", Conversion);
        Run("value formatting and read-back comparison", FormattingAndEquality);
        Run("duplicate parameter names are found", Duplicates);
        Run("similar parameter names for not_found", Suggestions);
        Run("tool arguments: parameters array parsing", ParameterArguments);
        Run("writes: all verified gives updated", WritesAllVerified);
        Run("writes: first setter throws", WritesFirstSetterThrows);
        Run("writes: later setter throws a COMException", WritesLaterSetterThrows);
        Run("writes: read-back throws after a successful write", WritesReadBackThrows);
        Run("writes: read-back mismatch stops later writes", WritesReadBackMismatch);
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

    private static void TypeNames()
    {
        foreach (var (input, expected) in new[]
                 {
                     ("TO_PID_Temp", "PID_Temp"), ("TO_PID_Compact", "PID_Compact"), ("TO_PID_3Step", "PID_3Step"),
                     ("to_pid_temp", "PID_Temp"), ("PID_Temp", "PID_Temp"), ("pid_compact", "PID_Compact"),
                     ("TO_PositioningAxis", "TO_PositioningAxis"), ("High_Speed_Counter", "High_Speed_Counter")
                 })
        {
            True(TechnologyObjectRules.TryNormalizeType(input, out string normalized, out _), input + " accepted");
            Equal(expected, normalized, input);
        }
        foreach (string? bad in new[] { null, "", "   ", " PID_Temp", "PID_Temp " })
            True(!TechnologyObjectRules.TryNormalizeType(bad, out _, out string error) && error.Length > 0, $"'{bad}' rejected");
    }

    private static void Versions()
    {
        foreach (string text in new[] { "1.1", "V1.1", "v1.1" })
        {
            True(TechnologyObjectRules.TryParseVersion(text, out Version? version, out _), text + " accepted");
            Equal(new Version(1, 1), version!, text);
        }
        True(TechnologyObjectRules.TryParseVersion("2.10", out Version? v210, out _) && v210!.Minor == 10, "2.10 keeps minor 10");
        Equal("2.3", TechnologyObjectRules.FormatVersion(new Version(2, 3)), "format");
        foreach (string? bad in new[] { null, "", "1", "abc", "1.1.x", "1.1.0", "V 1.1", " 1.1", "1.1 ", "-1.1", "1.-1", "VV1.1" })
            True(!TechnologyObjectRules.TryParseVersion(bad, out _, out string error) && error.Length > 0, $"'{bad}' rejected");
    }

    private static void VersionMatch()
    {
        var requested = new Version(1, 1);
        True(TechnologyObjectRules.VersionMatches(requested, new Version(1, 1)), "1.1");
        True(TechnologyObjectRules.VersionMatches(requested, new Version(1, 1, 0)), "1.1.0");
        True(TechnologyObjectRules.VersionMatches(requested, new Version(1, 1, 0, 0)), "1.1.0.0");
        True(!TechnologyObjectRules.VersionMatches(requested, new Version(1, 2)), "1.2");
        True(!TechnologyObjectRules.VersionMatches(requested, new Version(2, 1)), "2.1");
        True(!TechnologyObjectRules.VersionMatches(requested, null), "null");
    }

    private static object? Convert(Type type, string? text)
    {
        if (!TechnologyObjectRules.TryConvertParameterValue(type, text, out object? converted, out string error))
            throw new Exception($"{type.Name} '{text}' rejected: {error}");
        return converted;
    }

    private static void Rejected(Type type, string? text)
    {
        True(!TechnologyObjectRules.TryConvertParameterValue(type, text, out object? converted, out string error)
             && converted == null && error.Length > 0, $"{type.Name} '{text}' rejected");
    }

    private static void Conversion()
    {
        Equal<object?>(true, Convert(typeof(bool), "true"), "bool true");
        Equal<object?>(false, Convert(typeof(bool), " FALSE "), "bool FALSE");
        Rejected(typeof(bool), "yes");
        Rejected(typeof(bool), "1");

        Equal<object?>((sbyte)-128, Convert(typeof(sbyte), "-128"), "sbyte min");
        Rejected(typeof(sbyte), "-129");
        Equal<object?>((byte)255, Convert(typeof(byte), "255"), "byte max");
        Rejected(typeof(byte), "256");
        Rejected(typeof(byte), "-1");
        Equal<object?>((short)-32768, Convert(typeof(short), "-32768"), "short min");
        Rejected(typeof(short), "32768");
        Equal<object?>((ushort)65535, Convert(typeof(ushort), "65535"), "ushort max");
        Rejected(typeof(ushort), "65536");
        Equal<object?>(int.MinValue, Convert(typeof(int), "-2147483648"), "int min");
        Rejected(typeof(int), "2147483648");
        Rejected(typeof(int), "1.5");
        Rejected(typeof(int), "0x10");
        Rejected(typeof(int), "1,000");
        Equal<object?>(uint.MaxValue, Convert(typeof(uint), "4294967295"), "uint max");
        Rejected(typeof(uint), "-1");
        Equal<object?>(long.MaxValue, Convert(typeof(long), "9223372036854775807"), "long max");
        Equal<object?>(ulong.MaxValue, Convert(typeof(ulong), "18446744073709551615"), "ulong max");
        Rejected(typeof(ulong), "18446744073709551616");

        Equal<object?>(1.5f, Convert(typeof(float), "1.5"), "float");
        Equal<object?>(-2.5e3f, Convert(typeof(float), "-2.5E3"), "float exponent");
        Rejected(typeof(float), "1,5");
        Rejected(typeof(float), "NaN");
        Rejected(typeof(float), "Infinity");
        Rejected(typeof(float), "1e39");
        Equal<object?>(0.1, Convert(typeof(double), "0.1"), "double");
        Rejected(typeof(double), "-Infinity");

        Equal<object?>('A', Convert(typeof(char), "A"), "char");
        Rejected(typeof(char), "AB");
        Rejected(typeof(char), "");
        Equal<object?>(" text ", Convert(typeof(string), " text "), "string kept as is");
        Equal<object?>("", Convert(typeof(string), ""), "empty string");
        Rejected(typeof(DateTime), "2024-01-01");
        Rejected(typeof(TimeSpan), "00:00:01");
        Rejected(typeof(int), null);
    }

    private static void FormattingAndEquality()
    {
        Equal("true", TechnologyObjectRules.FormatValue(true), "bool");
        Equal("0.1", TechnologyObjectRules.FormatValue(0.1f), "float round-trip");
        Equal("1.5", TechnologyObjectRules.FormatValue(1.5), "double");
        Equal("-7", TechnologyObjectRules.FormatValue((short)-7), "short");
        Equal(null, TechnologyObjectRules.FormatValue(null), "null");
        True(TechnologyObjectRules.ValuesEqual(1.5f, 1.5f), "same float");
        True(TechnologyObjectRules.ValuesEqual((ushort)5, (ushort)5), "same ushort");
        True(TechnologyObjectRules.ValuesEqual(5, 5L), "same text across int/long");
        True(!TechnologyObjectRules.ValuesEqual(1.5f, 1.25f), "different float");
        True(!TechnologyObjectRules.ValuesEqual(true, false), "different bool");
        True(!TechnologyObjectRules.ValuesEqual(true, null), "null read-back");
        True(TechnologyObjectRules.ValuesEqual(null, null), "both null");
    }

    private static void Duplicates()
    {
        Equal(0, TechnologyObjectRules.FindDuplicateNames(new[] { "Config.InputPerOn", "Config.ActivateCooling" }).Count, "no duplicates");
        var duplicates = TechnologyObjectRules.FindDuplicateNames(new[] { "A", "B", "A", "a", "B", "A" });
        Equal("A,B", string.Join(",", duplicates), "case-insensitive duplicates");
        Equal(1, TechnologyObjectRules.FindDuplicateNames(new[] { "Config.InputPerOn", "config.inputperon" }).Count, "names differing only in case");
    }

    private static void Suggestions()
    {
        var names = new[] { "Config.InputPerOn", "Config.ActivateCooling", "Config.AdvancedCooling", "Retain.CtrlParams.Heat.Gain", "PhysicalUnit" };
        var similar = TechnologyObjectRules.SuggestParameterNames(names, "Config.ActivateHeating", 10);
        Equal("Config.InputPerOn,Config.ActivateCooling,Config.AdvancedCooling", string.Join(",", similar), "same prefix");
        Equal("Config.InputPerOn", TechnologyObjectRules.SuggestParameterNames(names, "config.inputperon", 10)[0], "case-insensitive match first");
        Equal(2, TechnologyObjectRules.SuggestParameterNames(names, "Config.X", 2).Count, "max honoured");
        Equal(0, TechnologyObjectRules.SuggestParameterNames(names, "Nothing.Here", 10).Count, "no match");
    }

    private static List<KeyValuePair<string, string>> ParseArgs(string json)
    {
        var method = typeof(ToolRegistry).GetMethod("GetTechnologyObjectParameterWrites", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new Exception("GetTechnologyObjectParameterWrites not found");
        JsonElement? args = JsonDocument.Parse(json).RootElement;
        try { return (List<KeyValuePair<string, string>>)method.Invoke(null, new object?[] { args })!; }
        catch (TargetInvocationException error) { throw error.InnerException!; }
    }

    private static void ParameterArguments()
    {
        var list = ParseArgs("{\"name\":\"PID_Temp_1\",\"parameters\":[{\"name\":\"Config.InputPerOn\",\"value\":\"true\"},{\"name\":\"A\",\"value\":false},{\"name\":\"B\",\"value\":0.5}]}");
        Equal(3, list.Count, "count");
        Equal("Config.InputPerOn=true", list[0].Key + "=" + list[0].Value, "string value");
        Equal("A=false", list[1].Key + "=" + list[1].Value, "boolean value");
        Equal("B=0.5", list[2].Key + "=" + list[2].Value, "number value");
        Equal(0, ParseArgs("{\"parameters\":[]}").Count, "empty list parsed (rejected later by the manager)");
        foreach (string bad in new[]
                 {
                     "{}", "{\"parameters\":{}}", "{\"parameters\":[\"x\"]}", "{\"parameters\":[{\"value\":\"1\"}]}",
                     "{\"parameters\":[{\"name\":\"A\"}]}", "{\"parameters\":[{\"name\":1,\"value\":\"1\"}]}",
                     "{\"parameters\":[{\"name\":\"A\",\"value\":null}]}", "{\"parameters\":[{\"name\":\"A\",\"value\":[1]}]}"
                 })
        {
            bool threw = false;
            try { ParseArgs(bad); } catch (ArgumentException) { threw = true; }
            True(threw, "rejected: " + bad);
        }
    }

    private sealed class FakeParameter
    {
        public object? Value;
        public Exception? WriteError;
        public Exception? ReadError;
        public Func<object, object?>? Recalculate;
        public int Writes;
    }

    private static (TechnologyObjectParameterSetResult Result, List<TechnologyObjectPlannedWrite> Writes) Plan(params (string Name, object Value, FakeParameter Fake)[] items)
    {
        var result = new TechnologyObjectParameterSetResult();
        var writes = new List<TechnologyObjectPlannedWrite>();
        foreach (var (name, value, fake) in items)
        {
            var entry = new TechnologyObjectParameterWrite { Name = name, RequestedValue = TechnologyObjectRules.FormatValue(value), Status = "pending" };
            result.Parameters.Add(entry);
            writes.Add(new TechnologyObjectPlannedWrite(entry, value,
                v => { fake.Writes++; if (fake.WriteError != null) throw fake.WriteError; fake.Value = fake.Recalculate != null ? fake.Recalculate(v) : v; },
                () => { if (fake.ReadError != null) throw fake.ReadError; return fake.Value; }));
        }
        return (result, writes);
    }

    private static string Statuses(TechnologyObjectParameterSetResult result) => string.Join(",", result.Parameters.Select(p => p.Status));

    private static void WritesAllVerified()
    {
        var a = new FakeParameter { Value = false };
        var b = new FakeParameter { Value = 1.0f };
        var (result, writes) = Plan(("A", true, a), ("B", 2.5f, b));
        TechnologyObjectRules.ExecuteParameterWrites(result, writes);
        True(result.Success, "success");
        Equal("updated", result.Status, "status");
        Equal(false, result.Partial, "not partial");
        Equal("updated,updated", Statuses(result), "entries");
        Equal("2.5", result.Parameters[1].NewValue, "read-back value");
    }

    private static void WritesFirstSetterThrows()
    {
        var a = new FakeParameter { Value = false, WriteError = new InvalidOperationException("read-only") };
        var b = new FakeParameter { Value = false };
        var (result, writes) = Plan(("A", true, a), ("B", true, b));
        TechnologyObjectRules.ExecuteParameterWrites(result, writes);
        True(!result.Success, "failed");
        Equal("api_failure", result.Status, "status");
        Equal(true, result.Partial, "partial: the failed write may have changed state");
        Equal("api_failure,not_written", Statuses(result), "entries");
        Equal(0, b.Writes, "later entry never written");
        Equal(1, result.Warnings.Count, "one warning");
        True(result.Warnings[0].StartsWith("No earlier parameter was written", StringComparison.Ordinal), "warning does not claim earlier writes: " + result.Warnings[0]);
    }

    private static void WritesLaterSetterThrows()
    {
        var a = new FakeParameter { Value = false };
        var b = new FakeParameter { Value = 0, WriteError = new System.Runtime.InteropServices.COMException("RPC failed") };
        var c = new FakeParameter { Value = 0 };
        var (result, writes) = Plan(("A", true, a), ("B", 5, b), ("C", 6, c));
        TechnologyObjectRules.ExecuteParameterWrites(result, writes);
        Equal("api_failure", result.Status, "status");
        Equal("updated,api_failure,not_written", Statuses(result), "entries");
        True(result.Parameters[1].Message!.Contains("COMException"), "COM error on the entry");
        True(result.Warnings[0].StartsWith("Earlier parameters in the list were written", StringComparison.Ordinal), "warning names earlier writes");
        True(result.Message.Contains("earlier parameters"), "message names earlier writes");
    }

    private static void WritesReadBackThrows()
    {
        var a = new FakeParameter { Value = false, ReadError = new System.Runtime.InteropServices.COMException("gone") };
        var b = new FakeParameter { Value = false };
        var (result, writes) = Plan(("A", true, a), ("B", true, b));
        TechnologyObjectRules.ExecuteParameterWrites(result, writes);
        Equal("verification_failed", result.Status, "status");
        Equal(true, result.Partial, "partial");
        Equal("verification_failed,not_written", Statuses(result), "written entry is not reported as not_written");
        Equal(true, a.Value, "value reached the fake");
        True(result.Parameters[0].Message!.Contains("was written"), "entry says it was written");
    }

    private static void WritesReadBackMismatch()
    {
        var a = new FakeParameter { Value = 0.0, Recalculate = v => 1.0 };
        var b = new FakeParameter { Value = 0.0 };
        var (result, writes) = Plan(("A", 2.0, a), ("B", 3.0, b));
        TechnologyObjectRules.ExecuteParameterWrites(result, writes);
        Equal("verification_failed", result.Status, "status");
        Equal("verification_failed,not_written", Statuses(result), "entries");
        Equal("1", result.Parameters[0].NewValue, "recalculated value reported");
        Equal(0, b.Writes, "later entry never written");
        True(result.Warnings[0].Contains("now reads back as '1'"), "warning: " + result.Warnings[0]);
    }
}
