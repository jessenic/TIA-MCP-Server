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
