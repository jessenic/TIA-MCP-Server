using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.Library.Types;
using Siemens.Engineering.SW;

namespace TiaOpennessMcp.Tia
{
    public partial class TiaManager
    {
        public LibraryTypeInstancesResult GetLibraryTypeInstances(
            string projectPath,
            string plcName,
            string libraryTypeId,
            string? versionId = null,
            string? sourceLibrary = null)
        {
            if (string.IsNullOrWhiteSpace(plcName))
            {
                throw new ArgumentException("An exact PLC software name is required.", nameof(plcName));
            }

            Guid typeGuid = ParseLibraryGuid(libraryTypeId, nameof(libraryTypeId));
            Guid? versionGuid = string.IsNullOrWhiteSpace(versionId)
                ? (Guid?)null
                : ParseLibraryGuid(versionId, nameof(versionId));

            if (versionId != null && string.IsNullOrWhiteSpace(versionId))
            {
                throw new ArgumentException(
                    "A supplied library version ID cannot be empty or whitespace.",
                    nameof(versionId));
            }

            if (sourceLibrary != null && string.IsNullOrWhiteSpace(sourceLibrary))
            {
                throw new ArgumentException(
                    "A supplied source library selector cannot be empty or whitespace.",
                    nameof(sourceLibrary));
            }

            Project project = ResolveOpenProject(projectPath);
            PlcSoftware plc = ResolveLibraryInstancePlc(project, plcName);

            var typeMatches = new List<LibraryType>();
            CollectLibraryTypesById(project.ProjectLibrary.TypeFolder, typeGuid, typeMatches);
            if (typeMatches.Count == 0)
            {
                throw new FileNotFoundException(
                    $"No project-library type with exact ID '{typeGuid:D}' was found in project '{project.Path?.FullName}'.");
            }

            if (typeMatches.Count > 1 && !versionGuid.HasValue && sourceLibrary == null)
            {
                throw new InvalidOperationException(
                    $"Library type ID '{typeGuid:D}' resolves to multiple project-library types; supply an exact version and/or source selector.");
            }

            var candidates = typeMatches
                .SelectMany(type => type.Versions.Select(version => new LibraryTypeVersionCandidate(type, version)))
                .Where(candidate => !versionGuid.HasValue || candidate.Version.Guid == versionGuid.Value)
                .Where(candidate => sourceLibrary == null ||
                    string.Equals(candidate.Version.OriginalLibrary ?? "", sourceLibrary, StringComparison.Ordinal))
                .ToList();

            if (candidates.Count == 0)
            {
                if (!versionGuid.HasValue && sourceLibrary == null)
                {
                    throw new FileNotFoundException(
                        $"Library type '{typeGuid:D}' has no versions available in the selected project library.");
                }

                string selector = versionGuid.HasValue
                    ? $"version ID '{versionGuid.Value:D}'"
                    : $"source library '{sourceLibrary}'";
                throw new FileNotFoundException(
                    $"No version matching exact {selector} was found for library type '{typeGuid:D}'.");
            }

            var selectedTypes = new List<LibraryType>();
            foreach (LibraryTypeVersionCandidate candidate in candidates)
            {
                if (!selectedTypes.Any(type => ReferenceEquals(type, candidate.Type)))
                {
                    selectedTypes.Add(candidate.Type);
                }
            }
            if (selectedTypes.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Library type ID '{typeGuid:D}' resolves to multiple project-library types after applying the supplied version and source selectors.");
            }

            LibraryType selectedType = selectedTypes[0];
            var selectedVersions = candidates
                .Where(candidate => ReferenceEquals(candidate.Type, selectedType))
                .Select(candidate => candidate.Version)
                .ToList();
            if (versionGuid.HasValue && selectedVersions.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Version ID '{versionGuid.Value:D}' is ambiguous for library type '{typeGuid:D}'.");
            }

            var result = new LibraryTypeInstancesResult
            {
                ProjectPath = project.Path?.FullName ?? "",
                PlcName = plc.Name,
                LibraryTypeId = selectedType.Guid.ToString("D"),
                LibraryTypeName = selectedType.Name,
                LibraryTypeNamespace = selectedType.Namespace ?? "",
                SourceSelector = sourceLibrary,
                VersionSelector = versionGuid?.ToString("D"),
                Scope = "Instances reported by LibraryTypeVersion.FindInstances for the exact selected PLC software scope."
            };

            var instanceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (LibraryTypeVersion version in selectedVersions)
            {
                result.VersionsQueried.Add(new LibraryTypeInstanceVersionMetadata
                {
                    VersionId = version.Guid.ToString("D"),
                    Version = version.VersionNumber.ToString(),
                    VersionState = version.State.ToString(),
                    SourceLibrary = version.OriginalLibrary ?? ""
                });

                foreach (LibraryTypeInstanceInfo instanceInfo in version.FindInstances(plc))
                {
                    LibraryTypeVersion actualVersion = instanceInfo.LibraryTypeVersion;
                    if (actualVersion == null || actualVersion.Guid != version.Guid)
                    {
                        throw new InvalidOperationException(
                            $"FindInstances returned an instance associated with an unexpected version while querying version '{version.Guid:D}'.");
                    }

                    IEngineeringObject instance = instanceInfo.LibraryTypeInstance;
                    string? instanceId = ReadLibraryInstanceGuid(instance);
                    if (instanceId != null && !instanceIds.Add(instanceId))
                    {
                        throw new InvalidOperationException(
                            $"FindInstances returned duplicate instance ID '{instanceId}' in the selected PLC scope.");
                    }

                    result.Instances.Add(new LibraryTypeInstanceMetadata
                    {
                        InstanceId = instanceId,
                        Name = ReadLibraryInstanceName(instance),
                        Location = BuildLibraryInstanceLocation(instanceInfo.Parent, plc.Name),
                        LibraryTypeId = selectedType.Guid.ToString("D"),
                        VersionId = actualVersion.Guid.ToString("D"),
                        Version = actualVersion.VersionNumber.ToString(),
                        VersionState = actualVersion.State.ToString(),
                        SourceLibrary = actualVersion.OriginalLibrary ?? ""
                    });
                }
            }

            result.Instances.Sort((left, right) =>
            {
                int locationOrder = string.Compare(left.Location, right.Location, StringComparison.OrdinalIgnoreCase);
                if (locationOrder != 0)
                {
                    return locationOrder;
                }

                int nameOrder = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
                return nameOrder != 0
                    ? nameOrder
                    : string.Compare(left.InstanceId, right.InstanceId, StringComparison.Ordinal);
            });

            result.EnumerationCompleted = true;
            result.CompletenessNote =
                "FindInstances completed for every selected type version in the exact PLC scope. The bundled API exposes version state and source library; instance ID, name, and parent names are included only when the instance's runtime attribute metadata exposes readable Guid and Name attributes. Instance consistency is not inferred.";
            return result;
        }

        private static Guid ParseLibraryGuid(string? value, string parameterName)
        {
            if (!Guid.TryParseExact(value, "D", out Guid parsed))
            {
                throw new ArgumentException(
                    "The selector must be a GUID in canonical D format (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx).",
                    parameterName);
            }

            return parsed;
        }

        private static PlcSoftware ResolveLibraryInstancePlc(Project project, string plcName)
        {
            var matches = new List<PlcSoftware>();

            void CollectDevice(Device device)
            {
                CollectItems(device.DeviceItems);
            }

            void CollectItems(DeviceItemComposition items)
            {
                foreach (DeviceItem item in items)
                {
                    SoftwareContainer? container = item.GetService<SoftwareContainer>();
                    if (container?.Software is PlcSoftware plc &&
                        string.Equals(plc.Name, plcName, StringComparison.Ordinal) &&
                        !matches.Any(match => ReferenceEquals(match, plc)))
                    {
                        matches.Add(plc);
                    }

                    if (item.DeviceItems.Count > 0)
                    {
                        CollectItems(item.DeviceItems);
                    }
                }
            }

            void CollectGroup(DeviceUserGroup group)
            {
                foreach (Device device in group.Devices)
                {
                    CollectDevice(device);
                }

                foreach (DeviceUserGroup child in group.Groups)
                {
                    CollectGroup(child);
                }
            }

            foreach (Device device in project.Devices)
            {
                CollectDevice(device);
            }

            foreach (DeviceUserGroup group in project.DeviceGroups)
            {
                CollectGroup(group);
            }

            if (matches.Count == 0)
            {
                throw new FileNotFoundException(
                    $"No PLC software with exact name '{plcName}' was found in project '{project.Path?.FullName}'.");
            }

            if (matches.Count > 1)
            {
                throw new InvalidOperationException(
                    $"PLC software name '{plcName}' is ambiguous in project '{project.Path?.FullName}'.");
            }

            return matches[0];
        }

        private static void CollectLibraryTypesById(
            LibraryTypeFolder folder,
            Guid typeId,
            ICollection<LibraryType> results)
        {
            foreach (LibraryType type in folder.Types)
            {
                if (type.Guid == typeId && !results.Any(match => ReferenceEquals(match, type)))
                {
                    results.Add(type);
                }
            }

            foreach (LibraryTypeUserFolder child in folder.Folders)
            {
                CollectLibraryTypesById(child, typeId, results);
            }
        }

        private static string? ReadLibraryInstanceGuid(IEngineeringObject instance)
        {
            object? value = ReadLibraryInstanceAttribute(instance, "Guid");
            if (value is Guid guid)
            {
                return guid.ToString("D");
            }

            return value is string text && Guid.TryParseExact(text, "D", out Guid parsed)
                ? parsed.ToString("D")
                : null;
        }

        private static string? ReadLibraryInstanceName(IEngineeringObject instance)
        {
            return ReadLibraryInstanceAttribute(instance, "Name")?.ToString();
        }

        private static string BuildLibraryInstanceLocation(IEngineeringObject? parent, string plcName)
        {
            var names = new List<string>();
            var visited = new HashSet<IEngineeringObject>();
            IEngineeringObject? current = parent;
            for (int depth = 0; current != null && depth < 64 && visited.Add(current); depth++)
            {
                string? name = ReadLibraryInstanceName(current);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name!);
                }

                object? parentValue = ReadLibraryInstanceAttribute(current, "Parent");
                current = parentValue as IEngineeringObject;
            }

            names.Reverse();
            if (names.Count == 0 ||
                !string.Equals(names[0], plcName, StringComparison.Ordinal))
            {
                names.Insert(0, plcName);
            }

            return string.Join("/", names);
        }

        private static object? ReadLibraryInstanceAttribute(IEngineeringObject target, string attributeName)
        {
            EngineeringAttributeInfo? attributeInfo = target.GetAttributeInfos()
                .FirstOrDefault(info => string.Equals(info.Name, attributeName, StringComparison.Ordinal));
            if (attributeInfo == null ||
                (attributeInfo.AccessMode != EngineeringAttributeAccessMode.Read &&
                 attributeInfo.AccessMode != EngineeringAttributeAccessMode.ReadWrite))
            {
                return null;
            }

            return target.GetAttribute(attributeName);
        }

        private sealed class LibraryTypeVersionCandidate
        {
            public LibraryTypeVersionCandidate(LibraryType type, LibraryTypeVersion version)
            {
                Type = type;
                Version = version;
            }

            public LibraryType Type { get; }
            public LibraryTypeVersion Version { get; }
        }
    }

    public sealed class LibraryTypeInstancesResult
    {
        public string ProjectPath { get; set; } = "";
        public string PlcName { get; set; } = "";
        public string LibraryTypeId { get; set; } = "";
        public string LibraryTypeName { get; set; } = "";
        public string LibraryTypeNamespace { get; set; } = "";
        public string? VersionSelector { get; set; }
        public string? SourceSelector { get; set; }
        public string Scope { get; set; } = "";
        public List<LibraryTypeInstanceVersionMetadata> VersionsQueried { get; set; } =
            new List<LibraryTypeInstanceVersionMetadata>();
        public List<LibraryTypeInstanceMetadata> Instances { get; set; } = new List<LibraryTypeInstanceMetadata>();
        public bool EnumerationCompleted { get; set; }
        public string CompletenessNote { get; set; } = "";
    }

    public sealed class LibraryTypeInstanceVersionMetadata
    {
        public string VersionId { get; set; } = "";
        public string Version { get; set; } = "";
        public string VersionState { get; set; } = "";
        public string SourceLibrary { get; set; } = "";
    }

    public sealed class LibraryTypeInstanceMetadata
    {
        public string? InstanceId { get; set; }
        public string? Name { get; set; }
        public string Location { get; set; } = "";
        public string LibraryTypeId { get; set; } = "";
        public string VersionId { get; set; } = "";
        public string Version { get; set; } = "";
        public string VersionState { get; set; } = "";
        public string SourceLibrary { get; set; } = "";
    }
}
