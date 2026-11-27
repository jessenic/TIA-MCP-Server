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
        TestHmiTagTools();
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

    private static void TestHmiTagTools()
    {
        var tagFake = new FakeHmiTagOperations();
        var registry = new ToolRegistry(new FakeHmiToolOperations()).UseHmiTagOperations(tagFake);

        ToolDefinition deleteTool = registry.GetTools().Single(tool => tool.Name == "hmi_delete_tag");
        ToolDefinition renameTool = registry.GetTools().Single(tool => tool.Name == "hmi_rename_tag");
        Check(deleteTool.InputSchema.Required.SequenceEqual(new[] { "tag_name" }),
            "hmi_delete_tag requires only tag_name");
        Check(deleteTool.InputSchema.Properties["on_referenced"].Enum.SequenceEqual(new[] { "fail", "ignore" }),
            "hmi_delete_tag exposes on_referenced fail and ignore");
        Check(renameTool.InputSchema.Required.SequenceEqual(new[] { "tag_name", "new_name", "on_conflict" }),
            "hmi_rename_tag requires tag_name, new_name and on_conflict");
        Check(renameTool.InputSchema.Properties["on_conflict"].Enum.SequenceEqual(new[] { "fail" }),
            "hmi_rename_tag exposes only the fail conflict policy");
        Check(renameTool.InputSchema.Properties["script_references"].Enum.SequenceEqual(new[] { "report", "replace" }),
            "hmi_rename_tag exposes script_references report and replace");

        CallToolResult deleteResult = registry.Execute("hmi_delete_tag", Arguments("{\"tag_name\":\"Motor_Speed\"}"));
        Check(!deleteResult.IsError && tagFake.LastOperation == "delete" && tagFake.TagName == "Motor_Speed" &&
            tagFake.TableName == null && tagFake.TargetName == null && tagFake.OnReferenced == "fail" && !tagFake.DryRun,
            "hmi_delete_tag applies the defaults on_referenced=fail and dry_run=false");

        registry.Execute("hmi_delete_tag", Arguments(
            "{\"tag_name\":\"Motor_Speed\",\"table_name\":\"Drives\",\"target_name\":\"HMI_RT\",\"on_referenced\":\"ignore\",\"dry_run\":true}"));
        Check(tagFake.TableName == "Drives" && tagFake.TargetName == "HMI_RT" && tagFake.OnReferenced == "ignore" && tagFake.DryRun,
            "hmi_delete_tag forwards table, target, on_referenced and dry_run");

        tagFake.NextDeleteStatus = "referenced";
        CallToolResult referencedResult = registry.Execute("hmi_delete_tag", Arguments("{\"tag_name\":\"Motor_Speed\"}"));
        Check(referencedResult.IsError && FirstText(referencedResult).Contains("\"referenced\"") &&
            FirstText(referencedResult).Contains("tag_dynamization") && !FirstText(referencedResult).Contains("ReadBack"),
            "a referenced delete is an error result that lists the references");
        tagFake.NextDeleteStatus = "deleted";

        int calls = tagFake.CallCount;
        Check(ExecuteQuietly(registry, "hmi_delete_tag", "{\"tag_name\":\"Motor_Speed\",\"on_referenced\":\"force\"}").IsError &&
            ExecuteQuietly(registry, "hmi_delete_tag", "{\"table_name\":\"Drives\"}").IsError &&
            tagFake.CallCount == calls,
            "hmi_delete_tag rejects a bad on_referenced value and a missing tag_name before dispatch");

        CallToolResult renameResult = registry.Execute("hmi_rename_tag", Arguments(
            "{\"tag_name\":\"Motor_Speed\",\"new_name\":\"Motor_SpeedSetpoint\",\"on_conflict\":\"fail\"}"));
        Check(!renameResult.IsError && tagFake.LastOperation == "rename" && tagFake.NewName == "Motor_SpeedSetpoint" &&
            tagFake.OnConflict == "fail" && tagFake.ScriptReferences == "report" && !tagFake.DryRun,
            "hmi_rename_tag applies the defaults script_references=report and dry_run=false");

        registry.Execute("hmi_rename_tag", Arguments(
            "{\"tag_name\":\"Motor_Speed\",\"new_name\":\"Speed\",\"on_conflict\":\"fail\",\"table_name\":\"Drives\",\"target_name\":\"HMI_RT\",\"script_references\":\"replace\",\"dry_run\":true}"));
        Check(tagFake.TableName == "Drives" && tagFake.TargetName == "HMI_RT" && tagFake.ScriptReferences == "replace" && tagFake.DryRun,
            "hmi_rename_tag forwards table, target, script_references and dry_run");

        calls = tagFake.CallCount;
        Check(ExecuteQuietly(registry, "hmi_rename_tag", "{\"tag_name\":\"A\",\"new_name\":\"B\",\"on_conflict\":\"replace\"}").IsError &&
            ExecuteQuietly(registry, "hmi_rename_tag", "{\"tag_name\":\"A\",\"new_name\":\"B\",\"on_conflict\":\"fail\",\"script_references\":\"all\"}").IsError &&
            ExecuteQuietly(registry, "hmi_rename_tag", "{\"tag_name\":\"A\",\"on_conflict\":\"fail\"}").IsError &&
            tagFake.CallCount == calls,
            "hmi_rename_tag rejects bad on_conflict, bad script_references and a missing new_name before dispatch");

        tagFake.NextRenameSuccess = false;
        Check(registry.Execute("hmi_rename_tag", Arguments("{\"tag_name\":\"A\",\"new_name\":\"B\",\"on_conflict\":\"fail\"}")).IsError,
            "a failed rename is an error result");

        TestHmiTagReferenceMatching();
        TestHmiTagScriptLexer();
        TestHmiTagManagerGuards();
    }

    private static void TestHmiTagReferenceMatching()
    {
        Func<string, bool> noOtherTags = name => false;
        Check(HmiTagReferenceText.IsTagReference("Motor", "Motor", noOtherTags), "exact tag reference matches");
        Check(HmiTagReferenceText.IsTagReference("Motor.Speed", "Motor", noOtherTags), "member reference matches");
        Check(HmiTagReferenceText.IsTagReference("Motor[2]", "Motor", noOtherTags), "array element reference matches");
        Check(HmiTagReferenceText.IsTagReference("Motor{2}.Fault", "Motor", noOtherTags), "brace element reference matches");
        Check(!HmiTagReferenceText.IsTagReference("Motor2", "Motor", noOtherTags), "longer tag name does not match");
        Check(!HmiTagReferenceText.IsTagReference("MotorX.Speed", "Motor", noOtherTags), "other root does not match");
        Check(!HmiTagReferenceText.IsTagReference("motor", "Motor", noOtherTags), "matching is case-sensitive");
        Check(!HmiTagReferenceText.IsTagReference(null, "Motor", noOtherTags) &&
            !HmiTagReferenceText.IsTagReference("", "Motor", noOtherTags), "empty reference does not match");
        Func<string, bool> dottedTagExists = name => name == "Motor.X";
        Check(!HmiTagReferenceText.IsTagReference("Motor.X", "Motor", dottedTagExists),
            "a dotted reference that is another tag's name does not match");
        Check(!HmiTagReferenceText.IsTagReference("Motor.X.Y", "Motor", dottedTagExists),
            "a member of a dotted tag does not match the shorter tag");
        Check(HmiTagReferenceText.IsTagReference("Motor.Z", "Motor", dottedTagExists),
            "other members still match when a dotted tag exists");

        Check(HmiTagReferenceText.RewriteTagReference("Motor", "Motor", "Pump") == "Pump" &&
            HmiTagReferenceText.RewriteTagReference("Motor.Speed", "Motor", "Pump") == "Pump.Speed" &&
            HmiTagReferenceText.RewriteTagReference("Motor[2]", "Motor", "Pump") == "Pump[2]",
            "reference rewrite keeps member and element suffixes");

        foreach (string bad in new[] { "A.B", "A[1]", "A{1}", "A\"B", "A'B", "A`B", "A\\B", "A:B" })
        {
            string candidate = bad;
            Throws<ArgumentException>(() => HmiTagReferenceText.ValidateNewTagName(candidate), "new tag name rejects " + candidate);
        }
        HmiTagReferenceText.ValidateNewTagName("Motor_Speed 2");
        Check(true, "new tag name accepts underscores and spaces");
    }

    private static void TestHmiTagScriptLexer()
    {
        HmiScriptScanResult scan = HmiTagReferenceText.FindScriptOccurrences(
            "let v = Tags(\"Motor\").Read();\nTags('Motor.Speed').Write(1);\n// Motor comment\nlet s = \"Motor2 and Motor x\";\nlet Motor = 1;\nlet t = `${a} Motor`;",
            "Motor");
        Check(!scan.Uncertain, "plain script is lexed with certainty");
        Check(scan.Occurrences.Count(o => o.Kind == HmiTagReferenceText.ExactLiteral && o.Replaceable) == 1 &&
            scan.Occurrences.Single(o => o.Kind == HmiTagReferenceText.ExactLiteral).Line == 1,
            "exact double-quoted literal is found and replaceable");
        Check(scan.Occurrences.Count(o => o.Kind == HmiTagReferenceText.LiteralMember && !o.Replaceable) == 1,
            "member literal is reported but not replaceable");
        Check(scan.Occurrences.Count(o => o.Kind == HmiTagReferenceText.Comment) == 1, "comment occurrence is reported");
        Check(scan.Occurrences.Count(o => o.Kind == HmiTagReferenceText.LiteralOther) == 1,
            "word inside a longer literal is reported once, not as Motor2");
        Check(scan.Occurrences.Count(o => o.Kind == HmiTagReferenceText.Code) == 1, "identifier occurrence is reported");
        Check(scan.Occurrences.Count(o => o.Kind == HmiTagReferenceText.TemplateLiteral) == 1, "template occurrence is reported");
        Check(HmiTagReferenceText.ScriptMentionsTag("Tags('Motor')", "Motor") &&
            !HmiTagReferenceText.ScriptMentionsTag("Tags('Motor2')", "Motor"),
            "ScriptMentionsTag uses whole-name matching");

        int replaced;
        string rewritten = HmiTagReferenceText.ReplaceExactLiterals(
            "Tags(\"Motor\").Write(Tags('Motor').Read()); Tags(\"Motor.Speed\"); // \"Motor\"\nlet esc = \"a\\\"Motor\";",
            "Motor", "Pump", out replaced);
        Check(replaced == 2 &&
            rewritten == "Tags(\"Pump\").Write(Tags('Pump').Read()); Tags(\"Motor.Speed\"); // \"Motor\"\nlet esc = \"a\\\"Motor\";",
            "only exact string literals are replaced; comments, members and escaped text stay");

        HmiScriptScanResult unterminated = HmiTagReferenceText.FindScriptOccurrences("Tags(\"Motor\"); let x = \"open", "Motor");
        string unchanged = HmiTagReferenceText.ReplaceExactLiterals("Tags(\"Motor\"); let x = \"open", "Motor", "Pump", out replaced);
        Check(unterminated.Uncertain && replaced == 0 && unchanged == "Tags(\"Motor\"); let x = \"open",
            "an unterminated literal makes the scan uncertain and blocks replacement");

        HmiScriptScanResult regex = HmiTagReferenceText.FindScriptOccurrences("let r = /\"x/; Tags(\"Motor\");", "Motor");
        HmiTagReferenceText.ReplaceExactLiterals("let r = /\"x/; Tags(\"Motor\");", "Motor", "Pump", out replaced);
        Check(regex.Uncertain && replaced == 0, "a regular-expression literal blocks replacement");

        HmiScriptScanResult division = HmiTagReferenceText.FindScriptOccurrences("let r = a / 2; Tags(\"Motor\");", "Motor");
        Check(!division.Uncertain && division.Occurrences.Count == 1, "division is not mistaken for a regular expression");

        Check(HmiTagReferenceText.IsBlockingKind(HmiTagReferenceText.ExactLiteral) &&
            HmiTagReferenceText.IsBlockingKind(HmiTagReferenceText.LiteralMember) &&
            !HmiTagReferenceText.IsBlockingKind(HmiTagReferenceText.Comment) &&
            !HmiTagReferenceText.IsBlockingKind(HmiTagReferenceText.Code),
            "only literal script hits block a delete");
    }

    private static void TestHmiTagManagerGuards()
    {
        TiaManager manager = TiaManager.Instance;
        Throws<ArgumentException>(() => manager.DeleteHmiTag(" ", null, null, "fail", false),
            "delete rejects a blank tag name before project access");
        Throws<ArgumentException>(() => manager.DeleteHmiTag("Motor", null, null, "force", false),
            "delete rejects an unknown on_referenced value before project access");
        Throws<ArgumentException>(() => manager.RenameHmiTag("Motor", "Motor.X", null, null, "fail", "report", false),
            "rename rejects reserved characters before project access");
        Throws<ArgumentException>(() => manager.RenameHmiTag("Motor", "Pump", null, null, "replace", "report", false),
            "rename rejects a non-fail conflict policy before project access");
        Throws<ArgumentException>(() => manager.RenameHmiTag("Motor", "Pump", null, null, "fail", "all", false),
            "rename rejects an unknown script_references value before project access");
        Throws<ArgumentException>(() => manager.RenameHmiTag("Motor", "Motor", null, null, "fail", "report", false),
            "rename rejects an unchanged name before project access");
    }

    private static CallToolResult ExecuteQuietly(ToolRegistry registry, string tool, string json)
    {
        TextWriter originalError = Console.Error;
        using (var discard = new StringWriter())
        {
            Console.SetError(discard);
            try
            {
                return registry.Execute(tool, Arguments(json));
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
    }

    private sealed class FakeHmiTagOperations : IHmiTagOperations
    {
        public string LastOperation = "";
        public int CallCount;
        public string TagName;
        public string NewName;
        public string TableName;
        public string TargetName;
        public string OnReferenced;
        public string OnConflict;
        public string ScriptReferences;
        public bool DryRun;
        public string NextDeleteStatus = "deleted";
        public bool NextRenameSuccess = true;

        public HmiTagDeleteResult DeleteHmiTag(string tagName, string tableName, string targetName, string onReferenced, bool dryRun)
        {
            LastOperation = "delete";
            CallCount++;
            TagName = tagName;
            TableName = tableName;
            TargetName = targetName;
            OnReferenced = onReferenced;
            DryRun = dryRun;
            var result = new HmiTagDeleteResult
            {
                Success = NextDeleteStatus == "deleted",
                Status = NextDeleteStatus,
                TagName = tagName
            };
            if (NextDeleteStatus == "referenced")
            {
                result.References.Add(new HmiTagReference
                {
                    Kind = "tag_dynamization",
                    Owner = "Screen 'Main' item 'io_Speed'",
                    Property = "ProcessValue",
                    Value = tagName
                });
                result.BlockingReferenceCount = 1;
            }
            return result;
        }

        public HmiTagRenameResult RenameHmiTag(string tagName, string newName, string tableName, string targetName,
            string onConflict, string scriptReferences, bool dryRun)
        {
            LastOperation = "rename";
            CallCount++;
            TagName = tagName;
            NewName = newName;
            TableName = tableName;
            TargetName = targetName;
            OnConflict = onConflict;
            ScriptReferences = scriptReferences;
            DryRun = dryRun;
            return new HmiTagRenameResult
            {
                Success = NextRenameSuccess,
                Status = NextRenameSuccess ? "renamed" : "conflict",
                OldName = tagName,
                Name = newName
            };
        }
    }

    private sealed class TestItemProperties
    {
        public bool Visible { get; set; }
        public int Top { get; set; }
    }
}
