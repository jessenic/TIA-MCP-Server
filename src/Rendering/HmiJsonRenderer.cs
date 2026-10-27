using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace TiaOpennessMcp.Rendering
{
    // Deliberately independent of Siemens assemblies and of all event/dynamization execution.
    public sealed class HmiJsonRenderer
    {
        private readonly Dictionary<string, JsonObject> screens = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        private readonly Dictionary<string, JsonObject> defaults = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        private readonly HashSet<string> warnings = new HashSet<string>(StringComparer.Ordinal);
        private JsonObject styles = new JsonObject();
        private string language = "en-US";
        private double scale;
        private bool compact;
        private readonly List<LayoutItem> layout = new List<LayoutItem>();
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public static object Render(string inputDirectory, string outputDirectory, string? screenName = null,
            string? language = null, double scale = 2)
        {
            return new HmiJsonRenderer().Run(inputDirectory, outputDirectory, screenName, language, scale);
        }

        private object Run(string inputDirectory, string outputDirectory, string? selector, string? requestedLanguage, double imageScale)
        {
            if (double.IsNaN(imageScale) || double.IsInfinity(imageScale) || imageScale < 0.5 || imageScale > 4)
                throw new ArgumentException("scale must be between 0.5 and 4.");
            scale = imageScale;
            string root = Path.GetFullPath(inputDirectory);
            string output = Path.GetFullPath(outputDirectory);
            if (string.Equals(root.TrimEnd('\\'), output.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The output directory must differ from the input directory.");
            JsonObject manifest = ReadObject(Path.Combine(root, "manifest.json"));
            RequireFormat(manifest);
            compact = Bool(manifest, "compact", true);
            var languages = manifest["languages"]?.AsArray().Select(n => n!.GetValue<string>()).ToList()
                ?? new List<string>();
            language = requestedLanguage ?? (languages.Contains("en-US") ? "en-US" : languages.FirstOrDefault()) ?? "en-US";
            if (languages.Count > 0 && !languages.Contains(language, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException($"Language '{language}' is not in the manifest.");
            foreach (JsonNode? reference in RequiredArray(manifest, "screenFiles"))
            {
                var entry = reference!.AsObject();
                JsonObject screen = ReadObject(Resolve(root, Str(entry, "path")));
                RequireFormat(screen);
                string key = ScreenKey(Str(screen, "name"), Str(screen, "group"));
                if (key != ScreenKey(Str(entry, "name"), Str(entry, "group")))
                    throw new InvalidDataException($"Screen identity does not match manifest: '{key}'.");
                if (screens.ContainsKey(key)) throw new InvalidDataException($"Duplicate screen '{key}'.");
                screens.Add(key, screen);
                ValidateDimensions(screen);
                if (RequiredArray(screen, "items").Count > 5000)
                    throw new InvalidDataException($"Screen '{key}' exceeds 5000 items.");
            }
            if (Str(manifest, "stylesFile") is string styleFile && styleFile.Length > 0)
                styles = ReadObject(Resolve(root, styleFile))["styles"]?.AsObject() ?? new JsonObject();
            if (Str(manifest, "defaultsFile") is string defaultsFile && defaultsFile.Length > 0)
            {
                var document = ReadObject(Resolve(root, defaultsFile));
                RequireFormat(document);
                foreach (JsonNode? node in RequiredArray(document, "items"))
                {
                    var item = node!.AsObject();
                    defaults.Add(Identity(item), item);
                }
            }
            var selected = screens.Where(s => selector == null || s.Key == selector ||
                Str(s.Value, "name") == selector).ToList();
            if (selected.Count == 0) throw new InvalidDataException($"No screens match '{selector}'.");
            if (selector != null && selected.Count != 1)
                throw new InvalidDataException($"Screen '{selector}' is ambiguous; use its group/name path.");
            Directory.CreateDirectory(output);
            string indexFile = Path.Combine(output, "render-index.json");
            if (File.Exists(indexFile)) throw new IOException($"Refusing to overwrite '{indexFile}'. Use a fresh output directory.");
            var results = new List<object>();
            int index = 0;
            foreach (var pair in selected)
            {
                warnings.Clear();
                layout.Clear();
                if (compact && defaults.Count == 0)
                    warnings.Add("Legacy compact export lacks defaultsFile. Omitted values are approximate; use a full or newly exported compact directory for reliable layout checking.");
                warnings.Add("Static engineering preview, not a WinCC runtime screenshot. Scripts, tag values, expressions and runtime navigation are not executed.");
                int width = (int)Num(pair.Value["props"], "Width", 0);
                int height = (int)Num(pair.Value["props"], "Height", 0);
                string stem = (++index).ToString("D4", CultureInfo.InvariantCulture) + "-" +
                    Regex.Replace(pair.Key, @"[^a-zA-Z0-9._-]", "_");
                string png = Path.Combine(output, stem + ".png");
                string annotated = Path.Combine(output, stem + ".layout.png");
                string report = Path.Combine(output, stem + ".layout.json");
                foreach (string file in new[] { png, annotated, report })
                    if (File.Exists(file)) throw new IOException($"Refusing to overwrite '{file}'. Use a fresh output directory.");
                using (var bitmap = new Bitmap((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale)))
                {
                    bitmap.SetResolution(96, 96);
                    using (Graphics graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.Clear(Color.White);
                        graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                        graphics.ScaleTransform((float)scale, (float)scale);
                        DrawScreen(graphics, pair.Key, new HashSet<string>(StringComparer.Ordinal), "", 1);
                    }
                    bitmap.Save(png, ImageFormat.Png);
                    using (Graphics graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.ScaleTransform((float)scale, (float)scale);
                        DrawOverlay(graphics);
                    }
                    bitmap.Save(annotated, ImageFormat.Png);
                }
                var document = new
                {
                    format = "tia-mcp-hmi-layout/1", screen = pair.Key, language, scale,
                    width, height, imageWidth = (int)Math.Ceiling(width * scale), imageHeight = (int)Math.Ceiling(height * scale),
                    image = png, annotatedImage = annotated,
                    coordinateSystem = "Screen pixels before output scaling; bounds include ancestor transforms and rotation. IDs match the annotated PNG.",
                    warnings = warnings.OrderBy(w => w, StringComparer.Ordinal).ToArray(),
                    items = layout.ToArray()
                };
                File.WriteAllText(report, JsonSerializer.Serialize(document, JsonOptions));
                results.Add(new { screen = pair.Key, image = png, annotatedImage = annotated, report,
                    itemCount = layout.Count, warningCount = warnings.Count });
            }
            var result = new { format = "tia-mcp-hmi-render/1", inputDirectory = root, outputDirectory = output,
                language, scale, screens = results };
            File.WriteAllText(indexFile, JsonSerializer.Serialize(result, JsonOptions));
            return result;
        }

        private void DrawScreen(Graphics graphics, string key, HashSet<string> stack, string parent, float opacity)
        {
            if (stack.Count >= 12 || !stack.Add(key))
                throw new InvalidDataException($"Cyclic or excessively deep screen-window reference at '{key}'.");
            JsonObject screen = screens[key];
            var props = screen["props"];
            graphics.SetClip(new RectangleF(0, 0, (float)Num(props, "Width", 0), (float)Num(props, "Height", 0)),
                CombineMode.Intersect);
            using (var background = new SolidBrush(ReadColor(props, "BackColor", Color.White, opacity)))
                graphics.FillRectangle(background, 0, 0, (float)Num(props, "Width", 0), (float)Num(props, "Height", 0));
            if (Str(props, "BackGraphic").Length > 0)
                warnings.Add($"{key}: screen background graphic is not included in JSON and is not rendered.");
            int order = 0;
            foreach (JsonNode? node in RequiredArray(screen, "items"))
            {
                JsonObject item = node!.AsObject();
                JsonObject effective = EffectiveItem(item);
                var p = effective["props"]!;
                string name = Str(item, "name");
                string path = parent.Length == 0 ? key + "/" + name : parent + "/" + name;
                string type = Str(item, "type");
                float x = (float)Num(p, "Left", 0), y = (float)Num(p, "Top", 0);
                float w = (float)Num(p, "Width", 80), h = (float)Num(p, "Height", 24);
                if (w < 0 || h < 0) throw new InvalidDataException($"Negative item size: '{path}'.");
                float itemOpacity = (float)Num(p, "Opacity", 1);
                if (itemOpacity < 0 || itemOpacity > 1)
                    throw new InvalidDataException($"Opacity must be between 0 and 1 at '{path}'.");
                float alpha = opacity * itemOpacity;
                bool visible = Bool(p, "Visible", true);
                var state = graphics.Save();
                try
                {
                    graphics.TranslateTransform(x, y);
                    float angle = (float)Num(p, "RotationAngle", 0);
                    if (angle != 0)
                    {
                        float cx = w / 2 + (float)Num(p, "RotationCenterX", 0);
                        float cy = h / 2 + (float)Num(p, "RotationCenterY", 0);
                        graphics.TranslateTransform(cx, cy);
                        graphics.RotateTransform(angle);
                        graphics.TranslateTransform(-cx, -cy);
                        if (Str(p, "RotationCenterPlacement") != "AbsoluteFromCenter")
                            warnings.Add($"{path}: rotation center placement is approximated as center-relative.");
                    }
                    var local = new RectangleF(0, 0, w, h);
                    RectangleF bounds = TransformBounds(graphics, local);
                    RectangleF clip = TransformBounds(graphics, graphics.ClipBounds);
                    var entry = new LayoutItem
                    {
                        Id = layout.Count + 1, Path = path, Type = type, ZOrder = order++,
                        Parent = parent, Bounds = Box(bounds), Visible = visible && alpha > 0,
                        ClipBounds = Box(clip), Clipped = !clip.Contains(bounds), Text = ""
                    };
                    layout.Add(entry);
                    if (item["dynamizations"] is JsonArray dyn && dyn.Count > 0)
                    {
                        entry.DynamicProperties = dyn.Select(d => Str(d, "property")).ToArray();
                        warnings.Add($"{path}: dynamic properties ({string.Join(", ", entry.DynamicProperties)}) use exported static state, not runtime values.");
                    }
                    if (alpha != 1 && alpha > 0)
                        warnings.Add($"{path}: opacity is approximated per primitive, not as a composited widget layer.");
                    if (!visible || alpha <= 0 || ((w == 0 || h == 0) && type != "HmiLine")) continue;
                    if (type == "HmiScreenWindow")
                        DrawWindow(graphics, effective, key, stack, path, alpha, local);
                    else
                        DrawWidget(graphics, effective, path, alpha, local, entry);
                }
                finally { graphics.Restore(state); }
                if (layout.Count > 20000) throw new InvalidDataException("Expanded screen exceeds 20000 items.");
            }
            stack.Remove(key);
        }

        private void DrawWindow(Graphics g, JsonObject item, string currentKey, HashSet<string> stack,
            string path, float opacity, RectangleF rect)
        {
            JsonNode p = item["props"]!;
            string reference = Str(p, "Screen").Replace('\\', '/').Trim('/');
            string group = currentKey.Contains("/") ? currentKey.Substring(0, currentKey.LastIndexOf('/')) : "";
            string relative = ScreenKey(reference, group);
            string? key = screens.ContainsKey(relative) ? relative : screens.ContainsKey(reference) ? reference : null;
            if (key == null && reference.Length > 0)
            {
                var matches = screens.Where(s => Str(s.Value, "name") == reference).Select(s => s.Key).ToList();
                if (matches.Count == 1) key = matches[0];
                else if (matches.Count > 1) throw new InvalidDataException($"Ambiguous nested screen '{reference}' at '{path}'.");
            }
            g.SetClip(rect, CombineMode.Intersect);
            if (key == null)
            {
                Placeholder(g, rect, "Screen: " + (reference.Length == 0 ? "(unset)" : reference), opacity);
                warnings.Add($"{path}: nested screen '{reference}' is unavailable.");
                return;
            }
            float zoom = (float)Num(p, "CurrentZoomFactor", 1);
            if (zoom <= 0 || zoom > 16) throw new InvalidDataException($"Invalid window zoom at '{path}'.");
            float sx = zoom, sy = zoom;
            if (Str(p, "Adaption") != "None" && Str(p, "Adaption").Length > 0)
            {
                sx = rect.Width / (float)Num(screens[key]["props"], "Width", 1);
                sy = rect.Height / (float)Num(screens[key]["props"], "Height", 1);
                warnings.Add($"{path}: '{Str(p, "Adaption")}' adaption is approximated as stretch-to-window.");
            }
            g.ScaleTransform(sx, sy);
            g.TranslateTransform(-(float)Num(p, "HorizontalScrollBarPosition", 0),
                -(float)Num(p, "VerticalScrollBarPosition", 0));
            DrawScreen(g, key, stack, path, opacity);
            if (Str(p, "WindowFlags") != "None" && Str(p, "WindowFlags").Length > 0)
                warnings.Add($"{path}: runtime window chrome ({Str(p, "WindowFlags")}) is not rendered.");
        }

        private void DrawWidget(Graphics g, JsonObject item, string path, float opacity, RectangleF rect, LayoutItem entry)
        {
            JsonNode p = item["props"]!;
            string type = Str(item, "type");
            float borderWidth = (float)Num(p, "BorderWidth", 1);
            Color back = ReadColor(p, "BackColor", Color.White, opacity);
            Color fore = ReadColor(p, "ForeColor", Color.Black, opacity);
            Color border = ReadColor(p, "BorderColor", Color.Gray, opacity);
            bool alternate = Bool(p, "IsAlternateState", false);
            if (alternate) back = ReadColor(p, "AlternateBackColor", back, opacity);
            if (!Bool(p, "Enabled", true))
                warnings.Add($"{path}: disabled-state theme is approximated with the exported colors.");
            using var pen = new Pen(border, Math.Max(0.1f, borderWidth));
            if (borderWidth < 0) throw new InvalidDataException($"Invalid border width at '{path}'.");
            if (Str(p, "DashType").IndexOf("Dash", StringComparison.OrdinalIgnoreCase) >= 0) pen.DashStyle = DashStyle.Dash;
            using var brush = new SolidBrush(back);
            if (type == "HmiLine")
            {
                pen.Color = ReadColor(p, "LineColor", border, opacity);
                pen.Width = (float)Num(p, "LineWidth", borderWidth);
                if (pen.Width <= 0) throw new InvalidDataException($"Invalid line width at '{path}'.");
                if (Str(p, "CapType") == "Round") pen.StartCap = pen.EndCap = LineCap.Round;
                if (Str(p, "StartType", "Line") != "Line" || Str(p, "EndType", "Line") != "Line")
                    warnings.Add($"{path}: decorative line end markers are not rendered.");
                g.DrawLine(pen, (float)Num(p, "X1", 0), (float)Num(p, "Y1", 0),
                    (float)Num(p, "X2", rect.Width), (float)Num(p, "Y2", rect.Height));
                return;
            }
            bool supported = new[] { "HmiText", "HmiTextBox", "HmiRectangle", "HmiCircle", "HmiEllipse",
                "HmiButton", "HmiIOField", "HmiToggleSwitch" }.Contains(type);
            if (!supported)
            {
                Placeholder(g, rect, type + (Str(item, "containedType").Length > 0 ? "\n" + Str(item, "containedType") : ""), opacity);
                warnings.Add($"{path}: {type} is a geometry placeholder; its runtime assets/compositions are not in the export.");
                entry.Placeholder = true;
                return;
            }
            bool textOnly = type == "HmiText";
            if (Str(p, "BackFillPattern", "Solid") != "Solid" && Str(p, "BackFillPattern") != "Transparent")
                warnings.Add($"{path}: fill pattern is approximated as a solid color.");
            if (Bool(p, "ShowFillLevel", false))
                warnings.Add($"{path}: fill-level visualization is not rendered.");
            using (GraphicsPath shape = Shape(rect, p["Corners"], type == "HmiCircle" || type == "HmiEllipse"))
            {
                if (!textOnly)
                {
                    if (Str(p, "BackFillPattern") != "Transparent") g.FillPath(brush, shape);
                    if (borderWidth > 0) g.DrawPath(pen, shape);
                }
            }
            if (type == "HmiRectangle" || type == "HmiCircle" || type == "HmiEllipse") return;
            g.SetClip(rect, CombineMode.Intersect);
            string text = Localized(item["text"]?[alternate ? "AlternateText" : "Text"], path);
            if (text.Length == 0) text = Localized(item["text"]?["Text"], path);
            if (type == "HmiIOField")
            {
                string value = Str(p, "ProcessValue");
                string format = Str(p, "OutputFormat");
                bool bound = (item["dynamizations"] as JsonArray)?.Any(d => Str(d, "property") == "ProcessValue") == true;
                if (bound) value = "?";
                if (value.Length == 0) value = "?";
                text = format.Length == 0 ? value : Regex.Replace(format, @"\{[^}]*\}", value);
                if (p["InputBehavior"]?["HiddenInput"]?.GetValue<bool>() == true) text = "****";
                entry.ValueUnknown = bound;
            }
            if (type == "HmiToggleSwitch")
                warnings.Add($"{path}: switch skin is approximated as a labeled button in its exported static state.");
            if (Str(p, "Graphic").Length > 0 || Str(p, "AlternateGraphic").Length > 0)
                warnings.Add($"{path}: graphic resource is unavailable; text and geometry are rendered.");
            entry.Text = text;
            using Font font = CreateFont(p["Font"], path);
            JsonNode? padding = p["Padding"];
            var content = new RectangleF((float)Num(padding, "Left", 0), (float)Num(padding, "Top", 0),
                Math.Max(0, rect.Width - (float)(Num(padding, "Left", 0) + Num(padding, "Right", 0))),
                Math.Max(0, rect.Height - (float)(Num(padding, "Top", 0) + Num(padding, "Bottom", 0))));
            using var formatText = (StringFormat)StringFormat.GenericTypographic.Clone();
            formatText.Alignment = Alignment(Str(p, "HorizontalTextAlignment", Str(p["Content"], "HorizontalTextAlignment", "Left")));
            formatText.LineAlignment = Alignment(Str(p, "VerticalTextAlignment", Str(p["Content"], "VerticalTextAlignment", "Center")));
            bool wrap = Bool(p, "WordWrap", false);
            if (!wrap) formatText.FormatFlags |= StringFormatFlags.NoWrap;
            if (Str(p, "TextTrimming", "None") != "None") formatText.Trimming = StringTrimming.EllipsisCharacter;
            if (text.Length > 0 && content.Width > 0 && content.Height > 0)
            {
                SizeF measured = g.MeasureString(text, font,
                    new SizeF(wrap ? content.Width : 100000, 100000), formatText);
                entry.TextOverflow = measured.Width > content.Width + 1 || measured.Height > content.Height + 1;
                using var textBrush = new SolidBrush(fore);
                g.DrawString(text, font, textBrush, content, formatText);
            }
        }

        private Font CreateFont(JsonNode? node, string path)
        {
            string name = Str(node, "Name", "Arial");
            float size = (float)Num(node, "Size", 10);
            if (size <= 0 || size > 1000) throw new InvalidDataException($"Invalid font size at '{path}'.");
            FontStyle style = Str(node, "Weight").IndexOf("Bold", StringComparison.OrdinalIgnoreCase) >= 0 ? FontStyle.Bold : FontStyle.Regular;
            if (Bool(node, "Italic", false)) style |= FontStyle.Italic;
            if (Bool(node, "Underline", false)) style |= FontStyle.Underline;
            if (Bool(node, "StrikeOut", false)) style |= FontStyle.Strikeout;
            var font = new Font(name, size, style, GraphicsUnit.Point);
            if (!string.Equals(font.Name, name, StringComparison.OrdinalIgnoreCase))
                warnings.Add($"Font '{name}' is unavailable; substituted '{font.Name}'. Text metrics may differ.");
            return font;
        }

        private string Localized(JsonNode? node, string path)
        {
            if (node == null) return "";
            string text;
            if (node is JsonValue) text = node.GetValue<string>();
            else if (node["html"] != null) text = node["html"]!.GetValue<string>();
            else if (node[language] is JsonValue localized) text = localized.GetValue<string>();
            else if (node[language]?["html"] != null) text = node[language]!["html"]!.GetValue<string>();
            else
            {
                warnings.Add($"{path}: missing '{language}' translation; no other language was silently substituted.");
                return "";
            }
            if (!Regex.IsMatch(text, @"</?(?:body|p|ProjectText|span|br)\b", RegexOptions.IgnoreCase)) return text;
            if (Regex.IsMatch(text, @"<(?:span|p)\b[^>]+>", RegexOptions.IgnoreCase))
                warnings.Add($"{path}: inline rich-text formatting is approximated using the item font.");
            text = Regex.Replace(text, @"<br\s*/?>|</p>\s*<p[^>]*>", "\n", RegexOptions.IgnoreCase);
            return WebUtility.HtmlDecode(Regex.Replace(text, "<[^>]*>", "")).Trim();
        }

        private JsonObject EffectiveItem(JsonObject item)
        {
            var result = (JsonObject)item.DeepClone();
            var props = new JsonObject();
            var texts = new JsonObject();
            if (compact && Bool(item, "compact", true) && defaults.TryGetValue(Identity(item), out JsonObject? preset))
            {
                Merge(props, preset["props"] as JsonObject);
                Merge(texts, preset["text"] as JsonObject);
            }
            string style = Str(item, "style");
            if (style.Length > 0)
            {
                if (styles[style] is not JsonObject styleProps) throw new InvalidDataException($"Missing style '{style}'.");
                Merge(props, styleProps);
            }
            Merge(props, item["props"] as JsonObject);
            Merge(texts, item["text"] as JsonObject);
            result["props"] = props;
            result["text"] = texts;
            return result;
        }

        private static void Merge(JsonObject target, JsonObject? source)
        {
            if (source == null) return;
            foreach (var pair in source)
            {
                if (pair.Value is JsonObject child && target[pair.Key] is JsonObject existing) Merge(existing, child);
                else target[pair.Key] = pair.Value?.DeepClone();
            }
        }

        private RectangleF TransformBounds(Graphics g, RectangleF rect)
        {
            var points = new[] { new PointF(rect.Left, rect.Top), new PointF(rect.Right, rect.Top),
                new PointF(rect.Right, rect.Bottom), new PointF(rect.Left, rect.Bottom) };
            using (Matrix matrix = g.Transform) matrix.TransformPoints(points);
            return RectangleF.FromLTRB(points.Min(p => p.X) / (float)scale, points.Min(p => p.Y) / (float)scale,
                points.Max(p => p.X) / (float)scale, points.Max(p => p.Y) / (float)scale);
        }

        private void DrawOverlay(Graphics g)
        {
            using var pen = new Pen(Color.FromArgb(210, 0, 100, 220), 0.75f) { DashStyle = DashStyle.Dash };
            using var label = new SolidBrush(Color.FromArgb(230, 0, 65, 150));
            using var font = new Font("Arial", 8, FontStyle.Bold, GraphicsUnit.Point);
            foreach (var item in layout.Where(i => i.Visible))
            {
                double[] b = item.Bounds;
                double[] c = item.ClipBounds;
                var state = g.Save();
                g.SetClip(new RectangleF((float)c[0], (float)c[1], (float)c[2], (float)c[3]), CombineMode.Intersect);
                g.DrawRectangle(pen, (float)b[0], (float)b[1], (float)b[2], (float)b[3]);
                string text = item.Id.ToString(CultureInfo.InvariantCulture);
                SizeF size = g.MeasureString(text, font);
                g.FillRectangle(label, (float)b[0], (float)b[1], size.Width, size.Height);
                g.DrawString(text, font, Brushes.White, (float)b[0], (float)b[1]);
                g.Restore(state);
            }
        }

        private static GraphicsPath Shape(RectangleF r, JsonNode? corners, bool ellipse)
        {
            var path = new GraphicsPath();
            if (ellipse) { path.AddEllipse(r); return path; }
            string[] names = { "TopLeftRadius", "TopRightRadius", "BottomRightRadius", "BottomLeftRadius" };
            float[] radii = names.Select(n => Math.Max(0, Math.Min((float)Num(corners, n, 0), Math.Min(r.Width, r.Height) / 2))).ToArray();
            var origins = new[] { new PointF(0, 0), new PointF(r.Width, 0), new PointF(r.Width, r.Height), new PointF(0, r.Height) };
            for (int i = 0; i < 4; i++)
            {
                float radius = radii[i];
                if (radius == 0) path.AddLine(origins[i], origins[i]);
                else
                {
                    float x = i == 1 || i == 2 ? r.Width - radius * 2 : 0;
                    float y = i >= 2 ? r.Height - radius * 2 : 0;
                    path.AddArc(x, y, radius * 2, radius * 2, 180 + i * 90, 90);
                }
            }
            path.CloseFigure();
            return path;
        }

        private static void Placeholder(Graphics g, RectangleF rect, string text, float opacity)
        {
            using var brush = new HatchBrush(HatchStyle.LightDownwardDiagonal,
                Color.FromArgb((int)(Math.Min(1, opacity) * 90), 148, 163, 184), Color.FromArgb(245, 247, 250));
            g.FillRectangle(brush, rect);
            using var pen = new Pen(Color.SlateGray, 1) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
            var state = g.Save();
            g.SetClip(rect, CombineMode.Intersect);
            using var font = new Font("Arial", 9);
            g.DrawString(text, font, Brushes.DimGray, rect);
            g.Restore(state);
        }

        private void ValidateDimensions(JsonObject screen)
        {
            double w = Num(screen["props"], "Width", 0), h = Num(screen["props"], "Height", 0);
            double pixelWidth = Math.Ceiling(w * scale), pixelHeight = Math.Ceiling(h * scale);
            if (w <= 0 || h <= 0 || w != Math.Truncate(w) || h != Math.Truncate(h) ||
                pixelWidth > 8192 || pixelHeight > 8192 || pixelWidth * pixelHeight > 32000000)
                throw new InvalidDataException("Screen dimensions must be positive integers, at most 8192 pixels per output axis and 32 megapixels.");
        }

        private static JsonObject ReadObject(string file)
        {
            if (new FileInfo(file).Length > 32 * 1024 * 1024) throw new InvalidDataException($"JSON file exceeds 32 MiB: '{file}'.");
            return JsonNode.Parse(File.ReadAllText(file))?.AsObject() ?? throw new InvalidDataException($"Empty JSON: '{file}'.");
        }

        private static string Resolve(string root, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
                throw new InvalidDataException("Manifest paths must be relative.");
            string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', '\\')));
            if (!full.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Manifest path escapes export directory.");
            return full;
        }

        private static void RequireFormat(JsonObject obj)
        {
            if (Str(obj, "format") != "tia-mcp-hmi/1") throw new InvalidDataException("Expected tia-mcp-hmi/1.");
        }

        private static JsonArray RequiredArray(JsonObject obj, string name) =>
            obj[name] as JsonArray ?? throw new InvalidDataException($"Missing array '{name}'.");
        private static string ScreenKey(string name, string group) => group.Length == 0 ? name : group.Trim('/') + "/" + name;
        private static string Identity(JsonObject item) => Str(item, "type") + "|" + Str(item, "containedType");
        private static string Str(JsonNode? node, string name, string fallback = "") =>
            node?[name] is JsonValue value ? value.ToString() : fallback;
        private static bool Bool(JsonNode? node, string name, bool fallback) =>
            node?[name] is JsonValue value ? value.GetValue<bool>() : fallback;
        private static double Num(JsonNode? node, string name, double fallback)
        {
            if (node?[name] == null) return fallback;
            double number = node[name]!.GetValue<double>();
            if (double.IsNaN(number) || double.IsInfinity(number) || Math.Abs(number) > 1000000)
                throw new InvalidDataException($"Invalid numeric property '{name}'.");
            return number;
        }
        private static StringAlignment Alignment(string value) =>
            value == "Center" ? StringAlignment.Center : value == "Right" || value == "Bottom" ? StringAlignment.Far : StringAlignment.Near;
        private static double[] Box(RectangleF r) => new[] { (double)r.X, r.Y, r.Width, r.Height };
        private static Color ReadColor(JsonNode? node, string name, Color fallback, float opacity)
        {
            string text = Str(node, name);
            Color color = fallback;
            if (text.Length > 0)
            {
                if (!Regex.IsMatch(text, @"^#(?:[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$"))
                    throw new InvalidDataException($"Invalid color '{text}'.");
                uint number = uint.Parse(text.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                color = Color.FromArgb(text.Length == 9 ? (int)(number >> 24) : 255,
                    (int)((number >> 16) & 255), (int)((number >> 8) & 255), (int)(number & 255));
            }
            return Color.FromArgb((int)(color.A * Math.Max(0, Math.Min(1, opacity))), color.R, color.G, color.B);
        }

        private sealed class LayoutItem
        {
            public int Id { get; set; }
            public string Path { get; set; } = "";
            public string Parent { get; set; } = "";
            public string Type { get; set; } = "";
            public int ZOrder { get; set; }
            public double[] Bounds { get; set; } = Array.Empty<double>();
            public double[] ClipBounds { get; set; } = Array.Empty<double>();
            public bool Visible { get; set; }
            public bool Clipped { get; set; }
            public bool Placeholder { get; set; }
            public bool TextOverflow { get; set; }
            public bool ValueUnknown { get; set; }
            public string Text { get; set; } = "";
            public string[] DynamicProperties { get; set; } = Array.Empty<string>();
        }
    }
}
