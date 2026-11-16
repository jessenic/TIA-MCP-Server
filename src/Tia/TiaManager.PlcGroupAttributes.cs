using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Siemens.Engineering;
using Siemens.Engineering.SW.Blocks;

namespace TiaOpennessMcp.Tia
{
    public partial class TiaManager
    {
        public PlcMutationResult CreateBlockGroup(
            string? plcName,
            string groupName,
            string? parentGroupPath = null)
        {
            const string operation = "create_block_group";
            bool mutationStarted = false;
            try
            {
                ValidatePlcMutationName(groupName, nameof(groupName));
                string normalizedPath = NormalizePlcMutationGroupPath(parentGroupPath);
                var plc = ResolveMutationPlc(plcName);
                var parent = ResolveBlockGroupExact(plc.BlockGroup, normalizedPath);

                if (parent.Groups.Cast<PlcBlockUserGroup>().Any(group =>
                    string.Equals(group.Name, groupName, StringComparison.OrdinalIgnoreCase)))
                {
                    return PlcMutationResult.FailureOutcome(
                        operation,
                        "collision",
                        plc.Name,
                        groupName,
                        BuildResultPath(normalizedPath, groupName),
                        "A block group with this name already exists under the selected parent.");
                }

                mutationStarted = true;
                var created = parent.Groups.Create(groupName);
                var readBack = parent.Groups.Cast<PlcBlockUserGroup>()
                    .Where(group => string.Equals(group.Name, created.Name, StringComparison.Ordinal))
                    .ToList();
                if (readBack.Count != 1)
                {
                    return PlcMutationResult.VerificationFailureOutcome(
                        operation,
                        plc.Name,
                        created.Name,
                        BuildResultPath(normalizedPath, created.Name),
                        "Created block group could not be verified by exact-name read-back.");
                }

                return PlcMutationResult.CreateOutcome(
                    operation,
                    "created",
                    plc.Name,
                    readBack[0].Name,
                    BuildResultPath(normalizedPath, readBack[0].Name),
                    "Block group created.");
            }
            catch (PlcMutationRejectedException ex)
            {
                return PlcMutationResult.FailureOutcome(
                    operation, ex.Status, plcName, groupName, parentGroupPath, ex.Message);
            }
            catch (EngineeringException ex)
            {
                return PlcMutationResult.ApiFailureOutcome(
                    operation, plcName, groupName, BuildResultPath(parentGroupPath, groupName), ex, mutationStarted);
            }
            catch (COMException ex)
            {
                return PlcMutationResult.ApiFailureOutcome(
                    operation, plcName, groupName, BuildResultPath(parentGroupPath, groupName), ex, mutationStarted);
            }
        }

        public PlcTagTableAttributesResult GetTagTableAttributes(
            string? plcName,
            string tableName,
            string? groupPath = null)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                throw new ArgumentException("A tag-table name is required.", nameof(tableName));
            ValidateMetadataGroupPath(groupPath);

            var plc = FindMetadataPlc(plcName);
            var location = RequireUniqueMetadataTarget(
                FindMetadataTagTables(plc.TagTableGroup, tableName, groupPath),
                $"tag table '{tableName}'",
                groupPath);

            var attributes = new List<PlcTagTableAttribute>();
            foreach (EngineeringAttributeInfo info in location.Table.GetAttributeInfos())
            {
                bool readable =
                    info.AccessMode == EngineeringAttributeAccessMode.Read ||
                    info.AccessMode == EngineeringAttributeAccessMode.ReadWrite;
                object? value = readable ? location.Table.GetAttribute(info.Name) : null;
                attributes.Add(new PlcTagTableAttribute
                {
                    Name = info.Name,
                    Value = value is bool boolean ? boolean : value?.ToString(),
                    ValueStatus = readable ? "available" : "not_readable",
                    AccessMode = info.AccessMode.ToString(),
                    CreateRelevance = info.CreateRelevance.ToString(),
                    SupportedTypes = info.SupportedTypes.Select(type => type.ToString()).ToList()
                });
            }

            return new PlcTagTableAttributesResult
            {
                PlcName = plc.Name,
                Name = location.Table.Name,
                GroupPath = location.GroupPath,
                Attributes = attributes,
                MetadataNotReturned = new List<string> { "stableId", "revision" }
            };
        }

        private static PlcBlockGroup ResolveBlockGroupExact(PlcBlockGroup root, string groupPath)
        {
            if (groupPath.Length == 0)
                return root;

            PlcBlockGroup current = root;
            foreach (string segment in groupPath.Split('/'))
            {
                ValidatePlcMutationName(segment, nameof(groupPath));
                var matches = current.Groups.Cast<PlcBlockUserGroup>()
                    .Where(group => string.Equals(group.Name, segment, StringComparison.Ordinal))
                    .ToList();
                if (matches.Count == 0)
                    throw new PlcMutationRejectedException(
                        "not_found", $"Block group path '{groupPath}' was not found.");
                if (matches.Count > 1)
                    throw new PlcMutationRejectedException(
                        "ambiguous", $"Block group path '{groupPath}' is ambiguous.");
                current = matches[0];
            }

            return current;
        }
    }

    public class PlcTagTableAttributesResult
    {
        public string PlcName { get; set; } = "";
        public string Name { get; set; } = "";
        public string GroupPath { get; set; } = "";
        public List<PlcTagTableAttribute> Attributes { get; set; } = new List<PlcTagTableAttribute>();
        public List<string> MetadataNotReturned { get; set; } = new List<string>();
    }

    public class PlcTagTableAttribute
    {
        public string Name { get; set; } = "";
        public object? Value { get; set; }
        public string ValueStatus { get; set; } = "";
        public string AccessMode { get; set; } = "";
        public string CreateRelevance { get; set; } = "";
        public List<string> SupportedTypes { get; set; } = new List<string>();
    }
}
