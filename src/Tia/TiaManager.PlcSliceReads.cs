using System;
using System.Collections.Generic;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Blocks.Interface;

namespace TiaOpennessMcp.Tia
{
    public partial class TiaManager
    {
        public PlcDbMemberAttributesResult GetDbMemberAttributes(
            string? plcName,
            string dbName,
            string memberName,
            string? groupPath = null)
        {
            if (string.IsNullOrWhiteSpace(dbName))
                throw new ArgumentException("A DB name is required.", nameof(dbName));
            if (string.IsNullOrWhiteSpace(memberName))
                throw new ArgumentException("A direct DB member name is required.", nameof(memberName));
            ValidateMetadataGroupPath(groupPath);

            var plc = FindMetadataPlc(plcName);
            var location = RequireUniqueMetadataTarget(
                FindMetadataBlocks(plc.BlockGroup, dbName, groupPath),
                $"DB '{dbName}'",
                groupPath);
            if (!(location.Block is GlobalDB db))
            {
                throw new ArgumentException(
                    $"Block '{dbName}' is not a global DB; only global DB member interfaces are supported.");
            }

            var interfaceCompositions = ((IEngineeringObject)db).GetCompositionInfos()
                .Where(info => info.Type == typeof(PlcBlockInterface))
                .ToList();
            if (interfaceCompositions.Count == 0)
            {
                throw new NotSupportedException(
                    $"The V21 block-member interface is unavailable for global DB '{db.Name}'.");
            }
            if (interfaceCompositions.Count > 1)
            {
                throw new InvalidOperationException(
                    $"The V21 block-member interface is ambiguous for global DB '{db.Name}'.");
            }

            var interfaceOrComposition = ((IEngineeringObject)db)
                .GetComposition(interfaceCompositions[0].Name);
            var blockInterfaces = new List<PlcBlockInterface>();
            if (interfaceOrComposition is PlcBlockInterface directInterface)
            {
                blockInterfaces.Add(directInterface);
            }
            else if (interfaceOrComposition is IEngineeringComposition composition)
            {
                foreach (var item in composition)
                {
                    if (item is PlcBlockInterface blockInterface)
                    {
                        blockInterfaces.Add(blockInterface);
                    }
                }
            }
            if (blockInterfaces.Count != 1)
            {
                throw new InvalidOperationException(
                    $"The V21 block-member interface could not be uniquely resolved for global DB '{db.Name}'.");
            }

            var memberMatches = blockInterfaces[0].Members.Cast<Member>()
                .Where(member => string.Equals(member.Name, memberName, StringComparison.Ordinal))
                .ToList();
            if (memberMatches.Count == 0)
            {
                throw new ArgumentException(
                    $"Direct member '{memberName}' was not found in global DB '{db.Name}'.");
            }
            if (memberMatches.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Direct member '{memberName}' is ambiguous in global DB '{db.Name}'.");
            }

            var selectedMember = memberMatches[0];
            var attributes = new List<PlcDbMemberAttribute>();
            foreach (EngineeringAttributeInfo info in selectedMember.GetAttributeInfos())
            {
                bool readable =
                    info.AccessMode == EngineeringAttributeAccessMode.Read ||
                    info.AccessMode == EngineeringAttributeAccessMode.ReadWrite;
                object? value = readable
                    ? ToDbMemberJsonValue(selectedMember.GetAttribute(info.Name))
                    : null;

                attributes.Add(new PlcDbMemberAttribute
                {
                    Name = info.Name,
                    Value = value,
                    ValueStatus = readable ? "available" : "not_readable",
                    AccessMode = info.AccessMode.ToString(),
                    CreateRelevance = info.CreateRelevance.ToString(),
                    SupportedTypes = info.SupportedTypes.Select(type => type.ToString()).ToList()
                });
            }

            return new PlcDbMemberAttributesResult
            {
                PlcName = plc.Name,
                DbName = db.Name,
                GroupPath = location.GroupPath,
                MemberName = selectedMember.Name,
                Attributes = attributes,
                MetadataNotReturned = new List<string>
                {
                    "stableId",
                    "revision",
                    "dataType",
                    "nestedMemberPaths"
                },
                CompletenessNote =
                    "Complete for the selected direct member: all attributes returned by its V21 GetAttributeInfos enumeration are included. Nested member paths and a dedicated member data-type field are not exposed by this operation."
            };
        }

        private static object? ToDbMemberJsonValue(object? value)
        {
            if (value == null ||
                value is string ||
                value is bool ||
                value is byte ||
                value is sbyte ||
                value is short ||
                value is ushort ||
                value is int ||
                value is uint ||
                value is long ||
                value is ulong ||
                value is float ||
                value is double ||
                value is decimal)
            {
                return value;
            }

            return value.ToString();
        }
    }

    public sealed class PlcDbMemberAttributesResult
    {
        public string PlcName { get; set; } = "";
        public string DbName { get; set; } = "";
        public string GroupPath { get; set; } = "";
        public string MemberName { get; set; } = "";
        public List<PlcDbMemberAttribute> Attributes { get; set; } = new List<PlcDbMemberAttribute>();
        public List<string> MetadataNotReturned { get; set; } = new List<string>();
        public string CompletenessNote { get; set; } = "";
    }

    public sealed class PlcDbMemberAttribute
    {
        public string Name { get; set; } = "";
        public object? Value { get; set; }
        public string ValueStatus { get; set; } = "";
        public string AccessMode { get; set; } = "";
        public string CreateRelevance { get; set; } = "";
        public List<string> SupportedTypes { get; set; } = new List<string>();
    }
}
