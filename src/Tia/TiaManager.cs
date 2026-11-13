using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;

namespace TiaOpennessMcp.Tia
{
    public partial class TiaManager : IDisposable, IHmiToolOperations
    {
        private static readonly Regex SclBlockDeclarationRegex = new Regex(
            @"(?im)^\s*(?<kind>FUNCTION_BLOCK|FUNCTION|ORGANIZATION_BLOCK|DATA_BLOCK)\s+(?:""(?<quoted>[^""]+)""|(?<identifier>[A-Za-z_][A-Za-z0-9_]*))(?=\s|:|$)",
            RegexOptions.CultureInvariant);
        private static readonly Regex SclCommentsRegex = new Regex(
            @"/\*.*?\*/|\(\*.*?\*\)|//[^\r\n]*",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        private static readonly Lazy<TiaManager> _instance = new Lazy<TiaManager>(() => new TiaManager());
        public static TiaManager Instance => _instance.Value;

        private TiaPortal? _tiaPortal;
        private Project? _project;
        private int? _attachedPid;
        private bool _ownsPortalInstance;

        public bool IsConnected => _tiaPortal != null && _project != null;
        public int? AttachedProcessId => _attachedPid;
        public Project? CurrentProject => _project;

        private TiaManager() { }

        #region Process & Connection Management

        public List<ProcessInfo> ListProcesses()
        {
            var list = new List<ProcessInfo>();
            foreach (var proc in TiaPortal.GetProcesses())
            {
                list.Add(new ProcessInfo
                {
                    Id = proc.Id,
                    ProjectPath = proc.ProjectPath?.FullName ?? "",
                    ProjectName = proc.ProjectPath != null ? Path.GetFileNameWithoutExtension(proc.ProjectPath.FullName) : "",
                    Mode = proc.Mode.ToString()
                });
            }
            return list;
        }

        public ConnectionResult Connect(int? processId = null)
        {
            try
            {
                var processes = TiaPortal.GetProcesses();
                if (processes.Count == 0)
                {
                    return new ConnectionResult
                    {
                        Success = false,
                        Message = "No running TIA Portal instances found. Please start TIA Portal V21 with your project open."
                    };
                }

                TiaPortalProcess? target = null;
                if (processId.HasValue && processId.Value > 0)
                {
                    target = processes.FirstOrDefault(p => p.Id == processId.Value);
                    if (target == null)
                    {
                        Console.Error.WriteLine($"[TiaManager] Preferred PID {processId.Value} not found, falling back to process with active project...");
                    }
                }

                if (target == null)
                {
                    target = processes.FirstOrDefault(p => p.ProjectPath != null) ?? processes[0];
                }

                Disconnect();

                Console.Error.WriteLine($"[TiaManager] Attaching to PID {target.Id}...");
                _tiaPortal = target.Attach();
                _attachedPid = target.Id;
                _ownsPortalInstance = false;

                _project = _tiaPortal.Projects.FirstOrDefault();

                return new ConnectionResult
                {
                    Success = true,
                    ProcessId = target.Id,
                    ProjectName = _project?.Name ?? "(No project currently open in this instance)",
                    ProjectPath = _project?.Path?.FullName ?? "",
                    Message = _project != null ? $"Successfully attached to TIA Portal PID {target.Id} with project '{_project.Name}'."
                                               : $"Attached to TIA Portal PID {target.Id}, but no project is open."
                };
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[TiaManager] Connect error: {ex}");
                return new ConnectionResult
                {
                    Success = false,
                    Message = $"Failed to connect to TIA Portal: {ex.Message}"
                };
            }
        }

        public void Disconnect()
        {
            try
            {
                if (_ownsPortalInstance && _tiaPortal != null)
                {
                    _tiaPortal.Dispose();
                }
                _tiaPortal = null;
                _project = null;
                _attachedPid = null;
                _ownsPortalInstance = false;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[TiaManager] Disconnect error: {ex.Message}");
            }
        }

        public ConnectionResult OpenProject(string projectPath, bool withGui = true)
        {
            try
            {
                if (!File.Exists(projectPath))
                {
                    return new ConnectionResult
                    {
                        Success = false,
                        Message = $"Project file not found: {projectPath}"
                    };
                }

                Disconnect();

                var mode = withGui ? TiaPortalMode.WithUserInterface : TiaPortalMode.WithoutUserInterface;
                Console.Error.WriteLine($"[TiaManager] Starting new TIA Portal instance ({mode})...");
                _tiaPortal = new TiaPortal(mode);
                _ownsPortalInstance = true;

                Console.Error.WriteLine($"[TiaManager] Opening project {projectPath}...");
                _project = _tiaPortal.Projects.Open(new FileInfo(projectPath));
                _attachedPid = TiaPortal.GetProcesses().FirstOrDefault(p => p.ProjectPath?.FullName == projectPath)?.Id;

                return new ConnectionResult
                {
                    Success = true,
                    ProcessId = _attachedPid,
                    ProjectName = _project.Name,
                    ProjectPath = _project.Path?.FullName ?? projectPath,
                    Message = $"Successfully opened project '{_project.Name}'."
                };
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[TiaManager] OpenProject error: {ex}");
                return new ConnectionResult
                {
                    Success = false,
                    Message = $"Failed to open project: {ex.Message}"
                };
            }
        }

        public void SaveProject()
        {
            EnsureProjectOpen();
            _project!.Save();
        }

        public void CloseProject()
        {
            EnsureProjectOpen();
            _project!.Close();
            _project = null;
        }

        public ProjectInfoDetails GetProjectInfo()
        {
            EnsureProjectOpen();
            return new ProjectInfoDetails
            {
                Name = _project!.Name,
                Path = _project.Path?.FullName ?? "",
                Author = GetSafeAttribute(_project, "Author"),
                Comment = GetSafeAttribute(_project, "Comment"),
                DateCreated = GetSafeAttribute(_project, "CreationDateTime"),
                DateModified = GetSafeAttribute(_project, "ModifiedDateTime"),
                IsModified = (bool?)GetSafeAttributeObj(_project, "IsModified") ?? false,
                AttachedProcessId = _attachedPid
            };
        }

        public ServerConnectionStatus GetStatus()
        {
            return new ServerConnectionStatus
            {
                IsConnected = IsConnected,
                AttachedProcessId = _attachedPid,
                ProjectName = _project?.Name ?? "",
                ProjectPath = _project?.Path?.FullName ?? "",
                OwnsPortalInstance = _ownsPortalInstance
            };
        }

        #endregion

        #region Devices & PLC Software Resolution

        public List<DeviceInfo> ListDevices()
        {
            EnsureProjectOpen();
            var list = new List<DeviceInfo>();

            foreach (Device dev in _project!.Devices)
            {
                var info = new DeviceInfo
                {
                    Name = dev.Name,
                    TypeIdentifier = dev.TypeIdentifier,
                    DeviceGroup = ""
                };
                FindPlcsInDevice(dev, info);
                list.Add(info);
            }

            foreach (DeviceUserGroup group in _project.DeviceGroups)
            {
                CollectGroupDevices(group, "", list);
            }

            return list;
        }

        private void CollectGroupDevices(DeviceUserGroup group, string parentPath, List<DeviceInfo> list)
        {
            string groupPath = string.IsNullOrEmpty(parentPath) ? group.Name : $"{parentPath}/{group.Name}";
            foreach (Device dev in group.Devices)
            {
                var info = new DeviceInfo
                {
                    Name = dev.Name,
                    TypeIdentifier = dev.TypeIdentifier,
                    DeviceGroup = groupPath
                };
                FindPlcsInDevice(dev, info);
                list.Add(info);
            }

            foreach (DeviceUserGroup sub in group.Groups)
            {
                CollectGroupDevices(sub, groupPath, list);
            }
        }

        private void FindPlcsInDevice(Device dev, DeviceInfo info)
        {
            WalkDeviceItemsForPlc(dev.DeviceItems, info);
        }

        private void WalkDeviceItemsForPlc(DeviceItemComposition items, DeviceInfo info)
        {
            foreach (DeviceItem item in items)
            {
                var sc = item.GetService<SoftwareContainer>();
                if (sc?.Software is PlcSoftware plc)
                {
                    info.PlcNames.Add(plc.Name);
                    info.HasPlc = true;
                }
                if (item.DeviceItems.Count > 0)
                {
                    WalkDeviceItemsForPlc(item.DeviceItems, info);
                }
            }
        }

        public PlcSoftware FindPlc(string? plcName = null)
        {
            EnsureProjectOpen();

            PlcSoftware? found = null;
            WalkAllPlcs((dev, item, plc) =>
            {
                if (found != null) return;
                if (string.IsNullOrEmpty(plcName) || string.Equals(plc.Name, plcName, StringComparison.OrdinalIgnoreCase))
                {
                    found = plc;
                }
            });

            if (found == null)
            {
                string msg = string.IsNullOrEmpty(plcName)
                    ? "No PLC software found in the current project."
                    : $"PLC software '{plcName}' was not found in the project.";
                throw new InvalidOperationException(msg);
            }

            return found;
        }

        public List<PlcSoftwareSummary> ListAllPlcs()
        {
            EnsureProjectOpen();
            var list = new List<PlcSoftwareSummary>();

            WalkAllPlcs((dev, item, plc) =>
            {
                list.Add(new PlcSoftwareSummary
                {
                    PlcName = plc.Name,
                    DeviceName = dev.Name,
                    ItemName = item.Name,
                    BlockCount = CountBlocks(plc.BlockGroup),
                    TypeCount = CountTypes(plc.TypeGroup),
                    TagTableCount = CountTagTables(plc.TagTableGroup)
                });
            });

            return list;
        }

        private void WalkAllPlcs(Action<Device, DeviceItem, PlcSoftware> callback)
        {
            void InspectDevice(Device dev)
            {
                InspectItems(dev.DeviceItems, dev);
            }

            void InspectItems(DeviceItemComposition items, Device dev)
            {
                foreach (DeviceItem item in items)
                {
                    var sc = item.GetService<SoftwareContainer>();
                    if (sc?.Software is PlcSoftware plc)
                    {
                        callback(dev, item, plc);
                    }
                    if (item.DeviceItems.Count > 0)
                    {
                        InspectItems(item.DeviceItems, dev);
                    }
                }
            }

            foreach (Device dev in _project!.Devices)
                InspectDevice(dev);

            void WalkGroup(DeviceUserGroup grp)
            {
                foreach (Device dev in grp.Devices)
                    InspectDevice(dev);
                foreach (DeviceUserGroup sub in grp.Groups)
                    WalkGroup(sub);
            }

            foreach (DeviceUserGroup grp in _project.DeviceGroups)
                WalkGroup(grp);
        }

        public DeviceItemNode GetDeviceTree(string deviceName)
        {
            EnsureProjectOpen();
            Device? targetDev = null;

            foreach (Device dev in _project!.Devices)
            {
                if (string.Equals(dev.Name, deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    targetDev = dev;
                    break;
                }
            }

            if (targetDev == null)
            {
                void SearchGroup(DeviceUserGroup grp)
                {
                    if (targetDev != null) return;
                    foreach (Device dev in grp.Devices)
                    {
                        if (string.Equals(dev.Name, deviceName, StringComparison.OrdinalIgnoreCase))
                        {
                            targetDev = dev;
                            return;
                        }
                    }
                    foreach (DeviceUserGroup sub in grp.Groups)
                        SearchGroup(sub);
                }

                foreach (DeviceUserGroup grp in _project.DeviceGroups)
                    SearchGroup(grp);
            }

            if (targetDev == null)
                throw new ArgumentException($"Device '{deviceName}' not found.");

            var root = new DeviceItemNode
            {
                Name = targetDev.Name,
                TypeIdentifier = targetDev.TypeIdentifier,
                NodeType = "Device"
            };

            BuildDeviceTree(targetDev.DeviceItems, root);
            return root;
        }

        private void BuildDeviceTree(DeviceItemComposition items, DeviceItemNode parent)
        {
            foreach (DeviceItem item in items)
            {
                var node = new DeviceItemNode
                {
                    Name = item.Name,
                    TypeIdentifier = item.TypeIdentifier,
                    NodeType = "DeviceItem"
                };

                var sc = item.GetService<SoftwareContainer>();
                if (sc?.Software is PlcSoftware plc)
                {
                    node.SoftwareName = plc.Name;
                    node.NodeType = "PLC_CPU";
                }

                parent.Children.Add(node);

                if (item.DeviceItems.Count > 0)
                {
                    BuildDeviceTree(item.DeviceItems, node);
                }
            }
        }

        #endregion

        #region PLC Blocks (OB, FB, FC, DB)

        public List<PlcBlockInfo> ListBlocks(string? plcName = null, string? blockType = null, string? folder = null)
        {
            var plc = FindPlc(plcName);
            var result = new List<PlcBlockInfo>();

            CollectBlocks(plc.BlockGroup, "", result, blockType, folder);
            return result;
        }

        private void CollectBlocks(PlcBlockGroup group, string currentPath, List<PlcBlockInfo> result, string? filterType, string? filterFolder)
        {
            foreach (PlcBlock block in group.Blocks)
            {
                string typeStr = GetBlockTypeName(block);
                if (!string.IsNullOrEmpty(filterType) && !string.Equals(typeStr, filterType, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrEmpty(filterFolder) && !currentPath.StartsWith(filterFolder, StringComparison.OrdinalIgnoreCase))
                    continue;

                result.Add(new PlcBlockInfo
                {
                    Name = block.Name,
                    Number = block.Number,
                    BlockType = typeStr,
                    ProgrammingLanguage = block.ProgrammingLanguage.ToString(),
                    GroupPath = currentPath,
                    IsConsistent = (bool?)GetSafeAttributeObj(block, "IsConsistent") ?? true,
                    IsKnowHowProtected = (bool?)GetSafeAttributeObj(block, "IsKnowHowProtected") ?? false
                });
            }

            foreach (PlcBlockUserGroup sub in group.Groups)
            {
                string nextPath = string.IsNullOrEmpty(currentPath) ? sub.Name : $"{currentPath}/{sub.Name}";
                CollectBlocks(sub, nextPath, result, filterType, filterFolder);
            }
        }

        private string GetBlockTypeName(PlcBlock block)
        {
            if (block is OB) return "OB";
            if (block is FB) return "FB";
            if (block is FC) return "FC";
            if (block is GlobalDB) return "GlobalDB";
            if (block is InstanceDB) return "InstanceDB";
            return block.GetType().Name;
        }

        public BlockExportResult GetBlockCode(string? plcName, string blockName)
        {
            var plc = FindPlc(plcName);
            PlcBlock? targetBlock = FindBlockRecursive(plc.BlockGroup, blockName);

            if (targetBlock == null)
                throw new ArgumentException($"Block '{blockName}' was not found in PLC '{plc.Name}'.");

            string tempFile = Path.Combine(Path.GetTempPath(), $"tia_block_{Guid.NewGuid():N}.xml");
            try
            {
                targetBlock.Export(new FileInfo(tempFile), ExportOptions.WithDefaults);
                string xml = File.ReadAllText(tempFile);

                // Attempt to extract structured text / SCL if present in SimaticML
                string? sclCode = ExtractSclFromSimaticMl(xml);

                return new BlockExportResult
                {
                    Name = targetBlock.Name,
                    Number = targetBlock.Number,
                    BlockType = GetBlockTypeName(targetBlock),
                    ProgrammingLanguage = targetBlock.ProgrammingLanguage.ToString(),
                    XmlContent = xml,
                    ExtractedScl = sclCode
                };
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        private string? ExtractSclFromSimaticMl(string xml)
        {
            try
            {
                var doc = XDocument.Parse(xml);
                // Look for StructuredText elements
                var stElements = doc.Descendants().Where(e => e.Name.LocalName == "StructuredText").ToList();
                if (stElements.Count > 0)
                {
                    var lines = new List<string>();
                    foreach (var st in stElements)
                    {
                        var textLines = st.Elements().Where(e => e.Name.LocalName == "Line" || e.Name.LocalName == "Token")
                                          .Select(e => e.Value).ToList();
                        if (textLines.Count > 0)
                            lines.Add(string.Join("\n", textLines));
                        else if (!string.IsNullOrWhiteSpace(st.Value))
                            lines.Add(st.Value);
                    }
                    if (lines.Count > 0)
                        return string.Join("\n\n// ── Network / Section ──\n", lines);
                }
            }
            catch { }
            return null;
        }

        public string ImportBlock(string? plcName, string xmlContent, bool overwrite = true, string? targetFolder = null)
        {
            var plc = FindPlc(plcName);
            PlcBlockGroup targetGroup = plc.BlockGroup;

            if (!string.IsNullOrEmpty(targetFolder))
            {
                targetGroup = GetOrCreateBlockUserGroup(plc.BlockGroup, targetFolder!);
            }

            string tempFile = Path.Combine(Path.GetTempPath(), $"tia_import_{Guid.NewGuid():N}.xml");
            try
            {
                File.WriteAllText(tempFile, xmlContent);
                var options = overwrite ? ImportOptions.Override : ImportOptions.None;
                IList<PlcBlock> importedBlocks = targetGroup.Blocks.Import(new FileInfo(tempFile), options);
                var first = importedBlocks.Count > 0 ? importedBlocks[0] : null;

                return first != null
                    ? $"Successfully imported block '{first.Name}' (Number {first.Number}, Language {first.ProgrammingLanguage}). Total imported: {importedBlocks.Count}."
                    : "Successfully imported block(s).";
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        public SclBlockImportResult ImportSclBlock(
            string? plcName,
            string sclCode,
            string? targetFolder = null,
            bool compileAfterImport = true,
            bool openAfterImport = false)
        {
            if (string.IsNullOrWhiteSpace(sclCode))
                throw new ArgumentException("scl_code must contain a complete SCL block source.", nameof(sclCode));

            var (blockName, blockKind) = GetSclBlockDeclaration(sclCode);
            var plc = FindPlc(plcName);
            List<BlockLocation> matches = FindBlockLocations(plc.BlockGroup, blockName);
            if (matches.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Block name '{blockName}' is ambiguous in PLC '{plc.Name}'; refusing to replace any block.");
            }

            BlockLocation? existing = matches.Count == 1 ? matches[0] : null;
            string sourceFile = Path.Combine(Path.GetTempPath(), $"tia_scl_{Guid.NewGuid():N}.scl");
            string backupFile = Path.Combine(Path.GetTempPath(), $"tia_scl_backup_{Guid.NewGuid():N}.xml");
            bool existingDeleted = false;

            try
            {
                if (existing != null)
                {
                    existing.Block.Export(new FileInfo(backupFile), ExportOptions.WithDefaults);
                }

                string destinationFolder = string.IsNullOrWhiteSpace(targetFolder)
                    ? existing?.FolderPath ?? ""
                    : targetFolder ?? "";
                PlcBlockGroup destinationGroup = FindBlockGroup(plc.BlockGroup, destinationFolder);

                File.WriteAllText(sourceFile, sclCode, new UTF8Encoding(false));
                string sourceName = Path.GetFileNameWithoutExtension(sourceFile);
                PlcExternalSource source = plc.ExternalSourceGroup.ExternalSources.CreateFromFile(sourceName, sourceFile)
                    ?? throw new InvalidOperationException("TIA Portal did not create an external SCL source.");

                if (existing != null)
                {
                    try
                    {
                        existing.Block.Delete();
                        existingDeleted = true;
                    }
                    catch (Exception deleteException)
                    {
                        try
                        {
                            source.Delete();
                        }
                        catch (Exception cleanupException)
                        {
                            throw new InvalidOperationException(
                                $"Could not replace block '{blockName}': deletion failed ({deleteException.Message}) and the imported external source could not be removed ({cleanupException.Message}).",
                                deleteException);
                        }

                        throw;
                    }
                }

                IList<IEngineeringObject> generated;
                try
                {
                    generated = destinationGroup is PlcBlockUserGroup destinationUserGroup
                        ? source.GenerateBlocksFromSource(
                            destinationUserGroup,
                            GenerateBlockOption.KeepOnError)
                        : source.GenerateBlocksFromSource(GenerateBlockOption.KeepOnError);

                    PlcBlock? importedBlock = generated
                        .OfType<PlcBlock>()
                        .FirstOrDefault(block => string.Equals(block.Name, blockName, StringComparison.OrdinalIgnoreCase));

                    if (importedBlock == null)
                    {
                        string generatedNames = string.Join(", ", generated.Select(item => item.ToString()));
                        throw new InvalidOperationException(
                            $"SCL source did not generate the declared block '{blockName}'. Generated objects: {generatedNames}.");
                    }

                    var result = new SclBlockImportResult
                    {
                        Imported = true,
                        BlockName = importedBlock.Name,
                        BlockKind = blockKind,
                        Folder = destinationFolder,
                        ReplacedExistingBlock = existing != null
                    };

                    try
                    {
                        source.Delete();
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"Imported external source could not be removed from the project: {ex.Message}");
                    }

                    if (existing != null)
                    {
                        try
                        {
                            File.Delete(backupFile);
                        }
                        catch (Exception ex)
                        {
                            result.Warnings.Add($"Block backup could not be removed: {backupFile}. {ex.Message}");
                        }
                    }

                    if (compileAfterImport)
                    {
                        try
                        {
                            result.Compilation = Compile(plc.Name);
                        }
                        catch (Exception ex)
                        {
                            result.CompilationError = ex.Message;
                        }
                    }

                    if (openAfterImport)
                    {
                        try
                        {
                            importedBlock.ShowInEditor();
                            result.OpenedInEditor = true;
                        }
                        catch (Exception ex)
                        {
                            result.OpenError = ex.Message;
                        }
                    }

                    return result;
                }
                catch (Exception importException)
                {
                    SclImportRollbackResult rollback = RollBackSclImport(
                        plc,
                        blockName,
                        existing,
                        existingDeleted,
                        backupFile);

                    if (existing == null || !existingDeleted)
                    {
                        if (!string.IsNullOrEmpty(rollback.CleanupError))
                        {
                            throw new InvalidOperationException(
                                $"SCL import failed: {importException.Message} Cleanup also failed: {rollback.CleanupError}",
                                importException);
                        }

                        throw new InvalidOperationException($"SCL import failed: {importException.Message}", importException);
                    }

                    if (!rollback.Restored)
                    {
                        string cleanupDetails = string.IsNullOrEmpty(rollback.CleanupError)
                            ? ""
                            : $" Partial generated block cleanup also failed: {rollback.CleanupError}";
                        throw new InvalidOperationException(
                            $"SCL import failed: {importException.Message} The previous block could not be restored: {rollback.RestoreError}{cleanupDetails} Backup retained at '{backupFile}'.",
                            importException);
                    }

                    string rollbackMessage = "The previous block was restored.";
                    if (!string.IsNullOrEmpty(rollback.CleanupError))
                        rollbackMessage += $" Partial generated block cleanup also failed: {rollback.CleanupError}";

                    try
                    {
                        File.Delete(backupFile);
                    }
                    catch (Exception ex)
                    {
                        rollbackMessage += $" Backup could not be removed from '{backupFile}': {ex.Message}";
                    }

                    throw new InvalidOperationException(
                        $"SCL import failed: {importException.Message} {rollbackMessage}",
                        importException);
                }
            }
            finally
            {
                if (File.Exists(sourceFile))
                    File.Delete(sourceFile);

                if (File.Exists(backupFile) && !existingDeleted)
                    File.Delete(backupFile);
            }
        }

        private (string BlockName, string BlockKind) GetSclBlockDeclaration(string sclCode)
        {
            sclCode = sclCode.TrimStart('\uFEFF');
            string withoutComments = SclCommentsRegex.Replace(sclCode, string.Empty);
            MatchCollection declarations = SclBlockDeclarationRegex.Matches(withoutComments);
            if (declarations.Count != 1)
            {
                throw new ArgumentException(
                    $"SCL source must contain exactly one complete FUNCTION, FUNCTION_BLOCK, ORGANIZATION_BLOCK, or DATA_BLOCK declaration; found {declarations.Count}.");
            }

            Match declaration = declarations[0];
            string name = declaration.Groups["quoted"].Success
                ? declaration.Groups["quoted"].Value
                : declaration.Groups["identifier"].Value;
            return (name, declaration.Groups["kind"].Value.ToUpperInvariant());
        }

        private List<BlockLocation> FindBlockLocations(PlcBlockGroup root, string blockName)
        {
            var matches = new List<BlockLocation>();

            void Walk(PlcBlockGroup group, string folderPath)
            {
                foreach (PlcBlock block in group.Blocks)
                {
                    if (string.Equals(block.Name, blockName, StringComparison.OrdinalIgnoreCase))
                    {
                        matches.Add(new BlockLocation
                        {
                            Block = block,
                            Group = group,
                            FolderPath = folderPath
                        });
                    }
                }

                foreach (PlcBlockUserGroup subGroup in group.Groups)
                {
                    string subPath = string.IsNullOrEmpty(folderPath)
                        ? subGroup.Name
                        : $"{folderPath}/{subGroup.Name}";
                    Walk(subGroup, subPath);
                }
            }

            Walk(root, "");
            return matches;
        }

        private PlcBlockGroup FindBlockGroup(PlcBlockGroup root, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return root;

            PlcBlockGroup current = root;
            foreach (string part in path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries))
            {
                PlcBlockUserGroup? next = current.Groups.FirstOrDefault(
                    group => string.Equals(group.Name, part, StringComparison.OrdinalIgnoreCase));
                if (next == null)
                {
                    throw new ArgumentException($"PLC block group '{path}' was not found.", nameof(path));
                }

                current = next;
            }

            return current;
        }

        private SclImportRollbackResult RollBackSclImport(
            PlcSoftware plc,
            string blockName,
            BlockLocation? existing,
            bool existingDeleted,
            string backupFile)
        {
            var result = new SclImportRollbackResult();
            foreach (BlockLocation partialBlock in FindBlockLocations(plc.BlockGroup, blockName))
            {
                try
                {
                    partialBlock.Block.Delete();
                }
                catch (Exception ex)
                {
                    result.CleanupError = ex.Message;
                }
            }

            if (!existingDeleted || existing == null)
                return result;

            try
            {
                IList<PlcBlock> restored = existing.Group.Blocks.Import(
                    new FileInfo(backupFile),
                    ImportOptions.Override);
                if (restored.Count == 0)
                    throw new InvalidOperationException("TIA Portal did not report a restored block.");

                result.Restored = true;
            }
            catch (Exception ex)
            {
                result.RestoreError = ex.Message;
            }

            return result;
        }

        private sealed class BlockLocation
        {
            public PlcBlock Block { get; set; } = null!;
            public PlcBlockGroup Group { get; set; } = null!;
            public string FolderPath { get; set; } = "";
        }

        private sealed class SclImportRollbackResult
        {
            public bool Restored { get; set; }
            public string? RestoreError { get; set; }
            public string? CleanupError { get; set; }
        }

        public void DeleteBlock(string? plcName, string blockName)
        {
            var plc = FindPlc(plcName);
            PlcBlock? block = FindBlockRecursive(plc.BlockGroup, blockName);
            if (block == null)
                throw new ArgumentException($"Block '{blockName}' not found in PLC '{plc.Name}'.");

            block.Delete();
        }

        private PlcBlock? FindBlockRecursive(PlcBlockGroup group, string name)
        {
            foreach (PlcBlock b in group.Blocks)
            {
                if (string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase))
                    return b;
            }
            foreach (PlcBlockUserGroup sub in group.Groups)
            {
                var found = FindBlockRecursive(sub, name);
                if (found != null) return found;
            }
            return null;
        }

        private PlcBlockGroup GetOrCreateBlockUserGroup(PlcBlockGroup root, string path)
        {
            string[] parts = path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            PlcBlockGroup current = root;

            foreach (var part in parts)
            {
                PlcBlockUserGroup? next = null;
                foreach (PlcBlockUserGroup sub in current.Groups)
                {
                    if (string.Equals(sub.Name, part, StringComparison.OrdinalIgnoreCase))
                    {
                        next = sub;
                        break;
                    }
                }
                if (next == null)
                {
                    next = current.Groups.Create(part);
                }
                current = next;
            }

            return current;
        }

        #endregion

        #region User Data Types (UDTs)

        public List<PlcTypeInfo> ListPlcTypes(string? plcName = null)
        {
            var plc = FindPlc(plcName);
            var result = new List<PlcTypeInfo>();
            CollectTypes(plc.TypeGroup, "", result);
            return result;
        }

        private void CollectTypes(PlcTypeGroup group, string currentPath, List<PlcTypeInfo> result)
        {
            foreach (PlcType type in group.Types)
            {
                result.Add(new PlcTypeInfo
                {
                    Name = type.Name,
                    GroupPath = currentPath,
                    IsConsistent = (bool?)GetSafeAttributeObj(type, "IsConsistent") ?? true
                });
            }

            foreach (PlcTypeUserGroup sub in group.Groups)
            {
                string nextPath = string.IsNullOrEmpty(currentPath) ? sub.Name : $"{currentPath}/{sub.Name}";
                CollectTypes(sub, nextPath, result);
            }
        }

        public string GetPlcType(string? plcName, string typeName)
        {
            var plc = FindPlc(plcName);
            PlcType? target = FindTypeRecursive(plc.TypeGroup, typeName);
            if (target == null)
                throw new ArgumentException($"PLC Type (UDT) '{typeName}' not found in PLC '{plc.Name}'.");

            string tempFile = Path.Combine(Path.GetTempPath(), $"tia_type_{Guid.NewGuid():N}.xml");
            try
            {
                target.Export(new FileInfo(tempFile), ExportOptions.WithDefaults);
                return File.ReadAllText(tempFile);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        public string ImportPlcType(string? plcName, string xmlContent, bool overwrite = true)
        {
            var plc = FindPlc(plcName);
            string tempFile = Path.Combine(Path.GetTempPath(), $"tia_type_import_{Guid.NewGuid():N}.xml");
            try
            {
                File.WriteAllText(tempFile, xmlContent);
                var options = overwrite ? ImportOptions.Override : ImportOptions.None;
                IList<PlcType> importedTypes = plc.TypeGroup.Types.Import(new FileInfo(tempFile), options);
                var first = importedTypes.Count > 0 ? importedTypes[0] : null;

                return first != null
                    ? $"Successfully imported PLC Type '{first.Name}'. Total imported: {importedTypes.Count}."
                    : "Successfully imported PLC Type(s).";
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        private PlcType? FindTypeRecursive(PlcTypeGroup group, string name)
        {
            foreach (PlcType t in group.Types)
            {
                if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
                    return t;
            }
            foreach (PlcTypeUserGroup sub in group.Groups)
            {
                var found = FindTypeRecursive(sub, name);
                if (found != null) return found;
            }
            return null;
        }

        #endregion

        #region Tag Tables & Tags

        public List<string> ListTagTables(string? plcName = null)
        {
            var plc = FindPlc(plcName);
            var list = new List<string>();
            CollectTagTables(plc.TagTableGroup, "", list);
            return list;
        }

        private void CollectTagTables(PlcTagTableGroup group, string currentPath, List<string> list)
        {
            foreach (PlcTagTable table in group.TagTables)
            {
                list.Add(string.IsNullOrEmpty(currentPath) ? table.Name : $"{currentPath}/{table.Name}");
            }
            foreach (PlcTagTableUserGroup sub in group.Groups)
            {
                string next = string.IsNullOrEmpty(currentPath) ? sub.Name : $"{currentPath}/{sub.Name}";
                CollectTagTables(sub, next, list);
            }
        }

        public List<TagInfo> GetTags(string? plcName, string? tableName = null)
        {
            var plc = FindPlc(plcName);
            var list = new List<TagInfo>();

            void ExtractTags(PlcTagTable tbl)
            {
                foreach (PlcTag tag in tbl.Tags)
                {
                    list.Add(new TagInfo
                    {
                        Name = tag.Name,
                        DataType = tag.DataTypeName,
                        LogicalAddress = tag.LogicalAddress,
                        Comment = GetSafeAttribute(tag, "Comment"),
                        TableName = tbl.Name
                    });
                }
            }

            void WalkTagGroup(PlcTagTableGroup group)
            {
                foreach (PlcTagTable table in group.TagTables)
                {
                    if (string.IsNullOrEmpty(tableName) || string.Equals(table.Name, tableName, StringComparison.OrdinalIgnoreCase))
                    {
                        ExtractTags(table);
                    }
                }
                foreach (PlcTagTableUserGroup sub in group.Groups)
                {
                    WalkTagGroup(sub);
                }
            }

            WalkTagGroup(plc.TagTableGroup);
            return list;
        }

        #endregion

        #region Compilation

        public CompilationReport Compile(string? plcName = null)
        {
            var plc = FindPlc(plcName);
            var compilable = plc.GetService<ICompilable>();
            if (compilable == null)
            {
                return new CompilationReport
                {
                    State = "Error",
                    ErrorCount = 1,
                    Messages = new List<CompileMessageItem>
                    {
                        new CompileMessageItem { Description = $"PlcSoftware '{plc.Name}' does not provide an ICompilable service." }
                    }
                };
            }

            Console.Error.WriteLine($"[TiaManager] Compiling PLC Software '{plc.Name}'...");
            CompilerResult result = compilable.Compile();

            var report = new CompilationReport
            {
                State = result.State.ToString(),
                ErrorCount = result.ErrorCount,
                WarningCount = result.WarningCount
            };

            CollectCompileMessages(result.Messages, report.Messages);
            return report;
        }

        private void CollectCompileMessages(CompilerResultMessageComposition messages, List<CompileMessageItem> list)
        {
            foreach (CompilerResultMessage msg in messages)
            {
                list.Add(new CompileMessageItem
                {
                    DateTime = msg.DateTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    Description = msg.Description,
                    Path = msg.Path,
                    State = msg.State.ToString(),
                    WarningCount = msg.WarningCount,
                    ErrorCount = msg.ErrorCount
                });

                if (msg.Messages.Count > 0)
                {
                    CollectCompileMessages(msg.Messages, list);
                }
            }
        }

        #endregion

        #region Helpers

        private void EnsureProjectOpen()
        {
            if (_tiaPortal == null || _project == null)
            {
                // Attempt automatic reconnect if instance is running
                var result = Connect();
                if (!result.Success || _project == null)
                {
                    throw new InvalidOperationException("No TIA Portal project is currently connected. Call 'tia_connect' first.");
                }
            }
        }

        private int CountBlocks(PlcBlockGroup group)
        {
            int count = group.Blocks.Count;
            foreach (PlcBlockGroup sub in group.Groups) count += CountBlocks(sub);
            return count;
        }

        private int CountTypes(PlcTypeGroup group)
        {
            int count = group.Types.Count;
            foreach (PlcTypeGroup sub in group.Groups) count += CountTypes(sub);
            return count;
        }

        private int CountTagTables(PlcTagTableGroup group)
        {
            int count = group.TagTables.Count;
            foreach (PlcTagTableGroup sub in group.Groups) count += CountTagTables(sub);
            return count;
        }

        private string GetSafeAttribute(IEngineeringObject obj, string attributeName)
        {
            try
            {
                var val = obj.GetAttribute(attributeName);
                return val?.ToString() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private object? GetSafeAttributeObj(IEngineeringObject obj, string attributeName)
        {
            try
            {
                return obj.GetAttribute(attributeName);
            }
            catch
            {
                return null;
            }
        }

        public void Dispose()
        {
            Disconnect();
        }

        #endregion
    }

    #region DTO Models

    public class ProcessInfo
    {
        public int Id { get; set; }
        public string ProjectPath { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public string Mode { get; set; } = "";
    }

    public class ConnectionResult
    {
        public bool Success { get; set; }
        public int? ProcessId { get; set; }
        public string ProjectName { get; set; } = "";
        public string ProjectPath { get; set; } = "";
        public string Message { get; set; } = "";
    }

    public class ProjectInfoDetails
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string Author { get; set; } = "";
        public string Comment { get; set; } = "";
        public string DateCreated { get; set; } = "";
        public string DateModified { get; set; } = "";
        public bool IsModified { get; set; }
        public int? AttachedProcessId { get; set; }
    }

    public class DeviceInfo
    {
        public string Name { get; set; } = "";
        public string TypeIdentifier { get; set; } = "";
        public string DeviceGroup { get; set; } = "";
        public bool HasPlc { get; set; }
        public List<string> PlcNames { get; } = new List<string>();
    }

    public class PlcSoftwareSummary
    {
        public string PlcName { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public string ItemName { get; set; } = "";
        public int BlockCount { get; set; }
        public int TypeCount { get; set; }
        public int TagTableCount { get; set; }
    }

    public class DeviceItemNode
    {
        public string Name { get; set; } = "";
        public string TypeIdentifier { get; set; } = "";
        public string NodeType { get; set; } = "";
        public string? SoftwareName { get; set; }
        public List<DeviceItemNode> Children { get; } = new List<DeviceItemNode>();
    }

    public class PlcBlockInfo
    {
        public string Name { get; set; } = "";
        public int Number { get; set; }
        public string BlockType { get; set; } = "";
        public string ProgrammingLanguage { get; set; } = "";
        public string GroupPath { get; set; } = "";
        public bool IsConsistent { get; set; }
        public bool IsKnowHowProtected { get; set; }
    }

    public class BlockExportResult
    {
        public string Name { get; set; } = "";
        public int Number { get; set; }
        public string BlockType { get; set; } = "";
        public string ProgrammingLanguage { get; set; } = "";
        public string XmlContent { get; set; } = "";
        public string? ExtractedScl { get; set; }
    }

    public class SclBlockImportResult
    {
        public bool Imported { get; set; }
        public string BlockName { get; set; } = "";
        public string BlockKind { get; set; } = "";
        public string Folder { get; set; } = "";
        public bool ReplacedExistingBlock { get; set; }
        public CompilationReport? Compilation { get; set; }
        public string? CompilationError { get; set; }
        public bool? OpenedInEditor { get; set; }
        public string? OpenError { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public class PlcTypeInfo
    {
        public string Name { get; set; } = "";
        public string GroupPath { get; set; } = "";
        public bool IsConsistent { get; set; }
    }

    public class TagInfo
    {
        public string Name { get; set; } = "";
        public string DataType { get; set; } = "";
        public string LogicalAddress { get; set; } = "";
        public string Comment { get; set; } = "";
        public string TableName { get; set; } = "";
    }

    public class CompilationReport
    {
        public string State { get; set; } = "";
        public int ErrorCount { get; set; }
        public int WarningCount { get; set; }
        public List<CompileMessageItem> Messages { get; set; } = new List<CompileMessageItem>();
    }

    public class CompileMessageItem
    {
        public string DateTime { get; set; } = "";
        public string Description { get; set; } = "";
        public string Path { get; set; } = "";
        public string State { get; set; } = "";
        public int WarningCount { get; set; }
        public int ErrorCount { get; set; }
    }

    public class ServerConnectionStatus
    {
        public bool IsConnected { get; set; }
        public int? AttachedProcessId { get; set; }
        public string ProjectName { get; set; } = "";
        public string ProjectPath { get; set; } = "";
        public bool OwnsPortalInstance { get; set; }
    }

    #endregion
}
