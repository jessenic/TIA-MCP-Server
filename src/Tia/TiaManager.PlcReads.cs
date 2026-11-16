using System;
using System.Collections.Generic;
using System.Linq;
using Siemens.Engineering.SW.Tags;

namespace TiaOpennessMcp.Tia
{
    public partial class TiaManager
    {
        public PlcTagInfoMetadata GetPlcTagInfo(
            string? plcName,
            string tableName,
            string tagName,
            string? groupPath = null)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                throw new ArgumentException("A tag-table name is required.", nameof(tableName));
            if (string.IsNullOrWhiteSpace(tagName))
                throw new ArgumentException("A PLC tag name is required.", nameof(tagName));
            ValidateMetadataGroupPath(groupPath);

            var plc = FindMetadataPlc(plcName);
            var tableLocation = RequireUniqueMetadataTarget(
                FindMetadataTagTables(plc.TagTableGroup, tableName, groupPath),
                $"tag table '{tableName}'",
                groupPath);
            var matches = tableLocation.Table.Tags.Cast<PlcTag>()
                .Where(tag => string.Equals(tag.Name, tagName, StringComparison.Ordinal))
                .ToList();

            if (matches.Count == 0)
                throw new ArgumentException(
                    $"PLC tag '{tagName}' was not found in tag table '{tableName}' at group path '{tableLocation.GroupPath}'.");
            if (matches.Count > 1)
                throw new InvalidOperationException(
                    $"PLC tag '{tagName}' is ambiguous in tag table '{tableName}'.");

            var tag = matches[0];
            return new PlcTagInfoMetadata
            {
                PlcName = plc.Name,
                Name = tag.Name,
                DataType = tag.DataTypeName,
                LogicalAddress = tag.LogicalAddress,
                Comment = GetSafeAttribute(tag, "Comment"),
                TableName = tableLocation.Table.Name,
                GroupPath = tableLocation.GroupPath,
                MetadataNotReturned = new List<string> { "stableId", "revision" }
            };
        }

        public PlcUserConstantMetadata ReadUserConstant(
            string? plcName,
            string constantName,
            string? tableName = null,
            string? groupPath = null)
        {
            if (string.IsNullOrWhiteSpace(constantName))
                throw new ArgumentException("A user-constant name is required.", nameof(constantName));
            if (tableName != null && string.IsNullOrWhiteSpace(tableName))
                throw new ArgumentException("A tag-table name must be omitted or an exact non-empty name.", nameof(tableName));
            ValidateMetadataGroupPath(groupPath);

            var plc = FindMetadataPlc(plcName);
            var tableLocations = ResolveUserConstantTables(plc.TagTableGroup, tableName, groupPath);
            var matches = new List<PlcUserConstantLocation>();
            foreach (var location in tableLocations)
            {
                foreach (PlcUserConstant constant in location.Table.UserConstants)
                {
                    if (string.Equals(constant.Name, constantName, StringComparison.Ordinal))
                    {
                        matches.Add(new PlcUserConstantLocation
                        {
                            Constant = constant,
                            TableName = location.Table.Name,
                            GroupPath = location.GroupPath
                        });
                    }
                }
            }

            if (matches.Count == 0)
                throw new ArgumentException(
                    $"User constant '{constantName}' was not found in the selected PLC and tag-table scope.");
            if (matches.Count > 1)
                throw new InvalidOperationException(
                    $"User constant '{constantName}' is ambiguous; specify its exact tag table and group path.");

            var match = matches[0];
            return new PlcUserConstantMetadata
            {
                PlcName = plc.Name,
                Name = match.Constant.Name,
                DataType = match.Constant.DataTypeName,
                Value = match.Constant.Value,
                TableName = match.TableName,
                GroupPath = match.GroupPath,
                MetadataNotReturned = new List<string> { "stableId", "revision", "comment" }
            };
        }

        private static List<PlcUserConstantTableLocation> ResolveUserConstantTables(
            PlcTagTableGroup root,
            string? tableName,
            string? groupPath)
        {
            if (tableName != null)
            {
                var location = RequireUniqueMetadataTarget(
                    FindMetadataTagTables(root, tableName, groupPath),
                    $"tag table '{tableName}'",
                    groupPath);
                return new List<PlcUserConstantTableLocation>
                {
                    new PlcUserConstantTableLocation
                    {
                        Table = location.Table,
                        GroupPath = location.GroupPath
                    }
                };
            }

            var locations = new List<PlcUserConstantTableLocation>();
            CollectUserConstantTables(root, groupPath, "", locations);
            return locations;
        }

        private static void CollectUserConstantTables(
            PlcTagTableGroup group,
            string? requestedGroupPath,
            string currentPath,
            List<PlcUserConstantTableLocation> locations)
        {
            bool includeCurrentGroup = requestedGroupPath == null ||
                string.Equals(currentPath, requestedGroupPath, StringComparison.Ordinal) ||
                (requestedGroupPath != null &&
                 requestedGroupPath.Length > 0 &&
                 currentPath.StartsWith(requestedGroupPath + "/", StringComparison.Ordinal));
            if (includeCurrentGroup)
            {
                foreach (PlcTagTable table in group.TagTables)
                {
                    locations.Add(new PlcUserConstantTableLocation
                    {
                        Table = table,
                        GroupPath = currentPath
                    });
                }
            }

            foreach (PlcTagTableUserGroup subGroup in group.Groups)
            {
                string nextPath = string.IsNullOrEmpty(currentPath)
                    ? subGroup.Name
                    : $"{currentPath}/{subGroup.Name}";
                bool isAncestorOfRequestedGroup = requestedGroupPath != null &&
                    requestedGroupPath.Length > 0 &&
                    requestedGroupPath.StartsWith(nextPath + "/", StringComparison.Ordinal);
                bool isDescendantOfRequestedGroup = requestedGroupPath != null &&
                    requestedGroupPath.Length > 0 &&
                    nextPath.StartsWith(requestedGroupPath + "/", StringComparison.Ordinal);
                if (requestedGroupPath == null ||
                    string.Equals(nextPath, requestedGroupPath, StringComparison.Ordinal) ||
                    isAncestorOfRequestedGroup ||
                    isDescendantOfRequestedGroup)
                {
                    CollectUserConstantTables(subGroup, requestedGroupPath, nextPath, locations);
                }
            }
        }

        private sealed class PlcUserConstantTableLocation
        {
            public PlcTagTable Table { get; set; } = null!;
            public string GroupPath { get; set; } = "";
        }

        private sealed class PlcUserConstantLocation
        {
            public PlcUserConstant Constant { get; set; } = null!;
            public string TableName { get; set; } = "";
            public string GroupPath { get; set; } = "";
        }
    }

    public class PlcTagInfoMetadata
    {
        public string PlcName { get; set; } = "";
        public string Name { get; set; } = "";
        public string DataType { get; set; } = "";
        public string LogicalAddress { get; set; } = "";
        public string Comment { get; set; } = "";
        public string TableName { get; set; } = "";
        public string GroupPath { get; set; } = "";
        public List<string> MetadataNotReturned { get; set; } = new List<string>();
    }

    public class PlcUserConstantMetadata
    {
        public string PlcName { get; set; } = "";
        public string Name { get; set; } = "";
        public string DataType { get; set; } = "";
        public string Value { get; set; } = "";
        public string TableName { get; set; } = "";
        public string GroupPath { get; set; } = "";
        public List<string> MetadataNotReturned { get; set; } = new List<string>();
    }
}
