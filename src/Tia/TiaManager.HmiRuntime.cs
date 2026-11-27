using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.Common;
using Siemens.Engineering.HmiUnified.RuntimeSettings;

namespace TiaOpennessMcp.Tia
{
    // WinCC Unified Runtime settings (HmiSoftware.RuntimeSettings): read-only overview and the start screen setter.
    public partial class TiaManager
    {
        private const string StartScreenPropertyName = "StartScreen";

        public HmiRuntimeSettingsInfo GetHmiRuntimeSettings(string? targetName = null)
        {
            HmiSoftware hmi = FindHmiSoftware(targetName);
            HmiRuntimeSetting settings = hmi.RuntimeSettings
                ?? throw new InvalidOperationException($"HMI Software '{hmi.Name}' has no Runtime settings object.");

            var info = new HmiRuntimeSettingsInfo { TargetSoftwareName = hmi.Name };
            info.StartScreen = ReadRuntimeSetting(info, "StartScreen", () => settings.StartScreen);
            info.ScreenResolution = ReadRuntimeSetting(info, "ScreenResolution", () => settings.ScreenResolution.ToString());
            info.AutoLogOffUrl = ReadRuntimeSetting(info, "AutoLogOffURL", () => settings.AutoLogOffURL);
            info.BitSelection = ReadRuntimeSetting<bool?>(info, "BitSelection", () => settings.BitSelection);
            info.BitSelectionStrategyForResourceLists = ReadRuntimeSetting(info, "BitSelectionStrategyForResourceLists",
                () => settings.BitSelectionStrategyForResourceLists.ToString());
            info.BitSelectionStrategyForTagDynamization = ReadRuntimeSetting(info, "BitSelectionStrategyForTagDynamization",
                () => settings.BitSelectionStrategyForTagDynamization.ToString());
            info.CentralInputHint = ReadRuntimeSetting(info, "CentralInputHint", () => settings.CentralInputHint.ToString());
            info.CentralPanning = ReadRuntimeSetting(info, "CentralPanning", () => settings.CentralPanning.ToString());
            info.CentralZooming = ReadRuntimeSetting(info, "CentralZooming", () => settings.CentralZooming.ToString());
            info.EnableLanguageCompatibleFontFamilies = ReadRuntimeSetting<bool?>(info, "EnableLanguageCompatibleFontFamilies",
                () => settings.EnableLanguageCompatibleFontFamilies);
            info.GmpEnabled = ReadRuntimeSetting<bool?>(info, "GMPEnabled", () => settings.GMPEnabled);
            info.GeneralEsigCommentsStrategy = ReadRuntimeSetting(info, "GeneralESIGCommentsStrategy",
                () => settings.GeneralESIGCommentsStrategy.ToString());

            if (info.StartScreen != null)
            {
                string startScreen = info.StartScreen;
                info.StartScreenExists = EnumerateHmiScreens(hmi.Screens, hmi.ScreenGroups)
                    .Any(screen => string.Equals(screen.Name, startScreen, StringComparison.Ordinal));
            }

            try
            {
                info.Validation = MapRuntimeValidation(settings.Validate());
            }
            catch (Exception ex) when (ex is EngineeringException || ex is COMException || ex is InvalidOperationException)
            {
                info.ReadErrors["Validate"] = $"{ex.GetType().Name}: {ex.Message}";
            }
            return info;
        }

        public HmiStartScreenResult SetHmiStartScreen(
            string screenName,
            string? targetName,
            string? expectedCurrentStartScreen,
            string onConflict)
        {
            // Preconditions fail as tool errors before anything is written.
            ValidateHmiName(screenName, nameof(screenName));
            RequireFailConflictPolicy(onConflict);

            HmiSoftware hmi = FindHmiSoftware(targetName);
            HmiRuntimeSetting settings = hmi.RuntimeSettings
                ?? throw new InvalidOperationException($"HMI Software '{hmi.Name}' has no Runtime settings object.");
            string oldStartScreen = settings.StartScreen ?? string.Empty;
            RequireExpectedStartScreen(oldStartScreen, expectedCurrentStartScreen);
            string exactName = ResolveExactScreenName(
                EnumerateHmiScreens(hmi.Screens, hmi.ScreenGroups).Select(screen => screen.Name), screenName);

            var result = new HmiStartScreenResult
            {
                TargetSoftwareName = hmi.Name,
                OldStartScreen = oldStartScreen,
                StartScreen = exactName
            };
            if (string.Equals(oldStartScreen, exactName, StringComparison.Ordinal))
            {
                result.Success = true;
                result.Status = "unchanged";
                result.Message = $"'{exactName}' is already the start screen; nothing was written.";
                return result;
            }

            try
            {
                using (ExclusiveAccess access = _tiaPortal!.ExclusiveAccess("Set Unified HMI start screen"))
                using (Transaction transaction = access.Transaction(_project!, "Set Unified HMI start screen"))
                {
                    settings.StartScreen = exactName;

                    string readBack = settings.StartScreen ?? string.Empty;
                    if (!string.Equals(readBack, exactName, StringComparison.Ordinal))
                    {
                        // Leaving the transaction without CommitOnDispose rolls the change back.
                        RestoreStartScreen(settings, oldStartScreen, result);
                        result.StartScreen = readBack;
                        result.Status = "verification_failed";
                        result.Message = $"Start screen read back as '{readBack}' instead of '{exactName}'; the change was not committed.";
                        return result;
                    }

                    result.Validation = MapRuntimeValidation(settings.Validate());
                    List<HmiRuntimeValidationEntry> startScreenErrors = GetStartScreenValidationErrors(result.Validation);
                    if (startScreenErrors.Count > 0)
                    {
                        RestoreStartScreen(settings, oldStartScreen, result);
                        result.StartScreen = oldStartScreen;
                        result.Status = "validation_failed";
                        result.Message =
                            $"Runtime settings validation rejected start screen '{exactName}': " +
                            string.Join("; ", startScreenErrors.SelectMany(entry => entry.Errors)) +
                            $". The previous start screen '{oldStartScreen}' was restored and nothing was committed.";
                        return result;
                    }

                    transaction.CommitOnDispose();
                }
            }
            catch (Exception ex) when (ex is EngineeringException || ex is COMException || ex is InvalidOperationException || ex is ArgumentException)
            {
                result.Success = false;
                result.Status = "api_failure";
                result.Message = $"{ex.GetType().Name}: {ex.Message}";
                result.Warnings.Add("The API call may have changed project state; read the start screen back with hmi_get_runtime_settings before retrying.");
                return result;
            }

            result.Success = true;
            result.Status = "changed";
            int otherErrors = result.Validation.Count(entry => entry.Errors.Count > 0);
            result.Message = otherErrors == 0
                ? "Start screen changed in place and verified by read-back."
                : $"Start screen changed in place and verified by read-back. Runtime settings validation reports errors on {otherErrors} other setting(s); see Validation.";
            return result;
        }

        private static void RestoreStartScreen(HmiRuntimeSetting settings, string oldStartScreen, HmiStartScreenResult result)
        {
            try
            {
                settings.StartScreen = oldStartScreen;
            }
            catch (Exception ex) when (ex is EngineeringException || ex is COMException || ex is InvalidOperationException || ex is ArgumentException)
            {
                result.Warnings.Add($"Restoring the previous start screen '{oldStartScreen}' failed ({ex.GetType().Name}: {ex.Message}); the transaction was not committed.");
            }
        }

        private static T? ReadRuntimeSetting<T>(HmiRuntimeSettingsInfo info, string propertyName, Func<T> getter)
        {
            try
            {
                return getter();
            }
            catch (Exception ex) when (ex is EngineeringException || ex is COMException || ex is InvalidOperationException || ex is NotSupportedException)
            {
                info.ReadErrors[propertyName] = $"{ex.GetType().Name}: {ex.Message}";
                return default;
            }
        }

        private static List<HmiRuntimeValidationEntry> MapRuntimeValidation(IEnumerable<HmiValidationResult>? results)
        {
            var entries = new List<HmiRuntimeValidationEntry>();
            if (results == null) return entries;
            foreach (HmiValidationResult result in results)
            {
                if (result == null) continue;
                entries.Add(new HmiRuntimeValidationEntry
                {
                    PropertyName = result.PropertyName ?? string.Empty,
                    Errors = result.Errors?.Select(error => error?.ToString() ?? string.Empty).ToList() ?? new List<string>(),
                    Warnings = result.Warnings?.Select(warning => warning?.ToString() ?? string.Empty).ToList() ?? new List<string>()
                });
            }
            return entries;
        }

        // Validation entries that block a start-screen change: errors reported on the StartScreen property itself.
        // Errors on other settings are pre-existing project state and are only reported.
        internal static List<HmiRuntimeValidationEntry> GetStartScreenValidationErrors(IEnumerable<HmiRuntimeValidationEntry> entries)
        {
            return entries
                .Where(entry => string.Equals(entry.PropertyName, StartScreenPropertyName, StringComparison.OrdinalIgnoreCase) &&
                    entry.Errors.Count > 0)
                .ToList();
        }

        // Optimistic-concurrency guard: when the caller states the current start screen, it must match exactly.
        internal static void RequireExpectedStartScreen(string currentStartScreen, string? expectedCurrentStartScreen)
        {
            if (expectedCurrentStartScreen == null) return;
            if (!string.Equals(currentStartScreen ?? string.Empty, expectedCurrentStartScreen, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The start screen is currently '{currentStartScreen}', not the expected '{expectedCurrentStartScreen}'; nothing was changed.");
            }
        }

        // Resolves the requested screen to exactly one existing screen name (case-sensitive), so the stored start
        // screen always matches a real screen.
        internal static string ResolveExactScreenName(IEnumerable<string> screenNames, string requested)
        {
            if (string.IsNullOrWhiteSpace(requested))
                throw new ArgumentException("A screen name is required.", nameof(requested));

            List<string> names = screenNames.ToList();
            List<string> exact = names.Where(name => string.Equals(name, requested, StringComparison.Ordinal)).ToList();
            if (exact.Count == 1) return exact[0];
            if (exact.Count > 1)
                throw new InvalidOperationException($"Screen name '{requested}' is ambiguous across HMI screen groups.");

            List<string> caseOnly = names.Where(name => string.Equals(name, requested, StringComparison.OrdinalIgnoreCase)).ToList();
            if (caseOnly.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Screen '{requested}' was not found; screen names are case-sensitive. Did you mean '{string.Join("', '", caseOnly)}'?");
            }

            string available = names.Count == 0
                ? "none"
                : string.Join(", ", names.Take(20)) + (names.Count > 20 ? $", ... ({names.Count} screens)" : string.Empty);
            throw new InvalidOperationException($"Screen '{requested}' was not found. Available screens: {available}.");
        }
    }

    public sealed class HmiRuntimeSettingsInfo
    {
        public string TargetSoftwareName { get; set; } = string.Empty;
        public string? StartScreen { get; set; }
        // Informational: true when a screen with exactly this name exists in the HMI (root or any screen group).
        public bool? StartScreenExists { get; set; }
        public string? ScreenResolution { get; set; }
        public string? AutoLogOffUrl { get; set; }
        public bool? BitSelection { get; set; }
        public string? BitSelectionStrategyForResourceLists { get; set; }
        public string? BitSelectionStrategyForTagDynamization { get; set; }
        public string? CentralInputHint { get; set; }
        public string? CentralPanning { get; set; }
        public string? CentralZooming { get; set; }
        public bool? EnableLanguageCompatibleFontFamilies { get; set; }
        public bool? GmpEnabled { get; set; }
        public string? GeneralEsigCommentsStrategy { get; set; }
        public Dictionary<string, string> ReadErrors { get; set; } = new Dictionary<string, string>();
        public List<HmiRuntimeValidationEntry> Validation { get; set; } = new List<HmiRuntimeValidationEntry>();
    }

    public sealed class HmiRuntimeValidationEntry
    {
        public string PropertyName { get; set; } = string.Empty;
        public List<string> Errors { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class HmiStartScreenResult
    {
        public bool Success { get; set; }
        public string Operation { get; set; } = "set_start_screen";
        public string Status { get; set; } = string.Empty;
        public string TargetSoftwareName { get; set; } = string.Empty;
        public string OldStartScreen { get; set; } = string.Empty;
        public string StartScreen { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public List<string> Warnings { get; set; } = new List<string>();
        public List<HmiRuntimeValidationEntry> Validation { get; set; } = new List<HmiRuntimeValidationEntry>();
    }
}
