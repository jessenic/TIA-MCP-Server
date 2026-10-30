using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Siemens.Engineering;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.Library;
using Siemens.Engineering.Library.Types;

namespace TiaOpennessMcp.Tia
{
    public partial class TiaManager
    {
        private List<HmiJsonLibraryType> ExportHmiLibrary(string root, string relativeFile)
        {
            List<LibraryType> types = EnumerateLibraryTypes(_project!.ProjectLibrary.TypeFolder).ToList();
            if (types.SelectMany(t => t.Versions).Any(v => v.State != LibraryTypeVersionState.Committed))
                throw new InvalidOperationException("Library export requires committed type versions. Release or discard in-work versions first.");

            string archivePath = ResolveManifestPathForWrite(root, relativeFile);
            if (File.Exists(archivePath))
                throw new IOException($"Library archive already exists: {archivePath}");
            string temporary = CreateHmiLibraryTemporaryDirectory();
            try
            {
                UserGlobalLibrary library = _tiaPortal!.GlobalLibraries.Create<UserGlobalLibrary>(
                    new DirectoryInfo(temporary), "HmiAssets");
                try
                {
                    // XML LibraryTypeVersion.Export contains metadata only for Unified types.
                    if (types.Count != 0)
                        _project.ProjectLibrary.UpdateLibrary(types.Cast<ILibraryTypeOrFolderSelection>(), library);
                    List<HmiJsonLibraryType> inventory = DescribeLibraryTypes(library.TypeFolder);
                    if (!SameLibraryInventory(DescribeLibraryTypes(_project.ProjectLibrary.TypeFolder), inventory))
                        throw new InvalidOperationException("Native library copy did not preserve all type/version identities.");
                    library.Save();
                    library.Archive(new DirectoryInfo(Path.GetDirectoryName(archivePath)!),
                        Path.GetFileName(archivePath), LibraryArchivationMode.Compressed);
                    return inventory;
                }
                finally
                {
                    library.Close();
                }
            }
            finally
            {
                Directory.Delete(temporary, true);
            }
        }

        private HmiLibraryPackage? OpenHmiLibrary(HmiJsonImportPlan plan)
        {
            if (string.IsNullOrWhiteSpace(plan.Manifest.LibraryFile))
                return null;
            string path = ResolveManifestPath(plan.Root, plan.Manifest.LibraryFile!);
            if (!string.Equals(Path.GetExtension(path), ".zal21", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Unified library packages must use the V21 .zal21 archive format.");
            string temporary = CreateHmiLibraryTemporaryDirectory();
            UserGlobalLibrary? library = null;
            try
            {
                library = (UserGlobalLibrary)_tiaPortal!.GlobalLibraries.Retrieve(
                    new FileInfo(path), new DirectoryInfo(temporary), OpenMode.ReadOnly);
                List<HmiJsonLibraryType> actual = DescribeLibraryTypes(library.TypeFolder);
                if (!SameLibraryInventory(plan.Manifest.LibraryTypes, actual))
                    throw new InvalidDataException("The native library inventory does not match manifest libraryTypes.");
                if (EnumerateLibraryTypes(library.TypeFolder).SelectMany(t => t.Versions)
                    .Any(v => v.State != LibraryTypeVersionState.Committed))
                    throw new InvalidDataException("Only committed library versions can be imported.");
                var package = new HmiLibraryPackage(library, temporary);
                ValidateLibraryConflicts(package);
                return package;
            }
            catch
            {
                library?.Close();
                Directory.Delete(temporary, true);
                throw;
            }
        }

        private void ValidateLibraryConflicts(HmiLibraryPackage package)
        {
            List<LibraryType> existing = EnumerateLibraryTypes(_project!.ProjectLibrary.TypeFolder).ToList();
            foreach (LibraryType source in EnumerateLibraryTypes(package.Library.TypeFolder))
            {
                LibraryType? destination = _project.ProjectLibrary.FindType(source.Guid);
                if (destination == null)
                {
                    if (existing.Any(t => string.Equals(t.Name, source.Name, StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidDataException($"Library type '{source.Name}' already exists with a different GUID. Automatic replacement is not allowed.");
                    package.MissingTypes.Add(source);
                    continue;
                }
                if (destination.Name != source.Name || GetLibraryTypeGroup(destination) != GetLibraryTypeGroup(source))
                    throw new InvalidDataException($"Library type GUID '{source.Guid}' has conflicting names or folders.");
                foreach (LibraryTypeVersion version in source.Versions)
                {
                    LibraryTypeVersion? match = destination.Versions.FirstOrDefault(v => v.VersionNumber == version.VersionNumber);
                    if (match != null && (match.Guid != version.Guid || match.State != LibraryTypeVersionState.Committed))
                        throw new InvalidDataException($"Library version '{source.Name} V{version.VersionNumber}' conflicts with the destination. Automatic replacement is not allowed.");
                }
                if (source.Versions.Any(v => destination.Versions.All(d => d.Guid != v.Guid)))
                    package.MissingTypes.Add(source);
            }
        }

        private void ApplyHmiLibrary(HmiLibraryPackage? package)
        {
            if (package == null)
                return;
            package.MissingTypes.Clear();
            ValidateLibraryConflicts(package);
            if (package.MissingTypes.Count == 0)
                return;
            package.Library.UpdateLibrary(package.MissingTypes.Cast<ILibraryTypeOrFolderSelection>(), _project!.ProjectLibrary);
            foreach (LibraryType type in EnumerateLibraryTypes(package.Library.TypeFolder))
                foreach (LibraryTypeVersion version in type.Versions)
                    if (_project.ProjectLibrary.FindVersion(version.Guid) == null)
                        throw new InvalidOperationException($"Native library import did not restore '{type.Name} V{version.VersionNumber}'.");
        }

        private static IEnumerable<LibraryType> EnumerateLibraryTypes(LibraryTypeFolder folder)
        {
            foreach (LibraryType type in folder.Types)
                yield return type;
            foreach (LibraryTypeFolder child in folder.Folders)
                foreach (LibraryType type in EnumerateLibraryTypes(child))
                    yield return type;
        }

        private static List<HmiJsonLibraryType> DescribeLibraryTypes(LibraryTypeFolder folder)
        {
            return EnumerateLibraryTypes(folder).OrderBy(t => t.Name, StringComparer.Ordinal).Select(t => new HmiJsonLibraryType
            {
                Name = t.Name,
                Group = GetLibraryTypeGroup(t),
                Guid = t.Guid,
                Versions = t.Versions.OrderBy(v => v.VersionNumber).Select(v => new HmiJsonLibraryVersion
                {
                    Version = v.VersionNumber.ToString(),
                    Guid = v.Guid
                }).ToList()
            }).ToList();
        }

        private static bool SameLibraryInventory(List<HmiJsonLibraryType> expected, List<HmiJsonLibraryType> actual)
        {
            IEnumerable<string> Keys(List<HmiJsonLibraryType> types) => types.SelectMany(t =>
                new[] { "type|" + t.Guid + "|" + t.Name + "|" + t.Group }.Concat(
                    t.Versions.Select(v => "version|" + t.Guid + "|" + v.Version + "|" + v.Guid)))
                .OrderBy(k => k, StringComparer.Ordinal);
            return Keys(expected).SequenceEqual(Keys(actual));
        }

        private static string? GetLibraryTypeGroup(LibraryType type)
        {
            var parts = new Stack<string>();
            for (var folder = type.Parent as LibraryTypeFolder;
                folder?.Parent is LibraryTypeFolder; folder = folder.Parent as LibraryTypeFolder)
                parts.Push(folder.Name);
            return parts.Count == 0 ? null : string.Join("/", parts);
        }

        private static string CreateHmiLibraryTemporaryDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "tia-mcp-hmi-library-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static List<HmiJsonInterfaceProperty>? CreateInterfaceDocument(HmiScreenItemBase item)
        {
            object? composition = FindOptionalProperty(item.GetType(), "Interface")?.GetValue(item, null);
            return composition == null ? null : ReadInterfaceProperties(composition);
        }

        private static List<HmiJsonInterfaceProperty> ReadInterfaceProperties(object composition)
        {
            return ((IEnumerable)composition).Cast<object>().Select(part => new HmiJsonInterfaceProperty
            {
                Name = (string)part.GetType().GetProperty("PropertyName")!.GetValue(part, null)!,
                Value = CreateMappingValue(part.GetType().GetProperty("Value")!.GetValue(part, null)),
                Properties = ReadNestedInterfaceProperties(part)
            }).ToList();
        }

        private static List<HmiJsonInterfaceProperty> ReadNestedInterfaceProperties(object part)
        {
            object? nested = part.GetType().GetProperty("Properties")?.GetValue(part, null);
            return nested == null ? new List<HmiJsonInterfaceProperty>() : ReadInterfaceProperties(nested);
        }

        private static void ValidateInterfaceDocument(List<HmiJsonInterfaceProperty>? properties, int depth = 0)
        {
            if (properties == null)
                return;
            if (depth > 16 || properties.Count > 1024)
                throw new InvalidDataException("Container interface exceeds the supported depth or property count.");
            EnsureUnique(properties.Select(p => p.Name), "container interface property");
            foreach (HmiJsonInterfaceProperty property in properties)
            {
                ReadMappingValue(property.Value);
                ValidateInterfaceDocument(property.Properties, depth + 1);
            }
        }

        private static void ApplyInterfaceDocument(HmiScreenItemBase item, List<HmiJsonInterfaceProperty>? properties)
        {
            if (properties == null)
                return;
            object? composition = FindOptionalProperty(item.GetType(), "Interface")?.GetValue(item, null);
            if (composition == null)
                throw new InvalidDataException($"Item '{item.Name}' does not have a container interface.");
            ApplyInterfaceProperties(composition, properties);
            List<HmiJsonInterfaceProperty> actual = ReadInterfaceProperties(composition);
            VerifyInterfaceProperties(actual, properties, item.Name);
        }

        private static void ApplyInterfaceProperties(object composition, List<HmiJsonInterfaceProperty> properties)
        {
            foreach (HmiJsonInterfaceProperty property in properties)
            {
                object? part = ((IEnumerable)composition).Cast<object>().SingleOrDefault(p =>
                    (string)p.GetType().GetProperty("PropertyName")!.GetValue(p, null)! == property.Name);
                if (part == null)
                    throw new InvalidDataException($"Container interface property '{property.Name}' is missing from the destination definition.");
                var valueProperty = part.GetType().GetProperty("Value")!;
                HmiJsonMappingValue? existing = CreateMappingValue(valueProperty.GetValue(part, null));
                if (!AreJsonValuesEqual(JsonSerializer.SerializeToElement(existing, HmiJsonOptions),
                    JsonSerializer.SerializeToElement(property.Value, HmiJsonOptions)))
                    valueProperty.SetValue(part, ReadMappingValue(property.Value), null);
                if (property.Properties.Count != 0)
                {
                    object? nested = part.GetType().GetProperty("Properties")?.GetValue(part, null);
                    if (nested == null)
                        throw new InvalidDataException($"Container interface property '{property.Name}' has no nested properties.");
                    ApplyInterfaceProperties(nested, property.Properties);
                }
            }
        }

        private static void VerifyInterfaceProperties(List<HmiJsonInterfaceProperty> actual,
            List<HmiJsonInterfaceProperty> expected, string itemName)
        {
            foreach (HmiJsonInterfaceProperty property in expected)
            {
                HmiJsonInterfaceProperty? match = actual.SingleOrDefault(p => p.Name == property.Name);
                if (match == null || !AreJsonValuesEqual(JsonSerializer.SerializeToElement(match.Value, HmiJsonOptions),
                    JsonSerializer.SerializeToElement(property.Value, HmiJsonOptions)))
                    throw new InvalidOperationException($"Container '{itemName}' failed interface read-back verification for '{property.Name}'.");
                VerifyInterfaceProperties(match.Properties, property.Properties, itemName);
            }
        }

        private sealed class HmiJsonInterfaceProperty
        {
            public string Name { get; set; } = "";
            public HmiJsonMappingValue? Value { get; set; }
            public List<HmiJsonInterfaceProperty> Properties { get; set; } = new List<HmiJsonInterfaceProperty>();
        }

        private sealed class HmiLibraryPackage : IDisposable
        {
            public UserGlobalLibrary Library { get; }
            public List<LibraryType> MissingTypes { get; } = new List<LibraryType>();
            private readonly string _temporary;

            public HmiLibraryPackage(UserGlobalLibrary library, string temporary)
            {
                Library = library;
                _temporary = temporary;
            }

            public void Dispose()
            {
                Library.Close();
                Directory.Delete(_temporary, true);
            }
        }

        private sealed class HmiJsonLibraryType
        {
            public string Name { get; set; } = "";
            public string? Group { get; set; }
            public Guid Guid { get; set; }
            public List<HmiJsonLibraryVersion> Versions { get; set; } = new List<HmiJsonLibraryVersion>();
        }

        private sealed class HmiJsonLibraryVersion
        {
            public string Version { get; set; } = "";
            public Guid Guid { get; set; }
        }
    }
}
