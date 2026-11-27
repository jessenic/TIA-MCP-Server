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
        Equal("A,B", string.Join(",", duplicates), "ordinal duplicates");
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
}
