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
