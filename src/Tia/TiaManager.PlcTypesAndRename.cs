using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Types;

namespace TiaOpennessMcp.Tia
{
    // Delete and rename of PLC data types (UDTs) and rename of program blocks, in place, with exact-name
    // resolution, fail-on-conflict and read-back. Scope: the PLC software root tree (PlcSoftware.TypeGroup /
    // BlockGroup); objects inside S7-1500 software units are not searched.
    public partial class TiaManager
    {
        private const string RootTreeScopeNote = "Only the PLC software's own type and block folders are searched; objects inside software units are not.";

        public PlcMutationResult DeletePlcType(string? plcName, string typeName, string? groupPath = null)
        {
            const string operation = "delete_plc_type";
            bool mutationStarted = false;
            try
            {
                string? nameError = PlcBlockToolLogic.CheckLookupName(typeName, "type_name");
                if (nameError != null)
                    return PlcMutationResult.FailureOutcome(operation, "invalid_input", plcName, typeName, groupPath, nameError);
                string? group = groupPath == null ? null : NormalizePlcMutationGroupPath(groupPath);

                var plc = ResolveMutationPlc(plcName);
                var candidates = CollectPlcTypeLocations(plc.TypeGroup)
                    .Where(location => group == null || string.Equals(location.GroupPath, group, StringComparison.Ordinal))
                    .ToList();
                NameSelection selection = PlcBlockToolLogic.SelectExactName(candidates.Select(location => location.Name).ToList(), typeName);
                if (selection.Status != "ok")
                {
                    string message = selection.Status == "ambiguous"
                        ? $"More than one PLC data type is named '{typeName}'; pass group_path to select one."
                        : NotFoundMessage("PLC data type", typeName, group, selection.Hint,
                            CollectPlcBlockLocations(plc.BlockGroup).Any(block => string.Equals(block.Name, typeName, StringComparison.OrdinalIgnoreCase))
                                ? $"'{typeName}' is a program block; use tia_delete_block."
                                : null);
                    return PlcMutationResult.FailureOutcome(operation, selection.Status, plc.Name, typeName, group, message);
                }

                PlcTypeLocation target = candidates[selection.Index];
                string targetPath = BuildResultPath(target.GroupPath, target.Name);
                mutationStarted = true;
                target.Type.Delete();

                bool stillThere = CollectPlcTypeLocations(plc.TypeGroup).Any(location =>
                    string.Equals(location.GroupPath, target.GroupPath, StringComparison.Ordinal) &&
                    string.Equals(location.Name, target.Name, StringComparison.Ordinal));
                if (stillThere)
                    return PlcMutationResult.VerificationFailureOutcome(operation, plc.Name, target.Name, targetPath,
                        "Delete() returned but the PLC data type is still present on read-back.");

                return PlcMutationResult.CreateOutcome(operation, "deleted", plc.Name, target.Name, targetPath,
                    "PLC data type deleted. Blocks, DBs and other PLC data types that used it become inconsistent until changed; run tia_compile to see them.");
            }
            catch (PlcMutationRejectedException ex)
            {
                return PlcMutationResult.FailureOutcome(operation, ex.Status, plcName, typeName, groupPath, ex.Message);
            }
            catch (EngineeringException ex)
            {
                return PlcMutationResult.ApiFailureOutcome(operation, plcName, typeName, BuildResultPath(groupPath, typeName), ex, mutationStarted);
            }
            catch (COMException ex)
            {
                return PlcMutationResult.ApiFailureOutcome(operation, plcName, typeName, BuildResultPath(groupPath, typeName), ex, mutationStarted);
            }
        }

        public PlcMutationResult RenamePlcType(string? plcName, string typeName, string newName, string? groupPath = null, string? onConflict = null)
        {
            const string operation = "rename_plc_type";
            bool mutationStarted = false;
            try
            {
                string? group = ValidateRenameInput(typeName, "type_name", newName, groupPath, onConflict, out string? inputError);
                if (inputError != null)
                    return PlcMutationResult.FailureOutcome(operation, "invalid_input", plcName, typeName, groupPath, inputError);

                var plc = ResolveMutationPlc(plcName);
                List<PlcTypeLocation> allTypes = CollectPlcTypeLocations(plc.TypeGroup);
                List<PlcBlockLocation> allBlocks = CollectPlcBlockLocations(plc.BlockGroup);
                var candidates = allTypes
                    .Where(location => group == null || string.Equals(location.GroupPath, group, StringComparison.Ordinal))
                    .ToList();
                NameSelection selection = PlcBlockToolLogic.SelectExactName(candidates.Select(location => location.Name).ToList(), typeName);
                if (selection.Status != "ok")
                {
                    string message = selection.Status == "ambiguous"
                        ? $"More than one PLC data type is named '{typeName}'; pass group_path to select one."
                        : NotFoundMessage("PLC data type", typeName, group, selection.Hint,
                            allBlocks.Any(block => string.Equals(block.Name, typeName, StringComparison.OrdinalIgnoreCase))
                                ? $"'{typeName}' is a program block; use tia_rename_block."
                                : null);
                    return PlcMutationResult.FailureOutcome(operation, selection.Status, plc.Name, typeName, group, message);
                }

                PlcTypeLocation target = candidates[selection.Index];
                var scopeNames = allTypes.Select(location => location.Name).Concat(allBlocks.Select(location => location.Name));
                if (PlcBlockToolLogic.HasRenameCollision(scopeNames, target.Name, newName))
                {
                    return PlcMutationResult.FailureOutcome(operation, "collision", plc.Name, typeName,
                        BuildResultPath(target.GroupPath, target.Name),
                        $"A PLC data type or program block named '{newName}' already exists in PLC '{plc.Name}' (names are compared case-insensitively). Nothing was renamed.");
                }

                string oldName = target.Name;
                mutationStarted = true;
                target.Type.Name = newName;

                var readBack = CollectPlcTypeLocations(plc.TypeGroup)
                    .Where(location => string.Equals(location.GroupPath, target.GroupPath, StringComparison.Ordinal))
                    .ToList();
                if (readBack.Count(location => string.Equals(location.Name, newName, StringComparison.Ordinal)) != 1 ||
                    readBack.Any(location => string.Equals(location.Name, oldName, StringComparison.Ordinal)))
                    return PlcMutationResult.VerificationFailureOutcome(operation, plc.Name, newName,
                        BuildResultPath(target.GroupPath, newName), "The renamed PLC data type could not be verified by exact-name read-back.");

                return PlcMutationResult.CreateOutcome(operation, "renamed", plc.Name, newName,
                    BuildResultPath(target.GroupPath, newName),
                    $"PLC data type '{oldName}' renamed to '{newName}' in place. Run tia_compile and check users of the type (blocks, DBs, HMI tags) for references that did not follow the rename.");
            }
            catch (PlcMutationRejectedException ex)
            {
                return PlcMutationResult.FailureOutcome(operation, ex.Status, plcName, typeName, groupPath, ex.Message);
            }
            catch (EngineeringException ex)
            {
                return PlcMutationResult.ApiFailureOutcome(operation, plcName, typeName, BuildResultPath(groupPath, typeName), ex, mutationStarted);
            }
            catch (COMException ex)
            {
                return PlcMutationResult.ApiFailureOutcome(operation, plcName, typeName, BuildResultPath(groupPath, typeName), ex, mutationStarted);
            }
        }

        public PlcMutationResult RenameBlock(string? plcName, string blockName, string newName, string? groupPath = null, string? onConflict = null)
        {
            const string operation = "rename_block";
            bool mutationStarted = false;
            try
            {
                string? group = ValidateRenameInput(blockName, "block_name", newName, groupPath, onConflict, out string? inputError);
                if (inputError != null)
                    return PlcMutationResult.FailureOutcome(operation, "invalid_input", plcName, blockName, groupPath, inputError);

                var plc = ResolveMutationPlc(plcName);
                List<PlcTypeLocation> allTypes = CollectPlcTypeLocations(plc.TypeGroup);
                List<PlcBlockLocation> allBlocks = CollectPlcBlockLocations(plc.BlockGroup);
                var candidates = allBlocks
                    .Where(location => group == null || string.Equals(location.GroupPath, group, StringComparison.Ordinal))
                    .ToList();
                NameSelection selection = PlcBlockToolLogic.SelectExactName(candidates.Select(location => location.Name).ToList(), blockName);
                if (selection.Status != "ok")
                {
                    string message = selection.Status == "ambiguous"
                        ? $"More than one program block is named '{blockName}'; pass group_path to select one."
                        : NotFoundMessage("Program block", blockName, group, selection.Hint,
                            allTypes.Any(type => string.Equals(type.Name, blockName, StringComparison.OrdinalIgnoreCase))
                                ? $"'{blockName}' is a PLC data type (UDT); use tia_rename_plc_type."
                                : null);
                    return PlcMutationResult.FailureOutcome(operation, selection.Status, plc.Name, blockName, group, message);
                }

                PlcBlockLocation target = candidates[selection.Index];
                var scopeNames = allBlocks.Select(location => location.Name).Concat(allTypes.Select(location => location.Name));
                if (PlcBlockToolLogic.HasRenameCollision(scopeNames, target.Name, newName))
                {
                    return PlcMutationResult.FailureOutcome(operation, "collision", plc.Name, blockName,
                        BuildResultPath(target.GroupPath, target.Name),
                        $"A program block or PLC data type named '{newName}' already exists in PLC '{plc.Name}' (names are compared case-insensitively). Nothing was renamed.");
                }

                string oldName = target.Name;
                mutationStarted = true;
                // Rename only; the block number (Number/AutoNumber) is left untouched.
                target.Block.Name = newName;

                var readBack = CollectPlcBlockLocations(plc.BlockGroup)
                    .Where(location => string.Equals(location.GroupPath, target.GroupPath, StringComparison.Ordinal))
                    .ToList();
                if (readBack.Count(location => string.Equals(location.Name, newName, StringComparison.Ordinal)) != 1 ||
                    readBack.Any(location => string.Equals(location.Name, oldName, StringComparison.Ordinal)))
                    return PlcMutationResult.VerificationFailureOutcome(operation, plc.Name, newName,
                        BuildResultPath(target.GroupPath, newName), "The renamed block could not be verified by exact-name read-back.");

                return PlcMutationResult.CreateOutcome(operation, "renamed", plc.Name, newName,
                    BuildResultPath(target.GroupPath, newName),
                    $"Block '{oldName}' renamed to '{newName}' in place; the block number is unchanged. Run tia_compile and check callers, instance DBs and HMI tags for references that did not follow the rename.");
            }
            catch (PlcMutationRejectedException ex)
            {
                return PlcMutationResult.FailureOutcome(operation, ex.Status, plcName, blockName, groupPath, ex.Message);
            }
            catch (EngineeringException ex)
            {
                return PlcMutationResult.ApiFailureOutcome(operation, plcName, blockName, BuildResultPath(groupPath, blockName), ex, mutationStarted);
            }
            catch (COMException ex)
            {
                return PlcMutationResult.ApiFailureOutcome(operation, plcName, blockName, BuildResultPath(groupPath, blockName), ex, mutationStarted);
            }
        }

        /// <summary>Shared input checks for the rename tools. Returns the normalized group path (null = all groups).</summary>
        private static string? ValidateRenameInput(
            string name,
            string nameParameter,
            string newName,
            string? groupPath,
            string? onConflict,
            out string? error)
        {
            error = PlcBlockToolLogic.CheckLookupName(name, nameParameter);
            if (error != null)
                return null;
            if (onConflict != null && !string.Equals(onConflict, "fail", StringComparison.Ordinal))
            {
                error = "on_conflict supports only 'fail'; existing names are never overwritten.";
                return null;
            }
            ValidatePlcMutationName(newName, "new_name");
            if (string.Equals(name, newName, StringComparison.Ordinal))
            {
                error = "new_name is the same as the current name.";
                return null;
            }
            return groupPath == null ? null : NormalizePlcMutationGroupPath(groupPath);
        }

        private static string NotFoundMessage(string kind, string name, string? group, string? caseHint, string? kindHint)
        {
            string where = group == null ? "" : group.Length == 0 ? " in the root folder" : $" in folder '{group}'";
            var parts = new List<string> { $"{kind} '{name}' was not found{where}." };
            if (kindHint != null) parts.Add(kindHint);
            if (caseHint != null) parts.Add(caseHint);
            parts.Add(RootTreeScopeNote);
            return string.Join(" ", parts);
        }

        private static List<PlcTypeLocation> CollectPlcTypeLocations(PlcTypeGroup root)
        {
            var result = new List<PlcTypeLocation>();

            void Walk(PlcTypeGroup group, string path)
            {
                foreach (PlcType type in group.Types)
                    result.Add(new PlcTypeLocation { Type = type, Name = type.Name, GroupPath = path });
                foreach (PlcTypeUserGroup sub in group.Groups)
                    Walk(sub, path.Length == 0 ? sub.Name : $"{path}/{sub.Name}");
            }

            Walk(root, "");
            return result;
        }

        private static List<PlcBlockLocation> CollectPlcBlockLocations(PlcBlockGroup root)
        {
            var result = new List<PlcBlockLocation>();

            void Walk(PlcBlockGroup group, string path)
            {
                foreach (PlcBlock block in group.Blocks)
                    result.Add(new PlcBlockLocation { Block = block, Name = block.Name, GroupPath = path });
                foreach (PlcBlockUserGroup sub in group.Groups)
                    Walk(sub, path.Length == 0 ? sub.Name : $"{path}/{sub.Name}");
            }

            Walk(root, "");
            return result;
        }

        private sealed class PlcTypeLocation
        {
            public PlcType Type { get; set; } = null!;
            public string Name { get; set; } = "";
            public string GroupPath { get; set; } = "";
        }

        private sealed class PlcBlockLocation
        {
            public PlcBlock Block { get; set; } = null!;
            public string Name { get; set; } = "";
            public string GroupPath { get; set; } = "";
        }
    }
}
