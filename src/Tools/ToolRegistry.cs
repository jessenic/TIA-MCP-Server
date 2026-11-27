using System;
using System.Collections.Generic;
using System.Text.Json;
using TiaOpennessMcp.Protocol;
using TiaOpennessMcp.Tia;
using TiaOpennessMcp.Rendering;

namespace TiaOpennessMcp.Tools
{
    public partial class ToolRegistry
    {
        private readonly List<ToolDefinition> _tools = new List<ToolDefinition>();
        private readonly Dictionary<string, Func<JsonElement?, CallToolResult>> _handlers =
            new Dictionary<string, Func<JsonElement?, CallToolResult>>(StringComparer.OrdinalIgnoreCase);
        private readonly Lazy<IHmiToolOperations> _hmiToolOperations;

        private const string PlcSimAdvancedOnlyNote =
            " S7-PLCSIM Advanced V8.0+ only (S7-1500 / ET 200SP); standard S7-PLCSIM and S7-1200 simulation are not supported.";

        public ToolRegistry()
            : this(new Lazy<IHmiToolOperations>(() => TiaManager.Instance))
        {
        }

        public ToolRegistry(IHmiToolOperations hmiToolOperations)
            : this(new Lazy<IHmiToolOperations>(() =>
                hmiToolOperations ?? throw new ArgumentNullException(nameof(hmiToolOperations))))
        {
        }

        private ToolRegistry(Lazy<IHmiToolOperations> hmiToolOperations)
        {
            _hmiToolOperations = hmiToolOperations;
            RegisterAllTools();
        }

        public List<ToolDefinition> GetTools() => _tools;

        public CallToolResult Execute(string toolName, JsonElement? args)
        {
            if (_handlers.TryGetValue(toolName, out var handler))
            {
                try
                {
                    return handler(args);
                }
                catch (TimeoutException ex)
                {
                    Console.Error.WriteLine($"[ToolRegistry] Timeout executing '{toolName}': {ex.Message}");
                    return CallToolResult.Error($"Timed out executing '{toolName}': {ex.Message}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[ToolRegistry] Error executing '{toolName}': {ex}");
                    // plcsim_* tools: a missing S7-PLCSIM Advanced API gets the diagnosis, not a bare load error.
                    string message = toolName.StartsWith("plcsim_", StringComparison.Ordinal)
                        ? PlcSimRuntimeProbe.DescribeToolFailure(ex)
                        : ex.Message;
                    return CallToolResult.Error($"Error executing '{toolName}': {message}");
                }
            }
            return CallToolResult.Error($"Unknown tool: '{toolName}'");
        }

        private void RegisterAllTools()
        {
            // 1. tia_list_processes
            Register("tia_list_processes", "List all running Siemens TIA Portal instances on the machine with PID, open project name and path, and mode. " +
                "Does not attach, so it is safe while an instance is busy. Pick the instance whose ProjectPath is the project you are working on " +
                "(ask the user when several match or none does), then call tia_connect with its Id as process_id.",
                schema => { },
                args => CallToolResult.Json(TiaManager.Instance.ListProcesses()));

            // 2. tia_connect
            Register("tia_connect", "Attach to one running TIA Portal instance by its process ID (from tia_list_processes). " +
                "The server never attaches on its own, so call this before any project tool. Attaching can block while that TIA Portal " +
                "instance is busy with a modal dialog (for example a firmware update); pick an idle instance or wait.",
                schema =>
                {
                    schema.Properties["process_id"] = new ToolProperty
                    {
                        Type = "integer",
                        Description = "Process ID (PID) of the TIA Portal instance to attach to, as listed by tia_list_processes."
                    };
                    schema.Required = new List<string> { "process_id" };
                },
                args =>
                {
                    int pid = GetIntProp(args, "process_id")
                        ?? throw new ArgumentException("process_id is required. Call tia_list_processes to see the running TIA Portal instances.");
                    var res = TiaManager.Instance.Connect(pid);
                    return CallToolResult.Json(res, isError: !res.Success);
                });

            // 3. tia_disconnect
            Register("tia_disconnect", "Disconnect / detach from the active TIA Portal instance.",
                schema => { },
                args =>
                {
                    TiaManager.Instance.Disconnect();
                    return CallToolResult.Text("Disconnected from TIA Portal.");
                });

            // 4. tia_get_project_info
            Register("tia_get_project_info", "Get detailed metadata for the currently open TIA Portal project (name, path, author, comments, timestamps).",
                schema => { },
                args => CallToolResult.Json(TiaManager.Instance.GetProjectInfo()));

            // 5. tia_open_project
            Register("tia_open_project", "Open a TIA project file (.ap21, .ap20) from disk in a new TIA Portal instance.",
                schema =>
                {
                    schema.Properties["project_path"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Full path to the project file (e.g. C:\\Projects\\MyProj.ap21)."
                    };
                    schema.Properties["with_gui"] = new ToolProperty
                    {
                        Type = "boolean",
                        Description = "Whether to start TIA Portal with user interface (true) or headless (false). Default: true.",
                        Default = true
                    };
                    schema.Required = new List<string> { "project_path" };
                },
                args =>
                {
                    string path = GetStringProp(args, "project_path") ?? throw new ArgumentException("project_path is required.");
                    bool withGui = GetBoolProp(args, "with_gui", true);
                    var res = TiaManager.Instance.OpenProject(path, withGui);
                    return CallToolResult.Json(res, isError: !res.Success);
                });

            // 6. tia_save_project
            Register("tia_save_project", "Save changes to the active TIA Portal project.",
                schema => { },
                args =>
                {
                    TiaManager.Instance.SaveProject();
                    return CallToolResult.Text("Project successfully saved.");
                });

            // 7. tia_close_project
            Register("tia_close_project", "Close the active TIA Portal project.",
                schema => { },
                args =>
                {
                    TiaManager.Instance.CloseProject();
                    return CallToolResult.Text("Project successfully closed.");
                });

            // 8. tia_list_devices
            Register("tia_list_devices", "List all devices in the project (PLCs, HMIs, Drives, distributed I/O) with their type identifiers and group folders.",
                schema => { },
                args => CallToolResult.Json(TiaManager.Instance.ListDevices()));

            // 9. tia_get_device_tree
            Register("tia_get_device_tree", "Get hierarchical hardware tree for a device (racks, slots, CPU, modules, subnets, interfaces).",
                schema =>
                {
                    schema.Properties["device_name"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Name of the device to inspect."
                    };
                    schema.Required = new List<string> { "device_name" };
                },
                args =>
                {
                    string name = GetStringProp(args, "device_name") ?? throw new ArgumentException("device_name is required.");
                    return CallToolResult.Json(TiaManager.Instance.GetDeviceTree(name));
                });

            // 10. tia_list_plcs
            Register("tia_list_plcs", "List all PLCs detected in the project with software summaries (block counts, UDT counts, tag tables).",
                schema => { },
                args => CallToolResult.Json(TiaManager.Instance.ListAllPlcs()));

            // 11. tia_list_blocks
            Register("tia_list_blocks", "List PLC software program blocks (OB, FB, FC, DB) with language, number, consistency, and group folder.",
                schema =>
                {
                    schema.Properties["plc_name"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Name of the target PLC (optional, defaults to first PLC)."
                    };
                    schema.Properties["block_type"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Filter by block type: OB, FB, FC, GlobalDB, or InstanceDB (optional)."
                    };
                    schema.Properties["folder"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Filter by folder path (optional)."
                    };
                },
                args =>
                {
                    string? plc = GetStringProp(args, "plc_name");
                    string? btype = GetStringProp(args, "block_type");
                    string? folder = GetStringProp(args, "folder");
                    return CallToolResult.Json(TiaManager.Instance.ListBlocks(plc, btype, folder));
                });

            // 12. tia_get_block_code
            Register("tia_get_block_code", "Read the code of a PLC block. By default (format 'auto') SCL, STL and DB blocks return the source text TIA Portal generates (Source, file extension .scl/.awl/.db) without SimaticML; other languages (LAD, FBD, GRAPH) and blocks whose source cannot be generated return SimaticML in XmlContent, with the reason in Warnings. " +
                "format 'xml' always returns SimaticML; format 'source' fails instead of falling back. Source generation can fail while TIA Portal is online with the PLC. " +
                "When no block has the name, a PLC data type (UDT) of that name is used (BlockType 'PlcType', ProgrammingLanguage 'UDT', source extension .udt). " +
                "The source of a single SCL block (without dependencies) can be edited and passed to tia_import_scl_block. ExtractedScl is a deprecated copy of Source for SCL blocks. " +
                "With include_xml, a failing SimaticML export is reported in Warnings instead of failing the call.",
                schema =>
                {
                    schema.Properties["block_name"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Name of the block, or of a PLC data type (UDT), to export (e.g. 'Main', 'PumpControl', 'DB_Config', 'UDT_Motor')."
                    };
                    schema.Properties["plc_name"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Name of the target PLC (optional, defaults to first PLC)."
                    };
                    schema.Properties["format"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "'auto' (default): generated source for SCL/STL/DB blocks and PLC data types, SimaticML otherwise or when generation fails. 'source': generated source only, fails if not possible. 'xml': SimaticML only.",
                        Enum = new List<string> { "auto", "source", "xml" },
                        Default = "auto"
                    };
                    schema.Properties["include_xml"] = new ToolProperty
                    {
                        Type = "boolean",
                        Description = "Also return the SimaticML in XmlContent when source text is returned (default false; the XML is often 80-100 kB).",
                        Default = false
                    };
                    schema.Properties["with_dependencies"] = new ToolProperty
                    {
                        Type = "boolean",
                        Description = "Generate the source together with the blocks and types it depends on (default false). Such output holds several blocks and cannot be passed to tia_import_scl_block.",
                        Default = false
                    };
                    schema.Required = new List<string> { "block_name" };
                },
                args =>
                {
                    string block = GetStringProp(args, "block_name") ?? throw new ArgumentException("block_name is required.");
                    string? plc = GetStringProp(args, "plc_name");
                    return CallToolResult.Json(TiaManager.Instance.GetBlockCode(
                        plc,
                        block,
                        GetStringProp(args, "format"),
                        GetBoolProp(args, "include_xml", false),
                        GetBoolProp(args, "with_dependencies", false)));
                });

            // 13. tia_import_block
            Register("tia_import_block", "Import or update a PLC block from SimaticML XML, given inline (xml_content) or as a server-local .xml file (file_path, preferred for large blocks). " +
                "OB attributes CyclicTime and PhaseOffset in SimaticML are microseconds (100 ms = <CyclicTime>100000</CyclicTime>); there is no 'PhaseShift' attribute. " +
                "The XML is imported unchanged; suspicious OB settings are listed as warnings after the result text, and the cycle settings TIA stored for imported cyclic interrupt OBs are read back.",
                schema =>
                {
                    schema.Properties["xml_content"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "The SimaticML XML string containing the block definition. Use this or file_path, not both."
                    };
                    schema.Properties["file_path"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Fully qualified server-local path to a SimaticML .xml file (e.g. one written by tia_export_project_files). Use this or xml_content, not both."
                    };
                    schema.Properties["plc_name"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Name of the target PLC (optional, defaults to first PLC)."
                    };
                    schema.Properties["overwrite"] = new ToolProperty
                    {
                        Type = "boolean",
                        Description = "Whether to overwrite existing block if it exists (default true).",
                        Default = true
                    };
                    schema.Properties["folder"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Target group folder path for the block (optional)."
                    };
                },
                args =>
                {
                    PlcImportSourceResult source = PlcImportSource.Resolve(
                        GetStringProp(args, "xml_content"), GetStringProp(args, "file_path"), "xml_content", new[] { ".xml" }, readText: false);
                    string? plc = GetStringProp(args, "plc_name");
                    bool overwrite = GetBoolProp(args, "overwrite", true);
                    string? folder = GetStringProp(args, "folder");
                    string msg = TiaManager.Instance.ImportBlock(plc, source.Text, overwrite, folder, source.FullPath);
                    return CallToolResult.Text(msg);
                });

            // 13a. tia_import_scl_block
            Register("tia_import_scl_block", "Import a complete SCL block source, given inline (scl_code) or as a server-local .scl file (file_path); the server creates the temporary .scl file. " +
                "Existing same-name blocks are backed up (unless backup is false), deleted, and replaced; failed imports attempt to restore the backup. " +
                "An inconsistent existing block is compiled once before the backup; if it stays inconsistent (or is know-how protected) nothing is replaced unless backup is false.",
                schema =>
                {
                    schema.Properties["scl_code"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Complete SCL source for exactly one FUNCTION, FUNCTION_BLOCK, ORGANIZATION_BLOCK, or DATA_BLOCK. Use this or file_path, not both."
                    };
                    schema.Properties["file_path"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Fully qualified server-local path to a .scl file with exactly one block declaration (read as UTF-8 unless it has a BOM). Use this or scl_code, not both."
                    };
                    schema.Properties["plc_name"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Name of the target PLC (optional, defaults to first PLC)."
                    };
                    schema.Properties["folder"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Target block group path (optional; an existing block's group is preserved when omitted)."
                    };
                    schema.Properties["compile_after_import"] = new ToolProperty
                    {
                        Type = "boolean",
                        Description = "Compile the target PLC after import (default true). Compilation errors are reported and do not roll back the imported block.",
                        Default = true
                    };
                    schema.Properties["open_after_import"] = new ToolProperty
                    {
                        Type = "boolean",
                        Description = "Open the imported block in the TIA Portal editor after import (default false).",
                        Default = false
                    };
                    schema.Properties["backup"] = new ToolProperty
                    {
                        Type = "boolean",
                        Description = "Export the existing same-name block as a backup before replacing it (default true). If that block is inconsistent it is compiled once first; " +
                            "if it stays inconsistent or is know-how protected, the import fails without replacing it. With false no backup is taken (a warning is returned) " +
                            "and a failed import cannot restore the previous block.",
                        Default = true
                    };
                },
                args =>
                {
                    PlcImportSourceResult source = PlcImportSource.Resolve(
                        GetStringProp(args, "scl_code"), GetStringProp(args, "file_path"), "scl_code", new[] { ".scl" }, readText: true);
                    string? plc = GetStringProp(args, "plc_name");
                    string? folder = GetStringProp(args, "folder");
                    bool compileAfterImport = GetBoolProp(args, "compile_after_import", true);
                    bool openAfterImport = GetBoolProp(args, "open_after_import", false);
                    bool backup = GetBoolProp(args, "backup", true);
                    return CallToolResult.Json(
                        TiaManager.Instance.ImportSclBlock(plc, source.Text ?? "", folder, compileAfterImport, openAfterImport, backup, source.FullPath));
                });

            // 14. tia_delete_block
            Register("tia_delete_block", "Delete a PLC program block (OB/FB/FC/DB) from the project. PLC data types (UDTs) are deleted with tia_delete_plc_type.",
                schema =>
                {
                    schema.Properties["block_name"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Name of the block to delete."
                    };
                    schema.Properties["plc_name"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Name of the target PLC (optional, defaults to first PLC)."
                    };
                    schema.Required = new List<string> { "block_name" };
                },
                args =>
                {
                    string block = GetStringProp(args, "block_name") ?? throw new ArgumentException("block_name is required.");
                    string? plc = GetStringProp(args, "plc_name");
                    TiaManager.Instance.DeleteBlock(plc, block);
                    return CallToolResult.Text($"Block '{block}' deleted successfully.");
                });

            // 15. tia_list_plc_types
            Register("tia_list_plc_types", "List PLC user data types (UDTs) with consistency state and folder path.",
                schema =>
                {
                    schema.Properties["plc_name"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Name of the target PLC (optional, defaults to first PLC)."
                    };
                },
                args =>
                {
                    string? plc = GetStringProp(args, "plc_name");
                    return CallToolResult.Json(TiaManager.Instance.ListPlcTypes(plc));
                });

            // 16. tia_get_plc_type
            Register("tia_get_plc_type", "Export and read the SimaticML XML definition of a PLC User Data Type (UDT).",
                schema =>
                {
                    schema.Properties["type_name"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Name of the PLC User Data Type (UDT)."
                    };
                    schema.Properties["plc_name"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Name of the target PLC (optional, defaults to first PLC)."
                    };
                    schema.Required = new List<string> { "type_name" };
                },
                args =>
                {
                    string type = GetStringProp(args, "type_name") ?? throw new ArgumentException("type_name is required.");
                    string? plc = GetStringProp(args, "plc_name");
                    return CallToolResult.Text(TiaManager.Instance.GetPlcType(plc, type));
                });

            // 17. tia_import_plc_type
            Register("tia_import_plc_type", "Import or update a PLC User Data Type (UDT) from SimaticML XML, given inline (xml_content) or as a server-local .xml file (file_path). " +
                "Importing under a new name creates a second type; use tia_rename_plc_type to rename.",
                schema =>
                {
                    schema.Properties["xml_content"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "The SimaticML XML string containing the UDT definition. Use this or file_path, not both."
                    };
                    schema.Properties["file_path"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Fully qualified server-local path to a SimaticML .xml file of the UDT (e.g. one written by tia_export_project_files). Use this or xml_content, not both."
                    };
                    schema.Properties["plc_name"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Name of the target PLC (optional, defaults to first PLC)."
                    };
                    schema.Properties["overwrite"] = new ToolProperty
                    {
                        Type = "boolean",
                        Description = "Whether to overwrite existing UDT (default true).",
                        Default = true
                    };
                },
                args =>
                {
                    PlcImportSourceResult source = PlcImportSource.Resolve(
                        GetStringProp(args, "xml_content"), GetStringProp(args, "file_path"), "xml_content", new[] { ".xml" }, readText: false);
                    string? plc = GetStringProp(args, "plc_name");
                    bool overwrite = GetBoolProp(args, "overwrite", true);
                    string msg = TiaManager.Instance.ImportPlcType(plc, source.Text, overwrite, source.FullPath);
                    return CallToolResult.Text(msg);
                });

            // 18. tia_list_tag_tables
            Register("tia_list_tag_tables", "List all PLC tag tables and folder groups.",
                schema =>
                {
                    schema.Properties["plc_name"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Name of the target PLC (optional, defaults to first PLC)."
                    };
                },
                args =>
                {
                    string? plc = GetStringProp(args, "plc_name");
                    return CallToolResult.Json(TiaManager.Instance.ListTagTables(plc));
                });

            // 19. tia_get_tags
            Register("tia_get_tags", "Read tags from a tag table (or all tag tables) with data types, logical addresses (%I, %Q, %M), and comments.",
                schema =>
                {
                    schema.Properties["table_name"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Name of specific tag table (optional, defaults to all tables)."
                    };
                    schema.Properties["plc_name"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Name of the target PLC (optional, defaults to first PLC)."
                    };
                },
                args =>
                {
                    string? table = GetStringProp(args, "table_name");
                    string? plc = GetStringProp(args, "plc_name");
                    return CallToolResult.Json(TiaManager.Instance.GetTags(plc, table));
                });

            // 20. tia_compile
            Register("tia_compile", "Compile PLC software to check syntax, consistency, and generate machine code. Returns errors, warnings, and messages. " +
                "TIA Portal compiles only changed blocks; Openness has no rebuild-all option. When no block needed compiling the result has UpToDate true and a Note, and warnings from an earlier compile are not repeated (see tia_get_compile_state).",
                schema =>
                {
                    schema.Properties["plc_name"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Name of the target PLC to compile (optional, defaults to first PLC)."
                    };
                },
                args =>
                {
                    string? plc = GetStringProp(args, "plc_name");
                    return CallToolResult.Json(TiaManager.Instance.Compile(plc));
                });

            // 21. tia_get_status
            Register("tia_get_status", "Get the current TIA Openness connection status, attached process ID, open project name and path.",
                schema => { },
                args => CallToolResult.Json(TiaManager.Instance.GetStatus()));

            // 22. hmi_list_targets
            Register("hmi_list_targets", "List all HMI targets (WinCC Unified runtime devices/software) in the active project.",
                schema => { },
                args => CallToolResult.Json(TiaManager.Instance.ListHmiTargets()));

            // 23. hmi_list_screens
            Register("hmi_list_screens", "List all screens in the specified HMI software target with dimensions and element counts.",
                schema =>
                {
                    schema.Properties["target_name"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Name of the HMI device or software target (optional, defaults to first found)."
                    };
                },
                args =>
                {
                    string? target = GetStringProp(args, "target_name");
                    return CallToolResult.Json(TiaManager.Instance.ListHmiScreens(target));
                });

            // 24. hmi_create_screen
            Register("hmi_create_screen", "Create or ensure existence of an HMI screen with specified dimensions.",
                schema =>
                {
                    schema.Properties["screen_name"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Name of the screen to create (e.g. Screen_1, Screen_2)."
                    };
                    schema.Properties["width"] = new ToolProperty
                    {
                        Type = "integer",
                        Description = "Width of the screen in pixels (optional)."
                    };
                    schema.Properties["height"] = new ToolProperty
                    {
                        Type = "integer",
                        Description = "Height of the screen in pixels (optional)."
                    };
                    schema.Properties["target_name"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Target HMI software name (optional)."
                    };
                    schema.Required = new List<string> { "screen_name" };
                },
                args =>
                {
                    string screen = GetStringProp(args, "screen_name") ?? throw new ArgumentException("screen_name is required.");
                    uint? width = GetUIntProp(args, "width");
                    uint? height = GetUIntProp(args, "height");
                    string? target = GetStringProp(args, "target_name");
                    return CallToolResult.Json(TiaManager.Instance.CreateHmiScreen(screen, width, height, target));
                });

            // 25. hmi_delete_screen
            Register("hmi_delete_screen", "Delete an HMI screen from the HMI software target.",
                schema =>
                {
                    schema.Properties["screen_name"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Name of the screen to delete."
                    };
                    schema.Properties["target_name"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Target HMI software name (optional)."
                    };
                    schema.Required = new List<string> { "screen_name" };
                },
                args =>
                {
                    string screen = GetStringProp(args, "screen_name") ?? throw new ArgumentException("screen_name is required.");
                    string? target = GetStringProp(args, "target_name");
                    TiaManager.Instance.DeleteHmiScreen(screen, target);
                    return CallToolResult.Text($"Screen '{screen}' deleted successfully.");
                });

            // 26. hmi_get_screen_items
            Register("hmi_get_screen_items", "Get all screen items (buttons, textboxes, shapes, IO fields) on a screen with positions, colors, scripts, and dynamizations.",
                schema =>
                {
                    schema.Properties["screen_name"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Name of the screen to inspect."
                    };
                    schema.Properties["target_name"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Target HMI software name (optional)."
                    };
                    schema.Required = new List<string> { "screen_name" };
                },
                args =>
                {
                    string screen = GetStringProp(args, "screen_name") ?? throw new ArgumentException("screen_name is required.");
                    string? target = GetStringProp(args, "target_name");
                    return CallToolResult.Json(TiaManager.Instance.GetHmiScreenItems(screen, target));
                });

            Register("hmi_get_screen_item", "Read one screen item with its complete supported properties, event scripts, and full tag/expression dynamization state.",
                schema =>
                {
                    schema.Properties["screen_name"] = new ToolProperty { Type = "string", Description = "Name of the screen containing the item." };
                    schema.Properties["item_name"] = new ToolProperty { Type = "string", Description = "Name of the item to inspect." };
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = "Target HMI software name (optional)." };
                    schema.Required = new List<string> { "screen_name", "item_name" };
                },
                args =>
                {
                    string screen = GetStringProp(args, "screen_name") ?? throw new ArgumentException("screen_name is required.");
                    string item = GetStringProp(args, "item_name") ?? throw new ArgumentException("item_name is required.");
                    string? target = GetStringProp(args, "target_name");
                    return CallToolResult.Json(_hmiToolOperations.Value.GetHmiScreenItem(screen, item, target));
                });

            Register("hmi_update_screen_item", "Update allowlisted screen-item properties in place (the item is never deleted or recreated) and verify each value by reading it back. Supports position, size, colors, Visible, Enabled, IO-field OutputFormat, text alignment, screen-window Screen, Text (keeps the existing paragraph style), and Font as a nested object or dotted Font.<property> paths. A property that also has a dynamization is still updated and reported under 'warnings'; the dynamization is not changed.",
                schema =>
                {
                    schema.Properties["screen_name"] = new ToolProperty { Type = "string", Description = "Name of the screen containing the item." };
                    schema.Properties["item_name"] = new ToolProperty { Type = "string", Description = "Name of the item to update." };
                    var fontWeights = new List<string> { "Light", "Normal", "SemiBold", "Bold" };
                    var fontNames = new List<string> { "Arial", "TimesNewRoman", "SimSun", "SiemensSans" };
                    var horizontalAlignments = new List<string> { "Left", "Center", "Right", "Stretch" };
                    var verticalAlignments = new List<string> { "Top", "Center", "Bottom", "Stretch" };
                    schema.Properties["item_properties"] = new ToolProperty
                    {
                        Type = "object",
                        Description = "Non-empty object of supported property/value pairs; use the property names exactly as listed. Allowed: Visible, Enabled, Left, Top, Width, Height, CenterX, CenterY, Radius, BackColor, ForeColor, BorderColor, BorderWidth, OutputFormat, HorizontalTextAlignment, VerticalTextAlignment, Screen, Text, Font {Size, Weight, Italic, Underline, StrikeOut, Name, Bold} and dotted Font.<property> paths (for example \"Font.Size\": 14). Each property may be given only once. A property the item type does not have fails before any change.",
                        Properties = new Dictionary<string, ToolProperty>
                        {
                            ["Visible"] = new ToolProperty { Type = "boolean", Description = "Whether the item is visible." },
                            ["Enabled"] = new ToolProperty { Type = "boolean", Description = "Whether the item can be operated in runtime." },
                            ["Left"] = new ToolProperty { Type = "integer", Description = "Horizontal position." },
                            ["Top"] = new ToolProperty { Type = "integer", Description = "Vertical position." },
                            ["Width"] = new ToolProperty { Type = "integer", Description = "Item width." },
                            ["Height"] = new ToolProperty { Type = "integer", Description = "Item height." },
                            ["CenterX"] = new ToolProperty { Type = "integer", Description = "Circle center X coordinate." },
                            ["CenterY"] = new ToolProperty { Type = "integer", Description = "Circle center Y coordinate." },
                            ["Radius"] = new ToolProperty { Type = "integer", Description = "Circle radius." },
                            ["BackColor"] = new ToolProperty { Type = "string", Description = "Background color as #RRGGBB or #AARRGGBB." },
                            ["ForeColor"] = new ToolProperty { Type = "string", Description = "Foreground/text color as #RRGGBB or #AARRGGBB." },
                            ["BorderColor"] = new ToolProperty { Type = "string", Description = "Border color as #RRGGBB or #AARRGGBB." },
                            ["BorderWidth"] = new ToolProperty { Type = "integer", Description = "Border width." },
                            ["OutputFormat"] = new ToolProperty { Type = "string", Description = "IO field output format, e.g. '{F1} °C'. Plain Unicode text, not HTML; do not use HTML entities." },
                            ["HorizontalTextAlignment"] = new ToolProperty { Type = "string", Enum = horizontalAlignments, Description = "Horizontal text alignment." },
                            ["VerticalTextAlignment"] = new ToolProperty { Type = "string", Enum = verticalAlignments, Description = "Vertical text alignment." },
                            ["Screen"] = new ToolProperty { Type = "string", Description = "Screen windows only: exact name of an existing screen in the same HMI to show in the window." },
                            ["Text"] = new ToolProperty
                            {
                                Type = null,
                                AnyOf = new List<ToolProperty>
                                {
                                    new ToolProperty { Type = "string", Description = "Plain Unicode text (ä, ö, å, °) for every active language; do not use HTML entities. The whole text becomes one paragraph that keeps the existing <body> and first <p> attributes such as style; further paragraphs and inline formatting are dropped (reported under 'warnings'). A string that starts with <body is taken as raw markup and must be well-formed." },
                                    new ToolProperty { Type = "object", Description = "{\"html\": \"<body><p>...</p></body>\"} raw markup for every language (recommended for markup), or a language map such as {\"en-US\": \"Start\", \"fi-FI\": \"Käynnistä\"} that covers every active language (values are plain text or {\"html\": ...})." }
                                },
                                Description = "Item text (buttons, texts, text boxes): a plain string or an object, see the alternatives."
                            },
                            ["Font"] = new ToolProperty
                            {
                                Type = "object",
                                Description = "Font part, updated in place; give only the properties to change.",
                                Properties = new Dictionary<string, ToolProperty>
                                {
                                    ["Size"] = new ToolProperty { Type = "number", Description = "Font size (DIU)." },
                                    ["Weight"] = new ToolProperty { Type = "string", Enum = fontWeights, Description = "Font weight." },
                                    ["Bold"] = new ToolProperty { Type = "boolean", Description = "Convenience for Weight: true = Bold, false = Normal. Do not combine with Weight." },
                                    ["Italic"] = new ToolProperty { Type = "boolean", Description = "Italic text." },
                                    ["Underline"] = new ToolProperty { Type = "boolean", Description = "Underlined text." },
                                    ["StrikeOut"] = new ToolProperty { Type = "boolean", Description = "Struck-out text." },
                                    ["Name"] = new ToolProperty { Type = "string", Enum = fontNames, Description = "Font family." }
                                },
                                AdditionalProperties = false
                            },
                            ["Font.Size"] = new ToolProperty { Type = "number", Description = "Dotted alias of Font.Size." },
                            ["Font.Weight"] = new ToolProperty { Type = "string", Enum = fontWeights, Description = "Dotted alias of Font.Weight." },
                            ["Font.Bold"] = new ToolProperty { Type = "boolean", Description = "Dotted alias of Font.Bold (true = Bold, false = Normal)." },
                            ["Font.Italic"] = new ToolProperty { Type = "boolean", Description = "Dotted alias of Font.Italic." },
                            ["Font.Underline"] = new ToolProperty { Type = "boolean", Description = "Dotted alias of Font.Underline." },
                            ["Font.StrikeOut"] = new ToolProperty { Type = "boolean", Description = "Dotted alias of Font.StrikeOut." },
                            ["Font.Name"] = new ToolProperty { Type = "string", Enum = fontNames, Description = "Dotted alias of Font.Name." }
                        },
                        AdditionalProperties = false
                    };
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = "Target HMI software name (optional)." };
                    schema.Required = new List<string> { "screen_name", "item_name", "item_properties" };
                },
                args =>
                {
                    string screen = GetStringProp(args, "screen_name") ?? throw new ArgumentException("screen_name is required.");
                    string item = GetStringProp(args, "item_name") ?? throw new ArgumentException("item_name is required.");
                    if (!args.HasValue || !args.Value.TryGetProperty("item_properties", out JsonElement properties))
                        throw new ArgumentException("item_properties is required.");
                    string? target = GetStringProp(args, "target_name");
                    return CallToolResult.Json(
                        _hmiToolOperations.Value.UpdateHmiScreenItemProperties(screen, item, properties, target));
                });

            Register("hmi_read_event_scripts", "Read event scripts and the V21-supported event names for a screen or one screen item.",
                schema =>
                {
                    schema.Properties["screen_name"] = new ToolProperty { Type = "string", Description = "Screen name. When item_name is omitted, reads the screen's events." };
                    schema.Properties["item_name"] = new ToolProperty { Type = "string", Description = "Optional item name; omit to read screen events." };
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = "Target HMI software name (optional)." };
                    schema.Required = new List<string> { "screen_name" };
                },
                args =>
                {
                    string screen = GetStringProp(args, "screen_name") ?? throw new ArgumentException("screen_name is required.");
                    string? item = GetStringProp(args, "item_name");
                    string? target = GetStringProp(args, "target_name");
                    return CallToolResult.Json(_hmiToolOperations.Value.GetHmiEventScripts(screen, item, target));
                });

            Register("hmi_set_event_script", "Create or update one supported screen or item event script in place and verify the script read-back (line endings and trailing whitespace are ignored in the comparison).",
                schema =>
                {
                    schema.Properties["screen_name"] = new ToolProperty { Type = "string", Description = "Screen name." };
                    schema.Properties["item_name"] = new ToolProperty { Type = "string", Description = "Optional item name; omit to update a screen event." };
                    schema.Properties["event_type"] = new ToolProperty { Type = "string", Description = "Event enum name supported by the selected screen or item (see hmi_read_event_scripts)." };
                    schema.Properties["script"] = new ToolProperty { Type = "string", Description = "Complete JavaScript event handler source." };
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = "Target HMI software name (optional)." };
                    schema.Required = new List<string> { "screen_name", "event_type", "script" };
                },
                args =>
                {
                    string screen = GetStringProp(args, "screen_name") ?? throw new ArgumentException("screen_name is required.");
                    string eventType = GetStringProp(args, "event_type") ?? throw new ArgumentException("event_type is required.");
                    string script = GetStringProp(args, "script") ?? throw new ArgumentException("script is required.");
                    string? item = GetStringProp(args, "item_name");
                    string? target = GetStringProp(args, "target_name");
                    return CallToolResult.Json(
                        _hmiToolOperations.Value.UpdateHmiEventScript(screen, eventType, script, item, target));
                });

            Register("hmi_get_js_type_definitions", "Get the server-bundled, documented subset of WinCC Unified runtime JavaScript type declarations. This is not a complete Siemens declaration set.",
                schema => { },
                args =>
                {
                    const string resourceUri = "tia://docs/hmi-javascript-type-definitions";
                    ResourceContent definition = McpResourceCatalog.Read(resourceUri).Contents[0];
                    return CallToolResult.Json(new
                    {
                        ResourceUri = resourceUri,
                        MimeType = definition.MimeType,
                        Scope = "Documented subset; not a complete or vendor-generated Siemens V21 declaration set.",
                        Definitions = definition.Text
                    });
                });

            Register("hmi_export_json", "Export a Unified HMI target as a versioned JSON directory containing screens, tag tables, alarms, optional styles, and file hashes.",
                schema =>
                {
                    schema.Properties["output_directory"] = new ToolProperty { Type = "string", Description = "Optional local server directory. Omit to create a remote-readable staged export and return its transfer_id." };
                    schema.Properties["compact"] = new ToolProperty { Type = "boolean", Description = "Omit item properties equal to their TIA-created type defaults (default: true)." };
                    schema.Properties["script_files"] = new ToolProperty { Type = "boolean", Description = "Write event scripts into .js sidecar files instead of inline JSON strings (default: false)." };
                    schema.Properties["include_library"] = new ToolProperty { Type = "boolean", Description = "Package the complete project type library, including faceplates, dynamic SVG definitions and PLC type dependencies, in a hashed V21 .zal21 archive (default: true). Requires committed library versions." };
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = "Target HMI software/device selector (optional when only one target exists)." };
                },
                args =>
                {
                    string? directory = GetStringProp(args, "output_directory");
                    bool compact = GetBoolProp(args, "compact", true);
                    bool scriptFiles = GetBoolProp(args, "script_files", false);
                    string? target = GetStringProp(args, "target_name");
                    bool includeLibrary = GetBoolProp(args, "include_library", true);
                    string? transferId = null;
                    if (string.IsNullOrWhiteSpace(directory))
                        transferId = HmiJsonTransferStore.CreateExport(out directory);
                    string exportDirectory = directory ?? throw new InvalidOperationException("The export directory could not be resolved.");
                    try
                    {
                        Dictionary<string, object> result = TiaManager.Instance.ExportHmiJson(exportDirectory, compact, scriptFiles, target, includeLibrary);
                        if (transferId != null)
                        {
                            result.Remove("directory");
                            result.Remove("fileHashes");
                            result["transferId"] = transferId;
                            result["files"] = HmiJsonTransferStore.ListFiles(transferId);
                        }
                        return CallToolResult.Json(result);
                    }
                    catch
                    {
                        if (transferId != null)
                            HmiJsonTransferStore.Delete(transferId);
                        throw;
                    }
                });

            Register("hmi_read_export_file", "Read a staged HMI JSON export file in bounded Base64 chunks.",
                schema =>
                {
                    schema.Properties["transfer_id"] = new ToolProperty { Type = "string", Description = "Transfer ID returned by hmi_export_json." };
                    schema.Properties["relative_path"] = new ToolProperty { Type = "string", Description = "Export file path, such as manifest.json or screens/0001-Main.json." };
                    schema.Properties["offset"] = new ToolProperty { Type = "integer", Description = "Byte offset (default: 0); use nextOffset from the previous response." };
                    schema.Properties["chunk_bytes"] = new ToolProperty { Type = "integer", Description = "Maximum bytes to return (default: 262144; maximum: 524288)." };
                    schema.Required = new List<string> { "transfer_id", "relative_path" };
                },
                args =>
                {
                    string transferId = GetStringProp(args, "transfer_id") ?? throw new ArgumentException("transfer_id is required.");
                    string relativePath = GetStringProp(args, "relative_path") ?? throw new ArgumentException("relative_path is required.");
                    long offset = GetLongProp(args, "offset") ?? 0;
                    int chunkBytes = GetIntProp(args, "chunk_bytes") ?? 0;
                    return CallToolResult.Json(HmiJsonTransferStore.ReadChunk(transferId, relativePath, offset, chunkBytes));
                });

            Register("hmi_begin_import", "Create a temporary HMI JSON import transfer. Upload manifest.json and every referenced file with hmi_upload_import_file.",
                schema => { },
                args => CallToolResult.Json(new Dictionary<string, object>
                {
                    ["transferId"] = HmiJsonTransferStore.CreateImport(),
                    ["maxFileBytes"] = 10 * 1024 * 1024,
                    ["maxTransferBytes"] = 100 * 1024 * 1024,
                    ["maxChunkBytes"] = 512 * 1024,
                    ["expiresAfterHours"] = 24
                }));

            Register("hmi_upload_import_file", "Upload a file to a staged HMI JSON import using sequential Base64 chunks.",
                schema =>
                {
                    schema.Properties["transfer_id"] = new ToolProperty { Type = "string", Description = "Import transfer ID returned by hmi_begin_import." };
                    schema.Properties["relative_path"] = new ToolProperty { Type = "string", Description = "Relative file path, such as manifest.json or screens/0001-Main.json." };
                    schema.Properties["content_base64"] = new ToolProperty { Type = "string", Description = "Base64-encoded bytes for this chunk; chunks are limited to 512 KiB decoded." };
                    schema.Properties["offset"] = new ToolProperty { Type = "integer", Description = "Byte offset of this chunk; begin at zero and use receivedBytes from the previous response." };
                    schema.Properties["total_bytes"] = new ToolProperty { Type = "integer", Description = "Total decoded file size in bytes, no more than 10 MiB." };
                    schema.Required = new List<string> { "transfer_id", "relative_path", "content_base64", "offset", "total_bytes" };
                },
                args =>
                {
                    string transferId = GetStringProp(args, "transfer_id") ?? throw new ArgumentException("transfer_id is required.");
                    string relativePath = GetStringProp(args, "relative_path") ?? throw new ArgumentException("relative_path is required.");
                    string content = GetStringProp(args, "content_base64") ?? throw new ArgumentException("content_base64 is required.");
                    long offset = GetLongProp(args, "offset") ?? throw new ArgumentException("offset is required.");
                    long totalBytes = GetLongProp(args, "total_bytes") ?? throw new ArgumentException("total_bytes is required.");
                    return CallToolResult.Json(HmiJsonTransferStore.WriteChunk(transferId, relativePath, content, offset, totalBytes));
                });

            Register("hmi_render_json", "Render exported Unified HMI JSON offline to lossless PNG previews, numbered layout overlays and geometry/warnings reports. Resolves static nested screen windows; never connects to TIA or executes scripts. Complex controls use explicit placeholders.",
                schema =>
                {
                    schema.Properties["input_directory"] = new ToolProperty { Type = "string", Description = "Export directory containing manifest.json. Full exports or compact exports with defaultsFile give best fidelity." };
                    schema.Properties["output_directory"] = new ToolProperty { Type = "string", Description = "Separate output directory for PNGs, layout reports and render-index.json. Existing output files are never overwritten." };
                    schema.Properties["screen_name"] = new ToolProperty { Type = "string", Description = "Optional screen name or group/name path; omitted renders every exported screen." };
                    schema.Properties["language"] = new ToolProperty { Type = "string", Description = "Text language from manifest (default: en-US when available, otherwise first exported language)." };
                    schema.Properties["scale"] = new ToolProperty { Type = "number", Description = "Output pixels per screen pixel, 0.5 to 4 (default: 2). Maximum 8192 per axis and 32 megapixels." };
                    schema.Required = new List<string> { "input_directory", "output_directory" };
                },
                args =>
                {
                    string input = GetStringProp(args, "input_directory") ?? throw new ArgumentException("input_directory is required.");
                    string output = GetStringProp(args, "output_directory") ?? throw new ArgumentException("output_directory is required.");
                    double scale = args.HasValue && args.Value.TryGetProperty("scale", out JsonElement value) ? value.GetDouble() : 2;
                    return CallToolResult.Json(HmiJsonRenderer.Render(input, output,
                        GetStringProp(args, "screen_name"), GetStringProp(args, "language"), scale));
                });

            Register("hmi_import_json", "Upsert a versioned Unified HMI JSON directory or staged transfer under an Openness transaction, verify it, compile, and never save the project. " +
                "An item without a text, dynamizations or events key keeps those sections unchanged; an empty list removes all dynamizations or events. " +
                "Existing Singlebit/Bitmask mapping tables are updated in place; a changed condition type or bit layout needs onConflict 'replace' on that dynamization (only the dynamization is recreated); bitmask entries must stay in TIA's exported order. A dry run reports these mapping-table refusals. " +
                "Flags enum properties such as WindowFlags take one name or names separated by ',' or '|', or 'None'. " +
                "Script read-back ignores line endings and trailing whitespace. " +
                "Import never reorders existing screen items: manifest or screen zOrder 'keep' (default) reports order differences in zOrderWarnings, 'require' fails and rolls back.",
                schema =>
                {
                    schema.Properties["input_directory"] = new ToolProperty { Type = "string", Description = "Server-local directory containing manifest.json and referenced files. Use this or transfer_id, not both." };
                    schema.Properties["transfer_id"] = new ToolProperty { Type = "string", Description = "Staged import ID returned by hmi_begin_import. Use this or input_directory, not both." };
                    schema.Properties["prune_items"] = new ToolProperty { Type = "boolean", Description = "Delete screen items absent from imported screen files (default: false)." };
                    schema.Properties["dry_run"] = new ToolProperty { Type = "boolean", Description = "Validate the files and target without making changes (default: false)." };
                    schema.Properties["verify_hashes"] = new ToolProperty { Type = "boolean", Description = "Verify exported file hashes (default: true). Set false only when intentionally editing exported JSON without refreshing manifest hashes." };
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = "Target HMI software/device selector (optional when only one target exists)." };
                },
                args =>
                {
                    string? directory = GetStringProp(args, "input_directory");
                    string? transferId = GetStringProp(args, "transfer_id");
                    if (string.IsNullOrWhiteSpace(directory) == string.IsNullOrWhiteSpace(transferId))
                        throw new ArgumentException("Provide exactly one of input_directory or transfer_id.");
                    if (transferId != null)
                    {
                        HmiJsonTransferStore.EnsureUploadsComplete(transferId);
                        directory = HmiJsonTransferStore.GetDirectory(transferId, "import");
                    }
                    string importDirectory = directory ?? throw new InvalidOperationException("The import directory could not be resolved.");
                    bool pruneItems = GetBoolProp(args, "prune_items", false);
                    bool dryRun = GetBoolProp(args, "dry_run", false);
                    bool verifyHashes = GetBoolProp(args, "verify_hashes", true);
                    string? target = GetStringProp(args, "target_name");
                    return CallToolResult.Json(TiaManager.Instance.ImportHmiJson(importDirectory, pruneItems, dryRun, target, verifyHashes));
                });

            Register("hmi_patch_json", "Validate or apply declarative Unified HMI JSON set/bind/delete operations with require and verify guards; changes are transactional, compiled, and never saved. " +
                "bind only changes the tag of a tag dynamization and never the mapping table; omitted readOnly/useIndirectAddressing keep their current values; expectTag guards the current tag. " +
                "require.screens[].items must exist before any change. Unknown fields are ignored and reported in warnings. Format: hmi-json skill, section Guarded patch workflow.",
                schema =>
                {
                    schema.Properties["file_path"] = new ToolProperty { Type = "string", Description = "Path to a tia-mcp-hmi-patch/1 JSON patch file: {format, require?, verify?, operations: [{op: 'set'|'bind'|'delete', screen, group?, item, ...}]}." };
                    schema.Properties["dry_run"] = new ToolProperty { Type = "boolean", Description = "Validate preconditions and report operations without applying them (default: false)." };
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = "Target HMI software/device selector (optional when only one target exists)." };
                    schema.Required = new List<string> { "file_path" };
                },
                args =>
                {
                    string filePath = GetStringProp(args, "file_path") ?? throw new ArgumentException("file_path is required.");
                    bool dryRun = GetBoolProp(args, "dry_run", false);
                    string? target = GetStringProp(args, "target_name");
                    return CallToolResult.Json(TiaManager.Instance.ApplyHmiJsonPatch(filePath, dryRun, target));
                });

            // 27. hmi_delete_screen_items
            Register("hmi_delete_screen_items", "Delete specific screen items by name list or by name prefix (e.g. 'btn_', 'lbl_').",
                schema =>
                {
                    schema.Properties["screen_name"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Name of the screen containing the items."
                    };
                    schema.Properties["item_names"] = new ToolProperty
                    {
                        Type = "array",
                        Items = new ToolProperty { Type = "string" },
                        Description = "List of item names to delete."
                    };
                    schema.Properties["prefix"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Prefix of item names to delete (e.g. 'M101_')."
                    };
                    schema.Properties["target_name"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Target HMI software name (optional)."
                    };
                    schema.Required = new List<string> { "screen_name" };
                },
                args =>
                {
                    string screen = GetStringProp(args, "screen_name") ?? throw new ArgumentException("screen_name is required.");
                    var items = GetStringListProp(args, "item_names");
                    string? prefix = GetStringProp(args, "prefix");
                    string? target = GetStringProp(args, "target_name");
                    int deleted = TiaManager.Instance.DeleteHmiScreenItems(screen, items, prefix, target);
                    return CallToolResult.Text($"Deleted {deleted} screen item(s) from '{screen}'.");
                });

            // 28. hmi_add_label
            Register("hmi_add_label", "Add or update a label / text box on a screen with text, position, font size, bold, alignment, and colors.",
                schema =>
                {
                    schema.Properties["screen_name"] = new ToolProperty { Type = "string", Description = "Screen name." };
                    schema.Properties["name"] = new ToolProperty { Type = "string", Description = "Item name." };
                    schema.Properties["text"] = new ToolProperty { Type = "string", Description = "Display text. Plain Unicode text (ä, ö, å, °); the server wraps and escapes it as HTML, so do not use HTML entities." };
                    schema.Properties["left"] = new ToolProperty { Type = "integer", Description = "X position." };
                    schema.Properties["top"] = new ToolProperty { Type = "integer", Description = "Y position." };
                    schema.Properties["width"] = new ToolProperty { Type = "integer", Description = "Width." };
                    schema.Properties["height"] = new ToolProperty { Type = "integer", Description = "Height." };
                    schema.Properties["font_size"] = new ToolProperty { Type = "number", Description = "Font size in pt (default: 11)." };
                    schema.Properties["is_bold"] = new ToolProperty { Type = "boolean", Description = "Bold text." };
                    schema.Properties["align"] = new ToolProperty { Type = "string", Description = "Horizontal alignment ('Left', 'Center', 'Right')." };
                    schema.Properties["fore_color"] = new ToolProperty { Type = "string", Description = "Text color ('#FFFFFF', 'white', etc.)." };
                    schema.Properties["back_color"] = new ToolProperty { Type = "string", Description = "Background color ('transparent', '#000000', etc.)." };
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = "Target HMI software name (optional)." };
                    schema.Required = new List<string> { "screen_name", "name", "text", "left", "top", "width", "height" };
                },
                args =>
                {
                    string screen = GetStringProp(args, "screen_name") ?? throw new ArgumentException("screen_name required.");
                    string name = GetStringProp(args, "name") ?? throw new ArgumentException("name required.");
                    string text = GetStringProp(args, "text") ?? throw new ArgumentException("text required.");
                    int left = GetIntProp(args, "left") ?? 0;
                    int top = GetIntProp(args, "top") ?? 0;
                    uint width = GetUIntProp(args, "width") ?? 100;
                    uint height = GetUIntProp(args, "height") ?? 30;
                    float fontSize = GetFloatProp(args, "font_size", 11f);
                    bool isBold = GetBoolProp(args, "is_bold", false);
                    string align = GetStringProp(args, "align") ?? "Center";
                    string? foreColor = GetStringProp(args, "fore_color");
                    string? backColor = GetStringProp(args, "back_color");
                    string? target = GetStringProp(args, "target_name");

                    var res = TiaManager.Instance.AddOrUpdateLabel(screen, name, text, left, top, width, height, fontSize, isBold, align, foreColor, backColor, target);
                    return CallToolResult.Text(res);
                });

            // 29. hmi_add_button
            Register("hmi_add_button", "Add or update a button on a screen with text, position, colors, pressed state tag, and OnClick/Tapped JavaScript script.",
                schema =>
                {
                    schema.Properties["screen_name"] = new ToolProperty { Type = "string", Description = "Screen name." };
                    schema.Properties["name"] = new ToolProperty { Type = "string", Description = "Button item name." };
                    schema.Properties["text"] = new ToolProperty { Type = "string", Description = "Button caption. Plain Unicode text (ä, ö, å, °); the server wraps and escapes it as HTML, so do not use HTML entities." };
                    schema.Properties["left"] = new ToolProperty { Type = "integer", Description = "X position." };
                    schema.Properties["top"] = new ToolProperty { Type = "integer", Description = "Y position." };
                    schema.Properties["width"] = new ToolProperty { Type = "integer", Description = "Width." };
                    schema.Properties["height"] = new ToolProperty { Type = "integer", Description = "Height." };
                    schema.Properties["back_color"] = new ToolProperty { Type = "string", Description = "Background color." };
                    schema.Properties["fore_color"] = new ToolProperty { Type = "string", Description = "Text color." };
                    schema.Properties["pressed_tag"] = new ToolProperty { Type = "string", Description = "HMI tag to bind to button pressed state (optional)." };
                    schema.Properties["tapped_script"] = new ToolProperty { Type = "string", Description = "JavaScript code to execute on button tap (e.g. Tags.SysFct.InvertBitInTag(\"tag\", 0);)." };
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = "Target HMI software name (optional)." };
                    schema.Required = new List<string> { "screen_name", "name", "text", "left", "top", "width", "height" };
                },
                args =>
                {
                    string screen = GetStringProp(args, "screen_name") ?? throw new ArgumentException("screen_name required.");
                    string name = GetStringProp(args, "name") ?? throw new ArgumentException("name required.");
                    string text = GetStringProp(args, "text") ?? throw new ArgumentException("text required.");
                    int left = GetIntProp(args, "left") ?? 0;
                    int top = GetIntProp(args, "top") ?? 0;
                    uint width = GetUIntProp(args, "width") ?? 100;
                    uint height = GetUIntProp(args, "height") ?? 30;
                    string? backColor = GetStringProp(args, "back_color");
                    string? foreColor = GetStringProp(args, "fore_color");
                    string? pressedTag = GetStringProp(args, "pressed_tag");
                    string? tappedScript = GetStringProp(args, "tapped_script");
                    string? target = GetStringProp(args, "target_name");

                    var res = TiaManager.Instance.AddOrUpdateButton(screen, name, text, left, top, width, height, backColor, foreColor, pressedTag, tappedScript, target);
                    return CallToolResult.Json(res);
                });

            // hmi_add_toggle_switch
            Register("hmi_add_toggle_switch", "Add or update a tag-bound toggle switch with separate off/on captions.",
                schema =>
                {
                    schema.Properties["screen_name"] = new ToolProperty { Type = "string", Description = "Screen name." };
                    schema.Properties["name"] = new ToolProperty { Type = "string", Description = "Toggle switch item name." };
                    schema.Properties["off_text"] = new ToolProperty { Type = "string", Description = "Caption shown in the normal state. Plain Unicode text (ä, ö, å, °); the server wraps and escapes it as HTML, so do not use HTML entities." };
                    schema.Properties["on_text"] = new ToolProperty { Type = "string", Description = "Caption shown in the alternate state. Plain Unicode text (ä, ö, å, °); the server wraps and escapes it as HTML, so do not use HTML entities." };
                    schema.Properties["left"] = new ToolProperty { Type = "integer", Description = "X position." };
                    schema.Properties["top"] = new ToolProperty { Type = "integer", Description = "Y position." };
                    schema.Properties["width"] = new ToolProperty { Type = "integer", Description = "Width." };
                    schema.Properties["height"] = new ToolProperty { Type = "integer", Description = "Height." };
                    schema.Properties["process_tag"] = new ToolProperty { Type = "string", Description = "HMI tag bound to IsAlternateState." };
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = "Target HMI software name (optional)." };
                    schema.Required = new List<string> { "screen_name", "name", "off_text", "on_text", "left", "top", "width", "height", "process_tag" };
                },
                args =>
                {
                    string screen = GetStringProp(args, "screen_name") ?? throw new ArgumentException("screen_name required.");
                    string name = GetStringProp(args, "name") ?? throw new ArgumentException("name required.");
                    string offText = GetStringProp(args, "off_text") ?? throw new ArgumentException("off_text required.");
                    string onText = GetStringProp(args, "on_text") ?? throw new ArgumentException("on_text required.");
                    int left = GetIntProp(args, "left") ?? throw new ArgumentException("left required.");
                    int top = GetIntProp(args, "top") ?? throw new ArgumentException("top required.");
                    uint width = GetUIntProp(args, "width") ?? throw new ArgumentException("width required.");
                    uint height = GetUIntProp(args, "height") ?? throw new ArgumentException("height required.");
                    string processTag = GetStringProp(args, "process_tag") ?? throw new ArgumentException("process_tag required.");
                    string? target = GetStringProp(args, "target_name");

                    var result = TiaManager.Instance.AddOrUpdateToggleSwitch(
                        screen, name, offText, onText, left, top, width, height, processTag, target);
                    return CallToolResult.Text(result);
                });

            // 30. hmi_add_shape
            Register("hmi_add_shape", "Add or update a shape ('Rectangle' or 'Circle') with dimensions, center/radius, fill and border colors.",
                schema =>
                {
                    schema.Properties["screen_name"] = new ToolProperty { Type = "string", Description = "Screen name." };
                    schema.Properties["name"] = new ToolProperty { Type = "string", Description = "Shape item name." };
                    schema.Properties["shape_type"] = new ToolProperty { Type = "string", Description = "'Rectangle' or 'Circle'." };
                    schema.Properties["left"] = new ToolProperty { Type = "integer", Description = "X position (for Rectangle)." };
                    schema.Properties["top"] = new ToolProperty { Type = "integer", Description = "Y position (for Rectangle)." };
                    schema.Properties["width"] = new ToolProperty { Type = "integer", Description = "Width (for Rectangle)." };
                    schema.Properties["height"] = new ToolProperty { Type = "integer", Description = "Height (for Rectangle)." };
                    schema.Properties["center_x"] = new ToolProperty { Type = "integer", Description = "Center X (for Circle)." };
                    schema.Properties["center_y"] = new ToolProperty { Type = "integer", Description = "Center Y (for Circle)." };
                    schema.Properties["radius"] = new ToolProperty { Type = "integer", Description = "Radius (for Circle)." };
                    schema.Properties["back_color"] = new ToolProperty { Type = "string", Description = "Fill / background color." };
                    schema.Properties["border_color"] = new ToolProperty { Type = "string", Description = "Border color." };
                    schema.Properties["border_width"] = new ToolProperty { Type = "integer", Description = "Border width in px (default: 1)." };
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = "Target HMI software name (optional)." };
                    schema.Required = new List<string> { "screen_name", "name", "shape_type" };
                },
                args =>
                {
                    string screen = GetStringProp(args, "screen_name") ?? throw new ArgumentException("screen_name required.");
                    string name = GetStringProp(args, "name") ?? throw new ArgumentException("name required.");
                    string shapeType = GetStringProp(args, "shape_type") ?? throw new ArgumentException("shape_type required.");
                    int? left = GetIntProp(args, "left");
                    int? top = GetIntProp(args, "top");
                    uint? width = GetUIntProp(args, "width");
                    uint? height = GetUIntProp(args, "height");
                    int? centerX = GetIntProp(args, "center_x");
                    int? centerY = GetIntProp(args, "center_y");
                    uint? radius = GetUIntProp(args, "radius");
                    string? backColor = GetStringProp(args, "back_color");
                    string? borderColor = GetStringProp(args, "border_color");
                    uint borderWidth = GetUIntProp(args, "border_width") ?? 1;
                    string? target = GetStringProp(args, "target_name");

                    var res = TiaManager.Instance.AddOrUpdateShape(screen, name, shapeType, left, top, width, height, centerX, centerY, radius, backColor, borderColor, borderWidth, target);
                    return CallToolResult.Text(res);
                });

            // 31. hmi_add_io_field
            Register("hmi_add_io_field", "Add or update an IO field on a screen bound to a process value tag.",
                schema =>
                {
                    schema.Properties["screen_name"] = new ToolProperty { Type = "string", Description = "Screen name." };
                    schema.Properties["name"] = new ToolProperty { Type = "string", Description = "IO Field item name." };
                    schema.Properties["left"] = new ToolProperty { Type = "integer", Description = "X position." };
                    schema.Properties["top"] = new ToolProperty { Type = "integer", Description = "Y position." };
                    schema.Properties["width"] = new ToolProperty { Type = "integer", Description = "Width." };
                    schema.Properties["height"] = new ToolProperty { Type = "integer", Description = "Height." };
                    schema.Properties["mode"] = new ToolProperty { Type = "string", Description = "'Output' or 'InputOutput' (default: Output)." };
                    schema.Properties["process_tag"] = new ToolProperty { Type = "string", Description = "HMI tag to bind to ProcessValue." };
                    schema.Properties["output_format"] = new ToolProperty { Type = "string", Description = "Output format, e.g. '{F1} °C' for one decimal and a unit (optional; omit to keep the current format). Plain Unicode text, not HTML; do not use HTML entities (numeric references such as &#176; are decoded)." };
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = "Target HMI software name (optional)." };
                    schema.Required = new List<string> { "screen_name", "name", "left", "top", "width", "height" };
                },
                args =>
                {
                    string screen = GetStringProp(args, "screen_name") ?? throw new ArgumentException("screen_name required.");
                    string name = GetStringProp(args, "name") ?? throw new ArgumentException("name required.");
                    int left = GetIntProp(args, "left") ?? 0;
                    int top = GetIntProp(args, "top") ?? 0;
                    uint width = GetUIntProp(args, "width") ?? 80;
                    uint height = GetUIntProp(args, "height") ?? 24;
                    string mode = GetStringProp(args, "mode") ?? "Output";
                    string? processTag = GetStringProp(args, "process_tag");
                    string? target = GetStringProp(args, "target_name");
                    string? outputFormat = GetStringProp(args, "output_format");

                    var res = TiaManager.Instance.AddOrUpdateIOField(screen, name, left, top, width, height, mode, processTag, target, outputFormat);
                    return CallToolResult.Text(res);
                });

            // 32. hmi_set_tag_dynamization
            Register("hmi_set_tag_dynamization", "Set tag dynamization on a screen item property using numeric ranges or Boolean Singlebit mapping. " +
                "An existing Singlebit mapping is updated in place. Switching an existing mapping table with entries to another condition type, or replacing a non-tag dynamization, fails: call hmi_delete_dynamization first. " +
                "'None' only sets the tag and leaves an existing mapping table unchanged. All changes run in one transaction and are rolled back on failure.",
                schema =>
                {
                    schema.Properties["screen_name"] = new ToolProperty { Type = "string", Description = "Screen name." };
                    schema.Properties["item_name"] = new ToolProperty { Type = "string", Description = "Item name." };
                    schema.Properties["property_name"] = new ToolProperty { Type = "string", Description = "Property to dynamize (e.g. 'BackColor', 'Visibility')." };
                    schema.Properties["tag_name"] = new ToolProperty { Type = "string", Description = "HMI tag name to evaluate." };
                    schema.Properties["condition_type"] = new ToolProperty { Type = "string", Description = "'None', 'Range', or 'Singlebit'." };
                    schema.Properties["ranges"] = new ToolProperty
                    {
                        Type = "array",
                        Description = "For Range, list numeric from/to bounds. For Singlebit, provide exactly two entries with matching bounds 0/0 and 1/1; their values map the off/on states.",
                        Items = new ToolProperty
                        {
                            Type = "object",
                            Properties = new Dictionary<string, ToolProperty>
                            {
                                ["from"] = new ToolProperty { Type = "number" },
                                ["to"] = new ToolProperty { Type = "number" },
                                ["value"] = new ToolProperty { Type = "string" }
                            },
                            Required = new List<string> { "from", "to", "value" }
                        }
                    };
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = "Target HMI software name (optional)." };
                    schema.Required = new List<string> { "screen_name", "item_name", "property_name", "tag_name" };
                },
                args =>
                {
                    string screen = GetStringProp(args, "screen_name") ?? throw new ArgumentException("screen_name required.");
                    string item = GetStringProp(args, "item_name") ?? throw new ArgumentException("item_name required.");
                    string prop = GetStringProp(args, "property_name") ?? throw new ArgumentException("property_name required.");
                    string tag = GetStringProp(args, "tag_name") ?? throw new ArgumentException("tag_name required.");
                    string condition = GetStringProp(args, "condition_type") ?? "None";
                    var ranges = GetRangesProp(args, "ranges");
                    string? target = GetStringProp(args, "target_name");

                    var res = TiaManager.Instance.SetTagDynamization(screen, item, prop, tag, condition, ranges, target);
                    return CallToolResult.Text(res);
                });

            Register("hmi_delete_dynamization", "Delete one dynamization from a screen item property in place, preserving the item and its other dynamizations.",
                schema =>
                {
                    schema.Properties["screen_name"] = new ToolProperty { Type = "string", Description = "Target screen." };
                    schema.Properties["item_name"] = new ToolProperty { Type = "string", Description = "Target screen item name." };
                    schema.Properties["property_name"] = new ToolProperty { Type = "string", Description = "Property whose dynamization to delete, such as BackColor." };
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = "Target HMI software name (optional)." };
                    schema.Required = new List<string> { "screen_name", "item_name", "property_name" };
                },
                args =>
                {
                    string screen = GetStringProp(args, "screen_name") ?? throw new ArgumentException("screen_name is required.");
                    string item = GetStringProp(args, "item_name") ?? throw new ArgumentException("item_name is required.");
                    string property = GetStringProp(args, "property_name") ?? throw new ArgumentException("property_name is required.");
                    string? target = GetStringProp(args, "target_name");
                    return CallToolResult.Json(_hmiToolOperations.Value.DeleteHmiDynamization(
                        screen, item, property, target));
                });

            // Update one existing converter entry without recreating the dynamization.
            Register("hmi_update_dynamization_entry", "Update the value, alternate value, or flashing settings of one existing tag or expression dynamization mapping entry.",
                schema =>
                {
                    schema.Properties["screen_name"] = new ToolProperty { Type = "string", Description = "Target screen." };
                    schema.Properties["item_name"] = new ToolProperty { Type = "string", Description = "Target screen item name." };
                    schema.Properties["property_name"] = new ToolProperty { Type = "string", Description = "Dynamized property, such as BackColor." };
                    schema.Properties["entry_index"] = new ToolProperty { Type = "integer", Description = "Zero-based index in the mapping table entries." };
                    var mappingValueProperties = new Dictionary<string, ToolProperty>
                    {
                        ["type"] = new ToolProperty { Type = "string", Description = "CLR mapping value type (for example Color, String, Boolean, or Double)." },
                        ["value"] = new ToolProperty { Type = "string", Description = "Value encoded as text; colors use #RRGGBB or #AARRGGBB." }
                    };
                    var mappingValueRequired = new List<string> { "type", "value" };
                    schema.Properties["new_value"] = new ToolProperty
                    {
                        Type = "object",
                        Description = "Optional typed replacement for the entry's primary value.",
                        Properties = mappingValueProperties,
                        Required = mappingValueRequired
                    };
                    schema.Properties["new_alternate_value"] = new ToolProperty
                    {
                        Type = "object",
                        Description = "Optional typed replacement for the entry's alternate value.",
                        Properties = mappingValueProperties,
                        Required = mappingValueRequired
                    };
                    schema.Properties["new_flashing"] = new ToolProperty { Type = "boolean", Description = "Optional flashing enable state." };
                    schema.Properties["new_flashing_rate"] = new ToolProperty { Type = "string", Description = "Optional TIA flashing rate enum name." };
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = "Target HMI software name (optional)." };
                    schema.Required = new List<string> { "screen_name", "item_name", "property_name", "entry_index" };
                },
                args =>
                {
                    string screen = GetStringProp(args, "screen_name") ?? throw new ArgumentException("screen_name required.");
                    string item = GetStringProp(args, "item_name") ?? throw new ArgumentException("item_name required.");
                    string property = GetStringProp(args, "property_name") ?? throw new ArgumentException("property_name required.");
                    int entryIndex = GetIntProp(args, "entry_index") ?? throw new ArgumentException("entry_index must be an integer.");
                    var newValue = GetMappingValueInputProp(args, "new_value");
                    var newAlternateValue = GetMappingValueInputProp(args, "new_alternate_value");
                    bool? newFlashing = GetNullableBoolProp(args, "new_flashing");
                    string? newFlashingRate = GetStringProp(args, "new_flashing_rate");
                    string? target = GetStringProp(args, "target_name");

                    string result = TiaManager.Instance.UpdateDynamizationEntry(
                        screen, item, property, entryIndex, newValue, newAlternateValue,
                        newFlashing, newFlashingRate, target);
                    return CallToolResult.Text(result);
                });

            Register("hmi_set_expression_dynamization", "Create or update one expression dynamization formula in place, preserving its mapping entries and all other item dynamizations.",
                schema =>
                {
                    schema.Properties["screen_name"] = new ToolProperty { Type = "string", Description = "Target screen." };
                    schema.Properties["item_name"] = new ToolProperty { Type = "string", Description = "Target screen item." };
                    schema.Properties["property_name"] = new ToolProperty { Type = "string", Description = "Item property to dynamize, such as Visible or Top." };
                    schema.Properties["expression"] = new ToolProperty { Type = "string", Description = "Non-empty WinCC Unified expression formula." };
                    schema.Properties["result_type"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Declared result type; must match the target property's CLR type. Formula semantics are not statically inferred.",
                        Enum = new List<string> { "Boolean", "Color", "String", "Byte", "Int16", "UInt16", "Int32", "UInt32", "Int64", "UInt64", "Single", "Double", "Decimal" }
                    };
                    schema.Properties["on_conflict"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Whether an existing tag dynamization on this same property may be replaced (default: fail).",
                        Enum = new List<string> { "fail", "replace" },
                        Default = "fail"
                    };
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = "Target HMI software name (optional)." };
                    schema.Required = new List<string> { "screen_name", "item_name", "property_name", "expression", "result_type" };
                },
                args =>
                {
                    string screen = GetStringProp(args, "screen_name") ?? throw new ArgumentException("screen_name is required.");
                    string item = GetStringProp(args, "item_name") ?? throw new ArgumentException("item_name is required.");
                    string property = GetStringProp(args, "property_name") ?? throw new ArgumentException("property_name is required.");
                    string expression = GetStringProp(args, "expression") ?? throw new ArgumentException("expression is required.");
                    string resultType = GetStringProp(args, "result_type") ?? throw new ArgumentException("result_type is required.");
                    string onConflict = GetStringProp(args, "on_conflict") ?? "fail";
                    string? target = GetStringProp(args, "target_name");
                    return CallToolResult.Json(_hmiToolOperations.Value.SetExpressionDynamization(
                        screen, item, property, expression, resultType, onConflict, target));
                });

            Register("hmi_set_expression_dynamization_entries", "Replace the ordered condition/value rows of one expression dynamization in place and verify the read-back.",
                schema =>
                {
                    schema.Properties["screen_name"] = new ToolProperty { Type = "string", Description = "Target screen." };
                    schema.Properties["item_name"] = new ToolProperty { Type = "string", Description = "Target screen item." };
                    schema.Properties["property_name"] = new ToolProperty { Type = "string", Description = "Property whose Expressions-tab condition rows to set, such as Top or Visible." };
                    schema.Properties["entries"] = new ToolProperty
                    {
                        Type = "array",
                        Description = "Non-empty, ordered list of Boolean condition expressions and mapped property values. Replaces all rows for this property.",
                        Items = new ToolProperty
                        {
                            Type = "object",
                            Properties = new Dictionary<string, ToolProperty>
                            {
                                ["condition"] = new ToolProperty { Type = "string", Description = "WinCC Unified condition expression; its syntax is validated by TIA, not this tool." },
                                ["value"] = new ToolProperty
                                {
                                    Type = "object",
                                    Properties = new Dictionary<string, ToolProperty>
                                    {
                                        ["type"] = new ToolProperty
                                        {
                                            Type = "string",
                                            Description = "Typed mapped value; must match the target property's CLR type.",
                                            Enum = new List<string> { "Boolean", "Color", "String", "Byte", "Int16", "UInt16", "Int32", "UInt32", "Int64", "UInt64", "Single", "Double", "Decimal" }
                                        },
                                        ["value"] = new ToolProperty { Type = "string", Description = "Mapped value encoded as text (for example \"500\", \"true\", or \"#00FF00\")." }
                                    },
                                    Required = new List<string> { "type", "value" },
                                    AdditionalProperties = false
                                }
                            },
                            Required = new List<string> { "condition", "value" },
                            AdditionalProperties = false
                        }
                    };
                    schema.Properties["on_conflict"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Whether an existing dynamization on this property may be replaced (default: fail).",
                        Enum = new List<string> { "fail", "replace" },
                        Default = "fail"
                    };
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = "Target HMI software name (optional)." };
                    schema.Required = new List<string> { "screen_name", "item_name", "property_name", "entries" };
                },
                args =>
                {
                    string screen = GetStringProp(args, "screen_name") ?? throw new ArgumentException("screen_name is required.");
                    string item = GetStringProp(args, "item_name") ?? throw new ArgumentException("item_name is required.");
                    string property = GetStringProp(args, "property_name") ?? throw new ArgumentException("property_name is required.");
                    if (!args.HasValue || args.Value.ValueKind != JsonValueKind.Object ||
                        !args.Value.TryGetProperty("entries", out JsonElement entries) ||
                        entries.ValueKind != JsonValueKind.Array)
                        throw new ArgumentException("entries must be an array.");
                    string onConflict = GetStringProp(args, "on_conflict") ?? "fail";
                    string? target = GetStringProp(args, "target_name");
                    return CallToolResult.Json(_hmiToolOperations.Value.SetExpressionDynamizationEntries(
                        screen, item, property, entries.Clone(), onConflict, target));
                });

            // 33. hmi_update_button_scripts
            Register("hmi_update_button_scripts", "Update button scripts in place on a screen matching text search/replace or list of button names without deleting items.",
                schema =>
                {
                    schema.Properties["screen_name"] = new ToolProperty { Type = "string", Description = "Screen name." };
                    schema.Properties["find_text"] = new ToolProperty { Type = "string", Description = "Substring to find in button scripts (e.g. 'SetBitInTag')." };
                    schema.Properties["replace_text"] = new ToolProperty { Type = "string", Description = "Replacement string (e.g. 'InvertBitInTag')." };
                    schema.Properties["new_script"] = new ToolProperty { Type = "string", Description = "Exact new script code to set unconditionally (optional)." };
                    schema.Properties["button_names"] = new ToolProperty { Type = "array", Items = new ToolProperty { Type = "string" }, Description = "Filter by specific button names (optional)." };
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = "Target HMI software name (optional)." };
                    schema.Required = new List<string> { "screen_name" };
                },
                args =>
                {
                    string screen = GetStringProp(args, "screen_name") ?? throw new ArgumentException("screen_name required.");
                    string? find = GetStringProp(args, "find_text");
                    string? rep = GetStringProp(args, "replace_text");
                    string? newScript = GetStringProp(args, "new_script");
                    var btnNames = GetStringListProp(args, "button_names");
                    string? target = GetStringProp(args, "target_name");

                    var res = TiaManager.Instance.UpdateHmiButtonScripts(screen, find, rep, newScript, btnNames, target);
                    return CallToolResult.Json(res);
                });

            // 34. hmi_list_tag_tables
            Register("hmi_list_tag_tables", "List all HMI tag tables in the HMI software target.",
                schema =>
                {
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = "Target HMI software name (optional)." };
                },
                args =>
                {
                    string? target = GetStringProp(args, "target_name");
                    return CallToolResult.Json(TiaManager.Instance.ListHmiTagTables(target));
                });

            // 35. hmi_list_tags
            Register("hmi_list_tags", "List HMI tags with their table, connection, PLC tag binding, and data type.",
                schema =>
                {
                    schema.Properties["table_name"] = new ToolProperty { Type = "string", Description = "Tag table name (optional, all tables if omitted)." };
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = "Target HMI software name (optional)." };
                },
                args =>
                {
                    string? table = GetStringProp(args, "table_name");
                    string? target = GetStringProp(args, "target_name");
                    return CallToolResult.Json(TiaManager.Instance.ListHmiTags(table, target));
                });

            // 36. hmi_create_tag
            Register("hmi_create_tag", "Create or update an HMI tag linked to a PLC tag with connection name and table.",
                schema =>
                {
                    schema.Properties["tag_name"] = new ToolProperty { Type = "string", Description = "HMI Tag name." };
                    schema.Properties["plc_tag"] = new ToolProperty { Type = "string", Description = "PLC tag path (e.g. 'DB_MixingLine.Line.MotorInfeed.RunFb')." };
                    schema.Properties["connection"] = new ToolProperty { Type = "string", Description = "HMI connection name (default: 'HMI_Connection_1')." };
                    schema.Properties["table_name"] = new ToolProperty { Type = "string", Description = "Tag table name (default: 'Default tag table')." };
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = "Target HMI software name (optional)." };
                    schema.Required = new List<string> { "tag_name", "plc_tag" };
                },
                args =>
                {
                    string tag = GetStringProp(args, "tag_name") ?? throw new ArgumentException("tag_name required.");
                    string plcTag = GetStringProp(args, "plc_tag") ?? throw new ArgumentException("plc_tag required.");
                    string conn = GetStringProp(args, "connection") ?? "HMI_Connection_1";
                    string? table = GetStringProp(args, "table_name");
                    string? target = GetStringProp(args, "target_name");

                    var res = TiaManager.Instance.CreateOrUpdateHmiTag(tag, plcTag, conn, table, target);
                    return CallToolResult.Text(res);
                });

            // 37. hmi_create_tag_table
            Register("hmi_create_tag_table", "Create an HMI tag table without overwriting an existing table.",
                schema =>
                {
                    schema.Properties["table_name"] = new ToolProperty { Type = "string", Description = "Name for the new HMI tag table; '/' and '\\' are not allowed." };
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = "Exact HMI software name (optional when one target resolves uniquely)." };
                    schema.Properties["on_conflict"] = new ToolProperty { Type = "string", Enum = new List<string> { "fail" }, Description = "Required conflict behavior; only 'fail' is supported." };
                    schema.Required = new List<string> { "table_name", "on_conflict" };
                },
                args =>
                {
                    string tableName = GetStringProp(args, "table_name") ?? throw new ArgumentException("table_name is required.");
                    string onConflict = GetStringProp(args, "on_conflict") ?? throw new ArgumentException("on_conflict is required.");
                    var result = TiaManager.Instance.CreateHmiTagTable(
                        GetStringProp(args, "target_name") ?? TiaManager.Instance.FindHmiSoftware().Name,
                        tableName, onConflict);
                    return CallToolResult.Json(result, isError: !result.Success);
                });

            // hmi_create_discrete_alarm
            Register("hmi_create_discrete_alarm", "Create or update a WinCC Unified discrete alarm with an alarm class, HMI tag trigger, and multilingual event text.",
                schema =>
                {
                    schema.Properties["alarm_name"] = new ToolProperty { Type = "string", Description = "Discrete alarm name." };
                    schema.Properties["alarm_class"] = new ToolProperty { Type = "string", Description = "Existing HMI alarm class name." };
                    schema.Properties["raised_state_tag"] = new ToolProperty { Type = "string", Description = "Existing HMI tag that raises the alarm." };
                    schema.Properties["raised_state_tag_bit_number"] = new ToolProperty { Type = "integer", Description = "Bit number in the trigger tag (default: 0)." };
                    schema.Properties["trigger_mode"] = new ToolProperty { Type = "string", Description = "Discrete alarm trigger mode (default: OnRisingEdge)." };
                    schema.Properties["text"] = new ToolProperty { Type = "string", Description = "Alarm event text. Plain Unicode text (ä, ö, å, °); the server wraps and escapes it as HTML, so do not use HTML entities." };
                    schema.Properties["acknowledgment_control_tag"] = new ToolProperty { Type = "string", Description = "Optional existing HMI tag used to control acknowledgment." };
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = "Target HMI software name (optional)." };
                    schema.Required = new List<string> { "alarm_name", "alarm_class", "raised_state_tag", "text" };
                },
                args =>
                {
                    string alarmName = GetStringProp(args, "alarm_name") ?? throw new ArgumentException("alarm_name required.");
                    string alarmClass = GetStringProp(args, "alarm_class") ?? throw new ArgumentException("alarm_class required.");
                    string raisedStateTag = GetStringProp(args, "raised_state_tag") ?? throw new ArgumentException("raised_state_tag required.");
                    string text = GetStringProp(args, "text") ?? throw new ArgumentException("text required.");
                    int? requestedBit = GetIntProp(args, "raised_state_tag_bit_number");
                    if (requestedBit.HasValue && requestedBit.Value < 0)
                        throw new ArgumentOutOfRangeException("raised_state_tag_bit_number", "Alarm trigger bit must be non-negative.");
                    uint bit = (uint)(requestedBit ?? 0);
                    string triggerMode = GetStringProp(args, "trigger_mode") ?? "OnRisingEdge";
                    string? acknowledgmentTag = GetStringProp(args, "acknowledgment_control_tag");
                    string? target = GetStringProp(args, "target_name");

                    var result = TiaManager.Instance.CreateOrUpdateHmiDiscreteAlarm(
                        alarmName, alarmClass, raisedStateTag, text, bit, triggerMode, acknowledgmentTag, target);
                    return CallToolResult.Text(result);
                });

            // 37. hmi_update_tag_acquisition_cycles
            Register("hmi_update_tag_acquisition_cycles", "Update acquisition cycle (e.g. 'T100ms', 'T1s') for HMI tags.",
                schema =>
                {
                    schema.Properties["cycle"] = new ToolProperty { Type = "string", Description = "Target acquisition cycle name or interval (e.g. 'T100ms', 'T1s')." };
                    schema.Properties["plc_only"] = new ToolProperty { Type = "boolean", Description = "Only update PLC-connected tags (default: true)." };
                    schema.Properties["table_name"] = new ToolProperty { Type = "string", Description = "Tag table name (optional, all tables if omitted)." };
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = "Target HMI software name (optional)." };
                    schema.Required = new List<string> { "cycle" };
                },
                args =>
                {
                    string cycle = GetStringProp(args, "cycle") ?? throw new ArgumentException("cycle required.");
                    bool plcOnly = GetBoolProp(args, "plc_only", true);
                    string? table = GetStringProp(args, "table_name");
                    string? target = GetStringProp(args, "target_name");

                    var res = TiaManager.Instance.UpdateHmiTagAcquisitionCycles(cycle, plcOnly, table, target);
                    return CallToolResult.Json(res);
                });

            // 38. hmi_set_tag_range
            Register("hmi_set_tag_range", "Configure minimum and maximum range limits (constant values) on an HMI tag.",
                schema =>
                {
                    schema.Properties["tag_name"] = new ToolProperty { Type = "string", Description = "HMI tag name." };
                    schema.Properties["min_value"] = new ToolProperty { Type = "number", Description = "Minimum limit value (optional)." };
                    schema.Properties["max_value"] = new ToolProperty { Type = "number", Description = "Maximum limit value (optional)." };
                    schema.Properties["table_name"] = new ToolProperty { Type = "string", Description = "Tag table name (optional)." };
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = "Target HMI software name (optional)." };
                    schema.Required = new List<string> { "tag_name" };
                },
                args =>
                {
                    string tag = GetStringProp(args, "tag_name") ?? throw new ArgumentException("tag_name required.");
                    double? min = GetDoubleProp(args, "min_value");
                    double? max = GetDoubleProp(args, "max_value");
                    string? table = GetStringProp(args, "table_name");
                    string? target = GetStringProp(args, "target_name");

                    var res = TiaManager.Instance.SetHmiTagRange(tag, min, max, table, target);
                    return CallToolResult.Text(res);
                });

            // 39. hmi_create_data_log
            Register("hmi_create_data_log", "Create a new DataLog in WinCC Unified HMI software.",
                schema =>
                {
                    schema.Properties["log_name"] = new ToolProperty { Type = "string", Description = "Name of the DataLog to create." };
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = "Target HMI software name (optional)." };
                    schema.Required = new List<string> { "log_name" };
                },
                args =>
                {
                    string logName = GetStringProp(args, "log_name") ?? throw new ArgumentException("log_name required.");
                    string? target = GetStringProp(args, "target_name");

                    var res = TiaManager.Instance.CreateDataLog(logName, target);
                    return CallToolResult.Text(res);
                });

            // 40. hmi_assign_logging_tags
            Register("hmi_assign_logging_tags", "Assign multiple HMI tags to a DataLog with logging mode and cycle.",
                schema =>
                {
                    schema.Properties["log_name"] = new ToolProperty { Type = "string", Description = "Target DataLog name." };
                    schema.Properties["tag_names"] = new ToolProperty { Type = "array", Items = new ToolProperty { Type = "string" }, Description = "List of HMI tag names to log." };
                    schema.Properties["cycle"] = new ToolProperty { Type = "string", Description = "Logging cycle (e.g. 'T1s', 'T500ms', default: 'T1s')." };
                    schema.Properties["mode"] = new ToolProperty { Type = "string", Description = "Logging mode ('Cyclic', 'OnChange', 'OnDemand', default: 'Cyclic')." };
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = "Target HMI software name (optional)." };
                    schema.Required = new List<string> { "log_name", "tag_names" };
                },
                args =>
                {
                    string logName = GetStringProp(args, "log_name") ?? throw new ArgumentException("log_name required.");
                    var tags = GetStringListProp(args, "tag_names") ?? throw new ArgumentException("tag_names array required.");
                    string cycle = GetStringProp(args, "cycle") ?? "T1s";
                    string mode = GetStringProp(args, "mode") ?? "Cyclic";
                    string? target = GetStringProp(args, "target_name");

                    var res = TiaManager.Instance.AssignTagsToDataLog(logName, tags, cycle, mode, target);
                    return CallToolResult.Json(res);
                });

            // 41. hmi_compile
            Register("hmi_compile", "Compile a WinCC Unified HMI using ICompilable. The HMI is selected like in the other HMI tools: by exact software, device item or device name, or automatically when the project has only one HMI; several HMIs without target_name fail and list them. Returns state, errors, warnings, messages, and the compiled target and object.",
                schema =>
                {
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = "Exact HMI software, device item, or device name, e.g. 'HMI_RT_1' or 'HMI_1'; optional when the project has exactly one HMI." };
                    schema.Properties["target_device"] = new ToolProperty { Type = "string", Description = "Deprecated alias of target_name, kept for existing clients. If both are given they must match." };
                },
                args =>
                {
                    string? targetName = GetStringProp(args, "target_name");
                    string? targetDevice = GetStringProp(args, "target_device");
                    if (!string.IsNullOrWhiteSpace(targetName) && !string.IsNullOrWhiteSpace(targetDevice) &&
                        !string.Equals(targetName, targetDevice, StringComparison.OrdinalIgnoreCase))
                    {
                        return CallToolResult.Error(
                            $"target_name '{targetName}' and target_device '{targetDevice}' differ; pass only target_name.");
                    }
                    string? target = string.IsNullOrWhiteSpace(targetName) ? targetDevice : targetName;
                    return CallToolResult.Json(_hmiToolOperations.Value.CompileHmi(target));
                });

            // 42. tia_testsuite_list_tests
            Register("tia_testsuite_list_tests", "List TestSuite test cases configured in the current project.",
                schema => { },
                args => CallToolResult.Json(TiaManager.Instance.ListTestCases()));

            // 43. tia_testsuite_load_test
            Register("tia_testsuite_load_test", "Load/import a TestSuite .tat test file into the project with Override option.",
                schema =>
                {
                    schema.Properties["file_path"] = new ToolProperty { Type = "string", Description = "Path to the .tat file on disk." };
                    schema.Required = new List<string> { "file_path" };
                },
                args =>
                {
                    string file = GetStringProp(args, "file_path") ?? throw new ArgumentException("file_path required.");
                    return CallToolResult.Json(TiaManager.Instance.LoadTestCase(file));
                });

            // 44. hmi_list_connections
            Register("hmi_list_connections", "List all HMI connections in the WinCC Unified HMI software (name, communication driver, partner station, partner node, disabled status).",
                schema =>
                {
                    schema.Properties["device_name"] = new ToolProperty { Type = "string", Description = "Device or HMI software name (optional, defaults to first HMI target)." };
                },
                args =>
                {
                    string? target = GetStringProp(args, "device_name");
                    return CallToolResult.Json(TiaManager.Instance.ListHmiConnections(target));
                });

            // 45. hmi_create_connection
            Register("hmi_create_connection", "Create or configure an integrated HMI connection between an HMI target and a partner PLC device/CPU.",
                schema =>
                {
                    schema.Properties["connection_name"] = new ToolProperty { Type = "string", Description = "Name of the HMI connection to create (e.g. 'HMI_PLC_1_Connection')." };
                    schema.Properties["partner_plc_name"] = new ToolProperty { Type = "string", Description = "Name of the partner PLC device in the project (e.g. 'PLC_1')." };
                    schema.Properties["device_name"] = new ToolProperty { Type = "string", Description = "Target HMI device or software name (optional, defaults to first HMI target)." };
                    schema.Properties["communication_driver"] = new ToolProperty { Type = "string", Description = "Communication driver (default: 'SIMATIC S7 1200/1500').", Default = "SIMATIC S7 1200/1500" };
                    schema.Properties["disabled_at_startup"] = new ToolProperty { Type = "boolean", Description = "Whether the connection is disabled at startup (default: false).", Default = false };
                    schema.Properties["comment"] = new ToolProperty { Type = "string", Description = "Optional comment describing the connection." };
                    schema.Required = new List<string> { "connection_name", "partner_plc_name" };
                },
                args =>
                {
                    string connName = GetStringProp(args, "connection_name") ?? throw new ArgumentException("connection_name required.");
                    string plcName = GetStringProp(args, "partner_plc_name") ?? throw new ArgumentException("partner_plc_name required.");
                    string? devName = GetStringProp(args, "device_name");
                    string? driver = GetStringProp(args, "communication_driver");
                    bool disabled = GetBoolProp(args, "disabled_at_startup", false);
                    string? comment = GetStringProp(args, "comment");

                    var res = TiaManager.Instance.CreateHmiConnection(devName, connName, plcName, driver, disabled, comment);
                    return CallToolResult.Json(res);
                });

            // 46. hmi_delete_connection
            Register("hmi_delete_connection", "Delete an HMI connection from WinCC Unified software and/or hardware communication management.",
                schema =>
                {
                    schema.Properties["connection_name"] = new ToolProperty { Type = "string", Description = "Name of the HMI connection to delete (e.g. 'HMI_Connection_2')." };
                    schema.Properties["device_name"] = new ToolProperty { Type = "string", Description = "Target HMI device or software name (optional, defaults to first HMI target)." };
                    schema.Required = new List<string> { "connection_name" };
                },
                args =>
                {
                    string connName = GetStringProp(args, "connection_name") ?? throw new ArgumentException("connection_name required.");
                    string? devName = GetStringProp(args, "device_name");

                    var res = TiaManager.Instance.DeleteHmiConnection(devName, connName);
                    return CallToolResult.Json(res, isError: !res.Success);
                });

            // 47. hmi_list_text_lists
            Register("hmi_list_text_lists", "List HMI text lists on a WinCC Unified device with their entries and values.",
                schema =>
                {
                    schema.Properties["device_name"] = new ToolProperty { Type = "string", Description = "Target HMI device or software name (optional)." };
                },
                args =>
                {
                    string? devName = GetStringProp(args, "device_name");
                    var res = TiaManager.Instance.ListTextLists(devName);
                    return CallToolResult.Json(res);
                });

            // 48. hmi_create_text_list
            Register("hmi_create_text_list", "Create a new text list on a WinCC Unified device with specified entries and values/ranges.",
                schema =>
                {
                    schema.Properties["list_name"] = new ToolProperty { Type = "string", Description = "Name of the text list to create." };
                    schema.Properties["entries"] = new ToolProperty
                    {
                        Type = "array",
                        Description = "List of text entries. Each entry: { value, from_value (optional), to_value (optional), text, multilingual_texts (optional dict of lang->text) }.",
                        Items = new ToolProperty
                        {
                            Type = "object",
                            Properties = new Dictionary<string, ToolProperty>
                            {
                                ["value"] = new ToolProperty { Type = "integer" },
                                ["from_value"] = new ToolProperty { Type = "integer" },
                                ["to_value"] = new ToolProperty { Type = "integer" },
                                ["text"] = new ToolProperty { Type = "string" },
                                ["multilingual_texts"] = new ToolProperty
                                {
                                    Type = "object",
                                    AdditionalProperties = new ToolProperty { Type = "string" }
                                }
                            }
                        }
                    };
                    schema.Properties["device_name"] = new ToolProperty { Type = "string", Description = "Target HMI device or software name (optional)." };
                    schema.Required = new List<string> { "list_name", "entries" };
                },
                args =>
                {
                    string listName = GetStringProp(args, "list_name") ?? throw new ArgumentException("list_name required.");
                    var entries = GetTextListEntries(args, "entries");
                    string? devName = GetStringProp(args, "device_name");

                    var res = TiaManager.Instance.CreateTextList(listName, entries, devName);
                    return CallToolResult.Json(res, isError: !res.Success);
                });

            // 49. hmi_delete_text_list
            Register("hmi_delete_text_list", "Delete an HMI text list from a WinCC Unified device.",
                schema =>
                {
                    schema.Properties["list_name"] = new ToolProperty { Type = "string", Description = "Name of the text list to delete." };
                    schema.Properties["device_name"] = new ToolProperty { Type = "string", Description = "Target HMI device or software name (optional)." };
                    schema.Required = new List<string> { "list_name" };
                },
                args =>
                {
                    string listName = GetStringProp(args, "list_name") ?? throw new ArgumentException("list_name required.");
                    string? devName = GetStringProp(args, "device_name");

                    var res = TiaManager.Instance.DeleteTextList(listName, devName);
                    return CallToolResult.Json(res, isError: !res.Success);
                });

            // 50. hmi_export_text_lists
            Register("hmi_export_text_lists", "Export all HMI text lists to YAML (.hmi.yml) in a destination folder.",
                schema =>
                {
                    schema.Properties["destination_folder"] = new ToolProperty { Type = "string", Description = "Full path to destination folder." };
                    schema.Properties["base_filename"] = new ToolProperty { Type = "string", Description = "Base filename for exported .hmi.yml files." };
                    schema.Properties["device_name"] = new ToolProperty { Type = "string", Description = "Target HMI device or software name (optional)." };
                    schema.Required = new List<string> { "destination_folder", "base_filename" };
                },
                args =>
                {
                    string dest = GetStringProp(args, "destination_folder") ?? throw new ArgumentException("destination_folder required.");
                    string baseFile = GetStringProp(args, "base_filename") ?? throw new ArgumentException("base_filename required.");
                    string? devName = GetStringProp(args, "device_name");

                    var res = TiaManager.Instance.ExportTextLists(dest, baseFile, devName);
                    return CallToolResult.Json(res);
                });

            // ----------------------------------------------------
            // S7-PLCSIM Advanced Tools
            // ----------------------------------------------------

            // 51. plcsim_list_instances
            Register("plcsim_list_instances", "List all registered Siemens S7-PLCSIM Advanced instances with ID, name, operating state, CPU type, and IP. " +
                "S7-PLCSIM Advanced V8.0+ only (S7-1500 / ET 200SP virtual controllers); standard S7-PLCSIM (V18-V21), and therefore S7-1200 simulation, has no public API and is not supported. " +
                "Returns an object {runtimeAvailable, apiPath, apiVersion (API folder, e.g. '8.0'), apiAssemblyVersion, runtimeManagerAvailable, runtimeVersion, instances[], standardPlcSimProcesses[], message, error}; " +
                "message explains an empty list, and standardPlcSimProcesses reports a running standard S7-PLCSIM. The call succeeds also when the PLCSIM Advanced runtime is not available: check runtimeAvailable.",
                schema => { },
                args => CallToolResult.Json(PlcSimRuntimeProbe.ListInstancesSafe()));

            // 52. plcsim_connect
            Register("plcsim_connect", "Connect to a running Siemens S7-PLCSIM Advanced instance by name (default 'PLC_1'). " +
                "S7-PLCSIM Advanced V8.0+ only (S7-1500 / ET 200SP); standard S7-PLCSIM and S7-1200 simulation are not supported. A failure message explains why (missing API, runtime not running, standard S7-PLCSIM detected).",
                schema =>
                {
                    schema.Properties["instance_name"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Instance name of the virtual controller (e.g. 'PLC_1'). Default is 'PLC_1'."
                    };
                },
                args =>
                {
                    string instanceName = GetStringProp(args, "instance_name") ?? "PLC_1";
                    var res = PlcSimRuntimeProbe.ConnectSafe(instanceName);
                    return CallToolResult.Json(res, isError: !res.Success);
                });

            // 53. plcsim_disconnect
            Register("plcsim_disconnect", "Disconnect from the currently connected S7-PLCSIM Advanced instance." + PlcSimAdvancedOnlyNote,
                schema => { },
                args =>
                {
                    PlcSimManager.Instance.Disconnect();
                    return CallToolResult.Success("Disconnected from S7-PLCSIM Advanced.");
                });

            // 54. plcsim_get_status
            Register("plcsim_get_status", "Get live status, operating mode, CPU type, scale factor, and tag count of the connected S7-PLCSIM Advanced instance." + PlcSimAdvancedOnlyNote,
                schema => { },
                args => CallToolResult.Json(PlcSimManager.Instance.GetStatus()));

            // 55. plcsim_set_operating_state
            Register("plcsim_set_operating_state", "Set the operating state of the connected S7-PLCSIM Advanced instance (Run, Stop, MemoryReset)." + PlcSimAdvancedOnlyNote,
                schema =>
                {
                    schema.Properties["state"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Desired operating state ('Run', 'Stop', or 'MemoryReset')."
                    };
                    schema.Required = new List<string> { "state" };
                },
                args =>
                {
                    string state = GetStringProp(args, "state") ?? throw new ArgumentException("state required.");
                    bool ok = PlcSimManager.Instance.SetOperatingState(state);
                    return CallToolResult.Success($"Operating state set to {state}. Result: {ok}");
                });

            // 56. plcsim_set_scale_factor
            Register("plcsim_set_scale_factor", "Set the simulation time scale factor (e.g. 1.0 = real-time, 2.0 = 2x speed, 5.0 = 5x speed)." + PlcSimAdvancedOnlyNote,
                schema =>
                {
                    schema.Properties["scale_factor"] = new ToolProperty
                    {
                        Type = "number",
                        Description = "Scale factor (> 0.0, e.g. 1.0, 2.0, 5.0)."
                    };
                    schema.Required = new List<string> { "scale_factor" };
                },
                args =>
                {
                    double? factor = GetDoubleProp(args, "scale_factor");
                    if (!factor.HasValue || factor.Value <= 0)
                        throw new ArgumentException("scale_factor must be positive number.");
                    double updated = PlcSimManager.Instance.SetScaleFactor(factor.Value);
                    return CallToolResult.Success($"ScaleFactor set to {updated}");
                });

            // 57. plcsim_read_tag
            Register("plcsim_read_tag", "Read a live variable or tag value from S7-PLCSIM Advanced memory by symbolic tag name." + PlcSimAdvancedOnlyNote,
                schema =>
                {
                    schema.Properties["tag_name"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Symbolic tag name (e.g. 'DB_MixingLine.Status_StepNumber', 'DB_MixingLine.Line.MixingTank.CurrentLevel')."
                    };
                    schema.Required = new List<string> { "tag_name" };
                },
                args =>
                {
                    string tagName = GetStringProp(args, "tag_name") ?? throw new ArgumentException("tag_name required.");
                    var res = PlcSimManager.Instance.ReadTag(tagName);
                    return CallToolResult.Json(res);
                });

            // 58. plcsim_write_tag
            Register("plcsim_write_tag", "Write a live value to a variable or tag in S7-PLCSIM Advanced memory by symbolic tag name." + PlcSimAdvancedOnlyNote,
                schema =>
                {
                    schema.Properties["tag_name"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Symbolic tag name (e.g. 'DB_MixingLine.Cmd_StartBatch', 'DB_MixingLine.BatchRecipe.Water')."
                    };
                    schema.Properties["value"] = new ToolProperty
                    {
                        Description = "Value to write (boolean, integer, float, or string)."
                    };
                    schema.Required = new List<string> { "tag_name", "value" };
                },
                args =>
                {
                    string tagName = GetStringProp(args, "tag_name") ?? throw new ArgumentException("tag_name required.");
                    if (!args.HasValue || !args.Value.TryGetProperty("value", out var pVal))
                    {
                        throw new ArgumentException("value required.");
                    }
                    bool ok = PlcSimManager.Instance.WriteTag(tagName, pVal);
                    return CallToolResult.Success($"Tag '{tagName}' written successfully.");
                });

            // 59. plcsim_read_tags
            Register("plcsim_read_tags", "Read multiple live variable/tag values from S7-PLCSIM Advanced memory." + PlcSimAdvancedOnlyNote,
                schema =>
                {
                    schema.Properties["tag_names"] = new ToolProperty
                    {
                        Type = "array",
                        Items = new ToolProperty { Type = "string" },
                        Description = "List of symbolic tag names to read."
                    };
                    schema.Required = new List<string> { "tag_names" };
                },
                args =>
                {
                    var names = GetStringListProp(args, "tag_names") ?? throw new ArgumentException("tag_names required.");
                    var res = PlcSimManager.Instance.ReadTags(names);
                    return CallToolResult.Json(res);
                });

            // 60. plcsim_write_tags
            Register("plcsim_write_tags", "Write multiple live values to variables/tags in S7-PLCSIM Advanced memory." + PlcSimAdvancedOnlyNote,
                schema =>
                {
                    schema.Properties["tags"] = new ToolProperty
                    {
                        Type = "object",
                        Description = "Key-value dictionary mapping symbolic tag names to values."
                    };
                    schema.Required = new List<string> { "tags" };
                },
                args =>
                {
                    var dict = GetObjectDictProp(args, "tags") ?? throw new ArgumentException("tags object required.");
                    int count = PlcSimManager.Instance.WriteTags(dict);
                    return CallToolResult.Success($"Successfully wrote {count} tags.");
                });

            // 61. plcsim_run_mixing_sequence
            Register("plcsim_run_mixing_sequence", "Execute an automated online sequence test on the mixing line in S7-PLCSIM Advanced. Configures recipe parameters, pulses start, monitors step progression, samples telemetry, and returns a detailed execution report." + PlcSimAdvancedOnlyNote,
                schema =>
                {
                    schema.Properties["recipe_id"] = new ToolProperty { Type = "string", Description = "Recipe identifier (default 'RECIPE_01')." };
                    schema.Properties["water_liters"] = new ToolProperty { Type = "number", Description = "Target water volume in liters (default 100.0)." };
                    schema.Properties["additive_type"] = new ToolProperty { Type = "integer", Description = "Additive flavor type 1..3 (default 1)." };
                    schema.Properties["additive_amount"] = new ToolProperty { Type = "number", Description = "Additive amount in liters (default 20.0)." };
                    schema.Properties["sugar_kg"] = new ToolProperty { Type = "number", Description = "Sugar amount in kg (default 10.0)." };
                    schema.Properties["mix_time_seconds"] = new ToolProperty { Type = "number", Description = "Mixing duration in seconds (default 3.0)." };
                    schema.Properties["mix_speed_rpm"] = new ToolProperty { Type = "number", Description = "Mixer speed in RPM (default 1200.0)." };
                    schema.Properties["scale_factor"] = new ToolProperty { Type = "number", Description = "Simulation speed scale factor (default 1.0)." };
                    schema.Properties["timeout_seconds"] = new ToolProperty { Type = "integer", Description = "Max timeout in seconds (default 60)." };
                },
                args =>
                {
                    var recipe = new MixingRecipeInput
                    {
                        RecipeID = GetStringProp(args, "recipe_id") ?? "RECIPE_01",
                        WaterLiters = GetFloatProp(args, "water_liters", 100.0f),
                        AdditiveType = GetIntProp(args, "additive_type") ?? 1,
                        AdditiveAmount = GetFloatProp(args, "additive_amount", 20.0f),
                        SugarKg = GetFloatProp(args, "sugar_kg", 10.0f),
                        MixTimeSeconds = GetFloatProp(args, "mix_time_seconds", 3.0f),
                        MixSpeedRpm = GetFloatProp(args, "mix_speed_rpm", 1200.0f)
                    };
                    double scale = GetDoubleProp(args, "scale_factor") ?? 1.0;
                    int timeout = GetIntProp(args, "timeout_seconds") ?? 60;

                    var report = PlcSimManager.Instance.RunMixingSequence(recipe, scale, timeout);
                    return CallToolResult.Json(report, isError: !report.Success);
                });

            // 62. plcsim_run_sequence (Generic universal sequence runner)
            Register("plcsim_run_sequence", "Execute a generic automated online sequence test on any PLC sequence/state machine in S7-PLCSIM Advanced. Writes initial/recipe tags, triggers start, tracks step progression, polls monitored tags, and generates an execution telemetry report." + PlcSimAdvancedOnlyNote,
                schema =>
                {
                    schema.Properties["sequence_name"] = new ToolProperty { Type = "string", Description = "Name or ID of the sequence under test (e.g. 'BottlingCycle', 'PressSequence')." };
                    schema.Properties["start_command_tag"] = new ToolProperty { Type = "string", Description = "Tag name to pulse TRUE to initiate the sequence (e.g. 'DB_Seq.cmdStart')." };
                    schema.Properties["reset_command_tag"] = new ToolProperty { Type = "string", Description = "Optional tag name to pulse TRUE to reset the sequence before starting." };
                    schema.Properties["step_number_tag"] = new ToolProperty { Type = "string", Description = "Tag name holding the current sequence step/state number (e.g. 'DB_Seq.currentStep')." };
                    schema.Properties["step_name_tag"] = new ToolProperty { Type = "string", Description = "Optional string tag name holding current step name." };
                    schema.Properties["done_tag"] = new ToolProperty { Type = "string", Description = "Tag name indicating sequence completion (e.g. 'DB_Seq.done')." };
                    schema.Properties["fault_tag"] = new ToolProperty { Type = "string", Description = "Optional tag name indicating sequence fault/alarm." };
                    schema.Properties["idle_step"] = new ToolProperty { Type = "integer", Description = "Step integer value that indicates idle state (default 0)." };
                    schema.Properties["initial_tags"] = new ToolProperty { Type = "object", Description = "Optional key-value map of tags to initialize before sequence start." };
                    schema.Properties["parameter_tags"] = new ToolProperty { Type = "object", Description = "Optional key-value map of recipe/setpoint parameters to apply." };
                    schema.Properties["monitored_tags"] = new ToolProperty { Type = "array", Items = new ToolProperty { Type = "string" }, Description = "Optional array of tag names to sample in real-time telemetry." };
                    schema.Properties["scale_factor"] = new ToolProperty { Type = "number", Description = "Virtual controller speed scale factor (default 1.0)." };
                    schema.Properties["timeout_seconds"] = new ToolProperty { Type = "integer", Description = "Timeout in seconds to wait for sequence completion (default 60)." };
                    schema.Required = new List<string> { "start_command_tag", "step_number_tag", "done_tag" };
                },
                args =>
                {
                    var startTag = GetStringProp(args, "start_command_tag") ?? throw new ArgumentException("start_command_tag is required.");
                    var stepTag = GetStringProp(args, "step_number_tag") ?? throw new ArgumentException("step_number_tag is required.");
                    var doneTag = GetStringProp(args, "done_tag") ?? throw new ArgumentException("done_tag is required.");

                    var seqInput = new SequenceExecutionInput
                    {
                        SequenceName = GetStringProp(args, "sequence_name") ?? "Sequence_01",
                        StartCommandTag = startTag,
                        ResetCommandTag = GetStringProp(args, "reset_command_tag"),
                        StepNumberTag = stepTag,
                        StepNameTag = GetStringProp(args, "step_name_tag"),
                        DoneTag = doneTag,
                        FaultTag = GetStringProp(args, "fault_tag"),
                        IdleStep = GetIntProp(args, "idle_step") ?? 0,
                        InitialTags = GetObjectDictProp(args, "initial_tags"),
                        ParameterTags = GetObjectDictProp(args, "parameter_tags"),
                        MonitoredTags = GetStringListProp(args, "monitored_tags")
                    };

                    double scale = GetDoubleProp(args, "scale_factor") ?? 1.0;
                    int timeout = GetIntProp(args, "timeout_seconds") ?? 60;

                    var report = PlcSimManager.Instance.RunSequence(seqInput, scale, timeout);
                    return CallToolResult.Json(report, isError: !report.Success);
                });

            Register("tia_get_block_info", "Get PLC block identity, language, group path, and verified metadata fields for one exact block.",
                schema =>
                {
                    schema.Properties["block_name"] = new ToolProperty { Type = "string", Description = "Exact PLC block name." };
                    schema.Properties["plc_name"] = new ToolProperty { Type = "string", Description = "Exact PLC software name; required when the project has multiple PLCs." };
                    schema.Properties["group_path"] = new ToolProperty { Type = "string", Description = "Exact block-group path, required when the block name is duplicated. Use an empty string for the root group." };
                    schema.Required = new List<string> { "block_name" };
                },
                args =>
                {
                    string blockName = GetStringProp(args, "block_name") ?? throw new ArgumentException("block_name is required.");
                    return CallToolResult.Json(TiaManager.Instance.GetPlcBlockInfo(
                        GetStringProp(args, "plc_name"), blockName, GetStringProp(args, "group_path")));
                });

            Register("tia_get_block_attributes", "Read the verified PLC block attributes IsConsistent and IsKnowHowProtected for one exact block. " +
                "For an OB also SecondaryType, CyclicTime and PhaseOffset (microseconds, with Unit 'us' and ValueMs), PriorityNumber and the read-only CyclicApplicationCycleTime; an attribute the OB does not have is reported with Status 'not_supported'.",
                schema =>
                {
                    schema.Properties["block_name"] = new ToolProperty { Type = "string", Description = "Exact PLC block name." };
                    schema.Properties["plc_name"] = new ToolProperty { Type = "string", Description = "Exact PLC software name; required when the project has multiple PLCs." };
                    schema.Properties["group_path"] = new ToolProperty { Type = "string", Description = "Exact block-group path, required when the block name is duplicated. Use an empty string for the root group." };
                    schema.Required = new List<string> { "block_name" };
                },
                args =>
                {
                    string blockName = GetStringProp(args, "block_name") ?? throw new ArgumentException("block_name is required.");
                    return CallToolResult.Json(TiaManager.Instance.GetPlcBlockAttributes(
                        GetStringProp(args, "plc_name"), blockName, GetStringProp(args, "group_path")));
                });

            Register("tia_get_tag_table_info", "Get the exact PLC tag-table name, group path, and contained-tag count.",
                schema =>
                {
                    schema.Properties["table_name"] = new ToolProperty { Type = "string", Description = "Exact PLC tag-table name." };
                    schema.Properties["plc_name"] = new ToolProperty { Type = "string", Description = "Exact PLC software name; required when the project has multiple PLCs." };
                    schema.Properties["group_path"] = new ToolProperty { Type = "string", Description = "Exact tag-table-group path, required when the table name is duplicated. Use an empty string for the root group." };
                    schema.Required = new List<string> { "table_name" };
                },
                args =>
                {
                    string tableName = GetStringProp(args, "table_name") ?? throw new ArgumentException("table_name is required.");
                    return CallToolResult.Json(TiaManager.Instance.GetPlcTagTableInfo(
                        GetStringProp(args, "plc_name"), tableName, GetStringProp(args, "group_path")));
                });

            Register("tia_get_type_info", "Get the exact PLC type name, group path, and verified consistency metadata.",
                schema =>
                {
                    schema.Properties["type_name"] = new ToolProperty { Type = "string", Description = "Exact PLC user type name." };
                    schema.Properties["plc_name"] = new ToolProperty { Type = "string", Description = "Exact PLC software name; required when the project has multiple PLCs." };
                    schema.Properties["group_path"] = new ToolProperty { Type = "string", Description = "Exact PLC type-group path, required when the type name is duplicated. Use an empty string for the root group." };
                    schema.Required = new List<string> { "type_name" };
                },
                args =>
                {
                    string typeName = GetStringProp(args, "type_name") ?? throw new ArgumentException("type_name is required.");
                    return CallToolResult.Json(TiaManager.Instance.GetPlcTypeInfo(
                        GetStringProp(args, "plc_name"), typeName, GetStringProp(args, "group_path")));
                });

            Register("tia_get_type_attributes", "Read the verified IsConsistent attribute for one exact PLC user type.",
                schema =>
                {
                    schema.Properties["type_name"] = new ToolProperty { Type = "string", Description = "Exact PLC user type name." };
                    schema.Properties["plc_name"] = new ToolProperty { Type = "string", Description = "Exact PLC software name; required when the project has multiple PLCs." };
                    schema.Properties["group_path"] = new ToolProperty { Type = "string", Description = "Exact PLC type-group path, required when the type name is duplicated. Use an empty string for the root group." };
                    schema.Required = new List<string> { "type_name" };
                },
                args =>
                {
                    string typeName = GetStringProp(args, "type_name") ?? throw new ArgumentException("type_name is required.");
                    return CallToolResult.Json(TiaManager.Instance.GetPlcTypeAttributes(
                        GetStringProp(args, "plc_name"), typeName, GetStringProp(args, "group_path")));
                });

            Register("tia_get_db_member_attributes", "Read the V21-reported attributes of one exact direct member of a global DB. Nested member paths and dedicated datatype/revision fields are not returned.",
                schema =>
                {
                    schema.Properties["db_name"] = new ToolProperty { Type = "string", Description = "Exact global DB name." };
                    schema.Properties["member_name"] = new ToolProperty { Type = "string", Description = "Exact name of a direct member in the selected global DB; nested member paths are unsupported." };
                    schema.Properties["plc_name"] = new ToolProperty { Type = "string", Description = "Exact PLC software name; required when the project has multiple PLCs." };
                    schema.Properties["group_path"] = new ToolProperty { Type = "string", Description = "Exact DB block-group path, required when the DB name is duplicated. Use an empty string for the root group." };
                    schema.Required = new List<string> { "db_name", "member_name" };
                },
                args =>
                {
                    string dbName = GetStringProp(args, "db_name") ?? throw new ArgumentException("db_name is required.");
                    string memberName = GetStringProp(args, "member_name") ?? throw new ArgumentException("member_name is required.");
                    return CallToolResult.Json(TiaManager.Instance.GetDbMemberAttributes(
                        GetStringProp(args, "plc_name"), dbName, memberName, GetStringProp(args, "group_path")));
                });

            Register("tia_list_global_libraries", "List accessible global-library metadata from the connected TIA Portal instance with bounded paging.",
                schema =>
                {
                    schema.Properties["name_filter"] = new ToolProperty { Type = "string", Description = "Optional case-insensitive substring filter for library names." };
                    schema.Properties["offset"] = new ToolProperty { Type = "integer", Description = "Zero-based result offset (default 0)." };
                    schema.Properties["page_size"] = new ToolProperty { Type = "integer", Description = "Maximum results, from 1 through 200 (default 50)." };
                },
                args =>
                {
                    int offset = GetIntPropOrDefault(args, "offset", 0);
                    int pageSize = GetIntPropOrDefault(args, "page_size", 50);
                    return CallToolResult.Json(TiaManager.Instance.ListGlobalLibraries(
                        GetStringProp(args, "name_filter"), offset, pageSize));
                });

            Register("tia_search_project_library_types", "Search or list types in the project library for one exact open project path, with bounded paging.",
                schema =>
                {
                    schema.Properties["project_path"] = new ToolProperty { Type = "string", Description = "Exact full path of a project already open in the connected TIA Portal instance." };
                    schema.Properties["query"] = new ToolProperty { Type = "string", Description = "Optional case-insensitive substring filter for type name or namespace." };
                    schema.Properties["offset"] = new ToolProperty { Type = "integer", Description = "Zero-based result offset (default 0)." };
                    schema.Properties["page_size"] = new ToolProperty { Type = "integer", Description = "Maximum results, from 1 through 200 (default 50)." };
                    schema.Required = new List<string> { "project_path" };
                },
                args =>
                {
                    string projectPath = GetStringProp(args, "project_path") ?? throw new ArgumentException("project_path is required.");
                    int offset = GetIntPropOrDefault(args, "offset", 0);
                    int pageSize = GetIntPropOrDefault(args, "page_size", 50);
                    return CallToolResult.Json(TiaManager.Instance.SearchProjectLibraryTypes(
                        projectPath, GetStringProp(args, "query"), offset, pageSize));
                });

            Register("tia_list_project_faceplates", "List faceplate library types visible in one exact open project's library, with bounded paging.",
                schema =>
                {
                    schema.Properties["project_path"] = new ToolProperty { Type = "string", Description = "Exact full path of a project already open in the connected TIA Portal instance." };
                    schema.Properties["name_filter"] = new ToolProperty { Type = "string", Description = "Optional case-insensitive substring filter for faceplate names." };
                    schema.Properties["offset"] = new ToolProperty { Type = "integer", Description = "Zero-based result offset (default 0)." };
                    schema.Properties["page_size"] = new ToolProperty { Type = "integer", Description = "Maximum results, from 1 through 200 (default 50)." };
                    schema.Required = new List<string> { "project_path" };
                },
                args =>
                {
                    string projectPath = GetStringProp(args, "project_path") ?? throw new ArgumentException("project_path is required.");
                    int offset = GetIntPropOrDefault(args, "offset", 0);
                    int pageSize = GetIntPropOrDefault(args, "page_size", 50);
                    return CallToolResult.Json(TiaManager.Instance.ListProjectFaceplates(
                        projectPath, GetStringProp(args, "name_filter"), offset, pageSize));
                });

            Register("tia_get_library_type_instances", "Read instances of an exact project-library type in one exact PLC scope. Optionally select an exact type-version GUID and/or exact OriginalLibrary source string; omitted version/source selectors include all versions of the unique selected type.",
                schema =>
                {
                    schema.Properties["project_path"] = new ToolProperty { Type = "string", Description = "Exact full path of a project already open in the connected TIA Portal instance." };
                    schema.Properties["plc_name"] = new ToolProperty { Type = "string", Description = "Exact, ordinal-case-sensitive PLC software name providing the IInstanceSearchScope." };
                    schema.Properties["library_type_id"] = new ToolProperty { Type = "string", Description = "Exact GUID of a LibraryType visible in the selected project's ProjectLibrary." };
                    schema.Properties["version_id"] = new ToolProperty { Type = "string", Description = "Optional exact LibraryTypeVersion GUID. If omitted, instances are queried for all matching versions." };
                    schema.Properties["source_library"] = new ToolProperty { Type = "string", Description = "Optional exact, ordinal-case-sensitive LibraryTypeVersion.OriginalLibrary value." };
                    schema.Required = new List<string> { "project_path", "plc_name", "library_type_id" };
                },
                args =>
                {
                    string projectPath = GetStringProp(args, "project_path") ?? throw new ArgumentException("project_path is required.");
                    string plcName = GetStringProp(args, "plc_name") ?? throw new ArgumentException("plc_name is required.");
                    string libraryTypeId = GetStringProp(args, "library_type_id") ?? throw new ArgumentException("library_type_id is required.");
                    return CallToolResult.Json(TiaManager.Instance.GetLibraryTypeInstances(
                        projectPath, plcName, libraryTypeId,
                        GetStringProp(args, "version_id"), GetStringProp(args, "source_library")));
                });

            Register("tia_list_project_text_categories", "List text categories for one exact open project with optional literal filtering and bounded paging.",
                schema =>
                {
                    schema.Properties["project_path"] = new ToolProperty { Type = "string", Description = "Exact full path of a project already open in the connected TIA Portal instance." };
                    schema.Properties["query"] = new ToolProperty { Type = "string", Description = "Optional case-insensitive substring filter for category name or identifier." };
                    schema.Properties["offset"] = new ToolProperty { Type = "integer", Description = "Zero-based result offset (default 0)." };
                    schema.Properties["page_size"] = new ToolProperty { Type = "integer", Description = "Maximum results, from 1 through 200 (default 50)." };
                    schema.Required = new List<string> { "project_path" };
                },
                args =>
                {
                    string projectPath = GetStringProp(args, "project_path") ?? throw new ArgumentException("project_path is required.");
                    int offset = GetIntPropOrDefault(args, "offset", 0);
                    int pageSize = GetIntPropOrDefault(args, "page_size", 50);
                    return CallToolResult.Json(TiaManager.Instance.ListProjectTextCategories(
                        projectPath, GetStringProp(args, "query"), offset, pageSize));
                });

            Register("tia_download_project_texts", "Export project texts to a new file using explicit cultures; the destination is never overwritten.",
                schema =>
                {
                    schema.Properties["project_path"] = new ToolProperty { Type = "string", Description = "Exact full path of a project already open in the connected TIA Portal instance." };
                    schema.Properties["destination_file_path"] = new ToolProperty { Type = "string", Description = "Full path for a new export file in an existing directory; an existing file is rejected." };
                    schema.Properties["primary_culture"] = new ToolProperty { Type = "string", Description = "Valid .NET culture name used as the primary export language." };
                    schema.Properties["reference_culture"] = new ToolProperty { Type = "string", Description = "Valid .NET culture name used as the reference export language." };
                    schema.Required = new List<string> { "project_path", "destination_file_path", "primary_culture", "reference_culture" };
                },
                args =>
                {
                    string projectPath = GetStringProp(args, "project_path") ?? throw new ArgumentException("project_path is required.");
                    string destination = GetStringProp(args, "destination_file_path") ?? throw new ArgumentException("destination_file_path is required.");
                    string primaryCulture = GetStringProp(args, "primary_culture") ?? throw new ArgumentException("primary_culture is required.");
                    string referenceCulture = GetStringProp(args, "reference_culture") ?? throw new ArgumentException("reference_culture is required.");
                    return CallToolResult.Json(TiaManager.Instance.DownloadProjectTexts(
                        projectPath, destination, primaryCulture, referenceCulture));
                });

            Register("hmi_list_screen_groups", "List the nested screen-group hierarchy and direct screen counts for one unambiguous HMI Unified target.",
                schema =>
                {
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = "Exact device, device-item, or HMI software name; required when multiple HMI targets exist." };
                },
                args => CallToolResult.Json(TiaManager.Instance.ListHmiScreenGroups(GetStringProp(args, "target_name"))));

            Register("hmi_list_graphic_lists", "List graphic-list names for one unambiguous HMI Unified target; entries and file-backed assets are not included.",
                schema =>
                {
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = "Exact device, device-item, or HMI software name; required when multiple HMI targets exist." };
                },
                args => CallToolResult.Json(TiaManager.Instance.ListHmiGraphicLists(GetStringProp(args, "target_name"))));

            Register("tia_create_plc_tag_table", "Create a PLC tag table in an exact tag-table group; existing names are never overwritten.",
                schema =>
                {
                    schema.Properties["table_name"] = new ToolProperty { Type = "string", Description = "Name for the new PLC tag table." };
                    schema.Properties["plc_name"] = new ToolProperty { Type = "string", Description = "Exact PLC software name; required when the project has multiple PLCs." };
                    schema.Properties["group_path"] = new ToolProperty { Type = "string", Description = "Exact parent tag-table-group path; omit or use an empty string for the root group." };
                    schema.Required = new List<string> { "table_name" };
                },
                args =>
                {
                    string tableName = GetStringProp(args, "table_name") ?? throw new ArgumentException("table_name is required.");
                    var result = TiaManager.Instance.CreatePlcTagTable(
                        GetStringProp(args, "plc_name"), tableName, GetStringProp(args, "group_path"));
                    return CallToolResult.Json(result, isError: !result.Success);
                });

            Register("tia_create_tagtable_group", "Create a PLC tag-table group under an exact parent path; existing names are never overwritten.",
                schema =>
                {
                    schema.Properties["group_name"] = new ToolProperty { Type = "string", Description = "Name for the new tag-table group." };
                    schema.Properties["plc_name"] = new ToolProperty { Type = "string", Description = "Exact PLC software name; required when the project has multiple PLCs." };
                    schema.Properties["parent_group_path"] = new ToolProperty { Type = "string", Description = "Exact parent group path; omit or use an empty string for the root group." };
                    schema.Required = new List<string> { "group_name" };
                },
                args =>
                {
                    string groupName = GetStringProp(args, "group_name") ?? throw new ArgumentException("group_name is required.");
                    var result = TiaManager.Instance.CreateTagTableGroup(
                        GetStringProp(args, "plc_name"), groupName, GetStringProp(args, "parent_group_path"));
                    return CallToolResult.Json(result, isError: !result.Success);
                });

            Register("tia_create_block_group", "Create a PLC block group under an exact parent path; existing groups are never overwritten.",
                schema =>
                {
                    schema.Properties["group_name"] = new ToolProperty { Type = "string", Description = "Name for the new block group." };
                    schema.Properties["plc_name"] = new ToolProperty { Type = "string", Description = "Exact PLC software name; required when the project has multiple PLCs." };
                    schema.Properties["parent_group_path"] = new ToolProperty { Type = "string", Description = "Exact slash-separated parent block-group path; omit or use an empty string for the root group." };
                    schema.Required = new List<string> { "group_name" };
                },
                args =>
                {
                    string groupName = GetStringProp(args, "group_name") ?? throw new ArgumentException("group_name is required.");
                    var result = TiaManager.Instance.CreateBlockGroup(
                        GetStringProp(args, "plc_name"), groupName, GetStringProp(args, "parent_group_path"));
                    return CallToolResult.Json(result, isError: !result.Success);
                });

            Register("tia_create_user_constant", "Create a supported PLC user constant in an exact tag table without overwriting existing constants.",
                schema =>
                {
                    schema.Properties["table_name"] = new ToolProperty { Type = "string", Description = "Exact tag-table name." };
                    schema.Properties["constant_name"] = new ToolProperty { Type = "string", Description = "Name for the new user constant." };
                    schema.Properties["data_type"] = new ToolProperty { Type = "string", Description = "Supported type: Bool, SInt, Int, DInt, LInt, USInt, Byte, UInt, Word, UDInt, DWord, ULInt, LWord, Real, or LReal." };
                    schema.Properties["value"] = new ToolProperty { Type = "string", Description = "Invariant-culture literal value for the selected type." };
                    schema.Properties["plc_name"] = new ToolProperty { Type = "string", Description = "Exact PLC software name; required when the project has multiple PLCs." };
                    schema.Properties["group_path"] = new ToolProperty { Type = "string", Description = "Exact containing tag-table-group path; omit or use an empty string for the root group." };
                    schema.Required = new List<string> { "table_name", "constant_name", "data_type", "value" };
                },
                args =>
                {
                    string tableName = GetStringProp(args, "table_name") ?? throw new ArgumentException("table_name is required.");
                    string constantName = GetStringProp(args, "constant_name") ?? throw new ArgumentException("constant_name is required.");
                    string dataType = GetStringProp(args, "data_type") ?? throw new ArgumentException("data_type is required.");
                    string value = GetStringProp(args, "value") ?? throw new ArgumentException("value is required.");
                    var result = TiaManager.Instance.CreateUserConstant(
                        GetStringProp(args, "plc_name"), tableName, constantName, dataType, value, GetStringProp(args, "group_path"));
                    return CallToolResult.Json(result, isError: !result.Success);
                });

            Register("tia_update_user_constant", "Update only the value of an exact PLC user constant using its existing datatype.",
                schema =>
                {
                    schema.Properties["table_name"] = new ToolProperty { Type = "string", Description = "Exact tag-table name." };
                    schema.Properties["constant_name"] = new ToolProperty { Type = "string", Description = "Exact user-constant name." };
                    schema.Properties["value"] = new ToolProperty { Type = "string", Description = "Invariant-culture literal value compatible with the constant's existing datatype." };
                    schema.Properties["plc_name"] = new ToolProperty { Type = "string", Description = "Exact PLC software name; required when the project has multiple PLCs." };
                    schema.Properties["group_path"] = new ToolProperty { Type = "string", Description = "Exact containing tag-table-group path; omit or use an empty string for the root group." };
                    schema.Required = new List<string> { "table_name", "constant_name", "value" };
                },
                args =>
                {
                    string tableName = GetStringProp(args, "table_name") ?? throw new ArgumentException("table_name is required.");
                    string constantName = GetStringProp(args, "constant_name") ?? throw new ArgumentException("constant_name is required.");
                    string value = GetStringProp(args, "value") ?? throw new ArgumentException("value is required.");
                    var result = TiaManager.Instance.UpdateUserConstant(
                        GetStringProp(args, "plc_name"), tableName, constantName, value, GetStringProp(args, "group_path"));
                    return CallToolResult.Json(result, isError: !result.Success);
                });

            Register("tia_create_project", "Create a project in an existing empty directory using the connected TIA Portal instance; existing content is never overwritten.",
                schema =>
                {
                    schema.Properties["destination_location"] = new ToolProperty { Type = "string", Description = "Full path to an existing empty directory." };
                    schema.Properties["project_name"] = new ToolProperty { Type = "string", Description = "Plain project name without a path or .ap21 extension." };
                    schema.Required = new List<string> { "destination_location", "project_name" };
                },
                args =>
                {
                    string location = GetStringProp(args, "destination_location") ?? throw new ArgumentException("destination_location is required.");
                    string projectName = GetStringProp(args, "project_name") ?? throw new ArgumentException("project_name is required.");
                    var result = TiaManager.Instance.CreateProject(location, projectName);
                    return CallToolResult.Json(result, isError: !result.Success);
                });

            Register("hmi_create_screen_group", "Create a screen group under an exact parent path; on_conflict must be fail and no existing group is overwritten.",
                schema =>
                {
                    schema.Properties["group_name"] = new ToolProperty { Type = "string", Description = "Name for the new group; '/' is not allowed." };
                    schema.Properties["parent_group_path"] = new ToolProperty { Type = "string", Description = "Exact parent group path; omit or use an empty string for the root." };
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = "Exact HMI device, device-item, or software name; required when multiple targets exist." };
                    schema.Properties["on_conflict"] = new ToolProperty { Type = "string", Enum = new List<string> { "fail" }, Description = "Required conflict behavior; only 'fail' is supported." };
                    schema.Required = new List<string> { "group_name", "on_conflict" };
                },
                args =>
                {
                    string groupName = GetStringProp(args, "group_name") ?? throw new ArgumentException("group_name is required.");
                    string onConflict = GetStringProp(args, "on_conflict") ?? throw new ArgumentException("on_conflict is required.");
                    var result = TiaManager.Instance.CreateHmiScreenGroup(
                        groupName, GetStringProp(args, "parent_group_path"), GetStringProp(args, "target_name"), onConflict);
                    return CallToolResult.Json(result, isError: !result.Success);
                });

            Register("hmi_create_screen_under_group", "Create a screen inside an existing exact screen group; on_conflict must be fail and existing screens are not overwritten.",
                schema =>
                {
                    schema.Properties["parent_group_path"] = new ToolProperty { Type = "string", Description = "Exact non-empty screen-group path." };
                    schema.Properties["screen_name"] = new ToolProperty { Type = "string", Description = "Name for the new screen." };
                    schema.Properties["width"] = new ToolProperty { Type = "integer", Minimum = 1, Description = "Optional positive screen width in pixels." };
                    schema.Properties["height"] = new ToolProperty { Type = "integer", Minimum = 1, Description = "Optional positive screen height in pixels." };
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = "Exact HMI device, device-item, or software name; required when multiple targets exist." };
                    schema.Properties["on_conflict"] = new ToolProperty { Type = "string", Enum = new List<string> { "fail" }, Description = "Required conflict behavior; only 'fail' is supported." };
                    schema.Required = new List<string> { "parent_group_path", "screen_name", "on_conflict" };
                },
                args =>
                {
                    string groupPath = GetStringProp(args, "parent_group_path") ?? throw new ArgumentException("parent_group_path is required.");
                    string screenName = GetStringProp(args, "screen_name") ?? throw new ArgumentException("screen_name is required.");
                    string onConflict = GetStringProp(args, "on_conflict") ?? throw new ArgumentException("on_conflict is required.");
                    var result = TiaManager.Instance.CreateHmiScreenUnderGroup(
                        groupPath, screenName, GetOptionalUIntProp(args, "width"), GetOptionalUIntProp(args, "height"),
                        GetStringProp(args, "target_name"), onConflict);
                    return CallToolResult.Json(result, isError: !result.Success);
                });

            Register("hmi_create_navigation_button", "Create a new HMI button with an explicit Tapped action that navigates to an existing screen. Existing item names are never overwritten.",
                schema =>
                {
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = "Exact HMI software name from hmi_list_targets." };
                    schema.Properties["screen_name"] = new ToolProperty { Type = "string", Description = "Exact unique source screen name." };
                    schema.Properties["item_name"] = new ToolProperty { Type = "string", Description = "New button item name; collisions fail without replacement." };
                    schema.Properties["text"] = new ToolProperty { Type = "string", Description = "Plain-text button caption. Plain Unicode text (ä, ö, å, °); the server wraps and escapes it as HTML, so do not use HTML entities." };
                    schema.Properties["destination_screen"] = new ToolProperty { Type = "string", Description = "Exact unique destination screen name in the selected HMI software." };
                    schema.Properties["left"] = new ToolProperty { Type = "integer", Description = "Horizontal pixel position." };
                    schema.Properties["top"] = new ToolProperty { Type = "integer", Description = "Vertical pixel position." };
                    schema.Properties["width"] = new ToolProperty { Type = "integer", Description = "Button width in pixels." };
                    schema.Properties["height"] = new ToolProperty { Type = "integer", Description = "Button height in pixels." };
                    schema.Properties["back_color"] = new ToolProperty { Type = "string", Description = "Optional color: #RRGGBB, #AARRGGBB, or white, gray, grey, red, green, blue, transparent." };
                    schema.Properties["fore_color"] = new ToolProperty { Type = "string", Description = "Optional color: #RRGGBB, #AARRGGBB, or white, gray, grey, red, green, blue, transparent." };
                    schema.Properties["on_conflict"] = new ToolProperty { Type = "string", Enum = new List<string> { "fail" }, Description = "Only fail-on-conflict is supported." };
                    schema.Required = new List<string>
                    {
                        "target_name", "screen_name", "item_name", "text", "destination_screen",
                        "left", "top", "width", "height", "on_conflict"
                    };
                },
                args =>
                {
                    string targetName = GetStringProp(args, "target_name") ?? throw new ArgumentException("target_name is required.");
                    string screenName = GetStringProp(args, "screen_name") ?? throw new ArgumentException("screen_name is required.");
                    string itemName = GetStringProp(args, "item_name") ?? throw new ArgumentException("item_name is required.");
                    string text = GetStringProp(args, "text") ?? throw new ArgumentException("text is required.");
                    string destinationScreen = GetStringProp(args, "destination_screen") ?? throw new ArgumentException("destination_screen is required.");
                    int left = GetIntProp(args, "left") ?? throw new ArgumentException("left is required.");
                    int top = GetIntProp(args, "top") ?? throw new ArgumentException("top is required.");
                    uint width = GetUIntProp(args, "width") ?? throw new ArgumentException("width is required.");
                    uint height = GetUIntProp(args, "height") ?? throw new ArgumentException("height is required.");
                    string onConflict = GetStringProp(args, "on_conflict") ?? throw new ArgumentException("on_conflict is required.");
                    var result = TiaManager.Instance.CreateHmiNavigationButton(
                        targetName, screenName, itemName, text, destinationScreen, left, top,
                        width, height, GetStringProp(args, "back_color"), GetStringProp(args, "fore_color"), onConflict);
                    return CallToolResult.Json(result, isError: !result.Success);
                });

            Register("hmi_rename_screen", "Rename an exact screen in place within its group; existing siblings are never overwritten.",
                schema =>
                {
                    schema.Properties["screen_name"] = new ToolProperty { Type = "string", Description = "Exact existing screen name." };
                    schema.Properties["group_path"] = new ToolProperty { Type = "string", Description = "Exact containing group path; use an empty string for a root screen." };
                    schema.Properties["new_name"] = new ToolProperty { Type = "string", Description = "New screen name." };
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = "Exact HMI device, device-item, or software name; required when multiple targets exist." };
                    schema.Properties["on_conflict"] = new ToolProperty { Type = "string", Enum = new List<string> { "fail" }, Description = "Required conflict behavior; only 'fail' is supported." };
                    schema.Required = new List<string> { "screen_name", "group_path", "new_name", "on_conflict" };
                },
                args =>
                {
                    string screenName = GetStringProp(args, "screen_name") ?? throw new ArgumentException("screen_name is required.");
                    string groupPath = GetStringProp(args, "group_path") ?? throw new ArgumentException("group_path is required.");
                    string newName = GetStringProp(args, "new_name") ?? throw new ArgumentException("new_name is required.");
                    string onConflict = GetStringProp(args, "on_conflict") ?? throw new ArgumentException("on_conflict is required.");
                    var result = TiaManager.Instance.RenameHmiScreen(
                        screenName, groupPath, newName, GetStringProp(args, "target_name"), onConflict);
                    return CallToolResult.Json(result, isError: !result.Success);
                });

            Register("hmi_rename_screen_group", "Rename an exact screen group in place while preserving its child objects; existing siblings are never overwritten.",
                schema =>
                {
                    schema.Properties["group_path"] = new ToolProperty { Type = "string", Description = "Exact non-empty current group path." };
                    schema.Properties["new_name"] = new ToolProperty { Type = "string", Description = "New group name; '/' is not allowed." };
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = "Exact HMI device, device-item, or software name; required when multiple targets exist." };
                    schema.Properties["on_conflict"] = new ToolProperty { Type = "string", Enum = new List<string> { "fail" }, Description = "Required conflict behavior; only 'fail' is supported." };
                    schema.Required = new List<string> { "group_path", "new_name", "on_conflict" };
                },
                args =>
                {
                    string groupPath = GetStringProp(args, "group_path") ?? throw new ArgumentException("group_path is required.");
                    string newName = GetStringProp(args, "new_name") ?? throw new ArgumentException("new_name is required.");
                    string onConflict = GetStringProp(args, "on_conflict") ?? throw new ArgumentException("on_conflict is required.");
                    var result = TiaManager.Instance.RenameHmiScreenGroup(
                        groupPath, newName, GetStringProp(args, "target_name"), onConflict);
                    return CallToolResult.Json(result, isError: !result.Success);
                });

            Register("hmi_rename_tag_table", "Rename an HMI tag table in place and verify its tag names remain unchanged; existing tables are never overwritten.",
                schema =>
                {
                    schema.Properties["table_name"] = new ToolProperty { Type = "string", Description = "Exact existing HMI tag-table name." };
                    schema.Properties["new_name"] = new ToolProperty { Type = "string", Description = "New tag-table name." };
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = "Exact HMI device, device-item, or software name; required when multiple targets exist." };
                    schema.Properties["on_conflict"] = new ToolProperty { Type = "string", Enum = new List<string> { "fail" }, Description = "Required conflict behavior; only 'fail' is supported." };
                    schema.Required = new List<string> { "table_name", "new_name", "on_conflict" };
                },
                args =>
                {
                    string tableName = GetStringProp(args, "table_name") ?? throw new ArgumentException("table_name is required.");
                    string newName = GetStringProp(args, "new_name") ?? throw new ArgumentException("new_name is required.");
                    string onConflict = GetStringProp(args, "on_conflict") ?? throw new ArgumentException("on_conflict is required.");
                    var result = TiaManager.Instance.RenameHmiTagTable(
                        tableName, newName, GetStringProp(args, "target_name"), onConflict);
                    return CallToolResult.Json(result, isError: !result.Success);
                });

            Register("tia_get_plc_tag_info", "Read the supported metadata for one PLC tag, selected by exact name and tag-table scope.",
                schema =>
                {
                    schema.Properties["tag_name"] = new ToolProperty { Type = "string", Description = "Exact PLC tag name." };
                    schema.Properties["table_name"] = new ToolProperty { Type = "string", Description = "Exact PLC tag-table name containing the tag." };
                    schema.Properties["plc_name"] = new ToolProperty { Type = "string", Description = "Exact PLC software name; required when the project has multiple PLCs." };
                    schema.Properties["group_path"] = new ToolProperty { Type = "string", Description = "Exact tag-table-group path, required when the table name is duplicated. Use an empty string for the root group." };
                    schema.Required = new List<string> { "tag_name", "table_name" };
                },
                args =>
                {
                    string tagName = GetStringProp(args, "tag_name") ?? throw new ArgumentException("tag_name is required.");
                    string tableName = GetStringProp(args, "table_name") ?? throw new ArgumentException("table_name is required.");
                    return CallToolResult.Json(TiaManager.Instance.GetPlcTagInfo(
                        GetStringProp(args, "plc_name"), tableName, tagName, GetStringProp(args, "group_path")));
                });

            Register("tia_read_user_constant", "Read a user constant's declared type and literal value from the selected PLC.",
                schema =>
                {
                    schema.Properties["constant_name"] = new ToolProperty { Type = "string", Description = "Exact user-constant name." };
                    schema.Properties["plc_name"] = new ToolProperty { Type = "string", Description = "Exact PLC software name; required when the project has multiple PLCs." };
                    schema.Properties["table_name"] = new ToolProperty { Type = "string", Description = "Optional exact tag-table name. Omit only when the constant name resolves uniquely within the selected PLC and group scope." };
                    schema.Properties["group_path"] = new ToolProperty { Type = "string", Description = "Optional exact tag-table-group path. Use an empty string for the root group; may be used without table_name to scope the search." };
                    schema.Required = new List<string> { "constant_name" };
                },
                args =>
                {
                    string constantName = GetStringProp(args, "constant_name") ?? throw new ArgumentException("constant_name is required.");
                    return CallToolResult.Json(TiaManager.Instance.ReadUserConstant(
                        GetStringProp(args, "plc_name"), constantName,
                        GetStringProp(args, "table_name"), GetStringProp(args, "group_path")));
                });

            Register("tia_get_tag_table_attributes", "Read the V21-reported attributes, values, access modes, and supported types for one exact PLC tag table.",
                schema =>
                {
                    schema.Properties["table_name"] = new ToolProperty { Type = "string", Description = "Exact PLC tag-table name." };
                    schema.Properties["plc_name"] = new ToolProperty { Type = "string", Description = "Exact PLC software name; required when the project has multiple PLCs." };
                    schema.Properties["group_path"] = new ToolProperty { Type = "string", Description = "Exact tag-table-group path, required when the table name is duplicated. Use an empty string for the root group." };
                    schema.Required = new List<string> { "table_name" };
                },
                args =>
                {
                    string tableName = GetStringProp(args, "table_name") ?? throw new ArgumentException("table_name is required.");
                    return CallToolResult.Json(TiaManager.Instance.GetTagTableAttributes(
                        GetStringProp(args, "plc_name"), tableName, GetStringProp(args, "group_path")));
                });

            RegisterDocsTools();
            RegisterPlcOnlineTools();
            RegisterHardwareTools();
            RegisterBackupTools();
            RegisterHmiTagTools();
            RegisterHmiRuntimeTools();
            RegisterPlcBlockTools();
            RegisterPlcMetaTools();
            RegisterTechnologyObjectTools();
        }

        // Read-only online access to a real S7-1200/S7-1500 over S7CommPlus with TLS (TIA V17+ projects).
        // No TIA Portal or Openness needed. There are deliberately no write or operating-state tools.
        private void RegisterPlcOnlineTools()
        {
            const string requirement = "Works with S7-1200 and S7-1500 CPUs reachable on TCP port 102: TLS for projects from TIA Portal V17 or later, legacy challenge login for older ones; no TIA Portal needed. Read-only.";

            Register("plc_online_connect",
                "Connect to a real Siemens PLC over S7CommPlus to read online values. The connection stays open across calls until plc_online_disconnect. " + requirement,
                schema =>
                {
                    schema.Properties["address"] = new ToolProperty { Type = "string", Description = "IP address or host name of the PLC, e.g. '192.168.0.10'." };
                    schema.Properties["password"] = new ToolProperty { Type = "string", Description = "Optional PLC access-protection password; only needed when the CPU's protection level requires one for reading." };
                    schema.Properties["username"] = new ToolProperty { Type = "string", Description = "Optional user name for CPUs using user management (TIA V19+); leave empty otherwise." };
                    schema.Properties["timeout_ms"] = new ToolProperty { Type = "integer", Description = "Connection and response timeout in milliseconds, 1000-60000. Default 8000.", Default = 8000, Minimum = 1000 };
                    schema.Properties["security_mode"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "'auto' (default: try TLS, fall back to legacy login), 'tls' (TIA V17+ projects only) or 'legacy' (TIA V16 and older).",
                        Enum = new List<string> { "auto", "tls", "legacy" }
                    };
                    schema.Required = new List<string> { "address" };
                },
                args => CallToolResult.Json(PlcOnline.PlcOnlineManager.Instance.Connect(
                    GetStringProp(args, "address") ?? throw new ArgumentException("address is required."),
                    GetStringProp(args, "password"), GetStringProp(args, "username"),
                    GetDocsInt(args, "timeout_ms", 8000), GetStringProp(args, "security_mode"))));

            Register("plc_online_get_status", "Show whether an online PLC connection is open: address, TLS or legacy security, CPU name, order number and firmware, current operating state (RUN/STOP), browsed variable count and the last error.",
                schema => { },
                args => CallToolResult.Json(PlcOnline.PlcOnlineManager.Instance.GetStatus()));

            Register("plc_online_disconnect", "Close the online PLC connection opened with plc_online_connect.",
                schema => { },
                args => CallToolResult.Json(PlcOnline.PlcOnlineManager.Instance.Disconnect()));

            Register("plc_online_list_datablocks", "List the data blocks in the connected PLC (number and name), read online.",
                schema => { },
                args => CallToolResult.Json(PlcOnline.PlcOnlineManager.Instance.ListDatablocks()));

            Register("plc_online_browse",
                "List variables in the connected PLC with their data types: data block members ('DB_Name.Member.Sub'), inputs, outputs and memory. An array of a simple type is one row with its bounds in 'array'; read it whole by name or one element as 'Name[3]'. The full list is read once and cached until refresh is true; filter narrows it by name text. Use the names with plc_online_read_tags.",
                schema =>
                {
                    schema.Properties["filter"] = new ToolProperty { Type = "string", Description = "Optional case-insensitive text the variable name must contain, e.g. 'DB_Process.' or 'Speed'." };
                    schema.Properties["offset"] = new ToolProperty { Type = "integer", Description = "Rows to skip, for paging. Default 0.", Default = 0, Minimum = 0 };
                    schema.Properties["limit"] = new ToolProperty { Type = "integer", Description = "Maximum rows, 1-1000. Default 200.", Default = 200, Minimum = 1 };
                    schema.Properties["refresh"] = new ToolProperty { Type = "boolean", Description = "Browse the PLC again instead of using the cached list (after a download). Default false.", Default = false };
                },
                args => CallToolResult.Json(PlcOnline.PlcOnlineManager.Instance.Browse(
                    GetStringProp(args, "filter"), GetDocsInt(args, "offset", 0), GetDocsInt(args, "limit", 200), GetBoolProp(args, "refresh", false))));

            Register("plc_online_read_tags",
                "Read current values from the connected PLC by full symbolic name (e.g. 'DB_Process.Level' or a PLC tag name). Returns value, data type and quality for each tag; names that cannot be resolved get an error without failing the others. At most 200 tags per call.",
                schema =>
                {
                    schema.Properties["tags"] = new ToolProperty
                    {
                        Type = "array",
                        Description = "Symbolic variable names as listed by plc_online_browse.",
                        Items = new ToolProperty { Type = "string", Description = "Full symbolic name." }
                    };
                    schema.Required = new List<string> { "tags" };
                },
                args => CallToolResult.Json(PlcOnline.PlcOnlineManager.Instance.ReadTags(
                    GetStringListProp(args, "tags") ?? throw new ArgumentException("tags is required."))));
        }

        // Siemens online documentation (docs.tia.siemens.cloud and docs.industrial-operations-x.siemens.cloud).
        // These tools need internet access but no TIA Portal connection.
        private void RegisterDocsTools()
        {
            const string urlDoc = "Pass a compact reference (<prefix>:<mapId> or <prefix>:<mapId>/<contentId>; prefixes tia=docs.tia.siemens.cloud, iox=docs.industrial-operations-x.siemens.cloud) or a reader URL copied from a tool result or the browser.";
            const string referenceDoc = "Results use compact references by default; set reference_format='url' for reader URLs.";
            const string filterDoc = "product (e.g. 'TIAPortal' for the Openness API, 'STEP 7', 'WinCC Unified') and version (e.g. 'V21') are matched against the catalog; call docs_list_publications to see valid values.";
            var referenceFormat = new ToolProperty
            {
                Type = "string",
                Description = "'compact' (default) for short references or 'url' for full reader URLs.",
                Enum = new List<string> { "compact", "url" }
            };
            var host = new ToolProperty { Type = "string", Description = "Documentation host; default docs.tia.siemens.cloud. Other allowed host: docs.industrial-operations-x.siemens.cloud." };
            var locale = new ToolProperty { Type = "string", Description = "Content locale, e.g. 'en-US' (default) or 'de-DE'." };
            var product = new ToolProperty { Type = "string", Description = "Optional product filter, e.g. 'TIAPortal' (Openness API), 'STEP 7', 'WinCC Unified'." };
            var version = new ToolProperty { Type = "string", Description = "Optional version filter, e.g. 'V21'." };

            Register("docs_search",
                "Full-text search of the online Siemens documentation (TIA Portal, STEP 7, WinCC Unified, Openness API, PLCSIM, Industrial Operations X). Returns a numbered Markdown list: title, publication and version, breadcrumb, excerpt and reference to pass to docs_read_page or docs_get_toc. Use it to check Openness classes, SCL instructions or WinCC Unified SysFct/JavaScript APIs before relying on them. Set scope to a publication or topic reference to search only inside it (product and version must then be omitted). " + referenceDoc + " " + filterDoc,
                schema =>
                {
                    schema.Properties["query"] = new ToolProperty { Type = "string", Description = "Search text, e.g. 'OpenScreenInPopup' or 'PlcBlockGroup CreateFromTemplate'." };
                    schema.Properties["product"] = product;
                    schema.Properties["version"] = version;
                    schema.Properties["host"] = host;
                    schema.Properties["locale"] = locale;
                    schema.Properties["limit"] = new ToolProperty { Type = "integer", Description = "Number of hits, 1-50. Default 10.", Default = 10, Minimum = 1 };
                    schema.Properties["scope"] = new ToolProperty { Type = "string", Description = "Optional publication or topic reference to search within. " + urlDoc };
                    schema.Properties["reference_format"] = referenceFormat;
                    schema.Required = new List<string> { "query" };
                },
                args => CallToolResult.Text(Docs.DocsTools.Instance.Search(
                    GetStringProp(args, "query") ?? throw new ArgumentException("query is required."),
                    GetStringProp(args, "host"), GetStringProp(args, "product"), GetStringProp(args, "version"),
                    GetStringProp(args, "locale"), GetDocsInt(args, "limit", 10),
                    GetStringProp(args, "reference_format"), GetStringProp(args, "scope"))));

            Register("docs_read_page",
                "Read one Siemens documentation page as Markdown, with a header naming the publication, version, path and source reference. mode='outline' lists numbered headings with sizes; section takes an outline number or heading text (e.g. 'Example', which also matches 'Example: ...') and returns only those sections; mode='code' returns only code blocks under their headings. Long results end with the exact arguments to fetch the next part. " + urlDoc + " " + referenceDoc,
                schema =>
                {
                    schema.Properties["url"] = new ToolProperty { Type = "string", Description = "Page reference or reader URL. " + urlDoc };
                    schema.Properties["mode"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "'full' (default), 'outline' (headings with sizes) or 'code' (code blocks only).",
                        Enum = new List<string> { "full", "outline", "code" }
                    };
                    schema.Properties["section"] = new ToolProperty { Type = "string", Description = "Optional outline number (e.g. '3') or heading text from mode='outline'." };
                    schema.Properties["offset"] = new ToolProperty { Type = "integer", Description = "Character offset to continue a long result; use the value the previous part gives.", Default = 0, Minimum = 0 };
                    schema.Properties["max_chars"] = new ToolProperty { Type = "integer", Description = "Maximum characters returned, 1000-100000. Default 20000.", Default = 20000, Minimum = 1000 };
                    schema.Properties["reference_format"] = referenceFormat;
                    schema.Required = new List<string> { "url" };
                },
                args => CallToolResult.Text(Docs.DocsTools.Instance.ReadPage(
                    GetStringProp(args, "url") ?? throw new ArgumentException("url is required."),
                    GetDocsInt(args, "offset", 0), GetDocsInt(args, "max_chars", 20000),
                    GetStringProp(args, "reference_format"), GetDocsSection(args), GetStringProp(args, "mode"))));

            Register("docs_get_toc",
                "Show the table of contents of a Siemens documentation publication, or of the subtree under a topic: an indented outline with one title and reference per line. " + urlDoc + " " + referenceDoc,
                schema =>
                {
                    schema.Properties["url"] = new ToolProperty { Type = "string", Description = "Publication or topic reference, or a reader URL. " + urlDoc };
                    schema.Properties["depth"] = new ToolProperty { Type = "integer", Description = "Levels to show, 1-6. Default 2.", Default = 2, Minimum = 1 };
                    schema.Properties["reference_format"] = referenceFormat;
                    schema.Required = new List<string> { "url" };
                },
                args => CallToolResult.Text(Docs.DocsTools.Instance.GetToc(
                    GetStringProp(args, "url") ?? throw new ArgumentException("url is required."),
                    GetDocsInt(args, "depth", 2), GetStringProp(args, "reference_format"))));

            Register("docs_list_publications",
                "List Siemens documentation publications (up to 50): title, product, version, locale and reference. Use it to find valid product and version values for docs_search, or a publication to open with docs_get_toc. " + filterDoc + " " + referenceDoc,
                schema =>
                {
                    schema.Properties["title_contains"] = new ToolProperty { Type = "string", Description = "Optional case-insensitive text the title must contain, e.g. 'Openness'." };
                    schema.Properties["product"] = product;
                    schema.Properties["version"] = version;
                    schema.Properties["host"] = host;
                    schema.Properties["locale"] = locale;
                    schema.Properties["reference_format"] = referenceFormat;
                },
                args => CallToolResult.Text(Docs.DocsTools.Instance.ListPublications(
                    GetStringProp(args, "host"), GetStringProp(args, "product"), GetStringProp(args, "version"),
                    GetStringProp(args, "locale"), GetStringProp(args, "title_contains"), GetStringProp(args, "reference_format"))));
        }

        private static int GetDocsInt(JsonElement? element, string propName, int defaultValue)
        {
            if (!element.HasValue || element.Value.ValueKind != JsonValueKind.Object ||
                !element.Value.TryGetProperty(propName, out var property) || property.ValueKind == JsonValueKind.Null)
                return defaultValue;
            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int value)) return value;
            if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out value))
                return value;
            throw new ArgumentException($"'{propName}' must be an integer.");
        }

        private static object? GetDocsSection(JsonElement? element)
        {
            if (!element.HasValue || element.Value.ValueKind != JsonValueKind.Object ||
                !element.Value.TryGetProperty("section", out var property))
                return null;
            switch (property.ValueKind)
            {
                case JsonValueKind.Null: return null;
                case JsonValueKind.String: return property.GetString();
                case JsonValueKind.Number when property.TryGetInt32(out int n): return n;
                default: throw new ArgumentException("'section' must be an outline number or heading text.");
            }
        }

        private void Register(string name, string description, Action<ToolInputSchema> schemaBuilder, Func<JsonElement?, CallToolResult> handler)
        {
            var schema = new ToolInputSchema();
            schemaBuilder(schema);

            var tool = new ToolDefinition
            {
                Name = name,
                Description = description,
                InputSchema = schema
            };

            _tools.Add(tool);
            _handlers[name] = handler;
        }

        private static string? GetStringProp(JsonElement? element, string propName)
        {
            if (element.HasValue && element.Value.ValueKind == JsonValueKind.Object &&
                element.Value.TryGetProperty(propName, out var prop))
            {
                return prop.GetString();
            }
            return null;
        }

        private static int? GetIntProp(JsonElement? element, string propName)
        {
            if (element.HasValue && element.Value.ValueKind == JsonValueKind.Object &&
                element.Value.TryGetProperty(propName, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out int val))
                    return val;
            }
            return null;
        }

        private static int GetIntPropOrDefault(JsonElement? element, string propName, int defaultValue)
        {
            if (!element.HasValue || element.Value.ValueKind != JsonValueKind.Object ||
                !element.Value.TryGetProperty(propName, out var property))
            {
                return defaultValue;
            }

            if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out int value))
            {
                throw new ArgumentException($"'{propName}' must be a 32-bit integer.");
            }

            return value;
        }

        private static long? GetLongProp(JsonElement? element, string propName)
        {
            if (element.HasValue && element.Value.ValueKind == JsonValueKind.Object &&
                element.Value.TryGetProperty(propName, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out long val))
                    return val;
            }
            return null;
        }

        private static uint? GetUIntProp(JsonElement? element, string propName)
        {
            if (element.HasValue && element.Value.ValueKind == JsonValueKind.Object &&
                element.Value.TryGetProperty(propName, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number && prop.TryGetUInt32(out uint val))
                    return val;
            }
            return null;
        }

        private static uint? GetOptionalUIntProp(JsonElement? element, string propName)
        {
            if (!element.HasValue || element.Value.ValueKind != JsonValueKind.Object ||
                !element.Value.TryGetProperty(propName, out var property))
                return null;

            if (property.ValueKind != JsonValueKind.Number || !property.TryGetUInt32(out uint value))
                throw new ArgumentException($"{propName} must be a non-negative 32-bit integer.");

            return value;
        }

        private static float GetFloatProp(JsonElement? element, string propName, float defaultValue)
        {
            if (element.HasValue && element.Value.ValueKind == JsonValueKind.Object &&
                element.Value.TryGetProperty(propName, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number && prop.TryGetSingle(out float val))
                    return val;
            }
            return defaultValue;
        }

        private static double? GetDoubleProp(JsonElement? element, string propName)
        {
            if (element.HasValue && element.Value.ValueKind == JsonValueKind.Object &&
                element.Value.TryGetProperty(propName, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out double val))
                    return val;
            }
            return null;
        }

        private static bool GetBoolProp(JsonElement? element, string propName, bool defaultValue)
        {
            if (element.HasValue && element.Value.ValueKind == JsonValueKind.Object &&
                element.Value.TryGetProperty(propName, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.True) return true;
                if (prop.ValueKind == JsonValueKind.False) return false;
            }
            return defaultValue;
        }

        private static bool? GetNullableBoolProp(JsonElement? element, string propName)
        {
            if (!element.HasValue || element.Value.ValueKind != JsonValueKind.Object ||
                !element.Value.TryGetProperty(propName, out var prop))
                return null;
            if (prop.ValueKind == JsonValueKind.True) return true;
            if (prop.ValueKind == JsonValueKind.False) return false;
            throw new ArgumentException($"{propName} must be a boolean.");
        }

        private static HmiMappingValueInput? GetMappingValueInputProp(JsonElement? element, string propName)
        {
            if (!element.HasValue || element.Value.ValueKind != JsonValueKind.Object ||
                !element.Value.TryGetProperty(propName, out var prop))
                return null;
            if (prop.ValueKind != JsonValueKind.Object ||
                !prop.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
                !prop.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.String)
                throw new ArgumentException($"{propName} must be an object with string 'type' and 'value' fields.");

            return new HmiMappingValueInput
            {
                Type = type.GetString() ?? "",
                Value = value.GetString() ?? ""
            };
        }

        private static List<string>? GetStringListProp(JsonElement? element, string propName)
        {
            if (element.HasValue && element.Value.ValueKind == JsonValueKind.Object &&
                element.Value.TryGetProperty(propName, out var prop) &&
                prop.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var item in prop.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var s = item.GetString();
                        if (s != null) list.Add(s);
                    }
                }
                return list;
            }
            return null;
        }

        private static List<HmiRangeEntryInput>? GetRangesProp(JsonElement? element, string propName)
        {
            if (element.HasValue && element.Value.ValueKind == JsonValueKind.Object &&
                element.Value.TryGetProperty(propName, out var prop) &&
                prop.ValueKind == JsonValueKind.Array)
            {
                var list = new List<HmiRangeEntryInput>();
                foreach (var item in prop.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                        throw new ArgumentException($"Each '{propName}' entry must be an object.");
                    if (!item.TryGetProperty("from", out var pFrom))
                        throw new ArgumentException($"Each '{propName}' entry must include a numeric 'from' value.");
                    if (!item.TryGetProperty("to", out var pTo))
                        throw new ArgumentException($"Each '{propName}' entry must include a numeric 'to' value.");
                    if (!item.TryGetProperty("value", out var pVal) || pVal.ValueKind != JsonValueKind.String)
                        throw new ArgumentException($"Each '{propName}' entry must include a string 'value'.");

                    list.Add(new HmiRangeEntryInput
                    {
                        From = GetRangeNumber(pFrom, propName, "from"),
                        To = GetRangeNumber(pTo, propName, "to"),
                        Value = pVal.GetString() ?? ""
                    });
                }
                return list;
            }
            return null;
        }

        private static object GetRangeNumber(JsonElement value, string propName, string fieldName)
        {
            if (value.ValueKind != JsonValueKind.Number)
                throw new ArgumentException($"'{fieldName}' in '{propName}' entries must be a number.");
            if (value.TryGetInt32(out int intValue)) return intValue;
            if (value.TryGetInt64(out long longValue)) return longValue;
            if (value.TryGetUInt64(out ulong ulongValue)) return ulongValue;
            if (value.TryGetDouble(out double doubleValue)) return doubleValue;
            throw new ArgumentException($"'{fieldName}' in '{propName}' entries is outside the supported numeric range.");
        }

        private static List<TextListEntryInput> GetTextListEntries(JsonElement? element, string propName)
        {
            var list = new List<TextListEntryInput>();
            if (element.HasValue && element.Value.ValueKind == JsonValueKind.Object &&
                element.Value.TryGetProperty(propName, out var prop) &&
                prop.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in prop.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        int val = 0;
                        int? fromVal = null;
                        int? toVal = null;
                        string text = "";
                        Dictionary<string, string>? ml = null;

                        if (item.TryGetProperty("value", out var pVal) && pVal.TryGetInt32(out var iVal)) val = iVal;
                        if (item.TryGetProperty("from_value", out var pFrom) && pFrom.TryGetInt32(out var iFrom)) fromVal = iFrom;
                        if (item.TryGetProperty("to_value", out var pTo) && pTo.TryGetInt32(out var iTo)) toVal = iTo;
                        if (item.TryGetProperty("text", out var pTxt)) text = pTxt.GetString() ?? "";

                        if (item.TryGetProperty("multilingual_texts", out var pMl) && pMl.ValueKind == JsonValueKind.Object)
                        {
                            ml = new Dictionary<string, string>();
                            foreach (var propMl in pMl.EnumerateObject())
                            {
                                ml[propMl.Name] = propMl.Value.GetString() ?? "";
                            }
                        }

                        list.Add(new TextListEntryInput
                        {
                            Value = val,
                            FromValue = fromVal,
                            ToValue = toVal,
                            Text = text,
                            MultilingualTexts = ml
                        });
                    }
                }
            }
            return list;
        }

        private static Dictionary<string, object>? GetObjectDictProp(JsonElement? element, string propName)
        {
            if (element.HasValue && element.Value.ValueKind == JsonValueKind.Object &&
                element.Value.TryGetProperty(propName, out var prop) &&
                prop.ValueKind == JsonValueKind.Object)
            {
                var dict = new Dictionary<string, object>();
                foreach (var p in prop.EnumerateObject())
                {
                    dict[p.Name] = p.Value;
                }
                return dict;
            }
            return null;
        }
    }
}
