using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using TiaOpennessMcp.Protocol;
using TiaOpennessMcp.Tia;
using TiaOpennessMcp.Tools;

internal static class HmiToolBehaviorTests
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
            Console.WriteLine("PASS: " + _checks + " offline HMI tool behavior checks.");
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

    private static void Run()
    {
        var fake = new FakeHmiToolOperations();
        var registry = new ToolRegistry(fake);

        Check(registry.GetTools().Any(tool => tool.Name == "hmi_get_js_type_definitions"),
            "type-definition tool is registered");
        var resources = McpResourceCatalog.List().Resources;
        ResourceDefinition typeDefinitions = resources.Single(resource =>
            resource.Uri == "tia://docs/hmi-javascript-type-definitions");
        Check(typeDefinitions.MimeType == "text/typescript", "type-definition resource MIME type");
        ResourceContent typeContent = McpResourceCatalog.Read(typeDefinitions.Uri).Contents.Single();
        Check(typeContent.Text.Contains("SetPropertyValue") &&
            typeContent.Text.Contains("not Siemens' complete"),
            "resource contains documented declarations and scope disclaimer");
        Check(!typeContent.Text.Contains("GetTagValue"),
            "unverified tag-read API is not advertised");

        ResourceDefinition toolsReference = resources.Single(resource =>
            resource.Uri == "tia://docs/tools-reference");
        Check(toolsReference.MimeType == "text/markdown" &&
            McpResourceCatalog.Read(toolsReference.Uri).Contents.Single().MimeType == "text/markdown",
            "existing documentation resources retain their Markdown MIME type");

        CallToolResult definitionsResult = new ToolRegistry().Execute(
            "hmi_get_js_type_definitions",
            Arguments("{}"));
        Check(!definitionsResult.IsError, "type-definition tool succeeds without a TIA connection");
        Check(FirstText(definitionsResult).Contains("tia://docs/hmi-javascript-type-definitions") &&
            FirstText(definitionsResult).Contains("SetPropertyValue"),
            "type-definition tool returns the bundled declaration resource");

        CallToolResult itemResult = registry.Execute(
            "hmi_get_screen_item",
            Arguments("{\"screen_name\":\"Main\",\"item_name\":\"btn_Start\",\"target_name\":\"HMI_RT\"}"));
        Check(!itemResult.IsError && fake.LastOperation == "get-item",
            "item read dispatches to the HMI operations backend");
        Check(fake.ScreenName == "Main" && fake.ItemName == "btn_Start" && fake.TargetName == "HMI_RT",
            "item read forwards screen, item, and target");
        Check(FirstText(itemResult).Contains("\"Visible\": false"),
            "item read returns the backend's complete item document");

        CallToolResult updateResult = registry.Execute(
            "hmi_update_screen_item",
            Arguments("{\"screen_name\":\"Main\",\"item_name\":\"btn_Start\",\"item_properties\":{\"Visible\":false,\"Top\":618}}"));
        Check(!updateResult.IsError && fake.LastOperation == "update-item",
            "item update dispatches to the HMI operations backend");
        Check(fake.Properties.GetProperty("Visible").GetBoolean() == false &&
            fake.Properties.GetProperty("Top").GetInt32() == 618,
            "item update forwards all property values");

        CallToolResult scriptReadResult = registry.Execute(
            "hmi_read_event_scripts",
            Arguments("{\"screen_name\":\"Main\",\"item_name\":\"btn_Start\"}"));
        Check(!scriptReadResult.IsError && fake.LastOperation == "read-events",
            "event-script read dispatches to the HMI operations backend");
        Check(FirstText(scriptReadResult).Contains("\"Loaded\"") &&
            FirstText(scriptReadResult).Contains("existing script"),
            "event-script read returns supported events and script bodies");

        CallToolResult screenScriptReadResult = registry.Execute(
            "hmi_read_event_scripts",
            Arguments("{\"screen_name\":\"Main\"}"));
        Check(!screenScriptReadResult.IsError && fake.LastOperation == "read-events" &&
            fake.ItemName == null && FirstText(screenScriptReadResult).Contains("\"screen\""),
            "event-script read targets screen events when item_name is omitted");

        CallToolResult scriptUpdateResult = registry.Execute(
            "hmi_set_event_script",
            Arguments("{\"screen_name\":\"Main\",\"item_name\":\"btn_Start\",\"event_type\":\"Tapped\",\"script\":\"return true;\",\"target_name\":\"HMI_RT\"}"));
        Check(!scriptUpdateResult.IsError && fake.LastOperation == "set-event",
            "event-script update dispatches to the HMI operations backend");
        Check(fake.EventType == "Tapped" && fake.ScriptCode == "return true;" &&
            fake.ItemName == "btn_Start" && fake.TargetName == "HMI_RT",
            "event-script update forwards event, source, item, and target");

        CallToolResult screenScriptUpdateResult = registry.Execute(
            "hmi_set_event_script",
            Arguments("{\"screen_name\":\"Main\",\"event_type\":\"Loaded\",\"script\":\"return true;\"}"));
        Check(!screenScriptUpdateResult.IsError && fake.LastOperation == "set-event" &&
            fake.EventType == "Loaded" && fake.ItemName == null,
            "event-script update targets a screen event when item_name is omitted");

        CallToolResult expressionResult = registry.Execute(
            "hmi_set_expression_dynamization",
            Arguments("{\"screen_name\":\"Main\",\"item_name\":\"btn_Start\",\"property_name\":\"Top\",\"expression\":\"618\",\"result_type\":\"Int32\",\"on_conflict\":\"replace\"}"));
        Check(!expressionResult.IsError && fake.LastOperation == "set-expression",
            "expression update dispatches to the HMI operations backend");
        Check(fake.PropertyName == "Top" && fake.Expression == "618" &&
            fake.ResultType == "Int32" && fake.OnConflict == "replace",
            "expression update forwards formula, declared type, and conflict policy");

        CallToolResult expressionEntriesResult = registry.Execute(
            "hmi_set_expression_dynamization_entries",
            Arguments("{\"screen_name\":\"Main\",\"item_name\":\"btn_Start\",\"property_name\":\"Top\",\"entries\":[{\"condition\":\"mask 1\",\"value\":{\"type\":\"Int32\",\"value\":\"500\"}},{\"condition\":\"mask 2\",\"value\":{\"type\":\"Int32\",\"value\":\"400\"}}],\"on_conflict\":\"replace\",\"target_name\":\"HMI_RT\"}"));
        Check(!expressionEntriesResult.IsError && fake.LastOperation == "set-expression-entries",
            "expression condition rows dispatch to the HMI operations backend");
        Check(fake.ScreenName == "Main" && fake.ItemName == "btn_Start" &&
            fake.PropertyName == "Top" && fake.OnConflict == "replace" &&
            fake.TargetName == "HMI_RT" && fake.ExpressionEntries.GetArrayLength() == 2 &&
            fake.ExpressionEntries[0].GetProperty("value").GetProperty("value").GetString() == "500",
            "expression condition rows preserve order, values, and target arguments");

        CallToolResult deleteDynamizationResult = registry.Execute(
            "hmi_delete_dynamization",
            Arguments("{\"screen_name\":\"Main\",\"item_name\":\"btn_Start\",\"property_name\":\"BackColor\",\"target_name\":\"HMI_RT\"}"));
        Check(!deleteDynamizationResult.IsError && fake.LastOperation == "delete-dynamization",
            "dynamization deletion dispatches to the HMI operations backend");
        Check(fake.ScreenName == "Main" && fake.ItemName == "btn_Start" &&
            fake.PropertyName == "BackColor" && fake.TargetName == "HMI_RT" &&
            FirstText(deleteDynamizationResult).Contains("\"deleted\": true"),
            "dynamization deletion forwards its exact target and returns the backend result");

        int callsBeforeInvalidInput = fake.CallCount;
        CallToolResult invalidResult;
        string invalidInputLog;
        TextWriter originalError = Console.Error;
        using (var expectedError = new StringWriter())
        {
            Console.SetError(expectedError);
            try
            {
                invalidResult = registry.Execute("hmi_set_event_script", Arguments("{\"screen_name\":\"Main\"}"));
            }
            finally
            {
                Console.SetError(originalError);
            }
            invalidInputLog = expectedError.ToString();
        }
        Check(invalidResult.IsError && fake.CallCount == callsBeforeInvalidInput &&
            invalidInputLog.Contains("event_type is required"),
            "missing required event arguments fail before backend dispatch");

        TestItemPropertyConversion();
        TestManagerInputGuards();
        TestExpressionMappingEntryParsing();
        TestHmiCompileTool(registry, fake);
        TestHmiTargetSelection();
        TestHmiRuntimeTools(registry, fake);
        TestStartScreenHelpers();
    }

    private static void TestHmiCompileTool(ToolRegistry registry, FakeHmiToolOperations fake)
    {
        ToolDefinition compileTool = registry.GetTools().Single(tool => tool.Name == "hmi_compile");
        Check(compileTool.InputSchema.Properties.ContainsKey("target_name") &&
            compileTool.InputSchema.Properties.ContainsKey("target_device"),
            "hmi_compile exposes target_name and the target_device alias");

        CallToolResult noTarget = registry.Execute("hmi_compile", Arguments("{}"));
        Check(!noTarget.IsError && fake.LastOperation == "compile" && fake.TargetName == null,
            "hmi_compile without a target lets the backend auto-select the only HMI");
        Check(FirstText(noTarget).Contains("\"CompiledObject\"") && FirstText(noTarget).Contains("HMI_RT_1"),
            "hmi_compile returns the compiled target and object");

        registry.Execute("hmi_compile", Arguments("{\"target_name\":\"HMI_RT_1\"}"));
        Check(fake.LastOperation == "compile" && fake.TargetName == "HMI_RT_1",
            "hmi_compile forwards a software name as target_name");

        registry.Execute("hmi_compile", Arguments("{\"target_device\":\"HMI_1\"}"));
        Check(fake.TargetName == "HMI_1", "hmi_compile forwards the target_device alias");

        registry.Execute("hmi_compile", Arguments("{\"target_name\":\"HMI_RT_1\",\"target_device\":\"hmi_rt_1\"}"));
        Check(fake.TargetName == "HMI_RT_1", "hmi_compile accepts matching target_name and target_device");

        int callsBefore = fake.CallCount;
        CallToolResult conflicting = registry.Execute("hmi_compile", Arguments("{\"target_name\":\"HMI_RT_1\",\"target_device\":\"HMI_2\"}"));
        Check(conflicting.IsError && fake.CallCount == callsBefore,
            "hmi_compile rejects differing target_name and target_device before backend dispatch");

        string plcReport = JsonSerializer.Serialize(new CompilationReport { State = "Success" });
        Check(!plcReport.Contains("Target") && !plcReport.Contains("DeviceName") && !plcReport.Contains("CompiledObject"),
            "PLC compilation reports keep their JSON shape without the HMI-only fields");
    }

    private static void TestHmiTargetSelection()
    {
        var single = new List<HmiTargetInfo> { Target("HMI_1", "HMI_RT_1", "HMI_RT_1") };
        Check(SelectTarget(single, null).SoftwareName == "HMI_RT_1", "the only HMI is selected without a selector");
        Check(SelectTarget(single, "HMI_RT_1").SoftwareName == "HMI_RT_1", "software name selects the HMI");
        Check(SelectTarget(single, "HMI_1").SoftwareName == "HMI_RT_1", "device name selects the HMI");
        Check(SelectTarget(single, "hmi_rt_1").SoftwareName == "HMI_RT_1", "selection is case-insensitive");
        Throws<InvalidOperationException>(() => SelectTarget(single, "HMI"),
            "a partial name does not select an HMI");
        try
        {
            SelectTarget(single, "PLC_1");
            throw new InvalidOperationException("FAIL: an unknown selector must fail");
        }
        catch (InvalidOperationException error)
        {
            Check(error.Message.Contains("HMI_RT_1") && error.Message.Contains("HMI_1") && !error.Message.StartsWith("FAIL"),
                "an unknown selector fails and lists the available HMI targets");
        }

        var two = new List<HmiTargetInfo>
        {
            Target("HMI_1", "HMI_RT_1", "HMI_RT_1"),
            Target("PC-System_1", "HMI_RT_2", "HMI_RT_2")
        };
        try
        {
            SelectTarget(two, null);
            throw new InvalidOperationException("FAIL: several HMIs without a selector must fail");
        }
        catch (InvalidOperationException error)
        {
            Check(error.Message.Contains("matched multiple") && error.Message.Contains("HMI_RT_1") && error.Message.Contains("HMI_RT_2"),
                "several HMIs without a selector fail and list the candidates");
        }
        Check(SelectTarget(two, "PC-System_1").SoftwareName == "HMI_RT_2", "a device name picks one of several HMIs");
        Throws<InvalidOperationException>(() => SelectTarget(new List<HmiTargetInfo>(), null),
            "a project without an HMI fails");
    }

    private static void TestHmiRuntimeTools(ToolRegistry registry, FakeHmiToolOperations fake)
    {
        Check(registry.GetTools().Any(tool => tool.Name == "hmi_get_runtime_settings") &&
            registry.GetTools().Any(tool => tool.Name == "hmi_set_start_screen"),
            "runtime-settings tools are registered");

        CallToolResult settings = registry.Execute("hmi_get_runtime_settings", Arguments("{\"target_name\":\"HMI_RT_1\"}"));
        Check(!settings.IsError && fake.LastOperation == "get-runtime-settings" && fake.TargetName == "HMI_RT_1",
            "runtime-settings read dispatches to the backend with its target");
        Check(FirstText(settings).Contains("\"StartScreen\": \"Frame\""),
            "runtime-settings read returns the backend's start screen");

        CallToolResult set = registry.Execute("hmi_set_start_screen",
            Arguments("{\"screen_name\":\"Frame\",\"target_name\":\"HMI_RT_1\",\"expected_current_start_screen\":\"Main\",\"on_conflict\":\"fail\"}"));
        Check(!set.IsError && fake.LastOperation == "set-start-screen" && fake.ScreenName == "Frame" &&
            fake.TargetName == "HMI_RT_1" && fake.ExpectedCurrentStartScreen == "Main" && fake.OnConflict == "fail",
            "start-screen update forwards screen, target, expected current value, and conflict policy");

        registry.Execute("hmi_set_start_screen", Arguments("{\"screen_name\":\"Frame\",\"expected_current_start_screen\":\"\",\"on_conflict\":\"fail\"}"));
        Check(fake.ExpectedCurrentStartScreen == "", "an empty expected start screen is forwarded as empty, not dropped");

        fake.NextStartScreenSuccess = false;
        CallToolResult failed = registry.Execute("hmi_set_start_screen", Arguments("{\"screen_name\":\"Frame\",\"on_conflict\":\"fail\"}"));
        fake.NextStartScreenSuccess = true;
        Check(failed.IsError && FirstText(failed).Contains("validation_failed"),
            "a failed start-screen update is reported as a tool error");

        int callsBefore = fake.CallCount;
        TextWriter originalError = Console.Error;
        CallToolResult missingScreen;
        CallToolResult missingConflict;
        using (var expectedError = new StringWriter())
        {
            Console.SetError(expectedError);
            try
            {
                missingScreen = registry.Execute("hmi_set_start_screen", Arguments("{\"on_conflict\":\"fail\"}"));
                missingConflict = registry.Execute("hmi_set_start_screen", Arguments("{\"screen_name\":\"Frame\"}"));
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
        Check(missingScreen.IsError && missingConflict.IsError && fake.CallCount == callsBefore,
            "start-screen update without screen_name or on_conflict fails before backend dispatch");

        TiaManager manager = TiaManager.Instance;
        Throws<ArgumentException>(() => manager.SetHmiStartScreen("Frame", null, null, "replace"),
            "start-screen update rejects any conflict policy other than fail before project access");
        Throws<ArgumentException>(() => manager.SetHmiStartScreen(" ", null, null, "fail"),
            "start-screen update rejects a blank screen name before project access");
    }

    private static void TestStartScreenHelpers()
    {
        var names = new List<string> { "Frame", "Main", "Overview" };
        Check((string)InvokeManagerHelperResult("ResolveExactScreenName", names, "Main") == "Main",
            "an exact screen name resolves");
        try
        {
            InvokeManagerHelperResult("ResolveExactScreenName", names, "main");
            throw new InvalidOperationException("FAIL: a case-only match must fail");
        }
        catch (InvalidOperationException error)
        {
            Check(error.Message.Contains("case-sensitive") && error.Message.Contains("'Main'"),
                "a case-only screen match fails and suggests the exact name");
        }
        try
        {
            InvokeManagerHelperResult("ResolveExactScreenName", names, "Missing");
            throw new InvalidOperationException("FAIL: an unknown screen must fail");
        }
        catch (InvalidOperationException error)
        {
            Check(error.Message.Contains("not found") && error.Message.Contains("Overview"),
                "an unknown screen fails and lists the available screens");
        }
        Throws<InvalidOperationException>(
            () => InvokeManagerHelperResult("ResolveExactScreenName", new List<string> { "Main", "Main" }, "Main"),
            "a screen name found twice is ambiguous");
        Throws<ArgumentException>(
            () => InvokeManagerHelperResult("ResolveExactScreenName", names, " "),
            "a blank screen name is rejected");

        InvokeManagerHelper("RequireExpectedStartScreen", "Main", null);
        InvokeManagerHelper("RequireExpectedStartScreen", "Main", "Main");
        InvokeManagerHelper("RequireExpectedStartScreen", "", "");
        Throws<InvalidOperationException>(
            () => InvokeManagerHelper("RequireExpectedStartScreen", "Main", "Frame"),
            "a differing expected start screen fails");
        Throws<InvalidOperationException>(
            () => InvokeManagerHelper("RequireExpectedStartScreen", "Main", "main"),
            "the expected start screen is compared case-sensitively");
        _checks++;

        var validation = new List<HmiRuntimeValidationEntry>
        {
            new HmiRuntimeValidationEntry { PropertyName = "StartScreen", Errors = new List<string> { "Screen does not exist." } },
            new HmiRuntimeValidationEntry { PropertyName = "ScreenResolution", Errors = new List<string> { "Unrelated." } },
            new HmiRuntimeValidationEntry { PropertyName = "StartScreen", Warnings = new List<string> { "Only a warning." } }
        };
        var blocking = (List<HmiRuntimeValidationEntry>)InvokeManagerHelperResult("GetStartScreenValidationErrors", validation);
        Check(blocking.Count == 1 && blocking[0].Errors[0] == "Screen does not exist.",
            "only start-screen validation errors block the update; other settings and warnings do not");
    }

    private static HmiTargetInfo Target(string device, string item, string software)
    {
        return new HmiTargetInfo { DeviceName = device, DeviceItemName = item, SoftwareName = software };
    }

    private static HmiTargetInfo SelectTarget(List<HmiTargetInfo> candidates, string selector)
    {
        return (HmiTargetInfo)InvokeManagerHelperResult("SelectHmiTarget", candidates, selector);
    }

    private static void TestItemPropertyConversion()
    {
        var item = new TestItemProperties { Visible = true, Top = 0 };
        InvokeManagerHelper(
            "SetPropertyValue",
            item,
            typeof(TestItemProperties).GetProperty("Visible"),
            JsonValue("false"));
        InvokeManagerHelper(
            "SetPropertyValue",
            item,
            typeof(TestItemProperties).GetProperty("Top"),
            JsonValue("618"));
        var expected = new Dictionary<string, JsonElement>
        {
            { "Visible", JsonValue("false") },
            { "Top", JsonValue("618") }
        };
        InvokeManagerHelper("VerifyObjectProperties", item, expected);
        Check(!item.Visible && item.Top == 618,
            "property conversion applies Boolean and integer updates and verifies readback");
    }

    private static void TestManagerInputGuards()
    {
        TiaManager manager = TiaManager.Instance;
        Throws<InvalidDataException>(
            () => manager.UpdateHmiScreenItemProperties("Main", "Button", JsonValue("{}")),
            "item property update rejects an empty property set before project access");
        Throws<InvalidDataException>(
            () => manager.UpdateHmiScreenItemProperties("Main", "Button", JsonValue("{\"Opacity\":0.5}")),
            "item property update rejects properties outside its allowlist before project access");
        Throws<ArgumentException>(
            () => manager.UpdateHmiEventScript("Main", " ", "script"),
            "event update rejects a blank event name before project access");
        Throws<ArgumentException>(
            () => manager.SetExpressionDynamization("Main", "Button", "Top", " ", "Int32"),
            "expression update rejects an empty formula before project access");
        Throws<InvalidDataException>(
            () => manager.SetExpressionDynamization("Main", "Button", "Top", "1", "NotAType"),
            "expression update rejects unsupported result types before project access");
        Throws<InvalidDataException>(
            () => manager.SetExpressionDynamizationEntries(
                "Main", "Button", "Top", JsonValue("[]")),
            "expression condition update rejects an empty row list before project access");
    }

    private static void TestExpressionMappingEntryParsing()
    {
        object parsed = InvokeManagerHelperResult(
            "BuildExpressionMappingEntries",
            JsonValue("[{\"condition\":\"bit 0\",\"value\":{\"type\":\"Int32\",\"value\":\"500\"}},{\"condition\":\"bit 1\",\"value\":{\"type\":\"Int32\",\"value\":\"400\"}}]"),
            typeof(int));
        using (JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(parsed)))
        {
            JsonElement entries = document.RootElement;
            Check(entries.GetArrayLength() == 2 &&
                entries[0].GetProperty("Condition").GetString() == "bit 0" &&
                entries[0].GetProperty("Value").GetProperty("Value").GetInt32() == 500 &&
                entries[1].GetProperty("Condition").GetString() == "bit 1" &&
                entries[1].GetProperty("Value").GetProperty("Value").GetInt32() == 400,
                "expression condition entries retain row order and parse typed values");
        }
        Throws<InvalidDataException>(
            () => InvokeManagerHelperResult(
                "BuildExpressionMappingEntries",
                JsonValue("[{\"condition\":\" \",\"value\":{\"type\":\"Int32\",\"value\":\"500\"}}]"),
                typeof(int)),
            "expression condition entries reject blank conditions");
        Throws<InvalidDataException>(
            () => InvokeManagerHelperResult(
                "BuildExpressionMappingEntries",
                JsonValue("[{\"condition\":\"bit 0\",\"value\":{\"type\":\"Boolean\",\"value\":\"true\"}}]"),
                typeof(int)),
            "expression condition entries reject values with the wrong target type");
    }

    private static void InvokeManagerHelper(string name, params object[] arguments)
    {
        try
        {
            typeof(TiaManager).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, arguments);
        }
        catch (TargetInvocationException error)
        {
            throw error.InnerException;
        }
    }

    private static object InvokeManagerHelperResult(string name, params object[] arguments)
    {
        try
        {
            return typeof(TiaManager).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, arguments);
        }
        catch (TargetInvocationException error)
        {
            throw error.InnerException;
        }
    }

    private static JsonElement JsonValue(string json)
    {
        using (JsonDocument document = JsonDocument.Parse(json))
            return document.RootElement.Clone();
    }

    private static void Throws<T>(Action action, string description) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            _checks++;
            return;
        }
        throw new InvalidOperationException("FAIL: " + description);
    }

    private static JsonElement? Arguments(string json)
    {
        using (JsonDocument document = JsonDocument.Parse(json))
            return document.RootElement.Clone();
    }

    private static string FirstText(CallToolResult result)
    {
        return result.Content[0].Text;
    }

    private static void Check(bool success, string description)
    {
        if (!success)
            throw new InvalidOperationException("FAIL: " + description);
        _checks++;
    }

    private sealed class FakeHmiToolOperations : IHmiToolOperations
    {
        public string LastOperation = "";
        public int CallCount;
        public string ScreenName = "";
        public string ItemName;
        public string TargetName;
        public JsonElement Properties;
        public string EventType = "";
        public string ScriptCode = "";
        public string PropertyName = "";
        public string Expression = "";
        public JsonElement ExpressionEntries;
        public string ResultType = "";
        public string OnConflict = "";
        public string ExpectedCurrentStartScreen;
        public bool NextStartScreenSuccess = true;

        public object DeleteHmiDynamization(
            string screenName,
            string itemName,
            string propertyName,
            string targetName = null)
        {
            Record("delete-dynamization", screenName, itemName, targetName);
            PropertyName = propertyName;
            return new { screenName, itemName, propertyName, dynamizationType = "TagDynamization", deleted = true };
        }

        public object GetHmiScreenItem(string screenName, string itemName, string targetName = null)
        {
            Record("get-item", screenName, itemName, targetName);
            return new
            {
                name = itemName,
                props = new { Visible = false, Top = 618 },
                events = new[] { new { type = "Tapped", script = "existing script" } }
            };
        }

        public object UpdateHmiScreenItemProperties(
            string screenName,
            string itemName,
            JsonElement properties,
            string targetName = null)
        {
            Record("update-item", screenName, itemName, targetName);
            Properties = properties.Clone();
            return new { name = itemName, props = properties };
        }

        public HmiEventScriptsDetails GetHmiEventScripts(
            string screenName,
            string itemName = null,
            string targetName = null)
        {
            Record("read-events", screenName, itemName, targetName);
            var result = new HmiEventScriptsDetails
            {
                ScreenName = screenName,
                TargetKind = itemName == null ? "screen" : "item",
                TargetName = itemName ?? screenName
            };
            result.SupportedEventTypes.Add("Loaded");
            result.SupportedEventTypes.Add("Unloaded");
            result.SupportedEventTypes.Add("Tapped");
            result.Events.Add(new HmiEventScriptEntry { EventType = "Tapped", Script = "existing script" });
            return result;
        }

        public HmiEventScriptUpdateResult UpdateHmiEventScript(
            string screenName,
            string eventType,
            string scriptCode,
            string itemName = null,
            string targetName = null)
        {
            Record("set-event", screenName, itemName, targetName);
            EventType = eventType;
            ScriptCode = scriptCode;
            return new HmiEventScriptUpdateResult
            {
                ScreenName = screenName,
                TargetKind = itemName == null ? "screen" : "item",
                TargetName = itemName ?? screenName,
                EventType = eventType,
                Script = scriptCode
            };
        }

        public object SetExpressionDynamization(
            string screenName,
            string itemName,
            string propertyName,
            string expression,
            string resultType,
            string onConflict = "fail",
            string targetName = null)
        {
            Record("set-expression", screenName, itemName, targetName);
            PropertyName = propertyName;
            Expression = expression;
            ResultType = resultType;
            OnConflict = onConflict;
            return new { propertyName, expression, resultType, onConflict };
        }

        public object SetExpressionDynamizationEntries(
            string screenName,
            string itemName,
            string propertyName,
            JsonElement entries,
            string onConflict = "fail",
            string targetName = null)
        {
            Record("set-expression-entries", screenName, itemName, targetName);
            PropertyName = propertyName;
            ExpressionEntries = entries.Clone();
            OnConflict = onConflict;
            return new { propertyName, entries = entries.Clone(), onConflict };
        }

        public CompilationReport CompileHmi(string targetName = null)
        {
            Record("compile", "", null, targetName);
            return new CompilationReport
            {
                State = "Success",
                Target = "HMI_RT_1",
                DeviceName = "HMI_1",
                CompiledObject = "Device 'HMI_1'"
            };
        }

        public HmiRuntimeSettingsInfo GetHmiRuntimeSettings(string targetName = null)
        {
            Record("get-runtime-settings", "", null, targetName);
            return new HmiRuntimeSettingsInfo
            {
                TargetSoftwareName = "HMI_RT_1",
                StartScreen = "Frame",
                StartScreenExists = true
            };
        }

        public HmiStartScreenResult SetHmiStartScreen(
            string screenName,
            string targetName,
            string expectedCurrentStartScreen,
            string onConflict)
        {
            Record("set-start-screen", screenName, null, targetName);
            ExpectedCurrentStartScreen = expectedCurrentStartScreen;
            OnConflict = onConflict;
            return new HmiStartScreenResult
            {
                Success = NextStartScreenSuccess,
                Status = NextStartScreenSuccess ? "changed" : "validation_failed",
                TargetSoftwareName = "HMI_RT_1",
                OldStartScreen = "Main",
                StartScreen = NextStartScreenSuccess ? screenName : "Main"
            };
        }

        private void Record(string operation, string screenName, string itemName, string targetName)
        {
            LastOperation = operation;
            ScreenName = screenName;
            ItemName = itemName;
            TargetName = targetName;
            CallCount++;
        }

    }

    private sealed class TestItemProperties
    {
        public bool Visible { get; set; }
        public int Top { get; set; }
    }
}
