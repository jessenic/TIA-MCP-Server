using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Siemens.Engineering;
using Siemens.Engineering.Cax;
using Siemens.Engineering.CrossReference;
using Siemens.Engineering.HW;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.WatchAndForceTables;

namespace TiaOpennessMcp.Tia
{
    // CAx/AutomationML hardware exchange, project export to files, project archive/retrieve and cross references.
    // Based on the V21 Openness manual: "Importing/exporting hardware data", "Archiving and Retrieving a project",
    // "Getting Cross References for Step7" and the PLC export/import chapters.
    public partial class TiaManager
    {
        #region CAx / AutomationML

        private CaxProvider GetCaxProvider()
        {
            EnsureProjectOpen();
            var provider = _project!.GetService<CaxProvider>();
            if (provider == null)
                throw new InvalidOperationException("The CaxProvider service is not available. The user must be in the 'Siemens TIA Openness' Windows group.");
            return provider;
        }

        private static List<string> FlattenCaxMessages(TransferResultMessageComposition messages, int depth = 0, List<string>? list = null)
        {
            list ??= new List<string>();
            foreach (TransferResultMessage m in messages)
            {
                list.Add($"{new string(' ', depth * 2)}{m.State}: {m.Message}");
                if (list.Count > 500) break;
                FlattenCaxMessages(m.Messages, depth + 1, list);
            }
            return list;
        }

        public HardwareOperationResult ExportAml(string outputPath, string? deviceName, bool overwrite)
        {
            var provider = GetCaxProvider();
            var path = Path.GetFullPath(outputPath);
            if (!path.EndsWith(".aml", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("output_path must end with .aml.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path))
            {
                if (!overwrite) return HardwareOperationResult.Fail($"'{path}' already exists. Pass overwrite=true to replace it.");
                File.Delete(path);
            }

            TransferResult result;
            try
            {
                result = string.IsNullOrWhiteSpace(deviceName)
                    ? provider.Export(_project!, new FileInfo(path))
                    : provider.Export(FindDeviceExact(deviceName!), new FileInfo(path));
            }
            catch (EngineeringException ex)
            {
                return HardwareOperationResult.Fail($"CAx export failed: {Describe(ex)}");
            }

            bool ok = result.State != TransferResultState.Error && File.Exists(path);
            return new HardwareOperationResult
            {
                Success = ok,
                Message = ok ? $"Exported {(string.IsNullOrWhiteSpace(deviceName) ? "project hardware" : $"device '{deviceName}'")} to {path}. Siemens drives (Startdrive) and most HMI devices are not part of CAx exports; GSDML devices are."
                             : "CAx export reported errors.",
                Data = new
                {
                    Path = path,
                    State = result.State.ToString(),
                    result.ErrorCount,
                    result.WarningCount,
                    SizeBytes = File.Exists(path) ? new FileInfo(path).Length : 0,
                    Messages = FlattenCaxMessages(result.Messages)
                }
            };
        }

        /// <summary>Lists the device names at the top of an AML file (InternalElements that carry a Device role).</summary>
        private static List<string> ReadAmlDeviceNames(string path)
        {
            var doc = XDocument.Load(path);
            var names = new List<string>();
            foreach (var ie in doc.Descendants().Where(e => e.Name.LocalName == "InternalElement"))
            {
                bool isDevice = ie.Elements().Any(e =>
                    (e.Name.LocalName == "RoleRequirements" || e.Name.LocalName == "SupportedRoleClass") &&
                    ((string?)e.Attribute("RefBaseRoleClassPath") ?? (string?)e.Attribute("RefRoleClassPath") ?? "").EndsWith("/Device", StringComparison.Ordinal));
                if (isDevice)
                {
                    var name = (string?)ie.Attribute("Name");
                    if (!string.IsNullOrEmpty(name)) names.Add(name!);
                }
            }
            return names.Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToList();
        }

        public HardwareOperationResult ImportAml(string amlPath, string conflictOption, bool dryRun)
        {
            var provider = GetCaxProvider();
            var path = Path.GetFullPath(amlPath);
            if (!File.Exists(path)) throw new FileNotFoundException($"AML file not found: {path}");

            CaxImportOptions option;
            switch ((conflictOption ?? "").Trim().ToLowerInvariant())
            {
                case "":
                case "move_to_parking_lot": option = CaxImportOptions.MoveToParkingLot; break;
                case "retain_tia_device": option = CaxImportOptions.RetainTiaDevice; break;
                case "overwrite_tia_device": option = CaxImportOptions.OverwriteTiaDevice; break;
                default: throw new ArgumentException("conflict_option must be move_to_parking_lot, retain_tia_device or overwrite_tia_device.");
            }

            List<string> amlDevices;
            try { amlDevices = ReadAmlDeviceNames(path); }
            catch (Exception ex) when (ex is System.Xml.XmlException || ex is IOException)
            {
                return HardwareOperationResult.Fail($"The file is not readable AML/XML: {ex.Message}");
            }
            var projectDevices = new HashSet<string>(EnumerateAllDevices().Select(d => d.Name), StringComparer.OrdinalIgnoreCase);
            var conflicts = amlDevices.Where(projectDevices.Contains).ToList();
            string effect = option == CaxImportOptions.OverwriteTiaDevice ? "will be REPLACED by the AML version"
                : option == CaxImportOptions.RetainTiaDevice ? "will be kept and skipped from the AML"
                : "will be kept; the AML versions go to a parking-lot folder";

            if (dryRun)
            {
                return new HardwareOperationResult
                {
                    Success = true,
                    DryRun = true,
                    Message = $"Preview: {amlDevices.Count} devices in the AML, {conflicts.Count} with the same name as existing devices ({effect}). " +
                              "Import cannot be undone through Openness: save and archive the project first (tia_archive_project), then run again with dry_run=false.",
                    Data = new { Path = path, ConflictOption = option.ToString(), AmlDevices = amlDevices, ConflictingDevices = conflicts }
                };
            }

            TransferResult result;
            try { result = provider.Import(new FileInfo(path), option); }
            catch (EngineeringException ex)
            {
                return HardwareOperationResult.Fail($"CAx import failed: {Describe(ex)}. The import cannot run inside exclusive access, and needs product licenses for licensed modules.");
            }

            return new HardwareOperationResult
            {
                Success = result.State != TransferResultState.Error,
                Message = $"CAx import finished with state {result.State}. Save the project with tia_save_project.",
                Data = new
                {
                    Path = path,
                    ConflictOption = option.ToString(),
                    State = result.State.ToString(),
                    result.ErrorCount,
                    result.WarningCount,
                    Messages = FlattenCaxMessages(result.Messages)
                }
            };
        }

        #endregion

        #region Project export to files

        private static string SafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (var c in name) sb.Append(invalid.Contains(c) ? '_' : c);
            var s = sb.ToString().Trim().TrimEnd('.');
            return s.Length == 0 ? "_" : s;
        }

        private static string JoinRel(params string[] parts) =>
            string.Join("/", parts.Where(p => !string.IsNullOrEmpty(p)));

        /// <summary>
        /// Runs an Openness export into a temp file and copies it to the target only when the content differs,
        /// so unchanged objects keep their file (and timestamps) and diffs stay small.
        /// </summary>
        private bool ExportViaTemp(Action<FileInfo> export, string targetPath, Func<string, string>? normalize = null)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "tia_mcp_export_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string tempFile = Path.Combine(tempDir, Path.GetFileName(targetPath));
                export(new FileInfo(tempFile));
                string content = File.ReadAllText(tempFile);
                if (normalize != null) content = normalize(content);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                if (File.Exists(targetPath) && File.ReadAllText(targetPath) == content) return false;
                File.WriteAllText(targetPath, content, new UTF8Encoding(true));
                return true;
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        private static readonly System.Text.RegularExpressions.Regex SimaticMlCreatedRegex = new System.Text.RegularExpressions.Regex(
            @"(<DocumentInfo>.*?)\s*<Created>[^<]*</Created>", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Singleline);

        /// <summary>SimaticML exports stamp DocumentInfo/Created with the export time; drop it so unchanged objects give unchanged files.</summary>
        internal static string NormalizeSimaticMl(string content) => SimaticMlCreatedRegex.Replace(content, "$1", 1);

        private static readonly System.Text.RegularExpressions.Regex GuidRegex = new System.Text.RegularExpressions.Regex(
            "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// CAx exports carry the export time and give every element a new random GUID on each export.
        /// For version control the time is dropped and each ID is replaced by a GUID derived from the element's
        /// name path (stable across exports), with all references (InternalLink partner sides) rewritten to match.
        /// </summary>
        internal static string NormalizeAml(string content)
        {
            XDocument doc;
            try { doc = XDocument.Parse(content, LoadOptions.PreserveWhitespace); }
            catch (System.Xml.XmlException) { return content; }

            foreach (var el in doc.Descendants().Where(e => e.Name.LocalName == "LastWritingDateTime").ToList()) el.Remove();

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var used = new HashSet<string>(StringComparer.Ordinal);
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                foreach (var el in doc.Descendants())
                {
                    var idAttr = el.Attribute("ID");
                    if (idAttr == null || !Guid.TryParse(idAttr.Value, out _) || map.ContainsKey(idAttr.Value)) continue;
                    var path = string.Join("/", el.AncestorsAndSelf().Reverse()
                        .Select(a => a.Name.LocalName + ":" + ((string?)a.Attribute("Name") ?? "")));
                    string key = path;
                    for (int n = 2; used.Contains(key); n++) key = path + "#" + n;
                    used.Add(key);
                    var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(key));
                    hash[6] = (byte)((hash[6] & 0x0F) | 0x30); // RFC 4122 version 3 (name based, MD5)
                    hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
                    map[idAttr.Value] = new Guid(hash).ToString();
                }
            }

            var text = doc.Declaration + (doc.Declaration != null ? "\r\n" : "") + doc.ToString(SaveOptions.DisableFormatting);
            return GuidRegex.Replace(text, m => map.TryGetValue(m.Value, out var stable) ? stable : m.Value);
        }

        public ProjectExportResult ExportProjectFiles(string outputDirectory, string? plcName, bool includeHardware, bool includeScl, bool removeStale)
        {
            EnsureProjectOpen();
            if (string.IsNullOrWhiteSpace(outputDirectory)) throw new ArgumentException("output_directory is required.");
            string root = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(root);

            var report = new ProjectExportResult { OutputDirectory = root };
            var files = new SortedSet<string>(StringComparer.Ordinal);

            void Record(string rel, bool changed)
            {
                files.Add(rel);
                if (changed) report.Changed++; else report.Unchanged++;
            }

            void Try(string what, Action action)
            {
                try { action(); }
                catch (Exception ex) when (ex is EngineeringException || ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
                {
                    report.Errors.Add($"{what}: {ex.Message}");
                }
            }

            var plcs = new List<PlcSoftware>();
            WalkAllPlcs((dev, item, plc) =>
            {
                if (string.IsNullOrEmpty(plcName) || string.Equals(plc.Name, plcName, StringComparison.OrdinalIgnoreCase)) plcs.Add(plc);
            });
            if (!string.IsNullOrEmpty(plcName) && plcs.Count == 0)
                throw new ArgumentException($"PLC software '{plcName}' was not found.");

            foreach (var plc in plcs.OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                string plcDir = JoinRel("plc", SafeFileName(plc.Name));

                void Blocks(PlcBlockGroup group, string rel)
                {
                    foreach (PlcBlock block in group.Blocks)
                    {
                        string baseRel = JoinRel(plcDir, "blocks", rel, SafeFileName(block.Name));
                        report.Blocks++;
                        Try($"block {block.Name}", () =>
                        {
                            bool changed = ExportViaTemp(f => block.Export(f, ExportOptions.WithDefaults), Path.Combine(root, baseRel + ".xml"), NormalizeSimaticMl);
                            Record(baseRel + ".xml", changed);
                        });
                        if (includeScl && block.ProgrammingLanguage == ProgrammingLanguage.SCL && block is IGenerateSource gs)
                        {
                            Try($"SCL source {block.Name}", () =>
                            {
                                bool changed = ExportViaTemp(f => plc.ExternalSourceGroup.GenerateSource(new[] { gs }, f, GenerateOptions.None), Path.Combine(root, baseRel + ".scl"));
                                Record(baseRel + ".scl", changed);
                            });
                        }
                    }
                    foreach (PlcBlockUserGroup sub in group.Groups) Blocks(sub, JoinRel(rel, SafeFileName(sub.Name)));
                }
                Blocks(plc.BlockGroup, "");

                void Types(PlcTypeGroup group, string rel)
                {
                    foreach (PlcType type in group.Types)
                    {
                        string fileRel = JoinRel(plcDir, "types", rel, SafeFileName(type.Name)) + ".xml";
                        report.Types++;
                        Try($"type {type.Name}", () => Record(fileRel, ExportViaTemp(f => type.Export(f, ExportOptions.WithDefaults), Path.Combine(root, fileRel), NormalizeSimaticMl)));
                    }
                    foreach (PlcTypeUserGroup sub in group.Groups) Types(sub, JoinRel(rel, SafeFileName(sub.Name)));
                }
                Types(plc.TypeGroup, "");

                void Tags(PlcTagTableGroup group, string rel)
                {
                    foreach (PlcTagTable table in group.TagTables)
                    {
                        string fileRel = JoinRel(plcDir, "tags", rel, SafeFileName(table.Name)) + ".xml";
                        report.TagTables++;
                        Try($"tag table {table.Name}", () => Record(fileRel, ExportViaTemp(f => table.Export(f, ExportOptions.WithDefaults), Path.Combine(root, fileRel), NormalizeSimaticMl)));
                    }
                    foreach (PlcTagTableUserGroup sub in group.Groups) Tags(sub, JoinRel(rel, SafeFileName(sub.Name)));
                }
                Tags(plc.TagTableGroup, "");

                Try("watch and force tables", () =>
                {
                    var wf = plc.WatchAndForceTableGroup;
                    void Watch(PlcWatchTableComposition watch, PlcForceTableComposition force, IEnumerable<PlcWatchAndForceTableUserGroup> groups, string rel)
                    {
                        foreach (PlcWatchTable t in watch)
                        {
                            string fileRel = JoinRel(plcDir, "watch", rel, SafeFileName(t.Name)) + ".xml";
                            report.WatchTables++;
                            Try($"watch table {t.Name}", () => Record(fileRel, ExportViaTemp(f => t.Export(f, ExportOptions.None), Path.Combine(root, fileRel), NormalizeSimaticMl)));
                        }
                        foreach (PlcForceTable t in force)
                        {
                            string fileRel = JoinRel(plcDir, "force", rel, SafeFileName(t.Name)) + ".xml";
                            report.WatchTables++;
                            Try($"force table {t.Name}", () => Record(fileRel, ExportViaTemp(f => t.Export(f, ExportOptions.None), Path.Combine(root, fileRel), NormalizeSimaticMl)));
                        }
                        foreach (var g in groups)
                            Watch(g.WatchTables, g.ForceTables, g.Groups.Cast<PlcWatchAndForceTableUserGroup>(), JoinRel(rel, SafeFileName(g.Name)));
                    }
                    Watch(wf.WatchTables, wf.ForceTables, wf.Groups.Cast<PlcWatchAndForceTableUserGroup>(), "");
                });
            }

            if (includeHardware)
            {
                Try("hardware AML", () =>
                {
                    var provider = GetCaxProvider();
                    TransferResult? cax = null;
                    bool changed = ExportViaTemp(f => { cax = provider.Export(_project!, f); }, Path.Combine(root, "hardware", "project.aml"), NormalizeAml);
                    Record("hardware/project.aml", changed);
                    if (cax != null && cax.State == TransferResultState.Error)
                        report.Errors.Add("hardware AML: CAx export reported errors: " + string.Join(" | ", FlattenCaxMessages(cax.Messages).Take(10)));
                });
            }

            // Manifest without timestamps: a re-export of an unchanged project leaves the folder byte-identical.
            string manifestPath = Path.Combine(root, "tia-export.json");
            var oldFiles = new List<string>();
            if (File.Exists(manifestPath))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
                    if (doc.RootElement.TryGetProperty("files", out var arr))
                        oldFiles = arr.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToList();
                }
                catch (JsonException) { }
            }

            if (removeStale)
            {
                foreach (var rel in oldFiles.Where(f => !files.Contains(f)))
                {
                    var full = Path.GetFullPath(Path.Combine(root, rel));
                    if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                    // Failed exports keep their previous file instead of silently dropping it.
                    if (report.Errors.Any(e => e.IndexOf(Path.GetFileNameWithoutExtension(rel), StringComparison.Ordinal) >= 0)) { files.Add(rel); continue; }
                    if (File.Exists(full)) { File.Delete(full); report.Removed.Add(rel); }
                }
            }

            var manifest = new
            {
                project = _project!.Name,
                generator = "TIA-MCP-Server tia_export_project_files",
                errors = report.Errors.OrderBy(e => e, StringComparer.Ordinal).ToList(),
                files = files.ToList()
            };
            string manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n") + "\n";
            if (!File.Exists(manifestPath) || File.ReadAllText(manifestPath) != manifestJson)
                File.WriteAllText(manifestPath, manifestJson, new UTF8Encoding(false));

            report.Files = files.Count;
            report.Success = report.Errors.Count == 0;
            report.Message = report.Success
                ? $"Exported {files.Count} files ({report.Changed} changed, {report.Unchanged} unchanged, {report.Removed.Count} removed)."
                : $"Exported {files.Count} files with {report.Errors.Count} errors. Inconsistent or know-how protected blocks cannot be exported; compile the PLC (tia_compile) and run again.";
            return report;
        }

        #endregion

        #region Archive and retrieve

        public HardwareOperationResult ArchiveProject(string targetDirectory, string? fileName, string? mode, bool saveFirst)
        {
            EnsureProjectOpen();
            if (string.IsNullOrWhiteSpace(targetDirectory)) throw new ArgumentException("target_directory is required.");
            var dir = Path.GetFullPath(targetDirectory);
            Directory.CreateDirectory(dir);

            ProjectArchivationMode archMode = (mode ?? "").Trim().ToLowerInvariant() switch
            {
                "" or "compressed" => ProjectArchivationMode.Compressed,
                "discard_restorable_data_and_compressed" => ProjectArchivationMode.DiscardRestorableDataAndCompressed,
                _ => throw new ArgumentException("mode must be compressed or discard_restorable_data_and_compressed (only compressed archives can be retrieved).")
            };

            string name = string.IsNullOrWhiteSpace(fileName)
                ? $"{SafeFileName(_project!.Name)}_{DateTime.Now:yyyyMMdd_HHmm}.zap21"
                : fileName!.Trim();
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new ArgumentException("file_name must be a plain file name.");
            if (!name.EndsWith(".zap21", StringComparison.OrdinalIgnoreCase)) name += ".zap21";
            string full = Path.Combine(dir, name);
            if (File.Exists(full)) return HardwareOperationResult.Fail($"'{full}' already exists; nothing was archived.");

            if (_project!.IsModified)
            {
                if (!saveFirst)
                    return HardwareOperationResult.Fail("The project has unsaved changes; Openness archives only a saved project. Save it (tia_save_project) or pass save_first=true.");
                _project.Save();
            }

            try
            {
                _project.Archive(new DirectoryInfo(dir), name, archMode);
            }
            catch (EngineeringException ex)
            {
                return HardwareOperationResult.Fail($"Archive failed: {Describe(ex)}");
            }

            return new HardwareOperationResult
            {
                Success = File.Exists(full),
                Message = File.Exists(full) ? $"Archived to {full}." : "Archive returned without error but the file was not found.",
                Data = new { Path = full, Mode = archMode.ToString(), SizeBytes = File.Exists(full) ? new FileInfo(full).Length : 0 }
            };
        }

        public ConnectionResult RetrieveProject(string archivePath, string targetDirectory, bool closeCurrent)
        {
            if (_tiaPortal == null)
                throw new InvalidOperationException("Not attached to TIA Portal. Call tia_list_processes and tia_connect first.");
            var archive = Path.GetFullPath(archivePath);
            if (!File.Exists(archive)) throw new FileNotFoundException($"Archive not found: {archive}");
            var target = Path.GetFullPath(targetDirectory);
            if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
                throw new IOException($"target_directory must be empty or not exist: {target}");
            Directory.CreateDirectory(target);

            bool closedProject = false;
            if (_tiaPortal.Projects.Count > 0)
            {
                if (!closeCurrent)
                    return new ConnectionResult { Success = false, Message = "A project is open in this TIA Portal instance. Pass close_current=true to close it (it must have no unsaved changes) before retrieving." };
                foreach (Project p in _tiaPortal.Projects.ToList())
                {
                    if (p.IsModified)
                        return new ConnectionResult { Success = false, Message = $"Project '{p.Name}' has unsaved changes. Save or discard them first; nothing was closed." };
                }
                foreach (Project p in _tiaPortal.Projects.ToList()) p.Close();
                _project = null;
                closedProject = true;
            }

            try
            {
                Project project;
                try { project = _tiaPortal.Projects.Retrieve(new FileInfo(archive), new DirectoryInfo(target)); }
                catch (EngineeringException) when (closedProject)
                {
                    // Right after closing a project TIA Portal can still hold file handles ("access denied");
                    // one delayed retry into a clean target folder resolves it.
                    System.Threading.Thread.Sleep(5000);
                    if (Directory.Exists(target)) Directory.Delete(target, true);
                    Directory.CreateDirectory(target);
                    project = _tiaPortal.Projects.Retrieve(new FileInfo(archive), new DirectoryInfo(target));
                }
                _project = project;
                return new ConnectionResult
                {
                    Success = true,
                    ProcessId = _attachedPid,
                    ProjectName = project.Name,
                    ProjectPath = project.Path?.FullName ?? "",
                    Message = "Archive retrieved and opened."
                };
            }
            catch (EngineeringException ex)
            {
                return new ConnectionResult { Success = false, Message = $"Retrieve failed: {Describe(ex)}. Archives from older TIA versions need an upgrade in TIA Portal." };
            }
        }

        #endregion

        #region Cross references

        public HardwareOperationResult ExportCrossReferences(string outputPath, string? plcName, List<string>? objectNames, string? filter, bool includeTagTables)
        {
            var plc = FindPlc(plcName);
            if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("output_path is required.");
            var path = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            CrossReferenceFilter xfilter = (filter ?? "").Trim().ToLowerInvariant() switch
            {
                "" or "objects_with_references" => CrossReferenceFilter.ObjectsWithReferences,
                "all_objects" => CrossReferenceFilter.AllObjects,
                "objects_without_references" => CrossReferenceFilter.ObjectsWithoutReferences,
                "unused_objects" => CrossReferenceFilter.UnusedObjects,
                _ => throw new ArgumentException("filter must be objects_with_references, all_objects, objects_without_references or unused_objects.")
            };

            var targets = new List<(string Kind, string Name, IEngineeringServiceProvider Obj)>();
            void Blocks(PlcBlockGroup g)
            {
                foreach (PlcBlock b in g.Blocks) targets.Add(("Block", b.Name, b));
                foreach (PlcBlockUserGroup s in g.Groups) Blocks(s);
            }
            void Tables(PlcTagTableGroup g)
            {
                foreach (PlcTagTable t in g.TagTables)
                    foreach (PlcTag tag in t.Tags) targets.Add(("Tag", tag.Name, tag));
                foreach (PlcTagTableUserGroup s in g.Groups) Tables(s);
            }
            Blocks(plc.BlockGroup);
            if (includeTagTables) Tables(plc.TagTableGroup);
            if (objectNames != null && objectNames.Count > 0)
            {
                var wanted = new HashSet<string>(objectNames, StringComparer.OrdinalIgnoreCase);
                targets = targets.Where(t => wanted.Contains(t.Name)).ToList();
                if (targets.Count == 0) throw new ArgumentException("None of object_names matched a block or PLC tag.");
            }

            int lines = 0, sources = 0;
            var errors = new List<string>();
            var unsupported = new List<string>();
            var options = new JsonSerializerOptions { WriteIndented = false };
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)) { NewLine = "\n" })
            {
                void WriteSource(SourceObject src, string root)
                {
                    sources++;
                    bool any = false;
                    foreach (ReferenceObject r in src.References)
                    {
                        foreach (Location loc in r.Locations)
                        {
                            any = true;
                            writer.WriteLine(JsonSerializer.Serialize(new
                            {
                                root,
                                source = src.Name,
                                sourceType = src.TypeName,
                                sourceAddress = NullIfEmpty(src.Address),
                                sourcePath = NullIfEmpty(src.Path),
                                device = NullIfEmpty(src.Device),
                                reference = r.Name,
                                referenceTypeName = r.TypeName,
                                referencePath = NullIfEmpty(r.Path),
                                location = NullIfEmpty(loc.ReferenceLocation),
                                locationName = NullIfEmpty(loc.Name),
                                referenceType = loc.ReferenceType.ToString(),
                                access = loc.Access.ToString(),
                                referencedAs = NullIfEmpty(SafeAttr(loc, "ReferncedAsName")?.ToString() ?? SafeAttr(loc, "ReferencedAsName")?.ToString()),
                                address = NullIfEmpty(loc.Address),
                                typeName = NullIfEmpty(loc.TypeName)
                            }, options));
                            lines++;
                        }
                    }
                    if (!any && xfilter != CrossReferenceFilter.ObjectsWithReferences)
                    {
                        writer.WriteLine(JsonSerializer.Serialize(new
                        {
                            root,
                            source = src.Name,
                            sourceType = src.TypeName,
                            sourceAddress = NullIfEmpty(src.Address),
                            sourcePath = NullIfEmpty(src.Path),
                            device = NullIfEmpty(src.Device),
                            reference = (string?)null
                        }, options));
                        lines++;
                    }
                    foreach (SourceObject child in src.Children) WriteSource(child, root);
                }

                foreach (var t in targets)
                {
                    try
                    {
                        var service = t.Obj.GetService<CrossReferenceService>();
                        if (service == null) { unsupported.Add($"{t.Kind}:{t.Name}"); continue; }
                        var result = service.GetCrossReferences(xfilter);
                        foreach (SourceObject src in result.Sources) WriteSource(src, $"{t.Kind}:{t.Name}");
                    }
                    catch (EngineeringException ex) { errors.Add($"{t.Kind} {t.Name}: {ex.Message}"); }
                }
            }

            return new HardwareOperationResult
            {
                Success = errors.Count == 0,
                Message = $"Wrote {lines} JSONL lines for {targets.Count} objects ({sources} source entries) to {path}." + (errors.Count > 0 ? $" {errors.Count} objects failed." : ""),
                Data = new { Path = path, Filter = xfilter.ToString(), Objects = targets.Count, Lines = lines, Errors = errors, WithoutCrossReferenceService = unsupported }
            };
        }

        private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

        #endregion
    }

    public sealed class ProjectExportResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string OutputDirectory { get; set; } = "";
        public int Files { get; set; }
        public int Changed { get; set; }
        public int Unchanged { get; set; }
        public int Blocks { get; set; }
        public int Types { get; set; }
        public int TagTables { get; set; }
        public int WatchTables { get; set; }
        public List<string> Removed { get; } = new List<string>();
        public List<string> Errors { get; } = new List<string>();
    }
}
