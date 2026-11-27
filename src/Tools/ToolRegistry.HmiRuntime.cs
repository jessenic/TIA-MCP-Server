using System;
using System.Collections.Generic;
using TiaOpennessMcp.Protocol;
using TiaOpennessMcp.Tia;

namespace TiaOpennessMcp.Tools
{
    // WinCC Unified Runtime settings tools (HmiSoftware.RuntimeSettings).
    public partial class ToolRegistry
    {
        private const string HmiRuntimeTargetDescription = "Exact HMI device, device-item, or software name; required when multiple targets exist.";

        private void RegisterHmiRuntimeTools()
        {
            Register("hmi_get_runtime_settings",
                "Read the WinCC Unified Runtime settings of an HMI: start screen (and whether that screen exists), screen resolution, auto log-off URL, central input hint/panning/zooming, bit-selection settings, language-compatible fonts, GMP and e-signature comment settings, plus the Runtime settings validation messages. Read-only.",
                schema =>
                {
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = HmiRuntimeTargetDescription };
                },
                args => CallToolResult.Json(_hmiToolOperations.Value.GetHmiRuntimeSettings(GetStringProp(args, "target_name"))));

            Register("hmi_set_start_screen",
                "Set the WinCC Unified Runtime start screen in place to an existing screen (exact, case-sensitive name; screens in screen groups are found too). No screens or screen items are changed. " +
                "The value is read back and the Runtime settings are validated; if validation reports a start-screen error the previous start screen is restored and the call fails. " +
                "For a frame screen with a screen window, set the frame screen as the start screen.",
                schema =>
                {
                    schema.Properties["screen_name"] = new ToolProperty { Type = "string", Description = "Exact name of the existing screen to start Runtime with." };
                    schema.Properties["target_name"] = new ToolProperty { Type = "string", Description = HmiRuntimeTargetDescription };
                    schema.Properties["expected_current_start_screen"] = new ToolProperty { Type = "string", Description = "Optional guard: the start screen you expect is set now (exact name, '' for none). If it differs, the call fails and nothing changes." };
                    schema.Properties["on_conflict"] = new ToolProperty { Type = "string", Enum = new List<string> { "fail" }, Description = "Required conflict behavior; only 'fail' is supported." };
                    schema.Required = new List<string> { "screen_name", "on_conflict" };
                },
                args =>
                {
                    string screenName = GetStringProp(args, "screen_name") ?? throw new ArgumentException("screen_name is required.");
                    string onConflict = GetStringProp(args, "on_conflict") ?? throw new ArgumentException("on_conflict is required.");
                    HmiStartScreenResult result = _hmiToolOperations.Value.SetHmiStartScreen(
                        screenName,
                        GetStringProp(args, "target_name"),
                        GetStringProp(args, "expected_current_start_screen"),
                        onConflict);
                    return CallToolResult.Json(result, isError: !result.Success);
                });
        }
    }
}
