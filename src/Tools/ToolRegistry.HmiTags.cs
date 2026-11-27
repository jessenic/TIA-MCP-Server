using System;
using System.Collections.Generic;
using TiaOpennessMcp.Protocol;
using TiaOpennessMcp.Tia;

namespace TiaOpennessMcp.Tools
{
    // Delete and rename of single Unified HMI tags.
    public partial class ToolRegistry
    {
        private IHmiTagOperations? _hmiTagOperationsOverride;

        // Offline tests inject a fake backend here; production uses TiaManager.
        public ToolRegistry UseHmiTagOperations(IHmiTagOperations operations)
        {
            _hmiTagOperationsOverride = operations ?? throw new ArgumentNullException(nameof(operations));
            return this;
        }

        private IHmiTagOperations HmiTagOperations =>
            _hmiTagOperationsOverride ?? (_hmiToolOperations.Value as IHmiTagOperations) ?? TiaManager.Instance;

        private void RegisterHmiTagTools()
        {
            const string targetDescription = "Exact HMI device, device-item, or software name; required when multiple targets exist.";
            const string tableDescription = "Optional exact tag-table name (or 'group/table' path) that must contain the tag. Tag names are unique per HMI software, so this is only a safety check.";

            Register("hmi_delete_tag",
                "Delete one Unified HMI tag by exact name, including tags in tag tables inside tag-table groups. " +
                "Before deleting, a best-effort scan looks for references in alarms (state, acknowledgment, parameter and trigger tags), other tags (handshake, limit and logging trigger tags), the exclusive-operation runtime settings, screen and item dynamizations, button parts and scripts; " +
                "TIA cross-references ('used by') are listed separately in CrossReferences when available. " +
                "By default (on_referenced='fail') a tag with scan hits or cross-references is not deleted and the references are returned. dry_run=true only reports (status 'dry_run'). " +
                "Members of structured tags, system tags and whole tag tables cannot be deleted with this tool.",
                schema =>
                {
                    schema.Properties["tag_name"] = new ToolProperty { Type = "string", Description = "Exact HMI tag name (case-sensitive)." };
                    schema.Properties["table_name"] = new ToolProperty { Type = "string", Description = tableDescription };
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = targetDescription };
                    schema.Properties["on_referenced"] = new ToolProperty
                    {
                        Type = "string",
                        Enum = new List<string> { "fail", "ignore" },
                        Default = "fail",
                        Description = "'fail' (default) refuses to delete a tag that the scan finds referenced; 'ignore' deletes it and returns the references that now point to a missing tag."
                    };
                    schema.Properties["dry_run"] = new ToolProperty { Type = "boolean", Default = false, Description = "Only resolve the tag and report its references; change nothing (default: false)." };
                    schema.Required = new List<string> { "tag_name" };
                },
                args =>
                {
                    string tagName = GetStringProp(args, "tag_name") ?? throw new ArgumentException("tag_name is required.");
                    string onReferenced = GetStringProp(args, "on_referenced") ?? "fail";
                    if (onReferenced != "fail" && onReferenced != "ignore")
                        throw new ArgumentException("on_referenced must be 'fail' or 'ignore'.");
                    var result = HmiTagOperations.DeleteHmiTag(
                        tagName,
                        GetStringProp(args, "table_name"),
                        GetStringProp(args, "target_name"),
                        onReferenced,
                        GetBoolProp(args, "dry_run", false));
                    return CallToolResult.Json(result, isError: !result.Success);
                });

            Register("hmi_rename_tag",
                "Rename one Unified HMI tag in place with TIA's own rename (the tag is never deleted and recreated), including tags in grouped tag tables. " +
                "Before renaming, a best-effort scan lists structured references (alarms, other tags, runtime settings, dynamizations, button parts, script trigger tags) and script text occurrences; after renaming, each structured reference is read back and reported with Updated=true or false. " +
                "TIA cross-references are listed in CrossReferences but not read back. " +
                "Structured references are never rewritten by this tool. script_references='replace' also rewrites script string literals that are exactly the old name (\"Old\" or 'Old'), only in scripts that have a syntax check; other occurrences are only reported. " +
                "The PLC tag is not renamed. Run with dry_run=true first.",
                schema =>
                {
                    schema.Properties["tag_name"] = new ToolProperty { Type = "string", Description = "Exact existing HMI tag name (case-sensitive)." };
                    schema.Properties["new_name"] = new ToolProperty { Type = "string", Description = "New HMI tag name. It must not exist yet (compared case-insensitively, system tags included) and must not contain . [ ] { } \" ' ` \\ or :." };
                    schema.Properties["table_name"] = new ToolProperty { Type = "string", Description = tableDescription };
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = targetDescription };
                    schema.Properties["on_conflict"] = new ToolProperty { Type = "string", Enum = new List<string> { "fail" }, Description = "Required conflict behavior; only 'fail' is supported." };
                    schema.Properties["script_references"] = new ToolProperty
                    {
                        Type = "string",
                        Enum = new List<string> { "report", "replace" },
                        Default = "report",
                        Description = "'report' (default) only lists script occurrences of the old name; 'replace' also rewrites string literals that are exactly the old name and verifies each script by read-back and by a syntax check that must not report more errors than before."
                    };
                    schema.Properties["dry_run"] = new ToolProperty { Type = "boolean", Default = false, Description = "Only report what the rename would touch; change nothing (default: false)." };
                    schema.Required = new List<string> { "tag_name", "new_name", "on_conflict" };
                },
                args =>
                {
                    string tagName = GetStringProp(args, "tag_name") ?? throw new ArgumentException("tag_name is required.");
                    string newName = GetStringProp(args, "new_name") ?? throw new ArgumentException("new_name is required.");
                    string onConflict = GetStringProp(args, "on_conflict") ?? throw new ArgumentException("on_conflict is required.");
                    if (onConflict != "fail")
                        throw new ArgumentException("on_conflict must be 'fail'; overwriting is not supported.");
                    string scriptReferences = GetStringProp(args, "script_references") ?? "report";
                    if (scriptReferences != "report" && scriptReferences != "replace")
                        throw new ArgumentException("script_references must be 'report' or 'replace'.");
                    var result = HmiTagOperations.RenameHmiTag(
                        tagName,
                        newName,
                        GetStringProp(args, "table_name"),
                        GetStringProp(args, "target_name"),
                        onConflict,
                        scriptReferences,
                        GetBoolProp(args, "dry_run", false));
                    return CallToolResult.Json(result, isError: !result.Success);
                });
        }
    }
}
