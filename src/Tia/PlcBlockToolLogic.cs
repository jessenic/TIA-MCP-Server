using System;
using System.Collections.Generic;
using System.Linq;

namespace TiaOpennessMcp.Tia
{
    /// <summary>
    /// Pure decision helpers for the PLC block and data-type tools (no Siemens types, testable offline).
    /// </summary>
    public static class PlcBlockToolLogic
    {
        /// <summary>
        /// Exact (Ordinal) name selection. Returns "ok" with the index of the single exact match,
        /// "ambiguous" for several exact matches, or "not_found". When there is no exact match but a
        /// case-insensitive one exists, the result is still "not_found" and Hint names the candidate(s).
        /// </summary>
        public static NameSelection SelectExactName(IReadOnlyList<string> names, string requested)
        {
            var exact = new List<int>();
            var caseInsensitive = new List<string>();
            for (int i = 0; i < names.Count; i++)
            {
                if (string.Equals(names[i], requested, StringComparison.Ordinal))
                    exact.Add(i);
                else if (string.Equals(names[i], requested, StringComparison.OrdinalIgnoreCase))
                    caseInsensitive.Add(names[i]);
            }

            if (exact.Count == 1)
                return new NameSelection { Status = "ok", Index = exact[0] };
            if (exact.Count > 1)
                return new NameSelection { Status = "ambiguous", Index = -1 };

            return new NameSelection
            {
                Status = "not_found",
                Index = -1,
                Hint = caseInsensitive.Count == 0
                    ? null
                    : $"Names are matched exactly (case-sensitive). Did you mean {string.Join(", ", caseInsensitive.Distinct().Select(name => $"'{name}'"))}?"
            };
        }

        /// <summary>
        /// True when newName (case-insensitive) is already used by another object. existingNames holds every
        /// name in the collision scope, including the object being renamed (exact oldName), which is skipped once.
        /// A case-only rename of the same object is therefore no collision.
        /// </summary>
        public static bool HasRenameCollision(IEnumerable<string> existingNames, string oldName, string newName)
        {
            bool selfSkipped = false;
            foreach (string name in existingNames)
            {
                if (!selfSkipped && string.Equals(name, oldName, StringComparison.Ordinal))
                {
                    selfSkipped = true;
                    continue;
                }
                if (string.Equals(name, newName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Lenient check for names that address an existing object: only null, empty, or whitespace-padded
        /// names are rejected, so any object TIA accepted can still be found. Returns an error text or null.
        /// </summary>
        public static string? CheckLookupName(string? name, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(name) || !string.Equals(name, name!.Trim(), StringComparison.Ordinal))
                return $"'{parameterName}' must be non-empty and have no leading/trailing whitespace.";
            return null;
        }

        /// <summary>
        /// Decides what tia_import_scl_block does with an existing same-name block before replacing it.
        /// compileAttempted: a single-block compile ran because the block was inconsistent.
        /// A null consistency value (attribute unreadable) is treated as consistent.
        /// </summary>
        public static SclBackupDecision DecideSclBackup(
            bool existingBlock,
            bool? knowHowProtected,
            bool? consistentBefore,
            bool compileAttempted,
            bool? consistentAfterCompile,
            bool backupRequested,
            string blockName)
        {
            if (!existingBlock)
                return new SclBackupDecision { Action = SclBackupAction.None };

            if (!backupRequested)
            {
                return new SclBackupDecision
                {
                    Action = SclBackupAction.SkipWithWarning,
                    Message = $"No backup of the existing block '{blockName}' was taken (backup:false). If the import fails after the old block is deleted, the block cannot be restored and is missing from the project."
                };
            }

            if (knowHowProtected == true)
            {
                return new SclBackupDecision
                {
                    Action = SclBackupAction.FailNotExportable,
                    Message = $"The existing block '{blockName}' is know-how protected and cannot be exported as a backup; compiling does not change that. Nothing was changed. To replace it without a backup, call tia_import_scl_block again with backup:false (a failed import then cannot restore the old block)."
                };
            }

            if (consistentBefore != false)
                return new SclBackupDecision { Action = SclBackupAction.Backup };

            if (compileAttempted && consistentAfterCompile != false)
            {
                return new SclBackupDecision
                {
                    Action = SclBackupAction.Backup,
                    Message = $"The existing block '{blockName}' was inconsistent; it was compiled before the backup and is now consistent."
                };
            }

            string compileText = compileAttempted
                ? "It was compiled once and is still inconsistent (its code probably no longer matches a changed callee or PLC data type interface). The compile is the only change that was made"
                : "TIA Portal did not offer a compile service for the single block, so it could not be made consistent. Nothing was changed";
            return new SclBackupDecision
            {
                Action = SclBackupAction.FailNotExportable,
                Message = $"The existing block '{blockName}' is inconsistent and TIA Portal cannot export inconsistent blocks, so no backup can be taken. {compileText}; the block was not replaced. To replace it without a backup, call tia_import_scl_block again with backup:false (a failed import then cannot restore the old block), or fix the callee/UDT and run tia_compile first."
            };
        }
    }

    public class NameSelection
    {
        public string Status { get; set; } = "";
        public int Index { get; set; } = -1;
        public string? Hint { get; set; }
    }

    public enum SclBackupAction
    {
        None,
        Backup,
        SkipWithWarning,
        FailNotExportable
    }

    public class SclBackupDecision
    {
        public SclBackupAction Action { get; set; }
        public string? Message { get; set; }
    }
}
