using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.Dynamization;
using Siemens.Engineering.HmiUnified.UI.Screens;

namespace TiaOpennessMcp.Tia
{
    // hmi_update_screen_item: allow-listed in-place property updates (issue #15).
    public partial class TiaManager
    {
        private const string HmiItemFontPropertyName = "Font";
        private const string HmiItemTextPropertyName = "Text";
        private const string HmiItemScreenPropertyName = "Screen";

        // Top-level properties, mapped from any casing to the canonical Openness property name.
        private static readonly string[] HmiItemPropertyUpdateNames =
        {
            "Visible",
            "Enabled",
            "Left",
            "Top",
            "Width",
            "Height",
            "CenterX",
            "CenterY",
            "Radius",
            "BackColor",
            "ForeColor",
            "BorderColor",
            "BorderWidth",
            "OutputFormat",
            "HorizontalTextAlignment",
            "VerticalTextAlignment",
            HmiItemScreenPropertyName,
            HmiItemTextPropertyName
        };

        // HmiFontPart properties. "Bold" is a convenience alias that sets Weight to Bold or Normal.
        private static readonly string[] HmiItemFontPropertyUpdateNames =
        {
            "Size",
            "Weight",
            "Italic",
            "Underline",
            "StrikeOut",
            "Name",
            "Bold"
        };

        /// <summary>
        /// Flattens item_properties into canonical leaf paths, for example {"Font":{"Size":14}} and "font.size"
        /// both become "Font.Size", and "Font.Bold": true becomes "Font.Weight": "Bold". Runs before any project
        /// access, so every input error is reported without touching TIA Portal.
        /// </summary>
        private static List<KeyValuePair<string, JsonElement>> NormalizeItemPropertyUpdates(JsonElement properties)
        {
            if (properties.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("item_properties must be a JSON object.");

            var updates = new List<KeyValuePair<string, JsonElement>>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(string path, JsonElement value, string givenName)
            {
                if (!seenPaths.Add(path))
                    throw new InvalidDataException(
                        $"item_properties sets '{path}' more than once (duplicate or overlapping key '{givenName}'; " +
                        "check nested Font, dotted Font.* keys, and Font.Bold together with Font.Weight).");
                updates.Add(new KeyValuePair<string, JsonElement>(path, value.Clone()));
            }

            void AddFontLeaf(string subName, JsonElement value, string givenName)
            {
                string? canonical = HmiItemFontPropertyUpdateNames.FirstOrDefault(name =>
                    string.Equals(name, subName, StringComparison.OrdinalIgnoreCase));
                if (canonical == null)
                    throw new InvalidDataException(
                        $"Unsupported Font property '{subName}'. Allowed Font properties: " +
                        string.Join(", ", HmiItemFontPropertyUpdateNames) + ".");
                if (canonical == "Bold")
                {
                    if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
                        throw new InvalidDataException("Font.Bold must be true or false.");
                    Add(HmiItemFontPropertyName + ".Weight",
                        JsonSerializer.SerializeToElement(value.GetBoolean() ? "Bold" : "Normal"), givenName);
                    return;
                }
                Add(HmiItemFontPropertyName + "." + canonical, value, givenName);
            }

            foreach (JsonProperty update in properties.EnumerateObject())
            {
                string name = update.Name;
                int dot = name.IndexOf('.');
                if (dot >= 0)
                {
                    string root = name.Substring(0, dot);
                    string rest = name.Substring(dot + 1);
                    if (!string.Equals(root, HmiItemFontPropertyName, StringComparison.OrdinalIgnoreCase) ||
                        rest.Length == 0 || rest.IndexOf('.') >= 0)
                        throw new InvalidDataException(
                            $"Unsupported item property path '{name}'. Only Font.<property> paths are supported.");
                    AddFontLeaf(rest, update.Value, name);
                    continue;
                }

                if (string.Equals(name, HmiItemFontPropertyName, StringComparison.OrdinalIgnoreCase))
                {
                    if (update.Value.ValueKind != JsonValueKind.Object)
                        throw new InvalidDataException(
                            "Font must be an object such as {\"Size\":14,\"Weight\":\"Bold\"}.");
                    if (!update.Value.EnumerateObject().Any())
                        throw new InvalidDataException("Font must contain at least one property.");
                    foreach (JsonProperty fontProperty in update.Value.EnumerateObject())
                        AddFontLeaf(fontProperty.Name, fontProperty.Value, name + "." + fontProperty.Name);
                    continue;
                }

                string? canonicalName = HmiItemPropertyUpdateNames.FirstOrDefault(allowed =>
                    string.Equals(allowed, name, StringComparison.OrdinalIgnoreCase));
                if (canonicalName == null)
                    throw new InvalidDataException(
                        $"Unsupported item property '{name}'. Allowed properties: " +
                        string.Join(", ", HmiItemPropertyUpdateNames.OrderBy(n => n, StringComparer.Ordinal)) +
                        ", Font (" + string.Join(", ", HmiItemFontPropertyUpdateNames) + ") and dotted Font.<property> paths.");
                if (canonicalName == HmiItemTextPropertyName)
                    ValidateMultilingualText(update.Value, HmiItemTextPropertyName);
                Add(canonicalName, update.Value, name);
            }

            if (updates.Count == 0)
                throw new InvalidDataException("item_properties must contain at least one supported property.");
            return updates;
        }

        /// <summary>
        /// Checks one leaf path against the concrete item type before any mutation: every segment exists, Font is a
        /// part, Text is a multilingual text, a scalar leaf has a public setter, and the JSON value converts to the
        /// property's CLR type (enum names, #RRGGBB colors, numeric ranges).
        /// </summary>
        private static void ValidateItemPropertyPath(Type itemType, string path, JsonElement value)
        {
            string[] segments = path.Split('.');
            Type currentType = itemType;
            for (int i = 0; i < segments.Length - 1; i++)
            {
                PropertyInfo partProperty = FindProperty(currentType, segments[i]);
                if (!IsPartType(partProperty.PropertyType))
                    throw new InvalidDataException(
                        $"Property path '{path}' is not supported on '{itemType.Name}': '{partProperty.Name}' is not a part.");
                currentType = partProperty.PropertyType;
            }

            PropertyInfo leaf = FindProperty(currentType, segments[segments.Length - 1]);
            if (IsMultilingualText(leaf.PropertyType))
            {
                if (segments.Length != 1)
                    throw new InvalidDataException($"Property path '{path}' is not supported on '{itemType.Name}'.");
                ValidateMultilingualText(value, path);
                return;
            }
            if (!IsSupportedValue(leaf.PropertyType))
                throw new InvalidDataException($"Property '{path}' is not a supported value property on '{itemType.Name}'.");
            if (leaf.SetMethod?.IsPublic != true)
                throw new InvalidDataException($"Property '{path}' is read-only on '{itemType.Name}'.");
            ValidatePropertyValue(leaf.PropertyType, value, path);
            Type effectiveType = Nullable.GetUnderlyingType(leaf.PropertyType) ?? leaf.PropertyType;
            try
            {
                ConvertJsonValue(effectiveType, value, path);
            }
            catch (FormatException error)
            {
                throw new InvalidDataException($"Value {value.GetRawText()} is out of range for '{path}' ({effectiveType.Name}).", error);
            }
        }

        /// <summary>
        /// Applies normalized leaf updates to an item (or any object with the same property shape) and verifies each
        /// one by reading it back. Throws on the first mismatch, so a surrounding transaction is never committed.
        /// </summary>
        private static void ApplyAndVerifyItemPropertyUpdates(
            object item,
            IList<KeyValuePair<string, JsonElement>> updates,
            IReadOnlyList<string> languages)
        {
            var expectedTexts = new List<KeyValuePair<object, string>>();
            foreach (KeyValuePair<string, JsonElement> update in updates)
            {
                object target = ResolveItemPropertyTarget(item, update.Key, out PropertyInfo leaf);
                if (IsMultilingualText(leaf.PropertyType))
                {
                    object multilingualText = leaf.GetValue(target, null)
                        ?? throw new InvalidDataException($"Text property '{update.Key}' is not available on '{item.GetType().Name}'.");
                    expectedTexts.AddRange(ApplyItemText(multilingualText, update.Value, update.Key, languages));
                }
                else
                {
                    SetPropertyValue(target, leaf, update.Value);
                }
            }

            foreach (KeyValuePair<string, JsonElement> update in updates)
            {
                object target = ResolveItemPropertyTarget(item, update.Key, out PropertyInfo leaf);
                if (IsMultilingualText(leaf.PropertyType))
                    continue;
                Type effectiveType = Nullable.GetUnderlyingType(leaf.PropertyType) ?? leaf.PropertyType;
                object? expected = ConvertJsonValue(effectiveType, update.Value, update.Key);
                object? actual = leaf.GetValue(target, null);
                if (!AreItemPropertyValuesEqual(expected, actual))
                    throw new InvalidOperationException(
                        $"Property '{item.GetType().Name}.{update.Key}' failed read-back verification: expected " +
                        $"{DescribeItemPropertyValue(expected)}, read back {DescribeItemPropertyValue(actual)}.");
            }

            foreach (KeyValuePair<object, string> expectedText in expectedTexts)
            {
                string? actual = expectedText.Key.GetType().GetProperty("Text")?.GetValue(expectedText.Key, null)?.ToString();
                if (!AreHmiMarkupEqual(actual, expectedText.Value))
                    throw new InvalidOperationException(
                        $"Text '{item.GetType().Name}.{HmiItemTextPropertyName}' failed read-back verification: " +
                        $"expected '{expectedText.Value}', read back '{actual}'.");
            }
        }

        private static object ResolveItemPropertyTarget(object item, string path, out PropertyInfo leaf)
        {
            string[] segments = path.Split('.');
            object target = item;
            for (int i = 0; i < segments.Length - 1; i++)
            {
                PropertyInfo partProperty = FindProperty(target.GetType(), segments[i]);
                target = partProperty.GetValue(target, null)
                    ?? throw new InvalidDataException($"Part '{segments[i]}' is not available on '{item.GetType().Name}'.");
            }
            leaf = FindProperty(target.GetType(), segments[segments.Length - 1]);
            return target;
        }

        /// <summary>
        /// Writes Text to every active language. A plain string replaces only the paragraph text and keeps the
        /// existing &lt;body&gt;/&lt;p&gt; attributes; {"html":"..."} writes raw markup; a language map must cover
        /// every active language. Returns the markup expected on read-back for each language entry.
        /// </summary>
        private static List<KeyValuePair<object, string>> ApplyItemText(
            object multilingualText,
            JsonElement value,
            string name,
            IReadOnlyList<string> languages)
        {
            List<object> items = GetMultilingualItems(multilingualText);
            if (items.Count == 0)
                throw new InvalidDataException($"Multilingual text '{name}' has no active language entries.");

            var targets = new List<KeyValuePair<object, string>>();
            if (value.ValueKind == JsonValueKind.String || value.TryGetProperty("html", out _))
            {
                foreach (object textItem in items)
                    targets.Add(new KeyValuePair<object, string>(textItem, ComposeItemText(textItem, value)));
            }
            else
            {
                var matched = new HashSet<object>();
                foreach (JsonProperty languageText in value.EnumerateObject())
                {
                    object? textItem = items.Select((entry, index) => new { Item = entry, Index = index })
                        .Where(entry => string.Equals(GetLanguageCode(entry.Item, entry.Index, languages),
                            languageText.Name, StringComparison.OrdinalIgnoreCase))
                        .Select(entry => entry.Item).FirstOrDefault();
                    if (textItem == null)
                        throw new InvalidDataException($"Language '{languageText.Name}' for '{name}' is not configured in the HMI.");
                    if (!matched.Add(textItem))
                        throw new InvalidDataException($"Language '{languageText.Name}' for '{name}' is given more than once.");
                    targets.Add(new KeyValuePair<object, string>(textItem, ComposeItemText(textItem, languageText.Value)));
                }
                foreach (object textItem in items)
                    if (!matched.Contains(textItem))
                        throw new InvalidDataException($"The language map for '{name}' omits an active HMI language.");
            }

            foreach (KeyValuePair<object, string> target in targets)
                SetTextItem(target.Key, target.Value);
            return targets;
        }

        private static string ComposeItemText(object textItem, JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.String)
                return value.GetProperty("html").GetString()!;
            string requested = value.GetString()!;
            string? existing = textItem.GetType().GetProperty("Text")?.GetValue(textItem, null)?.ToString();
            // A string that is already an unstyled <body><p>...</p></body> contributes its text only, so the
            // existing paragraph style is still kept.
            if (TryExtractPlainText(requested, out string plain))
                return ReplaceParagraphText(existing, plain, decodeNumericReferences: false);
            return ReplaceParagraphText(existing, requested);
        }

        private static bool AreItemPropertyValuesEqual(object? expected, object? actual)
        {
            if (expected == null || actual == null)
                return expected == null && actual == null;
            if (expected is Color expectedColor && actual is Color actualColor)
                return expectedColor.ToArgb() == actualColor.ToArgb();
            if (expected is float expectedSingle && actual is float actualSingle)
                return Math.Abs(expectedSingle - actualSingle) <= 1e-4f * Math.Max(1f, Math.Abs(expectedSingle));
            if (expected is double expectedDouble && actual is double actualDouble)
                return Math.Abs(expectedDouble - actualDouble) <= 1e-9 * Math.Max(1d, Math.Abs(expectedDouble));
            return Equals(expected, actual);
        }

        private static string DescribeItemPropertyValue(object? value)
        {
            if (value == null)
                return "null";
            try
            {
                return SerializePropertyValue(value).GetRawText();
            }
            catch (InvalidDataException)
            {
                return value.ToString() ?? value.GetType().Name;
            }
        }

        /// <summary>
        /// Reports updated paths that also have a dynamization. The static value is still written (as the TIA
        /// property grid does) and the dynamization is never touched. Font leaves are looked up on the item as
        /// "Font.Size" and on the HmiFontPart's own Dynamizations as "Size", because where TIA stores them is not
        /// documented.
        /// </summary>
        private static List<string> FindDynamizedItemPropertyWarnings(
            HmiScreenItemBase item,
            IEnumerable<KeyValuePair<string, JsonElement>> updates)
        {
            var warnings = new List<string>();
            foreach (KeyValuePair<string, JsonElement> update in updates)
            {
                string path = update.Key;
                DynamizationBase? dynamization = TryFindDynamization(item.Dynamizations, path);
                int dot = path.IndexOf('.');
                if (dynamization == null && dot > 0)
                {
                    object? part = TryGetPartValue(item, path.Substring(0, dot));
                    if (part is UIBase partBase)
                        dynamization = TryFindDynamization(partBase.Dynamizations, path.Substring(dot + 1));
                }
                if (dynamization != null)
                    warnings.Add(
                        $"'{path}' has a {dynamization.GetType().Name}; the static value was written but the " +
                        "dynamization still controls the property at runtime. The dynamization was not changed.");
            }
            return warnings;
        }

        private static DynamizationBase? TryFindDynamization(DynamizationBaseComposition? dynamizations, string name)
        {
            if (dynamizations == null)
                return null;
            try
            {
                return dynamizations.Find(name);
            }
            catch (Exception)
            {
                // Detection only: a lookup failure must not block the update.
                return null;
            }
        }

        private static object? TryGetPartValue(object item, string partName)
        {
            try
            {
                PropertyInfo? property = FindOptionalProperty(item.GetType(), partName);
                return property?.GetValue(item, null);
            }
            catch (TargetInvocationException)
            {
                return null;
            }
        }

        /// <summary>
        /// HmiScreenWindow.Screen must name an existing screen of the same HMI exactly (ordinal). Returns a warning
        /// when the window would show the screen that contains it.
        /// </summary>
        private static string? ValidateScreenWindowTarget(HmiSoftware hmi, HmiScreen containingScreen, JsonElement value)
        {
            string screenName = value.GetString() ?? "";
            if (screenName.Length == 0)
                throw new InvalidDataException(
                    "Screen must name an existing screen; clearing a screen window is not supported by this tool.");
            List<HmiScreen> screens = EnumerateHmiScreens(hmi.Screens, hmi.ScreenGroups).ToList();
            int exact = screens.Count(screen => string.Equals(screen.Name, screenName, StringComparison.Ordinal));
            if (exact == 0)
            {
                string? differentCase = screens.Select(screen => screen.Name)
                    .FirstOrDefault(name => string.Equals(name, screenName, StringComparison.OrdinalIgnoreCase));
                throw new InvalidDataException(
                    $"Screen '{screenName}' does not exist in HMI '{hmi.Name}'" +
                    (differentCase != null ? $"; did you mean '{differentCase}'? Screen names are matched exactly." : "."));
            }
            if (exact > 1)
                throw new InvalidDataException($"Screen name '{screenName}' is ambiguous in HMI '{hmi.Name}'.");
            return string.Equals(containingScreen.Name, screenName, StringComparison.Ordinal)
                ? $"Screen window shows '{screenName}', the screen that contains it."
                : null;
        }

        private static JsonElement AddItemUpdateWarnings(JsonElement document, List<string> warnings)
        {
            if (warnings.Count == 0 || document.ValueKind != JsonValueKind.Object)
                return document;
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (JsonProperty property in document.EnumerateObject())
                result[property.Name] = property.Value;
            result["warnings"] = warnings;
            return JsonSerializer.SerializeToElement(result, HmiJsonOptions);
        }
    }
}
