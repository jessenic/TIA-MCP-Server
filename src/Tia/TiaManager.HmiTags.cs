using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Serialization;
using Siemens.Engineering;
using Siemens.Engineering.CrossReference;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiAlarm;
using Siemens.Engineering.HmiUnified.HmiTags;
using Siemens.Engineering.HmiUnified.LoggingTags;
using Siemens.Engineering.HmiUnified.UI;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.Dynamization;
using Siemens.Engineering.HmiUnified.UI.Dynamization.Script;
using Siemens.Engineering.HmiUnified.UI.Events;
using Siemens.Engineering.HmiUnified.UI.Screens;

namespace TiaOpennessMcp.Tia
{
    // Delete and rename of single Unified HMI tags (hmi_delete_tag, hmi_rename_tag).
    public interface IHmiTagOperations
    {
        HmiTagDeleteResult DeleteHmiTag(
            string tagName,
            string? tableName,
            string? targetName,
            string onReferenced,
            bool dryRun);

        HmiTagRenameResult RenameHmiTag(
            string tagName,
            string newName,
            string? tableName,
            string? targetName,
            string onConflict,
            string scriptReferences,
            bool dryRun);
    }

    public partial class TiaManager : IHmiTagOperations
    {
        private static readonly string[] HmiTagScanNotCovered =
        {
            "Global script modules (HmiSoftware.Scripts): V21 Openness exposes no script code for them.",
            "Faceplate, custom widget and custom web control interface bindings.",
            "Runtime settings (for example exclusive-operation control and status tags).",
            "Scheduler tasks, reports, recipes and user-management settings.",
            "Other HMI devices, library types, and HMI JSON exports saved outside the project."
        };

        public HmiTagDeleteResult DeleteHmiTag(
            string tagName,
            string? tableName,
            string? targetName,
            string onReferenced,
            bool dryRun)
        {
            ValidateHmiName(tagName, nameof(tagName));
            if (tableName != null) ValidateHmiName(tableName, nameof(tableName));
            if (!string.Equals(onReferenced, "fail", StringComparison.Ordinal) &&
                !string.Equals(onReferenced, "ignore", StringComparison.Ordinal))
                throw new ArgumentException("onReferenced must be 'fail' or 'ignore'.", nameof(onReferenced));

            HmiSoftware hmi = FindHmiSoftware(targetName);
            var result = new HmiTagDeleteResult
            {
                TagName = tagName,
                TargetName = hmi.Name,
                DryRun = dryRun,
                NotScanned = HmiTagScanNotCovered.ToList()
            };

            HmiTagLocation? location = ResolveHmiTagForMutation(hmi, tagName, tableName, out string? failureStatus, out string? failureMessage);
            if (location == null)
                return FailDelete(result, failureStatus!, failureMessage!);
            FillDeleteDetails(result, location);

            if (dryRun)
            {
                HmiTagUsageScan preview = ScanHmiTagUsage(hmi, location.Tag, tagName, skipOwnTag: true);
                ApplyScan(result, preview);
                result.Success = true;
                result.Status = "preview";
                result.Message = result.BlockingReferenceCount == 0
                    ? "Dry run: the tag can be deleted; no blocking references were found by the best-effort scan. Nothing was changed."
                    : $"Dry run: {result.BlockingReferenceCount} blocking reference(s) found. With on_referenced='fail' the delete would be refused. Nothing was changed.";
                return result;
            }

            bool mutationStarted = false;
            try
            {
                using (ExclusiveAccess access = _tiaPortal!.ExclusiveAccess("Delete Unified HMI tag"))
                {
                    location = ResolveHmiTagForMutation(hmi, tagName, tableName, out failureStatus, out failureMessage);
                    if (location == null)
                        return FailDelete(result, failureStatus!, failureMessage!);
                    FillDeleteDetails(result, location);

                    HmiTagUsageScan scan = ScanHmiTagUsage(hmi, location.Tag, tagName, skipOwnTag: true);
                    ApplyScan(result, scan);
                    if (result.BlockingReferenceCount > 0 && onReferenced == "fail")
                    {
                        result.Success = false;
                        result.Status = "referenced";
                        result.Message = $"HMI tag '{tagName}' is still referenced ({result.BlockingReferenceCount} blocking reference(s)); nothing was deleted. " +
                            "Remove the references first, or pass on_referenced='ignore' to delete anyway.";
                        return result;
                    }

                    using (Transaction transaction = access.Transaction(_project!, "Delete Unified HMI tag"))
                    {
                        mutationStarted = true;
                        location.Tag.Delete();

                        bool stillPresent = EnumerateHmiTagTables(hmi.TagTables, hmi.TagTableGroups, null)
                            .Any(entry => entry.Table.Tags.Any(tag => string.Equals(tag.Name, tagName, StringComparison.Ordinal)));
                        if (stillPresent || hmi.Tags.Find(tagName) != null)
                            throw new HmiStructureVerificationException(
                                $"Deleted HMI tag '{tagName}' is still found by exact-name read-back.");

                        transaction.CommitOnDispose();
                    }
                }

                result.Success = true;
                result.Status = "deleted";
                result.Message = "HMI tag deleted and verified by exact-name read-back.";
                if (result.BlockingReferenceCount > 0)
                    result.Warnings.Add($"{result.BlockingReferenceCount} reference(s) to '{tagName}' now point to a missing tag; hmi_compile will report them.");
                return result;
            }
            catch (Exception ex) when (mutationStarted && IsHmiTagMutationFailure(ex))
            {
                result.Success = false;
                result.Status = ex is HmiStructureVerificationException ? "verification_failed" : "api_failure";
                result.Partial = true;
                result.Message = $"{ex.GetType().Name}: {ex.Message}";
                result.Warning = "The transaction was not committed, but the API call may have changed project state. Inspect the HMI tag by exact-name read-back before retrying; no rollback is claimed.";
                return result;
            }
        }

        public HmiTagRenameResult RenameHmiTag(
            string tagName,
            string newName,
            string? tableName,
            string? targetName,
            string onConflict,
            string scriptReferences,
            bool dryRun)
        {
            ValidateHmiName(tagName, nameof(tagName));
            ValidateHmiName(newName, nameof(newName));
            HmiTagReferenceText.ValidateNewTagName(newName);
            if (tableName != null) ValidateHmiName(tableName, nameof(tableName));
            RequireFailConflictPolicy(onConflict);
            if (!string.Equals(scriptReferences, "report", StringComparison.Ordinal) &&
                !string.Equals(scriptReferences, "replace", StringComparison.Ordinal))
                throw new ArgumentException("scriptReferences must be 'report' or 'replace'.", nameof(scriptReferences));
            if (string.Equals(tagName, newName, StringComparison.Ordinal))
                throw new ArgumentException("new_name must differ from tag_name.", nameof(newName));

            HmiSoftware hmi = FindHmiSoftware(targetName);
            var result = new HmiTagRenameResult
            {
                OldName = tagName,
                Name = newName,
                TargetName = hmi.Name,
                DryRun = dryRun,
                ScriptReferencesMode = scriptReferences,
                NotScanned = HmiTagScanNotCovered.ToList()
            };

            HmiTagLocation? location = ResolveHmiTagForMutation(hmi, tagName, tableName, out string? failureStatus, out string? failureMessage);
            if (location == null)
                return FailRename(result, failureStatus!, failureMessage!);
            string? conflict = FindHmiTagNameConflict(hmi, location, newName);
            if (conflict != null)
                return FailRename(result, "conflict", conflict);
            FillRenameDetails(result, location);

            if (dryRun)
            {
                HmiTagUsageScan preview = ScanHmiTagUsage(hmi, location.Tag, tagName, skipOwnTag: false);
                ApplyScan(result, preview, newName);
                result.Success = true;
                result.Status = "dry_run";
                result.Message = "Dry run: nothing was changed. StructuredReferences lists the bindings that TIA must update when the tag is renamed; " +
                    "ScriptOccurrences lists the script text hits (Replaceable ones are rewritten only with script_references='replace').";
                return result;
            }

            bool mutationStarted = false;
            try
            {
                using (ExclusiveAccess access = _tiaPortal!.ExclusiveAccess("Rename Unified HMI tag"))
                {
                    location = ResolveHmiTagForMutation(hmi, tagName, tableName, out failureStatus, out failureMessage);
                    if (location == null)
                        return FailRename(result, failureStatus!, failureMessage!);
                    conflict = FindHmiTagNameConflict(hmi, location, newName);
                    if (conflict != null)
                        return FailRename(result, "conflict", conflict);
                    FillRenameDetails(result, location);

                    HmiTagUsageScan scan = ScanHmiTagUsage(hmi, location.Tag, tagName, skipOwnTag: false);
                    ApplyScan(result, scan, newName);
                    string? plcTagBefore = location.Tag.PlcTag;
                    string? connectionBefore = location.Tag.Connection;
                    string tableBefore = location.Table.Name;

                    using (Transaction transaction = access.Transaction(_project!, "Rename Unified HMI tag"))
                    {
                        mutationStarted = true;
                        location.Tag.Name = newName;

                        List<HmiTag> renamed = location.Table.Tags
                            .Where(tag => string.Equals(tag.Name, newName, StringComparison.Ordinal))
                            .ToList();
                        bool oldStillPresent = EnumerateHmiTagTables(hmi.TagTables, hmi.TagTableGroups, null)
                            .Any(entry => entry.Table.Tags.Any(tag => string.Equals(tag.Name, tagName, StringComparison.Ordinal)));
                        if (renamed.Count != 1 || oldStillPresent ||
                            !string.Equals(renamed[0].TagTableName, tableBefore, StringComparison.Ordinal) ||
                            !string.Equals(renamed[0].PlcTag, plcTagBefore, StringComparison.Ordinal) ||
                            !string.Equals(renamed[0].Connection, connectionBefore, StringComparison.Ordinal))
                            throw new HmiStructureVerificationException(
                                $"Renamed HMI tag '{newName}' could not be verified by exact-name read-back in table '{tableBefore}'.");

                        ReadBackStructuredReferences(result, scan, tagName, newName);

                        if (scriptReferences == "replace")
                            ReplaceScriptLiterals(result, scan, tagName, newName);

                        transaction.CommitOnDispose();
                    }
                }

                int stale = result.StructuredReferences.Count(reference => reference.Updated == false);
                result.StaleReferenceCount = stale;
                result.Success = true;
                result.Status = stale == 0 ? "renamed" : "renamed_with_stale_references";
                result.Message = stale == 0
                    ? "HMI tag renamed and verified by read-back; every structured reference found before the rename now reads the new name."
                    : $"HMI tag renamed and verified by read-back, but {stale} structured reference(s) still read the old name (see StructuredReferences with Updated=false). " +
                      "Fix them, for example with hmi_set_tag_dynamization, or rename the tag back.";
                int remainingScripts = result.ScriptOccurrences.Count(occurrence => !occurrence.Replaced);
                if (remainingScripts > 0)
                    result.Warnings.Add($"{remainingScripts} script text occurrence(s) of '{tagName}' were not changed; review them (see ScriptOccurrences with Replaced=false).");
                result.Warnings.Add("The PLC tag is not renamed. HMI JSON exports made before the rename still use the old name; export again before importing.");
                return result;
            }
            catch (Exception ex) when (mutationStarted && IsHmiTagMutationFailure(ex))
            {
                result.Success = false;
                result.Status = ex is HmiStructureVerificationException ? "verification_failed" : "api_failure";
                result.Partial = true;
                result.Message = $"{ex.GetType().Name}: {ex.Message}";
                result.Warning = "The transaction was not committed, but the API call may have changed project state. Inspect the HMI tag by exact-name read-back before retrying; no rollback is claimed.";
                return result;
            }
        }

        private static bool IsHmiTagMutationFailure(Exception ex) =>
            ex is HmiStructureVerificationException || ex is EngineeringException || ex is COMException ||
            ex is InvalidOperationException || ex is ArgumentException || ex is TargetInvocationException;

        private static HmiTagDeleteResult FailDelete(HmiTagDeleteResult result, string status, string message)
        {
            result.Success = false;
            result.Status = status;
            result.Message = message;
            return result;
        }

        private static HmiTagRenameResult FailRename(HmiTagRenameResult result, string status, string message)
        {
            result.Success = false;
            result.Status = status;
            result.Message = message;
            return result;
        }

        private static void FillDeleteDetails(HmiTagDeleteResult result, HmiTagLocation location)
        {
            result.TableName = location.Table.Name;
            result.TableGroup = location.Group;
            result.PlcTag = NullIfBlank(location.Tag.PlcTag);
            result.Connection = NullIfBlank(location.Tag.Connection);
            result.DataType = NullIfBlank(location.Tag.DataType);
            result.MemberCount = location.Tag.Members.Count;
            result.LoggingTagCount = location.Tag.LoggingTags.Count;
        }

        private static void FillRenameDetails(HmiTagRenameResult result, HmiTagLocation location)
        {
            result.TableName = location.Table.Name;
            result.TableGroup = location.Group;
            result.PlcTag = NullIfBlank(location.Tag.PlcTag);
            result.Connection = NullIfBlank(location.Tag.Connection);
            result.LoggingTagsNamedAfterOldName = location.Tag.LoggingTags
                .Where(logging => string.Equals(logging.Name, result.OldName, StringComparison.Ordinal))
                .Select(logging => logging.Name)
                .ToList();
            if (result.LoggingTagsNamedAfterOldName.Count > 0)
                result.Warnings.Add("Logging tags named after the old tag name keep that name; hmi_assign_logging_tags looks logging tags up by tag name.");
        }

        private static string? NullIfBlank(string? value) => string.IsNullOrEmpty(value) ? null : value;

        private static void ApplyScan(HmiTagDeleteResult result, HmiTagUsageScan scan)
        {
            result.References = scan.References;
            result.ScriptOccurrences = scan.ScriptOccurrences;
            result.CrossReferenceServiceAvailable = scan.CrossReferenceServiceAvailable;
            result.Warnings.AddRange(scan.Warnings);
            result.BlockingReferenceCount = scan.References.Count +
                scan.ScriptOccurrences.Count(occurrence => HmiTagReferenceText.IsBlockingKind(occurrence.Kind));
            result.Warnings.Add("The reference scan is best-effort; see NotScanned for the places it does not read.");
        }

        private static void ApplyScan(HmiTagRenameResult result, HmiTagUsageScan scan, string newName)
        {
            result.StructuredReferences = scan.References;
            foreach (HmiTagReference reference in scan.References)
            {
                if (reference.Kind != "cross_reference")
                    reference.ExpectedAfter = HmiTagReferenceText.RewriteTagReference(reference.Value, result.OldName, newName);
            }
            result.ScriptOccurrences = scan.ScriptOccurrences;
            result.CrossReferenceServiceAvailable = scan.CrossReferenceServiceAvailable;
            result.Warnings.AddRange(scan.Warnings);
            result.Warnings.Add("The reference scan is best-effort; see NotScanned for the places it does not read.");
        }

        private static void ReadBackStructuredReferences(HmiTagRenameResult result, HmiTagUsageScan scan, string oldName, string newName)
        {
            foreach (HmiTagReference reference in scan.References)
            {
                if (reference.ReadBack == null) continue;
                string? after;
                try
                {
                    after = reference.ReadBack();
                }
                catch (Exception ex) when (ex is EngineeringException || ex is COMException || ex is InvalidOperationException)
                {
                    result.Warnings.Add($"Could not read back {reference.Owner} {reference.Property}: {ex.Message}");
                    continue;
                }
                reference.ValueAfter = after;
                reference.Updated = string.Equals(after, reference.ExpectedAfter, StringComparison.Ordinal);
            }
        }

        private static void ReplaceScriptLiterals(HmiTagRenameResult result, HmiTagUsageScan scan, string oldName, string newName)
        {
            foreach (HmiScriptSite site in scan.ScriptSites)
            {
                List<HmiScriptTagOccurrence> planned = result.ScriptOccurrences
                    .Where(occurrence => occurrence.SiteKey == site.Key && occurrence.Replaceable)
                    .ToList();
                if (planned.Count == 0) continue;
                if (site.Write == null)
                {
                    result.Warnings.Add($"Script at {site.Owner} {site.Location} is not writable through the V21 Openness API; it was not changed.");
                    continue;
                }

                string current = site.Read() ?? string.Empty;
                string updated = HmiTagReferenceText.ReplaceExactLiterals(current, oldName, newName, out int replaced);
                if (replaced == 0) continue;

                int errorsBefore = GetScriptDiagnostics(site.ScriptObject, "Errors").Count;
                site.Write(updated);
                string? readBack = site.Read();
                if (!string.Equals(readBack, updated, StringComparison.Ordinal))
                    throw new HmiStructureVerificationException(
                        $"Script at {site.Owner} {site.Location} could not be verified by read-back after replacing '{oldName}'.");
                List<string> errorsAfter = GetScriptDiagnostics(site.ScriptObject, "Errors");
                if (errorsAfter.Count > errorsBefore)
                    throw new HmiStructureVerificationException(
                        $"Replacing '{oldName}' in the script at {site.Owner} {site.Location} added syntax errors: {string.Join("; ", errorsAfter)}");

                foreach (HmiScriptTagOccurrence occurrence in planned)
                    occurrence.Replaced = true;
            }
        }

        private sealed class HmiTagLocation
        {
            public HmiTag Tag { get; set; } = null!;
            public HmiTagTable Table { get; set; } = null!;
            public string? Group { get; set; }
        }

        private static HmiTagLocation? ResolveHmiTagForMutation(
            HmiSoftware hmi,
            string tagName,
            string? tableName,
            out string? failureStatus,
            out string? failureMessage)
        {
            failureStatus = null;
            failureMessage = null;
            var matches = new List<HmiTagLocation>();
            foreach (var entry in EnumerateHmiTagTables(hmi.TagTables, hmi.TagTableGroups, null))
            {
                foreach (HmiTag tag in entry.Table.Tags)
                {
                    if (string.Equals(tag.Name, tagName, StringComparison.Ordinal))
                        matches.Add(new HmiTagLocation { Tag = tag, Table = entry.Table, Group = entry.Group });
                }
            }

            if (matches.Count == 0)
            {
                HmiTag? found = null;
                try { found = hmi.Tags.Find(tagName); }
                catch (EngineeringException) { }
                if (found != null && !(found.Parent is HmiTagTable))
                {
                    failureStatus = "member_tag";
                    failureMessage = $"'{tagName}' is a member of a structured HMI tag; only top-level tags in a tag table are supported.";
                }
                else
                {
                    failureStatus = "not_found";
                    failureMessage = $"HMI tag '{tagName}' was not found by exact name in any tag table of '{hmi.Name}'. Nothing was changed.";
                }
                return null;
            }
            if (matches.Count > 1)
            {
                failureStatus = "ambiguous";
                failureMessage = $"HMI tag name '{tagName}' exists in several tag tables: " +
                    string.Join(", ", matches.Select(match => HmiTagTablePath(match))) + ". Nothing was changed.";
                return null;
            }

            HmiTagLocation location = matches[0];
            if (tableName != null &&
                !string.Equals(location.Table.Name, tableName, StringComparison.Ordinal) &&
                !string.Equals(HmiTagTablePath(location), tableName, StringComparison.Ordinal))
            {
                failureStatus = "table_mismatch";
                failureMessage = $"HMI tag '{tagName}' is in tag table '{HmiTagTablePath(location)}', not in '{tableName}'. Nothing was changed.";
                return null;
            }
            return location;
        }

        private static string HmiTagTablePath(HmiTagLocation location) =>
            location.Group == null ? location.Table.Name : location.Group + "/" + location.Table.Name;

        private static string? FindHmiTagNameConflict(HmiSoftware hmi, HmiTagLocation location, string newName)
        {
            foreach (var entry in EnumerateHmiTagTables(hmi.TagTables, hmi.TagTableGroups, null))
            {
                foreach (HmiTag tag in entry.Table.Tags)
                {
                    if (!string.Equals(tag.Name, newName, StringComparison.OrdinalIgnoreCase)) continue;
                    bool isSameTag = string.Equals(tag.Name, location.Tag.Name, StringComparison.Ordinal) &&
                        string.Equals(entry.Table.Name, location.Table.Name, StringComparison.Ordinal) &&
                        string.Equals(entry.Group, location.Group, StringComparison.Ordinal);
                    if (!isSameTag)
                        return $"An HMI tag named '{tag.Name}' already exists in tag table '{entry.Table.Name}'. Nothing was changed.";
                }
            }
            foreach (HmiSystemTag systemTag in hmi.SystemTags)
            {
                if (string.Equals(systemTag.Name, newName, StringComparison.OrdinalIgnoreCase))
                    return $"'{newName}' is the name of an HMI system tag. Nothing was changed.";
            }
            return null;
        }

        private sealed class HmiTagUsageScan
        {
            public List<HmiTagReference> References { get; } = new List<HmiTagReference>();
            public List<HmiScriptTagOccurrence> ScriptOccurrences { get; } = new List<HmiScriptTagOccurrence>();
            public List<HmiScriptSite> ScriptSites { get; } = new List<HmiScriptSite>();
            public List<string> Warnings { get; } = new List<string>();
            public bool? CrossReferenceServiceAvailable { get; set; }
        }

        private sealed class HmiScriptSite
        {
            public string Key { get; set; } = string.Empty;
            public string Owner { get; set; } = string.Empty;
            public string Location { get; set; } = string.Empty;
            public Func<string?> Read { get; set; } = () => null;
            public Action<string>? Write { get; set; }
            public object? ScriptObject { get; set; }
        }

        // Read-only, best-effort scan of the places in one HMI software that can name a tag.
        private static HmiTagUsageScan ScanHmiTagUsage(HmiSoftware hmi, HmiTag subject, string tagName, bool skipOwnTag)
        {
            var scan = new HmiTagUsageScan();
            var tagNames = new HashSet<string>(StringComparer.Ordinal);
            var tagLocations = EnumerateHmiTagTables(hmi.TagTables, hmi.TagTableGroups, null).ToList();
            foreach (var entry in tagLocations)
                foreach (HmiTag tag in entry.Table.Tags)
                    tagNames.Add(tag.Name);
            Func<string, bool> isOtherTagName = name => !string.Equals(name, tagName, StringComparison.Ordinal) && tagNames.Contains(name);

            void AddReference(string kind, string owner, string property, string? value, Func<string?> readBack)
            {
                if (!HmiTagReferenceText.IsTagReference(value, tagName, isOtherTagName)) return;
                scan.References.Add(new HmiTagReference
                {
                    Kind = kind,
                    Owner = owner,
                    Property = property,
                    Value = value!,
                    ReadBack = readBack
                });
            }

            void RunCategory(string category, Action action)
            {
                try { action(); }
                catch (Exception ex) when (ex is EngineeringException || ex is COMException || ex is InvalidOperationException ||
                    ex is NotSupportedException || ex is TargetInvocationException)
                {
                    scan.Warnings.Add($"Could not scan {category}: {ex.Message}");
                }
            }

            void AddScriptSite(string owner, string location, object? scriptObject, PropertyInfo? codeProperty, object? codeOwner)
            {
                if (codeProperty == null || codeOwner == null) return;
                Func<string?> read = () => codeProperty.GetValue(codeOwner, null) as string;
                string? code = read();
                if (string.IsNullOrEmpty(code)) return;
                HmiScriptScanResult lexed = HmiTagReferenceText.FindScriptOccurrences(code!, tagName);
                if (lexed.Occurrences.Count == 0) return;

                string key = owner + "|" + location;
                bool writable = codeProperty.CanWrite && codeProperty.SetMethod?.IsPublic == true;
                scan.ScriptSites.Add(new HmiScriptSite
                {
                    Key = key,
                    Owner = owner,
                    Location = location,
                    Read = read,
                    Write = writable ? value => codeProperty.SetValue(codeOwner, value, null) : (Action<string>?)null,
                    ScriptObject = scriptObject
                });
                foreach (HmiScriptTextOccurrence occurrence in lexed.Occurrences)
                {
                    scan.ScriptOccurrences.Add(new HmiScriptTagOccurrence
                    {
                        SiteKey = key,
                        Owner = owner,
                        Location = location,
                        Line = occurrence.Line,
                        Column = occurrence.Column,
                        Kind = occurrence.Kind,
                        Snippet = occurrence.Snippet,
                        Replaceable = occurrence.Replaceable && !lexed.Uncertain && writable,
                        Note = lexed.Uncertain ? lexed.UncertainReason : (!writable && occurrence.Replaceable ? "Script code is not writable through the API." : null)
                    });
                }
            }

            void ScanEventHandlers(string owner, object target, string handlersPropertyName, string locationPrefix)
            {
                PropertyInfo? handlersProperty = target.GetType().GetProperty(handlersPropertyName, BindingFlags.Instance | BindingFlags.Public);
                if (!(handlersProperty?.GetValue(target, null) is IEnumerable handlers)) return;
                foreach (object handler in handlers)
                {
                    object? eventType = handler.GetType().GetProperty("EventType")?.GetValue(handler, null);
                    object? propertyName = handler.GetType().GetProperty("PropertyName")?.GetValue(handler, null);
                    object? script = handler.GetType().GetProperty("Script")?.GetValue(handler, null);
                    string location = locationPrefix + ":" + (propertyName == null ? "" : propertyName + ".") + eventType;
                    AddScriptSite(owner, location, script, script?.GetType().GetProperty("ScriptCode"), script);
                }
            }

            void ScanUi(UIBase ui, string owner)
            {
                RunCategory(owner + " dynamizations", () =>
                {
                    foreach (DynamizationBase dynamization in ui.Dynamizations)
                    {
                        string property = dynamization.PropertyName;
                        if (dynamization is TagDynamization tagDynamization)
                        {
                            AddReference("tag_dynamization", owner, property, tagDynamization.Tag, () => tagDynamization.Tag);
                        }
                        else if (dynamization is ResourceListDynamization resourceList)
                        {
                            AddReference("resource_list_dynamization", owner, property, resourceList.Tag, () => resourceList.Tag);
                        }
                        else if (dynamization is TagParameterDynamization tagParameter)
                        {
                            AddReference("tag_parameter_dynamization", owner, property, tagParameter.DynamicTagName, () => tagParameter.DynamicTagName);
                        }
                        else if (dynamization is ScriptDynamization script)
                        {
                            object? triggerTags = script.Trigger?.Tags;
                            if (triggerTags is IEnumerable<string> triggers)
                            {
                                int index = 0;
                                foreach (string trigger in triggers.ToList())
                                {
                                    int position = index++;
                                    AddReference("script_trigger_tag", owner, property + ".Trigger.Tags[" + position + "]", trigger,
                                        () => (script.Trigger?.Tags as IEnumerable<string>)?.ElementAtOrDefault(position));
                                }
                            }
                            else if (triggerTags != null)
                            {
                                scan.Warnings.Add($"Script trigger tags of {owner} {property} have an unsupported shape ({triggerTags.GetType().Name}) and were not scanned.");
                            }
                            AddScriptSite(owner, "dynamization:" + property, null,
                                script.GetType().GetProperty("ScriptCode"), script);
                            AddScriptSite(owner, "dynamization_global_definitions:" + property, null,
                                script.GetType().GetProperty("GlobalDefinitionAreaScriptCode"), script);
                        }
                    }
                });
                RunCategory(owner + " event scripts", () => ScanEventHandlers(owner, ui, "EventHandlers", "event"));
                RunCategory(owner + " property event scripts", () => ScanEventHandlers(owner, ui, "PropertyEventHandlers", "property_event"));
            }

            void ScanParts(HmiScreenItemBase item, string owner)
            {
                foreach (PropertyInfo property in item.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (property.GetIndexParameters().Length > 0 ||
                        !string.Equals(property.PropertyType.Namespace, "Siemens.Engineering.HmiUnified.UI.Parts", StringComparison.Ordinal))
                        continue;
                    object? value = property.GetValue(item, null);
                    if (value == null) continue;
                    IEnumerable<object> parts = value is IEnumerable enumerable && !(value is string)
                        ? enumerable.Cast<object>().ToList()
                        : new List<object> { value };
                    int index = 0;
                    foreach (object part in parts)
                    {
                        PropertyInfo? tagProperty = part.GetType().GetProperty("Tag", BindingFlags.Instance | BindingFlags.Public);
                        int position = index++;
                        if (tagProperty == null || tagProperty.PropertyType != typeof(string)) continue;
                        string label = value is IEnumerable && !(value is string) ? property.Name + "[" + position + "].Tag" : property.Name + ".Tag";
                        AddReference("part_tag", owner, label, tagProperty.GetValue(part, null) as string,
                            () => tagProperty.GetValue(part, null) as string);
                    }
                }
            }

            RunCategory("discrete alarms", () =>
            {
                foreach (HmiDiscreteAlarm alarm in hmi.DiscreteAlarms)
                {
                    string owner = "DiscreteAlarm '" + alarm.Name + "'";
                    AddReference("alarm", owner, "RaisedStateTag", alarm.RaisedStateTag, () => alarm.RaisedStateTag);
                    AddReference("alarm", owner, "AcknowledgmentControlTag", alarm.AcknowledgmentControlTag, () => alarm.AcknowledgmentControlTag);
                    AddReference("alarm", owner, "AcknowledgmentStateTag", alarm.AcknowledgmentStateTag, () => alarm.AcknowledgmentStateTag);
                    AddAlarmParameterTags(scan, owner, () => alarm.AlarmParameterTags, AddReference);
                }
            });
            RunCategory("analog alarms", () =>
            {
                foreach (HmiAnalogAlarm alarm in hmi.AnalogAlarms)
                {
                    string owner = "AnalogAlarm '" + alarm.Name + "'";
                    AddReference("alarm", owner, "RaisedStateTag", alarm.RaisedStateTag, () => alarm.RaisedStateTag);
                    AddAlarmParameterTags(scan, owner, () => alarm.AlarmParameterTags, AddReference);
                }
            });
            RunCategory("HMI tags", () =>
            {
                foreach (var entry in tagLocations)
                {
                    foreach (HmiTag tag in entry.Table.Tags)
                    {
                        bool isSubject = string.Equals(tag.Name, tagName, StringComparison.Ordinal);
                        if (isSubject && skipOwnTag) continue;
                        string owner = "HmiTag '" + tag.Name + "'";
                        AddReference("tag", owner, "ReadHandshakeTag", tag.ReadHandshakeTag, () => tag.ReadHandshakeTag);
                        AddReference("tag", owner, "WriteHandshakeTag", tag.WriteHandshakeTag, () => tag.WriteHandshakeTag);
                        AddReference("tag", owner, "BufferHandshakeTag", tag.BufferHandshakeTag, () => tag.BufferHandshakeTag);
                        if (tag.InitialMinValue != null && tag.InitialMinValue.ValueType == HmiLimitValueType.Tag)
                            AddReference("tag_limit", owner, "InitialMinValue", tag.InitialMinValue.Value as string, () => tag.InitialMinValue.Value as string);
                        if (tag.InitialMaxValue != null && tag.InitialMaxValue.ValueType == HmiLimitValueType.Tag)
                            AddReference("tag_limit", owner, "InitialMaxValue", tag.InitialMaxValue.Value as string, () => tag.InitialMaxValue.Value as string);
                        foreach (HmiLoggingTag logging in tag.LoggingTags)
                        {
                            HmiLoggingTag current = logging;
                            AddReference("logging_tag_trigger", owner + " LoggingTag '" + current.Name + "'", "TriggerTag", current.TriggerTag, () => current.TriggerTag);
                        }
                    }
                }
            });
            RunCategory("screens", () =>
            {
                foreach (HmiJsonScreenLocation location in EnumerateHmiScreenLocations(hmi.Screens, hmi.ScreenGroups, null))
                {
                    HmiScreen screen = location.Screen;
                    string screenPath = location.Group == null ? screen.Name : location.Group + "/" + screen.Name;
                    ScanUi(screen, "Screen '" + screenPath + "'");
                    foreach (HmiScreenItemBase item in screen.ScreenItems)
                    {
                        string owner = "Screen '" + screenPath + "' item '" + item.Name + "'";
                        if (item is UIBase ui) ScanUi(ui, owner);
                        RunCategory(owner + " parts", () => ScanParts(item, owner));
                    }
                }
            });

            RunCategory("TIA cross-references", () =>
            {
                CrossReferenceService? service = subject.GetService<CrossReferenceService>();
                scan.CrossReferenceServiceAvailable = service != null;
                if (service == null) return;
                CrossReferenceResult crossReferences = service.GetCrossReferences(CrossReferenceFilter.ObjectsWithReferences);
                void Walk(SourceObject source)
                {
                    foreach (ReferenceObject reference in source.References)
                    {
                        foreach (Location location in reference.Locations)
                        {
                            // Only "used by" relations are references to this tag; "uses" covers e.g. its PLC tag.
                            if (location.ReferenceType != ReferenceType.UsedBy) continue;
                            scan.References.Add(new HmiTagReference
                            {
                                Kind = "cross_reference",
                                Owner = string.IsNullOrEmpty(reference.Path) ? reference.Name : reference.Path,
                                Property = location.ReferenceLocation ?? location.Name ?? string.Empty,
                                Value = string.IsNullOrEmpty(location.ReferencedAsName) ? tagName : location.ReferencedAsName
                            });
                        }
                    }
                    foreach (SourceObject child in source.Children) Walk(child);
                }
                foreach (SourceObject source in crossReferences.Sources) Walk(source);
            });

            return scan;
        }

        private static void AddAlarmParameterTags(
            HmiTagUsageScan scan,
            string owner,
            Func<object?> getParameterTags,
            Action<string, string, string, string?, Func<string?>> addReference)
        {
            object? parameterTags = getParameterTags();
            if (parameterTags == null) return;
            if (parameterTags is string single)
            {
                addReference("alarm", owner, "AlarmParameterTags", single, () => getParameterTags() as string);
                return;
            }
            if (parameterTags is IEnumerable values)
            {
                int index = 0;
                foreach (object? value in values)
                {
                    int position = index++;
                    if (value is string text)
                        addReference("alarm", owner, "AlarmParameterTags[" + position + "]", text,
                            () => (getParameterTags() as IEnumerable)?.Cast<object?>().ElementAtOrDefault(position) as string);
                }
                return;
            }
            scan.Warnings.Add($"Alarm parameter tags of {owner} have an unsupported shape ({parameterTags.GetType().Name}) and were not scanned.");
        }
    }

    public sealed class HmiTagReference
    {
        public string Kind { get; set; } = string.Empty;
        public string Owner { get; set; } = string.Empty;
        public string Property { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string? ExpectedAfter { get; set; }
        public string? ValueAfter { get; set; }
        public bool? Updated { get; set; }

        [JsonIgnore]
        internal Func<string?>? ReadBack { get; set; }
    }

    public sealed class HmiScriptTagOccurrence
    {
        [JsonIgnore]
        internal string SiteKey { get; set; } = string.Empty;
        public string Owner { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public int Line { get; set; }
        public int Column { get; set; }
        public string Kind { get; set; } = string.Empty;
        public string Snippet { get; set; } = string.Empty;
        public bool Replaceable { get; set; }
        public bool Replaced { get; set; }
        public string? Note { get; set; }
    }

    public sealed class HmiTagDeleteResult
    {
        public bool Success { get; set; }
        public string Operation { get; set; } = "delete_hmi_tag";
        public string Status { get; set; } = string.Empty;
        public bool Partial { get; set; }
        public bool DryRun { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Warning { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
        public string TagName { get; set; } = string.Empty;
        public string? TableName { get; set; }
        public string? TableGroup { get; set; }
        public string? TargetName { get; set; }
        public string? PlcTag { get; set; }
        public string? Connection { get; set; }
        public string? DataType { get; set; }
        public int? MemberCount { get; set; }
        public int? LoggingTagCount { get; set; }
        public int BlockingReferenceCount { get; set; }
        public List<HmiTagReference> References { get; set; } = new List<HmiTagReference>();
        public List<HmiScriptTagOccurrence> ScriptOccurrences { get; set; } = new List<HmiScriptTagOccurrence>();
        public bool? CrossReferenceServiceAvailable { get; set; }
        public List<string> NotScanned { get; set; } = new List<string>();
    }

    public sealed class HmiTagRenameResult
    {
        public bool Success { get; set; }
        public string Operation { get; set; } = "rename_hmi_tag";
        public string Status { get; set; } = string.Empty;
        public bool Partial { get; set; }
        public bool DryRun { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Warning { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
        public string OldName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? TableName { get; set; }
        public string? TableGroup { get; set; }
        public string? TargetName { get; set; }
        public string? PlcTag { get; set; }
        public string? Connection { get; set; }
        public string ScriptReferencesMode { get; set; } = "report";
        public int StaleReferenceCount { get; set; }
        public List<HmiTagReference> StructuredReferences { get; set; } = new List<HmiTagReference>();
        public List<HmiScriptTagOccurrence> ScriptOccurrences { get; set; } = new List<HmiScriptTagOccurrence>();
        public List<string> LoggingTagsNamedAfterOldName { get; set; } = new List<string>();
        public bool? CrossReferenceServiceAvailable { get; set; }
        public List<string> NotScanned { get; set; } = new List<string>();
    }

    public sealed class HmiScriptTextOccurrence
    {
        public int Index { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        public string Kind { get; set; } = string.Empty;
        public bool Replaceable { get; set; }
        public string Snippet { get; set; } = string.Empty;
    }

    public sealed class HmiScriptScanResult
    {
        public List<HmiScriptTextOccurrence> Occurrences { get; } = new List<HmiScriptTextOccurrence>();
        public bool Uncertain { get; set; }
        public string? UncertainReason { get; set; }
    }

    // Pure text helpers for HMI tag references; no TIA access, used by the offline tests.
    public static class HmiTagReferenceText
    {
        public const string ExactLiteral = "exact_literal";
        public const string LiteralMember = "literal_member";
        public const string LiteralOther = "literal_other";
        public const string TemplateLiteral = "template";
        public const string Comment = "comment";
        public const string Code = "code";

        private const string ForbiddenNewNameCharacters = ".[]{}\"'`\\:";

        public static void ValidateNewTagName(string newName)
        {
            if (newName == null) throw new ArgumentNullException(nameof(newName));
            int index = newName.IndexOfAny(ForbiddenNewNameCharacters.ToCharArray());
            if (index >= 0)
                throw new ArgumentException(
                    $"new_name must not contain '{newName[index]}'. The characters . [ ] {{ }} \" ' ` \\ : are reserved for member, array and script addressing.",
                    nameof(newName));
        }

        // True when a structured reference names the tag itself, or a member or element of it
        // ('Tag.Member', 'Tag[2]', 'Tag{2}'). A dotted reference that is itself (or starts with) another
        // existing tag's name refers to that other tag instead.
        public static bool IsTagReference(string? reference, string tagName, Func<string, bool>? isOtherTagName)
        {
            if (string.IsNullOrEmpty(reference) || string.IsNullOrEmpty(tagName)) return false;
            if (string.Equals(reference, tagName, StringComparison.Ordinal)) return true;
            if (reference!.Length <= tagName.Length || !reference.StartsWith(tagName, StringComparison.Ordinal)) return false;
            char separator = reference[tagName.Length];
            if (separator != '.' && separator != '[' && separator != '{') return false;
            if (isOtherTagName == null) return true;
            if (isOtherTagName(reference)) return false;
            for (int i = tagName.Length + 1; i < reference.Length; i++)
            {
                char c = reference[i];
                if ((c == '.' || c == '[' || c == '{') && isOtherTagName(reference.Substring(0, i)))
                    return false;
            }
            return true;
        }

        public static string RewriteTagReference(string reference, string oldName, string newName)
        {
            if (string.Equals(reference, oldName, StringComparison.Ordinal)) return newName;
            if (reference != null && reference.StartsWith(oldName, StringComparison.Ordinal) && reference.Length > oldName.Length)
                return newName + reference.Substring(oldName.Length);
            return reference ?? string.Empty;
        }

        public static bool IsBlockingKind(string kind) =>
            kind == ExactLiteral || kind == LiteralMember || kind == TemplateLiteral;

        public static bool ScriptMentionsTag(string? code, string tagName) =>
            !string.IsNullOrEmpty(code) && FindScriptOccurrences(code!, tagName).Occurrences.Count > 0;

        // Small JavaScript lexer: finds the tag name in string literals, template literals, comments and
        // identifiers. Only a string literal whose whole content equals the tag name is replaceable.
        // Unterminated literals or comments and regular-expression literals make the result uncertain.
        public static HmiScriptScanResult FindScriptOccurrences(string code, string tagName)
        {
            var result = new HmiScriptScanResult();
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(tagName)) return result;

            int i = 0;
            char lastSignificant = '\0';
            string lastWord = string.Empty;
            while (i < code.Length)
            {
                char c = code[i];
                char next = i + 1 < code.Length ? code[i + 1] : '\0';
                if (c == '/' && next == '/')
                {
                    int end = code.IndexOf('\n', i);
                    if (end < 0) end = code.Length;
                    AddWordOccurrences(result, code, i + 2, end, tagName, Comment);
                    i = end;
                    continue;
                }
                if (c == '/' && next == '*')
                {
                    int end = code.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        MarkUncertain(result, "Unterminated block comment.");
                        end = code.Length;
                        AddWordOccurrences(result, code, i + 2, end, tagName, Comment);
                        i = end;
                    }
                    else
                    {
                        AddWordOccurrences(result, code, i + 2, end, tagName, Comment);
                        i = end + 2;
                    }
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    int start = i + 1;
                    int j = start;
                    bool terminated = false;
                    while (j < code.Length)
                    {
                        if (code[j] == '\\') { j += 2; continue; }
                        if (code[j] == c) { terminated = true; break; }
                        if (code[j] == '\n') break;
                        j++;
                    }
                    if (j > code.Length) j = code.Length;
                    if (!terminated)
                    {
                        MarkUncertain(result, "Unterminated string literal.");
                        AddWordOccurrences(result, code, start, j, tagName, LiteralOther);
                        i = j;
                    }
                    else
                    {
                        string content = code.Substring(start, j - start);
                        if (string.Equals(content, tagName, StringComparison.Ordinal))
                        {
                            Add(result, code, start, ExactLiteral, true);
                        }
                        else if (content.Length > tagName.Length && content.StartsWith(tagName, StringComparison.Ordinal) &&
                                 (content[tagName.Length] == '.' || content[tagName.Length] == '[' || content[tagName.Length] == '{'))
                        {
                            Add(result, code, start, LiteralMember, false);
                        }
                        else
                        {
                            AddWordOccurrences(result, code, start, j, tagName, LiteralOther);
                        }
                        i = j + 1;
                    }
                    lastSignificant = c;
                    lastWord = string.Empty;
                    continue;
                }
                if (c == '`')
                {
                    int j = i + 1;
                    int depth = 0;
                    bool terminated = false;
                    while (j < code.Length)
                    {
                        char t = code[j];
                        if (t == '\\') { j += 2; continue; }
                        if (depth == 0 && t == '`') { terminated = true; break; }
                        if (t == '$' && j + 1 < code.Length && code[j + 1] == '{') { depth++; j += 2; continue; }
                        if (depth > 0 && t == '{') depth++;
                        else if (depth > 0 && t == '}') depth--;
                        j++;
                    }
                    if (j > code.Length) j = code.Length;
                    if (!terminated) MarkUncertain(result, "Unterminated template literal.");
                    AddWordOccurrences(result, code, i + 1, j, tagName, TemplateLiteral);
                    i = terminated ? j + 1 : j;
                    lastSignificant = '`';
                    lastWord = string.Empty;
                    continue;
                }
                if (c == '/')
                {
                    if (RegexAllowed(lastSignificant, lastWord))
                    {
                        MarkUncertain(result, "The script contains a regular-expression literal; literal replacement is disabled for it.");
                        int j = i + 1;
                        bool inClass = false;
                        while (j < code.Length && code[j] != '\n')
                        {
                            if (code[j] == '\\') { j += 2; continue; }
                            if (code[j] == '[') inClass = true;
                            else if (code[j] == ']') inClass = false;
                            else if (code[j] == '/' && !inClass) break;
                            j++;
                        }
                        if (j > code.Length) j = code.Length;
                        AddWordOccurrences(result, code, i + 1, j, tagName, LiteralOther);
                        i = j < code.Length ? j + 1 : j;
                        lastSignificant = '/';
                        lastWord = string.Empty;
                        continue;
                    }
                    lastSignificant = '/';
                    lastWord = string.Empty;
                    i++;
                    continue;
                }
                if (IsIdentifierChar(c) || c == '$')
                {
                    int start = i;
                    while (i < code.Length && (IsIdentifierChar(code[i]) || code[i] == '$')) i++;
                    string word = code.Substring(start, i - start);
                    if (string.Equals(word, tagName, StringComparison.Ordinal))
                        Add(result, code, start, Code, false);
                    lastWord = word;
                    lastSignificant = 'a';
                    continue;
                }
                if (!char.IsWhiteSpace(c))
                {
                    lastSignificant = c;
                    lastWord = string.Empty;
                }
                i++;
            }
            return result;
        }

        public static string ReplaceExactLiterals(string code, string oldName, string newName, out int replaced)
        {
            replaced = 0;
            if (string.IsNullOrEmpty(code)) return code ?? string.Empty;
            HmiScriptScanResult scan = FindScriptOccurrences(code, oldName);
            if (scan.Uncertain) return code;
            var builder = new StringBuilder(code);
            foreach (HmiScriptTextOccurrence occurrence in scan.Occurrences
                .Where(occurrence => occurrence.Replaceable)
                .OrderByDescending(occurrence => occurrence.Index))
            {
                builder.Remove(occurrence.Index, oldName.Length);
                builder.Insert(occurrence.Index, newName);
                replaced++;
            }
            return builder.ToString();
        }

        private static bool RegexAllowed(char lastSignificant, string lastWord)
        {
            if (lastWord.Length > 0)
                return lastWord == "return" || lastWord == "typeof" || lastWord == "case" || lastWord == "in" ||
                    lastWord == "of" || lastWord == "delete" || lastWord == "void" || lastWord == "throw" || lastWord == "new";
            if (lastSignificant == '\0') return true;
            return "(,=:[!&|?{};+-*%<>~^".IndexOf(lastSignificant) >= 0;
        }

        private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        private static void MarkUncertain(HmiScriptScanResult result, string reason)
        {
            if (!result.Uncertain)
            {
                result.Uncertain = true;
                result.UncertainReason = reason;
            }
        }

        private static void AddWordOccurrences(HmiScriptScanResult result, string code, int start, int end, string tagName, string kind)
        {
            int index = start;
            while (index < end)
            {
                int found = code.IndexOf(tagName, index, end - index, StringComparison.Ordinal);
                if (found < 0 || found + tagName.Length > end) break;
                bool startBoundary = found == 0 || !IsIdentifierChar(code[found - 1]) || !IsIdentifierChar(tagName[0]);
                int after = found + tagName.Length;
                bool endBoundary = after >= code.Length || !IsIdentifierChar(code[after]) || !IsIdentifierChar(tagName[tagName.Length - 1]);
                if (startBoundary && endBoundary)
                    Add(result, code, found, kind, false);
                index = found + Math.Max(1, tagName.Length);
            }
        }

        private static void Add(HmiScriptScanResult result, string code, int index, string kind, bool replaceable)
        {
            int line = 1;
            int lineStart = 0;
            for (int i = 0; i < index && i < code.Length; i++)
            {
                if (code[i] == '\n')
                {
                    line++;
                    lineStart = i + 1;
                }
            }
            int lineEnd = code.IndexOf('\n', lineStart);
            if (lineEnd < 0) lineEnd = code.Length;
            string snippet = code.Substring(lineStart, lineEnd - lineStart).Trim();
            if (snippet.Length > 160) snippet = snippet.Substring(0, 157) + "...";
            result.Occurrences.Add(new HmiScriptTextOccurrence
            {
                Index = index,
                Line = line,
                Column = index - lineStart + 1,
                Kind = kind,
                Replaceable = replaceable,
                Snippet = snippet
            });
        }
    }
}
