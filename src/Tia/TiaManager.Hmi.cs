using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Text;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiAlarm;
using Siemens.Engineering.HmiUnified.HmiAlarm.HmiAlarmCommon;
using Siemens.Engineering.HmiUnified.HmiTags;
using Siemens.Engineering.HmiUnified.UI.Dynamization;
using Siemens.Engineering.HmiUnified.UI.Dynamization.Tag;
using Siemens.Engineering.HmiUnified.UI.Enum;
using Siemens.Engineering.HmiUnified.UI.Screens;
using Siemens.Engineering.HmiUnified.UI.ScreenGroup;
using Siemens.Engineering.HmiUnified.UI.Shapes;
using Siemens.Engineering.HmiUnified.UI.Widgets;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.HmiLogging;
using Siemens.Engineering.HmiUnified.LoggingTags;
using Siemens.Engineering.HmiUnified.HmiConnections;
using Siemens.Engineering.HW.CommunicationConnections;

namespace TiaOpennessMcp.Tia
{
    public partial class TiaManager
    {
        #region HMI Target Resolution

        public List<HmiTargetInfo> ListHmiTargets()
        {
            EnsureProjectOpen();
            var list = new List<HmiTargetInfo>();
            foreach (Device dev in _project!.Devices)
            {
                FindHmiInDevice(dev, list);
            }
            return list;
        }

        private void FindHmiInDevice(Device dev, List<HmiTargetInfo> list)
        {
            FindHmiInDeviceItems(dev.DeviceItems, dev.Name, list);
        }

        private static void FindHmiInDeviceItems(IEnumerable<DeviceItem> items, string deviceName, List<HmiTargetInfo> list)
        {
            foreach (DeviceItem item in items)
            {
                var sc = item.GetService<SoftwareContainer>();
                if (sc != null && sc.Software is HmiSoftware hmiSw)
                {
                    list.Add(new HmiTargetInfo
                    {
                        DeviceName = deviceName,
                        DeviceItemName = item.Name,
                        SoftwareName = hmiSw.Name,
                        ScreenCount = EnumerateHmiScreens(hmiSw.Screens, hmiSw.ScreenGroups).Count(),
                        TagTableCount = hmiSw.TagTables.Count
                    });
                }
                FindHmiInDeviceItems(item.DeviceItems, deviceName, list);
            }
        }

        public HmiSoftware FindHmiSoftware(string? targetName = null)
        {
            EnsureProjectOpen();
            var candidates = new List<HmiTargetInfo>();
            foreach (Device dev in _project!.Devices)
                FindHmiInDevice(dev, candidates);

            var matches = string.IsNullOrWhiteSpace(targetName)
                ? candidates
                : candidates.Where(candidate =>
                    string.Equals(candidate.DeviceName, targetName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(candidate.DeviceItemName, targetName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(candidate.SoftwareName, targetName, StringComparison.OrdinalIgnoreCase)).ToList();

            if (matches.Count == 0)
            {
                throw new InvalidOperationException($"No HMI Software target found matching '{targetName ?? "(any)"}'.");
            }
            if (matches.Count > 1)
            {
                throw new InvalidOperationException(
                    $"HMI target selector '{targetName ?? "(any)"}' matched multiple targets: {string.Join(", ", matches.Select(m => m.SoftwareName))}.");
            }

            foreach (Device dev in _project.Devices)
            {
                HmiSoftware? software = FindHmiSoftwareInDeviceItems(dev.DeviceItems, dev.Name, matches[0]);
                if (software != null) return software;
            }
            throw new InvalidOperationException($"HMI Software target '{matches[0].SoftwareName}' could not be resolved.");
        }

        private static HmiSoftware? FindHmiSoftwareInDeviceItems(IEnumerable<DeviceItem> items, string deviceName, HmiTargetInfo target)
        {
            foreach (DeviceItem item in items)
            {
                var sc = item.GetService<SoftwareContainer>();
                if (sc?.Software is HmiSoftware hmiSw &&
                    string.Equals(deviceName, target.DeviceName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.Name, target.DeviceItemName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(hmiSw.Name, target.SoftwareName, StringComparison.OrdinalIgnoreCase))
                {
                    return hmiSw;
                }

                var nested = FindHmiSoftwareInDeviceItems(item.DeviceItems, deviceName, target);
                if (nested != null) return nested;
            }
            return null;
        }

        private static IEnumerable<HmiScreen> EnumerateHmiScreens(HmiScreenComposition screens, HmiScreenGroupComposition groups)
        {
            foreach (HmiScreen screen in screens) yield return screen;
            foreach (HmiScreenGroup group in groups)
                foreach (HmiScreen screen in EnumerateHmiScreens(group.Screens, group.Groups))
                    yield return screen;
        }

        private static HmiScreen? FindHmiScreen(HmiSoftware hmiSw, string screenName)
        {
            var matches = EnumerateHmiScreens(hmiSw.Screens, hmiSw.ScreenGroups)
                .Where(screen => string.Equals(screen.Name, screenName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count > 1)
            {
                throw new InvalidOperationException($"Screen name '{screenName}' is ambiguous across HMI screen groups.");
            }
            return matches.FirstOrDefault();
        }

        #endregion

        #region Screen Management

        public List<HmiScreenSummary> ListHmiScreens(string? targetName = null)
        {
            var hmiSw = FindHmiSoftware(targetName);
            var list = new List<HmiScreenSummary>();
            foreach (HmiScreen s in EnumerateHmiScreens(hmiSw.Screens, hmiSw.ScreenGroups))
            {
                list.Add(new HmiScreenSummary
                {
                    Name = s.Name,
                    Width = s.Width,
                    Height = s.Height,
                    ItemCount = s.ScreenItems.Count
                });
            }
            return list;
        }

        public HmiScreenSummary CreateHmiScreen(string screenName, uint? width = null, uint? height = null, string? targetName = null)
        {
            var hmiSw = FindHmiSoftware(targetName);
            var screen = FindHmiScreen(hmiSw, screenName) ?? hmiSw.Screens.Create(screenName);
            if (width.HasValue) screen.Width = width.Value;
            if (height.HasValue) screen.Height = height.Value;

            return new HmiScreenSummary
            {
                Name = screen.Name,
                Width = screen.Width,
                Height = screen.Height,
                ItemCount = screen.ScreenItems.Count
            };
        }

        public void DeleteHmiScreen(string screenName, string? targetName = null)
        {
            var hmiSw = FindHmiSoftware(targetName);
            var screen = FindHmiScreen(hmiSw, screenName);
            if (screen == null)
            {
                throw new InvalidOperationException($"Screen '{screenName}' not found.");
            }
            screen.Delete();
        }

        public List<HmiScreenItemDetails> GetHmiScreenItems(string screenName, string? targetName = null)
        {
            var hmiSw = FindHmiSoftware(targetName);
            var screen = FindHmiScreen(hmiSw, screenName);
            if (screen == null)
            {
                throw new InvalidOperationException($"Screen '{screenName}' not found.");
            }

            var list = new List<HmiScreenItemDetails>();
            foreach (HmiScreenItemBase item in screen.ScreenItems)
            {
                var details = new HmiScreenItemDetails
                {
                    Name = item.Name,
                    Type = item.GetType().Name
                };

                // Position, Dimensions & Colors
                if (item is HmiWidgetBase w)
                {
                    try { details.Left = w.Left; } catch { }
                    try { details.Top = w.Top; } catch { }
                    try { details.Width = w.Width; } catch { }
                    try { details.Height = w.Height; } catch { }
                    try { details.BackColor = ColorToHex(w.BackColor); } catch { }
                    try { details.BorderColor = ColorToHex(w.BorderColor); } catch { }
                    try { details.BorderWidth = w.BorderWidth; } catch { }
                }
                else if (item is HmiRectangle rect)
                {
                    try { details.Left = rect.Left; } catch { }
                    try { details.Top = rect.Top; } catch { }
                    try { details.Width = rect.Width; } catch { }
                    try { details.Height = rect.Height; } catch { }
                    try { details.BackColor = ColorToHex(rect.BackColor); } catch { }
                    try { details.BorderColor = ColorToHex(rect.BorderColor); } catch { }
                    try { details.BorderWidth = rect.BorderWidth; } catch { }
                }
                else if (item is HmiCircle circle)
                {
                    try { details.CenterX = circle.CenterX; } catch { }
                    try { details.CenterY = circle.CenterY; } catch { }
                    try { details.Radius = circle.Radius; } catch { }
                    try { details.BackColor = ColorToHex(circle.BackColor); } catch { }
                    try { details.BorderColor = ColorToHex(circle.BorderColor); } catch { }
                    try { details.BorderWidth = circle.BorderWidth; } catch { }
                }

                // Text / Labels / Buttons
                if (item is HmiTextBox tb && tb.Text.Items.Count > 0)
                {
                    details.Text = tb.Text.Items[0].Text;
                }
                else if (item is HmiToggleSwitch toggleSwitch)
                {
                    if (toggleSwitch.Text.Items.Count > 0)
                    {
                        details.Text = toggleSwitch.Text.Items[0].Text;
                    }
                    var handler = toggleSwitch.EventHandlers.Find(HmiToggleSwitchEventType.Tapped);
                    if (handler != null)
                    {
                        details.TappedScript = handler.Script.ScriptCode;
                    }
                }
                else if (item is HmiButton btn && btn.Text.Items.Count > 0)
                {
                    details.Text = btn.Text.Items[0].Text;
                    var handler = btn.EventHandlers.Find(HmiButtonEventType.Tapped);
                    if (handler != null)
                    {
                        details.TappedScript = handler.Script.ScriptCode;
                    }
                }
                else if (item is HmiIOField io)
                {
                    details.IOFieldType = io.IOFieldType.ToString();
                }

                // Dynamizations
                foreach (var dyn in item.Dynamizations)
                {
                    var dynInfo = new DynamizationInfo
                    {
                        Property = dyn.PropertyName,
                        Type = dyn.GetType().Name
                    };
                    if (dyn is TagDynamization td)
                    {
                        dynInfo.TagName = td.Tag;
                        if (td.ValueConverter?.MappingTable != null)
                        {
                            dynInfo.ConditionType = td.ValueConverter.MappingTable.ConditionType.ToString();
                            foreach (var entry in td.ValueConverter.MappingTable.Entries)
                            {
                                if (entry is MappingTableEntryRange re)
                                {
                                    dynInfo.Ranges.Add(new RangeEntryDto
                                    {
                                        From = re.From != null ? Convert.ToDouble(re.From) : 0.0,
                                        To = re.To != null ? Convert.ToDouble(re.To) : 0.0,
                                        Value = re.Value?.ToString() ?? ""
                                    });
                                }
                                else if (entry is MappingTableEntryBitmask bitmask)
                                {
                                    dynInfo.BitmaskEntries.Add(new BitmaskEntryDto
                                    {
                                        BitDynamizationType = bitmask.BitDynamizationType.ToString(),
                                        Condition = bitmask.Condition,
                                        Relevant = bitmask.Relevant,
                                        Value = bitmask.Value?.ToString() ?? ""
                                    });
                                }
                            }
                        }
                    }
                    details.Dynamizations.Add(dynInfo);
                }

                list.Add(details);
            }

            return list;
        }

        public HmiEventScriptsDetails GetHmiEventScripts(
            string screenName,
            string? itemName = null,
            string? targetName = null)
        {
            var hmiSw = FindHmiSoftware(targetName);
            var screen = FindHmiScreen(hmiSw, screenName);
            if (screen == null)
                throw new InvalidOperationException($"Screen '{screenName}' not found.");

            object target = screen;
            string targetKind = "screen";
            string targetNameForResult = screen.Name;
            if (!string.IsNullOrWhiteSpace(itemName))
            {
                target = screen.ScreenItems.Find(itemName)
                    ?? throw new InvalidOperationException($"Item '{itemName}' not found on screen '{screenName}'.");
                targetKind = "item";
                targetNameForResult = ((HmiScreenItemBase)target).Name;
            }

            EventHandlerSurface surface = GetEventHandlerSurface(target);
            var result = new HmiEventScriptsDetails
            {
                ScreenName = screen.Name,
                TargetKind = targetKind,
                TargetName = targetNameForResult,
                SupportedEventTypes = surface.EventType.IsEnum
                    ? Enum.GetNames(surface.EventType).ToList()
                    : new List<string>()
            };

            if (surface.Handlers is System.Collections.IEnumerable handlers)
            {
                foreach (object handler in handlers)
                {
                    object? eventType = handler.GetType().GetProperty("EventType")?.GetValue(handler, null);
                    object? script = handler.GetType().GetProperty("Script")?.GetValue(handler, null);
                    object? scriptCode = script?.GetType().GetProperty("ScriptCode")?.GetValue(script, null);
                    if (eventType == null || scriptCode is not string code)
                        throw new InvalidOperationException(
                            $"An event handler on '{targetNameForResult}' does not expose readable event type and script code.");

                    result.Events.Add(new HmiEventScriptEntry
                    {
                        EventType = eventType.ToString()!,
                        Script = code
                    });
                }
            }
            else
            {
                throw new InvalidOperationException(
                    $"Event handlers on '{targetNameForResult}' are not enumerable through the V21 Openness API.");
            }

            return result;
        }

        public HmiEventScriptUpdateResult UpdateHmiEventScript(
            string screenName,
            string eventType,
            string scriptCode,
            string? itemName = null,
            string? targetName = null)
        {
            if (string.IsNullOrWhiteSpace(eventType))
                throw new ArgumentException("event_type is required.", nameof(eventType));
            if (scriptCode == null)
                throw new ArgumentNullException(nameof(scriptCode));

            var hmiSw = FindHmiSoftware(targetName);
            var screen = FindHmiScreen(hmiSw, screenName);
            if (screen == null)
                throw new InvalidOperationException($"Screen '{screenName}' not found.");

            object target = screen;
            string targetNameForResult = screen.Name;
            if (!string.IsNullOrWhiteSpace(itemName))
            {
                target = screen.ScreenItems.Find(itemName)
                    ?? throw new InvalidOperationException($"Item '{itemName}' not found on screen '{screenName}'.");
                targetNameForResult = ((HmiScreenItemBase)target).Name;
            }

            EventHandlerSurface surface = GetEventHandlerSurface(target);
            object eventValue;
            if (surface.EventType.IsEnum)
            {
                string? supportedName = Enum.GetNames(surface.EventType).FirstOrDefault(name =>
                    string.Equals(name, eventType, StringComparison.OrdinalIgnoreCase));
                if (supportedName == null)
                    throw new ArgumentException(
                        $"Unsupported event '{eventType}' for '{targetNameForResult}'. Supported events: " +
                        string.Join(", ", Enum.GetNames(surface.EventType)) + ".",
                        nameof(eventType));
                eventValue = Enum.Parse(surface.EventType, supportedName);
            }
            else
            {
                throw new InvalidOperationException(
                    $"The V21 Openness API exposes an unsupported event selector type '{surface.EventType.Name}'.");
            }

            HmiEventScriptsDetails readback;
            HmiEventScriptEntry? updatedEvent;
            object? script;
            List<string> errors;
            List<string> warnings;
            using (ExclusiveAccess access = _tiaPortal!.ExclusiveAccess("Update Unified HMI event script"))
            using (Transaction transaction = access.Transaction(_project!, "Update Unified HMI event script"))
            {
                object? handler = surface.Find?.Invoke(surface.Handlers, new[] { eventValue });
                if (handler == null)
                {
                    try
                    {
                        handler = surface.Create.Invoke(surface.Handlers, new[] { eventValue });
                    }
                    catch (TargetInvocationException ex) when (ex.InnerException != null)
                    {
                        throw new InvalidOperationException(
                            $"TIA could not create event '{eventType}' on '{targetNameForResult}': {ex.InnerException.Message}",
                            ex.InnerException);
                    }
                }

                script = handler?.GetType().GetProperty("Script")?.GetValue(handler, null);
                PropertyInfo? scriptCodeProperty = script?.GetType().GetProperty("ScriptCode");
                if (scriptCodeProperty?.CanWrite != true ||
                    scriptCodeProperty.SetMethod?.IsPublic != true)
                    throw new InvalidOperationException(
                        $"Event '{eventType}' on '{targetNameForResult}' does not expose writable script code.");

                scriptCodeProperty.SetValue(script, scriptCode, null);

                readback = GetHmiEventScripts(screen.Name, itemName, targetName);
                updatedEvent = readback.Events.FirstOrDefault(e =>
                    string.Equals(e.EventType, eventType, StringComparison.OrdinalIgnoreCase));
                if (updatedEvent == null || !string.Equals(updatedEvent.Script, scriptCode, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"Event script read-back verification failed for '{targetNameForResult}.{eventType}'.");

                errors = GetScriptDiagnostics(script, "Errors");
                warnings = GetScriptDiagnostics(script, "Warnings");
                transaction.CommitOnDispose();
            }

            return new HmiEventScriptUpdateResult
            {
                ScreenName = screen.Name,
                TargetKind = readback.TargetKind,
                TargetName = targetNameForResult,
                EventType = updatedEvent.EventType,
                Script = updatedEvent.Script,
                Errors = errors,
                Warnings = warnings
            };
        }

        private static EventHandlerSurface GetEventHandlerSurface(object target)
        {
            PropertyInfo? handlersProperty = target.GetType().GetProperty(
                "EventHandlers",
                BindingFlags.Instance | BindingFlags.Public);
            object? handlers = handlersProperty?.GetValue(target, null);
            if (handlers == null)
                throw new InvalidOperationException(
                    $"'{target.GetType().Name}' does not expose V21 event handlers.");

            MethodInfo? create = handlers.GetType().GetMethods().FirstOrDefault(method =>
                method.Name == "Create" && method.GetParameters().Length == 1 &&
                method.GetParameters()[0].ParameterType.IsEnum);
            if (create == null)
                throw new InvalidOperationException(
                    $"Event creation is not supported on '{target.GetType().Name}' by the V21 Openness API.");

            Type eventType = create.GetParameters()[0].ParameterType;
            MethodInfo? find = handlers.GetType().GetMethods().FirstOrDefault(method =>
                method.Name == "Find" && method.GetParameters().Length == 1 &&
                method.GetParameters()[0].ParameterType == eventType);
            return new EventHandlerSurface(handlers, create, find, eventType);
        }

        private static List<string> GetScriptDiagnostics(object? script, string propertyName)
        {
            MethodInfo? syntaxCheck = script?.GetType().GetMethod("SyntaxCheck", Type.EmptyTypes);
            object? diagnostics = syntaxCheck?.Invoke(script, null);
            object? values = diagnostics?.GetType().GetProperty(propertyName)?.GetValue(diagnostics, null);
            if (values is not System.Collections.IEnumerable enumerable)
                return new List<string>();

            return enumerable.Cast<object?>()
                .Where(value => value != null)
                .Select(value => value!.ToString()!)
                .ToList();
        }

        public int DeleteHmiScreenItems(string screenName, List<string>? itemNames = null, string? prefix = null, string? targetName = null)
        {
            var hmiSw = FindHmiSoftware(targetName);
            var screen = FindHmiScreen(hmiSw, screenName);
            if (screen == null)
            {
                throw new InvalidOperationException($"Screen '{screenName}' not found.");
            }

            var toDelete = new List<HmiScreenItemBase>();
            foreach (HmiScreenItemBase item in screen.ScreenItems)
            {
                bool match = false;
                if (itemNames != null && itemNames.Contains(item.Name, StringComparer.OrdinalIgnoreCase))
                    match = true;
                if (!string.IsNullOrEmpty(prefix) && item.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    match = true;

                if (match) toDelete.Add(item);
            }

            int count = toDelete.Count;
            foreach (var item in toDelete)
            {
                item.Delete();
            }
            return count;
        }

        #endregion

        #region Widget & Shape Builders

        public string AddOrUpdateLabel(
            string screenName,
            string name,
            string text,
            int left, int top, uint width, uint height,
            float fontSize = 11f,
            bool isBold = false,
            string align = "Center",
            string? foreColor = null,
            string? backColor = null,
            string? targetName = null)
        {
            var hmiSw = FindHmiSoftware(targetName);
            var screen = FindHmiScreen(hmiSw, screenName) ?? hmiSw.Screens.Create(screenName);

            var label = screen.ScreenItems.Find(name) as HmiTextBox ?? screen.ScreenItems.Create<HmiTextBox>(name);
            label.Left = left;
            label.Top = top;
            label.Width = width;
            label.Height = height;
            label.ReadOnly = true;
            label.BorderWidth = 0;
            label.VerticalTextAlignment = HmiVerticalAlignment.Center;

            if (Enum.TryParse<HmiHorizontalAlignment>(align, true, out var hAlign))
                label.HorizontalTextAlignment = hAlign;

            if (!string.IsNullOrEmpty(foreColor))
                label.ForeColor = ParseColor(foreColor) ?? label.ForeColor;
            if (!string.IsNullOrEmpty(backColor))
                label.BackColor = ParseColor(backColor) ?? label.BackColor;
            else
                label.BackColor = Color.Transparent;

            try
            {
                label.Font.Size = fontSize;
                if (isBold) label.Font.Weight = HmiFontWeight.Bold;
            }
            catch { }

            try
            {
                if (label.Text.Items.Count > 0)
                {
                    string formatted = FormatHtmlText(text);
                    foreach (var item in label.Text.Items)
                        item.Text = formatted;
                }
            }
            catch { }

            return $"Label '{name}' added/updated successfully on screen '{screenName}'.";
        }

        public ButtonBuildResult AddOrUpdateButton(
            string screenName,
            string name,
            string text,
            int left, int top, uint width, uint height,
            string? backColor = null,
            string? foreColor = null,
            string? pressedTag = null,
            string? tappedScript = null,
            string? targetName = null)
        {
            var hmiSw = FindHmiSoftware(targetName);
            var screen = FindHmiScreen(hmiSw, screenName) ?? hmiSw.Screens.Create(screenName);

            var btn = screen.ScreenItems.Find(name) as HmiButton ?? screen.ScreenItems.Create<HmiButton>(name);
            btn.Left = left;
            btn.Top = top;
            btn.Width = width;
            btn.Height = height;

            if (!string.IsNullOrEmpty(backColor)) btn.BackColor = ParseColor(backColor) ?? btn.BackColor;
            if (!string.IsNullOrEmpty(foreColor)) btn.ForeColor = ParseColor(foreColor) ?? btn.ForeColor;

            try
            {
                btn.Font.Size = 11f;
                btn.Font.Weight = HmiFontWeight.Bold;
            }
            catch { }

            try
            {
                if (btn.Text.Items.Count > 0)
                {
                    string formatted = FormatHtmlText(text);
                    foreach (var item in btn.Text.Items)
                        item.Text = formatted;
                }
            }
            catch { }

            if (!string.IsNullOrEmpty(pressedTag))
            {
                var pst = btn.PressedStateTags.Count > 0 ? btn.PressedStateTags[0] : btn.PressedStateTags.Create();
                pst.Tag = pressedTag;
            }

            var result = new ButtonBuildResult { ButtonName = name };

            if (!string.IsNullOrEmpty(tappedScript))
            {
                var handler = btn.EventHandlers.Find(HmiButtonEventType.Tapped) ?? btn.EventHandlers.Create(HmiButtonEventType.Tapped);
                handler.Script.ScriptCode = tappedScript;
                var chk = handler.Script.SyntaxCheck();
                result.Errors = ((IEnumerable<string>)chk.Errors).ToList();
                result.Warnings = ((IEnumerable<string>)chk.Warnings).ToList();
            }

            return result;
        }

        public string AddOrUpdateToggleSwitch(
            string screenName,
            string name,
            string offText,
            string onText,
            int left, int top, uint width, uint height,
            string processTag,
            string? targetName = null)
        {
            var hmiSw = FindHmiSoftware(targetName);
            var screen = FindHmiScreen(hmiSw, screenName) ?? hmiSw.Screens.Create(screenName);
            var existing = screen.ScreenItems.Find(name);
            if (existing != null && !(existing is HmiToggleSwitch))
                throw new InvalidOperationException($"Screen item '{name}' exists but is not an HMI toggle switch.");
            if (hmiSw.Tags.Find(processTag) == null)
                throw new InvalidOperationException($"HMI tag '{processTag}' not found.");

            var toggle = existing as HmiToggleSwitch ?? screen.ScreenItems.Create<HmiToggleSwitch>(name);
            toggle.Left = left;
            toggle.Top = top;
            toggle.Width = width;
            toggle.Height = height;

            if (toggle.Text.Items.Count == 0 || toggle.AlternateText.Items.Count == 0)
                throw new InvalidOperationException($"Toggle switch '{name}' has no active text language.");
            foreach (var value in toggle.Text.Items) value.Text = FormatHtmlText(offText);
            foreach (var value in toggle.AlternateText.Items) value.Text = FormatHtmlText(onText);

            var dynamization = toggle.Dynamizations.Find("IsAlternateState");
            if (dynamization != null && !(dynamization is TagDynamization))
                throw new InvalidOperationException($"Refusing to overwrite non-tag dynamization: {name}.IsAlternateState");
            var tagDynamization = dynamization as TagDynamization
                                  ?? toggle.Dynamizations.Create<TagDynamization>("IsAlternateState");
            tagDynamization.Tag = processTag;

            return $"Toggle switch '{name}' added/updated on screen '{screenName}' and bound to '{processTag}'.";
        }

        public string AddOrUpdateShape(
            string screenName,
            string name,
            string shapeType,
            int? left = null, int? top = null, uint? width = null, uint? height = null,
            int? centerX = null, int? centerY = null, uint? radius = null,
            string? backColor = null,
            string? borderColor = null,
            uint borderWidth = 1,
            string? targetName = null)
        {
            var hmiSw = FindHmiSoftware(targetName);
            var screen = FindHmiScreen(hmiSw, screenName) ?? hmiSw.Screens.Create(screenName);

            if (shapeType.Equals("Circle", StringComparison.OrdinalIgnoreCase))
            {
                var circle = screen.ScreenItems.Find(name) as HmiCircle ?? screen.ScreenItems.Create<HmiCircle>(name);
                if (centerX.HasValue) circle.CenterX = centerX.Value;
                if (centerY.HasValue) circle.CenterY = centerY.Value;
                if (radius.HasValue) circle.Radius = radius.Value;
                if (!string.IsNullOrEmpty(backColor)) circle.BackColor = ParseColor(backColor) ?? circle.BackColor;
                if (!string.IsNullOrEmpty(borderColor)) circle.BorderColor = ParseColor(borderColor) ?? circle.BorderColor;
                circle.BorderWidth = (byte)borderWidth;
            }
            else
            {
                var rect = screen.ScreenItems.Find(name) as HmiRectangle ?? screen.ScreenItems.Create<HmiRectangle>(name);
                if (left.HasValue) rect.Left = left.Value;
                if (top.HasValue) rect.Top = top.Value;
                if (width.HasValue) rect.Width = width.Value;
                if (height.HasValue) rect.Height = height.Value;
                if (!string.IsNullOrEmpty(backColor)) rect.BackColor = ParseColor(backColor) ?? rect.BackColor;
                if (!string.IsNullOrEmpty(borderColor)) rect.BorderColor = ParseColor(borderColor) ?? rect.BorderColor;
                rect.BorderWidth = (byte)borderWidth;
            }

            return $"Shape '{name}' ({shapeType}) added/updated successfully on screen '{screenName}'.";
        }

        public string AddOrUpdateIOField(
            string screenName,
            string name,
            int left, int top, uint width, uint height,
            string mode = "Output",
            string? processTag = null,
            string? targetName = null)
        {
            var hmiSw = FindHmiSoftware(targetName);
            var screen = FindHmiScreen(hmiSw, screenName) ?? hmiSw.Screens.Create(screenName);

            var io = screen.ScreenItems.Find(name) as HmiIOField ?? screen.ScreenItems.Create<HmiIOField>(name);
            io.Left = left;
            io.Top = top;
            io.Width = width;
            io.Height = height;

            if (mode.Equals("InputOutput", StringComparison.OrdinalIgnoreCase))
                io.IOFieldType = HmiIOFieldType.InputOutput;
            else
                io.IOFieldType = HmiIOFieldType.Output;

            if (!string.IsNullOrEmpty(processTag))
            {
                var dyn = io.Dynamizations.Find("ProcessValue") as TagDynamization
                          ?? io.Dynamizations.Create<TagDynamization>("ProcessValue");
                dyn.Tag = processTag;
            }

            return $"IOField '{name}' added/updated successfully on screen '{screenName}'.";
        }

        public string SetTagDynamization(
            string screenName,
            string itemName,
            string propertyName,
            string tagName,
            string conditionType = "None",
            List<HmiRangeEntryInput>? ranges = null,
            string? targetName = null)
        {
            bool isRange = conditionType.Equals("Range", StringComparison.OrdinalIgnoreCase);
            bool isSingleBit = conditionType.Equals("Singlebit", StringComparison.OrdinalIgnoreCase);
            if (!isRange && !isSingleBit && !conditionType.Equals("None", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Unsupported dynamization condition type '{conditionType}'. Use 'None', 'Range', or 'Singlebit'.", nameof(conditionType));
            if (isRange && (ranges == null || ranges.Count == 0))
                throw new ArgumentException("Range dynamization requires at least one range entry.", nameof(ranges));

            HmiRangeEntryInput? stateZero = null;
            HmiRangeEntryInput? stateOne = null;
            if (isSingleBit)
            {
                if (ranges == null || ranges.Count != 2)
                    throw new ArgumentException("Singlebit dynamization requires exactly two entries: one for state 0 and one for state 1.", nameof(ranges));

                foreach (HmiRangeEntryInput range in ranges)
                {
                    int state = GetSingleBitState(range);
                    if (state == 0)
                    {
                        if (stateZero != null)
                            throw new ArgumentException("Singlebit dynamization must have exactly one state 0 entry.", nameof(ranges));
                        stateZero = range;
                    }
                    else
                    {
                        if (stateOne != null)
                            throw new ArgumentException("Singlebit dynamization must have exactly one state 1 entry.", nameof(ranges));
                        stateOne = range;
                    }
                }

                if (stateZero == null || stateOne == null)
                    throw new ArgumentException("Singlebit dynamization requires one entry for state 0 and one for state 1.", nameof(ranges));
            }

            var hmiSw = FindHmiSoftware(targetName);
            var screen = FindHmiScreen(hmiSw, screenName);
            if (screen == null) throw new InvalidOperationException($"Screen '{screenName}' not found.");

            var item = screen.ScreenItems.Find(itemName);
            if (item == null) throw new InvalidOperationException($"Item '{itemName}' not found on screen '{screenName}'.");

            var tDyn = item.Dynamizations.Find(propertyName) as TagDynamization
                       ?? item.Dynamizations.Create<TagDynamization>(propertyName);
            tDyn.Tag = tagName;

            if (isRange || isSingleBit)
            {
                var mappingTable = tDyn.ValueConverter.MappingTable;
                while (mappingTable.Entries.Count > 0)
                {
                    mappingTable.Entries[0].Delete();
                }

                if (isRange)
                {
                    mappingTable.ConditionType = ConditionType.Range;
                    foreach (HmiRangeEntryInput range in ranges!)
                    {
                        var entry = mappingTable.Entries.Create<MappingTableEntryRange>();
                        entry.From = range.From;
                        entry.To = range.To;
                        entry.Value = GetDynamizationValue(propertyName, range.Value);
                    }
                }
                else
                {
                    mappingTable.ConditionType = ConditionType.Singlebit;
                    var entries = mappingTable.Entries.OfType<MappingTableEntryBitmask>().ToList();
                    if (mappingTable.Entries.Count == 0)
                    {
                        try
                        {
                            entries = mappingTable.Entries.Create(BitDynamizationType.SingleBit).ToList();
                        }
                        catch (EngineeringTargetInvocationException)
                        {
                            // TIA may have generated the entries when ConditionType was assigned.
                            entries = mappingTable.Entries.OfType<MappingTableEntryBitmask>().ToList();
                            if (entries.Count == 0)
                                throw;
                        }
                    }
                    if (mappingTable.Entries.Count != entries.Count)
                        throw new InvalidOperationException("TIA created unexpected non-bitmask entries for a Singlebit dynamization.");
                    if (entries.Count != 2)
                        throw new InvalidOperationException($"TIA created {entries.Count} entries for Singlebit dynamization; expected 2.");

                    MappingTableEntryBitmask zeroEntry = entries.SingleOrDefault(entry => entry.Condition == 0)
                        ?? throw new InvalidOperationException("TIA did not create the state 0 entry for Singlebit dynamization.");
                    MappingTableEntryBitmask oneEntry = entries.SingleOrDefault(entry => entry.Condition != 0)
                        ?? throw new InvalidOperationException("TIA did not create the state 1 entry for Singlebit dynamization.");
                    zeroEntry.Value = GetDynamizationValue(propertyName, stateZero!.Value);
                    oneEntry.Value = GetDynamizationValue(propertyName, stateOne!.Value);
                }

                ConditionType expectedCondition = isRange ? ConditionType.Range : ConditionType.Singlebit;
                int expectedEntryCount = isRange ? ranges!.Count : 2;
                if (mappingTable.ConditionType != expectedCondition || mappingTable.Entries.Count != expectedEntryCount)
                    throw new InvalidOperationException(
                        $"TIA did not apply the requested {expectedCondition} mapping to '{itemName}.{propertyName}'.");
            }

            return $"Dynamization on '{itemName}.{propertyName}' linked to '{tagName}' successfully.";
        }

        public object DeleteHmiDynamization(
            string screenName,
            string itemName,
            string propertyName,
            string? targetName = null)
        {
            if (string.IsNullOrWhiteSpace(screenName))
                throw new ArgumentException("screen_name must not be empty.", nameof(screenName));
            if (string.IsNullOrWhiteSpace(itemName))
                throw new ArgumentException("item_name must not be empty.", nameof(itemName));
            if (string.IsNullOrWhiteSpace(propertyName))
                throw new ArgumentException("property_name must not be empty.", nameof(propertyName));

            var hmiSw = FindHmiSoftware(targetName);
            var screen = FindHmiScreen(hmiSw, screenName);
            if (screen == null) throw new InvalidOperationException($"Screen '{screenName}' not found.");

            var item = screen.ScreenItems.Find(itemName);
            if (item == null) throw new InvalidOperationException($"Item '{itemName}' not found on screen '{screenName}'.");

            using (ExclusiveAccess access = _tiaPortal!.ExclusiveAccess("Delete Unified HMI dynamization"))
            using (Transaction transaction = access.Transaction(_project!, "Delete Unified HMI dynamization"))
            {
                DynamizationBase? dynamization = item.Dynamizations.Find(propertyName);
                if (dynamization == null)
                    throw new InvalidOperationException(
                        $"Dynamization for '{itemName}.{propertyName}' was not found on screen '{screenName}'.");

                string dynamizationType = dynamization.GetType().Name;
                dynamization.Delete();
                if (item.Dynamizations.Find(propertyName) != null)
                    throw new InvalidOperationException(
                        $"Dynamization for '{itemName}.{propertyName}' remained after deletion.");

                transaction.CommitOnDispose();
                return new
                {
                    screenName,
                    itemName,
                    propertyName,
                    dynamizationType,
                    deleted = true
                };
            }
        }

        private static int GetSingleBitState(HmiRangeEntryInput range)
        {
            double from = Convert.ToDouble(range.From, CultureInfo.InvariantCulture);
            double to = Convert.ToDouble(range.To, CultureInfo.InvariantCulture);
            if (from != to || (from != 0 && from != 1))
                throw new ArgumentException("Singlebit entries must use matching from/to bounds of 0 or 1.");
            return (int)from;
        }

        private static object GetDynamizationValue(string propertyName, string value)
        {
            if (propertyName.EndsWith("Color", StringComparison.OrdinalIgnoreCase))
                return ParseColor(value) ?? Color.White;
            return value;
        }

        public string UpdateDynamizationEntry(
            string screenName,
            string itemName,
            string propertyName,
            int entryIndex,
            HmiMappingValueInput? newValue = null,
            HmiMappingValueInput? newAlternateValue = null,
            bool? newFlashing = null,
            string? newFlashingRate = null,
            string? targetName = null)
        {
            var hmiSw = FindHmiSoftware(targetName);
            var screen = FindHmiScreen(hmiSw, screenName);
            if (screen == null) throw new InvalidOperationException($"Screen '{screenName}' not found.");

            var item = screen.ScreenItems.Find(itemName);
            if (item == null) throw new InvalidOperationException($"Item '{itemName}' not found on screen '{screenName}'.");

            var dynamization = item.Dynamizations.Find(propertyName);
            ValueConverter converter;
            if (dynamization is TagDynamization tagDynamization)
                converter = tagDynamization.ValueConverter;
            else if (dynamization is ExpressionDynamization expressionDynamization)
                converter = expressionDynamization.ValueConverter;
            else
                throw new InvalidOperationException(
                    $"A tag or expression dynamization for '{itemName}.{propertyName}' was not found.");

            var entries = converter.MappingTable.Entries;
            if (entryIndex < 0 || entryIndex >= entries.Count)
                throw new ArgumentOutOfRangeException(nameof(entryIndex),
                    $"Entry index must be between 0 and {entries.Count - 1}.");
            if (newValue == null && newAlternateValue == null &&
                !newFlashing.HasValue && newFlashingRate == null)
                throw new ArgumentException("At least one mapping entry value or flashing property must be provided.");

            MappingTableEntryBase entry = entries[entryIndex];
            object? value = newValue == null
                ? null
                : ParseDynamizationMappingValue(newValue, entry.Value?.GetType(), "new_value");
            Type? alternateValueType = entry.AlternateValue?.GetType() ?? entry.Value?.GetType();
            object? alternateValue = newAlternateValue == null
                ? null
                : ParseDynamizationMappingValue(newAlternateValue, alternateValueType, "new_alternate_value");
            Siemens.Engineering.HmiUnified.UI.Dynamization.Flashing.FlashingRate? flashingRate =
                newFlashingRate == null
                    ? (Siemens.Engineering.HmiUnified.UI.Dynamization.Flashing.FlashingRate?)null
                    : (Siemens.Engineering.HmiUnified.UI.Dynamization.Flashing.FlashingRate)ParseEnumValue(
                        typeof(Siemens.Engineering.HmiUnified.UI.Dynamization.Flashing.FlashingRate),
                        newFlashingRate,
                        "new_flashing_rate");

            if (newValue != null) entry.Value = value;
            if (newAlternateValue != null) entry.AlternateValue = alternateValue;
            if (newFlashing.HasValue) entry.Flashing = newFlashing.Value;
            if (flashingRate.HasValue) entry.FlashingRate = flashingRate.Value;

            return $"Updated mapping entry {entryIndex} on '{itemName}.{propertyName}'.";
        }

        private static object ParseDynamizationMappingValue(
            HmiMappingValueInput input,
            Type? expectedType,
            string propertyName)
        {
            object value;
            switch (input.Type)
            {
                case "String": value = input.Value; break;
                case "Boolean":
                    if (!bool.TryParse(input.Value, out bool booleanValue))
                        throw new InvalidDataException($"'{propertyName}' must be 'true' or 'false' for Boolean values.");
                    value = booleanValue;
                    break;
                case "Color": value = ParseJsonColor(input.Value, propertyName); break;
                case "Byte": value = byte.Parse(input.Value, NumberStyles.Integer, CultureInfo.InvariantCulture); break;
                case "Int16": value = short.Parse(input.Value, NumberStyles.Integer, CultureInfo.InvariantCulture); break;
                case "UInt16": value = ushort.Parse(input.Value, NumberStyles.Integer, CultureInfo.InvariantCulture); break;
                case "Int32": value = int.Parse(input.Value, NumberStyles.Integer, CultureInfo.InvariantCulture); break;
                case "UInt32": value = uint.Parse(input.Value, NumberStyles.Integer, CultureInfo.InvariantCulture); break;
                case "Int64": value = long.Parse(input.Value, NumberStyles.Integer, CultureInfo.InvariantCulture); break;
                case "UInt64": value = ulong.Parse(input.Value, NumberStyles.Integer, CultureInfo.InvariantCulture); break;
                case "Single": value = float.Parse(input.Value, NumberStyles.Float, CultureInfo.InvariantCulture); break;
                case "Double": value = double.Parse(input.Value, NumberStyles.Float, CultureInfo.InvariantCulture); break;
                case "Decimal": value = decimal.Parse(input.Value, NumberStyles.Number, CultureInfo.InvariantCulture); break;
                default:
                    throw new InvalidDataException(
                        $"Unsupported mapping value type '{input.Type}' for '{propertyName}'.");
            }

            if (expectedType == null)
                throw new InvalidOperationException(
                    $"Cannot infer the existing CLR value type for '{propertyName}'.");
            if (value.GetType() != expectedType)
                throw new InvalidDataException(
                    $"'{propertyName}' has type '{expectedType.Name}', but the supplied value has type '{value.GetType().Name}'.");
            return value;
        }

        public ScriptUpdateSummary UpdateHmiButtonScripts(
            string screenName,
            string? findText = null,
            string? replaceText = null,
            string? newScript = null,
            List<string>? buttonNames = null,
            string? targetName = null)
        {
            var hmiSw = FindHmiSoftware(targetName);
            var screen = FindHmiScreen(hmiSw, screenName);
            if (screen == null) throw new InvalidOperationException($"Screen '{screenName}' not found.");

            var summary = new ScriptUpdateSummary();

            foreach (var sItem in screen.ScreenItems)
            {
                if (sItem is HmiButton btn)
                {
                    if (buttonNames != null && buttonNames.Count > 0 &&
                        !buttonNames.Contains(btn.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var handler = btn.EventHandlers.Find(HmiButtonEventType.Tapped);
                    if (handler == null) continue;

                    string oldScript = handler.Script.ScriptCode ?? "";
                    string updated = oldScript;

                    if (!string.IsNullOrEmpty(newScript))
                    {
                        updated = newScript!;
                    }
                    else if (!string.IsNullOrEmpty(findText) && replaceText != null)
                    {
                        if (oldScript.Contains(findText!))
                        {
                            updated = oldScript.Replace(findText!, replaceText);
                        }
                    }

                    if (updated != oldScript)
                    {
                        handler.Script.ScriptCode = updated;
                        var chk = handler.Script.SyntaxCheck();
                        summary.ModifiedButtons.Add(new ModifiedButtonDetails
                        {
                            ButtonName = btn.Name,
                            OldScript = oldScript,
                            NewScript = updated,
                            Errors = ((IEnumerable<string>)chk.Errors).ToList(),
                            Warnings = ((IEnumerable<string>)chk.Warnings).ToList()
                        });
                    }
                }
            }

            return summary;
        }

        #endregion

        #region HMI Tags

        public List<string> ListHmiTagTables(string? targetName = null)
        {
            var hmiSw = FindHmiSoftware(targetName);
            var list = new List<string>();
            foreach (HmiTagTable tt in hmiSw.TagTables)
            {
                list.Add(tt.Name);
            }
            return list;
        }

        public List<HmiTagDetails> ListHmiTags(string? tableName = null, string? targetName = null)
        {
            var hmiSw = FindHmiSoftware(targetName);
            var list = new List<HmiTagDetails>();

            IEnumerable<HmiTagTable> tables = string.IsNullOrEmpty(tableName)
                ? hmiSw.TagTables
                : new[] { hmiSw.TagTables.Find(tableName) ?? throw new InvalidOperationException($"TagTable '{tableName}' not found.") };

            foreach (var tt in tables)
            {
                foreach (HmiTag t in tt.Tags)
                {
                    list.Add(new HmiTagDetails
                    {
                        Name = t.Name,
                        TableName = tt.Name,
                        Connection = t.Connection ?? "",
                        PlcTag = t.PlcTag ?? "",
                        DataType = t.DataType?.ToString() ?? "",
                        AcquisitionCycle = t.AcquisitionCycle ?? "",
                        AcquisitionMode = t.AcquisitionMode.ToString()
                    });
                }
            }

            return list;
        }

        public string CreateOrUpdateHmiTag(
            string tagName,
            string plcTag,
            string connection = "HMI_Connection_1",
            string? tableName = null,
            string? targetName = null)
        {
            var hmiSw = FindHmiSoftware(targetName);
            var tagTable = !string.IsNullOrEmpty(tableName)
                ? (hmiSw.TagTables.Find(tableName) ?? hmiSw.TagTables.Create(tableName))
                : (hmiSw.TagTables.Find("MixingLine_Tags") ?? hmiSw.TagTables.Find("Default tag table") ?? hmiSw.TagTables.FirstOrDefault() ?? hmiSw.TagTables.Create("Default tag table"));

            var hmiTag = tagTable.Tags.Find(tagName) ?? tagTable.Tags.Create(tagName);
            hmiTag.Connection = connection;

            try
            {
                hmiTag.PlcTag = plcTag;
            }
            catch
            {
                // Fallback quoted DB name format: "DB_MixingLine".Line.MotorInfeed.RunFb
                var firstDot = plcTag.IndexOf('.');
                if (firstDot > 0)
                {
                    var quoted = "\"" + plcTag.Substring(0, firstDot) + "\"" + plcTag.Substring(firstDot);
                    hmiTag.PlcTag = quoted;
                }
            }

            return $"HMI Tag '{tagName}' -> '{hmiTag.PlcTag}' (Connection '{connection}') in table '{tagTable.Name}'.";
        }

        public string CreateOrUpdateHmiDiscreteAlarm(
            string alarmName,
            string alarmClass,
            string raisedStateTag,
            string text,
            uint raisedStateTagBitNumber = 0,
            string triggerMode = "OnRisingEdge",
            string? acknowledgmentControlTag = null,
            string? targetName = null)
        {
            var hmiSw = FindHmiSoftware(targetName);
            if (hmiSw.AlarmClasses.Find(alarmClass) == null)
                throw new InvalidOperationException($"HMI alarm class '{alarmClass}' not found.");
            if (hmiSw.Tags.Find(raisedStateTag) == null)
                throw new InvalidOperationException($"HMI tag '{raisedStateTag}' not found.");
            if (!Enum.TryParse(triggerMode, true, out HmiDiscreteAlarmTriggerMode parsedTriggerMode) ||
                !Enum.IsDefined(typeof(HmiDiscreteAlarmTriggerMode), parsedTriggerMode))
                throw new ArgumentException($"Unsupported discrete alarm trigger mode '{triggerMode}'.", nameof(triggerMode));
            if (!string.IsNullOrEmpty(acknowledgmentControlTag) && hmiSw.Tags.Find(acknowledgmentControlTag) == null)
                throw new InvalidOperationException($"Acknowledgment HMI tag '{acknowledgmentControlTag}' not found.");

            var alarm = hmiSw.DiscreteAlarms.Find(alarmName) ?? hmiSw.DiscreteAlarms.Create(alarmName);
            alarm.AlarmClass = alarmClass;
            alarm.RaisedStateTag = raisedStateTag;
            alarm.RaisedStateTagBitNumber = raisedStateTagBitNumber;
            alarm.TriggerMode = parsedTriggerMode;
            if (acknowledgmentControlTag != null)
                alarm.AcknowledgmentControlTag = acknowledgmentControlTag;

            if (alarm.EventText.Items.Count == 0)
                throw new InvalidOperationException($"Discrete alarm '{alarmName}' has no active text language.");
            string formattedText = FormatHtmlText(text);
            foreach (var languageText in alarm.EventText.Items)
                languageText.Text = formattedText;

            return $"Discrete alarm '{alarmName}' configured for tag '{raisedStateTag}' bit {raisedStateTagBitNumber} in class '{alarmClass}'.";
        }

        public Dictionary<string, object> UpdateHmiTagAcquisitionCycles(
            string cycle,
            bool plcOnly = true,
            string? tableName = null,
            string? targetName = null)
        {
            var hmiSw = FindHmiSoftware(targetName);
            IEnumerable<HmiTagTable> tables = string.IsNullOrEmpty(tableName)
                ? hmiSw.TagTables
                : new[] { hmiSw.TagTables.Find(tableName) ?? throw new InvalidOperationException($"TagTable '{tableName}' not found.") };

            int totalTags = 0;
            int modifiedCount = 0;
            var updatedTags = new List<string>();

            foreach (var tt in tables)
            {
                foreach (HmiTag t in tt.Tags)
                {
                    totalTags++;
                    bool isPlcConnected = !string.IsNullOrEmpty(t.Connection) || !string.IsNullOrEmpty(t.PlcTag);
                    if (!plcOnly || isPlcConnected)
                    {
                        t.AcquisitionCycle = cycle;
                        modifiedCount++;
                        updatedTags.Add(t.Name);
                    }
                }
            }

            return new Dictionary<string, object>
            {
                ["TotalTags"] = totalTags,
                ["ModifiedCount"] = modifiedCount,
                ["AcquisitionCycle"] = cycle,
                ["UpdatedTags"] = updatedTags
            };
        }

        public string SetHmiTagRange(
            string tagName,
            double? minValue = null,
            double? maxValue = null,
            string? tableName = null,
            string? targetName = null)
        {
            var hmiSw = FindHmiSoftware(targetName);
            IEnumerable<HmiTagTable> tables = string.IsNullOrEmpty(tableName)
                ? hmiSw.TagTables
                : new[] { hmiSw.TagTables.Find(tableName) ?? throw new InvalidOperationException($"TagTable '{tableName}' not found.") };

            HmiTag? foundTag = null;
            foreach (var tt in tables)
            {
                foundTag = tt.Tags.Find(tagName);
                if (foundTag != null) break;
            }

            if (foundTag == null)
            {
                throw new InvalidOperationException($"HMI Tag '{tagName}' not found.");
            }

            if (minValue.HasValue)
            {
                foundTag.InitialMinValue.ValueType = HmiLimitValueType.Constant;
                foundTag.InitialMinValue.Value = minValue.Value;
            }
            else
            {
                foundTag.InitialMinValue.ValueType = HmiLimitValueType.None;
            }

            if (maxValue.HasValue)
            {
                foundTag.InitialMaxValue.ValueType = HmiLimitValueType.Constant;
                foundTag.InitialMaxValue.Value = maxValue.Value;
            }
            else
            {
                foundTag.InitialMaxValue.ValueType = HmiLimitValueType.None;
            }

            return $"Tag '{tagName}' range updated: Min={minValue}, Max={maxValue}.";
        }

        public string CreateDataLog(string logName, string? targetName = null)
        {
            var hmiSw = FindHmiSoftware(targetName);
            var existing = hmiSw.DataLogs.Find(logName);
            if (existing != null)
            {
                return $"DataLog '{logName}' already exists.";
            }

            var created = hmiSw.DataLogs.Create(logName);
            return $"DataLog '{created.Name}' created successfully.";
        }

        public Dictionary<string, object> AssignTagsToDataLog(
            string logName,
            List<string> tagNames,
            string cycle = "T1s",
            string mode = "Cyclic",
            string? targetName = null)
        {
            var hmiSw = FindHmiSoftware(targetName);
            var dl = hmiSw.DataLogs.Find(logName) ?? hmiSw.DataLogs.Create(logName);

            var loggingMode = mode.Equals("OnChange", StringComparison.OrdinalIgnoreCase)
                ? HmiLoggingMode.OnChange
                : (mode.Equals("OnDemand", StringComparison.OrdinalIgnoreCase) ? HmiLoggingMode.OnDemand : HmiLoggingMode.Cyclic);

            int assignedCount = 0;
            var assignedTags = new List<string>();

            foreach (var tt in hmiSw.TagTables)
            {
                foreach (HmiTag tag in tt.Tags)
                {
                    if (tagNames.Contains(tag.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        var lt = tag.LoggingTags.Find(tag.Name) ?? tag.LoggingTags.Create(tag.Name);
                        lt.DataLog = dl.Name;
                        lt.LoggingMode = loggingMode;
                        if (loggingMode == HmiLoggingMode.Cyclic)
                        {
                            lt.Cycle = cycle;
                        }
                        assignedCount++;
                        assignedTags.Add(tag.Name);
                    }
                }
            }

            return new Dictionary<string, object>
            {
                ["DataLog"] = dl.Name,
                ["AssignedCount"] = assignedCount,
                ["Mode"] = loggingMode.ToString(),
                ["Cycle"] = cycle,
                ["AssignedTags"] = assignedTags
            };
        }

        #endregion

        #region Device & HMI Compilation

        public CompilationReport CompileHmi(string? targetDevice = null)
        {
            EnsureProjectOpen();

            ICompilable? compilable = null;
            string targetPath = "";

            if (!string.IsNullOrEmpty(targetDevice))
            {
                var dev = _project!.Devices.Find(targetDevice);
                if (dev != null)
                {
                    foreach (DeviceItem item in dev.DeviceItems)
                    {
                        var comp = item.GetService<ICompilable>();
                        if (comp != null && (item.Name.Contains("HMI") || item.Name.Contains("RT")))
                        {
                            compilable = comp;
                            targetPath = $"{dev.Name}/{item.Name}";
                            break;
                        }
                    }
                    if (compilable == null)
                    {
                        compilable = dev.GetService<ICompilable>();
                        targetPath = dev.Name;
                    }
                }
            }

            if (compilable == null)
            {
                // Search for any HMI device item with ICompilable
                foreach (Device dev in _project!.Devices)
                {
                    foreach (DeviceItem item in dev.DeviceItems)
                    {
                        var comp = item.GetService<ICompilable>();
                        if (comp != null && (item.Name.Contains("HMI") || item.Name.Contains("RT")))
                        {
                            compilable = comp;
                            targetPath = $"{dev.Name}/{item.Name}";
                            break;
                        }
                    }
                    if (compilable != null) break;
                }
            }

            if (compilable == null)
            {
                throw new InvalidOperationException("No ICompilable HMI target found in project.");
            }

            Console.Error.WriteLine($"[TiaManager] Compiling HMI target '{targetPath}'...");
            var res = compilable.Compile();

            var report = new CompilationReport
            {
                State = res.State.ToString(),
                ErrorCount = res.ErrorCount,
                WarningCount = res.WarningCount
            };

            CollectCompileMessages(res.Messages, report.Messages);

            return report;
        }

        #endregion

        #region Helpers

        private static string FormatHtmlText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "<body><p></p></body>";
            if (text.StartsWith("<body", StringComparison.OrdinalIgnoreCase)) return text;
            return $"<body><p>{SecurityElement.Escape(text)}</p></body>";
        }

        private static Color? ParseColor(string? colorStr)
        {
            if (string.IsNullOrWhiteSpace(colorStr)) return null;
            colorStr = colorStr!.Trim();

            // Named colors
            if (colorStr.Equals("white", StringComparison.OrdinalIgnoreCase)) return Color.White;
            if (colorStr.Equals("gray", StringComparison.OrdinalIgnoreCase) || colorStr.Equals("grey", StringComparison.OrdinalIgnoreCase)) return Color.Gray;
            if (colorStr.Equals("red", StringComparison.OrdinalIgnoreCase)) return Color.Red;
            if (colorStr.Equals("green", StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(255, 34, 197, 94);
            if (colorStr.Equals("blue", StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(255, 37, 99, 235);
            if (colorStr.Equals("transparent", StringComparison.OrdinalIgnoreCase)) return Color.Transparent;

            // Hex formats: #RRGGBB or #AARRGGBB
            if (colorStr.StartsWith("#"))
            {
                string hex = colorStr.Substring(1);
                if (hex.Length == 6)
                {
                    int r = int.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
                    int g = int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
                    int b = int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
                    return Color.FromArgb(255, r, g, b);
                }
                if (hex.Length == 8)
                {
                    int a = int.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
                    int r = int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
                    int g = int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
                    int b = int.Parse(hex.Substring(6, 2), NumberStyles.HexNumber);
                    return Color.FromArgb(a, r, g, b);
                }
            }

            // RGB format: "128, 128, 128"
            var parts = colorStr.Split(',');
            if (parts.Length == 3 &&
                int.TryParse(parts[0].Trim(), out int cr) &&
                int.TryParse(parts[1].Trim(), out int cg) &&
                int.TryParse(parts[2].Trim(), out int cb))
            {
                return Color.FromArgb(255, cr, cg, cb);
            }

            return null;
        }

        private static string ColorToHex(Color color)
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        #region HMI Connections

        public List<HmiConnectionDetails> ListHmiConnections(string? targetName = null)
        {
            var hmiSw = FindHmiSoftware(targetName);
            var list = new List<HmiConnectionDetails>();
            foreach (var conn in hmiSw.Connections)
            {
                list.Add(new HmiConnectionDetails
                {
                    Name = conn.Name,
                    CommunicationDriver = conn.CommunicationDriver ?? "",
                    DisabledAtStartup = conn.DisabledAtStartup,
                    Partner = conn.Partner ?? "",
                    Station = conn.Station ?? "",
                    Node = conn.Node ?? "",
                    InitialAddress = conn.InitialAddress ?? "",
                    Comment = conn.Comment ?? ""
                });
            }
            return list;
        }

        public HmiConnectionDetails CreateHmiConnection(string? targetName, string connectionName, string partnerPlcName, string? driver = null, bool disabledAtStartup = false, string? comment = null)
        {
            EnsureProjectOpen();
            var hmiSw = FindHmiSoftware(targetName);

            // Find HMI Device and DeviceItem
            Device? pcDevice = null;
            DeviceItem? hmiItem = null;
            foreach (Device dev in _project!.Devices)
            {
                foreach (DeviceItem item in dev.DeviceItems)
                {
                    var sc = item.GetService<SoftwareContainer>();
                    if (sc != null && sc.Software == hmiSw)
                    {
                        pcDevice = dev;
                        hmiItem = item;
                        break;
                    }
                    foreach (DeviceItem sub in item.DeviceItems)
                    {
                        var subSc = sub.GetService<SoftwareContainer>();
                        if (subSc != null && subSc.Software == hmiSw)
                        {
                            pcDevice = dev;
                            hmiItem = sub;
                            break;
                        }
                    }
                    if (pcDevice != null) break;
                }
                if (pcDevice != null) break;
            }

            if (pcDevice == null || hmiItem == null)
                throw new InvalidOperationException("Could not find hardware Device/DeviceItem corresponding to HmiSoftware.");

            // Find Partner PLC Device
            Device? plcDevice = null;
            foreach (Device dev in _project.Devices)
            {
                if (string.Equals(dev.Name, partnerPlcName, StringComparison.OrdinalIgnoreCase))
                {
                    plcDevice = dev;
                    break;
                }
            }
            if (plcDevice == null)
                throw new ArgumentException($"Partner PLC device '{partnerPlcName}' not found in project.");

            // Find Local network node on PC device
            dynamic? localNode = null;
            FindNetworkNode(pcDevice.DeviceItems, ref localNode);

            // Find Partner CPU and partner node on PLC device
            DeviceItem? partnerCpuItem = null;
            dynamic? partnerNode = null;
            foreach (DeviceItem item in plcDevice.DeviceItems)
            {
                if (string.Equals(item.Name, partnerPlcName, StringComparison.OrdinalIgnoreCase) ||
                    item.TypeIdentifier.Contains("6ES7 5") || item.TypeIdentifier.Contains("6ES7 12") ||
                    item.GetService<SoftwareContainer>()?.Software is Siemens.Engineering.SW.PlcSoftware)
                {
                    partnerCpuItem = item;
                }
                FindNetworkNode(item.DeviceItems, ref partnerNode);
                if (partnerNode == null)
                {
                    try
                    {
                        var net = item.GetService<NetworkInterface>();
                        if (net != null && net.Nodes.Count > 0)
                        {
                            foreach (dynamic n in net.Nodes)
                            {
                                if (n.ConnectedSubnet != null) { partnerNode = n; break; }
                            }
                            if (partnerNode == null) partnerNode = net.Nodes[0];
                        }
                    }
                    catch { }
                }
            }
            if (partnerCpuItem == null)
            {
                partnerCpuItem = plcDevice.DeviceItems.FirstOrDefault();
            }

            // Create integrated hardware connection if CommunicationManagement is available
            var cm = hmiItem.GetService<CommunicationManagement>();
            if (cm != null && localNode != null && partnerCpuItem != null && partnerNode != null)
            {
                bool hwExists = false;
                for (int i = 0; i < cm.Connections.Count; i++)
                {
                    var c = cm.Connections[i];
                    if (c is Siemens.Engineering.HW.CommunicationConnections.HmiConnection hc &&
                        (string.Equals(hc.LocalConnectionName, connectionName, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(hc.LocalConnectionName, connectionName + "_1", StringComparison.OrdinalIgnoreCase)))
                    {
                        hwExists = true;
                        break;
                    }
                }

                if (!hwExists)
                {
                    var createMethod = cm.Connections.GetType().GetMethods()
                        .First(m => m.Name == "Create" && m.IsGenericMethodDefinition && m.GetParameters().Length == 3)
                        .MakeGenericMethod(typeof(Siemens.Engineering.HW.CommunicationConnections.HmiConnection));
                    var newHwConn = (Siemens.Engineering.HW.CommunicationConnections.HmiConnection)createMethod.Invoke(cm.Connections, new object[] { localNode, partnerCpuItem, partnerNode });
                    newHwConn.LocalConnectionName = connectionName;
                }
            }

            // Verify or update in WinCC Unified software connections
            var swConn = hmiSw.Connections.Find(connectionName) ?? hmiSw.Connections.Find(connectionName + "_1");
            if (swConn != null)
            {
                if (!string.IsNullOrEmpty(driver)) swConn.CommunicationDriver = driver;
                swConn.DisabledAtStartup = disabledAtStartup;
                if (comment != null) swConn.Comment = comment;
            }

            return new HmiConnectionDetails
            {
                Name = swConn?.Name ?? connectionName,
                CommunicationDriver = swConn?.CommunicationDriver ?? (driver ?? "SIMATIC S7 1200/1500"),
                DisabledAtStartup = swConn?.DisabledAtStartup ?? disabledAtStartup,
                Partner = swConn?.Partner ?? partnerPlcName,
                Station = swConn?.Station ?? partnerPlcName,
                Node = swConn?.Node ?? "",
                InitialAddress = swConn?.InitialAddress ?? "",
                Comment = swConn?.Comment ?? (comment ?? "")
            };
        }

        public HmiConnectionDeleteResult DeleteHmiConnection(string? targetName, string connectionName)
        {
            EnsureProjectOpen();
            var hmiSw = FindHmiSoftware(targetName);

            // Find HMI Device and DeviceItem
            Device? pcDevice = null;
            DeviceItem? hmiItem = null;
            foreach (Device dev in _project!.Devices)
            {
                foreach (DeviceItem item in dev.DeviceItems)
                {
                    var sc = item.GetService<SoftwareContainer>();
                    if (sc != null && sc.Software == hmiSw)
                    {
                        pcDevice = dev;
                        hmiItem = item;
                        break;
                    }
                    foreach (DeviceItem sub in item.DeviceItems)
                    {
                        var subSc = sub.GetService<SoftwareContainer>();
                        if (subSc != null && subSc.Software == hmiSw)
                        {
                            pcDevice = dev;
                            hmiItem = sub;
                            break;
                        }
                    }
                    if (pcDevice != null) break;
                }
                if (pcDevice != null) break;
            }

            bool deletedHw = false;
            bool deletedSw = false;

            // 1. Delete matching hardware HMI connection if present
            if (hmiItem != null)
            {
                var cm = hmiItem.GetService<CommunicationManagement>();
                if (cm != null)
                {
                    for (int i = cm.Connections.Count - 1; i >= 0; i--)
                    {
                        var c = cm.Connections[i];
                        if (c is Siemens.Engineering.HW.CommunicationConnections.HmiConnection hc)
                        {
                            if (string.Equals(hc.LocalConnectionName, connectionName, StringComparison.OrdinalIgnoreCase))
                            {
                                try
                                {
                                    hc.Delete();
                                    deletedHw = true;
                                    Console.Error.WriteLine($"[TiaManager] Deleted hardware HMI connection '{connectionName}'.");
                                }
                                catch (Exception ex)
                                {
                                    Console.Error.WriteLine($"[TiaManager] Error deleting hardware HMI connection '{connectionName}': {ex.Message}");
                                }
                            }
                        }
                    }
                }
            }

            // 2. Delete matching software HMI connection if present
            var swConn = hmiSw.Connections.Find(connectionName);
            if (swConn != null)
            {
                try
                {
                    swConn.Delete();
                    deletedSw = true;
                    Console.Error.WriteLine($"[TiaManager] Deleted software HMI connection '{connectionName}'.");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[TiaManager] Error deleting software HMI connection '{connectionName}': {ex.Message}");
                    throw;
                }
            }

            if (!deletedHw && !deletedSw)
            {
                return new HmiConnectionDeleteResult
                {
                    Success = false,
                    ConnectionName = connectionName,
                    Message = $"HMI connection '{connectionName}' was not found in either hardware or software connections."
                };
            }

            return new HmiConnectionDeleteResult
            {
                Success = true,
                ConnectionName = connectionName,
                DeletedHardware = deletedHw,
                DeletedSoftware = deletedSw,
                Message = $"Successfully deleted HMI connection '{connectionName}' (Hardware: {deletedHw}, Software: {deletedSw})."
            };
        }

        private static void FindNetworkNode(DeviceItemComposition items, ref dynamic? foundNode)
        {
            foreach (DeviceItem item in items)
            {
                try
                {
                    var net = item.GetService<NetworkInterface>();
                    if (net != null && net.Nodes.Count > 0)
                    {
                        foreach (dynamic n in net.Nodes)
                        {
                            if (n.ConnectedSubnet != null) { foundNode = n; return; }
                        }
                        if (foundNode == null) foundNode = net.Nodes[0];
                    }
                }
                catch { }

                FindNetworkNode(item.DeviceItems, ref foundNode);
                if (foundNode != null) return;
            }
        }

        #region HMI Text Lists

        private List<string> GetHmiLanguages(HmiSoftware hmiSw)
        {
            var langs = new List<string>();
            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "TiaOpennessLang_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                try
                {
                    if (hmiSw.HmiSystemTextLists.Count > 0)
                    {
                        var files = hmiSw.HmiSystemTextLists.Export(new DirectoryInfo(tempDir), "SysLang");
                        var libFile = files.FirstOrDefault(f => f.Name.EndsWith(".TextLibrary.hmi.yml", StringComparison.OrdinalIgnoreCase));
                        if (libFile != null && File.Exists(libFile.FullName))
                        {
                            var lines = File.ReadAllLines(libFile.FullName);
                            bool inLangs = false;
                            foreach (var line in lines)
                            {
                                string trimmed = line.Trim();
                                if (trimmed.StartsWith("Languages:"))
                                {
                                    inLangs = true;
                                    continue;
                                }
                                if (inLangs)
                                {
                                    if (trimmed.StartsWith("- "))
                                    {
                                        langs.Add(trimmed.Substring(2).Trim());
                                    }
                                    else if (!string.IsNullOrEmpty(trimmed))
                                    {
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
                finally
                {
                    if (Directory.Exists(tempDir))
                    {
                        try { Directory.Delete(tempDir, true); } catch { }
                    }
                }
            }
            catch { }

            if (langs.Count == 0 && _project != null)
            {
                try
                {
                    foreach (var l in _project.LanguageSettings.Languages)
                    {
                        langs.Add(l.Culture.Name);
                    }
                }
                catch { }
            }

            if (langs.Count == 0)
            {
                langs.Add("en-US");
            }
            return langs;
        }

        public List<HmiTextListSummary> ListTextLists(string? targetName = null)
        {
            EnsureProjectOpen();
            var hmiSw = FindHmiSoftware(targetName);
            var result = new List<HmiTextListSummary>();

            string tempDir = Path.Combine(Path.GetTempPath(), "TiaOpennessListTL_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                if (hmiSw.HmiTextLists.Count > 0)
                {
                    var files = hmiSw.HmiTextLists.Export(new DirectoryInfo(tempDir), "TempExport");
                    var ymlFile = files.FirstOrDefault(f => f.Name.Equals("TempExport.hmi.yml", StringComparison.OrdinalIgnoreCase));
                    var libFile = files.FirstOrDefault(f => f.Name.Equals("TempExport.TextLibrary.hmi.yml", StringComparison.OrdinalIgnoreCase));

                    // Read TextLibrary entries
                    var textMap = new Dictionary<string, List<string>>();
                    var libLanguages = new List<string>();
                    if (libFile != null && File.Exists(libFile.FullName))
                    {
                        var libLines = File.ReadAllLines(libFile.FullName);
                        bool inLangs = false;
                        string currentTextId = "";
                        bool inText = false;

                        foreach (var rawLine in libLines)
                        {
                            string trimmed = rawLine.Trim();
                            if (trimmed.StartsWith("Languages:"))
                            {
                                inLangs = true;
                                continue;
                            }
                            if (inLangs)
                            {
                                if (trimmed.StartsWith("- "))
                                {
                                    libLanguages.Add(trimmed.Substring(2).Trim());
                                    continue;
                                }
                                else if (!string.IsNullOrEmpty(trimmed))
                                {
                                    inLangs = false;
                                }
                            }

                            if (rawLine.StartsWith("      Text_") && trimmed.EndsWith(":"))
                            {
                                currentTextId = trimmed.TrimEnd(':');
                                if (!textMap.ContainsKey(currentTextId))
                                {
                                    textMap[currentTextId] = new List<string>();
                                }
                                inText = false;
                                continue;
                            }
                            if (!string.IsNullOrEmpty(currentTextId))
                            {
                                if (trimmed.StartsWith("Text:"))
                                {
                                    inText = true;
                                    continue;
                                }
                                if (inText)
                                {
                                    if (trimmed.StartsWith("- "))
                                    {
                                        string val = trimmed.Substring(2).Trim();
                                        if (val.StartsWith("'") && val.EndsWith("'") && val.Length >= 2)
                                        {
                                            val = val.Substring(1, val.Length - 2).Replace("''", "'");
                                        }
                                        textMap[currentTextId].Add(val);
                                    }
                                    else if (!string.IsNullOrEmpty(trimmed))
                                    {
                                        inText = false;
                                    }
                                }
                            }
                        }
                    }

                    // Parse text list definitions
                    if (ymlFile != null && File.Exists(ymlFile.FullName))
                    {
                        var ymlLines = File.ReadAllLines(ymlFile.FullName);
                        HmiTextListSummary? currentList = null;
                        HmiTextListEntrySummary? currentEntry = null;

                        foreach (var rawLine in ymlLines)
                        {
                            string trimmed = rawLine.Trim();
                            if (rawLine.StartsWith("      ") && !rawLine.StartsWith("        ") && trimmed.EndsWith(":"))
                            {
                                // New Text List
                                if (currentList != null)
                                {
                                    if (currentEntry != null) currentList.Entries.Add(currentEntry);
                                    result.Add(currentList);
                                    currentEntry = null;
                                }
                                currentList = new HmiTextListSummary { Name = trimmed.TrimEnd(':') };
                                continue;
                            }

                            if (currentList != null)
                            {
                                if (rawLine.StartsWith("          Text_list_entry_") && trimmed.EndsWith(":"))
                                {
                                    if (currentEntry != null) currentList.Entries.Add(currentEntry);
                                    currentEntry = new HmiTextListEntrySummary();
                                    continue;
                                }

                                if (currentEntry != null)
                                {
                                    if (trimmed.StartsWith("Value:"))
                                    {
                                        if (int.TryParse(trimmed.Substring(6).Trim(), out int v))
                                        {
                                            currentEntry.FromValue = v;
                                            currentEntry.ToValue = v;
                                        }
                                    }
                                    else if (trimmed.StartsWith("FromValue:"))
                                    {
                                        if (int.TryParse(trimmed.Substring(10).Trim(), out int fv)) currentEntry.FromValue = fv;
                                    }
                                    else if (trimmed.StartsWith("ToValue:"))
                                    {
                                        if (int.TryParse(trimmed.Substring(8).Trim(), out int tv)) currentEntry.ToValue = tv;
                                    }
                                    else if (trimmed.StartsWith("Text:"))
                                    {
                                        string textRef = trimmed.Substring(5).Trim();
                                        string idOnly = textRef.Contains(".") ? textRef.Substring(textRef.LastIndexOf('.') + 1) : textRef;
                                        if (textMap.TryGetValue(idOnly, out var texts))
                                        {
                                            if (texts.Count > 0) currentEntry.DefaultText = texts[0];
                                            for (int i = 0; i < texts.Count && i < libLanguages.Count; i++)
                                            {
                                                currentEntry.MultilingualTexts[libLanguages[i]] = texts[i];
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        if (currentList != null)
                        {
                            if (currentEntry != null) currentList.Entries.Add(currentEntry);
                            result.Add(currentList);
                        }
                    }
                }

                // If any text lists were not captured by parser, ensure they are present by name
                foreach (var tl in hmiSw.HmiTextLists)
                {
                    if (!result.Any(r => string.Equals(r.Name, tl.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        result.Add(new HmiTextListSummary { Name = tl.Name });
                    }
                }
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }

            return result;
        }

        public HmiTextListResult CreateTextList(string listName, List<TextListEntryInput> entries, string? targetName = null)
        {
            EnsureProjectOpen();
            var hmiSw = FindHmiSoftware(targetName);

            var existing = hmiSw.HmiTextLists.Find(listName);
            if (existing != null)
            {
                return new HmiTextListResult
                {
                    Success = false,
                    ListName = listName,
                    Message = $"Text list '{listName}' already exists. Delete it first if you want to recreate it."
                };
            }

            var languages = GetHmiLanguages(hmiSw);
            string defaultLang = languages[0];

            string tempDir = Path.Combine(Path.GetTempPath(), "TiaOpennessTextList_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                // 1. Build <listName>.hmi.yml
                var sbHmi = new StringBuilder();
                sbHmi.AppendLine("#Version: 2.0");
                sbHmi.AppendLine();
                sbHmi.AppendLine("TextListContainers:");
                sbHmi.AppendLine("  DeviceTextList:");
                sbHmi.AppendLine("    ResourceListType: TextList");
                sbHmi.AppendLine("    ResourceLists:");
                sbHmi.AppendLine($"      {listName}:");
                sbHmi.AppendLine("        Entries:");

                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    int fromVal = entry.FromValue ?? entry.Value;
                    int toVal = entry.ToValue ?? entry.Value;
                    sbHmi.AppendLine($"          Text_list_entry_{i}:");
                    if (fromVal != 0 || toVal != 0)
                    {
                        sbHmi.AppendLine($"            Value: {fromVal}");
                        sbHmi.AppendLine($"            FromValue: {fromVal}");
                        sbHmi.AppendLine($"            ToValue: {toVal}");
                    }
                    sbHmi.AppendLine($"            Text: MyTextLibrary.Text_{i}");
                }

                File.WriteAllText(Path.Combine(tempDir, $"{listName}.hmi.yml"), sbHmi.ToString(), Encoding.UTF8);

                // 2. Build <listName>.TextLibrary.hmi.yml
                var sbLib = new StringBuilder();
                sbLib.AppendLine("#Version: 2.0");
                sbLib.AppendLine();
                sbLib.AppendLine("TextLibraries:");
                sbLib.AppendLine("  MyTextLibrary:");
                sbLib.AppendLine("    Type: Text");
                sbLib.AppendLine("    Languages:");
                foreach (var lang in languages)
                {
                    sbLib.AppendLine($"    - {lang}");
                }
                sbLib.AppendLine($"    DefaultLanguage: {defaultLang}");
                sbLib.AppendLine("    Entries:");

                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    sbLib.AppendLine($"      Text_{i}:");
                    sbLib.AppendLine("        Text:");
                    foreach (var lang in languages)
                    {
                        string txtVal = entry.Text;
                        if (entry.MultilingualTexts != null && entry.MultilingualTexts.TryGetValue(lang, out var specificVal))
                        {
                            txtVal = specificVal;
                        }
                        string escaped = txtVal.Replace("'", "''");
                        sbLib.AppendLine($"        - '{escaped}'");
                    }
                }

                File.WriteAllText(Path.Combine(tempDir, $"{listName}.TextLibrary.hmi.yml"), sbLib.ToString(), Encoding.UTF8);

                // 3. Import into HmiTextLists
                bool imported = hmiSw.HmiTextLists.Import(new DirectoryInfo(tempDir), listName);
                if (!imported)
                {
                    return new HmiTextListResult
                    {
                        Success = false,
                        ListName = listName,
                        Message = $"Openness Import returned false for text list '{listName}'."
                    };
                }

                return new HmiTextListResult
                {
                    Success = true,
                    ListName = listName,
                    EntryCount = entries.Count,
                    Message = $"Successfully created text list '{listName}' with {entries.Count} entries."
                };
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        public HmiTextListResult DeleteTextList(string listName, string? targetName = null)
        {
            EnsureProjectOpen();
            var hmiSw = FindHmiSoftware(targetName);
            var tl = hmiSw.HmiTextLists.Find(listName);
            if (tl == null)
            {
                return new HmiTextListResult
                {
                    Success = false,
                    ListName = listName,
                    Message = $"Text list '{listName}' not found."
                };
            }

            tl.Delete();
            return new HmiTextListResult
            {
                Success = true,
                ListName = listName,
                Message = $"Successfully deleted text list '{listName}'."
            };
        }

        public List<string> ExportTextLists(string destinationFolder, string baseFilename, string? targetName = null)
        {
            EnsureProjectOpen();
            var hmiSw = FindHmiSoftware(targetName);
            var dir = new DirectoryInfo(destinationFolder);
            if (!dir.Exists)
            {
                dir.Create();
            }
            var files = hmiSw.HmiTextLists.Export(dir, baseFilename);
            return files.Select(f => f.FullName).ToList();
        }

        #endregion

        #endregion

        #endregion
    }

    #region HMI DTO Models

    public class HmiConnectionDetails
    {
        public string Name { get; set; } = "";
        public string CommunicationDriver { get; set; } = "";
        public bool DisabledAtStartup { get; set; }
        public string Partner { get; set; } = "";
        public string Station { get; set; } = "";
        public string Node { get; set; } = "";
        public string InitialAddress { get; set; } = "";
        public string Comment { get; set; } = "";
    }

    public class HmiConnectionDeleteResult
    {
        public bool Success { get; set; }
        public string ConnectionName { get; set; } = "";
        public bool DeletedHardware { get; set; }
        public bool DeletedSoftware { get; set; }
        public string Message { get; set; } = "";
    }

    public class HmiTargetInfo
    {
        public string DeviceName { get; set; } = "";
        public string DeviceItemName { get; set; } = "";
        public string SoftwareName { get; set; } = "";
        public int ScreenCount { get; set; }
        public int TagTableCount { get; set; }
    }

    public class HmiScreenSummary
    {
        public string Name { get; set; } = "";
        public uint Width { get; set; }
        public uint Height { get; set; }
        public int ItemCount { get; set; }
    }

    public class HmiScreenItemDetails
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public int Left { get; set; }
        public int Top { get; set; }
        public uint Width { get; set; }
        public uint Height { get; set; }
        public int? CenterX { get; set; }
        public int? CenterY { get; set; }
        public uint? Radius { get; set; }
        public string BackColor { get; set; } = "";
        public string BorderColor { get; set; } = "";
        public uint BorderWidth { get; set; }
        public string? Text { get; set; }
        public string? TappedScript { get; set; }
        public string? IOFieldType { get; set; }
        public List<DynamizationInfo> Dynamizations { get; } = new List<DynamizationInfo>();
    }

    public class HmiEventScriptsDetails
    {
        public string ScreenName { get; set; } = "";
        public string TargetKind { get; set; } = "";
        public string TargetName { get; set; } = "";
        public List<string> SupportedEventTypes { get; set; } = new List<string>();
        public List<HmiEventScriptEntry> Events { get; } = new List<HmiEventScriptEntry>();
    }

    public class HmiEventScriptEntry
    {
        public string EventType { get; set; } = "";
        public string Script { get; set; } = "";
    }

    public class HmiEventScriptUpdateResult : HmiEventScriptEntry
    {
        public string ScreenName { get; set; } = "";
        public string TargetKind { get; set; } = "";
        public string TargetName { get; set; } = "";
        public List<string> Errors { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    internal sealed class EventHandlerSurface
    {
        public EventHandlerSurface(object handlers, MethodInfo create, MethodInfo? find, Type eventType)
        {
            Handlers = handlers;
            Create = create;
            Find = find;
            EventType = eventType;
        }

        public object Handlers { get; }
        public MethodInfo Create { get; }
        public MethodInfo? Find { get; }
        public Type EventType { get; }
    }

    public class DynamizationInfo
    {
        public string Property { get; set; } = "";
        public string Type { get; set; } = "";
        public string TagName { get; set; } = "";
        public string ConditionType { get; set; } = "";
        public List<RangeEntryDto> Ranges { get; } = new List<RangeEntryDto>();
        public List<BitmaskEntryDto> BitmaskEntries { get; } = new List<BitmaskEntryDto>();
    }

    public class BitmaskEntryDto
    {
        public string BitDynamizationType { get; set; } = "";
        public ulong Condition { get; set; }
        public ulong Relevant { get; set; }
        public string Value { get; set; } = "";
    }

    public class RangeEntryDto
    {
        public double From { get; set; }
        public double To { get; set; }
        public string Value { get; set; } = "";
    }

    public class HmiRangeEntryInput
    {
        public object From { get; set; } = 0;
        public object To { get; set; } = 0;
        public string Value { get; set; } = "";
    }

    public class HmiMappingValueInput
    {
        public string Type { get; set; } = "";
        public string Value { get; set; } = "";
    }

    public class ButtonBuildResult
    {
        public string ButtonName { get; set; } = "";
        public List<string> Errors { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public class ScriptUpdateSummary
    {
        public List<ModifiedButtonDetails> ModifiedButtons { get; } = new List<ModifiedButtonDetails>();
    }

    public class ModifiedButtonDetails
    {
        public string ButtonName { get; set; } = "";
        public string OldScript { get; set; } = "";
        public string NewScript { get; set; } = "";
        public List<string> Errors { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public class HmiTagDetails
    {
        public string Name { get; set; } = "";
        public string TableName { get; set; } = "";
        public string Connection { get; set; } = "";
        public string PlcTag { get; set; } = "";
        public string DataType { get; set; } = "";
        public string AcquisitionCycle { get; set; } = "";
        public string AcquisitionMode { get; set; } = "";
    }

    public class TextListEntryInput
    {
        public int Value { get; set; }
        public int? FromValue { get; set; }
        public int? ToValue { get; set; }
        public string Text { get; set; } = "";
        public Dictionary<string, string>? MultilingualTexts { get; set; }
    }

    public class HmiTextListResult
    {
        public bool Success { get; set; }
        public string ListName { get; set; } = "";
        public int EntryCount { get; set; }
        public string Message { get; set; } = "";
    }

    public class HmiTextListSummary
    {
        public string Name { get; set; } = "";
        public List<HmiTextListEntrySummary> Entries { get; set; } = new List<HmiTextListEntrySummary>();
    }

    public class HmiTextListEntrySummary
    {
        public int FromValue { get; set; }
        public int ToValue { get; set; }
        public string DefaultText { get; set; } = "";
        public Dictionary<string, string> MultilingualTexts { get; set; } = new Dictionary<string, string>();
    }

    #endregion
}
