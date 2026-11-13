using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TiaOpennessMcp.Tools
{
    internal static class HmiJsonTransferStore
    {
        private const long MaxFileBytes = 10 * 1024 * 1024;
        private const long MaxTransferBytes = 100 * 1024 * 1024;
        private const int MaxFiles = 5000;
        private const int DefaultChunkBytes = 256 * 1024;
        private const int MaxChunkBytes = 512 * 1024;
        private static readonly object Sync = new object();
        private static readonly string Root = Path.Combine(Path.GetTempPath(), "TiaOpennessMcp", "HmiJsonTransfers");
        private static readonly Dictionary<string, Transfer> Transfers = new Dictionary<string, Transfer>(StringComparer.Ordinal);

        public static string CreateExport(out string directory)
        {
            lock (Sync)
            {
                CleanupExpired();
                return CreateTransfer("export", out directory);
            }
        }

        public static string CreateImport()
        {
            lock (Sync)
            {
                CleanupExpired();
                return CreateTransfer("import", out _);
            }
        }

        public static void Delete(string transferId)
        {
            lock (Sync)
            {
                if (!Transfers.TryGetValue(transferId, out Transfer? transfer))
                    return;
                Directory.Delete(transfer.Directory, true);
                Transfers.Remove(transferId);
            }
        }

        public static string GetDirectory(string transferId, string expectedKind)
        {
            lock (Sync)
            {
                Transfer transfer = GetTransfer(transferId, expectedKind);
                Touch(transfer);
                return transfer.Directory;
            }
        }

        public static IReadOnlyList<string> ListFiles(string transferId)
        {
            lock (Sync)
            {
                Transfer transfer = GetTransfer(transferId, "export");
                Touch(transfer);
                string[] paths = Directory.GetFiles(transfer.Directory, "*", SearchOption.AllDirectories);
                if (paths.Length > MaxFiles)
                    throw new InvalidDataException($"The export contains more than {MaxFiles} files.");
                long totalBytes = paths.Sum(path => new FileInfo(path).Length);
                if (totalBytes > MaxTransferBytes)
                    throw new InvalidDataException("The export exceeds the 100 MiB transfer limit.");
                if (paths.Any(path => new FileInfo(path).Length > MaxFileBytes))
                    throw new InvalidDataException("An export file exceeds the 10 MiB transfer limit.");
                return paths
                    .Select(path => path.Substring(transfer.Directory.TrimEnd(Path.DirectorySeparatorChar).Length + 1).Replace('\\', '/'))
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .ToList();
            }
        }

        public static Dictionary<string, object> ReadChunk(string transferId, string relativePath, long offset, int requestedBytes)
        {
            lock (Sync)
            {
                Transfer transfer = GetTransfer(transferId, "export");
                string path = ResolveFilePath(transfer.Directory, relativePath);
                if (!File.Exists(path))
                    throw new FileNotFoundException($"Export file '{relativePath}' was not found.");
                var info = new FileInfo(path);
                if (info.Length > MaxFileBytes)
                    throw new InvalidDataException($"Export file exceeds the 10 MiB transfer limit: '{relativePath}'.");
                if (offset < 0 || offset > info.Length)
                    throw new ArgumentOutOfRangeException(nameof(offset), "offset must be between zero and the file size.");

                int chunkSize = requestedBytes == 0 ? DefaultChunkBytes : requestedBytes;
                if (chunkSize < 1 || chunkSize > MaxChunkBytes)
                    throw new ArgumentOutOfRangeException(nameof(requestedBytes), $"chunk_bytes must be between 1 and {MaxChunkBytes}.");
                int count = (int)Math.Min(chunkSize, info.Length - offset);
                byte[] chunk = new byte[count];
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    stream.Position = offset;
                    int read = 0;
                    while (read < count)
                    {
                        int current = stream.Read(chunk, read, count - read);
                        if (current == 0)
                            throw new EndOfStreamException($"Export file '{relativePath}' changed while being read.");
                        read += current;
                    }
                }

                Touch(transfer);
                return new Dictionary<string, object>
                {
                    ["relativePath"] = relativePath.Replace('\\', '/'),
                    ["offset"] = offset,
                    ["nextOffset"] = offset + count,
                    ["totalBytes"] = info.Length,
                    ["complete"] = offset + count == info.Length,
                    ["contentBase64"] = Convert.ToBase64String(chunk)
                };
            }
        }

        public static Dictionary<string, object> WriteChunk(
            string transferId,
            string relativePath,
            string contentBase64,
            long offset,
            long totalBytes)
        {
            lock (Sync)
            {
                Transfer transfer = GetTransfer(transferId, "import");
                string normalizedPath = NormalizeRelativePath(relativePath);
                string path = ResolveFilePath(transfer.Directory, normalizedPath);
                if (totalBytes < 0 || totalBytes > MaxFileBytes)
                    throw new ArgumentOutOfRangeException(nameof(totalBytes), "total_bytes must not exceed 10 MiB.");
                if (offset < 0 || offset > totalBytes)
                    throw new ArgumentOutOfRangeException(nameof(offset), "offset must be between zero and total_bytes.");
                if (transfer.Uploads.Count >= MaxFiles && !transfer.Uploads.ContainsKey(normalizedPath))
                    throw new InvalidDataException($"An import transfer cannot contain more than {MaxFiles} files.");

                byte[] chunk;
                try
                {
                    chunk = Convert.FromBase64String(contentBase64);
                }
                catch (FormatException ex)
                {
                    throw new InvalidDataException("content_base64 must contain valid Base64 data.", ex);
                }
                if (chunk.Length > MaxChunkBytes)
                    throw new InvalidDataException($"Each upload chunk must be no larger than {MaxChunkBytes} bytes.");
                if (offset + chunk.Length > totalBytes)
                    throw new InvalidDataException("The upload chunk exceeds total_bytes.");

                Upload upload;
                if (offset == 0)
                {
                    long oldLength = transfer.Uploads.TryGetValue(normalizedPath, out Upload? existing) ? existing.TotalBytes : 0;
                    long currentTotal = GetUploadedBytes(transfer);
                    if (currentTotal - oldLength + totalBytes > MaxTransferBytes)
                        throw new InvalidDataException("The import transfer cannot exceed 100 MiB.");
                    upload = new Upload(totalBytes);
                    transfer.Uploads[normalizedPath] = upload;
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    using (new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None)) { }
                }
                else
                {
                    if (!transfer.Uploads.TryGetValue(normalizedPath, out Upload? existingUpload))
                        throw new InvalidDataException("The first chunk for a file must have offset zero.");
                    upload = existingUpload;
                    if (upload.TotalBytes != totalBytes || upload.WrittenBytes != offset)
                        throw new InvalidDataException("Upload chunks must be sequential and use a consistent total_bytes value.");
                }

                using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None))
                    stream.Write(chunk, 0, chunk.Length);
                upload.WrittenBytes += chunk.Length;
                Touch(transfer);
                return new Dictionary<string, object>
                {
                    ["transferId"] = transfer.Id,
                    ["relativePath"] = normalizedPath,
                    ["receivedBytes"] = upload.WrittenBytes,
                    ["totalBytes"] = upload.TotalBytes,
                    ["complete"] = upload.WrittenBytes == upload.TotalBytes
                };
            }
        }

        public static void EnsureUploadsComplete(string transferId)
        {
            lock (Sync)
            {
                Transfer transfer = GetTransfer(transferId, "import");
                string? incomplete = transfer.Uploads
                    .Where(pair => pair.Value.WrittenBytes != pair.Value.TotalBytes)
                    .Select(pair => pair.Key)
                    .FirstOrDefault();
                if (incomplete != null)
                    throw new InvalidDataException($"Import file upload is incomplete: '{incomplete}'.");
            }
        }

        private static string CreateTransfer(string kind, out string directory)
        {
            Directory.CreateDirectory(Root);
            string id;
            do
            {
                id = Guid.NewGuid().ToString("N");
                directory = Path.Combine(Root, id);
            }
            while (Directory.Exists(directory));
            Directory.CreateDirectory(directory);
            Transfers.Add(id, new Transfer(id, kind, directory));
            return id;
        }

        private static Transfer GetTransfer(string transferId, string expectedKind)
        {
            CleanupExpired();
            if (string.IsNullOrWhiteSpace(transferId) || !Guid.TryParseExact(transferId, "N", out _))
                throw new ArgumentException("transfer_id must be a valid transfer ID.");
            if (!Transfers.TryGetValue(transferId, out Transfer? transfer) ||
                !string.Equals(transfer.Kind, expectedKind, StringComparison.Ordinal))
                throw new KeyNotFoundException($"No active {expectedKind} transfer exists with ID '{transferId}'. Transfers expire after 24 hours.");
            return transfer;
        }

        private static string ResolveFilePath(string directory, string relativePath)
        {
            string normalized = NormalizeRelativePath(relativePath);
            string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(directory, normalized.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The file path escapes the transfer directory.");
            return path;
        }

        private static string NormalizeRelativePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentException("relative_path is required.");
            string normalized = relativePath.Replace('\\', '/');
            if (normalized.StartsWith("/", StringComparison.Ordinal) ||
                normalized.Split('/').Any(part => string.IsNullOrWhiteSpace(part) || part == "." || part == ".." || part.Contains(":")))
                throw new InvalidDataException("relative_path must be a normalized relative file path.");
            return normalized;
        }

        private static long GetUploadedBytes(Transfer transfer)
        {
            return transfer.Uploads.Values.Sum(upload => upload.TotalBytes);
        }

        private static void Touch(Transfer transfer)
        {
            Directory.SetLastWriteTimeUtc(transfer.Directory, DateTime.UtcNow);
        }

        private static void CleanupExpired()
        {
            if (!Directory.Exists(Root))
                return;
            DateTime cutoff = DateTime.UtcNow.AddHours(-24);
            foreach (string directory in Directory.GetDirectories(Root))
            {
                string id = Path.GetFileName(directory);
                if (Directory.GetLastWriteTimeUtc(directory) >= cutoff)
                    continue;
                Directory.Delete(directory, true);
                Transfers.Remove(id);
            }
        }

        private sealed class Transfer
        {
            public string Id { get; }
            public string Kind { get; }
            public string Directory { get; }
            public Dictionary<string, Upload> Uploads { get; } = new Dictionary<string, Upload>(StringComparer.OrdinalIgnoreCase);

            public Transfer(string id, string kind, string directory)
            {
                Id = id;
                Kind = kind;
                Directory = directory;
            }
        }

        private sealed class Upload
        {
            public long TotalBytes { get; }
            public long WrittenBytes { get; set; }

            public Upload(long totalBytes)
            {
                TotalBytes = totalBytes;
            }
        }
    }
}
