using System;
using System.Collections.Generic;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;

namespace TiaOpennessMcp.Tia
{
    public partial class TiaManager
    {
        private static readonly string[] BlockMetadataNotReturned =
        {
            "stableId",
            "interfaceSummary",
            "version",
            "compileState"
        };

        private static readonly string[] TypeMetadataNotReturned =
        {
            "stableId",
            "version",
            "fieldSummary",
            "dependencies"
        };

        public PlcBlockMetadata GetPlcBlockInfo(string? plcName, string blockName, string? groupPath = null)
        {
            if (string.IsNullOrWhiteSpace(blockName))
                throw new ArgumentException("A block name is required.", nameof(blockName));
            ValidateMetadataGroupPath(groupPath);

            var plc = FindMetadataPlc(plcName);
            var matches = FindMetadataBlocks(plc.BlockGroup, blockName, groupPath);
            var location = RequireUniqueMetadataTarget(matches, $"block '{blockName}'", groupPath);
            var block = location.Block;

            return new PlcBlockMetadata
            {
                PlcName = plc.Name,
                Name = block.Name,
                Number = block.Number,
                BlockType = GetBlockTypeName(block),
                ProgrammingLanguage = block.ProgrammingLanguage.ToString(),
                GroupPath = location.GroupPath,
                Attributes = ReadKnownAttributes(block, "IsConsistent", "IsKnowHowProtected"),
                MetadataNotReturned = BlockMetadataNotReturned.ToList()
            };
        }

        public PlcKnownAttributesResult GetPlcBlockAttributes(string? plcName, string blockName, string? groupPath = null)
        {
            if (string.IsNullOrWhiteSpace(blockName))
                throw new ArgumentException("A block name is required.", nameof(blockName));
            ValidateMetadataGroupPath(groupPath);

            var plc = FindMetadataPlc(plcName);
            var matches = FindMetadataBlocks(plc.BlockGroup, blockName, groupPath);
            var location = RequireUniqueMetadataTarget(matches, $"block '{blockName}'", groupPath);

            return new PlcKnownAttributesResult
            {
                PlcName = plc.Name,
                ObjectName = location.Block.Name,
                GroupPath = location.GroupPath,
                Attributes = ReadKnownAttributes(location.Block, "IsConsistent", "IsKnowHowProtected")
            };
        }

        public PlcTagTableMetadata GetPlcTagTableInfo(string? plcName, string tableName, string? groupPath = null)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                throw new ArgumentException("A tag-table name is required.", nameof(tableName));
            ValidateMetadataGroupPath(groupPath);

            var plc = FindMetadataPlc(plcName);
            var matches = FindMetadataTagTables(plc.TagTableGroup, tableName, groupPath);
            var location = RequireUniqueMetadataTarget(matches, $"tag table '{tableName}'", groupPath);

            return new PlcTagTableMetadata
            {
                PlcName = plc.Name,
                Name = location.Table.Name,
                GroupPath = location.GroupPath,
                ContainedTagCount = location.Table.Tags.Count,
                MetadataNotReturned = new List<string> { "stableId", "tableAttributes" }
            };
        }

        public PlcTypeMetadata GetPlcTypeInfo(string? plcName, string typeName, string? groupPath = null)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                throw new ArgumentException("A PLC type name is required.", nameof(typeName));
            ValidateMetadataGroupPath(groupPath);

            var plc = FindMetadataPlc(plcName);
            var matches = FindMetadataTypes(plc.TypeGroup, typeName, groupPath);
            var location = RequireUniqueMetadataTarget(matches, $"PLC type '{typeName}'", groupPath);

            return new PlcTypeMetadata
            {
                PlcName = plc.Name,
                Name = location.Type.Name,
                GroupPath = location.GroupPath,
                Attributes = ReadKnownAttributes(location.Type, "IsConsistent"),
                MetadataNotReturned = TypeMetadataNotReturned.ToList()
            };
        }

        public PlcKnownAttributesResult GetPlcTypeAttributes(string? plcName, string typeName, string? groupPath = null)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                throw new ArgumentException("A PLC type name is required.", nameof(typeName));
            ValidateMetadataGroupPath(groupPath);

            var plc = FindMetadataPlc(plcName);
            var matches = FindMetadataTypes(plc.TypeGroup, typeName, groupPath);
            var location = RequireUniqueMetadataTarget(matches, $"PLC type '{typeName}'", groupPath);

            return new PlcKnownAttributesResult
            {
                PlcName = plc.Name,
                ObjectName = location.Type.Name,
                GroupPath = location.GroupPath,
                Attributes = ReadKnownAttributes(location.Type, "IsConsistent")
            };
        }

        private PlcSoftware FindMetadataPlc(string? plcName)
        {
            EnsureProjectOpen();
            if (plcName != null && string.IsNullOrWhiteSpace(plcName))
            {
                throw new ArgumentException("A PLC name must be omitted or an exact non-empty name.", nameof(plcName));
            }

            var matches = new List<PlcSoftware>();
            WalkAllPlcs((device, item, plc) =>
            {
                if (plcName == null ||
                    string.Equals(plc.Name, plcName, StringComparison.Ordinal))
                {
                    matches.Add(plc);
                }
            });

            if (matches.Count == 0)
            {
                var target = string.IsNullOrWhiteSpace(plcName) ? "PLC software" : $"PLC software '{plcName}'";
                throw new ArgumentException($"{target} was not found in the current project.");
            }

            if (matches.Count > 1)
            {
                var target = string.IsNullOrWhiteSpace(plcName) ? "the current project" : $"PLC name '{plcName}'";
                throw new InvalidOperationException($"PLC selection is ambiguous for {target}; specify a PLC name that resolves uniquely.");
            }

            return matches[0];
        }

        private static List<MetadataBlockLocation> FindMetadataBlocks(
            PlcBlockGroup group,
            string blockName,
            string? requestedGroupPath,
            string currentPath = "")
        {
            var matches = new List<MetadataBlockLocation>();
            foreach (PlcBlock block in group.Blocks)
            {
                if (string.Equals(block.Name, blockName, StringComparison.Ordinal) &&
                    (requestedGroupPath == null ||
                     string.Equals(currentPath, requestedGroupPath, StringComparison.Ordinal)))
                {
                    matches.Add(new MetadataBlockLocation { Block = block, GroupPath = currentPath });
                }
            }

            foreach (PlcBlockUserGroup subGroup in group.Groups)
            {
                var nextPath = string.IsNullOrEmpty(currentPath) ? subGroup.Name : $"{currentPath}/{subGroup.Name}";
                matches.AddRange(FindMetadataBlocks(subGroup, blockName, requestedGroupPath, nextPath));
            }

            return matches;
        }

        private static List<MetadataTagTableLocation> FindMetadataTagTables(
            PlcTagTableGroup group,
            string tableName,
            string? requestedGroupPath,
            string currentPath = "")
        {
            var matches = new List<MetadataTagTableLocation>();
            foreach (PlcTagTable table in group.TagTables)
            {
                if (string.Equals(table.Name, tableName, StringComparison.Ordinal) &&
                    (requestedGroupPath == null ||
                     string.Equals(currentPath, requestedGroupPath, StringComparison.Ordinal)))
                {
                    matches.Add(new MetadataTagTableLocation { Table = table, GroupPath = currentPath });
                }
            }

            foreach (PlcTagTableUserGroup subGroup in group.Groups)
            {
                var nextPath = string.IsNullOrEmpty(currentPath) ? subGroup.Name : $"{currentPath}/{subGroup.Name}";
                matches.AddRange(FindMetadataTagTables(subGroup, tableName, requestedGroupPath, nextPath));
            }

            return matches;
        }

        private static List<MetadataTypeLocation> FindMetadataTypes(
            PlcTypeGroup group,
            string typeName,
            string? requestedGroupPath,
            string currentPath = "")
        {
            var matches = new List<MetadataTypeLocation>();
            foreach (PlcType type in group.Types)
            {
                if (string.Equals(type.Name, typeName, StringComparison.Ordinal) &&
                    (requestedGroupPath == null ||
                     string.Equals(currentPath, requestedGroupPath, StringComparison.Ordinal)))
                {
                    matches.Add(new MetadataTypeLocation { Type = type, GroupPath = currentPath });
                }
            }

            foreach (PlcTypeUserGroup subGroup in group.Groups)
            {
                var nextPath = string.IsNullOrEmpty(currentPath) ? subGroup.Name : $"{currentPath}/{subGroup.Name}";
                matches.AddRange(FindMetadataTypes(subGroup, typeName, requestedGroupPath, nextPath));
            }

            return matches;
        }

        private static TLocation RequireUniqueMetadataTarget<TLocation>(
            List<TLocation> matches,
            string target,
            string? groupPath)
        {
            if (matches.Count == 0)
                throw new ArgumentException($"{target} was not found in the selected PLC{(string.IsNullOrWhiteSpace(groupPath) ? "" : $" at group path '{groupPath}'")}.");

            if (matches.Count > 1)
                throw new InvalidOperationException($"{target} is ambiguous in the selected PLC; specify its exact group path.");

            return matches[0];
        }

        private static void ValidateMetadataGroupPath(string? groupPath)
        {
            if (groupPath != null && groupPath.Length > 0 && string.IsNullOrWhiteSpace(groupPath))
            {
                throw new ArgumentException("A group path must be empty for the root group or a non-empty exact path.", nameof(groupPath));
            }
        }

        private static List<PlcMetadataAttribute> ReadKnownAttributes(IEngineeringObject target, params string[] attributeNames)
        {
            var results = new List<PlcMetadataAttribute>(attributeNames.Length);
            foreach (var attributeName in attributeNames)
            {
                var value = target.GetAttribute(attributeName);
                results.Add(new PlcMetadataAttribute
                {
                    Name = attributeName,
                    Status = "available",
                    Value = value is bool boolean ? boolean : value?.ToString()
                });
            }

            return results;
        }

        private sealed class MetadataBlockLocation
        {
            public PlcBlock Block { get; set; } = null!;
            public string GroupPath { get; set; } = "";
        }

        private sealed class MetadataTagTableLocation
        {
            public PlcTagTable Table { get; set; } = null!;
            public string GroupPath { get; set; } = "";
        }

        private sealed class MetadataTypeLocation
        {
            public PlcType Type { get; set; } = null!;
            public string GroupPath { get; set; } = "";
        }
    }

    public class PlcMetadataAttribute
    {
        public string Name { get; set; } = "";
        public string Status { get; set; } = "";
        public object? Value { get; set; }
    }

    public class PlcKnownAttributesResult
    {
        public string PlcName { get; set; } = "";
        public string ObjectName { get; set; } = "";
        public string GroupPath { get; set; } = "";
        public List<PlcMetadataAttribute> Attributes { get; set; } = new List<PlcMetadataAttribute>();
    }

    public class PlcBlockMetadata
    {
        public string PlcName { get; set; } = "";
        public string Name { get; set; } = "";
        public int Number { get; set; }
        public string BlockType { get; set; } = "";
        public string ProgrammingLanguage { get; set; } = "";
        public string GroupPath { get; set; } = "";
        public List<PlcMetadataAttribute> Attributes { get; set; } = new List<PlcMetadataAttribute>();
        public List<string> MetadataNotReturned { get; set; } = new List<string>();
    }

    public class PlcTagTableMetadata
    {
        public string PlcName { get; set; } = "";
        public string Name { get; set; } = "";
        public string GroupPath { get; set; } = "";
        public int ContainedTagCount { get; set; }
        public List<string> MetadataNotReturned { get; set; } = new List<string>();
    }

    public class PlcTypeMetadata
    {
        public string PlcName { get; set; } = "";
        public string Name { get; set; } = "";
        public string GroupPath { get; set; } = "";
        public List<PlcMetadataAttribute> Attributes { get; set; } = new List<PlcMetadataAttribute>();
        public List<string> MetadataNotReturned { get; set; } = new List<string>();
    }
}
