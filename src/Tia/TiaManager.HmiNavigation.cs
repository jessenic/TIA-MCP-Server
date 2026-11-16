using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.Json;
using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.Enum;
using Siemens.Engineering.HmiUnified.UI.Events;
using Siemens.Engineering.HmiUnified.UI.Screens;
using Siemens.Engineering.HmiUnified.UI.Widgets;

namespace TiaOpennessMcp.Tia
{
    public partial class TiaManager
    {
        public HmiNavigationButtonMutationResult CreateHmiNavigationButton(
            string targetName,
            string screenName,
            string itemName,
            string text,
            string destinationScreen,
            int left,
            int top,
            uint width,
            uint height,
            string? backColor,
            string? foreColor,
            string onConflict)
        {
            ValidateNavigationName(targetName, nameof(targetName));
            ValidateNavigationName(screenName, nameof(screenName));
            ValidateNavigationName(itemName, nameof(itemName));
            ValidateNavigationName(destinationScreen, nameof(destinationScreen));
            if (string.IsNullOrWhiteSpace(text) || !string.Equals(text, text.Trim(), StringComparison.Ordinal) ||
                text.Any(char.IsControl))
                throw new ArgumentException("text must not be empty, contain edge whitespace, or contain control characters.", nameof(text));
            if (left < 0 || top < 0)
                throw new ArgumentOutOfRangeException(nameof(left), "left and top must be non-negative.");
            if (width == 0 || height == 0)
                throw new ArgumentOutOfRangeException(nameof(width), "width and height must be greater than zero.");
            if (!string.Equals(onConflict, "fail", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("on_conflict must be 'fail'.", nameof(onConflict));

            Color? resolvedBackColor = ParseNavigationButtonColor(backColor, nameof(backColor));
            Color? resolvedForeColor = ParseNavigationButtonColor(foreColor, nameof(foreColor));

            List<HmiTargetInfo> exactTargets = ListHmiTargets()
                .Where(candidate => string.Equals(candidate.SoftwareName, targetName, StringComparison.Ordinal))
                .ToList();
            if (exactTargets.Count != 1)
                throw new InvalidOperationException(
                    $"target_name must exactly identify one HMI software name; '{targetName}' matched {exactTargets.Count} targets.");

            HmiSoftware hmi = FindHmiSoftware(targetName);
            if (!string.Equals(hmi.Name, targetName, StringComparison.Ordinal))
                throw new InvalidOperationException($"Resolved HMI software name '{hmi.Name}' does not exactly match target_name '{targetName}'.");

            HmiScreen source = FindExactNavigationScreen(hmi, screenName, "screen_name");
            HmiScreen destination = FindExactNavigationScreen(hmi, destinationScreen, "destination_screen");

            if (source.ScreenItems.Any(item =>
                string.Equals(item.Name, itemName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException(
                    $"Screen item '{itemName}' already exists on screen '{source.Name}'; refusing to overwrite it.");

            string script = "HMIRuntime.UI.SysFct.ChangeScreen(" +
                JsonSerializer.Serialize(destination.Name) + ", \"~\");";
            string formattedText = "<body><p>" +
                (SecurityElement.Escape(text) ?? string.Empty) + "</p></body>";

            var result = new HmiNavigationButtonMutationResult
            {
                TargetName = hmi.Name,
                ScreenName = source.Name,
                ItemName = itemName,
                DestinationScreen = destination.Name,
                Text = text,
                NavigationAction = "HMIRuntime.UI.SysFct.ChangeScreen",
                Script = script,
                Left = left,
                Top = top,
                Width = width,
                Height = height,
                BackColor = resolvedBackColor.HasValue ? ToNavigationColorString(resolvedBackColor.Value) : null,
                ForeColor = resolvedForeColor.HasValue ? ToNavigationColorString(resolvedForeColor.Value) : null
            };

            bool mutationAttempted = false;
            string failureStatus = "api_failure";
            try
            {
                using (ExclusiveAccess access = _tiaPortal!.ExclusiveAccess("Create Unified HMI navigation button"))
                using (Transaction transaction = access.Transaction(_project!, "Create Unified HMI navigation button"))
                {
                    mutationAttempted = true;
                    HmiButton button = source.ScreenItems.Create<HmiButton>(itemName);
                    button.Left = left;
                    button.Top = top;
                    button.Width = width;
                    button.Height = height;
                    if (resolvedBackColor.HasValue) button.BackColor = resolvedBackColor.Value;
                    if (resolvedForeColor.HasValue) button.ForeColor = resolvedForeColor.Value;

                    if (button.Text.Items.Count == 0)
                    {
                        failureStatus = "verification_failed";
                        throw new InvalidOperationException($"Button '{itemName}' has no active text language.");
                    }
                    foreach (var languageText in button.Text.Items)
                        languageText.Text = formattedText;

                    HmiButtonEventHandler? eventHandler = button.EventHandlers.Find(HmiButtonEventType.Tapped);
                    if (eventHandler != null)
                    {
                        failureStatus = "conflict";
                        throw new InvalidOperationException(
                            $"New button '{itemName}' unexpectedly contains a Tapped handler; refusing to replace it.");
                    }

                    eventHandler = button.EventHandlers.Create(HmiButtonEventType.Tapped);
                    eventHandler.Script.ScriptCode = script;
                    var syntaxCheck = eventHandler.Script.SyntaxCheck();
                    result.SyntaxErrors = ((IEnumerable<string>)syntaxCheck.Errors).ToList();
                    result.SyntaxWarnings = ((IEnumerable<string>)syntaxCheck.Warnings).ToList();
                    if (result.SyntaxErrors.Count > 0)
                    {
                        failureStatus = "verification_failed";
                        throw new InvalidOperationException(
                            "Navigation button script failed the V21 HMI script syntax check.");
                    }

                    HmiScreenItemBase readBackItem = source.ScreenItems.Find(itemName)
                        ?? throw new InvalidOperationException($"Created button '{itemName}' was not found on read-back.");
                    if (!(readBackItem is HmiButton readBackButton) ||
                        !string.Equals(readBackButton.Name, itemName, StringComparison.Ordinal) ||
                        readBackButton.Left != left || readBackButton.Top != top ||
                        readBackButton.Width != width || readBackButton.Height != height ||
                        readBackButton.Text.Items.Count == 0 ||
                        !readBackButton.Text.Items.All(value => string.Equals(value.Text, formattedText, StringComparison.Ordinal)))
                    {
                        failureStatus = "verification_failed";
                        throw new InvalidOperationException($"Created button '{itemName}' did not match the requested properties on read-back.");
                    }

                    HmiButtonEventHandler? readBackHandler = readBackButton.EventHandlers.Find(HmiButtonEventType.Tapped);
                    if (readBackHandler == null ||
                        !string.Equals(readBackHandler.Script.ScriptCode, script, StringComparison.Ordinal))
                    {
                        failureStatus = "verification_failed";
                        throw new InvalidOperationException($"Navigation action for button '{itemName}' did not match on read-back.");
                    }

                    if ((resolvedBackColor.HasValue && readBackButton.BackColor != resolvedBackColor.Value) ||
                        (resolvedForeColor.HasValue && readBackButton.ForeColor != resolvedForeColor.Value))
                    {
                        failureStatus = "verification_failed";
                        throw new InvalidOperationException($"Button '{itemName}' colors did not match on read-back.");
                    }

                    transaction.CommitOnDispose();
                }

                result.Success = true;
                result.Status = "created";
                result.Message = "Navigation button and Tapped action were created and verified by read-back.";
                return result;
            }
            catch (Exception ex) when (mutationAttempted)
            {
                result.Success = false;
                result.Status = failureStatus;
                result.Partial = true;
                result.Message = ex.Message;
                result.Warning = "The operation may have partially changed the project. Inspect the screen item before retrying; no rollback is claimed.";
                if (ex is EngineeringException || ex is COMException)
                    result.Status = "api_failure";
                return result;
            }
        }

        private static HmiScreen FindExactNavigationScreen(HmiSoftware hmi, string name, string parameterName)
        {
            HmiScreen screen = FindHmiScreen(hmi, name)
                ?? throw new InvalidOperationException($"{parameterName} '{name}' was not found.");
            if (!string.Equals(screen.Name, name, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"{parameterName} must exactly match the screen name; '{name}' matched '{screen.Name}'.");
            return screen;
        }

        private static void ValidateNavigationName(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
                value.Any(char.IsControl))
                throw new ArgumentException(
                    $"{parameterName} must not be empty, contain edge whitespace, or contain control characters.",
                    parameterName);
        }

        private static Color? ParseNavigationButtonColor(string? value, string parameterName)
        {
            string color = value ?? string.Empty;
            if (color.Length == 0)
                return null;

            bool validHex = (color.Length == 7 || color.Length == 9) &&
                color[0] == '#' && color.Substring(1).All(Uri.IsHexDigit);
            bool validName = new[]
            {
                "white", "gray", "grey", "red", "green", "blue", "transparent"
            }.Any(name => string.Equals(name, color, StringComparison.OrdinalIgnoreCase));
            if (!validHex && !validName)
                throw new ArgumentException(
                    $"{parameterName} must be a supported named color or #RRGGBB/#AARRGGBB value.", parameterName);

            return ParseColor(color);
        }

        private static string ToNavigationColorString(Color color)
        {
            return color.A == 255
                ? ColorToHex(color)
                : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }
    }

    public sealed class HmiNavigationButtonMutationResult
    {
        public bool Success { get; set; }
        public string Status { get; set; } = string.Empty;
        public bool Partial { get; set; }
        public string TargetName { get; set; } = string.Empty;
        public string ScreenName { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string DestinationScreen { get; set; } = string.Empty;
        public string NavigationAction { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string Script { get; set; } = string.Empty;
        public int Left { get; set; }
        public int Top { get; set; }
        public uint Width { get; set; }
        public uint Height { get; set; }
        public string? BackColor { get; set; }
        public string? ForeColor { get; set; }
        public List<string> SyntaxErrors { get; set; } = new List<string>();
        public List<string> SyntaxWarnings { get; set; } = new List<string>();
        public string Message { get; set; } = string.Empty;
        public string? Warning { get; set; }
    }
}
