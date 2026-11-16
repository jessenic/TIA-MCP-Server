using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiTags;
using Siemens.Engineering.HmiUnified.UI.Screens;
using Siemens.Engineering.HmiUnified.UI.ScreenGroup;

namespace TiaOpennessMcp.Tia
{
    public partial class TiaManager
    {
        private const string HmiStructureConflictPolicy = "fail";

        public HmiStructureMutationResult CreateHmiScreenGroup(
            string groupName,
            string? parentGroupPath,
            string? targetName,
            string onConflict)
        {
            ValidateGroupName(groupName, nameof(groupName));
            RequireFailConflictPolicy(onConflict);

            HmiSoftware hmi = FindHmiSoftware(targetName);
            string parentPath = NormalizeHmiScreenGroupPath(parentGroupPath);
            HmiScreenGroupComposition siblings = FindGroupComposition(hmi, parentPath);
            EnsureNoGroupNameCollision(siblings, groupName);

            return RunHmiStructureMutation("create_screen_group", "created", groupName, parentPath, null, () =>
            {
                HmiScreenGroup created = siblings.Create(groupName);
                HmiScreenGroup? readBack = siblings.Find(groupName);
                if (readBack == null ||
                    !string.Equals(created.Name, groupName, StringComparison.Ordinal) ||
                    !string.Equals(readBack.Name, created.Name, StringComparison.Ordinal))
                    throw new HmiStructureVerificationException($"Created screen group '{groupName}' could not be verified by read-back.");

                return new HmiStructureMutationResult
                {
                    Operation = "create_screen_group",
                    Name = readBack.Name,
                    ParentGroupPath = parentPath,
                    GroupPath = JoinGroupPath(parentPath, readBack.Name)
                };
            });
        }

        public HmiStructureMutationResult CreateHmiTagTable(
            string targetName,
            string tableName,
            string onConflict)
        {
            ValidateHmiName(tableName, nameof(tableName));
            if (tableName.IndexOf('/') >= 0 || tableName.IndexOf('\\') >= 0)
            {
                throw new ArgumentException("Tag table names cannot contain '/' or '\\'.", nameof(tableName));
            }
            if (!string.Equals(onConflict, HmiStructureConflictPolicy, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("onConflict is required and must be 'fail'; overwriting is not supported.", nameof(onConflict));
            }

            HmiSoftware hmi = FindHmiSoftware(targetName);
            if (!string.Equals(hmi.Name, targetName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("targetName must exactly match an HMI software name.");
            }

            bool mutationStarted = false;
            try
            {
                using (ExclusiveAccess access = _tiaPortal!.ExclusiveAccess("Create Unified HMI tag table"))
                using (Transaction transaction = access.Transaction(_project!, "Create Unified HMI tag table"))
                {
                    if (hmi.TagTables.Any(table =>
                            string.Equals(table.Name, tableName, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new InvalidOperationException($"An HMI tag table named '{tableName}' already exists.");
                    }

                    mutationStarted = true;
                    HmiTagTable created = hmi.TagTables.Create(tableName);
                    List<HmiTagTable> readBack = hmi.TagTables
                        .Where(table => string.Equals(table.Name, tableName, StringComparison.Ordinal))
                        .ToList();
                    if (readBack.Count != 1 ||
                        !string.Equals(created.Name, tableName, StringComparison.Ordinal) ||
                        !string.Equals(readBack[0].Name, created.Name, StringComparison.Ordinal))
                    {
                        throw new HmiStructureVerificationException(
                            $"Created HMI tag table '{tableName}' could not be verified by exact-name read-back.");
                    }

                    transaction.CommitOnDispose();
                }

                return new HmiStructureMutationResult
                {
                    Success = true,
                    Operation = "create_hmi_tag_table",
                    Status = "created",
                    Partial = false,
                    Message = "HMI tag table created and verified by read-back.",
                    Name = tableName
                };
            }
            catch (Exception ex) when (mutationStarted)
            {
                return new HmiStructureMutationResult
                {
                    Success = false,
                    Operation = "create_hmi_tag_table",
                    Status = ex is HmiStructureVerificationException ? "verification_failed" : "api_failure",
                    Partial = true,
                    Message = $"{ex.GetType().Name}: {ex.Message}",
                    Name = tableName,
                    Warning = "The API call may have changed project state. Inspect the HMI tag table by exact-name read-back before retrying; no rollback is claimed."
                };
            }
        }

        public HmiStructureMutationResult CreateHmiScreenUnderGroup(
            string parentGroupPath,
            string screenName,
            uint? width,
            uint? height,
            string? targetName,
            string onConflict)
        {
            ValidateHmiName(screenName, nameof(screenName));
            RequireFailConflictPolicy(onConflict);
            ValidateScreenDimensions(width, height);

            HmiSoftware hmi = FindHmiSoftware(targetName);
            string groupPath = NormalizeHmiScreenGroupPath(parentGroupPath);
            if (groupPath.Length == 0)
            {
                throw new ArgumentException("parentGroupPath must identify a screen group.", nameof(parentGroupPath));
            }

            HmiScreenGroup group = FindGroup(hmi, groupPath)
                ?? throw new InvalidOperationException($"Screen group '{groupPath}' was not found.");
            EnsureNoScreenNameCollision(group.Screens, screenName);

            return RunHmiStructureMutation("create_screen_under_group", "created", screenName, groupPath, null, () =>
            {
                HmiScreen created = group.Screens.Create(screenName);
                if (width.HasValue) created.Width = width.Value;
                if (height.HasValue) created.Height = height.Value;

                HmiScreen? readBack = group.Screens.Find(screenName);
                if (readBack == null ||
                    !string.Equals(created.Name, screenName, StringComparison.Ordinal) ||
                    !string.Equals(readBack.Name, created.Name, StringComparison.Ordinal) ||
                    (width.HasValue && readBack.Width != width.Value) ||
                    (height.HasValue && readBack.Height != height.Value))
                    throw new HmiStructureVerificationException($"Created screen '{screenName}' in group '{groupPath}' could not be verified by read-back.");

                return ToScreenMutationResult("create_screen_under_group", readBack, groupPath);
            });
        }

        public HmiStructureMutationResult RenameHmiScreen(
            string screenName,
            string groupPath,
            string newName,
            string? targetName,
            string onConflict)
        {
            ValidateHmiName(screenName, nameof(screenName));
            ValidateHmiName(newName, nameof(newName));
            RequireFailConflictPolicy(onConflict);

            HmiSoftware hmi = FindHmiSoftware(targetName);
            string exactGroupPath = NormalizeHmiScreenGroupPath(groupPath);
            HmiScreenComposition screens = FindScreenComposition(hmi, exactGroupPath);
            HmiScreen screen = FindUniqueScreen(screens, screenName)
                ?? throw new InvalidOperationException($"Screen '{screenName}' was not found in group path '{exactGroupPath}'.");

            bool collidesWithAnotherScreen = screens.Any(candidate =>
                string.Equals(candidate.Name, newName, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(candidate.Name, screen.Name, StringComparison.Ordinal));
            if (collidesWithAnotherScreen)
            {
                throw new InvalidOperationException($"A screen named '{newName}' already exists in group path '{exactGroupPath}'.");
            }

            string oldName = screen.Name;
            return RunHmiStructureMutation("rename_screen", "renamed", newName, exactGroupPath, oldName, () =>
            {
                screen.Name = newName;
                HmiScreen? readBack = FindUniqueScreen(screens, newName);
                if (readBack == null)
                    throw new HmiStructureVerificationException($"Renamed screen '{newName}' could not be verified by read-back.");

                return ToScreenMutationResult("rename_screen", readBack, exactGroupPath, oldName);
            });
        }

        public HmiStructureMutationResult RenameHmiScreenGroup(
            string groupPath,
            string newName,
            string? targetName,
            string onConflict)
        {
            ValidateGroupName(newName, nameof(newName));
            RequireFailConflictPolicy(onConflict);

            HmiSoftware hmi = FindHmiSoftware(targetName);
            string exactPath = NormalizeHmiScreenGroupPath(groupPath);
            if (exactPath.Length == 0)
            {
                throw new ArgumentException("groupPath must identify a screen group.", nameof(groupPath));
            }

            HmiScreenGroup group = FindGroup(hmi, exactPath)
                ?? throw new InvalidOperationException($"Screen group '{exactPath}' was not found.");
            string parentPath = ParentGroupPath(exactPath);
            HmiScreenGroupComposition siblings = FindGroupComposition(hmi, parentPath);
            bool collidesWithAnotherGroup = siblings.Any(candidate =>
                string.Equals(candidate.Name, newName, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(candidate.Name, group.Name, StringComparison.Ordinal));
            if (collidesWithAnotherGroup)
            {
                throw new InvalidOperationException($"A screen group named '{newName}' already exists under '{parentPath}'.");
            }

            string oldName = group.Name;
            return RunHmiStructureMutation("rename_screen_group", "renamed", newName, parentPath, oldName, () =>
            {
                group.Name = newName;
                HmiScreenGroup? readBack = FindUniqueGroup(siblings, newName);
                if (readBack == null)
                    throw new HmiStructureVerificationException($"Renamed screen group '{newName}' could not be verified by read-back.");

                return new HmiStructureMutationResult
                {
                    Operation = "rename_screen_group",
                    OldName = oldName,
                    Name = readBack.Name,
                    ParentGroupPath = parentPath,
                    GroupPath = JoinGroupPath(parentPath, readBack.Name)
                };
            });
        }

        public HmiStructureMutationResult RenameHmiTagTable(
            string tableName,
            string newName,
            string? targetName,
            string onConflict)
        {
            ValidateHmiName(tableName, nameof(tableName));
            ValidateHmiName(newName, nameof(newName));
            RequireFailConflictPolicy(onConflict);

            HmiSoftware hmi = FindHmiSoftware(targetName);
            List<HmiTagTable> matches = hmi.TagTables
                .Where(table => string.Equals(table.Name, tableName, StringComparison.Ordinal))
                .ToList();
            if (matches.Count == 0)
            {
                throw new InvalidOperationException($"HMI tag table '{tableName}' was not found.");
            }
            if (matches.Count > 1)
            {
                throw new InvalidOperationException($"HMI tag table name '{tableName}' is ambiguous.");
            }

            HmiTagTable tableToRename = matches[0];
            List<HmiTagTable> collisions = hmi.TagTables
                .Where(table => string.Equals(table.Name, newName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (collisions.Count > 1 ||
                (collisions.Count == 1 && !string.Equals(collisions[0].Name, tableToRename.Name, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"An HMI tag table named '{newName}' already exists.");
            }

            string oldName = tableToRename.Name;
            string[] tagNames = tableToRename.Tags.Select(tag => tag.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
            int tagCount = tagNames.Length;
            return RunHmiStructureMutation("rename_hmi_tag_table", "renamed", newName, null, oldName, () =>
            {
                tableToRename.Name = newName;

                List<HmiTagTable> readBack = hmi.TagTables
                    .Where(table => string.Equals(table.Name, newName, StringComparison.Ordinal))
                    .ToList();
                string[] readBackTagNames = readBack.Count == 1
                    ? readBack[0].Tags.Select(tag => tag.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray()
                    : Array.Empty<string>();
                if (readBack.Count != 1 || !string.Equals(readBack[0].Name, newName, StringComparison.Ordinal) ||
                    !tagNames.SequenceEqual(readBackTagNames, StringComparer.Ordinal))
                    throw new HmiStructureVerificationException($"Renamed HMI tag table '{newName}' could not be verified by read-back.");

                return new HmiStructureMutationResult
                {
                    Operation = "rename_hmi_tag_table",
                    OldName = oldName,
                    Name = readBack[0].Name,
                    EntryCount = tagCount
                };
            });
        }

        private static HmiStructureMutationResult RunHmiStructureMutation(
            string operation,
            string successStatus,
            string objectName,
            string? parentGroupPath,
            string? oldName,
            Func<HmiStructureMutationResult> mutation)
        {
            try
            {
                HmiStructureMutationResult result = mutation();
                result.Success = true;
                result.Status = successStatus;
                result.Message = "Mutation completed and verified by read-back.";
                return result;
            }
            catch (HmiStructureVerificationException ex)
            {
                return HmiStructureMutationResult.Failure(operation, objectName, oldName, parentGroupPath, "verification_failed", ex);
            }
            catch (EngineeringException ex)
            {
                return HmiStructureMutationResult.Failure(operation, objectName, oldName, parentGroupPath, "api_failure", ex);
            }
            catch (COMException ex)
            {
                return HmiStructureMutationResult.Failure(operation, objectName, oldName, parentGroupPath, "api_failure", ex);
            }
            catch (InvalidOperationException ex)
            {
                return HmiStructureMutationResult.Failure(operation, objectName, oldName, parentGroupPath, "api_failure", ex);
            }
            catch (ArgumentException ex)
            {
                return HmiStructureMutationResult.Failure(operation, objectName, oldName, parentGroupPath, "api_failure", ex);
            }
        }

        private static void ValidateHmiName(string name, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(name) ||
                !string.Equals(name, name.Trim(), StringComparison.Ordinal) ||
                name.Any(char.IsControl))
            {
                throw new ArgumentException("A name must be non-empty, have no surrounding whitespace, and contain no control characters.", parameterName);
            }
        }

        private static void ValidateGroupName(string name, string parameterName)
        {
            ValidateHmiName(name, parameterName);
            if (name.IndexOf('/') >= 0)
            {
                throw new ArgumentException("Screen group names cannot contain '/' because group paths use slash-separated names.", parameterName);
            }
        }

        private static void ValidateScreenDimensions(uint? width, uint? height)
        {
            if (width.HasValue && width.Value == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "Screen width must be greater than zero.");
            }
            if (height.HasValue && height.Value == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height), "Screen height must be greater than zero.");
            }
        }

        private static void RequireFailConflictPolicy(string onConflict)
        {
            if (!string.Equals(onConflict, HmiStructureConflictPolicy, StringComparison.Ordinal))
            {
                throw new ArgumentException("onConflict is required and must be 'fail'; overwriting is not supported.", nameof(onConflict));
            }
        }

        private static string NormalizeHmiScreenGroupPath(string? groupPath)
        {
            if (groupPath == null || groupPath.Length == 0) return string.Empty;
            if (string.IsNullOrWhiteSpace(groupPath) ||
                groupPath.StartsWith("/", StringComparison.Ordinal) ||
                groupPath.EndsWith("/", StringComparison.Ordinal))
            {
                throw new ArgumentException("Group paths must be empty for the root or exact slash-separated group names.", nameof(groupPath));
            }
            if (groupPath.Split('/').Any(segment => segment.Length == 0))
            {
                throw new ArgumentException("Group paths must contain non-empty slash-separated name segments.", nameof(groupPath));
            }
            return groupPath;
        }

        private static string ParentGroupPath(string groupPath)
        {
            int separator = groupPath.LastIndexOf('/');
            return separator < 0 ? string.Empty : groupPath.Substring(0, separator);
        }

        private static string JoinGroupPath(string parentPath, string name)
        {
            return parentPath.Length == 0 ? name : parentPath + "/" + name;
        }

        private static HmiScreenGroupComposition FindGroupComposition(HmiSoftware hmi, string groupPath)
        {
            if (groupPath.Length == 0) return hmi.ScreenGroups;
            HmiScreenGroup group = FindGroup(hmi, groupPath)
                ?? throw new InvalidOperationException($"Screen group '{groupPath}' was not found.");
            return group.Groups;
        }

        private static HmiScreenComposition FindScreenComposition(HmiSoftware hmi, string groupPath)
        {
            if (groupPath.Length == 0) return hmi.Screens;
            HmiScreenGroup group = FindGroup(hmi, groupPath)
                ?? throw new InvalidOperationException($"Screen group '{groupPath}' was not found.");
            return group.Screens;
        }

        private static HmiScreenGroup? FindGroup(HmiSoftware hmi, string groupPath)
        {
            string[] segments = groupPath.Split('/');
            HmiScreenGroupComposition composition = hmi.ScreenGroups;
            HmiScreenGroup? current = null;
            foreach (string segment in segments)
            {
                current = FindUniqueGroup(composition, segment);
                if (current == null) return null;
                composition = current.Groups;
            }
            return current;
        }

        private static HmiScreenGroup? FindUniqueGroup(HmiScreenGroupComposition groups, string name)
        {
            List<HmiScreenGroup> matches = groups
                .Where(group => string.Equals(group.Name, name, StringComparison.Ordinal))
                .ToList();
            if (matches.Count > 1)
            {
                throw new InvalidOperationException($"Screen group name '{name}' is ambiguous in its parent group.");
            }
            return matches.FirstOrDefault();
        }

        private static HmiScreen? FindUniqueScreen(HmiScreenComposition screens, string name)
        {
            List<HmiScreen> matches = screens
                .Where(screen => string.Equals(screen.Name, name, StringComparison.Ordinal))
                .ToList();
            if (matches.Count > 1)
            {
                throw new InvalidOperationException($"Screen name '{name}' is ambiguous in its parent group.");
            }
            return matches.FirstOrDefault();
        }

        private static void EnsureNoGroupNameCollision(HmiScreenGroupComposition groups, string name)
        {
            if (groups.Any(group => string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"A screen group named '{name}' already exists in this parent group.");
            }
        }

        private static void EnsureNoScreenNameCollision(HmiScreenComposition screens, string name)
        {
            if (screens.Any(screen => string.Equals(screen.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"A screen named '{name}' already exists in this parent group.");
            }
        }

        private static HmiStructureMutationResult ToScreenMutationResult(
            string operation,
            HmiScreen screen,
            string groupPath,
            string? oldName = null)
        {
            return new HmiStructureMutationResult
            {
                Operation = operation,
                OldName = oldName,
                Name = screen.Name,
                ParentGroupPath = groupPath,
                Width = screen.Width,
                Height = screen.Height,
                ItemCount = screen.ScreenItems.Count
            };
        }

        private sealed class HmiStructureVerificationException : Exception
        {
            public HmiStructureVerificationException(string message) : base(message) { }
        }
    }

    public sealed class HmiStructureMutationResult
    {
        public bool Success { get; set; }
        public string Operation { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool Partial { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Warning { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
        public string? OldName { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? ParentGroupPath { get; set; }
        public string? GroupPath { get; set; }
        public uint? Width { get; set; }
        public uint? Height { get; set; }
        public int? ItemCount { get; set; }
        public int? EntryCount { get; set; }

        public static HmiStructureMutationResult Failure(
            string operation,
            string objectName,
            string? oldName,
            string? parentGroupPath,
            string status,
            Exception exception)
        {
            return new HmiStructureMutationResult
            {
                Success = false,
                Operation = operation,
                Status = status,
                Name = objectName,
                OldName = oldName,
                ParentGroupPath = parentGroupPath,
                Partial = true,
                Message = $"{exception.GetType().Name}: {exception.Message}",
                Warnings = new List<string>
                {
                    "The API call may have changed project state; inspect the target by read-back before retrying."
                }
            };
        }
    }
}
