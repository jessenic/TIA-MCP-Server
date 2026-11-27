using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using TiaOpennessMcp.Protocol;
using TiaOpennessMcp.Tia;
using TiaOpennessMcp.Tools;

// Offline checks for the PLC block / data-type tools (issues #11, #12, #13). Never attaches to TIA Portal:
// every registry call below either fails validation first or reaches the manager's "not attached" guard.
internal static class PlcBlockToolTests
{
    private static int _checks;
    private static string _binaryDirectory;
    private static string _apiDirectory;
    private static string _temporary;

    private static int Main(string[] args)
    {
        _binaryDirectory = args[0];
        _apiDirectory = args[1];
        _temporary = args[2];
        AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
        try
        {
            Run();
            Console.WriteLine("PASS: " + _checks + " offline PLC block tool checks.");
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
        TestRegistrations();
        TestNameSelection();
        TestRenameCollision();
        TestLookupNames();
        TestSclBackupDecision();
        TestImportSourceResolution();
        TestRegistryGuards();
    }

    private static void TestRegistrations()
    {
        var tools = new ToolRegistry().GetTools();
        var expected = new Dictionary<string, string[]>
        {
            { "tia_delete_plc_type", new[] { "type_name" } },
            { "tia_rename_plc_type", new[] { "type_name", "new_name" } },
            { "tia_rename_block", new[] { "block_name", "new_name" } }
        };
        foreach (var pair in expected)
        {
            ToolDefinition tool = tools.SingleOrDefault(candidate => candidate.Name == pair.Key);
            Check(tool != null, pair.Key + " is registered");
            Check(tool.InputSchema.Required != null && tool.InputSchema.Required.SequenceEqual(pair.Value),
                pair.Key + " requires exactly " + string.Join(",", pair.Value));
            Check(tool.InputSchema.Properties.ContainsKey("plc_name") && tool.InputSchema.Properties.ContainsKey("group_path"),
                pair.Key + " exposes plc_name and group_path");
        }
        foreach (string name in new[] { "tia_rename_plc_type", "tia_rename_block" })
        {
            ToolProperty onConflict = tools.Single(tool => tool.Name == name).InputSchema.Properties["on_conflict"];
            Check(onConflict.Enum.SequenceEqual(new[] { "fail" }) && (string)onConflict.Default == "fail",
                name + ".on_conflict allows and defaults to 'fail' only");
        }

        foreach (string name in new[] { "tia_import_block", "tia_import_scl_block", "tia_import_plc_type" })
        {
            ToolDefinition tool = tools.Single(candidate => candidate.Name == name);
            Check(tool.InputSchema.Properties.ContainsKey("file_path") &&
                (tool.InputSchema.Required == null || tool.InputSchema.Required.Count == 0),
                name + " exposes file_path and requires no single source");
        }
        ToolProperty backup = tools.Single(tool => tool.Name == "tia_import_scl_block").InputSchema.Properties["backup"];
        Check(backup.Type == "boolean" && Equals(backup.Default, true), "tia_import_scl_block.backup defaults to true");
        Check(tools.Single(tool => tool.Name == "tia_delete_block").Description.Contains("tia_delete_plc_type"),
            "tia_delete_block points to tia_delete_plc_type");
    }

    private static void TestNameSelection()
    {
        NameSelection ok = PlcBlockToolLogic.SelectExactName(new[] { "typeA", "typeHmiManual" }, "typeHmiManual");
        Check(ok.Status == "ok" && ok.Index == 1 && ok.Hint == null, "exact name selects its index");

        NameSelection caseOnly = PlcBlockToolLogic.SelectExactName(new[] { "TypeHmiManual" }, "typeHmiManual");
        Check(caseOnly.Status == "not_found" && caseOnly.Hint != null && caseOnly.Hint.Contains("'TypeHmiManual'"),
            "case-only match is not_found with the exact candidate as hint");

        NameSelection ambiguous = PlcBlockToolLogic.SelectExactName(new[] { "X", "X" }, "X");
        Check(ambiguous.Status == "ambiguous" && ambiguous.Index == -1, "two exact matches are ambiguous");

        NameSelection missing = PlcBlockToolLogic.SelectExactName(new string[0], "X");
        Check(missing.Status == "not_found" && missing.Hint == null, "no match is not_found without hint");

        var located = new[]
        {
            new KeyValuePair<string, string>("typeA", "Types/Sub"),
            new KeyValuePair<string, string>("typeA", ""),
            new KeyValuePair<string, string>("TYPEB", "Other")
        };
        string folderHint = PlcBlockToolLogic.OtherFolderHint(located, "typeA", "Missing");
        Check(folderHint != null && folderHint.Contains("folder 'Types/Sub'") && folderHint.Contains("the root folder"),
            "not found in the given folder names the other folders holding the exact name");
        string rootSearchHint = PlcBlockToolLogic.OtherFolderHint(located, "typeA", "");
        Check(rootSearchHint != null && rootSearchHint.Contains("folder 'Types/Sub'") && !rootSearchHint.Contains("root folder"),
            "group_path \"\" for a type in a subfolder points to that subfolder");
        Check(PlcBlockToolLogic.OtherFolderHint(located, "typeA", null) == null,
            "no folder hint when every folder was searched");
        Check(PlcBlockToolLogic.OtherFolderHint(located, "typeB", "Missing") == null,
            "folder hint needs an exact (case-sensitive) name");
        Check(PlcBlockToolLogic.FindNameIgnoringCase(new[] { "FB_Seq" }, "fb_seq") == "FB_Seq" &&
            PlcBlockToolLogic.FindNameIgnoringCase(new[] { "FB_Seq" }, "FC_X") == null,
            "kind hint uses the actual-case name of the other object");
    }

    private static void TestRenameCollision()
    {
        Check(PlcBlockToolLogic.HasRenameCollision(new[] { "typeOld", "TYPENEW" }, "typeOld", "typeNew"),
            "another object with the new name in another case collides");
        Check(!PlcBlockToolLogic.HasRenameCollision(new[] { "typeOld", "other" }, "typeOld", "TYPEOLD"),
            "case-only rename of the same object is no collision");
        Check(PlcBlockToolLogic.HasRenameCollision(new[] { "typeOld", "FB_Seq" }, "typeOld", "fb_seq"),
            "a block name colliding with the new type name is a collision");
        Check(!PlcBlockToolLogic.HasRenameCollision(new[] { "typeOld", "other" }, "typeOld", "typeNew"),
            "free new name is no collision");
    }

    private static void TestLookupNames()
    {
        Check(PlcBlockToolLogic.CheckLookupName("A/B", "type_name") == null,
            "lookup names with separators are still addressable");
        Check(PlcBlockToolLogic.CheckLookupName("", "type_name") != null &&
            PlcBlockToolLogic.CheckLookupName(" X", "type_name") != null &&
            PlcBlockToolLogic.CheckLookupName(null, "type_name") != null,
            "empty or padded lookup names are rejected");
    }

    private static void TestSclBackupDecision()
    {
        Check(PlcBlockToolLogic.DecideSclBackup(false, null, null, false, null, true, "FB").Action == SclBackupAction.None,
            "no existing block needs no backup");
        Check(PlcBlockToolLogic.DecideSclBackup(true, false, true, false, null, true, "FB").Action == SclBackupAction.Backup,
            "consistent block is backed up");
        Check(PlcBlockToolLogic.DecideSclBackup(true, null, null, false, null, true, "FB").Action == SclBackupAction.Backup,
            "unreadable consistency is treated as consistent");

        SclBackupDecision compiled = PlcBlockToolLogic.DecideSclBackup(true, false, false, true, true, true, "FB_Seq");
        Check(compiled.Action == SclBackupAction.Backup && compiled.Message.Contains("compiled"),
            "inconsistent block that compiles is backed up with a note");

        SclBackupDecision stillInconsistent = PlcBlockToolLogic.DecideSclBackup(true, false, false, true, false, true, "FB_Seq");
        Check(stillInconsistent.Action == SclBackupAction.FailNotExportable &&
            stillInconsistent.Message.Contains("FB_Seq") && stillInconsistent.Message.Contains("backup:false") &&
            stillInconsistent.Message.Contains("compiled once"),
            "still inconsistent block fails, names the block, the compile and the backup:false retry");

        SclBackupDecision noCompiler = PlcBlockToolLogic.DecideSclBackup(true, false, false, false, null, true, "FB_Seq");
        Check(noCompiler.Action == SclBackupAction.FailNotExportable && noCompiler.Message.Contains("Nothing was changed"),
            "inconsistent block without a compile service fails without changes");

        SclBackupDecision protectedBlock = PlcBlockToolLogic.DecideSclBackup(true, true, true, false, null, true, "FB_Kh");
        Check(protectedBlock.Action == SclBackupAction.FailNotExportable &&
            protectedBlock.Message.Contains("know-how protected") && protectedBlock.Message.Contains("backup:false"),
            "know-how protected block fails with a backup:false hint");

        SclBackupDecision skipped = PlcBlockToolLogic.DecideSclBackup(true, true, false, false, null, false, "FB_Seq");
        Check(skipped.Action == SclBackupAction.SkipWithWarning && skipped.Message.Contains("cannot be restored"),
            "backup:false skips the backup with a no-restore warning");
    }

    private static void TestImportSourceResolution()
    {
        string[] xml = { ".xml" };
        Throws<ArgumentException>(() => PlcImportSource.Resolve(null, null, "xml_content", xml, false),
            "neither source is rejected", "Provide exactly one of xml_content or file_path");
        Throws<ArgumentException>(() => PlcImportSource.Resolve("<x/>", @"C:\a.xml", "xml_content", xml, false),
            "both sources are rejected", "Provide exactly one of");

        PlcImportSourceResult inline = PlcImportSource.Resolve("<x/>", "  ", "xml_content", xml, false);
        Check(inline.Text == "<x/>" && inline.FullPath == null, "whitespace file_path counts as absent");

        foreach (string relative in new[] { "C:foo.xml", @"\foo.xml", "foo.xml", @"..\foo.xml" })
        {
            Throws<ArgumentException>(() => PlcImportSource.Resolve(null, relative, "xml_content", xml, false),
                "not fully qualified path '" + relative + "' is rejected", "fully qualified");
        }
        Check(PlcImportSource.IsFullyQualified(@"C:\a\b.xml") && PlcImportSource.IsFullyQualified("C:/a/b.xml") &&
            PlcImportSource.IsFullyQualified(@"\\server\share\b.xml"),
            "drive-rooted and UNC paths are fully qualified");

        string missing = Path.Combine(_temporary, "missing.xml");
        Throws<FileNotFoundException>(() => PlcImportSource.Resolve(null, missing, "xml_content", xml, false),
            "missing file is rejected", "Import file not found");

        string text = Path.Combine(_temporary, "block.txt");
        File.WriteAllText(text, "x");
        Throws<ArgumentException>(() => PlcImportSource.Resolve(null, text, "xml_content", xml, false),
            "wrong extension is rejected", ".xml");

        string empty = Path.Combine(_temporary, "empty.xml");
        File.WriteAllBytes(empty, new byte[0]);
        Throws<InvalidDataException>(() => PlcImportSource.Resolve(null, empty, "xml_content", xml, false),
            "empty file is rejected", "empty");

        Directory.CreateDirectory(Path.Combine(_temporary, "sub"));
        string valid = Path.Combine(_temporary, "FB_Seq.XML");
        File.WriteAllText(valid, "<Document/>");
        PlcImportSourceResult file = PlcImportSource.Resolve(
            null, Path.Combine(_temporary, "sub", "..", "FB_Seq.XML"), "xml_content", xml, false);
        Check(string.Equals(file.FullPath, Path.GetFullPath(valid), StringComparison.OrdinalIgnoreCase) && file.Text == null,
            "valid file resolves to its normalized full path without reading it");

        string scl = Path.Combine(_temporary, "FB \u00C4.scl");
        File.WriteAllText(scl, "FUNCTION_BLOCK \"FB_\u00C4\"\r\n// Kommentti \u00E4\u00F6\r\nBEGIN\r\nEND_FUNCTION_BLOCK\r\n", new UTF8Encoding(true));
        PlcImportSourceResult sclSource = PlcImportSource.Resolve(null, scl, "scl_code", new[] { ".scl" }, true);
        Check(sclSource.Text != null && sclSource.Text[0] == 'F' && sclSource.Text.Contains("\u00E4\u00F6"),
            "SCL file with a UTF-8 BOM is read as text without the BOM");
        Throws<ArgumentException>(() => PlcImportSource.Resolve(null, valid, "scl_code", new[] { ".scl" }, true),
            "SCL accepts only .scl files", ".scl");

        string sclNoBom = Path.Combine(_temporary, "FB_NoBom.scl");
        File.WriteAllText(sclNoBom, "// Lämpötila\r\n", new UTF8Encoding(false));
        PlcImportSourceResult noBom = PlcImportSource.Resolve(null, sclNoBom, "scl_code", new[] { ".scl" }, true);
        Check(noBom.Text == "// Lämpötila\r\n", "SCL file in UTF-8 without a BOM round-trips non-ASCII text");

        string sclUtf16 = Path.Combine(_temporary, "FB_Utf16.scl");
        File.WriteAllText(sclUtf16, "// Lämpötila", new UnicodeEncoding(false, true));
        Check(PlcImportSource.Resolve(null, sclUtf16, "scl_code", new[] { ".scl" }, true).Text == "// Lämpötila",
            "SCL file in UTF-16 LE with a BOM is decoded by its BOM");

        string sclAnsi = Path.Combine(_temporary, "FB_Ansi.scl");
        File.WriteAllBytes(sclAnsi, new byte[] { 0x2F, 0x2F, 0x20, 0x27, 0x4C, 0xE4, 0x6D, 0x70, 0xF6, 0x27, 0x0D, 0x0A });
        Throws<InvalidDataException>(() => PlcImportSource.Resolve(null, sclAnsi, "scl_code", new[] { ".scl" }, true),
            "SCL file in Windows-1252 without a BOM is rejected instead of being read with U+FFFD", "not valid UTF-8");

        MethodInfo declaration = typeof(TiaManager).GetMethod("GetSclBlockDeclaration", BindingFlags.NonPublic | BindingFlags.Instance);
        object parsed = declaration.Invoke(TiaManager.Instance, new object[] { sclSource.Text });
        Check(parsed.ToString().Contains("FB_\u00C4") && parsed.ToString().Contains("FUNCTION_BLOCK"),
            "SCL read from file parses to the declared block");
    }

    private static void TestRegistryGuards()
    {
        var registry = new ToolRegistry();
        string valid = Path.Combine(_temporary, "FB_Seq.XML");
        string scl = Path.Combine(_temporary, "FB \u00C4.scl");

        foreach (string tool in new[] { "tia_import_block", "tia_import_plc_type" })
        {
            ExpectError(registry, tool, "{}", "Provide exactly one of xml_content or file_path");
            ExpectError(registry, tool, Json(new { xml_content = "<x/>", file_path = valid }), "Provide exactly one of");
            ExpectError(registry, tool, Json(new { file_path = "relative.xml" }), "fully qualified");
            ExpectError(registry, tool, Json(new { file_path = Path.Combine(_temporary, "nope.xml") }), "Import file not found");
            ExpectError(registry, tool, Json(new { file_path = valid }), "Not attached to TIA Portal");
        }
        ExpectError(registry, "tia_import_scl_block", "{}", "Provide exactly one of scl_code or file_path");
        ExpectError(registry, "tia_import_scl_block", Json(new { file_path = valid }), ".scl");
        ExpectError(registry, "tia_import_scl_block", Json(new { file_path = scl, backup = false }), "Not attached to TIA Portal");
        ExpectError(registry, "tia_import_scl_block", Json(new { file_path = Path.Combine(_temporary, "FB_Ansi.scl") }), "not valid UTF-8");

        CallToolResult padded = Execute(registry, "tia_delete_plc_type", Json(new { type_name = " typeX" }));
        Check(padded.IsError && FirstText(padded).Contains("\"invalid_input\""),
            "tia_delete_plc_type rejects a padded name as JSON invalid_input before project access");
        CallToolResult conflict = Execute(registry, "tia_rename_plc_type", Json(new { type_name = "typeX", new_name = "typeY", on_conflict = "replace" }));
        Check(conflict.IsError && FirstText(conflict).Contains("\"invalid_input\"") && FirstText(conflict).Contains("on_conflict supports only"),
            "tia_rename_plc_type rejects on_conflict other than fail");
        CallToolResult badName = Execute(registry, "tia_rename_block", Json(new { block_name = "FB_A", new_name = "A/B" }));
        Check(badName.IsError && FirstText(badName).Contains("\"invalid_input\"") && FirstText(badName).Contains("new_name"),
            "tia_rename_block validates new_name strictly");
        CallToolResult same = Execute(registry, "tia_rename_block", Json(new { block_name = "FB_A", new_name = "FB_A" }));
        Check(same.IsError && FirstText(same).Contains("same as the current name"), "rename to the same name is rejected");
        CallToolResult groupPath = Execute(registry, "tia_rename_block", Json(new { block_name = "FB_A", new_name = "FB_B", group_path = "/A" }));
        Check(groupPath.IsError && FirstText(groupPath).Contains("\"invalid_input\""), "malformed group_path is rejected");

        ExpectError(registry, "tia_delete_plc_type", Json(new { type_name = "typeHmiManual" }), "Not attached to TIA Portal");
        ExpectError(registry, "tia_rename_plc_type", Json(new { type_name = "typeA", new_name = "typeB" }), "Not attached to TIA Portal");
        ExpectError(registry, "tia_rename_block", Json(new { block_name = "FB_A", new_name = "FB_B", group_path = "" }), "Not attached to TIA Portal");
    }

    private static void ExpectError(ToolRegistry registry, string tool, string json, string fragment)
    {
        CallToolResult result = Execute(registry, tool, json);
        Check(result.IsError && FirstText(result).Contains(fragment),
            tool + " " + json + " fails with '" + fragment + "' (got: " + FirstText(result) + ")");
    }

    private static CallToolResult Execute(ToolRegistry registry, string tool, string json)
    {
        TextWriter originalError = Console.Error;
        using (var swallowed = new StringWriter())
        using (JsonDocument document = JsonDocument.Parse(json))
        {
            Console.SetError(swallowed);
            try
            {
                return registry.Execute(tool, document.RootElement.Clone());
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
    }

    private static string Json(object value)
    {
        return JsonSerializer.Serialize(value);
    }

    private static string FirstText(CallToolResult result)
    {
        return result.Content[0].Text;
    }

    private static void Throws<T>(Action action, string description, string fragment) where T : Exception
    {
        try
        {
            action();
        }
        catch (T error)
        {
            if (!error.Message.Contains(fragment))
                throw new InvalidOperationException("FAIL: " + description + " (message: " + error.Message + ")");
            _checks++;
            return;
        }
        throw new InvalidOperationException("FAIL: " + description);
    }

    private static void Check(bool success, string description)
    {
        if (!success)
            throw new InvalidOperationException("FAIL: " + description);
        _checks++;
    }
}
