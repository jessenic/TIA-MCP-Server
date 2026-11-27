using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using TiaOpennessMcp.Protocol;
using TiaOpennessMcp.Tia;
using TiaOpennessMcp.Tools;

// Offline checks for the PLC metadata tools: OB attribute warnings and setter validation (#14),
// tia_get_block_code format selection (#16), compile up-to-date detection and compile state (#18).
// No TIA Portal or PLCSIM is started or attached.
internal static class PlcMetaTests
{
    private static int _checks;
    private static string _binaryDirectory;
    private static string _apiDirectory;

    private static int Main(string[] args)
    {
        _binaryDirectory = args[0];
        _apiDirectory = args[1];
        AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
        try
        {
            Run();
            Console.WriteLine("PASS: " + _checks + " offline PLC metadata checks.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name;
        foreach (string directory in new[] { _binaryDirectory, _apiDirectory })
        {
            foreach (string extension in new[] { ".dll", ".exe" })
            {
                string path = Path.Combine(directory, name + extension);
                if (File.Exists(path))
                    return Assembly.LoadFrom(path);
            }
        }
        return null;
    }

    private static object Invoke(string name, params object[] args)
    {
        MethodInfo method = typeof(TiaManager).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
        if (method == null) throw new InvalidOperationException("Missing TiaManager." + name);
        try { return method.Invoke(null, args); }
        catch (TargetInvocationException error) { throw error.InnerException; }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAILED: " + name);
        _checks++;
    }

    private static void Throws<T>(Action action, string name) where T : Exception
    {
        try { action(); }
        catch (T) { _checks++; return; }
        throw new InvalidOperationException("FAILED (no " + typeof(T).Name + "): " + name);
    }

    private static string Ob(string number, string secondaryType, string extra)
    {
        return "<?xml version=\"1.0\" encoding=\"utf-8\"?><Document><SW.Blocks.OB ID=\"0\"><AttributeList>" +
               "<Name>Cyclic interrupt</Name><Number>" + number + "</Number>" +
               (secondaryType == null ? "" : "<SecondaryType>" + secondaryType + "</SecondaryType>") +
               extra + "</AttributeList></SW.Blocks.OB></Document>";
    }

    private static List<string> Inspect(string xml)
    {
        return (List<string>)Invoke("InspectSimaticMlBlockAttributes", xml);
    }

    private static bool Normalize(string name, long value, string unit, out int apiValue, out string error)
    {
        var args = new object[] { name, value, unit, null, null, null };
        bool ok = (bool)Invoke("TryNormalizeBlockAttributeRequest", args);
        apiValue = (int)args[4];
        error = (string)args[5];
        return ok;
    }

    private static void Run()
    {
        ObAttributeChecks();
        BlockCodeFormatChecks();
        CompileChecks();
        RegistryChecks();
    }

    private static void ObAttributeChecks()
    {
        List<string> tooSmall = Inspect(Ob("30", "CyclicInterrupt", "<CyclicTime>100</CyclicTime><PhaseOffset>0</PhaseOffset>"));
        Check(tooSmall.Count == 1 && tooSmall[0].Contains("microseconds") && tooSmall[0].Contains("100000"),
            "CyclicTime 100 warns that the unit is microseconds and suggests 100000");
        Check(tooSmall[0].Contains("OB30"), "warning names the OB number");

        Check(Inspect(Ob("30", "CyclicInterrupt", "<CyclicTime ReadOnly=\"false\">100000</CyclicTime><PhaseOffset ReadOnly=\"false\">0</PhaseOffset>")).Count == 0,
            "CyclicTime 100000 with ReadOnly XML attributes gives no warning");

        string namespaced = "<Document xmlns=\"http://www.siemens.com/automation/Openness/SW/Interface/v5\"><SW.Blocks.OB><AttributeList>" +
                            "<Name>OB</Name><Number>31</Number><SecondaryType>CyclicInterrupt</SecondaryType><CyclicTime>50</CyclicTime></AttributeList></SW.Blocks.OB></Document>";
        Check(Inspect(namespaced).Count == 1, "elements are matched by local name in a namespaced document");

        List<string> notWholeMs = Inspect(Ob("30", "CyclicInterrupt", "<CyclicTime>100500</CyclicTime>"));
        Check(notWholeMs.Count == 1 && notWholeMs[0].Contains("whole number of milliseconds"), "CyclicTime 100500 warns about whole ms");

        List<string> phaseShift = Inspect(Ob("30", "CyclicInterrupt", "<CyclicTime>100000</CyclicTime><PhaseShift>0</PhaseShift>"));
        Check(phaseShift.Count == 1 && phaseShift[0].Contains("PhaseOffset"), "PhaseShift element warns to use PhaseOffset");

        List<string> cycleTime = Inspect(Ob("30", "CyclicInterrupt", "<CyclicTime>100000</CyclicTime><CyclicApplicationCycleTime>100</CyclicApplicationCycleTime>"));
        Check(cycleTime.Count == 1 && cycleTime[0].Contains("read-only"), "CyclicApplicationCycleTime warns that it is read-only");

        List<string> phase = Inspect(Ob("30", "CyclicInterrupt", "<CyclicTime>100000</CyclicTime><PhaseOffset>100000</PhaseOffset>"));
        Check(phase.Count == 1 && phase[0].Contains("smaller than CyclicTime"), "PhaseOffset >= CyclicTime warns");

        List<string> secondary = Inspect(Ob("30", "ProgramCycle", ""));
        Check(secondary.Count == 1 && secondary[0].Contains("CyclicInterrupt"), "OB30 with another SecondaryType warns");

        Check(Inspect(Ob("1", "ProgramCycle", "")).Count == 0, "Main OB1 gives no warning");
        Check(Inspect("<Document><SW.Blocks.FB><AttributeList><Name>FB</Name><Number>1</Number></AttributeList></SW.Blocks.FB></Document>").Count == 0,
            "FB gives no warning");
        Check(Inspect("<Document><SW.Blocks.OB>").Count == 0, "malformed XML gives no warning and does not throw");
        Check(Inspect(null).Count == 0, "null XML gives no warning");

        int api; string error;
        Check(Normalize("CyclicTime", 100, "ms", out api, out error) && api == 100000, "CyclicTime 100 ms -> 100000 us");
        Check(Normalize("cyclictime", 100000, "us", out api, out error) && api == 100000, "CyclicTime 100000 us, name case-insensitive");
        Check(!Normalize("CyclicTime", 100, null, out api, out error) && error.Contains("unit"), "CyclicTime without unit is rejected");
        Check(!Normalize("PhaseOffset", 0, "", out api, out error), "PhaseOffset without unit is rejected");
        Check(!Normalize("CyclicTime", 0, "ms", out api, out error), "CyclicTime 0 ms is rejected");
        Check(!Normalize("CyclicTime", 60001, "ms", out api, out error), "CyclicTime 60001 ms is rejected");
        Check(Normalize("CyclicTime", 60000, "ms", out api, out error) && api == 60000000, "CyclicTime 60000 ms is the maximum");
        Check(!Normalize("PhaseOffset", -1, "us", out api, out error), "negative PhaseOffset is rejected");
        Check(Normalize("PhaseOffset", 20, "ms", out api, out error) && api == 20000, "PhaseOffset 20 ms -> 20000 us");
        Check(Normalize("PriorityNumber", 14, null, out api, out error) && api == 14, "PriorityNumber 14 without unit");
        Check(!Normalize("PriorityNumber", 14, "ms", out api, out error) && error.Contains("no unit"), "PriorityNumber with unit is rejected");
        Check(!Normalize("PriorityNumber", 0, null, out api, out error), "PriorityNumber 0 is rejected");
        Check(!Normalize("PhaseShift", 0, "us", out api, out error) && error.Contains("CyclicTime, PhaseOffset, PriorityNumber"),
            "PhaseShift is rejected with the allow-list");
        Check(!Normalize("CyclicApplicationCycleTime", 1, "us", out api, out error), "CyclicApplicationCycleTime is rejected");
        Check(!Normalize("CyclicTime", 3000000000L, "us", out api, out error), "value above Int32 is rejected");
        Check(!Normalize("CyclicTime", long.MaxValue, "ms", out api, out error), "ms overflow is rejected");

        var convertArgs = new object[] { "CyclicTime", 100L, "us", null, null };
        Check((bool)Invoke("TryConvertBlockAttributeValue", convertArgs) && (long)convertArgs[3] == 100L,
            "expected_current_value 100 us converts without range check");

        Check((string)Invoke("FormatMicroseconds", 100L) == "100 us (0.1 ms)", "FormatMicroseconds 100");
        Check((string)Invoke("FormatMicroseconds", 100000L) == "100000 us (100 ms)", "FormatMicroseconds 100000");
        Check((bool)Invoke("IsCyclicInterruptSecondaryType", "cyclicinterrupt"), "SecondaryType compare is case-insensitive");
        Check(!(bool)Invoke("IsCyclicInterruptSecondaryType", new object[] { null }), "null SecondaryType is not cyclic");

        string notes = (string)Invoke("AppendImportNotes", "Imported.", new List<string> { "read back" }, new List<string> { "w1" });
        Check(notes.StartsWith("Imported.") && notes.Contains("read back") && notes.Contains("Warnings:") && notes.Contains("- w1"),
            "import text keeps the message and appends read-back and warnings");
        Check((string)Invoke("AppendImportNotes", "Imported.", new List<string>(), new List<string>()) == "Imported.",
            "import text is unchanged without notes");

        string attributeJson = JsonSerializer.Serialize(new PlcMetadataAttribute { Name = "IsConsistent", Status = "available", Value = true });
        Check(!attributeJson.Contains("Unit") && !attributeJson.Contains("ValueMs"), "attributes without unit keep their old JSON shape");
        string timeJson = JsonSerializer.Serialize(new PlcMetadataAttribute { Name = "CyclicTime", Status = "available", Value = 100000L, Unit = "us", ValueMs = 100 });
        Check(timeJson.Contains("\"Unit\":\"us\"") && timeJson.Contains("\"ValueMs\":100"), "time attributes carry unit and ms");
    }

    private static void BlockCodeFormatChecks()
    {
        Check((string)Invoke("ResolveBlockCodeFormat", null, "SCL") == "source", "auto SCL -> source");
        Check((string)Invoke("ResolveBlockCodeFormat", "auto", "STL") == "source", "auto STL -> source");
        Check((string)Invoke("ResolveBlockCodeFormat", "", "DB") == "source", "auto DB -> source");
        Check((string)Invoke("ResolveBlockCodeFormat", null, "LAD") == "xml", "auto LAD -> xml");
        Check((string)Invoke("ResolveBlockCodeFormat", null, "GRAPH") == "xml", "auto GRAPH -> xml");
        Check((string)Invoke("ResolveBlockCodeFormat", "xml", "SCL") == "xml", "explicit xml");
        Check((string)Invoke("ResolveBlockCodeFormat", "source", "LAD") == "source", "explicit source stays source");
        Check((string)Invoke("ResolveBlockCodeFormat", "SCL", "SCL") == "source", "'scl' is an alias of source");
        Throws<ArgumentException>(() => Invoke("ResolveBlockCodeFormat", "simatic_sd", "SCL"), "unknown format is rejected");

        Check((string)Invoke("SourceExtensionFor", "SCL") == ".scl", "SCL -> .scl");
        Check((string)Invoke("SourceExtensionFor", "STL") == ".awl", "STL -> .awl");
        Check((string)Invoke("SourceExtensionFor", "DB") == ".db", "DB -> .db");
        Check(Invoke("SourceExtensionFor", "LAD") == null, "LAD has no source");
        Check(Invoke("SourceExtensionFor", "FBD") == null, "FBD has no source");
        Check((string)Invoke("SourceExtensionFor", "UDT") == ".udt", "PLC data type -> .udt");
        Check((string)Invoke("ResolveBlockCodeFormat", null, "UDT") == "source", "auto UDT -> source");

        var sourceOnly = new BlockExportResult { Name = "FB_Pump", Format = "source", Source = "FUNCTION_BLOCK", SourceFileExtension = ".scl" };
        string json = CallToolResult.Json(sourceOnly).Content[0].Text;
        Check(!json.Contains("XmlContent") && !json.Contains("ExtractedScl"), "source result leaves out XmlContent and ExtractedScl");
        Check(json.Contains("\"Source\"") && json.Contains("\"Format\": \"source\""), "source result carries Source and Format");
    }

    private static void CompileChecks()
    {
        Check((bool)Invoke("IsCompileUpToDate", "Success", 0, 0, (int?)0, new string[0]), "no changed compile date -> up to date");
        Check(!(bool)Invoke("IsCompileUpToDate", "Success", 0, 0, (int?)2, new[] { "No block was compiled. All blocks are up-to-date." }),
            "changed compile dates win over the text");
        Check(!(bool)Invoke("IsCompileUpToDate", "Warning", 0, 1, (int?)0, new string[0]), "warnings -> not up to date");
        Check(!(bool)Invoke("IsCompileUpToDate", "Error", 1, 0, (int?)0, new string[0]), "errors -> not up to date");
        Check((bool)Invoke("IsCompileUpToDate", "Success", 0, 0, null, new[] { "No block was compiled. All blocks are up-to-date." }),
            "text fallback when compile dates are unknown");
        Check(!(bool)Invoke("IsCompileUpToDate", "Success", 0, 0, null, new[] { "Compiling finished (errors: 0; warnings: 0)" }),
            "no text hint and unknown dates -> not claimed up to date");

        var t1 = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var before = new Dictionary<string, DateTime?> { { "/A", t1 }, { "/B", t1 } };
        var same = new Dictionary<string, DateTime?> { { "/A", t1 }, { "/B", t1 } };
        var changed = new Dictionary<string, DateTime?> { { "/A", t1.AddMinutes(1) }, { "/B", t1 }, { "/C", t1 } };
        Check((int?)Invoke("CountChangedCompileDates", before, same) == 0, "unchanged compile dates");
        Check((int?)Invoke("CountChangedCompileDates", before, changed) == 2, "changed and new blocks are counted");
        var unreadableBefore = new Dictionary<string, DateTime?> { { "/A", t1 }, { "/B", null } };
        var unreadableAfter = new Dictionary<string, DateTime?> { { "/A", t1 }, { "/B", null } };
        Check(Invoke("CountChangedCompileDates", unreadableBefore, unreadableAfter) == null,
            "an unreadable compile date (null -> null) is unknown, not unchanged");
        var allUnreadable = new Dictionary<string, DateTime?> { { "/A", null }, { "/B", null } };
        Check(Invoke("CountChangedCompileDates", allUnreadable, allUnreadable) == null, "no readable compile date -> unknown");
        Check(Invoke("CountChangedCompileDates", before, new Dictionary<string, DateTime?> { { "/A", t1 }, { "/B", null } }) == null,
            "a date that became unreadable is unknown");
        Check((int?)Invoke("CountChangedCompileDates", unreadableBefore, new Dictionary<string, DateTime?> { { "/A", t1.AddMinutes(1) }, { "/B", null } }) == 1,
            "a real change is still reported when another date is unreadable");
        Check(!(bool)Invoke("IsCompileUpToDate", "Success", 0, 0, Invoke("CountChangedCompileDates", allUnreadable, allUnreadable),
                new[] { "Compiling finished (errors: 0; warnings: 0)" }),
            "unreadable compile dates are not reported as up to date");

        var states = new PlcCompileStateResult { PlcName = "PLC_1", TypeCount = 2, InconsistentTypes = 0, UnknownTypes = 1, Types = new List<PlcCompileStateType>() };
        string typesJson = JsonSerializer.Serialize(states);
        Check(typesJson.Contains("\"UnknownTypes\":1") && typesJson.Contains("\"InconsistentTypes\":0"), "type totals separate unknown from inconsistent");

        Check((string)Invoke("FormatCompileStateDate", (DateTime?)t1) == "2026-01-01T10:00:00.0000000Z", "dates are ISO 8601");
        Check(Invoke("FormatCompileStateDate", new object[] { null }) == null, "missing date stays null");

        var hmiReport = new CompilationReport { State = "Success" };
        string hmiJson = JsonSerializer.Serialize(hmiReport);
        using (JsonDocument document = JsonDocument.Parse(hmiJson))
        {
            string fields = string.Join(",", document.RootElement.EnumerateObject().Select(p => p.Name));
            Check(fields == "State,ErrorCount,WarningCount,Messages", "HMI compile report keeps its field set: " + fields);
        }
        var plcReport = new CompilationReport { State = "Success", UpToDate = true, Note = "n" };
        string plcJson = JsonSerializer.Serialize(plcReport);
        Check(plcJson.Contains("\"UpToDate\":true") && plcJson.Contains("\"Note\":\"n\""), "PLC compile report carries UpToDate and Note");

        string stateJson = JsonSerializer.Serialize(new PlcCompileStateResult { PlcName = "PLC_1" });
        Check(!stateJson.Contains("\"Types\"") && stateJson.Contains("\"Blocks\":[]"), "compile state leaves out Types when not requested");
    }

    private static void RegistryChecks()
    {
        var registry = new ToolRegistry();
        var tools = registry.GetTools();
        ToolDefinition setter = tools.Single(tool => tool.Name == "tia_set_block_attribute");
        Check(string.Join(",", setter.InputSchema.Required) == "block_name,attribute_name,value", "setter required fields");
        Check(string.Join(",", setter.InputSchema.Properties["attribute_name"].Enum) == "CyclicTime,PhaseOffset,PriorityNumber", "setter attribute enum");
        Check(string.Join(",", setter.InputSchema.Properties["unit"].Enum) == "ms,us", "setter unit enum");
        Check(tools.Any(tool => tool.Name == "tia_get_compile_state"), "compile state tool is registered");
        ToolDefinition code = tools.Single(tool => tool.Name == "tia_get_block_code");
        Check(string.Join(",", code.InputSchema.Properties["format"].Enum) == "auto,source,xml", "block code format enum");
        Check((string)code.InputSchema.Properties["format"].Default == "auto", "block code format default");

        // Argument errors are reported before any TIA Portal access.
        using (JsonDocument args = JsonDocument.Parse("{\"block_name\":\"OB\",\"attribute_name\":\"CyclicTime\",\"value\":\"100\",\"unit\":\"ms\"}"))
        {
            CallToolResult result = registry.Execute("tia_set_block_attribute", args.RootElement.Clone());
            Check(result.IsError && result.Content[0].Text.Contains("value must be an integer"), "string value is rejected as non-integer");
        }
        using (JsonDocument args = JsonDocument.Parse("{\"block_name\":\"OB\",\"attribute_name\":\"CyclicTime\",\"value\":1.5,\"unit\":\"ms\"}"))
        {
            CallToolResult result = registry.Execute("tia_set_block_attribute", args.RootElement.Clone());
            Check(result.IsError && result.Content[0].Text.Contains("value must be an integer"), "fractional value is rejected");
        }
    }
}
