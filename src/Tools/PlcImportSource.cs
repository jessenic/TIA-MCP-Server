using System;
using System.IO;

namespace TiaOpennessMcp.Tools
{
    /// <summary>
    /// Resolves the source of a PLC import tool: exactly one of an inline string or a server-local file path.
    /// Contains no Siemens types so the checks run (and can be tested) before TIA Portal is touched.
    /// </summary>
    public static class PlcImportSource
    {
        public const long MaxFileBytes = 50L * 1024 * 1024;

        /// <summary>
        /// Validates the source. Whitespace-only values count as absent. For a file, the path must be fully
        /// qualified (drive root or UNC), exist, have one of the allowed extensions and be 1 byte..50 MiB.
        /// When readText is true the file is read (BOM detected, UTF-8 otherwise) and returned in Text.
        /// </summary>
        public static PlcImportSourceResult Resolve(
            string? inlineContent,
            string? filePath,
            string inlineParamName,
            string[] allowedExtensions,
            bool readText)
        {
            if (string.IsNullOrWhiteSpace(inlineContent)) inlineContent = null;
            if (string.IsNullOrWhiteSpace(filePath)) filePath = null;

            if ((inlineContent == null) == (filePath == null))
                throw new ArgumentException($"Provide exactly one of {inlineParamName} or file_path.");

            if (inlineContent != null)
                return new PlcImportSourceResult { Text = inlineContent };

            string path = filePath!;
            if (!IsFullyQualified(path))
                throw new ArgumentException($"file_path must be a fully qualified server-local path (e.g. 'C:\\work\\FB_Seq.xml'): '{path}'.");

            string fullPath = Path.GetFullPath(path);
            string extension = Path.GetExtension(fullPath);
            bool extensionAllowed = false;
            foreach (string allowed in allowedExtensions)
            {
                if (string.Equals(extension, allowed, StringComparison.OrdinalIgnoreCase))
                {
                    extensionAllowed = true;
                    break;
                }
            }
            if (!extensionAllowed)
                throw new ArgumentException($"file_path must have the extension {string.Join(" or ", allowedExtensions)}: '{fullPath}'.");

            var info = new FileInfo(fullPath);
            if (!info.Exists)
                throw new FileNotFoundException($"Import file not found: {fullPath}", fullPath);
            if (info.Length == 0)
                throw new InvalidDataException($"Import file is empty: {fullPath}");
            if (info.Length > MaxFileBytes)
                throw new InvalidDataException($"Import file is larger than {MaxFileBytes / (1024 * 1024)} MiB: {fullPath}");

            return new PlcImportSourceResult
            {
                FullPath = fullPath,
                Text = readText ? File.ReadAllText(fullPath) : null
            };
        }

        /// <summary>True for 'X:\...' or 'X:/...' and UNC '\\server\share\...' paths; false for relative,
        /// drive-relative ('C:foo') and root-relative ('\foo') paths, which depend on the server's current directory.</summary>
        public static bool IsFullyQualified(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
                return true;
            return path.Length >= 3 &&
                (path[0] == '\\' || path[0] == '/') &&
                (path[1] == '\\' || path[1] == '/') &&
                path[2] != '\\' && path[2] != '/';
        }
    }

    public class PlcImportSourceResult
    {
        /// <summary>Inline content, or the file text when readText was requested.</summary>
        public string? Text { get; set; }

        /// <summary>Normalized full path when the source is a file; null for inline content.</summary>
        public string? FullPath { get; set; }
    }
}
