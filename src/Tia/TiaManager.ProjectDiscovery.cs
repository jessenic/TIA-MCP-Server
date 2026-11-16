using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.Library.Types;

namespace TiaOpennessMcp.Tia
{
    public partial class TiaManager
    {
        private const int ProjectDiscoveryMaxPageSize = 200;

        public DiscoveryPage<GlobalLibraryMetadata> ListGlobalLibraries(
            string? nameFilter = null,
            int offset = 0,
            int pageSize = 50)
        {
            ValidatePage(offset, pageSize);
            var portal = RequireConnectedPortal();

            var libraries = portal.GlobalLibraries.GetGlobalLibraryInfos()
                .Where(info => MatchesFilter(info.Name, nameFilter))
                .OrderBy(info => info.Name ?? "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(info => info.Path?.FullName ?? "", StringComparer.OrdinalIgnoreCase)
                .Select(info => new GlobalLibraryMetadata
                {
                    Name = info.Name ?? "",
                    Path = info.Path?.FullName ?? "",
                    LibraryType = info.LibraryType.ToString(),
                    IsOpen = info.IsOpen,
                    IsReadOnly = info.IsReadOnly
                })
                .ToList();

            return CreatePage(libraries, offset, pageSize,
                "Records reported by the connected TIA Portal instance; the API exposes no separate discovery-freshness indicator.");
        }

        public DiscoveryPage<ProjectLibraryTypeMetadata> SearchProjectLibraryTypes(
            string projectPath,
            string? query = null,
            int offset = 0,
            int pageSize = 50)
        {
            ValidatePage(offset, pageSize);
            var project = ResolveOpenProject(projectPath);
            var library = project.ProjectLibrary;
            var matches = new List<ProjectLibraryTypeMetadata>();
            CollectLibraryTypes(library.TypeFolder, "", matches);

            var filtered = matches
                .Where(type => MatchesFilter(type.Name, query) || MatchesFilter(type.Namespace, query))
                .OrderBy(type => type.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(type => type.Id, StringComparer.Ordinal)
                .ToList();

            return CreatePage(filtered, offset, pageSize,
                "All visible type folders in the selected project's ProjectLibrary were traversed. The API exposes no separate freshness/index indicator.");
        }

        public DiscoveryPage<ProjectLibraryTypeMetadata> ListProjectFaceplates(
            string projectPath,
            string? nameFilter = null,
            int offset = 0,
            int pageSize = 50)
        {
            ValidatePage(offset, pageSize);
            var project = ResolveOpenProject(projectPath);
            var faceplates = new List<ProjectLibraryTypeMetadata>();
            CollectLibraryTypes(project.ProjectLibrary.TypeFolder, "", faceplates);

            var filtered = faceplates
                .Where(type => type.TypeKind == "Siemens.Engineering.Hmi.Faceplate.FaceplateLibraryType")
                .Where(type => MatchesFilter(type.Name, nameFilter))
                .OrderBy(type => type.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(type => type.Id, StringComparer.Ordinal)
                .ToList();

            return CreatePage(filtered, offset, pageSize,
                "Lists faceplate library types visible in the selected project's ProjectLibrary. V21's faceplate library type exposes no interface metadata through this API surface.");
        }

        public DiscoveryPage<ProjectTextCategoryMetadata> ListProjectTextCategories(
            string projectPath,
            string? query = null,
            int offset = 0,
            int pageSize = 50)
        {
            ValidatePage(offset, pageSize);
            var project = ResolveOpenProject(projectPath);
            var categories = project.TextCategories
                .Where(category => MatchesFilter(category.Name, query) || MatchesFilter(category.Identifier, query))
                .OrderBy(category => category.Name ?? "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(category => category.Identifier ?? "", StringComparer.Ordinal)
                .Select(category => new ProjectTextCategoryMetadata
                {
                    Identifier = category.Identifier ?? "",
                    Name = category.Name ?? ""
                })
                .ToList();

            return CreatePage(categories, offset, pageSize,
                "The V21 API exposes category identifiers and names here, but no text-entry counts or per-language category counts.");
        }

        public ProjectTextDownloadResult DownloadProjectTexts(
            string projectPath,
            string destinationFilePath,
            string primaryCultureName,
            string referenceCultureName)
        {
            var project = ResolveOpenProject(projectPath);
            if (string.IsNullOrWhiteSpace(destinationFilePath))
            {
                throw new ArgumentException("A destination file path is required.", nameof(destinationFilePath));
            }

            if (string.IsNullOrWhiteSpace(primaryCultureName))
            {
                throw new ArgumentException("A primary culture name is required.", nameof(primaryCultureName));
            }

            if (string.IsNullOrWhiteSpace(referenceCultureName))
            {
                throw new ArgumentException("A reference culture name is required.", nameof(referenceCultureName));
            }

            var primaryCulture = CultureInfo.GetCultureInfo(primaryCultureName);
            var referenceCulture = CultureInfo.GetCultureInfo(referenceCultureName);
            var destinationPath = Path.GetFullPath(destinationFilePath);
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (destinationDirectory == null || !Directory.Exists(destinationDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"The destination directory does not exist: '{destinationDirectory ?? destinationPath}'.");
            }

            if (Directory.Exists(destinationPath))
            {
                throw new ArgumentException("The destination path is a directory.", nameof(destinationFilePath));
            }

            if (File.Exists(destinationPath))
            {
                throw new IOException($"The destination file already exists: '{destinationPath}'.");
            }

            string temporaryPath;
            string destinationName = Path.GetFileNameWithoutExtension(destinationPath);
            string extension = Path.GetExtension(destinationPath);
            do
            {
                temporaryPath = Path.Combine(
                    destinationDirectory,
                    $".{destinationName}.{Guid.NewGuid():N}{extension}");
            }
            while (File.Exists(temporaryPath) || Directory.Exists(temporaryPath));

            try
            {
                project.ExportProjectTexts(new FileInfo(temporaryPath), primaryCulture, referenceCulture);
                var temporaryFile = new FileInfo(temporaryPath);
                if (!temporaryFile.Exists)
                {
                    throw new IOException("TIA Portal completed ExportProjectTexts without creating the export file.");
                }

                File.Move(temporaryPath, destinationPath);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            var exportedFile = new FileInfo(destinationPath);

            return new ProjectTextDownloadResult
            {
                ProjectPath = project.Path?.FullName ?? "",
                FilePath = exportedFile.FullName,
                PrimaryCulture = primaryCulture.Name,
                ReferenceCulture = referenceCulture.Name,
                FileSizeBytes = exportedFile.Length,
                ExportCompleted = true,
                CompletenessNote = "ExportProjectTexts returned successfully and created the file. The API returns no exported-entry count or completeness indicator."
            };
        }

        private TiaPortal RequireConnectedPortal()
        {
            if (_tiaPortal == null)
            {
                throw new InvalidOperationException(
                    "No TIA Portal instance is connected. Connect to the intended instance before discovery; this operation will not auto-select an instance.");
            }

            return _tiaPortal;
        }

        private Project ResolveOpenProject(string projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                throw new ArgumentException("An exact open project path is required.", nameof(projectPath));
            }

            var requestedPath = Path.GetFullPath(projectPath);
            var matches = RequireConnectedPortal().Projects
                .Where(project => project.Path != null &&
                    string.Equals(Path.GetFullPath(project.Path.FullName), requestedPath, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 0)
            {
                throw new FileNotFoundException(
                    $"No open project in the connected TIA Portal instance matches the exact path '{requestedPath}'.",
                    requestedPath);
            }

            if (matches.Count > 1)
            {
                throw new InvalidOperationException(
                    $"More than one open project matches the exact path '{requestedPath}'. Project resolution is ambiguous.");
            }

            return matches[0];
        }

        private static void CollectLibraryTypes(
            LibraryTypeFolder folder,
            string parentFolderPath,
            ICollection<ProjectLibraryTypeMetadata> results)
        {
            var currentFolderPath = string.IsNullOrEmpty(parentFolderPath)
                ? folder.Name
                : parentFolderPath + "/" + folder.Name;

            foreach (LibraryType type in folder.Types)
            {
                results.Add(new ProjectLibraryTypeMetadata
                {
                    Id = type.Guid.ToString("D"),
                    Name = type.Name,
                    Namespace = type.Namespace ?? "",
                    TypeKind = type.GetType().FullName ?? type.GetType().Name,
                    FolderPath = currentFolderPath,
                    Status = type.Status.ToString(),
                    Versions = type.Versions.Select(version => new LibraryTypeVersionMetadata
                    {
                        Id = version.Guid.ToString("D"),
                        Version = version.VersionNumber.ToString(),
                        State = version.State.ToString(),
                        IsDefault = version.IsDefault,
                        SourceLibrary = version.OriginalLibrary ?? ""
                    }).ToList()
                });
            }

            foreach (LibraryTypeUserFolder childFolder in folder.Folders)
            {
                CollectLibraryTypes(childFolder, currentFolderPath, results);
            }
        }

        private static void ValidatePage(int offset, int pageSize)
        {
            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), "Offset must be zero or greater.");
            }

            if (pageSize < 1 || pageSize > ProjectDiscoveryMaxPageSize)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pageSize),
                    $"Page size must be between 1 and {ProjectDiscoveryMaxPageSize}.");
            }
        }

        private static bool MatchesFilter(string? value, string? filter)
        {
            var normalizedFilter = filter?.Trim();
            return string.IsNullOrEmpty(normalizedFilter) ||
                (value ?? "").IndexOf(normalizedFilter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static DiscoveryPage<T> CreatePage<T>(
            IList<T> items,
            int offset,
            int pageSize,
            string completenessNote)
        {
            var pageItems = items.Skip(offset).Take(pageSize).ToList();
            return new DiscoveryPage<T>
            {
                Items = pageItems,
                Offset = offset,
                PageSize = pageSize,
                TotalCount = items.Count,
                HasMore = offset + pageItems.Count < items.Count,
                EnumerationCompleted = true,
                CompletenessNote = completenessNote
            };
        }
    }

    public sealed class DiscoveryPage<T>
    {
        public List<T> Items { get; set; } = new List<T>();
        public int Offset { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public bool HasMore { get; set; }
        public bool EnumerationCompleted { get; set; }
        public string CompletenessNote { get; set; } = "";
    }

    public sealed class GlobalLibraryMetadata
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string LibraryType { get; set; } = "";
        public bool IsOpen { get; set; }
        public bool IsReadOnly { get; set; }
    }

    public sealed class ProjectLibraryTypeMetadata
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Namespace { get; set; } = "";
        public string TypeKind { get; set; } = "";
        public string FolderPath { get; set; } = "";
        public string Status { get; set; } = "";
        public List<LibraryTypeVersionMetadata> Versions { get; set; } = new List<LibraryTypeVersionMetadata>();
    }

    public sealed class LibraryTypeVersionMetadata
    {
        public string Id { get; set; } = "";
        public string Version { get; set; } = "";
        public string State { get; set; } = "";
        public bool IsDefault { get; set; }
        public string SourceLibrary { get; set; } = "";
    }

    public sealed class ProjectTextCategoryMetadata
    {
        public string Identifier { get; set; } = "";
        public string Name { get; set; } = "";
    }

    public sealed class ProjectTextDownloadResult
    {
        public string ProjectPath { get; set; } = "";
        public string FilePath { get; set; } = "";
        public string PrimaryCulture { get; set; } = "";
        public string ReferenceCulture { get; set; } = "";
        public long FileSizeBytes { get; set; }
        public bool ExportCompleted { get; set; }
        public string CompletenessNote { get; set; } = "";
    }
}
