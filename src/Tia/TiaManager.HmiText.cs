using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace TiaOpennessMcp.Tia
{
    public partial class TiaManager
    {
        private static readonly Regex NumericCharacterReferencePattern =
            new Regex("&#(?:[xX](?<hex>[0-9A-Fa-f]{1,6})|(?<dec>[0-9]{1,7}));", RegexOptions.CultureInvariant);

        /// <summary>
        /// Replaces numeric character references such as &amp;#196; or &amp;#xC4; with the character they name.
        /// Named entities, a bare '&amp;' and references to invalid code points (surrogates, values above U+10FFFF,
        /// characters XML cannot hold) are left unchanged.
        /// </summary>
        internal static string DecodeNumericCharacterReferences(string? text)
        {
            if (string.IsNullOrEmpty(text) || text!.IndexOf("&#", StringComparison.Ordinal) < 0)
                return text ?? string.Empty;
            return NumericCharacterReferencePattern.Replace(text, match =>
            {
                Group hex = match.Groups["hex"];
                int codePoint;
                bool parsed = hex.Success
                    ? int.TryParse(hex.Value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out codePoint)
                    : int.TryParse(match.Groups["dec"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out codePoint);
                if (!parsed || codePoint > 0x10FFFF || codePoint >= 0xD800 && codePoint <= 0xDFFF)
                    return match.Value;
                string decoded = char.ConvertFromUtf32(codePoint);
                return decoded.All(XmlConvert.IsXmlChar) || decoded.Length == 2 && XmlConvert.IsXmlSurrogatePair(decoded[1], decoded[0])
                    ? decoded
                    : match.Value;
            });
        }

        /// <summary>
        /// Wraps plain text as a WinCC Unified text paragraph. Numeric character references are decoded first and
        /// the result is XML-escaped, so callers pass plain Unicode and never HTML entities.
        /// </summary>
        internal static string WrapPlainTextAsHtml(string? text)
        {
            return "<body><p>" + (SecurityElement.Escape(DecodeNumericCharacterReferences(text)) ?? string.Empty) +
                "</p></body>";
        }

        private static string WrapEscapedParagraph(string text)
        {
            return "<body><p>" + (SecurityElement.Escape(text) ?? string.Empty) + "</p></body>";
        }

        /// <summary>
        /// Normalizes an expected hmi_import_json text value (plain string, {"html":...} or a language map) to the
        /// compact form SerializeMultilingualText produces, so read-back verification compares like with like.
        /// </summary>
        private static JsonElement NormalizeExpectedText(JsonElement expected)
        {
            if (expected.ValueKind == JsonValueKind.String || IsRawHtmlText(expected))
                return CompactTextValue(ParseTextContent(expected));
            if (expected.ValueKind != JsonValueKind.Object)
                return expected;
            var languageMap = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (JsonProperty language in expected.EnumerateObject())
                languageMap[language.Name] = language.Value.ValueKind == JsonValueKind.String || IsRawHtmlText(language.Value)
                    ? CompactTextValue(ParseTextContent(language.Value))
                    : language.Value;
            if (languageMap.Count > 0 && languageMap.Values.All(value =>
                string.Equals(value.GetRawText(), languageMap.Values.First().GetRawText(), StringComparison.Ordinal)))
                return languageMap.Values.First();
            return JsonSerializer.SerializeToElement(languageMap);
        }

        /// <summary>
        /// <see cref="NormalizeExpectedText"/> for optional texts (discrete alarm eventText): a missing or null
        /// value stays as it is, so the null semantics of the read-back comparison are unchanged.
        /// </summary>
        private static JsonElement? NormalizeExpectedNullableText(JsonElement? expected)
        {
            if (!expected.HasValue || expected.Value.ValueKind == JsonValueKind.Null)
                return expected;
            return NormalizeExpectedText(expected.Value);
        }

        /// <summary>True when a plain string looks like WinCC Unified markup (starts with &lt;body).</summary>
        internal static bool LooksLikeBodyMarkup(string? text)
        {
            return text != null && text.TrimStart().StartsWith("<body", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True when markup parses as XML whose root is a &lt;body&gt; element without a namespace.</summary>
        internal static bool IsWellFormedBodyMarkup(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;
            try
            {
                XElement? root = XDocument.Parse(text).Root;
                return root != null && root.Name.LocalName == "body" && root.Name.Namespace == XNamespace.None;
            }
            catch (XmlException)
            {
                return false;
            }
        }

        /// <summary>
        /// True when replacing the paragraph text of this markup would drop content: more than one paragraph, other
        /// elements in the body, or child elements (span, br, ...) inside the first paragraph.
        /// </summary>
        internal static bool HasMarkupBeyondSingleParagraph(string? markup)
        {
            if (string.IsNullOrWhiteSpace(markup))
                return false;
            try
            {
                XElement? body = XDocument.Parse(markup).Root;
                if (body == null)
                    return false;
                List<XElement> children = body.Elements().ToList();
                return children.Count > 1 || children.Count == 1 && children[0].Elements().Any();
            }
            catch (XmlException)
            {
                return false;
            }
        }

        private static bool IsRawHtmlText(JsonElement value)
        {
            return value.ValueKind == JsonValueKind.Object &&
                value.TryGetProperty("html", out JsonElement html) &&
                html.ValueKind == JsonValueKind.String &&
                value.EnumerateObject().Count() == 1;
        }

        /// <summary>
        /// Replaces the text of an existing WinCC Unified text paragraph and keeps the attributes of its &lt;body&gt;
        /// and first &lt;p&gt; element (for example style="margin: 0; text-align: center;"). Any child markup inside
        /// the paragraph is replaced by the plain text. Markup that cannot be parsed, or has no paragraph, falls back
        /// to <see cref="WrapPlainTextAsHtml"/>.
        /// </summary>
        internal static string ReplaceParagraphText(string? existingMarkup, string? plainText, bool decodeNumericReferences = true)
        {
            string text = decodeNumericReferences ? DecodeNumericCharacterReferences(plainText) : plainText ?? string.Empty;
            if (string.IsNullOrWhiteSpace(existingMarkup))
                return WrapEscapedParagraph(text);
            try
            {
                XElement? body = XDocument.Parse(existingMarkup).Root;
                if (body == null || body.Name.LocalName != "body" || body.Name.Namespace != XNamespace.None)
                    return WrapEscapedParagraph(text);
                XElement? paragraph = body.Elements().FirstOrDefault();
                if (paragraph == null || paragraph.Name.LocalName != "p" || paragraph.Name.Namespace != XNamespace.None)
                    return WrapEscapedParagraph(text);
                var result = new XElement("body", body.Attributes(),
                    new XElement("p", paragraph.Attributes(), new XText(text)));
                return result.ToString(SaveOptions.DisableFormatting);
            }
            catch (XmlException)
            {
                return WrapEscapedParagraph(text);
            }
        }

        /// <summary>
        /// Compares two pieces of WinCC Unified text markup. Both sides are parsed as XML and compared structurally
        /// (attribute order and quoting style do not matter); markup that does not parse is compared ordinally.
        /// </summary>
        internal static bool AreHmiMarkupEqual(string? left, string? right)
        {
            if (string.Equals(left, right, StringComparison.Ordinal))
                return true;
            if (left == null || right == null)
                return false;
            try
            {
                XElement? leftRoot = XDocument.Parse(left).Root;
                XElement? rightRoot = XDocument.Parse(right).Root;
                return leftRoot != null && rightRoot != null && AreXmlElementsEqual(leftRoot, rightRoot);
            }
            catch (XmlException)
            {
                return false;
            }
        }

        private static bool AreXmlElementsEqual(XElement left, XElement right)
        {
            if (left.Name != right.Name)
                return false;
            var leftAttributes = left.Attributes().Where(a => !a.IsNamespaceDeclaration)
                .OrderBy(a => a.Name.ToString(), StringComparer.Ordinal).ToList();
            var rightAttributes = right.Attributes().Where(a => !a.IsNamespaceDeclaration)
                .OrderBy(a => a.Name.ToString(), StringComparer.Ordinal).ToList();
            if (leftAttributes.Count != rightAttributes.Count ||
                leftAttributes.Zip(rightAttributes, (l, r) => l.Name == r.Name && l.Value == r.Value).Any(equal => !equal))
                return false;
            var leftNodes = left.Nodes().Where(n => !(n is XComment)).ToList();
            var rightNodes = right.Nodes().Where(n => !(n is XComment)).ToList();
            if (leftNodes.Count != rightNodes.Count)
                return false;
            for (int i = 0; i < leftNodes.Count; i++)
            {
                if (leftNodes[i] is XElement leftElement && rightNodes[i] is XElement rightElement)
                {
                    if (!AreXmlElementsEqual(leftElement, rightElement))
                        return false;
                }
                else if (leftNodes[i] is XText leftText && rightNodes[i] is XText rightText)
                {
                    if (!string.Equals(leftText.Value, rightText.Value, StringComparison.Ordinal))
                        return false;
                }
                else
                {
                    return false;
                }
            }
            return true;
        }
    }
}
