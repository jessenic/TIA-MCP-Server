using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.TechnologicalObjects;

namespace TiaOpennessMcp.Tia
{
    // Technology objects (PID_Compact, PID_3Step, PID_Temp, ...) in PlcSoftware.TechnologicalObjectGroup.
    // Openness: TechnologicalInstanceDBComposition.Create(name, ofSystemLibElement, ofSystemLibVersion),
    // TechnologicalInstanceDB.Parameters.Find(name).Value. Technology objects are never deleted here.
    public partial class TiaManager
    {
        public TechnologyObjectListResult ListTechnologyObjects(string? plcName)
        {
            try
            {
                var plc = ResolveMutationPlc(plcName);
                var objects = new List<TechnologyObjectInfo>();
                foreach (var (technologyObject, groupPath) in CollectTechnologyObjects(plc))
                    objects.Add(DescribeTechnologyObject(technologyObject, groupPath));

                return new TechnologyObjectListResult
                {
                    Success = true,
                    Status = "ok",
                    PlcName = plc.Name,
                    Count = objects.Count,
                    TechnologyObjects = objects,
                    Message = objects.Count == 0
                        ? "The PLC has no technology objects. Instance DBs of PID blocks created from SCL are plain instance DBs and are listed by tia_list_blocks, not here."
                        : $"{objects.Count} technology object(s) found."
                };
            }
            catch (PlcMutationRejectedException ex)
            {
                return new TechnologyObjectListResult { Success = false, Status = ex.Status, PlcName = plcName ?? "", Message = ex.Message };
            }
            catch (EngineeringException ex)
            {
                return new TechnologyObjectListResult { Success = false, Status = "api_failure", PlcName = plcName ?? "", Message = $"{ex.GetType().Name}: {ex.Message}" };
            }
            catch (COMException ex)
            {
                return new TechnologyObjectListResult { Success = false, Status = "api_failure", PlcName = plcName ?? "", Message = $"{ex.GetType().Name}: {ex.Message}" };
            }
        }

        public TechnologyObjectParametersResult GetTechnologyObjectParameters(string? plcName, string name, string? filter)
        {
            try
            {
                ValidatePlcMutationName(name, nameof(name));
                var plc = ResolveMutationPlc(plcName);
                var (technologyObject, groupPath) = ResolveTechnologyObject(plc, name);
                var info = DescribeTechnologyObject(technologyObject, groupPath);
                var parameters = new List<TechnologyObjectParameterInfo>();
                foreach (TechnologicalParameter parameter in technologyObject.Parameters)
                {
                    string parameterName;
                    try { parameterName = parameter.Name; }
                    catch (Exception ex) when (ex is EngineeringException || ex is COMException)
                    {
                        parameters.Add(new TechnologyObjectParameterInfo { Name = "", Error = $"{ex.GetType().Name}: {ex.Message}" });
                        continue;
                    }
                    if (!string.IsNullOrEmpty(filter) && !parameterName.StartsWith(filter, StringComparison.Ordinal))
                        continue;

                    var entry = new TechnologyObjectParameterInfo { Name = parameterName };
                    try
                    {
                        object? value = parameter.Value;
                        entry.Value = TechnologyObjectRules.FormatValue(value);
                        entry.ClrType = value?.GetType().Name;
                    }
                    catch (Exception ex) when (ex is EngineeringException || ex is COMException)
                    {
                        entry.Error = $"{ex.GetType().Name}: {ex.Message}";
                    }
                    parameters.Add(entry);
                }

                return new TechnologyObjectParametersResult
                {
                    Success = true,
                    Status = "ok",
                    PlcName = plc.Name,
                    Name = info.Name,
                    Type = info.Type,
                    Version = info.Version,
                    GroupPath = groupPath,
                    Filter = filter ?? "",
                    Count = parameters.Count,
                    Parameters = parameters,
                    Message = $"{parameters.Count} parameter(s) returned. Names are the 'Name in Openness' used by tia_set_technology_object_parameters."
                };
            }
            catch (PlcMutationRejectedException ex)
            {
                return new TechnologyObjectParametersResult { Success = false, Status = ex.Status, PlcName = plcName ?? "", Name = name ?? "", Filter = filter ?? "", Message = ex.Message };
            }
            catch (EngineeringException ex)
            {
                return new TechnologyObjectParametersResult { Success = false, Status = "api_failure", PlcName = plcName ?? "", Name = name ?? "", Filter = filter ?? "", Message = $"{ex.GetType().Name}: {ex.Message}" };
            }
            catch (COMException ex)
            {
                return new TechnologyObjectParametersResult { Success = false, Status = "api_failure", PlcName = plcName ?? "", Name = name ?? "", Filter = filter ?? "", Message = $"{ex.GetType().Name}: {ex.Message}" };
            }
        }

        public PlcMutationResult CreateTechnologyObject(string? plcName, string type, string name, string version)
        {
            const string operation = "create_technology_object";
            bool mutationStarted = false;
            try
            {
                ValidatePlcMutationName(name, nameof(name));
                if (!TechnologyObjectRules.TryNormalizeType(type, out string systemLibElement, out string typeError))
                    return PlcMutationResult.FailureOutcome(operation, "invalid_input", plcName, name, name, typeError);
                if (!TechnologyObjectRules.TryParseVersion(version, out Version? requestedVersion, out string versionError))
                    return PlcMutationResult.FailureOutcome(operation, "invalid_input", plcName, name, name, versionError);

                var plc = ResolveMutationPlc(plcName);
                string? collision = FindTechnologyObjectNameCollision(plc, name, systemLibElement);
                if (collision != null)
                    return PlcMutationResult.FailureOutcome(operation, "collision", plc.Name, name, name, collision);

                var composition = plc.TechnologicalObjectGroup.TechnologicalObjects;
                mutationStarted = true;
                var created = composition.Create(name, systemLibElement, requestedVersion!);

                var readBack = composition.Cast<TechnologicalInstanceDB>()
                    .Where(item => string.Equals(item.Name, name, StringComparison.Ordinal))
                    .ToList();
                var found = composition.Find(name);
                if (readBack.Count != 1 || found == null)
                    return PlcMutationResult.VerificationFailureOutcome(operation, plc.Name, name, name,
                        "Created technology object could not be verified by exact-name read-back.");
                string actualElement = found.OfSystemLibElement ?? "";
                Version? actualVersion = found.OfSystemLibVersion;
                if (!string.Equals(actualElement, systemLibElement, StringComparison.Ordinal) ||
                    !TechnologyObjectRules.VersionMatches(requestedVersion!, actualVersion))
                {
                    return PlcMutationResult.VerificationFailureOutcome(operation, plc.Name, found.Name, found.Name,
                        $"Created technology object reads back as '{actualElement}' V{actualVersion}, expected '{systemLibElement}' V{TechnologyObjectRules.FormatVersion(requestedVersion!)}.");
                }

                var result = PlcMutationResult.CreateOutcome(operation, "created", plc.Name, found.Name, found.Name,
                    "Technology object created in the 'Technology objects' folder. It is not called yet: call it from a cyclic interrupt OB with this name as its instance.",
                    actualElement, actualVersion?.ToString() ?? TechnologyObjectRules.FormatVersion(requestedVersion!));
                if (!string.Equals(type, systemLibElement, StringComparison.Ordinal))
                    result.Warnings.Add($"Type '{type}' was mapped to the Openness system-library name '{systemLibElement}'.");
                return result;
            }
            catch (PlcMutationRejectedException ex)
            {
                return PlcMutationResult.FailureOutcome(operation, ex.Status, plcName, name, name, ex.Message);
            }
            catch (EngineeringException ex)
            {
                return PlcMutationResult.ApiFailureOutcome(operation, plcName, name, name, ex, mutationStarted);
            }
            catch (COMException ex)
            {
                return PlcMutationResult.ApiFailureOutcome(operation, plcName, name, name, ex, mutationStarted);
            }
        }

        public TechnologyObjectParameterSetResult SetTechnologyObjectParameters(
            string? plcName,
            string name,
            IReadOnlyList<KeyValuePair<string, string>> parameters)
        {
            const string operation = "set_technology_object_parameters";
            var result = new TechnologyObjectParameterSetResult { Operation = operation, PlcName = plcName ?? "", ObjectName = name ?? "" };
            bool writesStarted = false;
            try
            {
                ValidatePlcMutationName(name!, nameof(name));
                if (parameters == null || parameters.Count == 0)
                    return result.Fail("invalid_input", "At least one parameter is required.");
                if (parameters.Any(p => string.IsNullOrEmpty(p.Key)))
                    return result.Fail("invalid_input", "Every parameter needs a non-empty name.");
                var duplicates = TechnologyObjectRules.FindDuplicateNames(parameters.Select(p => p.Key));
                if (duplicates.Count > 0)
                    return result.Fail("invalid_input", "Duplicate parameter names: " + string.Join(", ", duplicates) + ".");

                var plc = ResolveMutationPlc(plcName);
                result.PlcName = plc.Name;
                var (technologyObject, _) = ResolveTechnologyObject(plc, name!);
                result.ObjectName = technologyObject.Name;

                // Validate every parameter before writing any.
                var planned = new List<(TechnologicalParameter Parameter, TechnologyObjectParameterWrite Entry, object Converted)>();
                List<string>? allNames = null;
                foreach (var request in parameters)
                {
                    var entry = new TechnologyObjectParameterWrite { Name = request.Key, RequestedValue = request.Value, Status = "pending" };
                    result.Parameters.Add(entry);
                    var parameter = technologyObject.Parameters.Find(request.Key);
                    if (parameter == null)
                    {
                        allNames ??= technologyObject.Parameters.Cast<TechnologicalParameter>().Select(p => p.Name).ToList();
                        var similar = TechnologyObjectRules.SuggestParameterNames(allNames, request.Key, 10);
                        entry.Status = "not_found";
                        entry.Message = similar.Count == 0
                            ? "Parameter not found on this technology object."
                            : "Parameter not found. Similar names: " + string.Join(", ", similar);
                        return result.Fail("not_found",
                            $"Parameter '{request.Key}' was not found on '{technologyObject.Name}'. Nothing was written. Use tia_get_technology_object_parameters to list the names.");
                    }
                    // Find may match case-insensitively; names are exact, so a case mismatch is rejected.
                    string foundName = parameter.Name;
                    if (!string.Equals(foundName, request.Key, StringComparison.Ordinal))
                    {
                        entry.Status = "not_found";
                        entry.Message = $"Parameter names are case-sensitive. Did you mean: {foundName}?";
                        return result.Fail("not_found",
                            $"Parameter '{request.Key}' differs in case from '{foundName}' on '{technologyObject.Name}'. Nothing was written.");
                    }

                    object? current;
                    try { current = parameter.Value; }
                    catch (Exception ex) when (ex is EngineeringException || ex is COMException)
                    {
                        entry.Status = "not_readable";
                        entry.Message = $"{ex.GetType().Name}: {ex.Message}";
                        return result.Fail("invalid_input", $"The current value of '{request.Key}' cannot be read, so its type is unknown. Nothing was written.");
                    }
                    if (current == null)
                    {
                        entry.Status = "invalid_input";
                        entry.Message = "The current value is null, so its type is unknown.";
                        return result.Fail("invalid_input", $"The current value of '{request.Key}' is null. Nothing was written.");
                    }

                    entry.ClrType = current.GetType().Name;
                    entry.OldValue = TechnologyObjectRules.FormatValue(current);
                    if (!TechnologyObjectRules.TryConvertParameterValue(current.GetType(), request.Value, out object? converted, out string conversionError))
                    {
                        entry.Status = "invalid_input";
                        entry.Message = conversionError;
                        return result.Fail("invalid_input", $"Parameter '{request.Key}': {conversionError} Nothing was written.");
                    }
                    planned.Add((parameter, entry, converted!));
                }

                // Every write and read-back failure is caught per entry inside ExecuteParameterWrites.
                writesStarted = true;
                var writes = planned.Select(item =>
                {
                    var parameter = item.Parameter;
                    string parameterName = item.Entry.Name;
                    return new TechnologyObjectPlannedWrite(
                        item.Entry,
                        item.Converted,
                        value => parameter.Value = value,
                        () => technologyObject.Parameters.Find(parameterName)?.Value);
                }).ToList();
                return TechnologyObjectRules.ExecuteParameterWrites(result, writes);
            }
            catch (PlcMutationRejectedException ex)
            {
                return result.Fail(ex.Status, ex.Message, partial: writesStarted);
            }
            catch (EngineeringException ex)
            {
                return result.Fail("api_failure", $"{ex.GetType().Name}: {ex.Message}", partial: writesStarted);
            }
            catch (COMException ex)
            {
                return result.Fail("api_failure", $"{ex.GetType().Name}: {ex.Message}", partial: writesStarted);
            }
        }

        private static List<(TechnologicalInstanceDB TechnologyObject, string GroupPath)> CollectTechnologyObjects(PlcSoftware plc)
        {
            var result = new List<(TechnologicalInstanceDB, string)>();
            if (plc.TechnologicalObjectGroup == null)
                return result;
            void Walk(TechnologicalInstanceDBGroup group, string path)
            {
                foreach (TechnologicalInstanceDB technologyObject in group.TechnologicalObjects)
                    result.Add((technologyObject, path));
                foreach (TechnologicalInstanceDBUserGroup sub in group.Groups)
                    Walk(sub, path.Length == 0 ? sub.Name : $"{path}/{sub.Name}");
            }
            Walk(plc.TechnologicalObjectGroup, "");
            return result;
        }

        private static (TechnologicalInstanceDB TechnologyObject, string GroupPath) ResolveTechnologyObject(PlcSoftware plc, string name)
        {
            var all = CollectTechnologyObjects(plc);
            var matches = all.Where(item => string.Equals(item.TechnologyObject.Name, name, StringComparison.Ordinal)).ToList();
            if (matches.Count == 1)
                return matches[0];
            if (matches.Count > 1)
                throw new PlcMutationRejectedException("ambiguous", $"More than one technology object is named '{name}'.");

            var caseMatches = all.Where(item => string.Equals(item.TechnologyObject.Name, name, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.TechnologyObject.Name).ToList();
            throw new PlcMutationRejectedException("not_found", caseMatches.Count > 0
                ? $"Technology object '{name}' was not found; names are case-sensitive. Did you mean: {string.Join(", ", caseMatches)}?"
                : $"Technology object '{name}' was not found in PLC '{plc.Name}'. Use tia_list_technology_objects to list them.");
        }

        private static TechnologyObjectInfo DescribeTechnologyObject(TechnologicalInstanceDB technologyObject, string groupPath)
        {
            var info = new TechnologyObjectInfo { GroupPath = groupPath };
            try
            {
                info.Name = technologyObject.Name;
                info.Type = technologyObject.OfSystemLibElement ?? "";
                info.Version = technologyObject.OfSystemLibVersion?.ToString() ?? "";
                info.Number = technologyObject.Number;
                info.IsConsistent = technologyObject.IsConsistent;
            }
            catch (Exception ex) when (ex is EngineeringException || ex is COMException)
            {
                info.Error = $"{ex.GetType().Name}: {ex.Message}";
            }
            return info;
        }

        // Technology objects and blocks share one name space in a PLC; never overwrite either.
        private static string? FindTechnologyObjectNameCollision(PlcSoftware plc, string name, string systemLibElement)
        {
            var existingObject = CollectTechnologyObjects(plc)
                .FirstOrDefault(item => string.Equals(item.TechnologyObject.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existingObject.TechnologyObject != null)
                return $"A technology object named '{existingObject.TechnologyObject.Name}' already exists. Nothing was created or changed.";

            PlcBlock? block = FindBlockByNameIgnoreCase(plc.BlockGroup, name);
            if (block == null)
            {
                // Program resources (system blocks such as the PID_Temp FB itself) are in SystemBlockGroups.
                foreach (PlcSystemBlockGroup systemGroup in plc.BlockGroup.SystemBlockGroups)
                {
                    PlcBlock? systemBlock = FindSystemBlockByNameIgnoreCase(systemGroup, name);
                    if (systemBlock != null)
                        return $"System block '{systemBlock.Name}' (program resources) already uses this name; blocks and technology objects share one name space. Choose another name, e.g. '{systemBlock.Name}_1'. Nothing was created or changed.";
                }
                return null;
            }

            if (block is InstanceDB instanceDb)
            {
                string? instanceOf = null;
                try { instanceOf = instanceDb.InstanceOfName; } catch (EngineeringException) { }
                if (instanceOf != null && string.Equals(instanceOf, systemLibElement, StringComparison.OrdinalIgnoreCase))
                {
                    return $"Block '{block.Name}' already exists as a plain instance DB of {instanceOf} (for example created from an SCL call); it is not a technology object. " +
                        "Rename or delete that instance DB in TIA Portal (or with tia_delete_block), or choose another name, then create the technology object and call it with its name as instance. Nothing was created or changed.";
                }
                return $"Block '{block.Name}' (instance DB of {instanceOf ?? "an unknown block"}) already exists with this name. Nothing was created or changed.";
            }
            return $"Block '{block.Name}' already exists with this name; blocks and technology objects share one name space. Nothing was created or changed.";
        }

        private static PlcBlock? FindBlockByNameIgnoreCase(PlcBlockGroup group, string name)
        {
            foreach (PlcBlock block in group.Blocks)
                if (string.Equals(block.Name, name, StringComparison.OrdinalIgnoreCase))
                    return block;
            foreach (PlcBlockUserGroup sub in group.Groups)
            {
                var found = FindBlockByNameIgnoreCase(sub, name);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static PlcBlock? FindSystemBlockByNameIgnoreCase(PlcSystemBlockGroup group, string name)
        {
            foreach (PlcBlock block in group.Blocks)
                if (string.Equals(block.Name, name, StringComparison.OrdinalIgnoreCase))
                    return block;
            foreach (PlcSystemBlockGroup sub in group.Groups)
            {
                var found = FindSystemBlockByNameIgnoreCase(sub, name);
                if (found != null)
                    return found;
            }
            return null;
        }

        // Used by tia_import_scl_block: a block may not take a technology object's name. Returns the existing
        // technology object's name (case-insensitive match), or null when there is none or it cannot be read.
        private static string? FindTechnologyObjectNameForBlockImport(PlcSoftware plc, string name)
        {
            try
            {
                return CollectTechnologyObjects(plc)
                    .Select(item => item.TechnologyObject.Name)
                    .FirstOrDefault(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex) when (ex is EngineeringException || ex is COMException)
            {
                return null;
            }
        }
    }

    // Pure input and value rules for technology-object tools; no Openness access (unit-tested offline).
    public static class TechnologyObjectRules
    {
        private static readonly Regex VersionPattern = new Regex(@"^[Vv]?(\d+)\.(\d+)$", RegexOptions.CultureInvariant);

        private static readonly Dictionary<string, string> TypeAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TO_PID_Temp"] = "PID_Temp",
            ["TO_PID_Compact"] = "PID_Compact",
            ["TO_PID_3Step"] = "PID_3Step",
            ["PID_Temp"] = "PID_Temp",
            ["PID_Compact"] = "PID_Compact",
            ["PID_3Step"] = "PID_3Step"
        };

        // Maps the PID names users often write (TO_PID_Temp, pid_temp) to the Openness system-library name; any other
        // type string is passed to Openness unchanged so TIA decides whether it is supported.
        public static bool TryNormalizeType(string? type, out string normalized, out string error)
        {
            normalized = "";
            error = "";
            if (string.IsNullOrWhiteSpace(type) || !string.Equals(type, type!.Trim(), StringComparison.Ordinal))
            {
                error = "'type' must be a non-empty system-library name such as PID_Temp, PID_Compact or PID_3Step, without surrounding whitespace.";
                return false;
            }
            normalized = TypeAliases.TryGetValue(type, out string? alias) ? alias : type;
            return true;
        }

        public static bool TryParseVersion(string? text, out Version? version, out string error)
        {
            version = null;
            error = "";
            var match = text == null ? null : VersionPattern.Match(text);
            if (match == null || !match.Success ||
                !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int major) ||
                !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int minor))
            {
                error = "'version' must be major.minor such as '1.1' or 'V1.1' (the technology object version, e.g. PID_Temp V1.1 on S7-1200).";
                return false;
            }
            version = new Version(major, minor);
            return true;
        }

        public static string FormatVersion(Version version) => $"{version.Major}.{version.Minor}";

        // TIA may return 1.1, 1.1.0 or 1.1.0.0; only major and minor are compared.
        public static bool VersionMatches(Version requested, Version? actual)
        {
            return actual != null && requested.Major == actual.Major && requested.Minor == actual.Minor;
        }

        // Case-insensitive: whether TechnologicalParameterComposition.Find ignores case is not documented.
        public static List<string> FindDuplicateNames(IEnumerable<string> names)
        {
            return names.GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();
        }

        // Names that match case-insensitively or share the requested name's prefix up to its last '.'.
        public static List<string> SuggestParameterNames(IEnumerable<string> allNames, string requested, int max)
        {
            int dot = requested.LastIndexOf('.');
            string prefix = dot >= 0 ? requested.Substring(0, dot + 1) : requested;
            var names = allNames.Where(name => name != null).ToList();
            return names.Where(name => string.Equals(name, requested, StringComparison.OrdinalIgnoreCase))
                .Concat(names.Where(name => prefix.Length > 0 && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.Ordinal)
                .Take(max)
                .ToList();
        }

        public static string? FormatValue(object? value)
        {
            switch (value)
            {
                case null: return null;
                case bool boolean: return boolean ? "true" : "false";
                case float single: return single.ToString("R", CultureInfo.InvariantCulture);
                case double number: return number.ToString("R", CultureInfo.InvariantCulture);
                case IFormattable formattable: return formattable.ToString(null, CultureInfo.InvariantCulture);
                default: return value.ToString();
            }
        }

        public static bool ValuesEqual(object? expected, object? actual)
        {
            if (expected == null || actual == null)
                return expected == null && actual == null;
            if (expected.Equals(actual))
                return true;
            return string.Equals(FormatValue(expected), FormatValue(actual), StringComparison.Ordinal);
        }

        // Writes the validated entries in order and verifies each by read-back; stops at the first failure.
        // Entry status: updated, api_failure (setter threw; the value may or may not be in TIA), verification_failed
        // (setter returned but read-back threw or differs), not_written (never attempted). No rollback.
        public static TechnologyObjectParameterSetResult ExecuteParameterWrites(
            TechnologyObjectParameterSetResult result,
            IReadOnlyList<TechnologyObjectPlannedWrite> writes)
        {
            for (int index = 0; index < writes.Count; index++)
            {
                var write = writes[index];
                var entry = write.Entry;
                string earlier = index > 0
                    ? "Earlier parameters in the list were written and are not rolled back"
                    : "No earlier parameter was written";
                try
                {
                    write.Write(write.Converted);
                }
                catch (Exception ex)
                {
                    entry.Status = "api_failure";
                    entry.Message = $"{ex.GetType().Name}: {ex.Message}";
                    return result.Fail("api_failure", index > 0
                            ? $"Writing '{entry.Name}' failed; earlier parameters in the list were written and are not rolled back."
                            : $"Writing '{entry.Name}' failed; TIA may have rejected the value or the parameter is read-only.",
                        partial: true,
                        partialWarning: $"{earlier}. The failed write of '{entry.Name}' may still have changed project state; inspect it with tia_get_technology_object_parameters.");
                }

                // From here on the setter has returned, so the value may be in TIA.
                entry.Status = "written_unverified";
                object? readBack;
                try
                {
                    readBack = write.ReadBack();
                }
                catch (Exception ex)
                {
                    entry.Status = "verification_failed";
                    entry.Message = $"The value was written, but reading it back failed: {ex.GetType().Name}: {ex.Message}";
                    return result.Fail("verification_failed",
                        $"'{entry.Name}' was written but could not be read back; later parameters were not written.",
                        partial: true,
                        partialWarning: $"{earlier}. '{entry.Name}' was written and not verified; inspect it with tia_get_technology_object_parameters.");
                }

                entry.NewValue = FormatValue(readBack);
                if (!ValuesEqual(write.Converted, readBack))
                {
                    entry.Status = "verification_failed";
                    entry.Message = "The value read back after writing differs from the requested value (TIA may have rejected or recalculated it).";
                    return result.Fail("verification_failed",
                        $"Read-back of '{entry.Name}' does not match the written value; later parameters were not written.",
                        partial: true,
                        partialWarning: $"{earlier}. '{entry.Name}' now reads back as '{entry.NewValue}'; inspect it with tia_get_technology_object_parameters.");
                }
                entry.Status = "updated";
            }

            result.Success = true;
            result.Status = "updated";
            result.Message = $"{writes.Count} parameter(s) written and verified by read-back.";
            result.Warnings.Add("The writes are not transactional; save and reopen is a separate persisted-state verification.");
            return result;
        }

        // Converts invariant-culture text to the CLR type of the parameter's current value.
        public static bool TryConvertParameterValue(Type targetType, string? text, out object? converted, out string error)
        {
            converted = null;
            error = "";
            if (text == null)
            {
                error = "A value is required.";
                return false;
            }

            if (targetType == typeof(string))
            {
                converted = text;
                return true;
            }
            if (targetType == typeof(char))
            {
                if (text.Length != 1)
                {
                    error = $"Value '{text}' must be exactly one character (Char).";
                    return false;
                }
                converted = text[0];
                return true;
            }

            string trimmed = text.Trim();
            if (targetType == typeof(bool))
            {
                if (string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase)) { converted = true; return true; }
                if (string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase)) { converted = false; return true; }
                error = $"Value '{text}' must be true or false (Boolean).";
                return false;
            }

            const NumberStyles integerStyle = NumberStyles.AllowLeadingSign;
            var culture = CultureInfo.InvariantCulture;
            bool ok;
            if (targetType == typeof(sbyte)) { ok = sbyte.TryParse(trimmed, integerStyle, culture, out sbyte v); converted = v; }
            else if (targetType == typeof(byte)) { ok = byte.TryParse(trimmed, integerStyle, culture, out byte v); converted = v; }
            else if (targetType == typeof(short)) { ok = short.TryParse(trimmed, integerStyle, culture, out short v); converted = v; }
            else if (targetType == typeof(ushort)) { ok = ushort.TryParse(trimmed, integerStyle, culture, out ushort v); converted = v; }
            else if (targetType == typeof(int)) { ok = int.TryParse(trimmed, integerStyle, culture, out int v); converted = v; }
            else if (targetType == typeof(uint)) { ok = uint.TryParse(trimmed, integerStyle, culture, out uint v); converted = v; }
            else if (targetType == typeof(long)) { ok = long.TryParse(trimmed, integerStyle, culture, out long v); converted = v; }
            else if (targetType == typeof(ulong)) { ok = ulong.TryParse(trimmed, integerStyle, culture, out ulong v); converted = v; }
            else if (targetType == typeof(float))
            {
                ok = float.TryParse(trimmed, NumberStyles.Float, culture, out float v) && !float.IsNaN(v) && !float.IsInfinity(v);
                converted = v;
            }
            else if (targetType == typeof(double))
            {
                ok = double.TryParse(trimmed, NumberStyles.Float, culture, out double v) && !double.IsNaN(v) && !double.IsInfinity(v);
                converted = v;
            }
            else
            {
                error = $"Parameters of CLR type {targetType.Name} are not supported by this tool.";
                return false;
            }

            if (!ok)
            {
                converted = null;
                error = $"Value '{text}' is not a valid invariant-culture {targetType.Name} literal or is out of range.";
                return false;
            }
            return true;
        }
    }

    // One validated parameter write; Write and ReadBack wrap the Openness parameter (fakes in offline tests).
    public sealed class TechnologyObjectPlannedWrite
    {
        public TechnologyObjectPlannedWrite(TechnologyObjectParameterWrite entry, object converted, Action<object> write, Func<object?> readBack)
        {
            Entry = entry;
            Converted = converted;
            Write = write;
            ReadBack = readBack;
        }

        public TechnologyObjectParameterWrite Entry { get; }
        public object Converted { get; }
        public Action<object> Write { get; }
        public Func<object?> ReadBack { get; }
    }

    public class TechnologyObjectInfo
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string Version { get; set; } = "";
        public int? Number { get; set; }
        public string GroupPath { get; set; } = "";
        public bool? IsConsistent { get; set; }
        public string? Error { get; set; }
    }

    public class TechnologyObjectListResult
    {
        public bool Success { get; set; }
        public string Status { get; set; } = "";
        public string PlcName { get; set; } = "";
        public int Count { get; set; }
        public List<TechnologyObjectInfo> TechnologyObjects { get; set; } = new List<TechnologyObjectInfo>();
        public string Message { get; set; } = "";
    }

    public class TechnologyObjectParameterInfo
    {
        public string Name { get; set; } = "";
        public string? Value { get; set; }
        public string? ClrType { get; set; }
        public string? Error { get; set; }
    }

    public class TechnologyObjectParametersResult
    {
        public bool Success { get; set; }
        public string Status { get; set; } = "";
        public string PlcName { get; set; } = "";
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string Version { get; set; } = "";
        public string GroupPath { get; set; } = "";
        public string Filter { get; set; } = "";
        public int Count { get; set; }
        public List<TechnologyObjectParameterInfo> Parameters { get; set; } = new List<TechnologyObjectParameterInfo>();
        public string Message { get; set; } = "";
    }

    public class TechnologyObjectParameterWrite
    {
        public string Name { get; set; } = "";
        public string? ClrType { get; set; }
        public string? OldValue { get; set; }
        public string? RequestedValue { get; set; }
        public string? NewValue { get; set; }
        public string Status { get; set; } = "";
        public string? Message { get; set; }
    }

    public class TechnologyObjectParameterSetResult
    {
        public bool Success { get; set; }
        public string Operation { get; set; } = "";
        public string Status { get; set; } = "";
        public string PlcName { get; set; } = "";
        public string ObjectName { get; set; } = "";
        public bool Partial { get; set; }
        public string Message { get; set; } = "";
        public List<TechnologyObjectParameterWrite> Parameters { get; set; } = new List<TechnologyObjectParameterWrite>();
        public List<string> Warnings { get; set; } = new List<string>();

        // partialWarning replaces the default warning when partial is true and the default would be wrong.
        public TechnologyObjectParameterSetResult Fail(string status, string message, bool partial = false, string? partialWarning = null)
        {
            Success = false;
            Status = status;
            Message = message;
            Partial = partial;
            foreach (var entry in Parameters.Where(entry => entry.Status == "pending"))
                entry.Status = "not_written";
            if (partial)
                Warnings.Add(partialWarning ?? "Some parameters may have been written before the failure and are not rolled back; inspect them with tia_get_technology_object_parameters.");
            return this;
        }
    }
}
