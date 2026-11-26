using System;
using System.Collections.Generic;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HW.Utilities;

namespace TiaOpennessMcp.Tia
{
    // Hardware configuration (Devices & networks) through TIA Portal Openness V21:
    // devices, modules, plug locations, subnets, IO systems, node addresses and IO addresses.
    // Every API used here is documented in "TIA Portal Openness: API for automation of engineering workflows V21"
    // (Functions on devices / device items / networks).
    public partial class TiaManager
    {
        #region Device and item resolution

        private Device FindDeviceExact(string deviceName)
        {
            EnsureProjectOpen();
            if (string.IsNullOrWhiteSpace(deviceName))
                throw new ArgumentException("device_name is required.");

            var matches = new List<Device>();
            foreach (Device dev in _project!.Devices)
                if (string.Equals(dev.Name, deviceName, StringComparison.OrdinalIgnoreCase)) matches.Add(dev);

            void SearchGroup(DeviceUserGroup grp)
            {
                foreach (Device dev in grp.Devices)
                    if (string.Equals(dev.Name, deviceName, StringComparison.OrdinalIgnoreCase)) matches.Add(dev);
                foreach (DeviceUserGroup sub in grp.Groups) SearchGroup(sub);
            }
            foreach (DeviceUserGroup grp in _project.DeviceGroups) SearchGroup(grp);

            // The ungrouped devices folder ("Ungrouped devices") holds the distributed IO stations.
            try
            {
                foreach (Device dev in _project.UngroupedDevicesGroup.Devices)
                    if (string.Equals(dev.Name, deviceName, StringComparison.OrdinalIgnoreCase) && !matches.Contains(dev)) matches.Add(dev);
            }
            catch (EngineeringException) { }

            if (matches.Count == 0)
                throw new ArgumentException($"Device '{deviceName}' was not found. Call tia_list_devices to see the device names.");
            var exact = matches.Where(d => string.Equals(d.Name, deviceName, StringComparison.Ordinal)).ToList();
            if (exact.Count == 1) return exact[0];
            if (matches.Count > 1)
                throw new ArgumentException($"Device name '{deviceName}' is ambiguous ({matches.Count} devices).");
            return matches[0];
        }

        private IEnumerable<Device> EnumerateAllDevices()
        {
            EnsureProjectOpen();
            var seen = new HashSet<Device>();
            foreach (Device dev in _project!.Devices)
                if (seen.Add(dev)) yield return dev;

            var stack = new Stack<DeviceUserGroup>();
            foreach (DeviceUserGroup grp in _project.DeviceGroups) stack.Push(grp);
            while (stack.Count > 0)
            {
                var grp = stack.Pop();
                foreach (Device dev in grp.Devices)
                    if (seen.Add(dev)) yield return dev;
                foreach (DeviceUserGroup sub in grp.Groups) stack.Push(sub);
            }

            DeviceComposition? ungrouped = null;
            try { ungrouped = _project.UngroupedDevicesGroup.Devices; } catch (EngineeringException) { }
            if (ungrouped != null)
                foreach (Device dev in ungrouped)
                    if (seen.Add(dev)) yield return dev;
        }

        private static IEnumerable<DeviceItem> EnumerateItems(DeviceItemComposition items)
        {
            foreach (DeviceItem item in items)
            {
                yield return item;
                foreach (var child in EnumerateItems(item.DeviceItems)) yield return child;
            }
        }

        /// <summary>Slash-separated path of an item below its device, e.g. "PLC_1/PROFINET interface_1".</summary>
        private static string GetItemPath(DeviceItem item)
        {
            var parts = new List<string>();
            IEngineeringObject? current = item;
            while (current is DeviceItem di)
            {
                parts.Add(di.Name);
                current = di.Parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>
        /// Resolves a hardware object: the device itself when item_path is empty, otherwise the device item
        /// following the slash-separated path of item names below the device. A single name that is not a
        /// direct child is also searched in the whole device tree and must be unique.
        /// </summary>
        private HardwareObject ResolveHardwareObject(Device device, string? itemPath)
        {
            if (string.IsNullOrWhiteSpace(itemPath)) return device;
            string path = itemPath!.Trim().Trim('/');

            // Item names may themselves contain '/' (e.g. "DI 14/DQ 10_1"), so match child names against
            // the remaining path text, longest name first, instead of splitting on '/'.
            HardwareObject? Walk(HardwareObject node, string remaining, StringComparison cmp)
            {
                foreach (var child in node.DeviceItems.Cast<DeviceItem>().OrderByDescending(c => c.Name.Length))
                {
                    if (string.Equals(child.Name, remaining, cmp)) return child;
                    if (remaining.StartsWith(child.Name + "/", cmp))
                    {
                        var hit = Walk(child, remaining.Substring(child.Name.Length + 1), cmp);
                        if (hit != null) return hit;
                    }
                }
                return null;
            }

            var walked = Walk(device, path, StringComparison.Ordinal)
                ?? (path.StartsWith(device.Name + "/", StringComparison.Ordinal) ? Walk(device, path.Substring(device.Name.Length + 1), StringComparison.Ordinal) : null)
                ?? Walk(device, path, StringComparison.OrdinalIgnoreCase);
            if (walked != null) return walked;

            var found = EnumerateItems(device.DeviceItems)
                .Where(i => string.Equals(i.Name, path, StringComparison.Ordinal)).ToList();
            if (found.Count == 1) return found[0];
            if (found.Count > 1)
                throw new ArgumentException($"Item name '{path}' occurs {found.Count} times in device '{device.Name}'. Use the full item path: " +
                    string.Join(", ", found.Select(GetItemPath)));

            throw new ArgumentException($"Item path '{itemPath}' was not found in device '{device.Name}'. Call tia_get_device_tree or tia_list_plug_locations to see the item names.");
        }

        private ModuleInformationProvider GetModuleInformationProvider()
        {
            EnsureProjectOpen();
            var provider = _project!.HwUtilities.Find("ModuleInformationProvider") as ModuleInformationProvider;
            if (provider == null)
                throw new InvalidOperationException("The ModuleInformationProvider hardware utility is not available in this project.");
            return provider;
        }

        private static string Describe(Exception ex) => $"{ex.GetType().Name}: {ex.Message}";

        #endregion

        #region Catalog lookup

        public HardwareOperationResult FindModuleTypes(string partialTypeIdentifier)
        {
            if (string.IsNullOrWhiteSpace(partialTypeIdentifier))
                throw new ArgumentException("partial_type_identifier is required.");
            var provider = GetModuleInformationProvider();
            try
            {
                var types = provider.FindModuleTypes(partialTypeIdentifier.Trim()) ?? new string[0];
                var list = types.OrderBy(t => t, StringComparer.Ordinal).ToList();
                var containers = new Dictionary<string, string[]>();
                foreach (var t in list.Take(20))
                {
                    try { containers[t] = provider.FindContainerTypes(t) ?? new string[0]; }
                    catch (EngineeringException) { }
                }
                return new HardwareOperationResult
                {
                    Success = true,
                    Message = list.Count == 0
                        ? "No catalog entries match. OrderNumber needs at least one complete part (OrderNumber:6ES7 214-1AG40-0XB0), GSD at least two (GSD:GSDML-V2.4-ABB-FPNO-20220101.XML/D), System at least one. Wildcards are not supported. A GSD device must be installed in TIA Portal first (Options > Manage GSD files)."
                        : $"{list.Count} matching type identifiers.",
                    Data = new { TypeIdentifiers = list, ContainerTypes = containers }
                };
            }
            catch (EngineeringException ex)
            {
                return HardwareOperationResult.Fail($"FindModuleTypes failed: {Describe(ex)}");
            }
        }

        #endregion

        #region Devices and modules

        public HardwareOperationResult CreateDevice(string typeIdentifier, string deviceItemName, string? deviceName, bool dryRun)
        {
            EnsureProjectOpen();
            if (string.IsNullOrWhiteSpace(typeIdentifier)) throw new ArgumentException("type_identifier is required.");
            if (string.IsNullOrWhiteSpace(deviceItemName)) throw new ArgumentException("device_item_name is required.");

            var existingNames = EnumerateAllDevices().Select(d => d.Name).ToList();
            if (!string.IsNullOrWhiteSpace(deviceName) &&
                existingNames.Any(n => string.Equals(n, deviceName, StringComparison.OrdinalIgnoreCase)))
                return HardwareOperationResult.Fail($"A device named '{deviceName}' already exists. Nothing was created.");

            string[] containers = new string[0];
            string? catalogWarning = null;
            try { containers = GetModuleInformationProvider().FindContainerTypes(typeIdentifier) ?? new string[0]; }
            catch (EngineeringException ex) { catalogWarning = $"FindContainerTypes could not validate the type identifier: {ex.Message}"; }

            if (dryRun)
            {
                return new HardwareOperationResult
                {
                    Success = true,
                    DryRun = true,
                    Message = containers.Length > 0
                        ? "Type identifier found in the hardware catalog. Run again with dry_run=false to create the device."
                        : "The catalog did not confirm the type identifier (FindContainerTypes works only for modules shown in the network view). Creation may still work; run with dry_run=false to try.",
                    Data = new { TypeIdentifier = typeIdentifier, DeviceItemName = deviceItemName, DeviceName = deviceName, ContainerTypes = containers, CatalogWarning = catalogWarning }
                };
            }

            try
            {
                Device device = _project!.Devices.CreateWithItem(typeIdentifier, deviceItemName, string.IsNullOrWhiteSpace(deviceName) ? null : deviceName);
                return new HardwareOperationResult
                {
                    Success = true,
                    Message = $"Device '{device.Name}' created. Save the project with tia_save_project.",
                    Data = new
                    {
                        DeviceName = device.Name,
                        device.TypeIdentifier,
                        Items = EnumerateItems(device.DeviceItems).Select(i => new { Path = GetItemPath(i), i.TypeIdentifier, i.PositionNumber }).ToList()
                    }
                };
            }
            catch (EngineeringException ex)
            {
                return HardwareOperationResult.Fail($"CreateWithItem failed: {Describe(ex)}. Check the type identifier with tia_find_module_types; GSD devices must be installed in TIA Portal first.");
            }
        }

        public HardwareOperationResult ListPlugLocations(string deviceName, string? itemPath)
        {
            var device = FindDeviceExact(deviceName);
            var targets = new List<HardwareObject>();
            if (!string.IsNullOrWhiteSpace(itemPath)) targets.Add(ResolveHardwareObject(device, itemPath));
            else
            {
                targets.Add(device);
                targets.AddRange(EnumerateItems(device.DeviceItems));
            }

            var result = new List<object>();
            foreach (var hw in targets)
            {
                IList<Siemens.Engineering.HW.Extensions.PlugLocation>? free = null;
                string? error = null;
                try { free = hw.GetPlugLocations(); }
                catch (EngineeringException ex) { error = ex.Message; }

                bool explicitTarget = !string.IsNullOrWhiteSpace(itemPath);
                if (!explicitTarget && (free == null || free.Count == 0)) continue;

                result.Add(new
                {
                    ItemPath = hw is DeviceItem di ? GetItemPath(di) : "",
                    hw.Name,
                    hw.TypeIdentifier,
                    FreeLocations = free?.Select(p => new { p.PositionNumber, p.Label }).OrderBy(p => p.PositionNumber).ToList(),
                    Plugged = hw.DeviceItems.Cast<DeviceItem>().Select(c => new { c.Name, c.PositionNumber, c.TypeIdentifier }).ToList(),
                    Error = error
                });
            }

            return new HardwareOperationResult
            {
                Success = true,
                Message = result.Count == 0 ? "No free plug locations found." : $"{result.Count} hardware objects with free plug locations.",
                Data = result
            };
        }

        public HardwareOperationResult PlugModule(string deviceName, string? parentItemPath, string typeIdentifier, string name, int positionNumber, bool dryRun)
        {
            if (string.IsNullOrWhiteSpace(typeIdentifier)) throw new ArgumentException("type_identifier is required.");
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("name is required.");
            var device = FindDeviceExact(deviceName);
            var parent = ResolveHardwareObject(device, parentItemPath);

            bool canPlug;
            try { canPlug = parent.CanPlugNew(typeIdentifier, name, positionNumber); }
            catch (EngineeringException ex) { return HardwareOperationResult.Fail($"CanPlugNew failed: {Describe(ex)}"); }

            string parentPath = parent is DeviceItem pdi ? GetItemPath(pdi) : "";
            if (dryRun || !canPlug)
            {
                return new HardwareOperationResult
                {
                    Success = canPlug,
                    DryRun = dryRun,
                    Message = canPlug
                        ? "CanPlugNew returned true. Run again with dry_run=false to plug the module."
                        : $"CanPlugNew returned false: '{typeIdentifier}' cannot be plugged as '{name}' at position {positionNumber} of '{(parentPath.Length == 0 ? device.Name : parentPath)}'. Check free positions with tia_list_plug_locations and the name for duplicates.",
                    Data = new { Device = device.Name, ParentItemPath = parentPath, TypeIdentifier = typeIdentifier, Name = name, PositionNumber = positionNumber, CanPlug = canPlug }
                };
            }

            try
            {
                var item = parent.PlugNew(typeIdentifier, name, positionNumber);
                return new HardwareOperationResult
                {
                    Success = true,
                    Message = $"Plugged '{item.Name}' at position {item.PositionNumber}. Save the project with tia_save_project.",
                    Data = new
                    {
                        ItemPath = GetItemPath(item),
                        item.TypeIdentifier,
                        item.PositionNumber,
                        Addresses = new[] { item }.Concat(EnumerateItems(item.DeviceItems))
                            .Select(i => new { ItemPath = GetItemPath(i), Addresses = ReadAddresses(i) })
                            .Where(a => a.Addresses.Count > 0).ToList()
                    }
                };
            }
            catch (EngineeringException ex)
            {
                return HardwareOperationResult.Fail($"PlugNew failed: {Describe(ex)}");
            }
        }

        public HardwareOperationResult DeleteHardware(string deviceName, string? itemPath, bool confirm)
        {
            var device = FindDeviceExact(deviceName);
            var target = ResolveHardwareObject(device, itemPath);
            var children = EnumerateItems(target.DeviceItems).Select(GetItemPath).ToList();
            string label = target is DeviceItem di ? $"item '{GetItemPath(di)}' of device '{device.Name}'" : $"device '{device.Name}'";

            if (!confirm)
            {
                return new HardwareOperationResult
                {
                    Success = true,
                    DryRun = true,
                    Message = $"Preview only: would delete {label} and {children.Count} sub-items. Run again with confirm=true to delete. Tag and block references to its addresses are not changed.",
                    Data = new { Target = label, SubItems = children }
                };
            }

            try
            {
                if (target is Device d) d.Delete();
                else ((DeviceItem)target).Delete();
                return new HardwareOperationResult { Success = true, Message = $"Deleted {label}. Save the project with tia_save_project." };
            }
            catch (EngineeringException ex)
            {
                return HardwareOperationResult.Fail($"Delete failed for {label}: {Describe(ex)}. Built-in items (CPU, interfaces, ports) cannot be deleted.");
            }
        }

        public HardwareOperationResult RenameHardware(string deviceName, string? itemPath, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName)) throw new ArgumentException("new_name is required.");
            var device = FindDeviceExact(deviceName);
            var target = ResolveHardwareObject(device, itemPath);
            string oldName = target.Name;

            if (target is Device && EnumerateAllDevices().Any(d => d != target && string.Equals(d.Name, newName, StringComparison.OrdinalIgnoreCase)))
                return HardwareOperationResult.Fail($"A device named '{newName}' already exists.");

            try
            {
                if (target is Device d) d.Name = newName;
                else ((DeviceItem)target).Name = newName;
                return new HardwareOperationResult
                {
                    Success = true,
                    Message = $"Renamed '{oldName}' to '{target.Name}'. Save the project with tia_save_project.",
                    Data = new { OldName = oldName, NewName = target.Name }
                };
            }
            catch (EngineeringException ex)
            {
                return HardwareOperationResult.Fail($"Rename failed: {Describe(ex)}");
            }
        }

        #endregion

        #region IO addresses

        private static List<object> ReadAddresses(DeviceItem item)
        {
            var list = new List<object>();
            AddressComposition? addresses = null;
            try { addresses = item.Addresses; } catch (EngineeringException) { }
            if (addresses == null) return list;
            foreach (Address address in addresses)
            {
                object? ioType = null, start = null, length = null;
                try { ioType = address.IoType.ToString(); } catch (EngineeringException) { }
                try { start = address.StartAddress; } catch (EngineeringException) { }
                try { length = address.Length; } catch (EngineeringException) { }
                list.Add(new { IoType = ioType, StartAddress = start, Length = length });
            }
            return list;
        }

        public HardwareOperationResult GetIoAddresses(string? deviceName)
        {
            EnsureProjectOpen();
            var devices = string.IsNullOrWhiteSpace(deviceName)
                ? EnumerateAllDevices().ToList()
                : new List<Device> { FindDeviceExact(deviceName!) };

            var rows = new List<object>();
            foreach (var dev in devices)
            {
                foreach (var item in EnumerateItems(dev.DeviceItems))
                {
                    var addresses = ReadAddresses(item);
                    if (addresses.Count == 0) continue;
                    rows.Add(new { Device = dev.Name, ItemPath = GetItemPath(item), item.TypeIdentifier, Addresses = addresses });
                }
            }
            return new HardwareOperationResult
            {
                Success = true,
                Message = $"{rows.Count} device items with IO addresses. StartAddress is the byte address (%IB/%QB); Length is the Openness Address.Length value, which TIA reports in bits (8 = 1 byte).",
                Data = rows
            };
        }

        #endregion

        #region Networks

        private sealed class InterfaceRef
        {
            public DeviceItem Item = null!;
            public NetworkInterface Interface = null!;
        }

        private static List<InterfaceRef> FindInterfaces(Device device)
        {
            var list = new List<InterfaceRef>();
            foreach (var item in EnumerateItems(device.DeviceItems))
            {
                NetworkInterface? ni = null;
                try { ni = item.GetService<NetworkInterface>(); } catch (EngineeringException) { }
                if (ni != null && ni.Nodes.Count > 0) list.Add(new InterfaceRef { Item = item, Interface = ni });
            }
            return list;
        }

        private InterfaceRef ResolveInterface(Device device, string? interfacePath)
        {
            if (!string.IsNullOrWhiteSpace(interfacePath))
            {
                var hw = ResolveHardwareObject(device, interfacePath);
                var item = hw as DeviceItem ?? throw new ArgumentException("interface_path must name a device item.");
                var ni = item.GetService<NetworkInterface>();
                if (ni == null || ni.Nodes.Count == 0)
                    throw new ArgumentException($"'{GetItemPath(item)}' is not a network interface with nodes.");
                return new InterfaceRef { Item = item, Interface = ni };
            }

            var all = FindInterfaces(device);
            if (all.Count == 0) throw new ArgumentException($"Device '{device.Name}' has no network interface with nodes.");
            var ethernet = all.Where(i => i.Interface.Nodes.Cast<Node>().Any(n => SafeNodeType(n) == "Ethernet")).ToList();
            var candidates = ethernet.Count > 0 ? ethernet : all;
            if (candidates.Count > 1)
                throw new ArgumentException($"Device '{device.Name}' has {candidates.Count} network interfaces; pass interface_path: " +
                    string.Join(", ", candidates.Select(c => GetItemPath(c.Item))));
            return candidates[0];
        }

        private static string SafeNodeType(Node node)
        {
            try { return node.NodeType.ToString(); } catch (EngineeringException) { return ""; }
        }

        private static object? SafeAttr(IEngineeringObject obj, string name)
        {
            try { return obj.GetAttribute(name); } catch (EngineeringException) { return null; } catch (Exception) { return null; }
        }

        private Subnet FindSubnet(string subnetName)
        {
            EnsureProjectOpen();
            foreach (Subnet s in _project!.Subnets)
                if (string.Equals(s.Name, subnetName, StringComparison.OrdinalIgnoreCase)) return s;
            throw new ArgumentException($"Subnet '{subnetName}' was not found. Call tia_list_network to see the subnets.");
        }

        private IoSystem FindIoSystem(string ioSystemName)
        {
            EnsureProjectOpen();
            foreach (Subnet s in _project!.Subnets)
                foreach (IoSystem io in s.IoSystems)
                    if (string.Equals(io.Name, ioSystemName, StringComparison.OrdinalIgnoreCase)) return io;
            throw new ArgumentException($"IO system '{ioSystemName}' was not found. Call tia_list_network to see the IO systems.");
        }

        private static object DescribeNode(Node node)
        {
            var obj = (IEngineeringObject)node;
            Subnet? connected = null;
            try { connected = node.ConnectedSubnet; } catch (EngineeringException) { }
            return new
            {
                node.Name,
                NodeType = SafeNodeType(node),
                Subnet = connected?.Name,
                Address = SafeAttr(obj, "Address")?.ToString(),
                SubnetMask = SafeAttr(obj, "SubnetMask")?.ToString(),
                UseRouter = SafeAttr(obj, "UseRouter"),
                RouterAddress = SafeAttr(obj, "RouterAddress")?.ToString(),
                IpProtocolSelection = SafeAttr(obj, "IpProtocolSelection")?.ToString(),
                PnDeviceNameAutoGeneration = SafeAttr(obj, "PnDeviceNameAutoGeneration"),
                PnDeviceName = SafeAttr(obj, "PnDeviceName")?.ToString(),
                PnDeviceNameConverted = SafeAttr(obj, "PnDeviceNameConverted")?.ToString()
            };
        }

        public HardwareOperationResult ListNetwork(string? deviceName)
        {
            EnsureProjectOpen();
            var subnets = new List<object>();
            foreach (Subnet s in _project!.Subnets)
            {
                var ioSystems = new List<object>();
                foreach (IoSystem io in s.IoSystems)
                {
                    var connected = new List<string>();
                    try
                    {
                        foreach (IoConnector c in io.ConnectedIoDevices)
                        {
                            var item = c.Parent?.Parent as DeviceItem;
                            connected.Add(item != null ? $"{GetDeviceOf(item)?.Name}/{GetItemPath(item)}" : "?");
                        }
                    }
                    catch (EngineeringException) { }
                    ioSystems.Add(new { io.Name, io.Number, ConnectedIoDevices = connected });
                }
                string netType = "";
                try { netType = s.NetType.ToString(); } catch (EngineeringException) { }
                subnets.Add(new { s.Name, s.TypeIdentifier, NetType = netType, NodeCount = s.Nodes.Count, IoSystems = ioSystems });
            }

            var devices = string.IsNullOrWhiteSpace(deviceName) ? EnumerateAllDevices().ToList() : new List<Device> { FindDeviceExact(deviceName!) };
            var interfaces = new List<object>();
            foreach (var dev in devices)
            {
                foreach (var iref in FindInterfaces(dev))
                {
                    var controllers = new List<object>();
                    foreach (IoController ctrl in iref.Interface.IoControllers)
                    {
                        IoSystem? sys = null;
                        try { sys = ctrl.IoSystem; } catch (EngineeringException) { }
                        controllers.Add(new { IoSystem = sys?.Name });
                    }
                    var connectors = new List<object>();
                    foreach (IoConnector conn in iref.Interface.IoConnectors)
                    {
                        IoSystem? sys = null;
                        try { sys = conn.ConnectedToIoSystem; } catch (EngineeringException) { }
                        connectors.Add(new { ConnectedToIoSystem = sys?.Name, DeviceNumber = SafeAttr(conn, "PnDeviceNumber") });
                    }
                    string opMode = "";
                    try { opMode = iref.Interface.InterfaceOperatingMode.ToString(); } catch (EngineeringException) { }
                    interfaces.Add(new
                    {
                        Device = dev.Name,
                        InterfacePath = GetItemPath(iref.Item),
                        InterfaceOperatingMode = opMode,
                        Nodes = iref.Interface.Nodes.Cast<Node>().Select(DescribeNode).ToList(),
                        IoControllers = controllers,
                        IoConnectors = connectors
                    });
                }
            }

            return new HardwareOperationResult
            {
                Success = true,
                Message = $"{subnets.Count} subnets, {interfaces.Count} network interfaces.",
                Data = new { Subnets = subnets, Interfaces = interfaces }
            };
        }

        private static Device? GetDeviceOf(DeviceItem item)
        {
            IEngineeringObject? current = item;
            while (current != null && !(current is Device)) current = current.Parent;
            return current as Device;
        }

        public HardwareOperationResult CreateSubnet(string subnetName, string? typeIdentifier)
        {
            EnsureProjectOpen();
            if (string.IsNullOrWhiteSpace(subnetName)) throw new ArgumentException("subnet_name is required.");
            foreach (Subnet s in _project!.Subnets)
                if (string.Equals(s.Name, subnetName, StringComparison.OrdinalIgnoreCase))
                    return HardwareOperationResult.Fail($"Subnet '{subnetName}' already exists.");
            string type = string.IsNullOrWhiteSpace(typeIdentifier) ? "System:Subnet.Ethernet" : typeIdentifier!;
            try
            {
                var subnet = _project.Subnets.Create(type, subnetName);
                return new HardwareOperationResult
                {
                    Success = true,
                    Message = $"Subnet '{subnet.Name}' ({subnet.TypeIdentifier}) created. Connect interfaces with tia_connect_to_subnet.",
                    Data = new { subnet.Name, subnet.TypeIdentifier }
                };
            }
            catch (EngineeringException ex)
            {
                return HardwareOperationResult.Fail($"Subnets.Create failed: {Describe(ex)}");
            }
        }

        public HardwareOperationResult ConnectToSubnet(string deviceName, string? interfacePath, string subnetName)
        {
            var device = FindDeviceExact(deviceName);
            var iref = ResolveInterface(device, interfacePath);
            var subnet = FindSubnet(subnetName);
            var node = iref.Interface.Nodes[0];
            try
            {
                var current = node.ConnectedSubnet;
                if (current != null && current.Equals(subnet))
                    return new HardwareOperationResult { Success = true, Message = $"'{GetItemPath(iref.Item)}' is already connected to '{subnet.Name}'.", Data = DescribeNode(node) };
                node.ConnectToSubnet(subnet);
                return new HardwareOperationResult
                {
                    Success = true,
                    Message = $"Connected '{device.Name}/{GetItemPath(iref.Item)}' to subnet '{subnet.Name}'.",
                    Data = DescribeNode(node)
                };
            }
            catch (EngineeringException ex)
            {
                return HardwareOperationResult.Fail($"ConnectToSubnet failed: {Describe(ex)}");
            }
        }

        public HardwareOperationResult CreateIoSystem(string deviceName, string? interfacePath, string? ioSystemName)
        {
            var device = FindDeviceExact(deviceName);
            var iref = ResolveInterface(device, interfacePath);
            if (iref.Interface.IoControllers.Count == 0)
                return HardwareOperationResult.Fail($"'{GetItemPath(iref.Item)}' has no IO controller; only a controller interface (CPU) can own an IO system.");
            var controller = iref.Interface.IoControllers[0];
            try
            {
                if (controller.IoSystem != null)
                    return new HardwareOperationResult { Success = true, Message = $"The interface already owns IO system '{controller.IoSystem.Name}'.", Data = new { controller.IoSystem.Name, controller.IoSystem.Number } };
                var io = controller.CreateIoSystem(string.IsNullOrWhiteSpace(ioSystemName) ? null : ioSystemName);
                return new HardwareOperationResult
                {
                    Success = true,
                    Message = $"IO system '{io.Name}' created on '{device.Name}/{GetItemPath(iref.Item)}'.",
                    Data = new { io.Name, io.Number, Subnet = io.Subnet?.Name }
                };
            }
            catch (EngineeringException ex)
            {
                return HardwareOperationResult.Fail($"CreateIoSystem failed: {Describe(ex)}. The interface must be connected to a subnet first (tia_connect_to_subnet).");
            }
        }

        public HardwareOperationResult ConnectToIoSystem(string deviceName, string? interfacePath, string ioSystemName)
        {
            var device = FindDeviceExact(deviceName);
            var iref = ResolveInterface(device, interfacePath);
            if (iref.Interface.IoConnectors.Count == 0)
                return HardwareOperationResult.Fail($"'{GetItemPath(iref.Item)}' has no IO connector (IO device role).");
            var io = FindIoSystem(ioSystemName);
            var connector = iref.Interface.IoConnectors[0];
            try
            {
                connector.ConnectToIoSystem(io);
                return new HardwareOperationResult
                {
                    Success = true,
                    Message = $"'{device.Name}' assigned to IO system '{io.Name}'.",
                    Data = new { IoSystem = io.Name, DeviceNumber = SafeAttr(connector, "PnDeviceNumber") }
                };
            }
            catch (EngineeringException ex)
            {
                return HardwareOperationResult.Fail($"ConnectToIoSystem failed: {Describe(ex)}. Connect the interface to the IO system's subnet first.");
            }
        }

        public HardwareOperationResult SetNetworkNode(string deviceName, string? interfacePath, string? ipAddress, string? subnetMask,
            string? routerAddress, string? pnDeviceName, bool? pnNameAutoGeneration)
        {
            var device = FindDeviceExact(deviceName);
            var iref = ResolveInterface(device, interfacePath);
            var node = iref.Interface.Nodes[0];
            var obj = (IEngineeringObject)node;
            var applied = new List<string>();
            var failed = new List<string>();

            var unchanged = new List<string>();
            void Set(string attr, object value)
            {
                if (Equals(SafeAttr(obj, attr)?.ToString(), value.ToString())) { unchanged.Add(attr); return; }
                try { obj.SetAttribute(attr, value); applied.Add(attr); }
                catch (Exception ex) when (ex is EngineeringException || ex is ArgumentException || ex is InvalidCastException || ex is NotSupportedException)
                {
                    failed.Add(attr == "SubnetMask" && iref.Interface.IoConnectors.Count > 0
                        ? "SubnetMask: read-only on an IO device; it follows the IO controller's subnet mask (set it on the PLC interface)."
                        : $"{attr}: {ex.Message}");
                }
            }

            if (!string.IsNullOrWhiteSpace(ipAddress)) Set("Address", ipAddress!.Trim());
            if (!string.IsNullOrWhiteSpace(subnetMask)) Set("SubnetMask", subnetMask!.Trim());
            if (!string.IsNullOrWhiteSpace(routerAddress))
            {
                Set("UseRouter", true);
                Set("RouterAddress", routerAddress!.Trim());
            }
            if (pnNameAutoGeneration.HasValue) Set("PnDeviceNameAutoGeneration", pnNameAutoGeneration.Value);
            if (!string.IsNullOrWhiteSpace(pnDeviceName))
            {
                if (!pnNameAutoGeneration.HasValue) Set("PnDeviceNameAutoGeneration", false);
                Set("PnDeviceName", pnDeviceName!.Trim());
            }

            if (applied.Count == 0 && failed.Count == 0 && unchanged.Count == 0)
                throw new ArgumentException("Nothing to set: pass ip_address, subnet_mask, router_address, pn_device_name or pn_name_auto_generation.");

            return new HardwareOperationResult
            {
                Success = failed.Count == 0,
                Message = failed.Count == 0
                    ? (applied.Count > 0 ? $"Updated {string.Join(", ", applied)}." : "All values were already set.")
                    : $"Some attributes failed: {string.Join("; ", failed)}",
                Data = new { Device = device.Name, InterfacePath = GetItemPath(iref.Item), Applied = applied, Unchanged = unchanged, Failed = failed, Node = DescribeNode(node) }
            };
        }

        #endregion
    }

    public sealed class HardwareOperationResult
    {
        public bool Success { get; set; }
        public bool DryRun { get; set; }
        public string Message { get; set; } = "";
        public object? Data { get; set; }

        public static HardwareOperationResult Fail(string message) => new HardwareOperationResult { Success = false, Message = message };
    }
}
