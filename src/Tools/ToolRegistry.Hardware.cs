using System;
using System.Collections.Generic;
using TiaOpennessMcp.Protocol;
using TiaOpennessMcp.Tia;

namespace TiaOpennessMcp.Tools
{
    // Hardware configuration, networks, CAx/AML, project file export, archive and cross-reference tools.
    public partial class ToolRegistry
    {
        private const string DeviceNameDescription = "Exact device (station) name as listed by tia_list_devices, e.g. 'PLC_1' or 'ACS480_1'.";
        private const string ItemPathDescription = "Slash-separated device-item path below the device, e.g. 'PLC_1/PROFINET interface_1' or 'Rail_0'. A single unique item name also works. Omit for the device itself.";
        private const string InterfacePathDescription = "Device-item path of the network interface; omit when the device has only one Ethernet interface.";

        private static CallToolResult HwResult(HardwareOperationResult result) => CallToolResult.Json(result, isError: !result.Success);

        private void RegisterHardwareTools()
        {
            Register("tia_find_module_types",
                "Search the TIA hardware catalog for complete type identifiers by a partial one (Openness ModuleInformationProvider.FindModuleTypes), with their container types. " +
                "OrderNumber needs one complete part ('OrderNumber:6ES7 214-1AG40-0XB0'), GSD at least two ('GSD:GSDML-V2.4-ABB-FPNO-20220101.XML/D'), System one ('System:Rack.ET200SP'). No wildcards. " +
                "GSD/GSDML files must be installed in TIA Portal first (Options > Manage general station description files); Openness cannot install them.",
                schema =>
                {
                    schema.Properties["partial_type_identifier"] = new ToolProperty { Type = "string", Description = "Partial type identifier made of complete '/'-separated parts." };
                    schema.Required = new List<string> { "partial_type_identifier" };
                },
                args => HwResult(TiaManager.Instance.FindModuleTypes(
                    GetStringProp(args, "partial_type_identifier") ?? throw new ArgumentException("partial_type_identifier is required."))));

            Register("tia_create_device",
                "Create a device (station) from a catalog type identifier with Devices.CreateWithItem, e.g. 'OrderNumber:6ES7 214-1AG40-0XB0/V4.6' for a CPU or 'GSD:<gsdml file>/DAP/<id>' for a GSDML IO device such as a drive's fieldbus adapter. " +
                "Fails if the device name already exists. dry_run=true (default) only validates the name and checks the type in the catalog.",
                schema =>
                {
                    schema.Properties["type_identifier"] = new ToolProperty { Type = "string", Description = "Complete type identifier of the head module (from tia_find_module_types or the TIA catalog 'Article number'/version)." };
                    schema.Properties["device_item_name"] = new ToolProperty { Type = "string", Description = "Name of the created head device item, e.g. 'PLC_1'." };
                    schema.Properties["device_name"] = new ToolProperty { Type = "string", Description = "Optional station name; TIA generates one when omitted." };
                    schema.Properties["dry_run"] = new ToolProperty { Type = "boolean", Description = "Validate without creating (default: true).", Default = true };
                    schema.Required = new List<string> { "type_identifier", "device_item_name" };
                },
                args => HwResult(TiaManager.Instance.CreateDevice(
                    GetStringProp(args, "type_identifier") ?? throw new ArgumentException("type_identifier is required."),
                    GetStringProp(args, "device_item_name") ?? throw new ArgumentException("device_item_name is required."),
                    GetStringProp(args, "device_name"),
                    GetBoolProp(args, "dry_run", true))));

            Register("tia_list_plug_locations",
                "List free plug locations (HardwareObject.GetPlugLocations: position number and label) and plugged items of a device or one device item. " +
                "Without item_path, every hardware object of the device that has free positions is listed (racks, GSD heads with free submodule slots).",
                schema =>
                {
                    schema.Properties["device_name"] = new ToolProperty { Type = "string", Description = DeviceNameDescription };
                    schema.Properties["item_path"] = new ToolProperty { Type = "string", Description = ItemPathDescription };
                    schema.Required = new List<string> { "device_name" };
                },
                args => HwResult(TiaManager.Instance.ListPlugLocations(
                    GetStringProp(args, "device_name") ?? throw new ArgumentException("device_name is required."),
                    GetStringProp(args, "item_path"))));

            Register("tia_plug_module",
                "Create and plug a module or submodule (HardwareObject.PlugNew) into a device or device item at a position, e.g. a signal module into 'Rail_0' or a PROFINET telegram/GSD submodule into a drive's head module. " +
                "Checks CanPlugNew first; dry_run=true (default) stops after the check.",
                schema =>
                {
                    schema.Properties["device_name"] = new ToolProperty { Type = "string", Description = DeviceNameDescription };
                    schema.Properties["parent_item_path"] = new ToolProperty { Type = "string", Description = "Item to plug into (e.g. 'Rail_0' for an S7-1200 rack, or the GSD head module). Omit for the device itself." };
                    schema.Properties["type_identifier"] = new ToolProperty { Type = "string", Description = "Complete type identifier of the module/submodule." };
                    schema.Properties["name"] = new ToolProperty { Type = "string", Description = "Name for the new item; must be unique in its container." };
                    schema.Properties["position_number"] = new ToolProperty { Type = "integer", Description = "Free position number from tia_list_plug_locations." };
                    schema.Properties["dry_run"] = new ToolProperty { Type = "boolean", Description = "Only run CanPlugNew (default: true).", Default = true };
                    schema.Required = new List<string> { "device_name", "type_identifier", "name", "position_number" };
                },
                args => HwResult(TiaManager.Instance.PlugModule(
                    GetStringProp(args, "device_name") ?? throw new ArgumentException("device_name is required."),
                    GetStringProp(args, "parent_item_path"),
                    GetStringProp(args, "type_identifier") ?? throw new ArgumentException("type_identifier is required."),
                    GetStringProp(args, "name") ?? throw new ArgumentException("name is required."),
                    GetIntProp(args, "position_number") ?? throw new ArgumentException("position_number is required."),
                    GetBoolProp(args, "dry_run", true))));

            Register("tia_delete_hardware",
                "Delete a device or one device item (module/submodule). Without confirm=true only a preview of what would be deleted is returned.",
                schema =>
                {
                    schema.Properties["device_name"] = new ToolProperty { Type = "string", Description = DeviceNameDescription };
                    schema.Properties["item_path"] = new ToolProperty { Type = "string", Description = "Item to delete; omit to delete the whole device." };
                    schema.Properties["confirm"] = new ToolProperty { Type = "boolean", Description = "Must be true to delete (default: false = preview).", Default = false };
                    schema.Required = new List<string> { "device_name" };
                },
                args => HwResult(TiaManager.Instance.DeleteHardware(
                    GetStringProp(args, "device_name") ?? throw new ArgumentException("device_name is required."),
                    GetStringProp(args, "item_path"),
                    GetBoolProp(args, "confirm", false))));

            Register("tia_rename_hardware",
                "Rename a device (station) or a device item. Existing device names are never overwritten.",
                schema =>
                {
                    schema.Properties["device_name"] = new ToolProperty { Type = "string", Description = DeviceNameDescription };
                    schema.Properties["item_path"] = new ToolProperty { Type = "string", Description = "Item to rename; omit to rename the device." };
                    schema.Properties["new_name"] = new ToolProperty { Type = "string", Description = "New name." };
                    schema.Required = new List<string> { "device_name", "new_name" };
                },
                args => HwResult(TiaManager.Instance.RenameHardware(
                    GetStringProp(args, "device_name") ?? throw new ArgumentException("device_name is required."),
                    GetStringProp(args, "item_path"),
                    GetStringProp(args, "new_name") ?? throw new ArgumentException("new_name is required."))));

            Register("tia_get_io_addresses",
                "Read the IO addresses of device items (Address.StartAddress, Length, IoType) for one device or all devices, to wire PLC tags (%I/%Q) to modules and drive telegrams.",
                schema =>
                {
                    schema.Properties["device_name"] = new ToolProperty { Type = "string", Description = "Optional device name; omit for all devices." };
                },
                args => HwResult(TiaManager.Instance.GetIoAddresses(GetStringProp(args, "device_name"))));

            Register("tia_list_network",
                "List subnets with their IO systems and connected IO devices, and every network interface with node addresses (IP, subnet mask, router, PROFINET device name), IO controller and IO connector assignments.",
                schema =>
                {
                    schema.Properties["device_name"] = new ToolProperty { Type = "string", Description = "Optional device name to limit the interface list." };
                },
                args => HwResult(TiaManager.Instance.ListNetwork(GetStringProp(args, "device_name"))));

            Register("tia_create_subnet",
                "Create a subnet (Subnets.Create). Default type 'System:Subnet.Ethernet' (PROFINET/Industrial Ethernet). Existing names are never overwritten.",
                schema =>
                {
                    schema.Properties["subnet_name"] = new ToolProperty { Type = "string", Description = "Name of the subnet, e.g. 'PN/IE_1'." };
                    schema.Properties["type_identifier"] = new ToolProperty { Type = "string", Description = "Optional subnet type, e.g. 'System:Subnet.Ethernet' (default) or 'System:Subnet.Profibus'." };
                    schema.Required = new List<string> { "subnet_name" };
                },
                args => HwResult(TiaManager.Instance.CreateSubnet(
                    GetStringProp(args, "subnet_name") ?? throw new ArgumentException("subnet_name is required."),
                    GetStringProp(args, "type_identifier"))));

            Register("tia_connect_to_subnet",
                "Connect a device's network interface node to a subnet (Node.ConnectToSubnet).",
                schema =>
                {
                    schema.Properties["device_name"] = new ToolProperty { Type = "string", Description = DeviceNameDescription };
                    schema.Properties["interface_path"] = new ToolProperty { Type = "string", Description = InterfacePathDescription };
                    schema.Properties["subnet_name"] = new ToolProperty { Type = "string", Description = "Exact subnet name from tia_list_network." };
                    schema.Required = new List<string> { "device_name", "subnet_name" };
                },
                args => HwResult(TiaManager.Instance.ConnectToSubnet(
                    GetStringProp(args, "device_name") ?? throw new ArgumentException("device_name is required."),
                    GetStringProp(args, "interface_path"),
                    GetStringProp(args, "subnet_name") ?? throw new ArgumentException("subnet_name is required."))));

            Register("tia_create_io_system",
                "Create a PROFINET IO system on a controller interface (IoController.CreateIoSystem). The interface must already be connected to a subnet.",
                schema =>
                {
                    schema.Properties["device_name"] = new ToolProperty { Type = "string", Description = "Controller device (PLC station) name." };
                    schema.Properties["interface_path"] = new ToolProperty { Type = "string", Description = InterfacePathDescription };
                    schema.Properties["io_system_name"] = new ToolProperty { Type = "string", Description = "Optional IO system name; TIA generates one when omitted." };
                    schema.Required = new List<string> { "device_name" };
                },
                args => HwResult(TiaManager.Instance.CreateIoSystem(
                    GetStringProp(args, "device_name") ?? throw new ArgumentException("device_name is required."),
                    GetStringProp(args, "interface_path"),
                    GetStringProp(args, "io_system_name"))));

            Register("tia_connect_to_io_system",
                "Assign an IO device (e.g. a drive's PROFINET adapter) to an IO system (IoConnector.ConnectToIoSystem). Connect its interface to the same subnet first.",
                schema =>
                {
                    schema.Properties["device_name"] = new ToolProperty { Type = "string", Description = "IO device name." };
                    schema.Properties["interface_path"] = new ToolProperty { Type = "string", Description = InterfacePathDescription };
                    schema.Properties["io_system_name"] = new ToolProperty { Type = "string", Description = "Exact IO system name from tia_list_network." };
                    schema.Required = new List<string> { "device_name", "io_system_name" };
                },
                args => HwResult(TiaManager.Instance.ConnectToIoSystem(
                    GetStringProp(args, "device_name") ?? throw new ArgumentException("device_name is required."),
                    GetStringProp(args, "interface_path"),
                    GetStringProp(args, "io_system_name") ?? throw new ArgumentException("io_system_name is required."))));

            Register("tia_set_network_node",
                "Set the Ethernet node attributes of a device interface: IP address, subnet mask, router and PROFINET device name. " +
                "Setting pn_device_name turns automatic name generation off unless pn_name_auto_generation is given. Only offline project data changes; nothing is sent to a device.",
                schema =>
                {
                    schema.Properties["device_name"] = new ToolProperty { Type = "string", Description = DeviceNameDescription };
                    schema.Properties["interface_path"] = new ToolProperty { Type = "string", Description = InterfacePathDescription };
                    schema.Properties["ip_address"] = new ToolProperty { Type = "string", Description = "IPv4 address, e.g. '192.168.0.10'." };
                    schema.Properties["subnet_mask"] = new ToolProperty { Type = "string", Description = "Subnet mask, e.g. '255.255.255.0'." };
                    schema.Properties["router_address"] = new ToolProperty { Type = "string", Description = "Optional router (gateway); also enables UseRouter." };
                    schema.Properties["pn_device_name"] = new ToolProperty { Type = "string", Description = "PROFINET device name, e.g. 'acs480-1'." };
                    schema.Properties["pn_name_auto_generation"] = new ToolProperty { Type = "boolean", Description = "Generate the PROFINET device name automatically." };
                    schema.Required = new List<string> { "device_name" };
                },
                args => HwResult(TiaManager.Instance.SetNetworkNode(
                    GetStringProp(args, "device_name") ?? throw new ArgumentException("device_name is required."),
                    GetStringProp(args, "interface_path"),
                    GetStringProp(args, "ip_address"),
                    GetStringProp(args, "subnet_mask"),
                    GetStringProp(args, "router_address"),
                    GetStringProp(args, "pn_device_name"),
                    GetNullableBoolProp(args, "pn_name_auto_generation"))));
        }

        private void RegisterBackupTools()
        {
            Register("tia_export_aml",
                "Export hardware configuration as CAx/AutomationML (.aml) with CaxProvider.Export, for the whole project or one device. Siemens drives (Startdrive) and most HMI devices are not included by Siemens CAx; GSDML IO devices such as third-party drives are.",
                schema =>
                {
                    schema.Properties["output_path"] = new ToolProperty { Type = "string", Description = "Full path of the .aml file to write." };
                    schema.Properties["device_name"] = new ToolProperty { Type = "string", Description = "Optional device to export; omit for the whole project." };
                    schema.Properties["overwrite"] = new ToolProperty { Type = "boolean", Description = "Replace an existing file (default: false).", Default = false };
                    schema.Required = new List<string> { "output_path" };
                },
                args => HwResult(TiaManager.Instance.ExportAml(
                    GetStringProp(args, "output_path") ?? throw new ArgumentException("output_path is required."),
                    GetStringProp(args, "device_name"),
                    GetBoolProp(args, "overwrite", false))));

            Register("tia_import_aml",
                "Import hardware from a CAx/AutomationML (.aml) file with CaxProvider.Import. dry_run=true (default) lists the AML devices and name conflicts without changing the project. " +
                "The import cannot be undone through Openness; archive the project first (tia_archive_project).",
                schema =>
                {
                    schema.Properties["aml_path"] = new ToolProperty { Type = "string", Description = "Full path of the .aml file." };
                    schema.Properties["conflict_option"] = new ToolProperty
                    {
                        Type = "string",
                        Enum = new List<string> { "move_to_parking_lot", "retain_tia_device", "overwrite_tia_device" },
                        Description = "What to do with devices whose names exist: move_to_parking_lot (default, keeps both), retain_tia_device (skip AML one) or overwrite_tia_device (replace).",
                        Default = "move_to_parking_lot"
                    };
                    schema.Properties["dry_run"] = new ToolProperty { Type = "boolean", Description = "Preview only (default: true).", Default = true };
                    schema.Required = new List<string> { "aml_path" };
                },
                args => HwResult(TiaManager.Instance.ImportAml(
                    GetStringProp(args, "aml_path") ?? throw new ArgumentException("aml_path is required."),
                    GetStringProp(args, "conflict_option") ?? "move_to_parking_lot",
                    GetBoolProp(args, "dry_run", true))));

            Register("tia_export_project_files",
                "Export the project to a folder for version control: per PLC all blocks (SimaticML .xml, plus .scl source for SCL blocks), UDTs, PLC tag tables, watch and force tables, and the hardware as AML. " +
                "Layout: plc/<PLC>/{blocks,types,tags,watch,force}/<group path>/<name>.xml, hardware/project.aml, tia-export.json (sorted file list, no timestamps). " +
                "Unchanged files are left untouched and files of deleted objects are removed (remove_stale), so Git diffs stay small. Inconsistent or know-how protected blocks are reported as errors.",
                schema =>
                {
                    schema.Properties["output_directory"] = new ToolProperty { Type = "string", Description = "Folder to export into (created if missing), e.g. the 'tia-export' folder of the project repository." };
                    schema.Properties["plc_name"] = new ToolProperty { Type = "string", Description = "Optional PLC software name; omit for all PLCs." };
                    schema.Properties["include_hardware"] = new ToolProperty { Type = "boolean", Description = "Also export hardware/project.aml (default: true).", Default = true };
                    schema.Properties["include_scl"] = new ToolProperty { Type = "boolean", Description = "Also write .scl sources for SCL blocks (default: true).", Default = true };
                    schema.Properties["remove_stale"] = new ToolProperty { Type = "boolean", Description = "Delete files listed in the previous tia-export.json that were not exported now (default: true).", Default = true };
                    schema.Required = new List<string> { "output_directory" };
                },
                args =>
                {
                    var result = TiaManager.Instance.ExportProjectFiles(
                        GetStringProp(args, "output_directory") ?? throw new ArgumentException("output_directory is required."),
                        GetStringProp(args, "plc_name"),
                        GetBoolProp(args, "include_hardware", true),
                        GetBoolProp(args, "include_scl", true),
                        GetBoolProp(args, "remove_stale", true));
                    return CallToolResult.Json(result, isError: !result.Success);
                });

            Register("tia_archive_project",
                "Archive the open project to a compressed .zap21 file (Project.Archive). The project must be saved; existing archive files are never overwritten.",
                schema =>
                {
                    schema.Properties["target_directory"] = new ToolProperty { Type = "string", Description = "Folder for the archive (created if missing)." };
                    schema.Properties["file_name"] = new ToolProperty { Type = "string", Description = "Optional file name; default '<project>_<yyyyMMdd_HHmm>.zap21'." };
                    schema.Properties["mode"] = new ToolProperty
                    {
                        Type = "string",
                        Enum = new List<string> { "compressed", "discard_restorable_data_and_compressed" },
                        Description = "Archive mode (default: compressed). Only compressed archives can be retrieved.",
                        Default = "compressed"
                    };
                    schema.Properties["save_first"] = new ToolProperty { Type = "boolean", Description = "Save unsaved changes before archiving (default: false = refuse).", Default = false };
                    schema.Required = new List<string> { "target_directory" };
                },
                args => HwResult(TiaManager.Instance.ArchiveProject(
                    GetStringProp(args, "target_directory") ?? throw new ArgumentException("target_directory is required."),
                    GetStringProp(args, "file_name"),
                    GetStringProp(args, "mode"),
                    GetBoolProp(args, "save_first", false))));

            Register("tia_retrieve_project",
                "Retrieve a compressed project archive (.zap21) into an empty folder and open it in the attached TIA Portal instance (Projects.Retrieve). " +
                "An open project is closed only with close_current=true and only when it has no unsaved changes.",
                schema =>
                {
                    schema.Properties["archive_path"] = new ToolProperty { Type = "string", Description = "Full path of the .zap21 archive." };
                    schema.Properties["target_directory"] = new ToolProperty { Type = "string", Description = "Empty or non-existing folder to retrieve into." };
                    schema.Properties["close_current"] = new ToolProperty { Type = "boolean", Description = "Close the project currently open in this instance first (default: false).", Default = false };
                    schema.Required = new List<string> { "archive_path", "target_directory" };
                },
                args =>
                {
                    var result = TiaManager.Instance.RetrieveProject(
                        GetStringProp(args, "archive_path") ?? throw new ArgumentException("archive_path is required."),
                        GetStringProp(args, "target_directory") ?? throw new ArgumentException("target_directory is required."),
                        GetBoolProp(args, "close_current", false));
                    return CallToolResult.Json(result, isError: !result.Success);
                });

            Register("tia_export_cross_references",
                "Write the PLC cross-reference table (CrossReferenceService) of blocks and PLC tags to a JSONL file: one line per reference with source object and address, the referenced object (reference), location, access (Read/Write) and referenceType (Uses/UsedBy/...).",
                schema =>
                {
                    schema.Properties["output_path"] = new ToolProperty { Type = "string", Description = "Full path of the .jsonl file to write (replaced if it exists)." };
                    schema.Properties["plc_name"] = new ToolProperty { Type = "string", Description = "Exact PLC software name; required when the project has multiple PLCs." };
                    schema.Properties["object_names"] = new ToolProperty { Type = "array", Items = new ToolProperty { Type = "string" }, Description = "Optional block or PLC tag names to limit the export." };
                    schema.Properties["filter"] = new ToolProperty
                    {
                        Type = "string",
                        Enum = new List<string> { "objects_with_references", "all_objects", "objects_without_references", "unused_objects" },
                        Description = "Cross-reference filter (default: objects_with_references).",
                        Default = "objects_with_references"
                    };
                    schema.Properties["include_tags"] = new ToolProperty { Type = "boolean", Description = "Include the PLC tags of all tag tables (default: true).", Default = true };
                    schema.Required = new List<string> { "output_path" };
                },
                args => HwResult(TiaManager.Instance.ExportCrossReferences(
                    GetStringProp(args, "output_path") ?? throw new ArgumentException("output_path is required."),
                    GetStringProp(args, "plc_name"),
                    GetStringListProp(args, "object_names"),
                    GetStringProp(args, "filter"),
                    GetBoolProp(args, "include_tags", true))));
        }
    }
}
