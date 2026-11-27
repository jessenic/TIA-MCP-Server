using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Siemens.Engineering;
using Siemens.Engineering.SW.Blocks;

namespace TiaOpennessMcp.Tia
{
    // OB attributes (cyclic interrupt interval, phase offset, priority): import warnings, read and in-place write.
    // In SimaticML and Openness the OB attributes CyclicTime and PhaseOffset are Int32 microseconds
    // (Openness default CyclicTime 100000 = 100 ms); the TIA editor shows the interval in milliseconds.
    public partial class TiaManager
    {
        internal static readonly string[] SettableBlockAttributes = { "CyclicTime", "PhaseOffset", "PriorityNumber" };
        private const long MaxCyclicTimeMicroseconds = 60000L * 1000L;

        // Pure check of a SimaticML document before tia_import_block. Never throws; malformed XML gives no warnings.
        internal static List<string> InspectSimaticMlBlockAttributes(string? xml)
        {
            var warnings = new List<string>();
            if (string.IsNullOrWhiteSpace(xml))
                return warnings;

            XDocument document;
            try
            {
                document = XDocument.Parse(xml);
            }
            catch
            {
                return warnings;
            }

            foreach (XElement ob in document.Descendants().Where(e => e.Name.LocalName == "SW.Blocks.OB"))
            {
                XElement? attributes = ob.Elements().FirstOrDefault(e => e.Name.LocalName == "AttributeList");
                if (attributes == null)
                    continue;

                string? Read(string name) =>
                    attributes.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim();
                bool Has(string name) => attributes.Elements().Any(e => e.Name.LocalName == name);

                string name = Read("Name") ?? "";
                string? numberText = Read("Number");
                string label = string.IsNullOrEmpty(numberText) ? $"OB '{name}'" : $"OB '{name}' (OB{numberText})";
                string? secondaryType = Read("SecondaryType");
                bool cyclic = secondaryType == null ? Has("CyclicTime") : IsCyclicInterruptSecondaryType(secondaryType);

                long? cyclicTime = ParseLong(Read("CyclicTime"));
                long? phaseOffset = ParseLong(Read("PhaseOffset"));

                if (cyclic && cyclicTime.HasValue && cyclicTime.Value < 1000)
                {
                    warnings.Add($"{label}: CyclicTime is in microseconds; {FormatMicroseconds(cyclicTime.Value)} is below the 1 ms minimum " +
                                 "of the TIA Interval setting (1..60000 ms), so compiling fails with 'Interval: The value must be between 1 and 60000'. For 100 ms use <CyclicTime>100000</CyclicTime>.");
                }
                else if (cyclic && cyclicTime.HasValue && cyclicTime.Value % 1000 != 0)
                {
                    warnings.Add($"{label}: CyclicTime {FormatMicroseconds(cyclicTime.Value)} is not a whole number of milliseconds; S7-1200 accepts only whole milliseconds.");
                }

                if (Has("PhaseShift"))
                {
                    warnings.Add($"{label}: OBs have no attribute 'PhaseShift'; the phase shift of a cyclic interrupt OB is 'PhaseOffset' (microseconds).");
                }
                if (Has("CyclicApplicationCycleTime"))
                {
                    warnings.Add($"{label}: 'CyclicApplicationCycleTime' is read-only and is not the cyclic interrupt interval; set 'CyclicTime' (microseconds).");
                }

                if (cyclicTime.HasValue && phaseOffset.HasValue && phaseOffset.Value >= cyclicTime.Value)
                {
                    warnings.Add($"{label}: PhaseOffset {FormatMicroseconds(phaseOffset.Value)} must be smaller than CyclicTime {FormatMicroseconds(cyclicTime.Value)}.");
                }

                long? number = ParseLong(numberText);
                if (number.HasValue && number.Value >= 30 && number.Value <= 38 &&
                    (secondaryType == null || !IsCyclicInterruptSecondaryType(secondaryType)))
                {
                    warnings.Add($"{label}: OB30..OB38 are cyclic interrupt OBs, but SecondaryType is '{secondaryType ?? "(missing)"}'; TIA checks that SecondaryType matches the OB number on import (expected 'CyclicInterrupt').");
                }
            }

            return warnings;
        }

        // Validates one tia_set_block_attribute request and converts the value to the API unit (Int32; microseconds for times).
        internal static bool TryNormalizeBlockAttributeRequest(
            string? attributeName,
            long value,
            string? unit,
            out string canonicalName,
            out int apiValue,
            out string error)
        {
            canonicalName = "";
            apiValue = 0;
            error = "";

            string? match = SettableBlockAttributes.FirstOrDefault(
                name => string.Equals(name, (attributeName ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                error = $"attribute_name '{attributeName}' is not settable; allowed: {string.Join(", ", SettableBlockAttributes)}. " +
                        "('PhaseShift' does not exist on OBs, use PhaseOffset; 'CyclicApplicationCycleTime' is read-only.)";
                return false;
            }
            canonicalName = match;

            if (!TryConvertBlockAttributeValue(canonicalName, value, unit, out long converted, out error))
                return false;

            if (canonicalName == "CyclicTime" && (converted < 1 || converted > MaxCyclicTimeMicroseconds))
            {
                error = $"CyclicTime must be 1..60000 ms (1..{MaxCyclicTimeMicroseconds} us); got {value} {NormalizeUnit(unit)}.";
                return false;
            }
            if (canonicalName == "PhaseOffset" && (converted < 0 || converted > MaxCyclicTimeMicroseconds))
            {
                error = $"PhaseOffset must be 0..60000 ms (0..{MaxCyclicTimeMicroseconds} us); got {value} {NormalizeUnit(unit)}.";
                return false;
            }
            if (canonicalName == "PriorityNumber" && (converted < 1 || converted > int.MaxValue))
            {
                error = "PriorityNumber must be a positive integer; TIA Portal checks the range the CPU allows.";
                return false;
            }

            apiValue = (int)converted;
            return true;
        }

        // Converts a value given in the request unit to the API unit, without range checks (used for expected_current_value too).
        internal static bool TryConvertBlockAttributeValue(string canonicalName, long value, string? unit, out long converted, out string error)
        {
            converted = 0;
            error = "";
            string normalizedUnit = NormalizeUnit(unit);
            bool timeAttribute = canonicalName == "CyclicTime" || canonicalName == "PhaseOffset";
            if (timeAttribute)
            {
                if (normalizedUnit != "ms" && normalizedUnit != "us")
                {
                    error = $"{canonicalName} needs unit 'ms' or 'us' (the API stores microseconds; 100 ms = 100000 us).";
                    return false;
                }
                if (normalizedUnit == "ms")
                {
                    if (value > long.MaxValue / 1000 || value < long.MinValue / 1000)
                    {
                        error = $"{canonicalName} value {value} ms is out of range.";
                        return false;
                    }
                    converted = value * 1000;
                }
                else
                {
                    converted = value;
                }
            }
            else
            {
                if (normalizedUnit.Length > 0)
                {
                    error = $"{canonicalName} has no unit; omit 'unit'.";
                    return false;
                }
                converted = value;
            }

            if (converted > int.MaxValue || converted < int.MinValue)
            {
                error = $"{canonicalName} value is outside the Int32 range of the API.";
                return false;
            }
            return true;
        }

        internal static string FormatMicroseconds(long microseconds)
        {
            return $"{microseconds.ToString(CultureInfo.InvariantCulture)} us ({(microseconds / 1000.0).ToString("0.###", CultureInfo.InvariantCulture)} ms)";
        }

        internal static bool IsCyclicInterruptSecondaryType(string? secondaryType)
        {
            return string.Equals((secondaryType ?? "").Trim(), "CyclicInterrupt", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeUnit(string? unit) => (unit ?? "").Trim().ToLowerInvariant();

        private static long? ParseLong(string? text)
        {
            return long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value)
                ? value
                : (long?)null;
        }

        private static long? TryReadLongAttribute(IEngineeringObject target, string attributeName)
        {
            try
            {
                object? value = target.GetAttribute(attributeName);
                return value == null ? (long?)null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                return null;
            }
        }

        // OB-only attributes added to tia_get_block_attributes. Each one is read on its own; a missing one is 'not_supported'.
        private static List<PlcMetadataAttribute> ReadObAttributes(OB ob)
        {
            var results = new List<PlcMetadataAttribute>();

            string? secondaryType = null;
            bool secondaryOk = true;
            try { secondaryType = ob.SecondaryType; }
            catch (Exception ex) when (!(ex is OutOfMemoryException)) { secondaryOk = false; }
            results.Add(new PlcMetadataAttribute
            {
                Name = "SecondaryType",
                Status = secondaryOk ? "available" : "not_supported",
                Value = secondaryType
            });

            foreach (string name in new[] { "CyclicTime", "PhaseOffset" })
            {
                long? value = TryReadLongAttribute(ob, name);
                results.Add(new PlcMetadataAttribute
                {
                    Name = name,
                    Status = value.HasValue ? "available" : "not_supported",
                    Value = value,
                    Unit = value.HasValue ? "us" : null,
                    ValueMs = value.HasValue ? value.Value / 1000.0 : (double?)null
                });
            }

            long? priority = TryReadLongAttribute(ob, "PriorityNumber");
            results.Add(new PlcMetadataAttribute
            {
                Name = "PriorityNumber",
                Status = priority.HasValue ? "available" : "not_supported",
                Value = priority
            });

            object? cycleTime = null;
            bool cycleOk = true;
            try { cycleTime = ob.GetAttribute("CyclicApplicationCycleTime"); }
            catch (Exception ex) when (!(ex is OutOfMemoryException)) { cycleOk = false; }
            results.Add(new PlcMetadataAttribute
            {
                Name = "CyclicApplicationCycleTime",
                Status = cycleOk ? "available" : "not_supported",
                Value = cycleTime is IConvertible ? cycleTime : cycleTime?.ToString()
            });

            return results;
        }

        // After tia_import_block: the cycle settings TIA actually stored for each imported cyclic interrupt OB.
        private static List<string> DescribeImportedObAttributes(IEnumerable<PlcBlock> importedBlocks)
        {
            var lines = new List<string>();
            foreach (PlcBlock block in importedBlocks)
            {
                if (!(block is OB ob))
                    continue;
                string? secondaryType = null;
                try { secondaryType = ob.SecondaryType; }
                catch (Exception ex) when (!(ex is OutOfMemoryException)) { }
                if (!IsCyclicInterruptSecondaryType(secondaryType))
                    continue;

                long? cyclicTime = TryReadLongAttribute(ob, "CyclicTime");
                long? phaseOffset = TryReadLongAttribute(ob, "PhaseOffset");
                lines.Add($"OB '{ob.Name}' (OB{ob.Number}) read back: CyclicTime " +
                          (cyclicTime.HasValue ? FormatMicroseconds(cyclicTime.Value) : "not readable") +
                          ", PhaseOffset " +
                          (phaseOffset.HasValue ? FormatMicroseconds(phaseOffset.Value) : "not readable") + ".");
            }
            return lines;
        }

        internal static string AppendImportNotes(string message, IList<string> readBack, IList<string> warnings)
        {
            string result = message;
            foreach (string line in readBack)
                result += Environment.NewLine + line;
            if (warnings.Count > 0)
            {
                result += Environment.NewLine + "Warnings:";
                foreach (string warning in warnings)
                    result += Environment.NewLine + "- " + warning;
            }
            return result;
        }

        public PlcMutationResult SetBlockAttribute(
            string? plcName,
            string blockName,
            string? groupPath,
            string attributeName,
            long value,
            string? unit,
            long? expectedCurrentValue)
        {
            const string operation = "set_block_attribute";
            bool mutationStarted = false;
            string targetPath = BuildResultPath(groupPath, blockName);
            try
            {
                if (string.IsNullOrWhiteSpace(blockName))
                    return PlcMutationResult.FailureOutcome(operation, "invalid_input", plcName, blockName, targetPath, "block_name is required.");

                if (!TryNormalizeBlockAttributeRequest(attributeName, value, unit, out string name, out int apiValue, out string error))
                    return PlcMutationResult.FailureOutcome(operation, "invalid_input", plcName, blockName, targetPath, error);

                long? expectedApiValue = null;
                if (expectedCurrentValue.HasValue)
                {
                    if (!TryConvertBlockAttributeValue(name, expectedCurrentValue.Value, unit, out long expected, out error))
                        return PlcMutationResult.FailureOutcome(operation, "invalid_input", plcName, blockName, targetPath, "expected_current_value: " + error);
                    expectedApiValue = expected;
                }

                string? requestedGroup = groupPath == null ? null : NormalizePlcMutationGroupPath(groupPath);
                var plc = ResolveMutationPlc(plcName);
                var matches = FindMetadataBlocks(plc.BlockGroup, blockName, requestedGroup);
                if (matches.Count == 0)
                    return PlcMutationResult.FailureOutcome(operation, "not_found", plc.Name, blockName, targetPath,
                        "No block with this exact name was found" + (requestedGroup == null ? "." : $" at group path '{requestedGroup}'."));
                if (matches.Count > 1)
                    return PlcMutationResult.FailureOutcome(operation, "ambiguous", plc.Name, blockName, targetPath,
                        "More than one block has this exact name; specify group_path.");

                var location = matches[0];
                var block = location.Block;
                targetPath = AppendPath(location.GroupPath, block.Name);

                if (!(block is OB ob))
                    return PlcMutationResult.FailureOutcome(operation, "invalid_target", plc.Name, block.Name, targetPath,
                        $"Block '{block.Name}' is {GetBlockTypeName(block)}; only OB attributes can be set with this tool.");

                if (block.IsKnowHowProtected)
                    return PlcMutationResult.FailureOutcome(operation, "protected", plc.Name, block.Name, targetPath,
                        $"Block '{block.Name}' is know-how protected.");

                if (name != "PriorityNumber")
                {
                    string? secondaryType = ob.SecondaryType;
                    if (!IsCyclicInterruptSecondaryType(secondaryType))
                        return PlcMutationResult.FailureOutcome(operation, "invalid_target", plc.Name, block.Name, targetPath,
                            $"{name} applies only to cyclic interrupt OBs; OB '{block.Name}' has SecondaryType '{secondaryType}'.");
                }

                EngineeringAttributeInfo? info = block.GetAttributeInfos()
                    .FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
                if (info != null &&
                    (info.AccessMode & EngineeringAttributeAccessMode.Write) != EngineeringAttributeAccessMode.Write)
                {
                    return PlcMutationResult.FailureOutcome(operation, "not_supported", plc.Name, block.Name, targetPath,
                        $"TIA Portal reports attribute '{name}' of OB '{block.Name}' as not writable (AccessMode {info.AccessMode}).");
                }

                long? current = TryReadLongAttribute(block, name);
                if (!current.HasValue)
                    return PlcMutationResult.FailureOutcome(operation, "not_supported", plc.Name, block.Name, targetPath,
                        $"OB '{block.Name}' has no readable attribute '{name}'.");

                bool time = name != "PriorityNumber";
                string Show(long v) => time ? FormatMicroseconds(v) : v.ToString(CultureInfo.InvariantCulture);

                if (expectedApiValue.HasValue && expectedApiValue.Value != current.Value)
                    return PlcMutationResult.FailureOutcome(operation, "conflict", plc.Name, block.Name, targetPath,
                        $"{name} is {Show(current.Value)}, not the expected {Show(expectedApiValue.Value)}; nothing was changed.");

                if (name == "PhaseOffset")
                {
                    long? cyclicTime = TryReadLongAttribute(block, "CyclicTime");
                    if (cyclicTime.HasValue && apiValue >= cyclicTime.Value)
                        return PlcMutationResult.FailureOutcome(operation, "invalid_input", plc.Name, block.Name, targetPath,
                            $"PhaseOffset {Show(apiValue)} must be smaller than CyclicTime {Show(cyclicTime.Value)}.");
                }
                if (name == "CyclicTime")
                {
                    long? phaseOffset = TryReadLongAttribute(block, "PhaseOffset");
                    if (phaseOffset.HasValue && phaseOffset.Value >= apiValue)
                        return PlcMutationResult.FailureOutcome(operation, "invalid_input", plc.Name, block.Name, targetPath,
                            $"CyclicTime {Show(apiValue)} must be greater than the current PhaseOffset {Show(phaseOffset.Value)}; change PhaseOffset first.");
                }

                if (current.Value == apiValue)
                    return PlcMutationResult.CreateOutcome(operation, "unchanged", plc.Name, block.Name, targetPath,
                        $"{name} is already {Show(current.Value)}; nothing was written.", "Int32", Show(current.Value));

                mutationStarted = true;
                block.SetAttribute(name, apiValue);

                long? readBack = TryReadLongAttribute(block, name);
                if (!readBack.HasValue || readBack.Value != apiValue)
                    return PlcMutationResult.VerificationFailureOutcome(operation, plc.Name, block.Name, targetPath,
                        $"{name} read back as {(readBack.HasValue ? Show(readBack.Value) : "unreadable")} after writing {Show(apiValue)}.");

                var result = PlcMutationResult.CreateOutcome(operation, "updated", plc.Name, block.Name, targetPath,
                    $"{name} changed from {Show(current.Value)} to {Show(readBack.Value)}. The project is not compiled or saved; run tia_compile.",
                    "Int32", Show(readBack.Value));
                if (info == null)
                    result.Warnings.Add($"GetAttributeInfos did not list '{name}'; the write was accepted by TIA Portal and verified by read-back.");
                return result;
            }
            catch (PlcMutationRejectedException ex)
            {
                return PlcMutationResult.FailureOutcome(operation, ex.Status, plcName, blockName, targetPath, ex.Message);
            }
            catch (EngineeringException ex)
            {
                return PlcMutationResult.ApiFailureOutcome(operation, plcName, blockName, targetPath, ex, mutationStarted);
            }
            catch (COMException ex)
            {
                return PlcMutationResult.ApiFailureOutcome(operation, plcName, blockName, targetPath, ex, mutationStarted);
            }
        }
    }
}
