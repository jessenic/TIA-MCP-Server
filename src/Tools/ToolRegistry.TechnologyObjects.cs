using System;
using System.Collections.Generic;
using System.Text.Json;
using TiaOpennessMcp.Protocol;
using TiaOpennessMcp.Tia;

namespace TiaOpennessMcp.Tools
{
    // Technology objects (PID_Compact, PID_3Step, PID_Temp, ...): list, read parameters, create, write parameters.
    public partial class ToolRegistry
    {
        private const string TechnologyObjectPlcNameDescription = "Exact PLC software name (case-sensitive); required when the project has multiple PLCs.";
        private const string TechnologyObjectNameDescription = "Exact technology-object name (case-sensitive), e.g. 'PID_Temp_1'.";

        private void RegisterTechnologyObjectTools()
        {
            Register("tia_list_technology_objects",
                "List the technology objects of a PLC (the 'Technology objects' folder, including its subfolders) with system-library type, version, DB number and folder path. " +
                "Technology objects are not listed by tia_list_blocks. An instance DB of PID_Temp created from an SCL call is a plain instance DB, not a technology object, and does not appear here.",
                schema =>
                {
                    schema.Properties["plc_name"] = new ToolProperty { Type = "string", Description = TechnologyObjectPlcNameDescription };
                },
                args =>
                {
                    var result = TiaManager.Instance.ListTechnologyObjects(GetStringProp(args, "plc_name"));
                    return CallToolResult.Json(result, isError: !result.Success);
                });

            Register("tia_get_technology_object_parameters",
                "Read the parameters of one technology object by their 'Name in Openness' (TechnologicalInstanceDB.Parameters), with each current value as invariant text and its CLR type. " +
                "Use this before tia_set_technology_object_parameters to learn the exact names and types, which differ per object type and version (e.g. PID_Temp V1.1 on S7-1200 has no Config.ActivateHeating; that exists from V2.0).",
                schema =>
                {
                    schema.Properties["name"] = new ToolProperty { Type = "string", Description = TechnologyObjectNameDescription };
                    schema.Properties["filter"] = new ToolProperty { Type = "string", Description = "Optional case-sensitive name prefix, e.g. 'Config.' or 'Retain.CtrlParams.'." };
                    schema.Properties["plc_name"] = new ToolProperty { Type = "string", Description = TechnologyObjectPlcNameDescription };
                    schema.Required = new List<string> { "name" };
                },
                args =>
                {
                    string name = GetStringProp(args, "name") ?? throw new ArgumentException("name is required.");
                    var result = TiaManager.Instance.GetTechnologyObjectParameters(
                        GetStringProp(args, "plc_name"), name, GetStringProp(args, "filter"));
                    return CallToolResult.Json(result, isError: !result.Success);
                });

            Register("tia_create_technology_object",
                "Create a technology object in the PLC's 'Technology objects' folder (TechnologicalObjects.Create). " +
                "type is the Openness system-library name: PID_Temp, PID_Compact, PID_3Step (TO_PID_Temp, TO_PID_Compact and TO_PID_3Step are mapped to these); other names are passed to TIA unchanged and TIA rejects unsupported ones. " +
                "version is required, there is no automatic latest: S7-1200 (FW >= 4.2) supports PID_Temp 1.1, PID_Compact 2.3 or later and PID_3Step 2.3; check the TIA documentation for other CPUs. " +
                "Fails without changing anything if any technology object or block already has the name (case-insensitive); never overwrites or deletes. " +
                "A PID instance DB created from an SCL call is a plain instance DB, not a technology object: rename or delete it first or pick another name. " +
                "Creating the object does not call it: call it from a cyclic interrupt OB with the object's name as instance, e.g. \"PID_Temp_1\"(Setpoint := ..., Input := ...); hardware inputs and outputs are wired through those call parameters. " +
                "Motion and counting objects are created without any hardware interconnection.",
                schema =>
                {
                    schema.Properties["type"] = new ToolProperty { Type = "string", Description = "System-library name of the technology object, e.g. 'PID_Temp', 'PID_Compact' or 'PID_3Step'." };
                    schema.Properties["name"] = new ToolProperty { Type = "string", Description = "Name for the new technology object, e.g. 'PID_Temp_1'." };
                    schema.Properties["version"] = new ToolProperty { Type = "string", Description = "Technology-object version as major.minor, e.g. '1.1' or 'V1.1' for PID_Temp on S7-1200." };
                    schema.Properties["plc_name"] = new ToolProperty { Type = "string", Description = TechnologyObjectPlcNameDescription };
                    schema.Required = new List<string> { "type", "name", "version" };
                },
                args =>
                {
                    string type = GetStringProp(args, "type") ?? throw new ArgumentException("type is required.");
                    string name = GetStringProp(args, "name") ?? throw new ArgumentException("name is required.");
                    string version = GetStringProp(args, "version") ?? throw new ArgumentException("version is required.");
                    var result = TiaManager.Instance.CreateTechnologyObject(GetStringProp(args, "plc_name"), type, name, version);
                    return CallToolResult.Json(result, isError: !result.Success);
                });

            Register("tia_set_technology_object_parameters",
                "Write existing parameters of one technology object in place by their 'Name in Openness', e.g. Config.InputPerOn or Config.ActivateCooling for PID_Temp. " +
                "Every value is a string that is converted to the CLR type of the parameter's current value (Boolean true/false, integers and floating point in invariant culture). " +
                "All names and values are validated before anything is written; then they are written in order and each is verified by read-back. " +
                "Not transactional: if a later write fails, earlier writes stay and the result has Partial=true. Read the names first with tia_get_technology_object_parameters.",
                schema =>
                {
                    schema.Properties["name"] = new ToolProperty { Type = "string", Description = TechnologyObjectNameDescription };
                    schema.Properties["parameters"] = new ToolProperty
                    {
                        Type = "array",
                        Description = "Non-empty list of parameters to write, in order; each name at most once.",
                        Items = new ToolProperty
                        {
                            Type = "object",
                            Properties = new Dictionary<string, ToolProperty>
                            {
                                ["name"] = new ToolProperty { Type = "string", Description = "Exact parameter 'Name in Openness', e.g. 'Config.InputPerOn'." },
                                ["value"] = new ToolProperty { Type = "string", Description = "New value as an invariant-culture JSON string, e.g. true, 5 or 0.5 sent as a string." }
                            },
                            Required = new List<string> { "name", "value" },
                            AdditionalProperties = false
                        }
                    };
                    schema.Properties["plc_name"] = new ToolProperty { Type = "string", Description = TechnologyObjectPlcNameDescription };
                    schema.Required = new List<string> { "name", "parameters" };
                },
                args =>
                {
                    string name = GetStringProp(args, "name") ?? throw new ArgumentException("name is required.");
                    var parameters = GetTechnologyObjectParameterWrites(args);
                    var result = TiaManager.Instance.SetTechnologyObjectParameters(GetStringProp(args, "plc_name"), name, parameters);
                    return CallToolResult.Json(result, isError: !result.Success);
                });
        }

        private static List<KeyValuePair<string, string>> GetTechnologyObjectParameterWrites(JsonElement? args)
        {
            if (!args.HasValue || args.Value.ValueKind != JsonValueKind.Object ||
                !args.Value.TryGetProperty("parameters", out JsonElement parameters) ||
                parameters.ValueKind != JsonValueKind.Array)
                throw new ArgumentException("parameters must be an array of {name, value} objects.");

            var list = new List<KeyValuePair<string, string>>();
            foreach (JsonElement item in parameters.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !item.TryGetProperty("name", out JsonElement nameElement) || nameElement.ValueKind != JsonValueKind.String ||
                    !item.TryGetProperty("value", out JsonElement valueElement))
                    throw new ArgumentException("Every parameters entry must be an object with a string 'name' and a 'value'.");

                string value = valueElement.ValueKind switch
                {
                    JsonValueKind.String => valueElement.GetString() ?? "",
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Number => valueElement.GetRawText(),
                    _ => throw new ArgumentException("A parameters entry 'value' must be a string, number or boolean.")
                };
                list.Add(new KeyValuePair<string, string>(nameElement.GetString() ?? "", value));
            }
            return list;
        }
    }
}
