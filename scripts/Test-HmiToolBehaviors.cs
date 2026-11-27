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

        CallToolResult fontUpdateResult = registry.Execute(
            "hmi_update_screen_item",
            Arguments("{\"screen_name\":\"Main\",\"item_name\":\"btn_Start\",\"item_properties\":{\"Font\":{\"Size\":14,\"Weight\":\"Bold\"},\"Font.Italic\":true,\"Text\":\"Start\",\"Enabled\":false}}"));
        Check(!fontUpdateResult.IsError && fake.LastOperation == "update-item" &&
            fake.Properties.GetProperty("Font").GetProperty("Size").GetInt32() == 14 &&
            fake.Properties.GetProperty("Font.Italic").GetBoolean() &&
            fake.Properties.GetProperty("Text").GetString() == "Start" &&
            !fake.Properties.GetProperty("Enabled").GetBoolean(),
            "item update forwards nested Font, dotted Font paths, Text, and Enabled unchanged");

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
        TestItemPropertyPathNormalization();
        TestItemPropertyPathValidation();
        TestItemPropertyPathApply();
        TestHmiPlainTextHelpers();
        TestHmiTagTools();
        TestHmiCompileTool(registry, fake);
        TestHmiTargetSelection();
        TestCompileProbeChain();
        TestHmiRuntimeTools(registry, fake);
        TestStartScreenHelpers();
    }

    private static void TestItemPropertyPathNormalization()
    {
        var updates = (List<KeyValuePair<string, JsonElement>>)InvokeManagerHelperResult(
            "NormalizeItemPropertyUpdates",
            JsonValue("{\"font\":{\"size\":14,\"bold\":true},\"FONT.italic\":true,\"visible\":false,\"text\":\"Start\"}"));
        Check(string.Join(",", updates.Select(update => update.Key).ToArray()) ==
            "Font.Size,Font.Weight,Font.Italic,Visible,Text",
            "item property normalization flattens nested and dotted Font paths to canonical names in input order");
        Check(updates[1].Value.GetString() == "Bold",
            "Font.Bold true is a convenience for Font.Weight Bold");
        var notBold = (List<KeyValuePair<string, JsonElement>>)InvokeManagerHelperResult(
            "NormalizeItemPropertyUpdates", JsonValue("{\"Font.Bold\":false}"));
        Check(notBold.Single().Key == "Font.Weight" && notBold.Single().Value.GetString() == "Normal",
            "Font.Bold false maps to Font.Weight Normal");
    }

    private static void TestItemPropertyPathValidation()
    {
        Type host = typeof(TestItemHost);
        InvokeManagerHelper("ValidateItemPropertyPath", host, "Font.Size", JsonValue("14.5"));
        InvokeManagerHelper("ValidateItemPropertyPath", host, "Font.Weight", JsonValue("\"semibold\""));
        InvokeManagerHelper("ValidateItemPropertyPath", host, "Text", JsonValue("{\"html\":\"<body><p>x</p></body>\"}"));
        InvokeManagerHelper("ValidateItemPropertyPath", host, "BackColor", JsonValue("\"#ff0000\""));
        _checks++;
        Throws<InvalidDataException>(
            () => InvokeManagerHelper("ValidateItemPropertyPath", host, "Font.Weight", JsonValue("\"Heavy\"")),
            "item property validation rejects an unknown enum name before mutation");
        Throws<InvalidDataException>(
            () => InvokeManagerHelper("ValidateItemPropertyPath", host, "Font.Size", JsonValue("\"big\"")),
            "item property validation rejects a wrong JSON type for a Font leaf");
        Throws<InvalidDataException>(
            () => InvokeManagerHelper("ValidateItemPropertyPath", host, "Top", JsonValue("1e12")),
            "item property validation rejects an out-of-range integer before mutation");
        Throws<InvalidDataException>(
            () => InvokeManagerHelper("ValidateItemPropertyPath", host, "BackColor", JsonValue("\"red\"")),
            "item property validation rejects a color name (only #RRGGBB/#AARRGGBB)");
        Throws<InvalidDataException>(
            () => InvokeManagerHelper("ValidateItemPropertyPath", host, "ReadOnlyCaption", JsonValue("\"x\"")),
            "item property validation rejects a read-only leaf before mutation");
        Throws<InvalidDataException>(
            () => InvokeManagerHelper("ValidateItemPropertyPath", host, "Text", JsonValue("5")),
            "item property validation rejects a numeric Text value");
        Throws<InvalidDataException>(
            () => InvokeManagerHelper("ValidateItemPropertyPath", typeof(TestItemProperties), "Font.Size", JsonValue("14")),
            "item property validation rejects Font on an item type without a Font part");
    }

    private static void TestItemPropertyPathApply()
    {
        var languages = new List<string> { "en-US", "fi-FI" };
        var item = new TestItemHost();
        item.Text.Items.Add(new TestTextItem { Language = "en-US", Text = "<body><p style=\"margin: 0; text-align: center;\">Old</p></body>" });
        item.Text.Items.Add(new TestTextItem { Language = "fi-FI", Text = "<body><p>Vanha</p></body>" });
        var updates = (List<KeyValuePair<string, JsonElement>>)InvokeManagerHelperResult(
            "NormalizeItemPropertyUpdates",
            JsonValue("{\"Font\":{\"Size\":14.5,\"Bold\":true},\"Text\":\"K\\u00e4ynnist\\u00e4 &#196; & <x>\",\"BackColor\":\"#ff0000\",\"Visible\":false,\"Font.Italic\":true}"));
        InvokeManagerHelper("ApplyAndVerifyItemPropertyUpdates", item, updates, languages);
        Check(Math.Abs(item.Font.Size - 14.5f) < 0.0001f && item.Font.Weight == TestFontWeight.Bold && item.Font.Italic,
            "partial Font update sets only the given leaves and passes read-back");
        Check(item.BackColor.ToArgb() == System.Drawing.Color.FromArgb(255, 255, 0, 0).ToArgb() && !item.Visible,
            "lower-case hex color passes CLR read-back verification");
        Check(item.Text.Items[0].Text ==
            "<body><p style=\"margin: 0; text-align: center;\">Käynnistä Ä &amp; &lt;x&gt;</p></body>",
            "plain Text keeps the existing paragraph style, decodes numeric references, and escapes markup characters");
        Check(item.Text.Items[1].Text == "<body><p>Käynnistä Ä &amp; &lt;x&gt;</p></body>",
            "plain Text keeps an unstyled paragraph unstyled");

        var lowerEnum = (List<KeyValuePair<string, JsonElement>>)InvokeManagerHelperResult(
            "NormalizeItemPropertyUpdates", JsonValue("{\"Font.Weight\":\"semibold\"}"));
        InvokeManagerHelper("ApplyAndVerifyItemPropertyUpdates", item, lowerEnum, languages);
        Check(item.Font.Weight == TestFontWeight.SemiBold && item.Font.Size > 14f,
            "case-insensitive enum name passes read-back and leaves other Font leaves unchanged");

        var languageMap = (List<KeyValuePair<string, JsonElement>>)InvokeManagerHelperResult(
            "NormalizeItemPropertyUpdates",
            JsonValue("{\"Text\":{\"en-US\":\"Start\",\"fi-FI\":{\"html\":\"<body><p style='x'>Aloita</p></body>\"}}}"));
        InvokeManagerHelper("ApplyAndVerifyItemPropertyUpdates", item, languageMap, languages);
        Check(item.Text.Items[0].Text == "<body><p style=\"margin: 0; text-align: center;\">Start</p></body>" &&
            item.Text.Items[1].Text == "<body><p style='x'>Aloita</p></body>",
            "language-map Text sets plain and raw-html values per language");

        var missingLanguage = (List<KeyValuePair<string, JsonElement>>)InvokeManagerHelperResult(
            "NormalizeItemPropertyUpdates", JsonValue("{\"Text\":{\"en-US\":\"Only English\"}}"));
        Throws<InvalidDataException>(
            () => InvokeManagerHelper("ApplyAndVerifyItemPropertyUpdates", item, missingLanguage, languages),
            "language-map Text must cover every active language");

        var styledMarkup = (List<KeyValuePair<string, JsonElement>>)InvokeManagerHelperResult(
            "NormalizeItemPropertyUpdates",
            JsonValue("{\"Text\":\"<body><p style=\\\"margin: 0; text-align: center;\\\">Label</p></body>\"}"));
        InvokeManagerHelper("ApplyAndVerifyItemPropertyUpdates", item, styledMarkup, languages);
        Check(item.Text.Items[0].Text == "<body><p style=\"margin: 0; text-align: center;\">Label</p></body>" &&
            item.Text.Items[1].Text == "<body><p style=\"margin: 0; text-align: center;\">Label</p></body>",
            "a styled <body> markup string is written as raw markup, as hmi_add_label/hmi_add_button do");
        Throws<InvalidDataException>(
            () => InvokeManagerHelperResult("NormalizeItemPropertyUpdates", JsonValue("{\"Text\":\"<body><p>broken\"}")),
            "a malformed <body> markup string is rejected before project access");
        Throws<InvalidDataException>(
            () => InvokeManagerHelperResult("NormalizeItemPropertyUpdates",
                JsonValue("{\"Text\":{\"en-US\":\"ok\",\"fi-FI\":\"<body><p>broken\"}}")),
            "a malformed <body> markup string in a language map is rejected before project access");

        item.Text.Items[0].Text = "<body><p style='a'>Line 1</p><p>Line 2</p></body>";
        item.Text.Items[1].Text = "<body><p>Rivi</p></body>";
        var plainText = (List<KeyValuePair<string, JsonElement>>)InvokeManagerHelperResult(
            "NormalizeItemPropertyUpdates", JsonValue("{\"Text\":\"One line\"}"));
        var rawText = (List<KeyValuePair<string, JsonElement>>)InvokeManagerHelperResult(
            "NormalizeItemPropertyUpdates", JsonValue("{\"Text\":{\"html\":\"<body><p>A</p><p>B</p></body>\"}}"));
        var dropWarnings = (List<string>)InvokeManagerHelperResult("FindTextReplacementWarnings", item, plainText);
        var rawWarnings = (List<string>)InvokeManagerHelperResult("FindTextReplacementWarnings", item, rawText);
        Check(dropWarnings.Count == 1 && dropWarnings[0].Contains("single paragraph") && rawWarnings.Count == 0,
            "a plain Text that replaces multi-paragraph markup is reported under warnings; raw html is not");
        item.Text.Items[0].Text = "<body><p>Line <span>1</span></p></body>";
        Check(((List<string>)InvokeManagerHelperResult("FindTextReplacementWarnings", item, plainText)).Count == 1,
            "a plain Text that replaces inline formatting is reported under warnings");
        item.Text.Items[0].Text = "<body><p style='a'>Only</p></body>";
        Check(((List<string>)InvokeManagerHelperResult("FindTextReplacementWarnings", item, plainText)).Count == 0,
            "a single styled paragraph gives no text warning");

        var stubborn = new TestStubbornItem();
        var topUpdate = (List<KeyValuePair<string, JsonElement>>)InvokeManagerHelperResult(
            "NormalizeItemPropertyUpdates", JsonValue("{\"Top\":5}"));
        Throws<InvalidOperationException>(
            () => InvokeManagerHelper("ApplyAndVerifyItemPropertyUpdates", stubborn, topUpdate, languages),
            "a value that does not read back fails verification (so the transaction is not committed)");
    }

    private static void TestHmiPlainTextHelpers()
    {
        Check((string)InvokeManagerHelperResult("WrapPlainTextAsHtml", "Lämpötila °C") ==
            "<body><p>Lämpötila °C</p></body>",
            "plain Unicode text is wrapped unchanged");
        Check((string)InvokeManagerHelperResult("WrapPlainTextAsHtml", "&#196;ly &#xE4; &#x1F600;") ==
            "<body><p>Äly ä 😀</p></body>",
            "decimal and hexadecimal numeric character references are decoded before escaping");
        Check((string)InvokeManagerHelperResult("WrapPlainTextAsHtml", "A & B <x> &amp; &#0; &#xD800; &#1114112;") ==
            "<body><p>A &amp; B &lt;x&gt; &amp;amp; &amp;#0; &amp;#xD800; &amp;#1114112;</p></body>",
            "named entities, a bare ampersand, and invalid code points stay literal and are escaped");
        Check((string)InvokeManagerHelperResult("FormatHtmlText", "&#176;C") == "<body><p>°C</p></body>" &&
            (string)InvokeManagerHelperResult("FormatHtmlText", "") == "<body><p></p></body>" &&
            (string)InvokeManagerHelperResult("FormatHtmlText", "<body><p>x</p></body>") == "<body><p>x</p></body>",
            "add-tool wrapper decodes numeric references and keeps its markup passthrough");
        Check((string)InvokeManagerHelperResult("ToHmiMarkup", "&#196; & <x>") == "<body><p>&amp;#196; &amp; &lt;x&gt;</p></body>" &&
            (string)InvokeManagerHelperResult("ToHmiMarkup", "<body><p>x</p></body>") == "<body><p>x</p></body>",
            "import wrapper only escapes (a literal &#196; survives export/import) and keeps its plain-markup passthrough");
        Check((string)InvokeManagerHelperResult("DecodeNumericCharacterReferences", "{F1} &#176;C") == "{F1} °C",
            "output_format decoding turns numeric references into characters");
        Check((string)InvokeManagerHelperResult("ReplaceParagraphText", "not <markup", "x", true) == "<body><p>x</p></body>" &&
            (string)InvokeManagerHelperResult("ReplaceParagraphText", null, "a<b", true) == "<body><p>a&lt;b</p></body>",
            "paragraph text replacement falls back to plain wrapping for missing or unparseable markup");
        Check((bool)InvokeManagerHelperResult("AreHmiMarkupEqual",
                "<body><p a=\"1\" b='2'>x</p></body>", "<body><p b=\"2\" a=\"1\">x</p></body>") &&
            !(bool)InvokeManagerHelperResult("AreHmiMarkupEqual", "<body><p>x</p></body>", "<body><p>y</p></body>"),
            "markup comparison ignores attribute order and quoting but not text");
        Check(((JsonElement?)InvokeManagerHelperResult("NormalizeExpectedNullableText", (JsonElement?)JsonValue("\"Lämpötila &#176;C\""))).Value.GetString() == "Lämpötila &#176;C" &&
            ((JsonElement?)InvokeManagerHelperResult("NormalizeExpectedNullableText", (JsonElement?)JsonValue("{\"html\":\"<body><p>X</p></body>\"}"))).Value.GetString() == "X" &&
            ((JsonElement?)InvokeManagerHelperResult("NormalizeExpectedNullableText", (JsonElement?)JsonValue("null"))).Value.ValueKind == JsonValueKind.Null,
            "alarm eventText read-back normalizes the expected text (literal numeric reference kept, {html} compacted, null kept)");
        Check(InvokeManagerHelperResult("NormalizeExpectedNullableText", new object[] { null }) == null,
            "alarm eventText read-back keeps a missing expected text missing");
        Check(((JsonElement)InvokeManagerHelperResult("NormalizeExpectedText", JsonValue("\"&#196;\""))).GetString() == "&#196;" &&
            ((JsonElement)InvokeManagerHelperResult("NormalizeExpectedText", JsonValue("{\"html\":\"<body><p>X</p></body>\"}"))).GetString() == "X" &&
            ((JsonElement)InvokeManagerHelperResult("NormalizeExpectedText", JsonValue("{\"en-US\":\"A\",\"fi-FI\":\"A\"}"))).GetString() == "A",
            "import text read-back normalizes expected strings, raw html, and equal language maps");
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
            "hmi_compile passes the backend's compiled target and object through to the JSON result");

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

    private static void TestCompileProbeChain()
    {
        var calls = new List<string>();
        var compilable = new object();
        var probes = new List<KeyValuePair<string, Func<object>>>
        {
            new KeyValuePair<string, Func<object>>("SoftwareContainer", () => { calls.Add("SoftwareContainer"); return null; }),
            new KeyValuePair<string, Func<object>>("Holder", () => { calls.Add("Holder"); throw new System.Runtime.InteropServices.COMException("not supported"); }),
            new KeyValuePair<string, Func<object>>("Parent", () => { calls.Add("Parent"); return compilable; }),
            new KeyValuePair<string, Func<object>>("Device", () => { calls.Add("Device"); return new object(); })
        };
        var tried = new List<string>();
        object[] arguments = { probes, tried, null };
        var label = (string)typeof(TiaManager).GetMethod("ProbeServiceChain", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, arguments);
        Check(label == "Parent" && ReferenceEquals(arguments[2], compilable),
            "compile probe chain returns the first object that offers the service");
        Check(string.Join(",", calls) == "SoftwareContainer,Holder,Parent",
            "compile probe chain asks in order and stops at the first hit");
        Check(tried.Count == 3 && tried[0] == "SoftwareContainer" && tried[1].StartsWith("Holder (COMException") && tried[2] == "Parent",
            "compile probe chain records a throwing getter with its error and continues");

        var none = new List<KeyValuePair<string, Func<object>>>
        {
            new KeyValuePair<string, Func<object>>("SoftwareContainer", () => null),
            new KeyValuePair<string, Func<object>>("Device", () => null)
        };
        var triedNone = new List<string>();
        object[] noneArguments = { none, triedNone, null };
        object noneLabel = typeof(TiaManager).GetMethod("ProbeServiceChain", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, noneArguments);
        Check(noneLabel == null && noneArguments[2] == null && triedNone.Count == 2,
            "compile probe chain returns nothing and lists every object tried when none offers the service");

        bool propagated = false;
        try
        {
            var failing = new List<KeyValuePair<string, Func<object>>>
            {
                new KeyValuePair<string, Func<object>>("Bug", () => { throw new NullReferenceException("bug"); })
            };
            typeof(TiaManager).GetMethod("ProbeServiceChain", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { failing, new List<string>(), null });
        }
        catch (TargetInvocationException error)
        {
            propagated = error.InnerException is NullReferenceException;
        }
        Check(propagated, "compile probe chain does not swallow non-Openness exceptions");
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
        foreach (string invalid in new[]
        {
            "{\"Font\":{}}",
            "{\"Font\":5}",
            "{\"Font\":{\"Opacity\":1}}",
            "{\"Font\":{\"Size\":12},\"Font.Size\":14}",
            "{\"font.size\":1,\"Font.Size\":2}",
            "{\"Font\":{\"Bold\":true,\"Weight\":\"Bold\"}}",
            "{\"Font.Bold\":\"yes\"}",
            "{\"Font.Size.Value\":1}",
            "{\"Screen.Name\":\"x\"}",
            "{\"Name\":\"x\"}",
            "{\"Text\":5}",
            "{\"Text\":{\"html\":\"<body/>\",\"en-US\":\"x\"}}",
            "{\"visible\":true,\"Visible\":false}"
        })
        {
            string input = invalid;
            Throws<InvalidDataException>(
                () => manager.UpdateHmiScreenItemProperties("Main", "Button", JsonValue(input)),
                "item property update rejects " + input + " before project access");
        }
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
        Check(deleteTool.Description.Contains("scan_incomplete") && deleteTool.Description.Contains("ScanFailures"),
            "hmi_delete_tag documents that an incomplete reference scan refuses the delete");
        Check(typeof(HmiTagDeleteResult).GetProperty("ScanFailures") != null && typeof(HmiTagRenameResult).GetProperty("ScanFailures") != null,
            "delete and rename results expose the scan categories that could not be read");
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
        string referencedJson = FirstText(referencedResult);
        Check(referencedResult.IsError && referencedJson.Contains("\"referenced\"") &&
            referencedJson.Contains("tag_dynamization") && referencedJson.Contains("cross_reference") &&
            referencedJson.Contains("CrossReferenceCount"),
            "a referenced delete is an error result that lists the references and cross-references separately");
        Check(!referencedJson.Contains("ReadBack") && !referencedJson.Contains("LeakedReadBackValue") &&
            !referencedJson.Contains("SiteKey") && !referencedJson.Contains("LeakedSiteKeyValue"),
            "the internal ReadBack delegate and SiteKey are not serialized");
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

        Check(HmiTagReferenceText.IsTagAddressReference("Motor:3", "Motor", noOtherTags) &&
            HmiTagReferenceText.IsTagAddressReference("Motor.x3", "Motor", noOtherTags) &&
            HmiTagReferenceText.IsTagAddressReference("Motor", "Motor", noOtherTags),
            "alarm trigger addresses match the tag, a member, or 'Tag:bit'");
        Check(!HmiTagReferenceText.IsTagAddressReference("Motor2:3", "Motor", noOtherTags) &&
            !HmiTagReferenceText.IsTagAddressReference("motor:3", "Motor", noOtherTags) &&
            !HmiTagReferenceText.IsTagReference("Motor:3", "Motor", noOtherTags),
            "'Tag:bit' is accepted only for trigger addresses and only for the exact tag name");
        Check(HmiTagReferenceText.RewriteTagReference("Motor:3", "Motor", "Pump") == "Pump:3",
            "a 'Tag:bit' address keeps its bit suffix when the expected value is rewritten");
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

        // The replace guard needs SyntaxCheck: public (ScriptDynamization) or on an interface (IHmiScript).
        Check(TiaManager.FindScriptSyntaxCheck(new PublicSyntaxCheckScript()) != null,
            "SyntaxCheck is found on a script object with a public method");
        Check(TiaManager.FindScriptSyntaxCheck(new ExplicitSyntaxCheckScript()) != null,
            "SyntaxCheck is found through an explicitly implemented interface");
        Check(TiaManager.FindScriptSyntaxCheck(new object()) == null && TiaManager.FindScriptSyntaxCheck(null) == null,
            "a script object without SyntaxCheck (or none at all) is reported as not checkable");

        // Event-handler scripts (IHmiScript) have a global definition area; it is its own scanned and replaceable site.
        foreach (object script in new object[] { new PublicEventScript(), new ExplicitEventScript() })
        {
            List<HmiEventScriptCodeSite> sites = TiaManager.GetEventScriptCodeSites(script, "event", null, "Tapped");
            HmiEventScriptCodeSite global = sites.SingleOrDefault(site => site.Location == "event_global_definitions:Tapped");
            Check(sites.Count == 2 && sites.Any(site => site.Location == "event:Tapped") && global != null,
                script.GetType().Name + ": event script exposes ScriptCode and GlobalDefinitionAreaScriptCode sites");
            string globalCode = global.CodeProperty.GetValue(script, null) as string;
            Check(HmiTagReferenceText.FindScriptOccurrences(globalCode, "Motor").Occurrences
                    .Any(occurrence => occurrence.Replaceable && HmiTagReferenceText.IsBlockingKind(occurrence.Kind)),
                script.GetType().Name + ": a tag literal only in the global definition area is a blocking, replaceable hit");
            Check(global.CodeProperty.CanWrite && TiaManager.FindScriptSyntaxCheck(script) != null,
                script.GetType().Name + ": the global definition area is writable and guarded by SyntaxCheck");
        }
        Check(TiaManager.GetEventScriptCodeSites(null, "event", null, "Tapped").Count == 0,
            "a handler without a script has no code sites");
    }

    public interface IFakeEventScript
    {
        string ScriptCode { get; set; }
        string GlobalDefinitionAreaScriptCode { get; set; }
        object SyntaxCheck();
    }

    public sealed class PublicEventScript
    {
        private string _code = "HMIRuntime.Trace('x');";
        private string _global = "const TAG = \"Motor\";";
        public string ScriptCode { get { return _code; } set { _code = value; } }
        public string GlobalDefinitionAreaScriptCode { get { return _global; } set { _global = value; } }
        public object SyntaxCheck() { return null; }
    }

    public sealed class ExplicitEventScript : IFakeEventScript
    {
        private string _code = "HMIRuntime.Trace('x');";
        private string _global = "const TAG = \"Motor\";";
        string IFakeEventScript.ScriptCode { get { return _code; } set { _code = value; } }
        string IFakeEventScript.GlobalDefinitionAreaScriptCode { get { return _global; } set { _global = value; } }
        object IFakeEventScript.SyntaxCheck() { return null; }
    }

    public interface IFakeHmiScript
    {
        object SyntaxCheck();
    }

    public sealed class PublicSyntaxCheckScript
    {
        public object SyntaxCheck() { return null; }
    }

    public sealed class ExplicitSyntaxCheckScript : IFakeHmiScript
    {
        object IFakeHmiScript.SyntaxCheck() { return null; }
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
                var reference = new HmiTagReference
                {
                    Kind = "tag_dynamization",
                    Owner = "Screen 'Main' item 'io_Speed'",
                    Property = "ProcessValue",
                    Value = tagName
                };
                // Set the internal members the way TiaManager does, so the test sees whether they leak into JSON.
                typeof(HmiTagReference).GetProperty("ReadBack", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(reference, (Func<string>)(() => "LeakedReadBackValue"));
                result.References.Add(reference);
                var occurrence = new HmiScriptTagOccurrence
                {
                    Owner = "Screen 'Main'",
                    Location = "event:Loaded",
                    Kind = HmiTagReferenceText.ExactLiteral,
                    Snippet = "Tags('" + tagName + "')"
                };
                typeof(HmiScriptTagOccurrence).GetProperty("SiteKey", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(occurrence, "LeakedSiteKeyValue");
                result.ScriptOccurrences.Add(occurrence);
                result.CrossReferences.Add(new HmiTagReference { Kind = "cross_reference", Owner = "HMI_RT/Main", Value = tagName });
                result.BlockingReferenceCount = 2;
                result.CrossReferenceCount = 1;
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

    private enum TestFontWeight
    {
        Light,
        Normal,
        SemiBold,
        Bold
    }

    // The name must end in "Part" so the manager treats it like an Openness part (HmiFontPart).
    private sealed class TestFontPart
    {
        public float Size { get; set; }
        public TestFontWeight Weight { get; set; }
        public bool Italic { get; set; }
    }

    private sealed class TestTextItem
    {
        public string Language { get; set; }
        public string Text { get; set; }
    }

    // The name must be exactly "MultilingualText" to match the manager's Openness text detection.
    private sealed class MultilingualText
    {
        private readonly List<TestTextItem> _items = new List<TestTextItem>();
        public List<TestTextItem> Items { get { return _items; } }
    }

    private sealed class TestItemHost
    {
        private readonly TestFontPart _font = new TestFontPart { Size = 11f, Weight = TestFontWeight.Normal };
        private readonly MultilingualText _text = new MultilingualText();
        public TestFontPart Font { get { return _font; } }
        public MultilingualText Text { get { return _text; } }
        public bool Visible { get; set; }
        public int Top { get; set; }
        public System.Drawing.Color BackColor { get; set; }
        public string ReadOnlyCaption { get { return "fixed"; } }
    }

    private sealed class TestStubbornItem
    {
        public int Top { get { return 0; } set { } }
    }
}
