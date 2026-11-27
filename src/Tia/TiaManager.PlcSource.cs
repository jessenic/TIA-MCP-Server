using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Types;

namespace TiaOpennessMcp.Tia
{
    // tia_get_block_code: TIA-generated source text (PlcExternalSourceSystemGroup.GenerateSource) instead of
    // reconstructing SCL from the SimaticML token tree.
    public partial class TiaManager
    {
        internal static readonly string[] BlockCodeFormats = { "auto", "source", "xml" };

        // Returns "auto", "source" or "xml". "scl" is accepted as an alias of "source".
        internal static string NormalizeBlockCodeFormatRequest(string? format)
        {
            if (format == null || format.Trim().Length == 0)
                return "auto";

            string value = format.Trim().ToLowerInvariant();
            if (value == "scl")
                return "source";
            if (Array.IndexOf(BlockCodeFormats, value) >= 0)
                return value;

            throw new ArgumentException(
                $"format '{format}' is not supported; use one of: {string.Join(", ", BlockCodeFormats)}.");
        }

        // Decides what tia_get_block_code tries first: "source" or "xml".
        internal static string ResolveBlockCodeFormat(string? requested, string programmingLanguage)
        {
            string normalized = NormalizeBlockCodeFormatRequest(requested);
            if (normalized != "auto")
                return normalized;
            return SourceExtensionFor(programmingLanguage) != null ? "source" : "xml";
        }

        // ProgrammingLanguage reported for a PLC data type (PlcType has no ProgrammingLanguage property).
        internal const string PlcTypeCodeLanguage = "UDT";

        // File extension GenerateSource requires for a block language, or null when TIA cannot generate source for it.
        // The V21 'Generate source from block' rules: SCL -> .scl, STL -> .awl, data blocks -> .db, PLC data types -> .udt.
        internal static string? SourceExtensionFor(string? programmingLanguage)
        {
            switch ((programmingLanguage ?? "").Trim().ToUpperInvariant())
            {
                case "SCL": return ".scl";
                case "STL": return ".awl";
                case "DB": return ".db";
                case PlcTypeCodeLanguage: return ".udt";
                default: return null;
            }
        }

        // Null when no block has this name (the caller then looks for a PLC data type).
        private PlcBlock? ResolveBlockForCode(PlcSoftware plc, string blockName)
        {
            if (string.IsNullOrWhiteSpace(blockName))
                throw new ArgumentException("block_name is required.");

            List<BlockLocation> matches = FindBlockLocations(plc.BlockGroup, blockName);
            if (matches.Count == 0)
                return null;
            if (matches.Count == 1)
                return matches[0].Block;

            var exact = matches.Where(m => string.Equals(m.Block.Name, blockName, StringComparison.Ordinal)).ToList();
            if (exact.Count == 1)
                return exact[0].Block;

            string paths = string.Join(", ", matches.Select(m =>
                string.IsNullOrEmpty(m.FolderPath) ? m.Block.Name : $"{m.FolderPath}/{m.Block.Name}"));
            throw new InvalidOperationException(
                $"Block name '{blockName}' is ambiguous in PLC '{plc.Name}' ({paths}); use the exact name.");
        }

        // Exact-case match wins over a case-insensitive one; null when no PLC data type has this name.
        private static PlcType? ResolveTypeForCode(PlcSoftware plc, string typeName)
        {
            var matches = new List<(PlcType Type, string Path)>();
            void Walk(PlcTypeGroup group, string path)
            {
                foreach (PlcType type in group.Types)
                {
                    if (string.Equals(type.Name, typeName, StringComparison.OrdinalIgnoreCase))
                        matches.Add((type, path));
                }
                foreach (PlcTypeUserGroup sub in group.Groups)
                    Walk(sub, string.IsNullOrEmpty(path) ? sub.Name : $"{path}/{sub.Name}");
            }
            Walk(plc.TypeGroup, "");

            if (matches.Count == 0)
                return null;
            if (matches.Count == 1)
                return matches[0].Type;

            var exact = matches.Where(m => string.Equals(m.Type.Name, typeName, StringComparison.Ordinal)).ToList();
            if (exact.Count == 1)
                return exact[0].Type;

            string paths = string.Join(", ", matches.Select(m =>
                string.IsNullOrEmpty(m.Path) ? m.Type.Name : $"{m.Path}/{m.Type.Name}"));
            throw new InvalidOperationException(
                $"PLC data type name '{typeName}' is ambiguous in PLC '{plc.Name}' ({paths}); use the exact name.");
        }

        // Returns null on success, or a message saying why no source could be generated.
        // target is a PlcBlock or a PlcType (both implement IGenerateSource in V21).
        private string? TryGenerateBlockSource(
            PlcSoftware plc,
            object target,
            string targetName,
            string programmingLanguage,
            bool isKnowHowProtected,
            bool withDependencies,
            out string? source,
            out string? extension)
        {
            source = null;
            extension = SourceExtensionFor(programmingLanguage);
            if (extension == null)
            {
                return $"TIA Portal generates source text only for SCL, STL and DB blocks and PLC data types; '{targetName}' is {programmingLanguage}. Use format 'xml'.";
            }
            if (isKnowHowProtected)
            {
                return $"'{targetName}' is know-how protected; TIA Portal does not generate source for it.";
            }
            if (!(target is IGenerateSource generator))
            {
                return $"'{targetName}' does not support source generation in TIA Portal Openness. Use format 'xml'.";
            }

            string directory = Path.Combine(Path.GetTempPath(), $"tia_src_{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(directory);
                // GenerateSource fails when the file already exists, so the file is not created beforehand.
                var file = new FileInfo(Path.Combine(directory, SafeFileName(targetName) + extension));
                plc.ExternalSourceGroup.GenerateSource(
                    new[] { generator },
                    file,
                    withDependencies ? GenerateOptions.WithDependencies : GenerateOptions.None);
                if (!File.Exists(file.FullName))
                    return $"TIA Portal did not write a source file for '{targetName}'.";

                // Encoding.UTF8 already drops a leading BOM; the TrimStart only guards a doubled one.
                source = File.ReadAllText(file.FullName, Encoding.UTF8).TrimStart('\uFEFF');
                return null;
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                return $"TIA Portal could not generate source for '{targetName}': {ex.Message} " +
                       "(one possible cause: TIA Portal is online with this PLC; source generation needs it offline).";
            }
            finally
            {
                try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
                catch { }
            }
        }
    }
}
