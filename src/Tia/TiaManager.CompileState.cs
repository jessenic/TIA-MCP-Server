using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Types;

namespace TiaOpennessMcp.Tia
{
    // Compile state without compiling (tia_get_compile_state) and the "everything was up to date" note of tia_compile.
    // TIA Portal Openness V21 compiles incrementally (ICompilable.Compile() has no options) and has no API that returns
    // the warnings TIA stored from an earlier compile.
    public partial class TiaManager
    {
        internal const string CompileUpToDateNote =
            "TIA Portal compiled no block because all blocks were already up to date, so warnings from an earlier compile are not repeated. " +
            "TIA Portal Openness V21 has no rebuild-all option (ICompilable.Compile() takes no parameters) and cannot read stored compile warnings. " +
            "Use tia_get_compile_state to see block consistency and compile dates, or run Compile > Software (rebuild all blocks) in TIA Portal to see all warnings again.";

        internal const string CompileStateNote =
            "Read-only; nothing was compiled. Inconsistent blocks are compiled by the next tia_compile. " +
            "Openness cannot read the warnings TIA stored from an earlier compile; to see the warnings of consistent blocks run Compile > Software (rebuild all blocks) in TIA Portal.";

        // Up to date = TIA compiled no block. Structural check first (no block's CompileDate changed during the compile);
        // the English 'up-to-date' text is only a fallback when the compile dates could not be read.
        internal static bool IsCompileUpToDate(
            string? state,
            int errorCount,
            int warningCount,
            int? changedBlockCount,
            IEnumerable<string?>? descriptions)
        {
            if (errorCount > 0 || warningCount > 0)
                return false;
            if (!string.Equals(state, "Success", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(state, "Information", StringComparison.OrdinalIgnoreCase))
                return false;
            if (changedBlockCount.HasValue)
                return changedBlockCount.Value == 0;

            return (descriptions ?? Enumerable.Empty<string?>()).Any(text =>
                text != null &&
                (text.IndexOf("up-to-date", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 text.IndexOf("up to date", StringComparison.OrdinalIgnoreCase) >= 0));
        }

        // Number of blocks whose CompileDate is new or different after the compile. Null when no block changed but
        // the CompileDate of at least one block could not be read before or after (unknown, not unchanged), so the
        // caller does not claim "up to date" from dates it never saw.
        internal static int? CountChangedCompileDates(
            IDictionary<string, DateTime?> before,
            IDictionary<string, DateTime?> after)
        {
            int changed = 0;
            bool unknown = false;
            foreach (var entry in after)
            {
                bool known = before.TryGetValue(entry.Key, out DateTime? previous);
                if (entry.Value == null || (known && previous == null))
                {
                    unknown = true;
                    continue;
                }
                if (!known || previous != entry.Value)
                    changed++;
            }
            if (changed > 0)
                return changed;
            return unknown ? (int?)null : 0;
        }

        internal static string? FormatCompileStateDate(DateTime? value)
        {
            return value?.ToString("o", CultureInfo.InvariantCulture);
        }

        private static T? TryReadValue<T>(Func<T> read) where T : struct
        {
            try { return read(); }
            catch (Exception ex) when (!(ex is OutOfMemoryException)) { return null; }
        }

        // Block key (group path + name) -> CompileDate. Null when the dates cannot be read.
        private static Dictionary<string, DateTime?>? TrySnapshotCompileDates(PlcSoftware plc)
        {
            try
            {
                var dates = new Dictionary<string, DateTime?>(StringComparer.Ordinal);
                void Walk(PlcBlockGroup group, string path)
                {
                    foreach (PlcBlock block in group.Blocks)
                        dates[path + "/" + block.Name] = TryReadValue(() => block.CompileDate);
                    foreach (PlcBlockUserGroup sub in group.Groups)
                        Walk(sub, path + "/" + sub.Name);
                }
                Walk(plc.BlockGroup, "");
                return dates;
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                return null;
            }
        }

        private static void ApplyCompileUpToDateNote(
            CompilationReport report,
            PlcSoftware plc,
            Dictionary<string, DateTime?>? before)
        {
            int? changed = null;
            if (before != null)
            {
                var after = TrySnapshotCompileDates(plc);
                if (after != null)
                    changed = CountChangedCompileDates(before, after);
            }

            bool upToDate = IsCompileUpToDate(report.State, report.ErrorCount, report.WarningCount, changed,
                report.Messages.Select(message => message.Description));
            report.UpToDate = upToDate;
            if (upToDate)
                report.Note = CompileUpToDateNote;
        }

        public PlcCompileStateResult GetCompileState(
            string? plcName,
            string? groupPath = null,
            bool onlyInconsistent = false,
            bool includeTypes = true)
        {
            ValidateMetadataGroupPath(groupPath);
            var plc = FindMetadataPlc(plcName);

            PlcBlockGroup root = plc.BlockGroup;
            string rootPath = "";
            if (!string.IsNullOrEmpty(groupPath))
            {
                try
                {
                    rootPath = NormalizePlcMutationGroupPath(groupPath);
                    root = ResolveBlockGroupExact(plc.BlockGroup, rootPath);
                }
                catch (PlcMutationRejectedException ex)
                {
                    throw new ArgumentException(ex.Message);
                }
            }

            var result = new PlcCompileStateResult
            {
                PlcName = plc.Name,
                GroupPath = rootPath,
                Note = CompileStateNote
            };

            var blocks = new List<PlcCompileStateBlock>();
            void WalkBlocks(PlcBlockGroup group, string path)
            {
                foreach (PlcBlock block in group.Blocks)
                {
                    blocks.Add(new PlcCompileStateBlock
                    {
                        Name = block.Name,
                        Number = block.Number,
                        BlockType = GetBlockTypeName(block),
                        ProgrammingLanguage = block.ProgrammingLanguage.ToString(),
                        GroupPath = path,
                        IsConsistent = TryReadValue(() => block.IsConsistent),
                        IsKnowHowProtected = TryReadValue(() => block.IsKnowHowProtected),
                        CompileDate = FormatCompileStateDate(TryReadValue(() => block.CompileDate)),
                        ModifiedDate = FormatCompileStateDate(TryReadValue(() => block.ModifiedDate)),
                        CodeModifiedDate = FormatCompileStateDate(TryReadValue(() => block.CodeModifiedDate)),
                        InterfaceModifiedDate = FormatCompileStateDate(TryReadValue(() => block.InterfaceModifiedDate))
                    });
                }
                foreach (PlcBlockUserGroup sub in group.Groups)
                    WalkBlocks(sub, string.IsNullOrEmpty(path) ? sub.Name : $"{path}/{sub.Name}");
            }
            WalkBlocks(root, rootPath);

            result.BlockCount = blocks.Count;
            result.ConsistentBlocks = blocks.Count(b => b.IsConsistent == true);
            result.InconsistentBlocks = blocks.Count(b => b.IsConsistent == false);
            result.UnknownBlocks = blocks.Count(b => b.IsConsistent == null);
            result.Blocks = onlyInconsistent ? blocks.Where(b => b.IsConsistent != true).ToList() : blocks;

            if (includeTypes)
            {
                var types = new List<PlcCompileStateType>();
                void WalkTypes(PlcTypeGroup group, string path)
                {
                    foreach (PlcType type in group.Types)
                    {
                        types.Add(new PlcCompileStateType
                        {
                            Name = type.Name,
                            GroupPath = path,
                            IsConsistent = TryReadValue(() => type.IsConsistent),
                            ModifiedDate = FormatCompileStateDate(TryReadValue(() => type.ModifiedDate))
                        });
                    }
                    foreach (PlcTypeUserGroup sub in group.Groups)
                        WalkTypes(sub, string.IsNullOrEmpty(path) ? sub.Name : $"{path}/{sub.Name}");
                }
                WalkTypes(plc.TypeGroup, "");

                result.TypeCount = types.Count;
                result.InconsistentTypes = types.Count(t => t.IsConsistent == false);
                result.UnknownTypes = types.Count(t => t.IsConsistent == null);
                result.Types = onlyInconsistent ? types.Where(t => t.IsConsistent != true).ToList() : types;
            }

            return result;
        }
    }

    public class PlcCompileStateResult
    {
        public string PlcName { get; set; } = "";
        public string GroupPath { get; set; } = "";
        public int BlockCount { get; set; }
        public int ConsistentBlocks { get; set; }
        public int InconsistentBlocks { get; set; }
        public int UnknownBlocks { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public int? TypeCount { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public int? InconsistentTypes { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public int? UnknownTypes { get; set; }
        public List<PlcCompileStateBlock> Blocks { get; set; } = new List<PlcCompileStateBlock>();
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public List<PlcCompileStateType>? Types { get; set; }
        public string Note { get; set; } = "";
    }

    public class PlcCompileStateBlock
    {
        public string Name { get; set; } = "";
        public int Number { get; set; }
        public string BlockType { get; set; } = "";
        public string ProgrammingLanguage { get; set; } = "";
        public string GroupPath { get; set; } = "";
        public bool? IsConsistent { get; set; }
        public bool? IsKnowHowProtected { get; set; }
        public string? CompileDate { get; set; }
        public string? ModifiedDate { get; set; }
        public string? CodeModifiedDate { get; set; }
        public string? InterfaceModifiedDate { get; set; }
    }

    public class PlcCompileStateType
    {
        public string Name { get; set; } = "";
        public string GroupPath { get; set; } = "";
        public bool? IsConsistent { get; set; }
        public string? ModifiedDate { get; set; }
    }
}
