using System;
using System.Collections.Generic;
using TiaOpennessMcp.Protocol;
using TiaOpennessMcp.Tia;

namespace TiaOpennessMcp.Tools
{
    // Delete and rename of PLC data types (UDTs) and rename of program blocks.
    public partial class ToolRegistry
    {
        private const string PlcSoftwareNameDescription = "Exact PLC software name; required when the project has multiple PLCs.";
        private const string SoftwareUnitScopeNote = " Only the PLC software's own folders are searched; objects inside S7-1500 software units are out of scope.";

        private void RegisterPlcBlockTools()
        {
            Register("tia_delete_plc_type",
                "Delete one PLC data type (UDT) by exact, case-sensitive name. TIA Portal decides whether the delete is allowed and its answer is reported; " +
                "blocks, DBs and other UDTs that used the type become inconsistent until changed, so compile afterwards. Program blocks are deleted with tia_delete_block." +
                SoftwareUnitScopeNote,
                schema =>
                {
                    schema.Properties["type_name"] = new ToolProperty { Type = "string", Description = "Exact name of the PLC data type (UDT) to delete." };
                    schema.Properties["plc_name"] = new ToolProperty { Type = "string", Description = PlcSoftwareNameDescription };
                    schema.Properties["group_path"] = new ToolProperty { Type = "string", Description = "Exact type-folder path, e.g. 'HMI/Manual'. Omit to search all folders; an empty string means the root folder only." };
                    schema.Required = new List<string> { "type_name" };
                },
                args =>
                {
                    string typeName = GetStringProp(args, "type_name") ?? throw new ArgumentException("type_name is required.");
                    var result = TiaManager.Instance.DeletePlcType(
                        GetStringProp(args, "plc_name"), typeName, GetStringProp(args, "group_path"));
                    return CallToolResult.Json(result, isError: !result.Success);
                });

            Register("tia_rename_plc_type",
                "Rename one PLC data type (UDT) in place (no delete, re-import or overwrite) and verify by read-back. Fails without changes when any UDT or program block " +
                "in the PLC already uses the new name (case-insensitive). Importing a UDT under a new name creates a second type instead; use this tool to rename." +
                SoftwareUnitScopeNote,
                schema =>
                {
                    schema.Properties["type_name"] = new ToolProperty { Type = "string", Description = "Exact current name of the PLC data type (UDT)." };
                    schema.Properties["new_name"] = new ToolProperty { Type = "string", Description = "New name; no leading/trailing whitespace, control characters or path separators." };
                    schema.Properties["plc_name"] = new ToolProperty { Type = "string", Description = PlcSoftwareNameDescription };
                    schema.Properties["group_path"] = new ToolProperty { Type = "string", Description = "Exact type-folder path of the type. Omit to search all folders; an empty string means the root folder only." };
                    schema.Properties["on_conflict"] = new ToolProperty { Type = "string", Description = "Conflict policy; only 'fail' is supported, existing names are never overwritten.", Enum = new List<string> { "fail" }, Default = "fail" };
                    schema.Required = new List<string> { "type_name", "new_name" };
                },
                args =>
                {
                    string typeName = GetStringProp(args, "type_name") ?? throw new ArgumentException("type_name is required.");
                    string newName = GetStringProp(args, "new_name") ?? throw new ArgumentException("new_name is required.");
                    var result = TiaManager.Instance.RenamePlcType(
                        GetStringProp(args, "plc_name"), typeName, newName, GetStringProp(args, "group_path"), GetStringProp(args, "on_conflict"));
                    return CallToolResult.Json(result, isError: !result.Success);
                });

            Register("tia_rename_block",
                "Rename one PLC program block (OB/FB/FC/DB) in place and verify by read-back; the block number is left unchanged. Fails without changes when any program block " +
                "or PLC data type in the PLC already uses the new name (case-insensitive). PLC data types are renamed with tia_rename_plc_type." +
                SoftwareUnitScopeNote,
                schema =>
                {
                    schema.Properties["block_name"] = new ToolProperty { Type = "string", Description = "Exact current name of the program block." };
                    schema.Properties["new_name"] = new ToolProperty { Type = "string", Description = "New name; no leading/trailing whitespace, control characters or path separators." };
                    schema.Properties["plc_name"] = new ToolProperty { Type = "string", Description = PlcSoftwareNameDescription };
                    schema.Properties["group_path"] = new ToolProperty { Type = "string", Description = "Exact block-folder path of the block. Omit to search all folders; an empty string means the root folder only." };
                    schema.Properties["on_conflict"] = new ToolProperty { Type = "string", Description = "Conflict policy; only 'fail' is supported, existing names are never overwritten.", Enum = new List<string> { "fail" }, Default = "fail" };
                    schema.Required = new List<string> { "block_name", "new_name" };
                },
                args =>
                {
                    string blockName = GetStringProp(args, "block_name") ?? throw new ArgumentException("block_name is required.");
                    string newName = GetStringProp(args, "new_name") ?? throw new ArgumentException("new_name is required.");
                    var result = TiaManager.Instance.RenameBlock(
                        GetStringProp(args, "plc_name"), blockName, newName, GetStringProp(args, "group_path"), GetStringProp(args, "on_conflict"));
                    return CallToolResult.Json(result, isError: !result.Success);
                });
        }
    }
}
