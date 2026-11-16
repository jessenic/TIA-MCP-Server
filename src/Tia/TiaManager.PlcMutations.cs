using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Tags;

namespace TiaOpennessMcp.Tia
{
    public partial class TiaManager
    {
        private static readonly Regex PlcMutationNamePattern = new Regex(
            @"^[^\x00-\x1f\x7f/\\]+$",
            RegexOptions.CultureInvariant);

        public PlcMutationResult CreatePlcTagTable(string? plcName, string tableName, string? groupPath = null)
        {
            const string operation = "create_plc_tag_table";
            bool mutationStarted = false;
            try
            {
                ValidatePlcMutationName(tableName, nameof(tableName));
                var plc = ResolveMutationPlc(plcName);
                var parent = ResolveTagTableGroup(plc.TagTableGroup, groupPath);
                if (parent.TagTables.Cast<PlcTagTable>().Any(table =>
                        string.Equals(table.Name, tableName, StringComparison.OrdinalIgnoreCase)) ||
                    parent.Groups.Cast<PlcTagTableUserGroup>().Any(group =>
                        string.Equals(group.Name, tableName, StringComparison.OrdinalIgnoreCase)))
                {
                    return PlcMutationResult.FailureOutcome(operation, "collision", plc.Name, tableName,
                        NormalizePlcMutationGroupPath(groupPath), "An object with this name already exists in the selected tag-table group.");
                }

                mutationStarted = true;
                var created = parent.TagTables.Create(tableName);
                var readBack = parent.TagTables.Cast<PlcTagTable>()
                    .Where(table => string.Equals(table.Name, created.Name, StringComparison.Ordinal))
                    .ToList();
                if (readBack.Count != 1)
                    return PlcMutationResult.VerificationFailureOutcome(operation, plc.Name, created.Name,
                        BuildResultPath(groupPath, created.Name), "Created tag table could not be verified by exact-name read-back.");

                return PlcMutationResult.CreateOutcome(operation, "created", plc.Name, readBack[0].Name,
                    AppendPath(groupPath, created.Name), "Tag table created.");
            }
            catch (PlcMutationRejectedException ex)
            {
                return PlcMutationResult.FailureOutcome(operation, ex.Status, plcName, tableName, groupPath, ex.Message);
            }
            catch (EngineeringException ex)
            {
                return PlcMutationResult.ApiFailureOutcome(operation, plcName, tableName, BuildResultPath(groupPath, tableName), ex, mutationStarted);
            }
            catch (COMException ex)
            {
                return PlcMutationResult.ApiFailureOutcome(operation, plcName, tableName, BuildResultPath(groupPath, tableName), ex, mutationStarted);
            }
        }

        public PlcMutationResult CreateTagTableGroup(string? plcName, string groupName, string? parentGroupPath = null)
        {
            const string operation = "create_tagtable_group";
            bool mutationStarted = false;
            try
            {
                ValidatePlcMutationName(groupName, nameof(groupName));
                var plc = ResolveMutationPlc(plcName);
                var parent = ResolveTagTableGroup(plc.TagTableGroup, parentGroupPath);
                bool nameCollision =
                    parent.Groups.Cast<PlcTagTableUserGroup>().Any(group =>
                        string.Equals(group.Name, groupName, StringComparison.OrdinalIgnoreCase)) ||
                    parent.TagTables.Cast<PlcTagTable>().Any(table =>
                        string.Equals(table.Name, groupName, StringComparison.OrdinalIgnoreCase));
                if (nameCollision)
                {
                    return PlcMutationResult.FailureOutcome(operation, "collision", plc.Name, groupName,
                        NormalizePlcMutationGroupPath(parentGroupPath), "An object with this name already exists in the selected tag-table group.");
                }

                mutationStarted = true;
                var created = parent.Groups.Create(groupName);
                var readBack = parent.Groups.Cast<PlcTagTableUserGroup>()
                    .Where(group => string.Equals(group.Name, created.Name, StringComparison.Ordinal))
                    .ToList();
                if (readBack.Count != 1)
                    return PlcMutationResult.VerificationFailureOutcome(operation, plc.Name, created.Name,
                        BuildResultPath(parentGroupPath, created.Name), "Created tag-table group could not be verified by exact-name read-back.");

                return PlcMutationResult.CreateOutcome(operation, "created", plc.Name, readBack[0].Name,
                    AppendPath(parentGroupPath, created.Name), "Tag-table group created.");
            }
            catch (PlcMutationRejectedException ex)
            {
                return PlcMutationResult.FailureOutcome(operation, ex.Status, plcName, groupName, parentGroupPath, ex.Message);
            }
            catch (EngineeringException ex)
            {
                return PlcMutationResult.ApiFailureOutcome(operation, plcName, groupName,
                    BuildResultPath(parentGroupPath, groupName), ex, mutationStarted);
            }
            catch (COMException ex)
            {
                return PlcMutationResult.ApiFailureOutcome(operation, plcName, groupName,
                    BuildResultPath(parentGroupPath, groupName), ex, mutationStarted);
            }
        }

        public PlcMutationResult CreateUserConstant(
            string? plcName,
            string tableName,
            string constantName,
            string dataType,
            string value,
            string? groupPath = null)
        {
            const string operation = "create_user_constant";
            bool mutationStarted = false;
            try
            {
                ValidatePlcMutationName(constantName, nameof(constantName));
                if (!TryNormalizeConstant(dataType, value, out string normalizedType, out string normalizedValue, out string error))
                {
                    return PlcMutationResult.FailureOutcome(operation, "invalid_input", plcName, constantName,
                        BuildResultPath(groupPath, tableName), error);
                }

                var plc = ResolveMutationPlc(plcName);
                var table = ResolveTagTable(plc.TagTableGroup, tableName, groupPath);
                if (table.UserConstants.Cast<PlcUserConstant>().Any(constant =>
                    string.Equals(constant.Name, constantName, StringComparison.OrdinalIgnoreCase)))
                {
                    return PlcMutationResult.FailureOutcome(operation, "collision", plc.Name, constantName,
                        AppendPath(groupPath, table.Name), "A user constant with this name already exists in the selected tag table.");
                }

                mutationStarted = true;
                var created = table.UserConstants.Create(constantName, normalizedType, normalizedValue);
                var readBack = table.UserConstants.Cast<PlcUserConstant>()
                    .Where(constant => string.Equals(constant.Name, constantName, StringComparison.Ordinal))
                    .ToList();
                if (readBack.Count != 1 ||
                    !string.Equals(readBack[0].DataTypeName, created.DataTypeName, StringComparison.Ordinal) ||
                    !string.Equals(readBack[0].Value, created.Value, StringComparison.Ordinal))
                    return PlcMutationResult.VerificationFailureOutcome(operation, plc.Name, created.Name,
                        AppendPath(groupPath, table.Name), "Created user constant could not be verified by exact-name read-back.");

                return PlcMutationResult.CreateOutcome(operation, "created", plc.Name, readBack[0].Name,
                    AppendPath(groupPath, table.Name), "User constant created.", readBack[0].DataTypeName, readBack[0].Value);
            }
            catch (PlcMutationRejectedException ex)
            {
                return PlcMutationResult.FailureOutcome(operation, ex.Status, plcName, constantName,
                    BuildResultPath(groupPath, tableName), ex.Message);
            }
            catch (EngineeringException ex)
            {
                return PlcMutationResult.ApiFailureOutcome(operation, plcName, constantName,
                    BuildResultPath(groupPath, tableName), ex, mutationStarted);
            }
            catch (COMException ex)
            {
                return PlcMutationResult.ApiFailureOutcome(operation, plcName, constantName,
                    BuildResultPath(groupPath, tableName), ex, mutationStarted);
            }
        }

        public PlcMutationResult UpdateUserConstant(
            string? plcName,
            string tableName,
            string constantName,
            string value,
            string? groupPath = null)
        {
            const string operation = "update_user_constant";
            bool mutationStarted = false;
            try
            {
                ValidatePlcMutationName(constantName, nameof(constantName));
                var plc = ResolveMutationPlc(plcName);
                var table = ResolveTagTable(plc.TagTableGroup, tableName, groupPath);
                var matches = table.UserConstants.Cast<PlcUserConstant>()
                    .Where(constant => string.Equals(constant.Name, constantName, StringComparison.Ordinal))
                    .ToList();
                if (matches.Count == 0)
                {
                    return PlcMutationResult.FailureOutcome(operation, "not_found", plc.Name, constantName,
                        AppendPath(groupPath, table.Name), "The user constant was not found in the selected tag table.");
                }
                if (matches.Count > 1)
                {
                    return PlcMutationResult.FailureOutcome(operation, "ambiguous", plc.Name, constantName,
                        AppendPath(groupPath, table.Name), "More than one user constant has this exact name in the selected tag table.");
                }

                var target = matches[0];
                if (!TryNormalizeConstant(target.DataTypeName, value, out _, out string normalizedValue, out string error))
                {
                    return PlcMutationResult.FailureOutcome(operation, "invalid_input", plc.Name, constantName,
                        AppendPath(groupPath, table.Name), error);
                }

                mutationStarted = true;
                target.Value = normalizedValue;
                var readBack = table.UserConstants.Cast<PlcUserConstant>()
                    .Where(constant => string.Equals(constant.Name, constantName, StringComparison.Ordinal))
                    .ToList();
                if (readBack.Count != 1 ||
                    !string.Equals(readBack[0].Value, normalizedValue, StringComparison.Ordinal))
                    return PlcMutationResult.VerificationFailureOutcome(operation, plc.Name, target.Name,
                        AppendPath(groupPath, table.Name), "Updated user constant could not be verified by exact-name read-back.");

                return PlcMutationResult.CreateOutcome(operation, "updated", plc.Name, readBack[0].Name,
                    AppendPath(groupPath, table.Name), "User constant value updated.", readBack[0].DataTypeName, readBack[0].Value);
            }
            catch (PlcMutationRejectedException ex)
            {
                return PlcMutationResult.FailureOutcome(operation, ex.Status, plcName, constantName,
                    BuildResultPath(groupPath, tableName), ex.Message);
            }
            catch (EngineeringException ex)
            {
                return PlcMutationResult.ApiFailureOutcome(operation, plcName, constantName,
                    BuildResultPath(groupPath, tableName), ex, mutationStarted);
            }
            catch (COMException ex)
            {
                return PlcMutationResult.ApiFailureOutcome(operation, plcName, constantName,
                    BuildResultPath(groupPath, tableName), ex, mutationStarted);
            }
        }

        private PlcSoftware ResolveMutationPlc(string? plcName)
        {
            if (plcName != null)
                ValidatePlcMutationName(plcName, nameof(plcName));
            EnsureProjectOpen();
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
                throw new PlcMutationRejectedException("not_found",
                    plcName == null
                        ? "No PLC software was found in the current project."
                        : $"PLC software '{plcName}' was not found in the current project.");
            }
            if (matches.Count > 1)
            {
                throw new PlcMutationRejectedException("ambiguous",
                    plcName == null
                        ? "PLC selection is ambiguous; specify an exact PLC software name."
                        : $"PLC software name '{plcName}' is ambiguous in the current project.");
            }

            return matches[0];
        }

        private static PlcTagTableGroup ResolveTagTableGroup(PlcTagTableGroup root, string? groupPath)
        {
            string normalizedPath = NormalizePlcMutationGroupPath(groupPath);
            if (normalizedPath.Length == 0)
                return root;

            PlcTagTableGroup current = root;
            foreach (string segment in normalizedPath.Split('/'))
            {
                ValidatePlcMutationName(segment, nameof(groupPath));
                var matches = current.Groups.Cast<PlcTagTableUserGroup>()
                    .Where(group => string.Equals(group.Name, segment, StringComparison.Ordinal))
                    .ToList();
                if (matches.Count == 0)
                    throw new PlcMutationRejectedException("not_found", $"Tag-table group path '{normalizedPath}' was not found.");
                if (matches.Count > 1)
                    throw new PlcMutationRejectedException("ambiguous", $"Tag-table group path '{normalizedPath}' is ambiguous.");
                current = matches[0];
            }
            return current;
        }

        private static PlcTagTable ResolveTagTable(PlcTagTableGroup root, string tableName, string? groupPath)
        {
            ValidatePlcMutationName(tableName, nameof(tableName));
            var group = ResolveTagTableGroup(root, groupPath);
            var matches = group.TagTables.Cast<PlcTagTable>()
                .Where(table => string.Equals(table.Name, tableName, StringComparison.Ordinal))
                .ToList();
            if (matches.Count == 0)
                throw new PlcMutationRejectedException("not_found",
                    $"Tag table '{tableName}' was not found at group path '{NormalizePlcMutationGroupPath(groupPath)}'.");
            if (matches.Count > 1)
                throw new PlcMutationRejectedException("ambiguous",
                    $"Tag table '{tableName}' is ambiguous at group path '{NormalizePlcMutationGroupPath(groupPath)}'.");
            return matches[0];
        }

        private static void ValidatePlcMutationName(string name, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(name) ||
                !string.Equals(name, name.Trim(), StringComparison.Ordinal) ||
                !PlcMutationNamePattern.IsMatch(name))
            {
                throw new PlcMutationRejectedException("invalid_input",
                    $"'{parameterName}' must be non-empty, have no leading/trailing whitespace, control characters, or path separators.");
            }
        }

        private static string NormalizePlcMutationGroupPath(string? groupPath)
        {
            if (groupPath == null || groupPath.Length == 0)
                return "";
            string path = groupPath;
            if (!string.Equals(path, path.Trim(), StringComparison.Ordinal) ||
                path.StartsWith("/", StringComparison.Ordinal) ||
                path.EndsWith("/", StringComparison.Ordinal) ||
                path.Contains("//"))
            {
                throw new PlcMutationRejectedException("invalid_input",
                    "Group paths must be relative slash-separated names with no empty segments or surrounding whitespace.");
            }
            return path;
        }

        private static string AppendPath(string? groupPath, string name)
        {
            string normalizedPath = NormalizePlcMutationGroupPath(groupPath);
            return normalizedPath.Length == 0 ? name : $"{normalizedPath}/{name}";
        }

        private static string BuildResultPath(string? groupPath, string name)
        {
            return string.IsNullOrEmpty(groupPath) ? name : $"{groupPath}/{name}";
        }

        private static bool TryNormalizeConstant(
            string dataType,
            string value,
            out string normalizedType,
            out string normalizedValue,
            out string error)
        {
            normalizedType = "";
            normalizedValue = "";
            error = "";
            if (string.IsNullOrWhiteSpace(dataType) || string.IsNullOrWhiteSpace(value))
            {
                error = "A supported datatype and non-empty literal value are required.";
                return false;
            }

            string requestedType = dataType.Trim();
            var integerTypes = new Dictionary<string, (string Name, int Bits, bool Signed)>(StringComparer.OrdinalIgnoreCase)
            {
                ["SInt"] = ("SInt", 8, true),
                ["Int"] = ("Int", 16, true),
                ["DInt"] = ("DInt", 32, true),
                ["LInt"] = ("LInt", 64, true),
                ["USInt"] = ("USInt", 8, false),
                ["Byte"] = ("Byte", 8, false),
                ["UInt"] = ("UInt", 16, false),
                ["Word"] = ("Word", 16, false),
                ["UDInt"] = ("UDInt", 32, false),
                ["DWord"] = ("DWord", 32, false),
                ["ULInt"] = ("ULInt", 64, false),
                ["LWord"] = ("LWord", 64, false)
            };

            if (string.Equals(requestedType, "Bool", StringComparison.OrdinalIgnoreCase))
            {
                string boolValue = value.Trim();
                if (string.Equals(boolValue, "true", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(boolValue, "false", StringComparison.OrdinalIgnoreCase))
                {
                    normalizedType = "Bool";
                    normalizedValue = boolValue.Equals("true", StringComparison.OrdinalIgnoreCase) ? "TRUE" : "FALSE";
                    return true;
                }
                error = "Bool values must be TRUE or FALSE.";
                return false;
            }

            if (integerTypes.TryGetValue(requestedType, out var integerType))
            {
                if (!BigInteger.TryParse(value.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out BigInteger integer))
                {
                    error = $"Value '{value}' is not a base-10 {integerType.Name} literal.";
                    return false;
                }

                BigInteger minimum = integerType.Signed ? -(BigInteger.One << (integerType.Bits - 1)) : BigInteger.Zero;
                BigInteger maximum = integerType.Signed
                    ? (BigInteger.One << (integerType.Bits - 1)) - BigInteger.One
                    : (BigInteger.One << integerType.Bits) - BigInteger.One;
                if (integer < minimum || integer > maximum)
                {
                    error = $"Value '{value}' is outside the {integerType.Name} range.";
                    return false;
                }

                normalizedType = integerType.Name;
                normalizedValue = integer.ToString(CultureInfo.InvariantCulture);
                return true;
            }

            if (string.Equals(requestedType, "Real", StringComparison.OrdinalIgnoreCase))
            {
                if (float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float real) &&
                    !float.IsNaN(real) && !float.IsInfinity(real))
                {
                    normalizedType = "Real";
                    normalizedValue = real.ToString("R", CultureInfo.InvariantCulture);
                    return true;
                }
                error = $"Value '{value}' is not a finite invariant-culture Real literal.";
                return false;
            }

            if (string.Equals(requestedType, "LReal", StringComparison.OrdinalIgnoreCase))
            {
                if (double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double lreal) &&
                    !double.IsNaN(lreal) && !double.IsInfinity(lreal))
                {
                    normalizedType = "LReal";
                    normalizedValue = lreal.ToString("R", CultureInfo.InvariantCulture);
                    return true;
                }
                error = $"Value '{value}' is not a finite invariant-culture LReal literal.";
                return false;
            }

            error = $"Datatype '{dataType}' is not supported by this bounded implementation.";
            return false;
        }

        private sealed class PlcMutationRejectedException : Exception
        {
            public PlcMutationRejectedException(string status, string message) : base(message)
            {
                Status = status;
            }

            public string Status { get; }
        }
    }

    public class PlcMutationResult
    {
        public bool Success { get; set; }
        public string Operation { get; set; } = "";
        public string Status { get; set; } = "";
        public string PlcName { get; set; } = "";
        public string ObjectName { get; set; } = "";
        public string TargetPath { get; set; } = "";
        public string? DataType { get; set; }
        public string? Value { get; set; }
        public bool Partial { get; set; }
        public string Message { get; set; } = "";
        public List<string> Warnings { get; set; } = new List<string>();

        public static PlcMutationResult CreateOutcome(
            string operation,
            string status,
            string plcName,
            string objectName,
            string targetPath,
            string message,
            string? dataType = null,
            string? value = null)
        {
            return new PlcMutationResult
            {
                Success = true,
                Operation = operation,
                Status = status,
                PlcName = plcName,
                ObjectName = objectName,
                TargetPath = targetPath,
                DataType = dataType,
                Value = value,
                Message = message,
                Warnings = new List<string>
                {
                    "TIA Openness does not expose a stable object ID or revision through the API surface used here.",
                    "The operation is not transactional; save and reopen is a separate persisted-state verification."
                }
            };
        }

        public static PlcMutationResult FailureOutcome(
            string operation,
            string status,
            string? plcName,
            string objectName,
            string? targetPath,
            string message)
        {
            return new PlcMutationResult
            {
                Success = false,
                Operation = operation,
                Status = status,
                PlcName = plcName ?? "",
                ObjectName = objectName ?? "",
                TargetPath = targetPath ?? "",
                Message = message,
                Partial = false
            };
        }

        public static PlcMutationResult ApiFailureOutcome(
            string operation,
            string? plcName,
            string objectName,
            string? targetPath,
            Exception exception,
            bool mutationStarted = false)
        {
            return new PlcMutationResult
            {
                Success = false,
                Operation = operation,
                Status = "api_failure",
                PlcName = plcName ?? "",
                ObjectName = objectName ?? "",
                TargetPath = targetPath ?? "",
                Message = $"{exception.GetType().Name}: {exception.Message}",
                Partial = mutationStarted,
                Warnings = mutationStarted
                    ? new List<string> { "The API call may have changed project state before failing; inspect the target by read-back." }
                    : new List<string>()
            };
        }

        public static PlcMutationResult VerificationFailureOutcome(
            string operation,
            string plcName,
            string objectName,
            string targetPath,
            string message)
        {
            return new PlcMutationResult
            {
                Success = false,
                Operation = operation,
                Status = "verification_failed",
                PlcName = plcName,
                ObjectName = objectName,
                TargetPath = targetPath,
                Partial = true,
                Message = message,
                Warnings = new List<string> { "The API call may have changed project state; inspect the target by read-back before retrying." }
            };
        }
    }
}
