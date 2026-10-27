using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiAlarm;
using Siemens.Engineering.HmiUnified.HmiAlarm.HmiAlarmCommon;
using Siemens.Engineering.HmiUnified.HmiTags;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.Dynamization;
using Siemens.Engineering.HmiUnified.UI.Dynamization.Tag;
using Siemens.Engineering.HmiUnified.UI.Screens;
using Siemens.Engineering.HmiUnified.UI.ScreenGroup;
using Siemens.Engineering.HmiUnified.UI.Shapes;
using Siemens.Engineering.HmiUnified.UI.Widgets;

namespace TiaOpennessMcp.Tia
{
    public partial class TiaManager
    {
        private static readonly object HmiJsonDefaultsLock = new object();
        private static readonly Dictionary<string, HmiJsonItem> HmiJsonDefaultsCache =
            new Dictionary<string, HmiJsonItem>(StringComparer.Ordinal);

        private static readonly JsonSerializerOptions HmiJsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private static readonly Dictionary<string, Type> HmiJsonItemTypes =
            new Dictionary<string, Type>(StringComparer.Ordinal)
            {
                ["HmiButton"] = typeof(HmiButton),
                ["HmiCircle"] = typeof(HmiCircle),
                ["HmiIOField"] = typeof(HmiIOField),
                ["HmiLine"] = typeof(HmiLine),
                ["HmiRectangle"] = typeof(HmiRectangle),
                ["HmiText"] = typeof(HmiText),
                ["HmiTextBox"] = typeof(HmiTextBox),
                ["HmiToggleSwitch"] = typeof(HmiToggleSwitch)
            };

        public Dictionary<string, object> ExportHmiJson(
            string directory,
            bool compact = true,
            bool scriptFiles = false,
            string? targetName = null)
        {
            EnsureProjectOpen();
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("An output directory is required.", nameof(directory));

            var hmi = FindHmiSoftware(targetName);
            string root = Path.GetFullPath(directory);
            string screensDirectory = Path.Combine(root, "screens");
            Directory.CreateDirectory(screensDirectory);
            List<string> languages = GetHmiLanguages(hmi);
            var warnings = EnumerateHmiScreenLocations(hmi.Screens, hmi.ScreenGroups, null)
                .SelectMany(location => location.Screen.ScreenItems
                    .OfType<HmiCustomWidgetContainer>()
                    .Select(item => $"Custom widget '{location.Screen.Name}/{item.Name}' is exported without default omission; " +
                        "its creation type is not exposed by V21. Import requires the existing widget and preserves its custom interface."))
                .ToList();
            var defaults = new Dictionary<string, HmiJsonItem>(StringComparer.Ordinal);
            if (compact)
            {
                var itemTypes = EnumerateHmiScreenLocations(hmi.Screens, hmi.ScreenGroups, null)
                    .SelectMany(location => location.Screen.ScreenItems)
                    .Select(CreateItemIdentity)
                    .ToList();
                using (ExclusiveAccess access = _tiaPortal!.ExclusiveAccess("Read Unified HMI item defaults"))
                using (Transaction transaction = access.Transaction(_project!, "Read Unified HMI item defaults"))
                    defaults = CreateDefaultItems(hmi, itemTypes, languages);
            }

            var fileHashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var screenFiles = new List<HmiJsonScreenFileReference>();
            int screenIndex = 0;
            foreach (HmiJsonScreenLocation location in EnumerateHmiScreenLocations(hmi.Screens, hmi.ScreenGroups, null))
            {
                screenIndex++;
                string fileName = screenIndex.ToString("D4", CultureInfo.InvariantCulture) + "-" +
                    MakeFileNameSafe(location.Screen.Name) + ".json";
                string relativeFile = NormalizeManifestPath(Path.Combine("screens", fileName));
                string fullPath = ResolveManifestPathForWrite(root, relativeFile);
                HmiJsonScreen document = CreateScreenDocument(location.Screen, location.Group, languages, defaults, compact);
                if (scriptFiles)
                    WriteScriptSidecars(root, screenIndex, location.Screen.Name, document, fileHashes);
                WriteJsonFile(fullPath, document);
                fileHashes[relativeFile] = ComputeFileHash(fullPath);
                screenFiles.Add(new HmiJsonScreenFileReference
                {
                    Name = location.Screen.Name,
                    Group = location.Group,
                    Path = relativeFile
                });
            }

            var tagFiles = new List<string>();
            int tableIndex = 0;
            foreach (HmiTagTable table in hmi.TagTables)
            {
                tableIndex++;
                string relativeFile = NormalizeManifestPath(Path.Combine(
                    "tags", tableIndex.ToString("D4", CultureInfo.InvariantCulture) + "-" +
                    MakeFileNameSafe(table.Name) + ".json"));
                string fullPath = ResolveManifestPathForWrite(root, relativeFile);
                WriteJsonFile(fullPath, new HmiJsonTagFile
                {
                    Format = "tia-mcp-hmi/1",
                    Table = table.Name,
                    Tags = table.Tags.Select(CreateTagDocument).ToList()
                });
                fileHashes[relativeFile] = ComputeFileHash(fullPath);
                tagFiles.Add(relativeFile);
            }

            const string alarmFile = "alarms/discrete.json";
            string alarmPath = ResolveManifestPathForWrite(root, alarmFile);
            WriteJsonFile(alarmPath, new HmiJsonAlarmFile
            {
                Format = "tia-mcp-hmi/1",
                DiscreteAlarms = hmi.DiscreteAlarms.Select(alarm => CreateAlarmDocument(alarm, languages)).ToList()
            });
            fileHashes[alarmFile] = ComputeFileHash(alarmPath);

            const string stylesFile = "styles.json";
            string stylesPath = ResolveManifestPathForWrite(root, stylesFile);
            WriteJsonFile(stylesPath, new HmiJsonStylesFile { Format = "tia-mcp-hmi/1" });
            fileHashes[stylesFile] = ComputeFileHash(stylesPath);

            string? defaultsFile = null;
            if (compact)
            {
                defaultsFile = "defaults.json";
                string defaultsPath = ResolveManifestPathForWrite(root, defaultsFile);
                WriteJsonFile(defaultsPath, new
                {
                    format = "tia-mcp-hmi/1",
                    items = defaults.OrderBy(d => d.Key, StringComparer.Ordinal).Select(d => new
                    {
                        type = d.Value.Type,
                        containedType = d.Value.ContainedType,
                        props = d.Value.Props,
                        text = d.Value.Text
                    }).ToList()
                });
                fileHashes[defaultsFile] = ComputeFileHash(defaultsPath);
            }

            WriteJsonFile(Path.Combine(root, "manifest.json"), new HmiJsonManifest
            {
                Format = "tia-mcp-hmi/1",
                TargetName = hmi.Name,
                ScreenFiles = screenFiles,
                TagFiles = tagFiles,
                DiscreteAlarmFile = alarmFile,
                StylesFile = stylesFile,
                DefaultsFile = defaultsFile,
                Compact = compact,
                TiaVersion = typeof(TiaPortal).Assembly.GetName().Version?.ToString() ?? "unknown",
                Languages = languages,
                ExportedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                FileHashes = fileHashes,
                Warnings = warnings
            });

            return new Dictionary<string, object>
            {
                ["directory"] = root,
                ["targetName"] = hmi.Name,
                ["screenCount"] = screenFiles.Count,
                ["tagCount"] = hmi.Tags.Count,
                ["discreteAlarmCount"] = hmi.DiscreteAlarms.Count,
                ["screenFiles"] = screenFiles.Select(f => f.Path).ToList(),
                ["fileHashes"] = fileHashes,
                ["warnings"] = warnings
            };
        }

        public Dictionary<string, object> ImportHmiJson(
            string directory,
            bool pruneItems = false,
            bool dryRun = false,
            string? targetName = null,
            bool verifyHashes = true)
        {
            EnsureProjectOpen();
            var hmi = FindHmiSoftware(targetName);
            HmiJsonImportPlan plan = ReadHmiJsonImportPlan(directory, verifyHashes);
            ValidateHmiImportPlan(hmi, plan);
            ValidateHmiRequirements(hmi, plan.Manifest.Require);

            if (dryRun)
                return CreateImportResult(plan, hmi.Name, true, pruneItems, null);

            using (ExclusiveAccess access = _tiaPortal!.ExclusiveAccess("Import Unified HMI JSON"))
            using (Transaction transaction = access.Transaction(_project!, "Import Unified HMI JSON"))
            {
                var itemTypes = plan.Screens.SelectMany(s => s.Items).ToList();
                Dictionary<string, HmiJsonItem> defaults = plan.Manifest.Compact
                    ? CreateDefaultItems(hmi, itemTypes, plan.Manifest.Languages)
                    : new Dictionary<string, HmiJsonItem>(StringComparer.Ordinal);
                ApplyHmiImportPlan(hmi, plan, pruneItems, defaults);
                VerifyHmiImportPlan(hmi, plan);
                VerifyHmiPatch(hmi, plan.Manifest.Verify);
                transaction.CommitOnDispose();
            }

            HmiTargetInfo target = ListHmiTargets().Single(t =>
                string.Equals(t.SoftwareName, hmi.Name, StringComparison.OrdinalIgnoreCase));
            CompilationReport compilation = CompileHmi(target.DeviceName);
            var result = CreateImportResult(plan, hmi.Name, false, pruneItems, compilation);
            if (compilation.ErrorCount > 0)
                throw new InvalidOperationException(
                    $"HMI JSON import was applied in memory but compilation reported {compilation.ErrorCount} error(s). " +
                    "The project has not been saved. Compile diagnostics: " +
                    JsonSerializer.Serialize(compilation.Messages, HmiJsonOptions));
            return result;
        }

        public Dictionary<string, object> ApplyHmiJsonPatch(
            string filePath,
            bool dryRun = false,
            string? targetName = null)
        {
            EnsureProjectOpen();
            var hmi = FindHmiSoftware(targetName);
            HmiJsonPatch patch = ReadJsonFile<HmiJsonPatch>(filePath);
            if (!string.Equals(patch.Format, "tia-mcp-hmi-patch/1", StringComparison.Ordinal))
                throw new InvalidDataException($"Unsupported HMI patch format '{patch.Format}'.");
            ValidatePatchRequirements(hmi, patch.Require);
            ValidatePatchOperations(hmi, patch.Operations);
            List<string> languages = GetHmiLanguages(hmi);

            if (dryRun)
                return new Dictionary<string, object>
                {
                    ["targetName"] = hmi.Name,
                    ["dryRun"] = true,
                    ["operationCount"] = patch.Operations.Count,
                    ["operations"] = patch.Operations.Select(DescribePatchOperation).ToList()
                };

            using (ExclusiveAccess access = _tiaPortal!.ExclusiveAccess("Apply Unified HMI JSON patch"))
            using (Transaction transaction = access.Transaction(_project!, "Apply Unified HMI JSON patch"))
            {
                foreach (HmiJsonPatchOperation operation in patch.Operations)
                    ApplyPatchOperation(hmi, operation, languages);
                VerifyPatchRequirements(hmi, patch.Require);
                VerifyHmiPatch(hmi, patch.Verify);
                transaction.CommitOnDispose();
            }

            HmiTargetInfo target = ListHmiTargets().Single(t =>
                string.Equals(t.SoftwareName, hmi.Name, StringComparison.OrdinalIgnoreCase));
            CompilationReport compilation = CompileHmi(target.DeviceName);
            if (compilation.ErrorCount > 0)
                throw new InvalidOperationException(
                    $"HMI JSON patch was applied in memory but compilation reported {compilation.ErrorCount} error(s). " +
                    "The project has not been saved. Compile diagnostics: " +
                    JsonSerializer.Serialize(compilation.Messages, HmiJsonOptions));

            return new Dictionary<string, object>
            {
                ["targetName"] = hmi.Name,
                ["dryRun"] = false,
                ["operationCount"] = patch.Operations.Count,
                ["operations"] = patch.Operations.Select(DescribePatchOperation).ToList(),
                ["compilation"] = compilation
            };
        }

        private static HmiJsonScreen CreateScreenDocument(
            HmiScreen screen,
            string? group,
            IReadOnlyList<string> languages,
            Dictionary<string, HmiJsonItem> defaults,
            bool compact)
        {
            var props = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            var text = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (PropertyInfo property in GetSerializableProperties(screen.GetType()))
            {
                if (property.Name == "Name" || property.Name == "ScreenItems" || property.Name == "Parent")
                    continue;
                object? value = property.GetValue(screen, null);
                if (value == null) continue;
                if (IsMultilingualText(property.PropertyType))
                    text[property.Name] = SerializeMultilingualText(value, languages);
                else if (IsSupportedValue(property.PropertyType) || IsPartType(property.PropertyType))
                    props[property.Name] = SerializePropertyValue(value);
            }
            return new HmiJsonScreen
            {
                Format = "tia-mcp-hmi/1",
                Name = screen.Name,
                Group = group,
                Props = props,
                Text = text,
                Items = screen.ScreenItems.Select(item =>
                    CreateItemDocument(item, languages, defaults, compact)).ToList()
            };
        }

        private static IEnumerable<HmiJsonScreenLocation> EnumerateHmiScreenLocations(
            HmiScreenComposition screens,
            HmiScreenGroupComposition groups,
            string? parentPath)
        {
            foreach (HmiScreen screen in screens)
                yield return new HmiJsonScreenLocation { Screen = screen, Group = parentPath };
            foreach (HmiScreenGroup group in groups)
            {
                string path = string.IsNullOrEmpty(parentPath) ? group.Name : parentPath + "/" + group.Name;
                foreach (HmiJsonScreenLocation nested in EnumerateHmiScreenLocations(group.Screens, group.Groups, path))
                    yield return nested;
            }
        }

        private static string? GetScreenGroupPath(HmiScreen screen)
        {
            var groupNames = new List<string>();
            object? parent = screen.GetType().GetProperty("Parent")?.GetValue(screen, null);
            while (parent != null && parent.GetType().Name == "HmiScreenGroup")
            {
                string? name = parent.GetType().GetProperty("Name")?.GetValue(parent, null)?.ToString();
                if (name != null && name.Length > 0)
                    groupNames.Add(name);
                parent = parent.GetType().GetProperty("Parent")?.GetValue(parent, null);
            }
            groupNames.Reverse();
            return groupNames.Count == 0 ? null : string.Join("/", groupNames);
        }

        private static HmiScreen? FindHmiScreen(HmiSoftware hmi, string name, string? group)
        {
            var matches = EnumerateHmiScreenLocations(hmi.Screens, hmi.ScreenGroups, null)
                .Where(location => string.Equals(location.Screen.Name, name, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(location.Group, group, StringComparison.OrdinalIgnoreCase))
                .Select(location => location.Screen)
                .ToList();
            if (matches.Count > 1)
                throw new InvalidOperationException($"Screen '{group ?? "(root)"}/{name}' is not unique.");
            return matches.FirstOrDefault();
        }

        private static HmiScreen CreateScreenInGroup(HmiSoftware hmi, string name, string? groupPath)
        {
            if (groupPath == null || string.IsNullOrWhiteSpace(groupPath))
                return hmi.Screens.Create(name);
            HmiScreenGroupComposition groups = hmi.ScreenGroups;
            HmiScreenGroup? group = null;
            foreach (string segment in groupPath.Split('/'))
            {
                if (string.IsNullOrWhiteSpace(segment))
                    throw new InvalidDataException($"Invalid HMI screen group path '{groupPath}'.");
                group = groups.Find(segment) ?? groups.Create(segment);
                if (group == null)
                    throw new InvalidOperationException($"Could not find or create HMI screen group '{segment}'.");
                HmiScreenGroupComposition? nestedGroups = group.Groups;
                if (nestedGroups == null)
                    throw new InvalidOperationException($"HMI screen group '{segment}' does not expose nested groups.");
                groups = nestedGroups;
            }
            return group!.Screens.Create(name);
        }

        private static HmiJsonItem CreateItemDocument(
            HmiScreenItemBase item,
            IReadOnlyList<string> languages,
            Dictionary<string, HmiJsonItem>? defaults = null,
            bool compact = false)
        {
            var document = new HmiJsonItem
            {
                Name = item.Name,
                Type = item.GetType().Name,
                ContainedType = (item as HmiContainerBase)?.ContainedType,
                Compact = item is HmiCustomWidgetContainer ? false : (bool?)null,
                Props = new Dictionary<string, JsonElement>(StringComparer.Ordinal),
                Text = new Dictionary<string, JsonElement>(StringComparer.Ordinal),
                Dynamizations = new List<HmiJsonDynamization>(),
                Events = new List<HmiJsonEvent>()
            };

            foreach (PropertyInfo property in GetSerializableProperties(item.GetType()))
            {
                if (string.Equals(property.Name, "Name", StringComparison.Ordinal))
                    continue;

                object? value = property.GetValue(item, null);
                if (value == null)
                    continue;

                if (IsMultilingualText(property.PropertyType))
                {
                    document.Text[property.Name] = SerializeMultilingualText(value, languages);
                    continue;
                }

                if (IsSupportedValue(property.PropertyType) || IsPartType(property.PropertyType))
                {
                    document.Props[property.Name] = SerializePropertyValue(value);
                }
            }

            document.Dynamizations = item.Dynamizations.Select(CreateDynamizationDocument).ToList();

            document.Events = ReadItemEvents(item);
            if (compact && defaults != null && defaults.TryGetValue(GetItemIdentityKey(document), out HmiJsonItem? defaultItem))
            {
                RemoveDefaultValues(document.Props, defaultItem.Props);
                RemoveDefaultValues(document.Text, defaultItem.Text);
            }
            return document;
        }

        private Dictionary<string, HmiJsonItem> CreateDefaultItems(
            HmiSoftware hmi,
            IEnumerable<HmiJsonItem> itemTypes,
            IReadOnlyList<string> languages)
        {
            var defaults = new Dictionary<string, HmiJsonItem>(StringComparer.Ordinal);
            lock (HmiJsonDefaultsLock)
            {
                string languageKey = string.Join("|", languages);
                var missingTypes = new List<HmiJsonItem>();
                foreach (HmiJsonItem itemType in itemTypes.GroupBy(GetItemIdentityKey).Select(g => g.First()))
                {
                    if (itemType.Type == nameof(HmiCustomWidgetContainer))
                        continue;
                    string cacheKey = GetDefaultCacheKey(hmi, itemType, languageKey);
                    if (HmiJsonDefaultsCache.TryGetValue(cacheKey, out HmiJsonItem? cached))
                        defaults[GetItemIdentityKey(itemType)] = cached;
                    else
                        missingTypes.Add(itemType);
                }

                if (missingTypes.Count == 0)
                    return defaults;

                HmiScreen? screen = EnumerateHmiScreens(hmi.Screens, hmi.ScreenGroups).FirstOrDefault();
                bool temporaryScreen = screen == null;
                screen = screen ?? hmi.Screens.Create("__HmiJsonDefaults_" + Guid.NewGuid().ToString("N"));
                foreach (HmiJsonItem itemType in missingTypes)
                {
                    string itemName = "__HmiJsonDefaults_" + Guid.NewGuid().ToString("N");
                    HmiScreenItemBase item = CreateScreenItem(screen, itemName,
                        ResolveHmiScreenItemType(itemType.Type), itemType.ContainedType);
                    HmiJsonItem snapshot = CreateItemDocument(item, languages);
                    item.Delete();
                    HmiJsonDefaultsCache[GetDefaultCacheKey(hmi, itemType, languageKey)] = snapshot;
                    defaults[GetItemIdentityKey(itemType)] = snapshot;
                }
                if (temporaryScreen)
                    screen.Delete();
            }
            return defaults;
        }

        private static HmiJsonItem CreateItemIdentity(HmiScreenItemBase item)
        {
            return new HmiJsonItem
            {
                Type = item.GetType().Name,
                ContainedType = (item as HmiContainerBase)?.ContainedType,
                Compact = item is HmiCustomWidgetContainer ? false : (bool?)null
            };
        }

        private static string GetItemIdentityKey(HmiJsonItem item)
        {
            return item.Type + "|" + item.ContainedType;
        }

        private string GetDefaultCacheKey(HmiSoftware hmi, HmiJsonItem itemType, string languageKey)
        {
            return string.Join("|",
                _project?.Path.FullName ?? "",
                hmi.Name,
                typeof(TiaPortal).Assembly.GetName().Version?.ToString() ?? "",
                GetItemIdentityKey(itemType),
                languageKey);
        }

        private static void RemoveDefaultValues(
            Dictionary<string, JsonElement> properties,
            Dictionary<string, JsonElement> defaults)
        {
            foreach (string key in properties.Keys.ToList())
                if (defaults.TryGetValue(key, out JsonElement defaultValue) &&
                    AreJsonValuesEqual(properties[key], defaultValue))
                    properties.Remove(key);
        }

        private static bool AreJsonValuesEqual(JsonElement left, JsonElement right)
        {
            if (left.ValueKind != right.ValueKind) return false;
            switch (left.ValueKind)
            {
                case JsonValueKind.Object:
                    var leftProperties = left.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
                    var rightProperties = right.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
                    return leftProperties.Count == rightProperties.Count &&
                        leftProperties.All(p => rightProperties.TryGetValue(p.Key, out JsonElement other) &&
                            AreJsonValuesEqual(p.Value, other));
                case JsonValueKind.Array:
                    JsonElement.ArrayEnumerator leftArray = left.EnumerateArray();
                    JsonElement.ArrayEnumerator rightArray = right.EnumerateArray();
                    var leftValues = leftArray.ToList();
                    var rightValues = rightArray.ToList();
                    return leftValues.Count == rightValues.Count &&
                        leftValues.Zip(rightValues, AreJsonValuesEqual).All(equal => equal);
                case JsonValueKind.String:
                    return left.GetString() == right.GetString();
                case JsonValueKind.Number:
                    return left.GetDecimal() == right.GetDecimal();
                case JsonValueKind.True:
                case JsonValueKind.False:
                    return left.GetBoolean() == right.GetBoolean();
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    return true;
                default:
                    return left.GetRawText() == right.GetRawText();
            }
        }

        private static void WriteScriptSidecars(
            string root,
            int screenIndex,
            string screenName,
            HmiJsonScreen screen,
            IDictionary<string, string> fileHashes)
        {
            for (int itemIndex = 0; itemIndex < screen.Items.Count; itemIndex++)
            {
                HmiJsonItem item = screen.Items[itemIndex];
                for (int eventIndex = 0; eventIndex < item.Events.Count; eventIndex++)
                {
                    HmiJsonEvent itemEvent = item.Events[eventIndex];
                    if (string.IsNullOrEmpty(itemEvent.Script))
                        continue;
                    string relativePath = NormalizeManifestPath(Path.Combine(
                        "scripts",
                        screenIndex.ToString("D4", CultureInfo.InvariantCulture) + "-" + MakeFileNameSafe(screenName),
                        itemIndex.ToString("D4", CultureInfo.InvariantCulture) + "-" +
                        MakeFileNameSafe(item.Name) + "-" + MakeFileNameSafe(itemEvent.Type) + ".js"));
                    string fullPath = ResolveManifestPathForWrite(root, relativePath);
                    File.WriteAllText(fullPath, itemEvent.Script);
                    fileHashes[relativePath] = ComputeFileHash(fullPath);
                    itemEvent.ScriptFile = relativePath;
                    itemEvent.Script = null;
                }
            }
        }

        private static HmiJsonTag CreateTagDocument(HmiTag tag)
        {
            return new HmiJsonTag
            {
                Name = tag.Name,
                Table = tag.TagTableName,
                Connection = tag.Connection,
                PlcTag = tag.PlcTag,
                DataType = tag.DataType,
                Min = CreateLimitDocument(tag.InitialMinValue),
                Max = CreateLimitDocument(tag.InitialMaxValue)
            };
        }

        private static JsonElement? CreateLimitDocument(dynamic limit)
        {
            if (limit.ValueType == HmiLimitValueType.None)
                return null;
            if (limit.ValueType != HmiLimitValueType.Constant)
                throw new InvalidDataException("Tag-based HMI limits are not supported by this JSON format.");
            return JsonSerializer.SerializeToElement(Convert.ToDouble(limit.Value, CultureInfo.InvariantCulture));
        }

        private static JsonElement? SerializeNullableObject(object? value)
        {
            return value == null ? (JsonElement?)null : JsonSerializer.SerializeToElement(value, value.GetType());
        }

        private static HmiJsonAlarm CreateAlarmDocument(HmiDiscreteAlarm alarm, IReadOnlyList<string> languages)
        {
            return new HmiJsonAlarm
            {
                Name = alarm.Name,
                AlarmClass = alarm.AlarmClass,
                RaisedStateTag = alarm.RaisedStateTag,
                RaisedStateTagBitNumber = alarm.RaisedStateTagBitNumber,
                TriggerMode = alarm.TriggerMode.ToString(),
                AcknowledgmentControlTag = alarm.AcknowledgmentControlTag,
                EventText = SerializeMultilingualText(alarm.EventText, languages)
            };
        }

        private static IEnumerable<HmiTag> EnumerateHmiTags(HmiSoftware hmi)
        {
            foreach (HmiTag tag in hmi.Tags)
                yield return tag;
        }

        private static string GetTagLookupName(string reference) =>
            System.Text.RegularExpressions.Regex.Replace(reference, @"\[(-?\d+(?:,\s*-?\d+)*)\]", "{$1}");

        private static string? GetArrayTagRoot(string reference, Func<string, string?> getDataType)
        {
            var match = System.Text.RegularExpressions.Regex.Match(reference, @"^(.+)\[(-?\d+(?:,\s*-?\d+)*)\]$");
            if (!match.Success) return null;
            string root = match.Groups[1].Value;
            string? dataType = getDataType(root);
            if (dataType == null) return null;
            var array = System.Text.RegularExpressions.Regex.Match(dataType, @"^Array\s*\[([^\]]+)\]\s+of\s+.+$");
            if (!array.Success) return null;
            string[] indices = match.Groups[2].Value.Split(',');
            string[] bounds = array.Groups[1].Value.Split(',');
            if (indices.Length != bounds.Length) return null;
            for (int i = 0; i < indices.Length; i++)
            {
                var range = System.Text.RegularExpressions.Regex.Match(bounds[i].Trim(), @"^(-?\d+)\.\.(-?\d+)$");
                if (!range.Success || !int.TryParse(indices[i].Trim(), out int index) ||
                    !int.TryParse(range.Groups[1].Value, out int lower) ||
                    !int.TryParse(range.Groups[2].Value, out int upper) || index < lower || index > upper)
                    return null;
            }
            return root;
        }

        private static HmiTag? FindReferencedTag(HmiSoftware hmi, string reference)
        {
            HmiTag? tag = hmi.Tags.Find(reference) ?? hmi.Tags.Find(GetTagLookupName(reference));
            if (tag != null) return tag;
            string? root = GetArrayTagRoot(reference, name => hmi.Tags.Find(name)?.DataType);
            return root == null ? null : hmi.Tags.Find(root);
        }

        private static bool ContainsTagReference(Dictionary<string, string> tags, string reference) =>
            tags.ContainsKey(reference) || tags.ContainsKey(GetTagLookupName(reference)) ||
            GetArrayTagRoot(reference, name => tags.TryGetValue(name, out string? type) ? type : null) != null;

        private static void VerifyManifestHash(string root, HmiJsonManifest manifest, string relativePath, string path)
        {
            if (!manifest.FileHashes.TryGetValue(NormalizeManifestPath(relativePath), out string? expected))
                throw new InvalidDataException($"Manifest has no SHA-256 hash for '{relativePath}'.");
            string actual = ComputeFileHash(path);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"SHA-256 mismatch for '{relativePath}'.");
        }

        private static string ComputeFileHash(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        private static string NormalizeManifestPath(string path)
        {
            return path.Replace('\\', '/');
        }

        private static string ResolveManifestPathForWrite(string root, string relativePath)
        {
            string normalized = NormalizeManifestPath(relativePath);
            if (Path.IsPathRooted(normalized))
                throw new InvalidDataException("Manifest output paths must be relative.");
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
            if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Output path '{relativePath}' escapes the export directory.");
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            return fullPath;
        }

        private HmiJsonImportPlan ReadHmiJsonImportPlan(string directory, bool verifyHashes)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("An import directory is required.", nameof(directory));
            string root = Path.GetFullPath(directory);
            string manifestPath = Path.Combine(root, "manifest.json");
            HmiJsonManifest manifest = ReadJsonFile<HmiJsonManifest>(manifestPath);
            if (!string.Equals(manifest.Format, "tia-mcp-hmi/1", StringComparison.Ordinal))
                throw new InvalidDataException($"Unsupported HMI JSON format '{manifest.Format}'.");
            if (manifest.ScreenFiles.Count > 2000)
                throw new InvalidDataException("The manifest contains too many screen files.");

            var plan = new HmiJsonImportPlan
            {
                Root = root,
                Manifest = manifest,
                Screens = manifest.ScreenFiles.Select(file =>
                {
                    string screenPath = ResolveManifestPath(root, file.Path);
                    if (verifyHashes)
                        VerifyManifestHash(root, manifest, file.Path, screenPath);
                    HmiJsonScreen screen = ReadJsonFile<HmiJsonScreen>(screenPath);
                    if (!string.Equals(screen.Format, "tia-mcp-hmi/1", StringComparison.Ordinal))
                        throw new InvalidDataException($"Unsupported screen format '{screen.Format}' in '{file.Path}'.");
                    if (!string.Equals(screen.Name, file.Name, StringComparison.Ordinal) ||
                        !string.Equals(screen.Group, file.Group, StringComparison.Ordinal))
                        throw new InvalidDataException($"Screen identity in '{file.Path}' does not match the manifest.");
                    if (screen.Items.Count > 10000)
                        throw new InvalidDataException($"Screen '{screen.Name}' contains too many items.");
                    foreach (HmiJsonEvent itemEvent in screen.Items.SelectMany(i => i.Events))
                    {
                        if (itemEvent.ScriptFile == null || string.IsNullOrWhiteSpace(itemEvent.ScriptFile))
                            continue;
                        string scriptPath = ResolveManifestPath(root, itemEvent.ScriptFile);
                        if (verifyHashes)
                            VerifyManifestHash(root, manifest, itemEvent.ScriptFile, scriptPath);
                        var scriptInfo = new FileInfo(scriptPath);
                        if (scriptInfo.Length > 1024 * 1024)
                            throw new InvalidDataException($"HMI script exceeds the 1 MiB limit: {itemEvent.ScriptFile}");
                        itemEvent.Script = File.ReadAllText(scriptPath);
                    }
                    return screen;
                }).ToList(),
                Tags = new List<HmiJsonTag>(),
                Alarms = new List<HmiJsonAlarm>(),
                Styles = new Dictionary<string, Dictionary<string, JsonElement>>(StringComparer.Ordinal)
            };
            foreach (string tagFile in manifest.TagFiles)
            {
                string path = ResolveManifestPath(root, tagFile);
                if (verifyHashes)
                    VerifyManifestHash(root, manifest, tagFile, path);
                HmiJsonTagFile file = ReadJsonFile<HmiJsonTagFile>(path);
                if (!string.Equals(file.Format, "tia-mcp-hmi/1", StringComparison.Ordinal))
                    throw new InvalidDataException($"Unsupported tag file format '{file.Format}'.");
                foreach (HmiJsonTag tag in file.Tags)
                {
                    if (string.IsNullOrWhiteSpace(tag.Table))
                        tag.Table = file.Table;
                    plan.Tags.Add(tag);
                }
            }
            string alarmPath = ResolveManifestPath(root, manifest.DiscreteAlarmFile);
            if (verifyHashes)
                VerifyManifestHash(root, manifest, manifest.DiscreteAlarmFile, alarmPath);
            HmiJsonAlarmFile alarmDocument = ReadJsonFile<HmiJsonAlarmFile>(alarmPath);
            if (!string.Equals(alarmDocument.Format, "tia-mcp-hmi/1", StringComparison.Ordinal))
                throw new InvalidDataException($"Unsupported alarm file format '{alarmDocument.Format}'.");
            plan.Alarms = alarmDocument.DiscreteAlarms;
            if (manifest.StylesFile != null && !string.IsNullOrWhiteSpace(manifest.StylesFile))
            {
                string stylesPath = ResolveManifestPath(root, manifest.StylesFile);
                if (verifyHashes)
                    VerifyManifestHash(root, manifest, manifest.StylesFile, stylesPath);
                HmiJsonStylesFile stylesDocument = ReadJsonFile<HmiJsonStylesFile>(stylesPath);
                if (!string.Equals(stylesDocument.Format, "tia-mcp-hmi/1", StringComparison.Ordinal))
                    throw new InvalidDataException($"Unsupported styles file format '{stylesDocument.Format}'.");
                plan.Styles = stylesDocument.Styles;
            }
            if (!string.IsNullOrWhiteSpace(manifest.DefaultsFile))
            {
                string defaultsPath = ResolveManifestPath(root, manifest.DefaultsFile!);
                if (verifyHashes)
                    VerifyManifestHash(root, manifest, manifest.DefaultsFile!, defaultsPath);
            }
            ValidateUniqueNames(plan);
            return plan;
        }

        private static void ValidateUniqueNames(HmiJsonImportPlan plan)
        {
            EnsureUnique(plan.Screens.Select(s => (s.Group ?? "") + "/" + s.Name), "screen path");
            foreach (HmiJsonScreen screen in plan.Screens)
                EnsureUnique(screen.Items.Select(i => i.Name), $"item on screen '{screen.Name}'");
            EnsureUnique(plan.Tags.Select(t => t.Name), "tag");
            EnsureUnique(plan.Alarms.Select(a => a.Name), "discrete alarm");
        }

        private static void EnsureUnique(IEnumerable<string> names, string itemDescription)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in names)
            {
                if (string.IsNullOrWhiteSpace(name) || !seen.Add(name))
                    throw new InvalidDataException($"The HMI JSON contains an empty or duplicate {itemDescription} name '{name}'.");
            }
        }

        private static void ValidateHmiImportPlan(HmiSoftware hmi, HmiJsonImportPlan plan)
        {
            foreach (HmiJsonTag tag in plan.Tags)
            {
                if (string.IsNullOrWhiteSpace(tag.Name))
                    throw new InvalidDataException("Every HMI tag requires a name.");
                if (!string.IsNullOrWhiteSpace(tag.Connection) && tag.Connection != "<Internal tag>" &&
                    string.IsNullOrWhiteSpace(tag.PlcTag))
                    throw new InvalidDataException($"HMI tag '{tag.Name}' requires a PLC tag path.");
                if (string.IsNullOrWhiteSpace(tag.PlcTag) && string.IsNullOrWhiteSpace(tag.DataType))
                    throw new InvalidDataException($"Internal HMI tag '{tag.Name}' requires dataType.");
                if (string.IsNullOrWhiteSpace(tag.Table))
                    throw new InvalidDataException($"HMI tag '{tag.Name}' requires a tag table.");
                ValidateLimitValue(tag.Min, tag.Name + ".min");
                ValidateLimitValue(tag.Max, tag.Name + ".max");
            }

            var suppliedTags = EnumerateHmiTags(hmi).ToDictionary(t => t.Name, t => t.DataType,
                StringComparer.OrdinalIgnoreCase);
            foreach (HmiJsonTag tag in plan.Tags)
                suppliedTags[tag.Name] = tag.DataType ??
                    (suppliedTags.TryGetValue(tag.Name, out string? existingType) ? existingType : "");
            foreach (HmiJsonAlarm alarm in plan.Alarms)
            {
                if (string.IsNullOrWhiteSpace(alarm.AlarmClass) || hmi.AlarmClasses.Find(alarm.AlarmClass) == null)
                    throw new InvalidDataException($"Discrete alarm '{alarm.Name}' requires an existing alarm class.");
                if (alarm.EventText.HasValue)
                    ValidateMultilingualText(alarm.EventText.Value, "alarm " + alarm.Name + ".eventText");
                if (alarm.RaisedStateTag == null || string.IsNullOrWhiteSpace(alarm.RaisedStateTag) ||
                    !ContainsTagReference(suppliedTags, alarm.RaisedStateTag))
                    throw new InvalidDataException($"Discrete alarm '{alarm.Name}' references missing HMI tag '{alarm.RaisedStateTag}'.");
                if (!Enum.TryParse(alarm.TriggerMode, true, out HmiDiscreteAlarmTriggerMode triggerMode) ||
                    !Enum.IsDefined(typeof(HmiDiscreteAlarmTriggerMode), triggerMode))
                    throw new InvalidDataException($"Discrete alarm '{alarm.Name}' has unsupported trigger mode '{alarm.TriggerMode}'.");
                if (alarm.AcknowledgmentControlTag != null &&
                    !string.IsNullOrWhiteSpace(alarm.AcknowledgmentControlTag) &&
                    alarm.AcknowledgmentControlTag != "<No tag>" &&
                    !ContainsTagReference(suppliedTags, alarm.AcknowledgmentControlTag))
                    throw new InvalidDataException($"Discrete alarm '{alarm.Name}' references missing acknowledgment tag '{alarm.AcknowledgmentControlTag}'.");
            }

            foreach (HmiJsonScreen screenDocument in plan.Screens)
            {
                if (!screenDocument.Props.TryGetValue("Width", out JsonElement screenWidth) ||
                    !screenDocument.Props.TryGetValue("Height", out JsonElement screenHeight) ||
                    screenWidth.GetUInt32() == 0 || screenHeight.GetUInt32() == 0)
                    throw new InvalidDataException($"Screen '{screenDocument.Name}' must include nonzero Width and Height properties.");
                ValidateProperties(typeof(HmiScreen), screenDocument.Props);
                ValidateTexts(typeof(HmiScreen), screenDocument.Text);
                foreach (HmiJsonItem itemDocument in screenDocument.Items)
                {
                    Type itemType = ResolveHmiScreenItemType(itemDocument.Type);
                    if (typeof(HmiContainerBase).IsAssignableFrom(itemType) &&
                        string.IsNullOrWhiteSpace(itemDocument.ContainedType))
                        throw new InvalidDataException($"Container '{itemDocument.Name}' requires containedType.");
                    if (itemType == typeof(HmiCustomWidgetContainer) &&
                        FindHmiScreen(hmi, screenDocument.Name, screenDocument.Group)?.ScreenItems.Find(itemDocument.Name) == null)
                        throw new InvalidDataException(
                            $"Custom widget '{screenDocument.Name}/{itemDocument.Name}' must already exist: V21 does not expose its creation type in the public API.");
                    if (itemDocument.Props.Count > 256 || itemDocument.Text.Count > 64)
                        throw new InvalidDataException($"Item '{screenDocument.Name}/{itemDocument.Name}' has too many properties.");
                    ValidateProperties(itemType, itemDocument.Props);
                    ValidateTexts(itemType, itemDocument.Text);
                    var effectiveProperties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                    if (itemDocument.Style != null && !string.IsNullOrWhiteSpace(itemDocument.Style))
                    {
                        if (!plan.Styles.TryGetValue(itemDocument.Style, out Dictionary<string, JsonElement>? style))
                            throw new InvalidDataException(
                                $"Item '{screenDocument.Name}/{itemDocument.Name}' references missing style '{itemDocument.Style}'.");
                        foreach (var styleProperty in style)
                            effectiveProperties[styleProperty.Key] = styleProperty.Value;
                    }
                    foreach (var itemProperty in itemDocument.Props)
                        effectiveProperties[itemProperty.Key] = itemProperty.Value;
                    ValidateProperties(itemType, effectiveProperties);
                    foreach (HmiJsonDynamization dynamization in itemDocument.Dynamizations)
                    {
                        ValidateDynamizationDocument(itemType, dynamization, suppliedTags);
                    }
                    EnsureUnique(itemDocument.Dynamizations.Select(d => d.Property),
                        $"dynamization on '{screenDocument.Name}/{itemDocument.Name}'");
                    ValidateEvents(itemType, itemDocument.Events);
                }
            }
        }

        private static void ValidateLimitValue(JsonElement? value, string name)
        {
            if (!value.HasValue || value.Value.ValueKind == JsonValueKind.Null) return;
            if (value.Value.ValueKind != JsonValueKind.Number || !value.Value.TryGetDouble(out _))
                throw new InvalidDataException($"HMI tag limit '{name}' must be a number or null.");
        }

        private static void ValidateProperties(Type itemType, Dictionary<string, JsonElement> properties)
        {
            foreach (KeyValuePair<string, JsonElement> entry in properties)
            {
                PropertyInfo property = FindProperty(itemType, entry.Key);
                if (string.Equals(property.Name, "Name", StringComparison.Ordinal) ||
                    IsMultilingualText(property.PropertyType) ||
                    !(IsSupportedValue(property.PropertyType) || IsPartType(property.PropertyType)))
                    throw new InvalidDataException($"Property '{entry.Key}' is not supported on '{itemType.Name}'.");
                if (IsSupportedValue(property.PropertyType) &&
                    (property.SetMethod == null || !property.SetMethod.IsPublic))
                    throw new InvalidDataException($"Property '{entry.Key}' is read-only on '{itemType.Name}'.");
                ValidatePropertyValue(property.PropertyType, entry.Value, property.Name);
            }
        }

        private static void ValidatePropertyValue(Type propertyType, JsonElement value, string propertyName)
        {
            if (IsPartType(propertyType))
            {
                if (value.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException($"Property '{propertyName}' must be an object.");
                foreach (JsonProperty child in value.EnumerateObject())
                {
                    PropertyInfo childProperty = FindProperty(propertyType, child.Name);
                    if (!(IsSupportedValue(childProperty.PropertyType) || IsPartType(childProperty.PropertyType)) ||
                        IsSupportedValue(childProperty.PropertyType) &&
                        (childProperty.SetMethod == null || !childProperty.SetMethod.IsPublic))
                        throw new InvalidDataException($"Nested property '{propertyName}.{child.Name}' is not supported.");
                    ValidatePropertyValue(childProperty.PropertyType, child.Value, propertyName + "." + childProperty.Name);
                }
                return;
            }

            Type effectiveType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            bool valid = effectiveType == typeof(string) ? value.ValueKind == JsonValueKind.String :
                effectiveType == typeof(bool) ? value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False :
                effectiveType == typeof(Color) ? value.ValueKind == JsonValueKind.String :
                effectiveType.IsEnum ? value.ValueKind == JsonValueKind.String :
                effectiveType == typeof(byte) || effectiveType == typeof(short) || effectiveType == typeof(ushort) ||
                effectiveType == typeof(int) || effectiveType == typeof(uint) || effectiveType == typeof(long) ||
                effectiveType == typeof(ulong) || effectiveType == typeof(float) || effectiveType == typeof(double) ||
                effectiveType == typeof(decimal) ? value.ValueKind == JsonValueKind.Number : false;
            if (!valid)
                throw new InvalidDataException($"Value for property '{propertyName}' has the wrong JSON type.");
        }

        private static void ValidateTexts(Type itemType, Dictionary<string, JsonElement> texts)
        {
            foreach (KeyValuePair<string, JsonElement> text in texts)
            {
                PropertyInfo property = FindProperty(itemType, text.Key);
                if (!IsMultilingualText(property.PropertyType))
                    throw new InvalidDataException($"Text property '{text.Key}' is not supported on '{itemType.Name}'.");
                ValidateMultilingualText(text.Value, text.Key);
            }
        }

        private static void ValidateMultilingualText(JsonElement value, string name)
        {
            if (value.ValueKind == JsonValueKind.String)
                return;
            if (value.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"Text '{name}' must be a string, language map, or {{\"html\":\"...\"}} object.");
            if (value.TryGetProperty("html", out JsonElement html))
            {
                if (html.ValueKind != JsonValueKind.String || value.EnumerateObject().Count() != 1)
                    throw new InvalidDataException($"Raw markup for '{name}' must be an object with only a string 'html' property.");
                return;
            }
            foreach (JsonProperty languageText in value.EnumerateObject())
            {
                if (languageText.Value.ValueKind == JsonValueKind.String)
                    continue;
                if (languageText.Value.ValueKind == JsonValueKind.Object &&
                    languageText.Value.TryGetProperty("html", out JsonElement languageHtml) &&
                    languageHtml.ValueKind == JsonValueKind.String &&
                    languageText.Value.EnumerateObject().Count() == 1)
                    continue;
                throw new InvalidDataException($"Language text '{name}.{languageText.Name}' must be a string or raw-html object.");
            }
        }

        private static void ValidateEvents(Type itemType, List<HmiJsonEvent> events)
        {
            if (events.Count == 0) return;
            PropertyInfo? handlersProperty = FindOptionalProperty(itemType, "EventHandlers");
            if (handlersProperty == null)
                throw new InvalidDataException($"Item type '{itemType.Name}' does not support events.");
            Type? compositionType = handlersProperty.PropertyType;
            MethodInfo? create = compositionType.GetMethods().FirstOrDefault(m =>
                m.Name == "Create" && m.GetParameters().Length == 1 &&
                (m.GetParameters()[0].ParameterType.IsEnum || m.GetParameters()[0].ParameterType == typeof(string)));
            if (create == null)
                throw new InvalidDataException($"Event creation is not supported for item type '{itemType.Name}'.");
            foreach (HmiJsonEvent itemEvent in events)
            {
                if (string.IsNullOrWhiteSpace(itemEvent.Type) || itemEvent.Script == null)
                    throw new InvalidDataException($"Events on '{itemType.Name}' require a type and script.");
                if (create.GetParameters()[0].ParameterType.IsEnum)
                    ParseEnumValue(create.GetParameters()[0].ParameterType, itemEvent.Type, $"event on {itemType.Name}");
            }
            EnsureUnique(events.Select(e => e.Type), $"event on '{itemType.Name}'");
        }

        private static void ApplyHmiImportPlan(
            HmiSoftware hmi,
            HmiJsonImportPlan plan,
            bool pruneItems,
            Dictionary<string, HmiJsonItem> defaults)
        {
            foreach (HmiJsonTag tagDocument in plan.Tags)
                ApplyTagDocument(hmi, tagDocument);
            foreach (HmiJsonAlarm alarmDocument in plan.Alarms)
                ApplyAlarmDocument(hmi, alarmDocument, plan.Manifest.Languages);

            foreach (HmiJsonScreen screenDocument in plan.Screens)
            {
                HmiScreen? screen = FindHmiScreen(hmi, screenDocument.Name, screenDocument.Group);
                if (screen != null &&
                    !string.Equals(GetScreenGroupPath(screen), screenDocument.Group, StringComparison.Ordinal))
                    throw new InvalidDataException(
                        $"Existing screen '{screen.Name}' is in group '{GetScreenGroupPath(screen) ?? "(root)"}', " +
                        $"not '{screenDocument.Group ?? "(root)"}'.");
                screen = screen ?? CreateScreenInGroup(hmi, screenDocument.Name, screenDocument.Group);
                ApplyObjectProperties(screen, screenDocument.Props);
                ApplyTexts(screen, screenDocument.Text, plan.Manifest.Languages);
                var importedNames = new HashSet<string>(screenDocument.Items.Select(i => i.Name), StringComparer.OrdinalIgnoreCase);

                foreach (HmiJsonItem itemDocument in screenDocument.Items)
                {
                    HmiScreenItemBase? item = screen.ScreenItems.Find(itemDocument.Name);
                    if (item != null && !string.Equals(item.GetType().Name, itemDocument.Type, StringComparison.Ordinal))
                        throw new InvalidDataException(
                            $"Existing item '{screen.Name}/{itemDocument.Name}' is '{item.GetType().Name}', not '{itemDocument.Type}'.");
                    if (item == null)
                        item = CreateScreenItem(screen, itemDocument.Name, ResolveHmiScreenItemType(itemDocument.Type),
                            itemDocument.ContainedType);
                    if (!string.Equals((item as HmiContainerBase)?.ContainedType, itemDocument.ContainedType, StringComparison.Ordinal))
                        throw new InvalidDataException($"Container type does not match for '{screen.Name}/{item.Name}'.");

                    if (plan.Manifest.Compact && itemDocument.Compact != false &&
                        defaults.TryGetValue(GetItemIdentityKey(itemDocument), out HmiJsonItem? defaultItem))
                    {
                        ApplyObjectProperties(item, defaultItem.Props);
                        ApplyTexts(item, defaultItem.Text, plan.Manifest.Languages);
                    }
                    var mergedProperties = ResolveStyleProperties(plan.Styles, itemDocument);
                    ApplyObjectProperties(item, mergedProperties);
                    ApplyTexts(item, itemDocument.Text, plan.Manifest.Languages);
                    ApplyDynamizations(hmi, item, itemDocument.Dynamizations);
                    ApplyItemEvents(item, itemDocument.Events);
                }

                if (pruneItems)
                {
                    var toDelete = screen.ScreenItems.Where(i => !importedNames.Contains(i.Name)).ToList();
                    foreach (HmiScreenItemBase item in toDelete)
                        item.Delete();
                }
            }
        }

        private static Dictionary<string, JsonElement> ResolveStyleProperties(
            Dictionary<string, Dictionary<string, JsonElement>> styles,
            HmiJsonItem item)
        {
            var merged = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (item.Style != null && !string.IsNullOrWhiteSpace(item.Style))
            {
                if (!styles.TryGetValue(item.Style, out Dictionary<string, JsonElement>? style))
                    throw new InvalidDataException($"Item '{item.Name}' references missing style '{item.Style}'.");
                foreach (var property in style)
                    merged[property.Key] = property.Value;
            }
            foreach (var property in item.Props)
                merged[property.Key] = property.Value;
            return merged;
        }

        private static void ApplyTagDocument(HmiSoftware hmi, HmiJsonTag document)
        {
            HmiTagTable table = string.IsNullOrWhiteSpace(document.Table)
                ? (hmi.TagTables.Find("Default tag table") ?? hmi.TagTables.FirstOrDefault() ?? hmi.TagTables.Create("Default tag table"))
                : (hmi.TagTables.Find(document.Table) ?? hmi.TagTables.Create(document.Table));
            HmiTag tag = hmi.Tags.Find(document.Name) ?? table.Tags.Create(document.Name);
            if (tag.TagTableName != table.Name)
                throw new InvalidDataException(
                    $"HMI tag '{document.Name}' already exists in table '{tag.TagTableName}', not '{table.Name}'.");
            tag.Connection = document.Connection;
            tag.PlcTag = document.PlcTag;
            if (string.IsNullOrWhiteSpace(document.PlcTag) && document.DataType != null)
                tag.DataType = document.DataType;
            ApplyLimitValue(tag.InitialMinValue, document.Min);
            ApplyLimitValue(tag.InitialMaxValue, document.Max);
        }

        private static void ApplyLimitValue(dynamic limit, JsonElement? value)
        {
            if (!value.HasValue || value.Value.ValueKind == JsonValueKind.Null)
            {
                if (limit.ValueType != HmiLimitValueType.None)
                    limit.ValueType = HmiLimitValueType.None;
                return;
            }
            if (limit.ValueType != HmiLimitValueType.Constant)
                limit.ValueType = HmiLimitValueType.Constant;
            limit.Value = ConvertJsonNumber(value.Value);
        }

        private static double ConvertJsonNumber(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number))
                return number;
            throw new InvalidDataException("HMI tag limit values must be numeric or null.");
        }

        private static void ApplyAlarmDocument(HmiSoftware hmi, HmiJsonAlarm document, IReadOnlyList<string> languages)
        {
            var alarm = hmi.DiscreteAlarms.Find(document.Name) ?? hmi.DiscreteAlarms.Create(document.Name);
            alarm.AlarmClass = document.AlarmClass;
            alarm.RaisedStateTag = document.RaisedStateTag;
            alarm.RaisedStateTagBitNumber = document.RaisedStateTagBitNumber;
            alarm.TriggerMode = (HmiDiscreteAlarmTriggerMode)Enum.Parse(
                typeof(HmiDiscreteAlarmTriggerMode), document.TriggerMode, true);
            alarm.AcknowledgmentControlTag = document.AcknowledgmentControlTag ?? string.Empty;
            if (document.EventText != null)
                ApplyMultilingualText(alarm.EventText, document.EventText.Value, "alarm " + document.Name, languages);
        }

        private static HmiScreenItemBase CreateScreenItem(HmiScreen screen, string name, Type itemType,
            string? containedType = null)
        {
            bool isContainer = typeof(HmiContainerBase).IsAssignableFrom(itemType);
            if (isContainer && string.IsNullOrWhiteSpace(containedType))
                throw new InvalidDataException($"Creating '{itemType.Name}' requires containedType.");
            MethodInfo create = screen.ScreenItems.GetType().GetMethods().FirstOrDefault(m =>
                m.Name == "Create" && m.IsGenericMethodDefinition &&
                m.GetGenericArguments().Length == 1 &&
                m.GetParameters().Length == (isContainer ? 2 : 1) &&
                m.GetParameters().All(p => p.ParameterType == typeof(string)))
                ?? throw new InvalidOperationException($"The TIA screen item collection does not expose the creation overload for '{itemType.Name}'.");
            try
            {
                return (HmiScreenItemBase)create.MakeGenericMethod(itemType).Invoke(screen.ScreenItems,
                    isContainer ? new object?[] { name, containedType } : new object[] { name })!;
            }
            catch (TargetInvocationException error) when (error.InnerException != null)
            {
                throw new InvalidOperationException(
                    $"Cannot create '{itemType.Name}' (containedType '{containedType ?? "(none)"}'): {error.InnerException.Message}",
                    error.InnerException);
            }
        }

        private static void ApplyObjectProperties(object target, Dictionary<string, JsonElement> properties)
        {
            foreach (KeyValuePair<string, JsonElement> entry in properties)
            {
                PropertyInfo property = FindProperty(target.GetType(), entry.Key);
                if ((target is HmiScreenItemBase || target is HmiScreen) &&
                    string.Equals(property.Name, "Name", StringComparison.Ordinal))
                    throw new InvalidDataException("Screen item names are immutable within the JSON properties object.");
                SetPropertyValue(target, property, entry.Value);
            }
        }

        private static void SetPropertyValue(object target, PropertyInfo property, JsonElement value)
        {
            Type propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (IsPartType(propertyType) && !property.CanWrite)
            {
                object? part = property.GetValue(target, null);
                if (part == null)
                    throw new InvalidDataException($"Part '{property.Name}' is not available on '{target.GetType().Name}'.");
                var nested = value.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
                ApplyObjectProperties(part, nested);
                return;
            }
            if (!property.CanWrite || property.SetMethod == null || !property.SetMethod.IsPublic)
                throw new InvalidDataException($"Property '{property.Name}' is read-only.");

            object? converted = ConvertJsonValue(propertyType, value, property.Name);
            property.SetValue(target, converted, null);
        }

        private static object? ConvertJsonValue(Type targetType, JsonElement value, string propertyName)
        {
            if (targetType == typeof(string)) return value.GetString();
            if (targetType == typeof(Color)) return ParseJsonColor(value.GetString()!, propertyName);
            if (targetType.IsEnum)
            {
                string enumName = value.GetString()!;
                return ParseEnumValue(targetType, enumName, propertyName);
            }

            if (targetType == typeof(bool)) return value.GetBoolean();
            if (targetType == typeof(byte)) return value.GetByte();
            if (targetType == typeof(short)) return value.GetInt16();
            if (targetType == typeof(ushort)) return value.GetUInt16();
            if (targetType == typeof(int)) return value.GetInt32();
            if (targetType == typeof(uint)) return value.GetUInt32();
            if (targetType == typeof(long)) return value.GetInt64();
            if (targetType == typeof(ulong)) return value.GetUInt64();
            if (targetType == typeof(float)) return value.GetSingle();
            if (targetType == typeof(double)) return value.GetDouble();
            if (targetType == typeof(decimal)) return value.GetDecimal();
            throw new InvalidDataException($"Unsupported property type '{targetType.Name}' for '{propertyName}'.");
        }

        private static object ParseEnumValue(Type enumType, string value, string context)
        {
            try
            {
                object parsed = Enum.Parse(enumType, value, true);
                if (!Enum.IsDefined(enumType, parsed))
                    throw new InvalidDataException($"'{value}' is not a valid {enumType.Name} value for '{context}'.");
                return parsed;
            }
            catch (ArgumentException error)
            {
                throw new InvalidDataException($"'{value}' is not a valid {enumType.Name} value for '{context}'.", error);
            }
        }

        private static Color ParseJsonColor(string text, string propertyName)
        {
            if (text == null || text.Length != 7 && text.Length != 9 || text[0] != '#')
                throw new InvalidDataException($"Color '{propertyName}' must use #RRGGBB or #AARRGGBB.");
            try
            {
                if (text.Length == 7)
                    return Color.FromArgb(255,
                        byte.Parse(text.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                        byte.Parse(text.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                        byte.Parse(text.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                return Color.FromArgb(
                    byte.Parse(text.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(text.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(text.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(text.Substring(7, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }
            catch (FormatException error)
            {
                throw new InvalidDataException($"Color '{propertyName}' contains invalid hexadecimal digits.", error);
            }
        }

        private static void ApplyTexts(object target, Dictionary<string, JsonElement> texts, IReadOnlyList<string> languages)
        {
            string targetName = target.GetType().GetProperty("Name")?.GetValue(target, null)?.ToString() ??
                target.GetType().Name;
            foreach (KeyValuePair<string, JsonElement> entry in texts)
            {
                PropertyInfo property = FindProperty(target.GetType(), entry.Key);
                if (!IsMultilingualText(property.PropertyType))
                    throw new InvalidDataException($"'{entry.Key}' is not a multilingual text property on '{target.GetType().Name}'.");
                object? multilingualText = property.GetValue(target, null);
                if (multilingualText == null)
                    throw new InvalidDataException($"Multilingual text '{entry.Key}' is not available on '{targetName}'.");
                ApplyMultilingualText(multilingualText, entry.Value, targetName + "." + entry.Key, languages);
            }
        }

        private static void ApplyMultilingualText(
            object multilingualText,
            JsonElement value,
            string name,
            IReadOnlyList<string> languages)
        {
            var items = GetMultilingualItems(multilingualText);
            if (items.Count == 0)
                throw new InvalidDataException($"Multilingual text '{name}' has no active language entries.");
            if (value.ValueKind == JsonValueKind.String)
            {
                SetTextForAll(items, value.GetString()!);
                return;
            }
            if (value.TryGetProperty("html", out JsonElement html))
            {
                foreach (object item in items)
                    SetTextItem(item, html.GetString()!);
                return;
            }

            var matched = new HashSet<object>();
            foreach (JsonProperty languageText in value.EnumerateObject())
            {
                object? textItem = items.Select((item, index) => new { Item = item, Index = index })
                    .Where(entry => string.Equals(GetLanguageCode(entry.Item, entry.Index, languages),
                        languageText.Name, StringComparison.OrdinalIgnoreCase))
                    .Select(entry => entry.Item).FirstOrDefault();
                if (textItem == null)
                    throw new InvalidDataException($"Language '{languageText.Name}' for '{name}' is not configured in the HMI.");
                SetTextItem(textItem, ParseTextContent(languageText.Value));
                matched.Add(textItem);
            }
            foreach (object textItem in items)
                if (!matched.Contains(textItem))
                    throw new InvalidDataException($"The language map for '{name}' omits an active HMI language.");
        }

        private static List<object> GetMultilingualItems(object multilingualText)
        {
            PropertyInfo? itemsProperty = multilingualText.GetType().GetProperty("Items");
            if (itemsProperty?.GetValue(multilingualText, null) is not IEnumerable items)
                throw new InvalidDataException("The HMI multilingual text object does not expose an Items collection.");
            return items.Cast<object>().ToList();
        }

        private static void SetTextForAll(List<object> items, string text)
        {
            string markup = ToHmiMarkup(text);
            foreach (object item in items)
                SetTextItem(item, markup);
        }

        private static void SetTextItem(object item, string text)
        {
            PropertyInfo? textProperty = item.GetType().GetProperty("Text");
            if (textProperty?.CanWrite != true)
                throw new InvalidDataException("An HMI multilingual text entry does not expose a writable Text property.");
            textProperty.SetValue(item, text, null);
        }

        private static JsonElement SerializeMultilingualText(object multilingualText, IReadOnlyList<string> languages)
        {
            List<object> items = GetMultilingualItems(multilingualText);
            var values = items.Select((item, index) => new
            {
                Language = GetLanguageCode(item, index, languages),
                Value = CompactTextValue(item.GetType().GetProperty("Text")?.GetValue(item, null)?.ToString() ?? "")
            }).ToList();
            if (values.Count == 0)
                return JsonSerializer.SerializeToElement("");
            if (values.All(v => string.Equals(v.Value.GetRawText(), values[0].Value.GetRawText(), StringComparison.Ordinal)))
                return values[0].Value;

            var languageMap = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var value in values)
            {
                if (languageMap.ContainsKey(value.Language))
                    throw new InvalidOperationException($"HMI multilingual text contains duplicate language '{value.Language}'.");
                languageMap[value.Language] = value.Value;
            }
            return JsonSerializer.SerializeToElement(languageMap);
        }

        private static string GetLanguageCode(object multilingualTextItem, int index, IReadOnlyList<string> languages)
        {
            foreach (string propertyName in new[] { "Language", "Culture", "LanguageCode" })
            {
                object? value = multilingualTextItem.GetType().GetProperty(propertyName)?.GetValue(multilingualTextItem, null);
                if (value == null) continue;
                if (value is string text && !string.IsNullOrWhiteSpace(text)) return text;
                object? name = value.GetType().GetProperty("Name")?.GetValue(value, null);
                if (name is string cultureName && !string.IsNullOrWhiteSpace(cultureName)) return cultureName;
                object? culture = value.GetType().GetProperty("Culture")?.GetValue(value, null);
                object? cultureNameValue = culture?.GetType().GetProperty("Name")?.GetValue(culture, null);
                if (cultureNameValue is string nameValue && !string.IsNullOrWhiteSpace(nameValue)) return nameValue;
            }
            return index < languages.Count ? languages[index] :
                "language-" + index.ToString(CultureInfo.InvariantCulture);
        }

        private static JsonElement CompactTextValue(string text)
        {
            if (TryExtractPlainText(text, out string plain))
                return JsonSerializer.SerializeToElement(plain);
            return JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["html"] = text });
        }

        private static bool TryExtractPlainText(string markup, out string text)
        {
            text = "";
            try
            {
                var document = System.Xml.Linq.XDocument.Parse(markup);
                var body = document.Root;
                if (body?.Name.LocalName != "body" || body.Attributes().Any())
                    return false;
                var paragraphs = body.Elements().ToList();
                if (paragraphs.Count != 1)
                    return false;
                var paragraph = paragraphs[0];
                if (paragraph.Name.LocalName != "p" || paragraph.Attributes().Any() || paragraph.Elements().Any())
                    return false;
                text = paragraph.Value;
                return true;
            }
            catch (System.Xml.XmlException)
            {
                return false;
            }
        }

        private static string ToHmiMarkup(string text)
        {
            if (TryExtractPlainText(text, out _))
                return text;
            return "<body><p>" + SecurityElement.Escape(text) + "</p></body>";
        }

        private static string ParseTextContent(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.String)
                return ToHmiMarkup(value.GetString()!);
            return value.GetProperty("html").GetString()!;
        }

        private static List<HmiJsonEvent> ReadItemEvents(HmiScreenItemBase item)
        {
            PropertyInfo? handlersProperty = FindOptionalProperty(item.GetType(), "EventHandlers");
            if (handlersProperty?.GetValue(item, null) is not IEnumerable handlers)
                return new List<HmiJsonEvent>();
            var events = new List<HmiJsonEvent>();
            foreach (object handler in handlers)
            {
                object? script = handler.GetType().GetProperty("Script")?.GetValue(handler, null);
                object? code = script?.GetType().GetProperty("ScriptCode")?.GetValue(script, null);
                object? eventType = handler.GetType().GetProperty("EventType")?.GetValue(handler, null);
                if (eventType != null && code is string scriptCode)
                    events.Add(new HmiJsonEvent { Type = eventType.ToString()!, Script = scriptCode });
            }
            return events;
        }

        private static void ApplyItemEvents(HmiScreenItemBase item, List<HmiJsonEvent> events)
        {
            PropertyInfo? handlersProperty = FindOptionalProperty(item.GetType(), "EventHandlers");
            if (handlersProperty == null)
            {
                if (events.Count > 0)
                    throw new InvalidDataException($"Item type '{item.GetType().Name}' does not expose event handlers.");
                return;
            }
            object? handlers = handlersProperty.GetValue(item, null);
            if (handlers == null)
            {
                if (events.Count > 0)
                    throw new InvalidDataException($"Event handlers are unavailable on '{item.Name}'.");
                return;
            }
            Type? enumType = handlers.GetType().GetMethods().FirstOrDefault(m =>
                m.Name == "Create" && m.GetParameters().Length == 1 &&
                (m.GetParameters()[0].ParameterType.IsEnum || m.GetParameters()[0].ParameterType == typeof(string)))
                ?.GetParameters()[0].ParameterType;
            if (enumType == null)
                throw new InvalidDataException($"Event creation is not supported on '{item.GetType().Name}'.");
            var desiredEvents = new HashSet<string>(events.Select(e => e.Type), StringComparer.OrdinalIgnoreCase);
            if (handlers is IEnumerable existingHandlers)
            {
                foreach (object existingHandler in existingHandlers.Cast<object>().ToList())
                {
                    string? existingType = existingHandler.GetType().GetProperty("EventType")
                        ?.GetValue(existingHandler, null)?.ToString();
                    if (existingType != null && !desiredEvents.Contains(existingType))
                    {
                        MethodInfo? delete = existingHandler.GetType().GetMethod("Delete", Type.EmptyTypes);
                        if (delete == null)
                            throw new InvalidOperationException(
                                $"Cannot remove stale event '{existingType}' from '{item.Name}'.");
                        delete.Invoke(existingHandler, null);
                    }
                }
            }
            MethodInfo create = handlers.GetType().GetMethods().First(m =>
                m.Name == "Create" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == enumType);
            MethodInfo? find = handlers.GetType().GetMethods().FirstOrDefault(m =>
                m.Name == "Find" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == enumType);
            foreach (HmiJsonEvent itemEvent in events)
            {
                object eventValue = enumType == typeof(string) ? itemEvent.Type : Enum.Parse(enumType, itemEvent.Type, true);
                object? handler = find?.Invoke(handlers, new[] { eventValue }) ??
                    create.Invoke(handlers, new[] { eventValue });
                object? script = handler?.GetType().GetProperty("Script")?.GetValue(handler, null);
                PropertyInfo? scriptCode = script?.GetType().GetProperty("ScriptCode");
                if (scriptCode?.CanWrite != true)
                    throw new InvalidOperationException($"Event '{itemEvent.Type}' does not expose writable script code.");
                scriptCode.SetValue(script, itemEvent.Script, null);
            }
        }

        private static HmiJsonDynamization CreateDynamizationDocument(DynamizationBase dynamization)
        {
            var document = new HmiJsonDynamization { Property = dynamization.PropertyName };
            if (dynamization is TagDynamization tag)
            {
                document.Type = "Tag";
                document.Tag = tag.Tag;
                document.ReadOnly = tag.ReadOnly;
                document.UseIndirectAddressing = tag.UseIndirectAddressing;
                document.ValueConverter = CreateValueConverterDocument(tag.ValueConverter);
            }
            else if (dynamization is ExpressionDynamization expression)
            {
                document.Type = "Expression";
                document.ValueConverter = CreateValueConverterDocument(expression.ValueConverter);
            }
            else
                throw new InvalidDataException(
                    $"Cannot export unsupported dynamization '{dynamization.GetType().Name}' on '{dynamization.PropertyName}' without losing data.");
            return document;
        }

        private static HmiJsonValueConverter CreateValueConverterDocument(ValueConverter converter)
        {
            return new HmiJsonValueConverter
            {
                Formula = converter.Formula,
                IsFormulaSelected = converter.IsFormulaSelected,
                ConditionType = converter.MappingTable.ConditionType.ToString(),
                Entries = converter.MappingTable.Entries.Select(entry =>
                {
                    var document = new HmiJsonMappingEntry
                    {
                        Value = CreateMappingValue(entry.Value),
                        AlternateValue = CreateMappingValue(entry.AlternateValue),
                        Flashing = entry.Flashing,
                        FlashingRate = entry.FlashingRate.ToString()
                    };
                    if (entry is MappingTableEntrySimple simple)
                    {
                        document.Type = "Simple";
                        document.Condition = simple.Condition;
                    }
                    else if (entry is MappingTableEntryRange range)
                    {
                        document.Type = "Range";
                        document.From = CreateMappingValue(range.From);
                        document.To = CreateMappingValue(range.To);
                    }
                    else
                        throw new InvalidDataException($"Unsupported mapping entry '{entry.GetType().Name}'.");
                    return document;
                }).ToList()
            };
        }

        private static HmiJsonMappingValue? CreateMappingValue(object? value)
        {
            if (value == null) return null;
            Type type = value.GetType();
            if (!IsSupportedValue(type) || type.IsEnum)
                throw new InvalidDataException($"Unsupported mapping value type '{type.Name}'.");
            return new HmiJsonMappingValue { Type = type.Name, Value = SerializePropertyValue(value) };
        }

        private static object? ReadMappingValue(HmiJsonMappingValue? value)
        {
            if (value == null) return null;
            Type type;
            switch (value.Type)
            {
                case "String": type = typeof(string); break;
                case "Boolean": type = typeof(bool); break;
                case "Color": type = typeof(Color); break;
                case "Byte": type = typeof(byte); break;
                case "Int16": type = typeof(short); break;
                case "UInt16": type = typeof(ushort); break;
                case "Int32": type = typeof(int); break;
                case "UInt32": type = typeof(uint); break;
                case "Int64": type = typeof(long); break;
                case "UInt64": type = typeof(ulong); break;
                case "Single": type = typeof(float); break;
                case "Double": type = typeof(double); break;
                case "Decimal": type = typeof(decimal); break;
                default: throw new InvalidDataException($"Unsupported mapping value type '{value.Type}'.");
            }
            ValidatePropertyValue(type, value.Value, "mapping value");
            return ConvertJsonValue(type, value.Value, "mapping value");
        }

        private static void ValidateDynamizationDocument(Type itemType, HmiJsonDynamization document,
            Dictionary<string, string> suppliedTags)
        {
            if (document.Type != "Tag" && document.Type != "Expression")
                throw new InvalidDataException($"Unsupported dynamization '{document.Type}'. Re-export legacy expression placeholders.");
            if (string.IsNullOrWhiteSpace(document.Property))
                throw new InvalidDataException("Dynamization requires a property.");
            Type propertyType = FindProperty(itemType, document.Property).PropertyType;
            if (!IsSupportedValue(propertyType) && !IsMultilingualText(propertyType))
                throw new InvalidDataException($"Unsupported dynamization property '{document.Property}' on '{itemType.Name}'.");
            if (document.Type == "Tag" && !ContainsTagReference(suppliedTags, document.Tag))
                throw new InvalidDataException($"Dynamization references missing HMI tag '{document.Tag}'.");
            if (document.Type == "Expression" && document.ValueConverter == null)
                throw new InvalidDataException("Expression dynamization requires valueConverter, not a legacy export placeholder.");
            if (document.OnConflict != "fail" && document.OnConflict != "replace")
                throw new InvalidDataException($"Unsupported onConflict '{document.OnConflict}'.");
            if (document.ValueConverter != null)
                ValidateValueConverter(document.ValueConverter);
        }

        private static void ValidateValueConverter(HmiJsonValueConverter document)
        {
            if (document.Formula == null)
                throw new InvalidDataException("Value converter formula must be a string.");
            ParseEnumValue(typeof(ConditionType), document.ConditionType, "mapping condition type");
            foreach (HmiJsonMappingEntry entry in document.Entries)
            {
                if (entry.Type == "Simple")
                {
                    if (entry.Condition == null)
                        throw new InvalidDataException("Simple mapping entries require a condition.");
                }
                else if (entry.Type == "Range")
                {
                    if (entry.From == null || entry.To == null)
                        throw new InvalidDataException("Range mapping entries require from and to values.");
                }
                else
                    throw new InvalidDataException($"Unsupported mapping entry type '{entry.Type}'.");
                ParseEnumValue(typeof(Siemens.Engineering.HmiUnified.UI.Dynamization.Flashing.FlashingRate),
                    entry.FlashingRate, "mapping flashing rate");
                ReadMappingValue(entry.From);
                ReadMappingValue(entry.To);
                ReadMappingValue(entry.Value);
                ReadMappingValue(entry.AlternateValue);
            }
        }

        private static void ApplyValueConverter(ValueConverter converter, HmiJsonValueConverter document)
        {
            if (converter.Formula != document.Formula)
            {
                converter.IsFormulaSelected = true;
                converter.Formula = document.Formula;
            }
            converter.IsFormulaSelected = document.IsFormulaSelected;
            var table = converter.MappingTable;
            foreach (MappingTableEntryBase entry in table.Entries.ToList())
                entry.Delete();
            table.ConditionType = (ConditionType)ParseEnumValue(typeof(ConditionType),
                document.ConditionType, "mapping condition type");
            foreach (HmiJsonMappingEntry entry in document.Entries)
            {
                MappingTableEntryBase actual;
                if (entry.Type == "Simple")
                {
                    var simple = table.Entries.Create<MappingTableEntrySimple>();
                    simple.Condition = entry.Condition;
                    actual = simple;
                }
                else if (entry.Type == "Range")
                {
                    var range = table.Entries.Create<MappingTableEntryRange>();
                    range.From = ReadMappingValue(entry.From);
                    range.To = ReadMappingValue(entry.To);
                    actual = range;
                }
                else
                    throw new InvalidDataException($"Unsupported mapping entry type '{entry.Type}'.");
                if (entry.Value != null)
                    actual.Value = ReadMappingValue(entry.Value);
                actual.Flashing = entry.Flashing;
                if (entry.AlternateValue != null)
                    actual.AlternateValue = ReadMappingValue(entry.AlternateValue);
                actual.FlashingRate = (Siemens.Engineering.HmiUnified.UI.Dynamization.Flashing.FlashingRate)
                    ParseEnumValue(typeof(Siemens.Engineering.HmiUnified.UI.Dynamization.Flashing.FlashingRate),
                        entry.FlashingRate, "mapping flashing rate");
            }
        }

        private static void ApplyDynamizations(
            HmiSoftware hmi,
            HmiScreenItemBase item,
            List<HmiJsonDynamization> dynamizations,
            bool reconcile = true)
        {
            var desiredProperties = new HashSet<string>(
                dynamizations.Select(d => d.Property), StringComparer.OrdinalIgnoreCase);
            if (reconcile)
                foreach (DynamizationBase existingDynamization in item.Dynamizations.ToList())
                    if (!desiredProperties.Contains(existingDynamization.PropertyName))
                        existingDynamization.Delete();

            foreach (HmiJsonDynamization document in dynamizations)
            {
                if (document.Type != "Tag" && document.Type != "Expression")
                    throw new InvalidDataException($"Unsupported dynamization '{document.Type}'.");
                DynamizationBase? existing = item.Dynamizations.Find(document.Property);
                bool expression = document.Type == "Expression";
                Type desiredType = expression ? typeof(ExpressionDynamization) : typeof(TagDynamization);
                if (existing != null && existing.GetType() != desiredType)
                {
                    if (!string.Equals(document.OnConflict, "replace", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(
                            $"Refusing to replace '{existing.GetType().Name}' dynamization on '{item.Name}.{document.Property}'.");
                    existing.Delete();
                    existing = null;
                }
                ValueConverter converter;
                if (expression)
                {
                    var dynamization = existing as ExpressionDynamization ??
                        item.Dynamizations.Create<ExpressionDynamization>(document.Property);
                    converter = dynamization.ValueConverter;
                }
                else
                {
                    TagDynamization tagDynamization = existing as TagDynamization ??
                        item.Dynamizations.Create<TagDynamization>(document.Property);
                    if (FindReferencedTag(hmi, document.Tag) == null)
                        throw new InvalidDataException($"HMI tag '{document.Tag}' does not exist.");
                    if (tagDynamization.Tag != document.Tag)
                        tagDynamization.Tag = document.Tag;
                    if (tagDynamization.UseIndirectAddressing != document.UseIndirectAddressing)
                        tagDynamization.UseIndirectAddressing = document.UseIndirectAddressing;
                    converter = tagDynamization.ValueConverter;
                }
                if (document.ValueConverter != null)
                    ApplyValueConverter(converter, document.ValueConverter);
                if (!expression)
                {
                    var tagDynamization = (TagDynamization)item.Dynamizations.Find(document.Property);
                    if (tagDynamization.ReadOnly != document.ReadOnly)
                        tagDynamization.ReadOnly = document.ReadOnly;
                }
            }
        }

        private static void VerifyHmiImportPlan(HmiSoftware hmi, HmiJsonImportPlan plan)
        {
            foreach (HmiJsonScreen screenDocument in plan.Screens)
            {
                HmiScreen? screen = FindHmiScreen(hmi, screenDocument.Name, screenDocument.Group);
                if (screen == null ||
                    screenDocument.Props["Width"].GetUInt32() != screen.Width ||
                    screenDocument.Props["Height"].GetUInt32() != screen.Height)
                    throw new InvalidOperationException($"Screen '{screenDocument.Name}' failed read-back verification.");
                VerifyObjectProperties(screen, screenDocument.Props);
                VerifyTexts(screen, screenDocument.Text, plan.Manifest.Languages);
                foreach (HmiJsonItem itemDocument in screenDocument.Items)
                {
                    HmiScreenItemBase? item = screen.ScreenItems.Find(itemDocument.Name);
                    if (item == null || item.GetType().Name != itemDocument.Type)
                        throw new InvalidOperationException($"Screen item '{screen.Name}/{itemDocument.Name}' failed type verification.");
                    if (item is HmiContainerBase container && container.ContainedType != itemDocument.ContainedType)
                        throw new InvalidOperationException($"Container '{screen.Name}/{itemDocument.Name}' failed contained type verification.");
                    VerifyObjectProperties(item, ResolveStyleProperties(plan.Styles, itemDocument));
                    VerifyTexts(item, itemDocument.Text, plan.Manifest.Languages);
                    foreach (HmiJsonDynamization dynamization in itemDocument.Dynamizations)
                    {
                        var actual = item.Dynamizations.Find(dynamization.Property);
                        var actualDocument = actual == null ? null : CreateDynamizationDocument(actual);
                        if (actualDocument != null)
                            actualDocument.OnConflict = dynamization.OnConflict;
                        if (actualDocument != null && dynamization.ValueConverter == null)
                            actualDocument.ValueConverter = null;
                        if (actualDocument == null || !AreJsonValuesEqual(
                            JsonSerializer.SerializeToElement(actualDocument, HmiJsonOptions),
                            JsonSerializer.SerializeToElement(dynamization, HmiJsonOptions)))
                            throw new InvalidOperationException(
                                $"Dynamization '{screen.Name}/{item.Name}/{dynamization.Property}' failed read-back verification.");
                    }
                    if (item.Dynamizations.Count != itemDocument.Dynamizations.Count)
                        throw new InvalidOperationException($"Dynamizations on '{screen.Name}/{item.Name}' failed read-back verification.");
                    var actualEvents = ReadItemEvents(item);
                    if (actualEvents.Count != itemDocument.Events.Count ||
                        itemDocument.Events.Any(expected => !actualEvents.Any(actual =>
                            actual.Type == expected.Type && actual.Script == expected.Script)))
                        throw new InvalidOperationException($"Events on '{screen.Name}/{item.Name}' failed read-back verification.");
                }
            }
            foreach (HmiJsonTag expected in plan.Tags)
            {
                HmiTag? actual = hmi.Tags.Find(expected.Name);
                if (actual == null || actual.TagTableName != expected.Table ||
                    actual.Connection != expected.Connection || actual.PlcTag != expected.PlcTag ||
                    expected.DataType != null && actual.DataType != expected.DataType ||
                    !AreNullableJsonValuesEqual(CreateLimitDocument(actual.InitialMinValue), expected.Min) ||
                    !AreNullableJsonValuesEqual(CreateLimitDocument(actual.InitialMaxValue), expected.Max))
                    throw new InvalidOperationException($"HMI tag '{expected.Name}' failed read-back verification.");
            }
            foreach (HmiJsonAlarm expected in plan.Alarms)
            {
                HmiDiscreteAlarm? actual = hmi.DiscreteAlarms.Find(expected.Name);
                if (actual == null || actual.AlarmClass != expected.AlarmClass ||
                    actual.RaisedStateTag != expected.RaisedStateTag ||
                    actual.RaisedStateTagBitNumber != expected.RaisedStateTagBitNumber ||
                    actual.AcknowledgmentControlTag != expected.AcknowledgmentControlTag ||
                    !string.Equals(actual.TriggerMode.ToString(), expected.TriggerMode, StringComparison.OrdinalIgnoreCase) ||
                    !AreNullableJsonValuesEqual(SerializeMultilingualText(actual.EventText, plan.Manifest.Languages), expected.EventText))
                    throw new InvalidOperationException($"Discrete alarm '{expected.Name}' failed read-back verification.");
            }
        }

        private static bool HasItemEvents(HmiScreenItemBase item)
        {
            if (ReadItemEvents(item).Count > 0)
                return true;
            PropertyInfo? property = item.GetType().GetProperty("PropertyEventHandlers");
            return property?.GetValue(item, null) is IEnumerable handlers && handlers.Cast<object>().Any();
        }

        private static void VerifyObjectProperties(object target, Dictionary<string, JsonElement> properties)
        {
            foreach (KeyValuePair<string, JsonElement> expected in properties)
            {
                PropertyInfo property = FindProperty(target.GetType(), expected.Key);
                object? value = property.GetValue(target, null);
                if (value == null || !AreJsonValuesEqual(SerializePropertyValue(value), expected.Value))
                    throw new InvalidOperationException(
                        $"Property '{target.GetType().Name}.{property.Name}' failed read-back verification.");
            }
        }

        private static void VerifyTexts(
            object target,
            Dictionary<string, JsonElement> expectedTexts,
            IReadOnlyList<string> languages)
        {
            foreach (KeyValuePair<string, JsonElement> expected in expectedTexts)
            {
                PropertyInfo property = FindProperty(target.GetType(), expected.Key);
                object? value = property.GetValue(target, null);
                if (value == null || !AreJsonValuesEqual(
                    SerializeMultilingualText(value, languages), expected.Value))
                    throw new InvalidOperationException(
                        $"Text '{target.GetType().Name}.{property.Name}' failed read-back verification.");
            }
        }

        private static bool AreNullableJsonValuesEqual(JsonElement? left, JsonElement? right)
        {
            if (!left.HasValue || left.Value.ValueKind == JsonValueKind.Null)
                return !right.HasValue || right.Value.ValueKind == JsonValueKind.Null;
            return right.HasValue && AreJsonValuesEqual(left.Value, right.Value);
        }

        private static void ValidateHmiRequirements(HmiSoftware hmi, HmiJsonRequirements? requirements)
        {
            ValidatePatchRequirements(hmi, requirements);
        }

        private static void ValidatePatchRequirements(HmiSoftware hmi, HmiJsonRequirements? requirements)
        {
            if (requirements == null) return;
            foreach (HmiJsonScreenRequirement requiredScreen in requirements.Screens)
            {
                HmiScreen? screen = FindHmiScreen(hmi, requiredScreen.Name, requiredScreen.Group);
                if (screen == null)
                    throw new InvalidOperationException($"Required screen '{requiredScreen.Name}' does not exist.");
                if (requiredScreen.Width.HasValue && screen.Width != requiredScreen.Width.Value ||
                    requiredScreen.Height.HasValue && screen.Height != requiredScreen.Height.Value)
                    throw new InvalidOperationException($"Required screen dimensions do not match for '{requiredScreen.Name}'.");
            }
            foreach (HmiJsonTagRequirement requiredTag in requirements.Tags)
            {
                HmiTag? tag = hmi.Tags.Find(requiredTag.Name);
                if (tag == null || requiredTag.PlcTag != null &&
                    !string.Equals(tag.PlcTag, requiredTag.PlcTag, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Required HMI tag '{requiredTag.Name}' is missing or has a different PLC binding.");
            }
        }

        private static void VerifyPatchRequirements(HmiSoftware hmi, HmiJsonRequirements? requirements)
        {
            ValidatePatchRequirements(hmi, requirements);
        }

        private static void VerifyHmiPatch(HmiSoftware hmi, HmiJsonRequirements? verification)
        {
            if (verification == null) return;
            ValidatePatchRequirements(hmi, verification);
            foreach (HmiJsonScreenRequirement requiredScreen in verification.Screens)
            {
                HmiScreen? screen = FindHmiScreen(hmi, requiredScreen.Name, requiredScreen.Group);
                if (screen == null) continue;
                foreach (HmiJsonItemRequirement requiredItem in requiredScreen.Items)
                {
                    HmiScreenItemBase? item = screen.ScreenItems.Find(requiredItem.Name);
                    if (item == null || requiredItem.Type != null &&
                        !string.Equals(item.GetType().Name, requiredItem.Type, StringComparison.Ordinal))
                        throw new InvalidOperationException(
                            $"Verification failed for item '{requiredScreen.Name}/{requiredItem.Name}'.");
                }
            }
        }

        private static void ValidatePatchOperations(HmiSoftware hmi, List<HmiJsonPatchOperation> operations)
        {
            if (operations.Count > 10000)
                throw new InvalidDataException("An HMI patch may contain at most 10,000 operations.");
            foreach (HmiJsonPatchOperation operation in operations)
            {
                HmiScreen? screen = FindHmiScreen(hmi, operation.Screen, operation.Group);
                if (screen == null)
                    throw new InvalidOperationException($"Patch screen '{operation.Screen}' does not exist.");
                HmiScreenItemBase? item = screen.ScreenItems.Find(operation.Item);
                if (item == null)
                    throw new InvalidOperationException($"Patch item '{operation.Screen}/{operation.Item}' does not exist.");
                if (!string.IsNullOrWhiteSpace(operation.RequireType) &&
                    !string.Equals(operation.RequireType, item.GetType().Name, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Patch precondition type failed for '{operation.Screen}/{operation.Item}'.");

                switch (operation.Op)
                {
                    case "set":
                        if (operation.Property == null || string.IsNullOrWhiteSpace(operation.Property))
                            throw new InvalidDataException("Patch 'set' operations require a property path.");
                        ValidatePatchProperty(item.GetType(), operation.Property, operation.Value);
                        break;
                    case "bind":
                        if (string.IsNullOrWhiteSpace(operation.Property) || string.IsNullOrWhiteSpace(operation.Tag))
                            throw new InvalidDataException("Patch 'bind' operations require property and tag.");
                        if (FindReferencedTag(hmi, operation.Tag!) == null)
                            throw new InvalidOperationException($"Patch references missing HMI tag '{operation.Tag}'.");
                        DynamizationBase? existing = item.Dynamizations.Find(operation.Property);
                        if (existing != null && !(existing is TagDynamization) &&
                            !string.Equals(operation.OnConflict, "replace", StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException(
                                $"Refusing to overwrite non-tag dynamization on '{item.Name}.{operation.Property}'.");
                        break;
                    case "delete":
                        if (operation.RequireNoDynamizations && item.Dynamizations.Any())
                            throw new InvalidOperationException($"Delete precondition failed: '{item.Name}' has dynamizations.");
                        if (operation.RequireNoEvents && HasItemEvents(item))
                            throw new InvalidOperationException($"Delete precondition failed: '{item.Name}' has event handlers.");
                        break;
                    default:
                        throw new InvalidDataException($"Unsupported patch operation '{operation.Op}'.");
                }
            }
        }

        private static void ValidatePatchProperty(Type itemType, string path, JsonElement value)
        {
            string[] segments = path.Split('.');
            Type currentType = itemType;
            for (int i = 0; i < segments.Length; i++)
            {
                PropertyInfo property = FindProperty(currentType, segments[i]);
                if (i < segments.Length - 1)
                {
                    if (!IsPartType(property.PropertyType))
                        throw new InvalidDataException($"Patch path '{path}' traverses unsupported part '{property.Name}'.");
                    currentType = property.PropertyType;
                }
                else
                {
                    if (segments.Length == 1 && property.Name == "Name")
                        throw new InvalidDataException("Patch operations cannot rename screen items.");
                    if (IsMultilingualText(property.PropertyType))
                    {
                        ValidateMultilingualText(value, path);
                        return;
                    }
                    if (!(IsSupportedValue(property.PropertyType) || IsPartType(property.PropertyType)))
                        throw new InvalidDataException($"Patch property '{path}' is not supported.");
                    ValidatePropertyValue(property.PropertyType, value, path);
                }
            }
        }

        private static void ApplyPatchOperation(
            HmiSoftware hmi,
            HmiJsonPatchOperation operation,
            IReadOnlyList<string> languages)
        {
            HmiScreen screen = FindHmiScreen(hmi, operation.Screen, operation.Group)!;
            HmiScreenItemBase item = screen.ScreenItems.Find(operation.Item)!;
            switch (operation.Op)
            {
                case "set":
                    object target = item;
                    string[] segments = operation.Property!.Split('.');
                    for (int i = 0; i < segments.Length - 1; i++)
                    {
                        PropertyInfo partProperty = FindProperty(target.GetType(), segments[i]);
                        target = partProperty.GetValue(target, null)
                            ?? throw new InvalidDataException($"Patch part '{segments[i]}' is not available.");
                    }
                    PropertyInfo setProperty = FindProperty(target.GetType(), segments[segments.Length - 1]);
                    if (IsMultilingualText(setProperty.PropertyType))
                    {
                        object? multilingualText = setProperty.GetValue(target, null);
                        if (multilingualText == null)
                            throw new InvalidDataException($"Patch text property '{operation.Property}' is not available.");
                        ApplyMultilingualText(multilingualText, operation.Value, operation.Property!, languages);
                    }
                    else
                    {
                        SetPropertyValue(target, setProperty, operation.Value);
                    }
                    break;
                case "bind":
                    var binding = new HmiJsonDynamization
                    {
                        Type = "Tag",
                        Property = operation.Property!,
                        Tag = operation.Tag!,
                        ReadOnly = operation.ReadOnly,
                        UseIndirectAddressing = operation.UseIndirectAddressing,
                        OnConflict = operation.OnConflict
                    };
                    ApplyDynamizations(hmi, item, new List<HmiJsonDynamization> { binding }, reconcile: false);
                    break;
                case "delete":
                    item.Delete();
                    break;
            }
        }

        private static object DescribePatchOperation(HmiJsonPatchOperation operation)
        {
            return new Dictionary<string, object?>
            {
                ["op"] = operation.Op,
                ["screen"] = operation.Screen,
                ["group"] = operation.Group,
                ["item"] = operation.Item,
                ["property"] = operation.Property,
                ["tag"] = operation.Tag,
                ["requireType"] = operation.RequireType
            };
        }

        private static Dictionary<string, object> CreateImportResult(
            HmiJsonImportPlan plan,
            string targetName,
            bool dryRun,
            bool pruneItems,
            CompilationReport? compilation)
        {
            var result = new Dictionary<string, object>
            {
                ["targetName"] = targetName,
                ["dryRun"] = dryRun,
                ["pruneItems"] = pruneItems,
                ["screenCount"] = plan.Screens.Count,
                ["itemCount"] = plan.Screens.Sum(s => s.Items.Count),
                ["tagCount"] = plan.Tags.Count,
                ["discreteAlarmCount"] = plan.Alarms.Count,
                ["saved"] = false
            };
            if (compilation != null)
                result["compilation"] = compilation;
            return result;
        }

        private static PropertyInfo FindProperty(Type type, string name)
        {
            PropertyInfo? property = FindOptionalProperty(type, name);
            return property ?? throw new InvalidDataException($"Unknown property '{name}' on '{type.Name}'.");
        }

        private static PropertyInfo? FindOptionalProperty(Type type, string name)
        {
            for (Type? current = type; current != null; current = current.BaseType)
            {
                PropertyInfo? property = current.GetProperty(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase | BindingFlags.DeclaredOnly);
                if (property != null)
                    return property;
            }
            return null;
        }

        private static IEnumerable<PropertyInfo> GetSerializableProperties(Type type)
        {
            return type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.GetIndexParameters().Length == 0 && p.GetMethod?.IsPublic == true &&
                    (IsMultilingualText(p.PropertyType) ||
                     IsPartType(p.PropertyType) ||
                     IsSupportedValue(p.PropertyType) && p.SetMethod?.IsPublic == true))
                .OrderBy(p => p.Name, StringComparer.Ordinal);
        }

        private static bool IsSupportedValue(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            return type.IsEnum || type == typeof(string) || type == typeof(bool) || type == typeof(Color) ||
                type == typeof(byte) || type == typeof(short) || type == typeof(ushort) || type == typeof(int) ||
                type == typeof(uint) || type == typeof(long) || type == typeof(ulong) || type == typeof(float) ||
                type == typeof(double) || type == typeof(decimal);
        }

        private static bool IsPartType(Type type)
        {
            return type.Name.EndsWith("Part", StringComparison.Ordinal) &&
                !IsMultilingualText(type) &&
                !typeof(IEnumerable).IsAssignableFrom(type);
        }

        private static bool IsMultilingualText(Type type)
        {
            return string.Equals(type.Name, "MultilingualText", StringComparison.Ordinal);
        }

        private static JsonElement SerializePropertyValue(object value)
        {
            if (value is Color color)
            {
                string colorValue = color.A == 255
                    ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
                    : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
                return JsonSerializer.SerializeToElement(colorValue);
            }
            if (value.GetType().IsEnum)
                return JsonSerializer.SerializeToElement(value.ToString());
            if (IsSupportedValue(value.GetType()))
                return JsonSerializer.SerializeToElement(value, value.GetType());
            if (IsPartType(value.GetType()))
            {
                var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (PropertyInfo property in GetSerializableProperties(value.GetType()))
                {
                    if (property.Name == "Parent" || IsMultilingualText(property.PropertyType) ||
                        !(IsSupportedValue(property.PropertyType) || IsPartType(property.PropertyType)))
                        continue;
                    object? nestedValue = property.GetValue(value, null);
                    if (nestedValue != null)
                        properties[property.Name] = JsonElementToObject(SerializePropertyValue(nestedValue));
                }
                return JsonSerializer.SerializeToElement(properties);
            }
            throw new InvalidDataException($"Unsupported HMI property value type '{value.GetType().FullName}'.");
        }

        private static object? JsonElementToObject(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String: return element.GetString();
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.Number: return element.GetDouble();
                case JsonValueKind.Object:
                    return element.EnumerateObject().ToDictionary(p => p.Name, p => JsonElementToObject(p.Value));
                case JsonValueKind.Array:
                    return element.EnumerateArray().Select(JsonElementToObject).ToList();
                default: return null;
            }
        }

        private static string MakeFileNameSafe(string name)
        {
            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
            string safe = new string(name.Select(c => invalid.Contains(c) || c == '/' || c == '\\' ? '_' : c).ToArray()).Trim();
            return string.IsNullOrWhiteSpace(safe) ? "screen" : safe;
        }

        private static string ResolveManifestPath(string root, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
                throw new InvalidDataException("Manifest file names must be non-empty relative paths.");
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Manifest path '{relativePath}' escapes the import directory.");
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"Manifest file '{relativePath}' was not found.", fullPath);
            return fullPath;
        }

        private static void WriteJsonFile(string path, object document)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(document, HmiJsonOptions));
        }

        private static T ReadJsonFile<T>(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                throw new FileNotFoundException("HMI JSON file not found.", path);
            if (info.Length > 10 * 1024 * 1024)
                throw new InvalidDataException($"HMI JSON file exceeds the 10 MiB limit: {path}");
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, HmiJsonOptions)
                ?? throw new InvalidDataException($"HMI JSON file is empty or invalid: {path}");
        }

        private static Type ResolveHmiScreenItemType(string typeName)
        {
            if (HmiJsonItemTypes.TryGetValue(typeName, out Type? knownType))
                return knownType;
            Type[] matches = typeof(HmiScreenItemBase).Assembly.GetTypes()
                .Where(type => type.IsClass && !type.IsAbstract &&
                    typeof(HmiScreenItemBase).IsAssignableFrom(type) &&
                    string.Equals(type.Name, typeName, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException(matches.Length == 0
                    ? $"Unsupported HMI screen item type '{typeName}'."
                    : $"Ambiguous HMI screen item type '{typeName}'; use one of the explicitly supported types.");
            return matches[0];
        }

        private sealed class HmiJsonManifest
        {
            public string Format { get; set; } = "";
            public string? TargetName { get; set; }
            public string TiaVersion { get; set; } = "";
            public List<string> Languages { get; set; } = new List<string>();
            public string ExportedAt { get; set; } = "";
            public List<HmiJsonScreenFileReference> ScreenFiles { get; set; } = new List<HmiJsonScreenFileReference>();
            public List<string> TagFiles { get; set; } = new List<string>();
            public string DiscreteAlarmFile { get; set; } = "alarms/discrete.json";
            public string? StylesFile { get; set; }
            public string? DefaultsFile { get; set; }
            public bool Compact { get; set; } = true;
            public SortedDictionary<string, string> FileHashes { get; set; } =
                new SortedDictionary<string, string>(StringComparer.Ordinal);
            public HmiJsonRequirements? Require { get; set; }
            public HmiJsonRequirements? Verify { get; set; }
            public List<string> Warnings { get; set; } = new List<string>();
        }

        private sealed class HmiJsonScreenFileReference
        {
            public string Name { get; set; } = "";
            [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
            public string? Group { get; set; }
            public string Path { get; set; } = "";
        }

        private sealed class HmiJsonStylesFile
        {
            public string Format { get; set; } = "";
            public Dictionary<string, Dictionary<string, JsonElement>> Styles { get; set; } =
                new Dictionary<string, Dictionary<string, JsonElement>>(StringComparer.Ordinal);
        }

        private sealed class HmiJsonScreen
        {
            public string Format { get; set; } = "";
            public string Name { get; set; } = "";
            [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
            public string? Group { get; set; }
            public Dictionary<string, JsonElement> Props { get; set; } =
                new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            public Dictionary<string, JsonElement> Text { get; set; } =
                new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            public List<HmiJsonItem> Items { get; set; } = new List<HmiJsonItem>();
        }

        private sealed class HmiJsonItem
        {
            public string Name { get; set; } = "";
            public string Type { get; set; } = "";
            public string? ContainedType { get; set; }
            public bool? Compact { get; set; }
            public string? Style { get; set; }
            public Dictionary<string, JsonElement> Props { get; set; } =
                new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            public Dictionary<string, JsonElement> Text { get; set; } =
                new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            public List<HmiJsonDynamization> Dynamizations { get; set; } = new List<HmiJsonDynamization>();
            public List<HmiJsonEvent> Events { get; set; } = new List<HmiJsonEvent>();
        }

        private sealed class HmiJsonTagFile
        {
            public string Format { get; set; } = "";
            public string Table { get; set; } = "";
            public List<HmiJsonTag> Tags { get; set; } = new List<HmiJsonTag>();
        }

        private sealed class HmiJsonTag
        {
            public string Name { get; set; } = "";
            public string? Table { get; set; }
            public string? Connection { get; set; }
            public string? PlcTag { get; set; }
            public string? DataType { get; set; }
            [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
            public JsonElement? Min { get; set; }
            [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
            public JsonElement? Max { get; set; }
        }

        private sealed class HmiJsonAlarmFile
        {
            public string Format { get; set; } = "";
            public List<HmiJsonAlarm> DiscreteAlarms { get; set; } = new List<HmiJsonAlarm>();
        }

        private sealed class HmiJsonAlarm
        {
            public string Name { get; set; } = "";
            public string? AlarmClass { get; set; }
            public string? RaisedStateTag { get; set; }
            public uint RaisedStateTagBitNumber { get; set; }
            public string TriggerMode { get; set; } = "";
            public string? AcknowledgmentControlTag { get; set; }
            public JsonElement? EventText { get; set; }
        }

        private sealed class HmiJsonDynamization
        {
            public string Type { get; set; } = "";
            public string Property { get; set; } = "";
            public string Tag { get; set; } = "";
            public bool ReadOnly { get; set; }
            public bool UseIndirectAddressing { get; set; }
            public string? OnConflict { get; set; } = "fail";
            public HmiJsonValueConverter? ValueConverter { get; set; }
        }

        private sealed class HmiJsonValueConverter
        {
            public string Formula { get; set; } = "";
            public bool IsFormulaSelected { get; set; }
            public string ConditionType { get; set; } = "";
            public List<HmiJsonMappingEntry> Entries { get; set; } = new List<HmiJsonMappingEntry>();
        }

        private sealed class HmiJsonMappingEntry
        {
            public string Type { get; set; } = "";
            public string? Condition { get; set; }
            public HmiJsonMappingValue? From { get; set; }
            public HmiJsonMappingValue? To { get; set; }
            public HmiJsonMappingValue? Value { get; set; }
            public HmiJsonMappingValue? AlternateValue { get; set; }
            public bool Flashing { get; set; }
            public string FlashingRate { get; set; } = "Medium";
        }

        private sealed class HmiJsonMappingValue
        {
            public string Type { get; set; } = "";
            public JsonElement Value { get; set; }
        }

        private sealed class HmiJsonEvent
        {
            public string Type { get; set; } = "";
            public string? Script { get; set; }
            public string? ScriptFile { get; set; }
        }

        private sealed class HmiJsonImportPlan
        {
            public string Root { get; set; } = "";
            public HmiJsonManifest Manifest { get; set; } = new HmiJsonManifest();
            public List<HmiJsonScreen> Screens { get; set; } = new List<HmiJsonScreen>();
            public List<HmiJsonTag> Tags { get; set; } = new List<HmiJsonTag>();
            public List<HmiJsonAlarm> Alarms { get; set; } = new List<HmiJsonAlarm>();
            public Dictionary<string, Dictionary<string, JsonElement>> Styles { get; set; } =
                new Dictionary<string, Dictionary<string, JsonElement>>(StringComparer.Ordinal);
        }

        private sealed class HmiJsonPatch
        {
            public string Format { get; set; } = "";
            public HmiJsonRequirements? Require { get; set; }
            public HmiJsonRequirements? Verify { get; set; }
            public List<HmiJsonPatchOperation> Operations { get; set; } = new List<HmiJsonPatchOperation>();
        }

        private sealed class HmiJsonRequirements
        {
            public List<HmiJsonScreenRequirement> Screens { get; set; } = new List<HmiJsonScreenRequirement>();
            public List<HmiJsonTagRequirement> Tags { get; set; } = new List<HmiJsonTagRequirement>();
        }

        private sealed class HmiJsonScreenRequirement
        {
            public string Name { get; set; } = "";
            [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
            public string? Group { get; set; }
            public uint? Width { get; set; }
            public uint? Height { get; set; }
            public List<HmiJsonItemRequirement> Items { get; set; } = new List<HmiJsonItemRequirement>();
        }

        private sealed class HmiJsonTagRequirement
        {
            public string Name { get; set; } = "";
            public string? PlcTag { get; set; }
        }

        private sealed class HmiJsonItemRequirement
        {
            public string Name { get; set; } = "";
            public string? Type { get; set; }
        }

        private sealed class HmiJsonScreenLocation
        {
            public HmiScreen Screen { get; set; } = null!;
            public string? Group { get; set; }
        }

        private sealed class HmiJsonPatchOperation
        {
            public string Op { get; set; } = "";
            public string Screen { get; set; } = "";
            public string? Group { get; set; }
            public string Item { get; set; } = "";
            public string? Property { get; set; }
            public JsonElement Value { get; set; }
            public string? Tag { get; set; }
            public bool ReadOnly { get; set; }
            public bool UseIndirectAddressing { get; set; }
            public string? OnConflict { get; set; } = "fail";
            public string? RequireType { get; set; }
            public bool RequireNoDynamizations { get; set; }
            public bool RequireNoEvents { get; set; }
        }
    }
}
