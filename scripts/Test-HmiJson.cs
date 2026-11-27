using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Siemens.Engineering;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.Dynamization;
using Siemens.Engineering.HmiUnified.UI.Dynamization.Tag;
using Siemens.Engineering.HmiUnified.UI.Screens;
using Siemens.Engineering.HmiUnified.UI.Widgets;
using TiaOpennessMcp.Tia;

class HmiJsonTests
{
    static string binaryDirectory;
    static string apiDirectory;
    static int checks;

    static int Main(string[] args)
    {
        binaryDirectory = args[0];
        apiDirectory = args[1];
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        try { Run(args); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name;
        foreach (string directory in new[] { binaryDirectory, apiDirectory })
            foreach (string extension in new[] { ".dll", ".exe" })
            {
                string path = Path.Combine(directory, name + extension);
                if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
        return null;
    }

    static object Invoke(string name, params object[] args)
    {
        try
        {
            return typeof(TiaManager).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, args);
        }
        catch (TargetInvocationException error) { throw error.InnerException; }
    }

    static object Document(string name)
    {
        return Activator.CreateInstance(typeof(TiaManager).GetNestedType(name, BindingFlags.NonPublic), true);
    }

    static object InstanceInvoke(object target, string name, params object[] args)
    {
        try { return target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args); }
        catch (TargetInvocationException error) { throw error.InnerException; }
    }

    static void Set(object target, string name, object value)
    {
        target.GetType().GetProperty(name).SetValue(target, value, null);
    }

    static object Get(object target, string name)
    {
        return target.GetType().GetProperty(name).GetValue(target, null);
    }

    static string Json(object value)
    {
        return JsonSerializer.Serialize(value, value.GetType(), new JsonSerializerOptions());
    }

    static void Check(bool success, string message)
    {
        if (!success) throw new Exception("FAIL: " + message);
        checks++;
    }

    static void Reject(Action action, string message)
    {
        try { action(); }
        catch (InvalidDataException) { checks++; return; }
        throw new Exception("FAIL: expected invalid input rejection: " + message);
    }

    static string Fails(Action action, string message)
    {
        try { action(); }
        catch (InvalidOperationException error) { checks++; return error.Message; }
        throw new Exception("FAIL: expected InvalidOperationException: " + message);
    }

    static JsonSerializerOptions HmiOptions()
    {
        return (JsonSerializerOptions)typeof(TiaManager).GetField("HmiJsonOptions", BindingFlags.NonPublic | BindingFlags.Static)
            .GetValue(null);
    }

    static object Parse(string typeName, string json)
    {
        return JsonSerializer.Deserialize(json, typeof(TiaManager).GetNestedType(typeName, BindingFlags.NonPublic), HmiOptions());
    }

    static JsonElement JsonValue(string json)
    {
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    static IList NewList(string elementTypeName)
    {
        return (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(
            typeof(TiaManager).GetNestedType(elementTypeName, BindingFlags.NonPublic)));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Run(string[] args)
    {
        object[] variants = {
            "raw <ProjectText>markup</ProjectText>", true, false, (byte)255,
            (short)-32000, (ushort)65000, -123, uint.MaxValue, long.MinValue,
            ulong.MaxValue, 1.25f, 1.25d, 1.25m, Color.FromArgb(128, 10, 20, 30)
        };
        foreach (object value in variants)
        {
            object encoded = Invoke("CreateMappingValue", value);
            object decoded = Invoke("ReadMappingValue", encoded);
            Check(value.GetType() == decoded.GetType() && value.Equals(decoded),
                "mapping value round-trip: " + value.GetType().Name);
        }
        object invalid = Document("HmiJsonMappingValue");
        Set(invalid, "Type", "ArbitraryObject");
        Reject(() => Invoke("ReadMappingValue", invalid), "unknown value type");
        object bitmaskEntry = Document("HmiJsonMappingEntry");
        Set(bitmaskEntry, "Type", "Bitmask");
        Set(bitmaskEntry, "BitDynamizationType", "MultiBit");
        Set(bitmaskEntry, "Condition", JsonSerializer.SerializeToElement(UInt64.MaxValue));
        Set(bitmaskEntry, "Relevant", UInt64.MaxValue);
        object bitmaskConverter = Document("HmiJsonValueConverter");
        Set(bitmaskConverter, "Formula", "");
        Set(bitmaskConverter, "ConditionType", "Bitmask");
        var bitmaskEntries = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(bitmaskEntry.GetType()));
        bitmaskEntries.Add(bitmaskEntry);
        Set(bitmaskConverter, "Entries", bitmaskEntries);
        Invoke("ValidateValueConverter", bitmaskConverter);
        Check(Json(bitmaskEntry).Contains("\"Condition\":18446744073709551615") &&
            Json(bitmaskEntry).Contains("\"Relevant\":18446744073709551615"),
            "bitmask unsigned 64-bit values retain full precision");
        Set(bitmaskEntry, "BitDynamizationType", "SingleBit");
        Set(bitmaskConverter, "ConditionType", "Singlebit");
        Invoke("ValidateValueConverter", bitmaskConverter);
        Check(true, "Singlebit condition accepts SingleBit mapping entries");
        Set(bitmaskEntry, "BitDynamizationType", "MultiBit");
        Reject(() => Invoke("ValidateValueConverter", bitmaskConverter),
            "Singlebit condition rejects MultiBit mapping entries");
        Set(bitmaskEntry, "BitDynamizationType", "SingleBit");
        Set(bitmaskConverter, "ConditionType", "Bitmask");
        object simpleEntry = Document("HmiJsonMappingEntry");
        Set(simpleEntry, "Type", "Simple");
        Set(simpleEntry, "Condition", JsonSerializer.SerializeToElement("=="));
        object rangeEntry = Document("HmiJsonMappingEntry");
        Set(rangeEntry, "Type", "Range");
        Set(rangeEntry, "From", Invoke("CreateMappingValue", 1));
        Set(rangeEntry, "To", Invoke("CreateMappingValue", 10));
        var simpleRangeEntries = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(simpleEntry.GetType()));
        simpleRangeEntries.Add(simpleEntry);
        simpleRangeEntries.Add(rangeEntry);
        object simpleRangeConverter = Document("HmiJsonValueConverter");
        Set(simpleRangeConverter, "Formula", "");
        Set(simpleRangeConverter, "ConditionType", "None");
        Set(simpleRangeConverter, "Entries", simpleRangeEntries);
        Invoke("ValidateValueConverter", simpleRangeConverter);
        Check(Json(simpleEntry).Contains("\"Condition\":\"==\"") &&
            Json(rangeEntry).Contains("\"From\"") && Json(rangeEntry).Contains("\"To\""),
            "Simple and Range mapping document shapes remain unchanged");
        object missingRelevant = Document("HmiJsonMappingEntry");
        Set(missingRelevant, "Type", "Bitmask");
        Set(missingRelevant, "BitDynamizationType", "SingleBit");
        Set(missingRelevant, "Condition", JsonSerializer.SerializeToElement(1UL));
        var missingRelevantEntries = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(missingRelevant.GetType()));
        missingRelevantEntries.Add(missingRelevant);
        object missingRelevantConverter = Document("HmiJsonValueConverter");
        Set(missingRelevantConverter, "Formula", "");
        Set(missingRelevantConverter, "ConditionType", "Bitmask");
        Set(missingRelevantConverter, "Entries", missingRelevantEntries);
        Reject(() => Invoke("ValidateValueConverter", missingRelevantConverter), "bitmask requires relevant mask");
        Set(bitmaskEntry, "BitDynamizationType", "Unknown");
        Reject(() => Invoke("ValidateValueConverter", bitmaskConverter), "invalid bitmask dynamization type");
        Set(bitmaskEntry, "BitDynamizationType", "MultiBit");
        object expression = Document("HmiJsonDynamization");
        Set(expression, "Type", "Expression");
        Set(expression, "Property", "Enabled");
        Reject(() => Invoke("ValidateDynamizationDocument", typeof(Siemens.Engineering.HmiUnified.UI.Widgets.HmiButton),
            expression, new Dictionary<string, string>()), "legacy expression placeholder");
        var arrayTags = new Dictionary<string, string> { { "Values", "Array [0..2] of Real" } };
        Check((bool)Invoke("ContainsTagReference", arrayTags, "Values[0]"), "array element reference");
        Check(!(bool)Invoke("ContainsTagReference", arrayTags, "Values[3]"), "out-of-range array reference rejected");
        var memberTags = new Dictionary<string, string> { { "UnitParent_UnitData", "dtIECore_UnitData" } };
        Check((bool)Invoke("ContainsTagReference", memberTags,
            "UnitParent_UnitData.UnitStatus.Error"), "nested UDT member reference");
        Check(!(bool)Invoke("ContainsTagReference", memberTags,
            "Missing_UnitData.UnitStatus.Error"), "UDT member reference requires an existing root tag");
        Check(!(bool)Invoke("ContainsTagReference", memberTags,
            "UnitParent_UnitData.UnitStatus."), "malformed UDT member reference rejected");
        NewDynamizationTests();
        FlagsEnumTests();
        ScriptComparisonTests();
        PartialItemTests();
        MappingTablePlanTests();
        ZOrderTests();
        PatchFormatTests();
        object first = Document("HmiJsonItem");
        object second = Document("HmiJsonItem");
        Set(first, "Type", "HmiFaceplateContainer");
        Set(second, "Type", "HmiFaceplateContainer");
        Set(first, "ContainedType", "V1\\First");
        Set(second, "ContainedType", "V2\\Second");
        Check(!Equals(Invoke("GetItemIdentityKey", first), Invoke("GetItemIdentityKey", second)),
            "contained type separates default cache entries");
        var properties = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(
            typeof(TiaManager).GetNestedType("HmiJsonInterfaceProperty", BindingFlags.NonPublic)));
        object parameter = Document("HmiJsonInterfaceProperty");
        Set(parameter, "Name", "LevelPercent");
        Set(parameter, "Value", Invoke("CreateMappingValue", 60.25d));
        properties.Add(parameter);
        Invoke("ValidateInterfaceDocument", properties, 0);
        Check(true, "typed container interface accepted");
        properties.Add(parameter);
        Reject(() => Invoke("ValidateInterfaceDocument", properties, 0), "duplicate interface parameter");
        properties.RemoveAt(1);
        Reject(() => Invoke("ValidateInterfaceDocument", properties, 17), "interface nesting limit");
        Set(parameter, "Value", invalid);
        Reject(() => Invoke("ValidateInterfaceDocument", properties, 0), "unsupported interface value type");
        var inventory = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(
            typeof(TiaManager).GetNestedType("HmiJsonLibraryType", BindingFlags.NonPublic)));
        object type = Document("HmiJsonLibraryType");
        Set(type, "Name", "IndicatorLight");
        Set(type, "Guid", Guid.NewGuid());
        object version = Document("HmiJsonLibraryVersion");
        Set(version, "Version", "0.0.7");
        Set(version, "Guid", Guid.NewGuid());
        ((IList)Get(type, "Versions")).Add(version);
        inventory.Add(type);
        Check((bool)Invoke("SameLibraryInventory", inventory, inventory), "library GUID inventory matches itself");
        var empty = (IList)Activator.CreateInstance(inventory.GetType());
        Check(!(bool)Invoke("SameLibraryInventory", inventory, empty), "missing library definition rejected");
        ItemPropertyPathTests();
        if (args.Length > 2)
            Live(int.Parse(args[2]), args[3], args.Length > 4 && bool.Parse(args[4]));
        Console.WriteLine("PASS: " + checks + " HMI JSON regression checks.");
    }

    // hmi_update_screen_item leaf paths against the real V21 Openness types (typeof only, no TIA connection).
    static void ItemPropertyPathTests()
    {
        Type button = typeof(HmiButton);
        Type ioField = typeof(HmiIOField);
        Type text = typeof(Siemens.Engineering.HmiUnified.UI.Shapes.HmiText);
        Type screenWindow = typeof(HmiScreenWindow);
        Invoke("ValidateItemPropertyPath", button, "Font.Size", JsonElementOf("14"));
        Invoke("ValidateItemPropertyPath", button, "Font.Weight", JsonElementOf("\"Bold\""));
        Invoke("ValidateItemPropertyPath", button, "Font.Name", JsonElementOf("\"SiemensSans\""));
        Invoke("ValidateItemPropertyPath", button, "Font.Italic", JsonElementOf("true"));
        Invoke("ValidateItemPropertyPath", button, "Enabled", JsonElementOf("false"));
        Invoke("ValidateItemPropertyPath", button, "Text", JsonElementOf("\"Start\""));
        Invoke("ValidateItemPropertyPath", ioField, "OutputFormat", JsonElementOf("\"{F2}\""));
        Invoke("ValidateItemPropertyPath", ioField, "Font.Size", JsonElementOf("12"));
        Invoke("ValidateItemPropertyPath", ioField, "HorizontalTextAlignment", JsonElementOf("\"Right\""));
        Invoke("ValidateItemPropertyPath", text, "HorizontalTextAlignment", JsonElementOf("\"Center\""));
        Invoke("ValidateItemPropertyPath", text, "VerticalTextAlignment", JsonElementOf("\"Center\""));
        Invoke("ValidateItemPropertyPath", screenWindow, "Screen", JsonElementOf("\"Main\""));
        checks++;
        Reject(() => Invoke("ValidateItemPropertyPath", ioField, "Text", JsonElementOf("\"x\"")), "IO field has no Text");
        Reject(() => Invoke("ValidateItemPropertyPath", text, "HorizontalTextAlignment", JsonElementOf("5")), "alignment must be a name");
        Reject(() => Invoke("ValidateItemPropertyPath", text, "VerticalTextAlignment", JsonElementOf("\"Middle\"")), "Middle is not an HmiVerticalAlignment");
        Reject(() => Invoke("ValidateItemPropertyPath", button, "Font.Weight", JsonElementOf("\"Heavy\"")), "unknown font weight");
        Reject(() => Invoke("ValidateItemPropertyPath", screenWindow, "Font.Size", JsonElementOf("12")), "screen window has no Font");
        Reject(() => Invoke("ValidateItemPropertyPath", button, "Screen", JsonElementOf("\"Main\"")), "button has no Screen");
    }

    static JsonElement JsonElementOf(string json)
    {
        using (JsonDocument document = JsonDocument.Parse(json))
            return document.RootElement.Clone();
    }

    static void NewDynamizationTests()
    {
        var converter = Document("HmiJsonValueConverter");
        Set(converter, "ConditionType", "Bitmask");
        var entries = (IList)Get(converter, "Entries");
        var entry = Document("HmiJsonMappingEntry");
        Set(entry, "Type", "Bitmask");
        Set(entry, "BitCondition", ulong.MaxValue);
        Set(entry, "Relevant", 1UL << 63);
        Set(entry, "BitDynamizationType", "MultiBit");
        entries.Add(entry);
        Invoke("ValidateValueConverter", converter);
        Check(Json(entry).Contains("18446744073709551615") && Json(entry).Contains("9223372036854775808"),
            "64-bit bitmask values serialize without precision loss");
        Set(entry, "Relevant", null);
        Reject(() => Invoke("ValidateValueConverter", converter), "missing relevant mask");
        Set(entry, "Relevant", 1UL << 63);
        Set(entry, "BitDynamizationType", "InvalidMode");
        Reject(() => Invoke("ValidateValueConverter", converter), "invalid bitmask mode");
        Set(entry, "BitDynamizationType", "SingleBit");
        Invoke("ValidateValueConverter", converter);
        Check(true, "single-bit mapping accepted");

        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var memberList = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(
            typeof(TiaManager).GetNestedType("HmiJsonTagMember", BindingFlags.NonPublic)));
        var member = Document("HmiJsonTagMember");
        Set(member, "Name", "Fault");
        Set(member, "DataType", "Bool");
        memberList.Add(member);
        Invoke("AddTagMembers", tags, "Conveyor{0}", memberList, 0);
        Check((bool)Invoke("ContainsTagReference", tags, "Conveyor[0].Fault"), "structured member bracket lookup");
        Check(!(bool)Invoke("ContainsTagReference", tags, "Conveyor[0].Missing"), "unknown member rejected");
        Reject(() => Invoke("AddTagMembers", tags, "Conveyor", memberList, 33), "tag nesting limit");

        var script = Document("HmiJsonScriptDynamization");
        Set(script, "Code", "return 42;");
        Set(script, "GlobalDefinitionCode", "const value = 42;");
        Set(script, "TriggerType", "Tags");
        ((IList)Get(script, "Tags")).Add("Conveyor{0}.Fault");
        var dynamization = Document("HmiJsonDynamization");
        Set(dynamization, "Type", "Script");
        Set(dynamization, "Property", "Enabled");
        Set(dynamization, "Script", script);
        var buttonType = typeof(Siemens.Engineering.HmiUnified.UI.Widgets.HmiButton);
        Invoke("ValidateDynamizationDocument", buttonType, dynamization, tags);
        Check(true, "script trigger and code accepted");
        ((IList)Get(script, "Tags")).Add("Missing");
        Reject(() => Invoke("ValidateDynamizationDocument", buttonType, dynamization, tags), "unknown trigger tag");
        ((IList)Get(script, "Tags")).Remove("Missing");
        Set(script, "TriggerType", "InvalidCycle");
        Reject(() => Invoke("ValidateDynamizationDocument", buttonType, dynamization, tags), "invalid script cycle");
        Set(dynamization, "Script", null);
        Reject(() => Invoke("ValidateDynamizationDocument", buttonType, dynamization, tags), "missing script configuration");

        Set(dynamization, "Type", "ResourceList");
        Set(dynamization, "Tag", "Conveyor{0}.Fault");
        Set(dynamization, "ResourceList", "StatusGraphics");
        Invoke("ValidateDynamizationDocument", buttonType, dynamization, tags);
        Check(true, "resource-list binding accepted");
        Set(dynamization, "ResourceList", null);
        Reject(() => Invoke("ValidateDynamizationDocument", buttonType, dynamization, tags), "missing resource-list name");
    }

    // Issue #5: [Flags] enums such as HmiWindowFlag accept names separated by ',' or '|' and 'None'.
    static void FlagsEnumTests()
    {
        Type flags = typeof(Siemens.Engineering.HmiUnified.UI.Enum.HmiWindowFlag);
        string tiaDefault = "ShowCaption, ShowBorder, CanSize, CanMove, CanMaximize, CanClose";
        Check(Convert.ToInt64(Invoke("ParseEnumValue", flags, tiaDefault, "WindowFlags")) == 123,
            "flags default combination parses to 123");
        Check(Convert.ToInt64(Invoke("ParseEnumValue", flags, "None", "WindowFlags")) == 0, "flags None parses to 0");
        Check(Convert.ToInt64(Invoke("ParseEnumValue", flags, "canclose,showcaption", "WindowFlags")) == 65,
            "flags names are case-insensitive and comma-separated");
        Check(Convert.ToInt64(Invoke("ParseEnumValue", flags, "CanClose | ShowCaption", "WindowFlags")) == 65,
            "flags names may be separated by '|'");
        Check(Convert.ToInt64(Invoke("ParseEnumValue", flags, "AlwaysInParent", "WindowFlags")) == 128,
            "single flag parses");
        for (int value = 0; value <= 255; value++)
        {
            object expected = Enum.ToObject(flags, value);
            var text = ((JsonElement)Invoke("SerializePropertyValue", expected)).GetString();
            Check(Equals(Invoke("ParseEnumValue", flags, text, "WindowFlags"), expected),
                "HmiWindowFlag export text round-trips: " + text);
        }
        foreach (Type other in new[] { typeof(Siemens.Engineering.HmiUnified.UI.Enum.HmiVisibleAlarms),
            typeof(Siemens.Engineering.HmiUnified.UI.Enum.HmiScaleMode) })
        {
            long mask = 0;
            foreach (object member in Enum.GetValues(other))
            {
                mask |= Convert.ToInt64(member);
                var text = ((JsonElement)Invoke("SerializePropertyValue", member)).GetString();
                Check(Equals(Invoke("ParseEnumValue", other, text, "x"), member), other.Name + " member round-trips");
            }
            object all = Enum.ToObject(other, mask);
            var allText = ((JsonElement)Invoke("SerializePropertyValue", all)).GetString();
            Check(Equals(Invoke("ParseEnumValue", other, allText, "x"), all), other.Name + " full mask round-trips");
        }
        foreach (string bad in new[] { "Bogus", "ShowCaption, Bogus", "ShowCaption,,CanClose", "", " ", "123", "ShowCaption, 4" })
        {
            string value = bad;
            Reject(() => Invoke("ParseEnumValue", flags, value, "WindowFlags"), "invalid flags text '" + bad + "'");
        }
        Reject(() => Invoke("ParseEnumValue", typeof(Siemens.Engineering.HmiUnified.UI.Dynamization.Script.TriggerType), "99", "x"),
            "non-flags enum still rejects undefined numbers");
        Reject(() => Invoke("ParseEnumValue", typeof(ConditionType), "Bogus", "x"), "non-flags enum still rejects unknown names");
        Check(Equals(Invoke("ParseEnumValue", typeof(ConditionType), "singlebit", "x"), ConditionType.Singlebit),
            "non-flags enum still parses names");
        Reject(() => Invoke("ValidatePropertyValue", flags, JsonValue("\"Bogus\""), "WindowFlags"),
            "property validation rejects bad flags text");
        Invoke("ValidatePropertyValue", flags, JsonValue("\"None\""), "WindowFlags");
        Invoke("ValidatePropertyValue", flags, JsonValue("\"" + tiaDefault + "\""), "WindowFlags");
        Check(true, "property validation accepts None and the TIA default combination");
        Check(Convert.ToInt64(Invoke("ConvertJsonValue", flags, JsonValue("\"" + tiaDefault + "\""), "WindowFlags")) == 123,
            "flags JSON value converts");
        Check((bool)Invoke("IsPropertyValueEqual", Enum.ToObject(flags, 65), JsonValue("\"canclose|ShowCaption\""), "WindowFlags"),
            "flags read-back compares by value, not text order");
        Check(!(bool)Invoke("IsPropertyValueEqual", Enum.ToObject(flags, 65), JsonValue("\"CanClose\""), "WindowFlags"),
            "flags read-back detects a different combination");
        Check(!(bool)Invoke("IsPropertyValueEqual", Enum.ToObject(flags, 65), JsonValue("\"Bogus\""), "WindowFlags"),
            "flags read-back treats invalid text as a mismatch");
        Check((bool)Invoke("IsPropertyValueEqual", 12, JsonValue("12"), "Left"), "non-enum read-back unchanged");
    }

    // Issue #9: script read-back tolerates CRLF/LF and trailing whitespace only.
    static void ScriptComparisonTests()
    {
        string[][] equivalent = {
            new[] { "a\nb", "a\r\nb" }, new[] { "a\nb", "a\rb" }, new[] { "a\n\tb", "a  \n\tb\t" },
            new[] { "a\nb", "a\nb\n" }, new[] { "a\nb", "a\r\nb\r\n\r\n" }, new[] { "a\nb", "﻿a\nb" },
            new[] { null, "" }, new[] { "", "\r\n" }
        };
        foreach (string[] pair in equivalent)
        {
            Check((bool)Invoke("AreScriptsEquivalent", pair[0], pair[1]), "equivalent scripts: " + Json(pair));
            Check(Invoke("DescribeScriptDifference", pair[0], pair[1]) == null, "no difference described: " + Json(pair));
        }
        string[][] different = {
            new[] { "a\nb", "a\nc" }, new[] { "a\n b", "a\nb" }, new[] { "a\nb", "a\nb\nc" }, new[] { "a b", "a  b" }
        };
        foreach (string[] pair in different)
            Check(!(bool)Invoke("AreScriptsEquivalent", pair[0], pair[1]), "different scripts: " + Json(pair));
        string line3 = (string)Invoke("DescribeScriptDifference", "one\ntwo\nthree", "one\r\ntwo\r\nTHREE");
        Check(line3 != null && line3.Contains("line 3") && line3.Contains("THREE"), "difference names line 3: " + line3);
        string lines = (string)Invoke("DescribeScriptDifference", "one\ntwo", "one\ntwo\nthree");
        Check(lines.Contains("2 line(s)") && lines.Contains("3 line(s)"), "difference names line counts: " + lines);

        IList expected = NewList("HmiJsonEvent");
        IList actual = NewList("HmiJsonEvent");
        object expectedEvent = Document("HmiJsonEvent");
        Set(expectedEvent, "Type", "Up");
        Set(expectedEvent, "Script", "if (x) {\n  y();\n}\n");
        expected.Add(expectedEvent);
        object actualEvent = Document("HmiJsonEvent");
        Set(actualEvent, "Type", "up");
        Set(actualEvent, "Script", "if (x) {\r\n  y();  \r\n}");
        actual.Add(actualEvent);
        Invoke("VerifyItemEvents", actual, expected, "Main/Nav_F1");
        Check(true, "multi-line event script with CRLF and trailing spaces passes read-back");
        Set(actualEvent, "Script", "if (x) {\r\n  z();\r\n}");
        string eventError = Fails(() => Invoke("VerifyItemEvents", actual, expected, "Main/Nav_F1"), "changed event script");
        Check(eventError.Contains("line 2") && eventError.Contains("Main/Nav_F1"), "event mismatch is described: " + eventError);
        actual.Clear();
        string countError = Fails(() => Invoke("VerifyItemEvents", actual, expected, "Main/Nav_F1"), "missing event");
        Check(countError.Contains("Up"), "event count mismatch lists the types: " + countError);

        object expectedScript = Document("HmiJsonScriptDynamization");
        object actualScript = Document("HmiJsonScriptDynamization");
        Set(expectedScript, "Code", "let a = 1;\nreturn a;");
        Set(actualScript, "Code", "let a = 1;\r\nreturn a;\r\n");
        Set(expectedScript, "GlobalDefinitionCode", "");
        Set(actualScript, "GlobalDefinitionCode", "\r\n");
        Invoke("VerifyScriptDynamizationCode", actualScript, expectedScript, "Main/A/Visible");
        Check((string)Get(actualScript, "Code") == "let a = 1;\nreturn a;", "equivalent script dynamization code is normalized for the field compare");
        Set(actualScript, "Code", "return 2;");
        Fails(() => Invoke("VerifyScriptDynamizationCode", actualScript, expectedScript, "Main/A/Visible"),
            "changed script dynamization code");
    }

    // Issue #7: a missing or null text/dynamizations/events key means "leave unchanged".
    static void PartialItemTests()
    {
        object missing = Parse("HmiJsonItem", "{\"name\":\"A\",\"type\":\"HmiButton\",\"props\":{\"Left\":1}}");
        Check(Get(missing, "Text") == null && Get(missing, "Dynamizations") == null && Get(missing, "Events") == null,
            "missing item sections deserialize as null");
        Check(((string[])Invoke("GetUnchangedItemSections", missing)).Length == 3, "missing sections are reported");
        object nulls = Parse("HmiJsonItem", "{\"name\":\"A\",\"type\":\"HmiButton\",\"text\":null,\"dynamizations\":null,\"events\":null}");
        Check(Get(nulls, "Text") == null && Get(nulls, "Dynamizations") == null && Get(nulls, "Events") == null,
            "explicit null item sections mean no change");
        object empty = Parse("HmiJsonItem", "{\"name\":\"A\",\"type\":\"HmiButton\",\"text\":{},\"dynamizations\":[],\"events\":[]}");
        Check(Get(empty, "Text") != null && ((IList)Get(empty, "Dynamizations")).Count == 0 && ((IList)Get(empty, "Events")).Count == 0,
            "empty item sections stay empty (clear)");
        Check(((string[])Invoke("GetUnchangedItemSections", empty)).Length == 0, "complete item reports no unchanged sections");
        string serialized = JsonSerializer.Serialize(empty, empty.GetType(), HmiOptions());
        Check(serialized.Contains("\"text\": {}") && serialized.Contains("\"dynamizations\": []") && serialized.Contains("\"events\": []"),
            "export shape still writes text, dynamizations and events: " + serialized);

        string directory = Path.Combine(Path.GetTempPath(), "tia-json-partial-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "screens"));
        Directory.CreateDirectory(Path.Combine(directory, "alarms"));
        try
        {
            File.WriteAllText(Path.Combine(directory, "manifest.json"),
                "{\"format\":\"tia-mcp-hmi/1\",\"screenFiles\":[{\"name\":\"Main\",\"group\":null,\"path\":\"screens/main.json\"}]," +
                "\"tagFiles\":[],\"discreteAlarmFile\":\"alarms/discrete.json\",\"zOrder\":\"keep\"}");
            File.WriteAllText(Path.Combine(directory, "screens", "main.json"),
                "{\"format\":\"tia-mcp-hmi/1\",\"name\":\"Main\",\"group\":null,\"zOrder\":\"require\",\"props\":{\"Width\":800,\"Height\":480}," +
                "\"items\":[{\"name\":\"A\",\"type\":\"HmiButton\",\"props\":{\"Left\":10}}]}");
            File.WriteAllText(Path.Combine(directory, "alarms", "discrete.json"), "{\"format\":\"tia-mcp-hmi/1\",\"discreteAlarms\":[]}");
            object plan = InstanceInvoke(TiaManager.Instance, "ReadHmiJsonImportPlan", directory, false);
            object screen = ((IList)Get(plan, "Screens"))[0];
            object item = ((IList)Get(screen, "Items"))[0];
            Check(Get(item, "Events") == null && (string)Get(screen, "ZOrder") == "require",
                "import plan reads an item without events and the screen zOrder");
            File.WriteAllText(Path.Combine(directory, "manifest.json"),
                "{\"format\":\"tia-mcp-hmi/1\",\"screenFiles\":[{\"name\":\"Main\",\"group\":null,\"path\":\"screens/main.json\"}]," +
                "\"tagFiles\":[],\"discreteAlarmFile\":\"alarms/discrete.json\",\"zOrder\":\"sideways\"}");
            Reject(() => InstanceInvoke(TiaManager.Instance, "ReadHmiJsonImportPlan", directory, false), "unknown manifest zOrder");
        }
        finally { Directory.Delete(directory, true); }
    }

    static object Bitmask(string bitType, ulong condition, ulong relevant, string color)
    {
        object entry = Document("HmiJsonMappingEntry");
        Set(entry, "Type", "Bitmask");
        Set(entry, "BitDynamizationType", bitType);
        Set(entry, "Condition", JsonSerializer.SerializeToElement(condition));
        Set(entry, "Relevant", relevant);
        object value = Document("HmiJsonMappingValue");
        Set(value, "Type", "String");
        Set(value, "Value", JsonSerializer.SerializeToElement(color));
        Set(entry, "Value", value);
        return entry;
    }

    static object Converter(string conditionType, params object[] entries)
    {
        object converter = Document("HmiJsonValueConverter");
        Set(converter, "ConditionType", conditionType);
        foreach (object entry in entries)
            ((IList)Get(converter, "Entries")).Add(entry);
        return converter;
    }

    static string PlanMode(object existing, object desired)
    {
        return Get(Invoke("PlanMappingTableUpdate", existing, desired), "Mode").ToString();
    }

    // Issue #6: existing Singlebit/Bitmask tables are updated in place, never by deleting bitmask entries.
    static void MappingTablePlanTests()
    {
        object singlebit = Converter("Singlebit", Bitmask("SingleBit", 0, 1, "#E2E5E8"), Bitmask("SingleBit", 1, 1, "#E80000"));
        object sameSinglebit = Converter("Singlebit", Bitmask("SingleBit", 0, 1, "#E2E5E8"), Bitmask("SingleBit", 1, 1, "#E80000"));
        object plan = Invoke("PlanMappingTableUpdate", singlebit, sameSinglebit);
        Check(Get(plan, "Mode").ToString() == "UpdateBitmaskInPlace" && Json(Get(plan, "Pairing")) == "[0,1]",
            "unchanged Singlebit re-import is updated in place");
        Check(PlanMode(singlebit, Converter("Singlebit", Bitmask("SingleBit", 0, 1, "#00FF00"), Bitmask("SingleBit", 1, 1, "#0000FF")))
            == "UpdateBitmaskInPlace", "Singlebit colour change is updated in place");
        // Same masks per position: the in-place update and read-back are positional, so edited conditions that another
        // entry already had are written per position (identity pairing), not refused as a reorder.
        object swappedConditions = Invoke("PlanMappingTableUpdate", singlebit,
            Converter("Singlebit", Bitmask("SingleBit", 1, 1, "#E80000"), Bitmask("SingleBit", 0, 1, "#E2E5E8")));
        Check(Get(swappedConditions, "Mode").ToString() == "UpdateBitmaskInPlace" && Json(Get(swappedConditions, "Pairing")) == "[0,1]",
            "Singlebit conditions edited at the same positions are updated in place with the identity pairing");
        object sharedMask = Converter("Bitmask", Bitmask("MultiBit", 4, 20, "#000001"), Bitmask("MultiBit", 16, 20, "#000002"));
        object sharedEdited = Invoke("PlanMappingTableUpdate", sharedMask,
            Converter("Bitmask", Bitmask("MultiBit", 16, 20, "#00000A"), Bitmask("MultiBit", 4, 20, "#00000B")));
        Check(Get(sharedEdited, "Mode").ToString() == "UpdateBitmaskInPlace" && Json(Get(sharedEdited, "Pairing")) == "[0,1]",
            "MultiBit entries with a shared mask and edited conditions are updated in place with the identity pairing");
        object distinctMasks = Converter("Bitmask", Bitmask("MultiBit", 1, 1, "#000001"), Bitmask("MultiBit", 2, 2, "#000002"));
        object swapped = Invoke("PlanMappingTableUpdate", distinctMasks,
            Converter("Bitmask", Bitmask("MultiBit", 2, 2, "#000002"), Bitmask("MultiBit", 1, 1, "#000001")));
        // The read-back compares entries by position, so a reordered table is refused instead of failing verification.
        Check(Get(swapped, "Mode").ToString() == "RefuseEntryOrder" && ((string)Get(swapped, "Reason")).Contains("order") &&
            Json(Get(swapped, "Pairing")) == "[1,0]",
            "reordered entries with different masks are refused with an entry-order reason");
        string orderRefusal = (string)Invoke("DescribeMappingTablePlanConflict", "Lamp_1", "BackColor", swapped, "replace");
        Check(orderRefusal != null && orderRefusal.Contains("Lamp_1.BackColor") && orderRefusal.Contains("TIA's entry order"),
            "entry-order refusal applies even with onConflict replace: " + orderRefusal);
        Check(Invoke("DescribeMappingTablePlanConflict", "Lamp_1", "BackColor", plan, null) == null,
            "in-place plan has no conflict");
        object movedPlan = Invoke("PlanMappingTableUpdate", singlebit,
            Converter("Singlebit", Bitmask("SingleBit", 0, 8, "#E2E5E8"), Bitmask("SingleBit", 8, 8, "#E80000")));
        Check(Invoke("DescribeMappingTablePlanConflict", "Lamp_1", "BackColor", movedPlan, null) != null &&
            Invoke("DescribeMappingTablePlanConflict", "Lamp_1", "BackColor", movedPlan, "Replace") == null,
            "replace plan conflicts only without onConflict replace (dry run and apply share this check)");
        object moved = Invoke("PlanMappingTableUpdate", singlebit,
            Converter("Singlebit", Bitmask("SingleBit", 0, 8, "#E2E5E8"), Bitmask("SingleBit", 8, 8, "#E80000")));
        Check(Get(moved, "Mode").ToString() == "ReplaceDynamization" && ((string)Get(moved, "Reason")).Contains("relevant"),
            "Singlebit bit change needs a replace");
        object range = Document("HmiJsonMappingEntry");
        Set(range, "Type", "Range");
        Set(range, "From", Invoke("CreateMappingValue", 0));
        Set(range, "To", Invoke("CreateMappingValue", 1));
        object rangeConverter = Converter("Range", range);
        Check(PlanMode(singlebit, rangeConverter) == "ReplaceDynamization", "Singlebit to Range needs a replace");
        Check(PlanMode(rangeConverter, rangeConverter) == "RebuildEntries", "Range to Range rebuilds the entries");
        Check(PlanMode(Converter("None"), singlebit) == "RebuildEntries", "empty table to Singlebit uses the fresh path");
        Check(PlanMode(singlebit, Converter("Singlebit")) == "ReplaceDynamization", "Singlebit to an empty table needs a replace");
        object multi = Converter("Bitmask", Bitmask("MultiBit", 1, 1, "#000001"), Bitmask("MultiBit", 2, 2, "#000002"));
        Check(PlanMode(multi, Converter("Bitmask", Bitmask("MultiBit", 0, 1, "#000003"), Bitmask("MultiBit", 2, 2, "#000004")))
            == "UpdateBitmaskInPlace", "MultiBit with the same masks is updated in place");
        Check(PlanMode(multi, Converter("Bitmask", Bitmask("MultiBit", 1, 1, "#000001"))) == "ReplaceDynamization",
            "MultiBit with a different entry count needs a replace");
        Check(PlanMode(multi, Converter("Singlebit", Bitmask("SingleBit", 0, 1, "#0"), Bitmask("SingleBit", 1, 1, "#1")))
            == "ReplaceDynamization", "Bitmask to Singlebit needs a replace");
        string conflict = (string)Invoke("FormatMappingTableConflict", "Lamp_1", "BackColor", "reason");
        Check(conflict.Contains("Lamp_1.BackColor") && conflict.Contains("onConflict") && conflict.Contains("replace"),
            "mapping table refusal names item.property and onConflict");

        Check(Invoke("DescribeTagDynamizationTypeSwitch", "Singlebit", 2, 2, "Singlebit") == null,
            "hmi_set_tag_dynamization keeps an existing Singlebit table");
        Check(Invoke("DescribeTagDynamizationTypeSwitch", "None", 0, 0, "Singlebit") == null,
            "hmi_set_tag_dynamization fills an empty table");
        Check(Invoke("DescribeTagDynamizationTypeSwitch", "Range", 3, 0, "Range") == null,
            "hmi_set_tag_dynamization rebuilds a Range table");
        Check(Invoke("DescribeTagDynamizationTypeSwitch", "Singlebit", 2, 2, "Range") != null,
            "hmi_set_tag_dynamization refuses Singlebit to Range");
        Check(Invoke("DescribeTagDynamizationTypeSwitch", "Range", 2, 0, "Singlebit") != null,
            "hmi_set_tag_dynamization refuses Range to Singlebit");
        Check(Invoke("DescribeTagDynamizationTypeSwitch", "Singlebit", 3, 3, "Singlebit") != null,
            "hmi_set_tag_dynamization refuses an unexpected Singlebit layout");
    }

    // Issue #8: z-order check helpers.
    static void ZOrderTests()
    {
        Check(((IList)Invoke("FindZOrderDifferences", new List<string> { "A", "B", "C", "D" },
            new List<string> { "A", "B", "C", "D" })).Count == 0, "matching order has no differences");
        var front = (List<string>)Invoke("FindZOrderDifferences", new List<string> { "Back", "A", "B", "C", "D" },
            new List<string> { "C", "D", "Back", "A", "B" });
        Check(front.Count == 2 && front.Contains("C") && front.Contains("D"), "new items placed at the back are reported: " + Json(front));
        Check(((IList)Invoke("FindZOrderDifferences", new List<string> { "A", "B" },
            new List<string> { "a", "X", "b" })).Count == 0, "items not in the file are ignored, names case-insensitive");
        Check(((IList)Invoke("FindZOrderDifferences", new List<string> { "A", "B", "C" },
            new List<string> { "C", "B", "A" })).Count == 2, "reversed order reports all but one item");
        Check((string)Invoke("ResolveZOrderMode", null, null, "x") == "keep", "zOrder defaults to keep");
        Check((string)Invoke("ResolveZOrderMode", "require", null, "x") == "require", "manifest zOrder require");
        Check((string)Invoke("ResolveZOrderMode", "require", "Keep", "x") == "keep", "screen zOrder overrides manifest");
        Reject(() => Invoke("ResolveZOrderMode", null, "file", "screens/main.json"), "unsupported zOrder value");
        var names = new List<string> { "C" };
        string message = (string)Invoke("DescribeZOrderDifference", "Main", names, false);
        Check(message.Contains("Main") && message.Contains("'C'"), "z-order warning names the screen and items");
    }

    // Issue #22: patch format fields, bind guards and unknown-field warnings.
    static void PatchFormatTests()
    {
        object patch = Parse("HmiJsonPatch",
            "{\"format\":\"tia-mcp-hmi-patch/1\",\"extra\":1," +
            "\"require\":{\"screens\":[{\"name\":\"Main\",\"items\":[{\"name\":\"Lamp\",\"type\":\"HmiCircle\",\"kind\":\"x\"}]}]}," +
            "\"operations\":[{\"op\":\"bind\",\"screen\":\"Main\",\"item\":\"Lamp\",\"property\":\"BackColor\",\"tag\":\"New\"," +
            "\"expectTag\":\"Old\",\"bogus\":true}]}");
        object operation = ((IList)Get(patch, "Operations"))[0];
        Check(Get(operation, "ReadOnly") == null && Get(operation, "UseIndirectAddressing") == null &&
            (string)Get(operation, "ExpectTag") == "Old", "bind flags are optional and expectTag is read");
        var warnings = (List<string>)Invoke("CollectUnknownPatchFields", patch);
        Check(warnings.Count == 3 && warnings.Any(w => w.Contains("(root)") && w.Contains("extra")) &&
            warnings.Any(w => w.Contains("operations[0]") && w.Contains("bogus")) &&
            warnings.Any(w => w.Contains("require.screens[0].items[0]") && w.Contains("kind")),
            "unknown patch fields are reported: " + Json(warnings));
        Check(((List<string>)Invoke("CollectUnknownPatchFields", Parse("HmiJsonPatch",
            "{\"format\":\"tia-mcp-hmi-patch/1\",\"Operations\":[{\"Op\":\"delete\",\"Screen\":\"Main\",\"Item\":\"A\"}]}"))).Count == 0,
            "known patch fields in any case give no warnings");
        Check((bool)Invoke("ResolveBindFlag", null, true), "omitted bind flag keeps the existing true value");
        Check(!(bool)Invoke("ResolveBindFlag", null, null), "omitted bind flag is false for a new dynamization");
        Check(!(bool)Invoke("ResolveBindFlag", false, true), "explicit bind flag wins");
        Check(Invoke("CheckBindExpectTag", null, null, null) == null, "no expectTag guard");
        Check(Invoke("CheckBindExpectTag", "Old", "TagDynamization", "Old") == null, "expectTag matches");
        Check(Invoke("CheckBindExpectTag", "Old", "TagDynamization", "Other") != null, "expectTag mismatch fails");
        Check(Invoke("CheckBindExpectTag", "Old", null, null) != null, "expectTag without a dynamization fails");
        Check(Invoke("CheckBindExpectTag", "Old", "ScriptDynamization", null) != null, "expectTag on a script dynamization fails");
        Type button = typeof(HmiButton);
        Invoke("ValidatePatchBindDocument", button, "BackColor", "Replace");
        Invoke("ValidatePatchBindDocument", button, "BackColor", null);
        Check(true, "bind accepts a dynamizable property and onConflict replace/null");
        Reject(() => Invoke("ValidatePatchBindDocument", button, "BackColor", "merge"), "bind rejects unknown onConflict");
        Reject(() => Invoke("ValidatePatchBindDocument", button, "Font.Size", null), "bind rejects a dotted part path");
        Reject(() => Invoke("ValidatePatchBindDocument", button, "NoSuchProperty", null), "bind rejects an unknown property");
        Invoke("ValidatePatchProperty", button, "Left", JsonValue("286"));
        Check(true, "set accepts a numeric Left value");
        Reject(() => Invoke("ValidatePatchProperty", button, "Name", JsonValue("\"x\"")), "set rejects renames");
        var description = (Dictionary<string, object>)Invoke("DescribePatchOperation", operation);
        Check((string)description["readOnly"] == "keep" && (string)description["expectTag"] == "Old",
            "dry-run description shows kept bind flags and expectTag");
        object explicitPatch = Parse("HmiJsonPatch",
            "{\"format\":\"tia-mcp-hmi-patch/1\",\"operations\":[{\"op\":\"bind\",\"screen\":\"Main\",\"item\":\"Lamp\"," +
            "\"property\":\"BackColor\",\"tag\":\"New\",\"readOnly\":true,\"useIndirectAddressing\":false}]}");
        var explicitDescription = (Dictionary<string, object>)Invoke("DescribePatchOperation",
            ((IList)Get(explicitPatch, "Operations"))[0]);
        Check(explicitDescription["readOnly"] is bool && (bool)explicitDescription["readOnly"] &&
            explicitDescription["useIndirectAddressing"] is bool && !(bool)explicitDescription["useIndirectAddressing"],
            "dry-run description gives explicit bind flags as JSON booleans");
    }

    static void Live(int pid, string target, bool libraryTests)
    {
        var manager = TiaManager.Instance;
        var connected = manager.Connect(pid);
        Check(connected.Success && connected.ProcessId == pid, "exact project connection");
        var hmi = manager.FindHmiSoftware(target);
        var screens = ((IEnumerable)Invoke("EnumerateHmiScreens", hmi.Screens, hmi.ScreenGroups)).Cast<HmiScreen>().ToList();
        int screenCount = hmi.Screens.Count;
        string name = "__JsonRegression_" + Guid.NewGuid().ToString("N");
        var portal = (TiaPortal)typeof(TiaManager).GetField("_tiaPortal", BindingFlags.NonPublic | BindingFlags.Instance)
            .GetValue(manager);
        var allItems = screens.SelectMany(s => s.ScreenItems).ToList();
        try
        {
            if (libraryTests) LibraryTests(manager, portal, target);
            using (var access = portal.ExclusiveAccess("HMI JSON rollback-only regression tests"))
            using (var transaction = access.Transaction(manager.CurrentProject, "HMI JSON regression tests - rollback"))
            {
                HmiScreen temporary = hmi.Screens.Create(name);
                var bitmaskSource = temporary.ScreenItems.Create<HmiButton>("__BitmaskSource");
                var bitmaskDynamization = bitmaskSource.Dynamizations.Create<ExpressionDynamization>("BackColor");
                bitmaskDynamization.ValueConverter.MappingTable.ConditionType = ConditionType.Singlebit;
                var generatedBitmasks = bitmaskDynamization.ValueConverter.MappingTable.Entries
                    .OfType<MappingTableEntryBitmask>().ToList();
                if (bitmaskDynamization.ValueConverter.MappingTable.Entries.Count == 0)
                    generatedBitmasks = bitmaskDynamization.ValueConverter.MappingTable.Entries
                        .Create(BitDynamizationType.SingleBit).ToList();
                Check(generatedBitmasks.Count == 2, "TIA creates off/on entries for Singlebit dynamization");
                object bitmaskDocument = Invoke("CreateDynamizationDocument", bitmaskDynamization);
                var bitmaskTarget = temporary.ScreenItems.Create<HmiButton>("__BitmaskTarget");
                var bitmaskDynamizations = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(
                    typeof(TiaManager).GetNestedType("HmiJsonDynamization", BindingFlags.NonPublic)));
                bitmaskDynamizations.Add(bitmaskDocument);
                Invoke("ApplyDynamizations", hmi, bitmaskTarget, bitmaskDynamizations, true);
                object bitmaskReadBack = Invoke("CreateDynamizationDocument",
                    bitmaskTarget.Dynamizations.Find("BackColor"));
                Check(Json(bitmaskDocument) == Json(bitmaskReadBack),
                    "live bitmask converter API create and read-back");
                // Issue #6: applying the same Singlebit document again updates the existing table in place.
                Invoke("ApplyDynamizations", hmi, bitmaskTarget, bitmaskDynamizations, true);
                Check(Json(bitmaskDocument) == Json(Invoke("CreateDynamizationDocument",
                    bitmaskTarget.Dynamizations.Find("BackColor"))) &&
                    ((ExpressionDynamization)bitmaskTarget.Dynamizations.Find("BackColor")).ValueConverter
                        .MappingTable.Entries.Count == 2,
                    "live Singlebit re-apply updates the existing table in place");
                var existingTag = hmi.Tags.FirstOrDefault();
                if (existingTag != null)
                {
                    var singlebitTarget = temporary.ScreenItems.Create<HmiButton>("__SinglebitTarget");
                    singlebitTarget.Dynamizations.Create<TagDynamization>("BackColor");
                    // The public method opens its own transaction; inside this rollback-only transaction the
                    // body is called directly. Called twice: the second call must update the table in place.
                    for (int pass = 0; pass < 2; pass++)
                        InstanceInvoke(manager, "SetTagDynamizationInTransaction", name, singlebitTarget.Name,
                            "BackColor", existingTag.Name, "Singlebit", new List<HmiRangeEntryInput>
                            {
                                new HmiRangeEntryInput { From = 0, To = 0, Value = "#E2E5E8" },
                                new HmiRangeEntryInput { From = 1, To = 1, Value = "#E80000" }
                            }, target);
                    var singlebitTable = ((TagDynamization)singlebitTarget.Dynamizations.Find("BackColor"))
                        .ValueConverter.MappingTable;
                    var singlebitEntries = singlebitTable.Entries.OfType<MappingTableEntryBitmask>().ToList();
                    Check(singlebitTable.ConditionType == ConditionType.Singlebit && singlebitEntries.Count == 2,
                        "Singlebit setter reuses TIA-generated entries on an existing tag dynamization");
                    Check(((Color)singlebitEntries.Single(entry => entry.Condition == 0).Value).ToArgb() ==
                        Color.FromArgb(255, 226, 229, 232).ToArgb() &&
                        ((Color)singlebitEntries.Single(entry => entry.Condition != 0).Value).ToArgb() ==
                        Color.FromArgb(255, 232, 0, 0).ToArgb(),
                        "Singlebit setter applies values to the generated off/on entries");
                }
                int index = 0;
                foreach (var source in allItems)
                {
                    if (source is HmiCustomWidgetContainer) continue;
                    string contained = (source as HmiContainerBase) == null ? null : ((HmiContainerBase)source).ContainedType;
                    var item = (HmiScreenItemBase)Invoke("CreateScreenItem", temporary, "Item_" + index++,
                        source.GetType(), contained);
                    Check(item.GetType() == source.GetType(), "create " + source.GetType().Name);
                    object sourceInterface = Invoke("CreateInterfaceDocument", source);
                    if (sourceInterface != null)
                    {
                        Invoke("ApplyInterfaceDocument", item, sourceInterface);
                        Check(Json(sourceInterface) == Json(Invoke("CreateInterfaceDocument", item)),
                            "faceplate instance interface read-back");
                    }
                    var documents = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(
                        typeof(TiaManager).GetNestedType("HmiJsonDynamization", BindingFlags.NonPublic)));
                    foreach (var dynamization in source.Dynamizations)
                        documents.Add(Invoke("CreateDynamizationDocument", dynamization));
                    Invoke("ApplyDynamizations", hmi, item, documents, true);
                    Check(item.Dynamizations.Count == documents.Count, "dynamization count");
                    foreach (object expected in documents)
                    {
                        object actual = Invoke("CreateDynamizationDocument",
                            item.Dynamizations.Find((string)Get(expected, "Property")));
                        Check(Json(expected) == Json(actual), "converter and typed mapping read-back for " +
                            source.Name + "." + Get(expected, "Property") + "\nExpected: " + Json(expected) + "\nActual: " + Json(actual));
                    }
                    var events = Invoke("ReadItemEvents", source);
                    Invoke("ApplyItemEvents", item, events);
                    Check(Json(events) == Json(Invoke("ReadItemEvents", item)), "event script read-back");
                    if (documents.Count > 1)
                    {
                        var single = (IList)Activator.CreateInstance(documents.GetType());
                        single.Add(documents[0]);
                        Invoke("ApplyDynamizations", hmi, item, single, false);
                        Check(item.Dynamizations.Count == documents.Count, "patch binding preserves unrelated bindings");
                    }
                    foreach (object document in documents)
                    {
                        object converter = Get(document, "ValueConverter");
                        if (converter == null || String.IsNullOrEmpty((string)Get(converter, "Formula"))) continue;
                        Set(converter, "Formula", "");
                        Set(converter, "IsFormulaSelected", false);
                        var single = (IList)Activator.CreateInstance(documents.GetType());
                        single.Add(document);
                        Invoke("ApplyDynamizations", hmi, item, single, false);
                        object actual = Invoke("CreateDynamizationDocument",
                            item.Dynamizations.Find((string)Get(document, "Property")));
                        Check(Json(document) == Json(actual), "clearing an existing converter formula");
                    }
                }
                foreach (var tag in hmi.Tags.ToList().Where(t => String.IsNullOrEmpty(t.PlcTag)))
                {
                    object document = Invoke("CreateTagDocument", tag);
                    string tagName = "__JsonTag_" + Guid.NewGuid().ToString("N");
                    Set(document, "Name", tagName);
                    Invoke("ApplyTagDocument", hmi, document);
                    var actual = hmi.Tags.Find(tagName);
                    Check(actual.DataType == tag.DataType, "internal tag data type");
                    Check(Json(Invoke("CreateLimitDocument", actual.InitialMinValue) ?? new object()) ==
                        Json(Invoke("CreateLimitDocument", tag.InitialMinValue) ?? new object()), "unset tag limit");
                }
                foreach (var widget in allItems.OfType<HmiCustomWidgetContainer>())
                {
                    object expected = Invoke("CreateInterfaceDocument", widget);
                    Invoke("ApplyInterfaceDocument", widget, expected);
                    Check(Json(expected) == Json(Invoke("CreateInterfaceDocument", widget)), "SVG interface read-back");
                }
                // Deliberately never commit: every test object is rolled back.
            }
            Check(hmi.Screens.Count == screenCount && hmi.Screens.Find(name) == null, "temporary screen rolled back");
            Check(hmi.Tags.All(t => !t.Name.StartsWith("__JsonTag_", StringComparison.Ordinal)), "temporary tags rolled back");
        }
        finally { manager.Disconnect(); }
    }

    static void LibraryTests(TiaManager manager, TiaPortal portal, string target)
    {
        string directory = Path.Combine(Path.GetTempPath(), "tia-library-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var exported = manager.ExportHmiJson(directory, false, false, target);
            Check((int)exported["libraryTypeCount"] > 0, "native library definitions exported");
            Check(File.Exists(Path.Combine(directory, (string)exported["libraryFile"])), "native archive exists");
            string screenPath = Directory.GetFiles(Path.Combine(directory, "screens")).FirstOrDefault();
            if (screenPath != null)
            {
                byte[] original = File.ReadAllBytes(screenPath);
                File.AppendAllText(screenPath, " ");
                string edited = File.ReadAllText(screenPath);
                bool rejected = false;
                try { manager.ExportHmiJson(directory, false, false, target); }
                catch (IOException) { rejected = true; }
                Check(rejected && File.ReadAllText(screenPath) == edited, "existing library archive rejected before overwriting JSON");
                File.WriteAllBytes(screenPath, original);
            }
            Check((bool)manager.ImportHmiJson(directory, false, true, target)["dryRun"], "native archive dry-run accepted");
            object plan = InstanceInvoke(manager, "ReadHmiJsonImportPlan", directory, true);
            object package = InstanceInvoke(manager, "OpenHmiLibrary", plan);
            string originalLibrary = Json(Invoke("DescribeLibraryTypes", manager.CurrentProject.ProjectLibrary.TypeFolder));
            using ((IDisposable)package)
            using (var access = portal.ExclusiveAccess("HMI library rollback-only regression"))
            using (var transaction = access.Transaction(manager.CurrentProject, "HMI library regression - rollback"))
            {
                var library = Get(package, "Library");
                var folder = Get(library, "TypeFolder");
                var types = (IEnumerable)Invoke("EnumerateLibraryTypes", folder);
                var missing = (IList)Get(package, "MissingTypes");
                Check(missing.Count == 0, "matching versions do not need replacement");
                var source = (Siemens.Engineering.Library.UserGlobalLibrary)library;
                source.UpdateLibrary(types.Cast<Siemens.Engineering.Library.Types.ILibraryTypeOrFolderSelection>(),
                    manager.CurrentProject.ProjectLibrary);
                InstanceInvoke(manager, "ApplyHmiLibrary", package);
                Check(true, "native library copy is supported inside project rollback transaction");
            }
            Check(originalLibrary == Json(Invoke("DescribeLibraryTypes", manager.CurrentProject.ProjectLibrary.TypeFolder)),
                "project type/version inventory unchanged after rollback");
            string archive = Path.Combine(directory, (string)exported["libraryFile"]);
            File.AppendAllText(archive, "tampered");
            Reject(() => InstanceInvoke(manager, "ReadHmiJsonImportPlan", directory, true), "tampered native archive hash");
            object manifest = Get(plan, "Manifest");
            Set(manifest, "LibraryFile", "..\\escape.zal21");
            Reject(() => InstanceInvoke(manager, "OpenHmiLibrary", plan), "library archive traversal");
        }
        finally { Directory.Delete(directory, true); }
    }
}
