using System;
using System.Collections.Generic;
using TiaOpennessMcp.Protocol;
using TiaOpennessMcp.Tia;

namespace TiaOpennessMcp.Tools
{
    // PLC block metadata: OB attribute setter and read-only compile state.
    public partial class ToolRegistry
    {
        private void RegisterPlcMetaTools()
        {
            Register("tia_set_block_attribute",
                "Set one allow-listed attribute of an OB in place (PlcBlock.SetAttribute; the block is not deleted or re-imported): CyclicTime (cyclic interrupt interval) and PhaseOffset of a cyclic interrupt OB, or PriorityNumber. " +
                "Time values need an explicit unit 'ms' or 'us'; Openness and SimaticML store microseconds (100 ms = 100000 us). " +
                "expected_current_value (same unit) makes the call fail with 'conflict' when the current value differs. The attribute is written only when TIA Portal lists it as writable for that OB (otherwise status 'not_supported'). The new value is read back; the project is not compiled or saved. F-CPU safety OBs are not supported.",
                schema =>
                {
                    schema.Properties["block_name"] = new ToolProperty { Type = "string", Description = "Exact OB name, e.g. 'Cyclic interrupt'." };
                    schema.Properties["attribute_name"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Attribute to set. CyclicTime and PhaseOffset only on cyclic interrupt OBs (SecondaryType CyclicInterrupt).",
                        Enum = new List<string>(TiaManager.SettableBlockAttributes)
                    };
                    schema.Properties["value"] = new ToolProperty { Type = "integer", Description = "New value as an integer, in 'unit' for CyclicTime and PhaseOffset. CyclicTime 1..60000 ms, PhaseOffset smaller than CyclicTime." };
                    schema.Properties["unit"] = new ToolProperty
                    {
                        Type = "string",
                        Description = "Unit of value and expected_current_value: required for CyclicTime and PhaseOffset, must be omitted for PriorityNumber.",
                        Enum = new List<string> { "ms", "us" }
                    };
                    schema.Properties["expected_current_value"] = new ToolProperty { Type = "integer", Description = "Optional guard: the value you expect the attribute to have now, in the same unit." };
                    schema.Properties["plc_name"] = new ToolProperty { Type = "string", Description = "Exact PLC software name; required when the project has multiple PLCs." };
                    schema.Properties["group_path"] = new ToolProperty { Type = "string", Description = "Exact block-group path, required when the block name is duplicated. Use an empty string for the root group." };
                    schema.Required = new List<string> { "block_name", "attribute_name", "value" };
                },
                args =>
                {
                    string blockName = GetStringProp(args, "block_name") ?? throw new ArgumentException("block_name is required.");
                    string attributeName = GetStringProp(args, "attribute_name") ?? throw new ArgumentException("attribute_name is required.");
                    long value = GetRequiredIntegerProp(args, "value");
                    long? expected = GetOptionalIntegerProp(args, "expected_current_value");
                    var result = TiaManager.Instance.SetBlockAttribute(
                        GetStringProp(args, "plc_name"),
                        blockName,
                        GetStringProp(args, "group_path"),
                        attributeName,
                        value,
                        GetStringProp(args, "unit"),
                        expected);
                    return CallToolResult.Json(result, isError: !result.Success);
                });

            Register("tia_get_compile_state",
                "List the compile state of PLC blocks and PLC data types without compiling (read-only): IsConsistent, IsKnowHowProtected, CompileDate, ModifiedDate, CodeModifiedDate and InterfaceModifiedDate per block, IsConsistent per type, and totals. " +
                "IsConsistent is null when TIA does not report it. Openness cannot read the warnings TIA stored from an earlier compile.",
                schema =>
                {
                    schema.Properties["plc_name"] = new ToolProperty { Type = "string", Description = "Exact PLC software name; required when the project has multiple PLCs." };
                    schema.Properties["group_path"] = new ToolProperty { Type = "string", Description = "Exact block-group path to list (with its subgroups); omit or use an empty string for all blocks. Types are always listed from the root." };
                    schema.Properties["only_inconsistent"] = new ToolProperty { Type = "boolean", Description = "List only blocks and types that are not consistent or whose state is unknown (default false). Totals always count everything.", Default = false };
                    schema.Properties["include_types"] = new ToolProperty { Type = "boolean", Description = "Also list PLC data types (default true).", Default = true };
                },
                args => CallToolResult.Json(TiaManager.Instance.GetCompileState(
                    GetStringProp(args, "plc_name"),
                    GetStringProp(args, "group_path"),
                    GetBoolProp(args, "only_inconsistent", false),
                    GetBoolProp(args, "include_types", true))));
        }

        private static long GetRequiredIntegerProp(System.Text.Json.JsonElement? element, string propName)
        {
            return GetOptionalIntegerProp(element, propName) ?? throw new ArgumentException($"{propName} is required.");
        }

        private static long? GetOptionalIntegerProp(System.Text.Json.JsonElement? element, string propName)
        {
            if (!element.HasValue || element.Value.ValueKind != System.Text.Json.JsonValueKind.Object ||
                !element.Value.TryGetProperty(propName, out var property) ||
                property.ValueKind == System.Text.Json.JsonValueKind.Null)
                return null;

            if (property.ValueKind != System.Text.Json.JsonValueKind.Number || !property.TryGetInt64(out long value))
                throw new ArgumentException($"{propName} must be an integer.");
            return value;
        }
    }
}
