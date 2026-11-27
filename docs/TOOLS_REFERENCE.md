# Siemens TIA Portal V21 Openness MCP Server
## Complete Tools Reference Dictionary (153 Tools)

**Protocol Specification:** Model Context Protocol (MCP) JSON-RPC 2.0  
**Transport Binding:** HTTP (`POST /mcp`), SSE (`GET /sse`), stdio  
**Host & Port:** `http://localhost:5001/` (Configured via `config.json`)  
**Target API:** Siemens TIA Portal Openness V21 & S7-PLCSIM Advanced API V8.0 (.NET 4.8 x64)  

---

## Tool Category Index

| Category | Domain | Tool Count | Tools Included |
| :---: | :--- | :---: | :--- |
| **1** | [Connection & Session Management](#category-1-connection--session-management-8-tools) | 8 | `tia_get_status`, `tia_list_processes`, `tia_connect`, `tia_disconnect`, `tia_get_project_info`, `tia_open_project`, `tia_save_project`, `tia_close_project` |
| **2** | [Hardware & Device Topology](#category-2-hardware--device-topology-2-tools) | 2 | `tia_list_devices`, `tia_get_device_tree` |
| **3** | [PLC Software, Blocks & Compilation](#category-3-plc-software-blocks--compilation-21-tools) | 21 | `tia_list_plcs`, `tia_list_blocks`, `tia_get_block_code`, `tia_get_block_info`, `tia_get_block_attributes`, `tia_set_block_attribute`, `tia_import_block`, `tia_import_scl_block`, `tia_rename_block`, `tia_delete_block`, `tia_list_plc_types`, `tia_get_plc_type`, `tia_get_type_info`, `tia_get_type_attributes`, `tia_get_tag_table_info`, `tia_import_plc_type`, `tia_rename_plc_type`, `tia_delete_plc_type`, `tia_compile`, `tia_get_compile_state`, `tia_get_db_member_attributes` |
| **4** | [WinCC Unified HMI Engineering](#category-4-wincc-unified-hmi-screen--widget-engineering-51-tools) | 51 | `hmi_list_targets`, `hmi_list_screens`, `hmi_list_screen_groups`, `hmi_list_graphic_lists`, `hmi_create_screen`, `hmi_delete_screen`, `hmi_get_screen_items`, `hmi_get_screen_item`, `hmi_update_screen_item`, `hmi_read_event_scripts`, `hmi_set_event_script`, `hmi_delete_screen_items`, `hmi_add_label`, `hmi_add_button`, `hmi_add_toggle_switch`, `hmi_add_shape`, `hmi_add_io_field`, `hmi_set_tag_dynamization`, `hmi_delete_dynamization`, `hmi_update_dynamization_entry`, `hmi_set_expression_dynamization`, `hmi_set_expression_dynamization_entries`, `hmi_get_js_type_definitions`, `hmi_update_button_scripts`, `hmi_list_tag_tables`, `hmi_list_tags`, `hmi_create_tag`, `hmi_create_discrete_alarm`, `hmi_update_tag_acquisition_cycles`, `hmi_set_tag_range`, `hmi_create_data_log`, `hmi_assign_logging_tags`, `hmi_list_connections`, `hmi_create_connection`, `hmi_delete_connection`, `hmi_list_text_lists`, `hmi_create_text_list`, `hmi_delete_text_list`, `hmi_export_text_lists`, `hmi_compile`, `hmi_get_runtime_settings`, `hmi_set_start_screen`, `tia_list_tag_tables`, `tia_get_tags`, `hmi_export_json`, `hmi_read_export_file`, `hmi_begin_import`, `hmi_upload_import_file`, `hmi_import_json`, `hmi_patch_json`, `hmi_render_json` |
| **5** | [Siemens TestSuite Automation](#category-5-siemens-testsuite-automation-2-tools) | 2 | `tia_testsuite_list_tests`, `tia_testsuite_load_test` |
| **6** | [S7-PLCSIM Advanced Live Simulation & Testing](#category-6-s7-plcsim-advanced-live-simulation--testing-12-tools) | 12 | `plcsim_list_instances`, `plcsim_connect`, `plcsim_disconnect`, `plcsim_get_status`, `plcsim_set_operating_state`, `plcsim_set_scale_factor`, `plcsim_read_tag`, `plcsim_write_tag`, `plcsim_read_tags`, `plcsim_write_tags`, `plcsim_run_sequence`, `plcsim_run_mixing_sequence` |
| **7** | [Project, Library & Text Discovery](#category-7-project-library--text-discovery-6-tools) | 6 | `tia_list_global_libraries`, `tia_search_project_library_types`, `tia_list_project_faceplates`, `tia_get_library_type_instances`, `tia_list_project_text_categories`, `tia_download_project_texts` |
| **8** | [PLC, Project & HMI Operations](#category-8-plc-project--hmi-operations-18-tools) | 18 | `tia_get_plc_tag_info`, `tia_read_user_constant`, `tia_get_tag_table_attributes`, `tia_create_plc_tag_table`, `tia_create_tagtable_group`, `tia_create_block_group`, `tia_create_user_constant`, `tia_update_user_constant`, `tia_create_project`, `hmi_create_screen_group`, `hmi_create_screen_under_group`, `hmi_create_tag_table`, `hmi_create_navigation_button`, `hmi_rename_screen`, `hmi_rename_screen_group`, `hmi_rename_tag_table`, `hmi_delete_tag`, `hmi_rename_tag` |
| **9** | [Siemens Online Documentation](#category-9-siemens-online-documentation-4-tools) | 4 | `docs_search`, `docs_read_page`, `docs_get_toc`, `docs_list_publications` |
| **10** | [Online PLC Values](#category-10-online-plc-values-6-tools) | 6 | `plc_online_connect`, `plc_online_get_status`, `plc_online_disconnect`, `plc_online_list_datablocks`, `plc_online_browse`, `plc_online_read_tags` |
| **11** | [Hardware Configuration, Networks & Project Backup](#category-11-hardware-configuration-networks--project-backup-19-tools) | 19 | `tia_find_module_types`, `tia_create_device`, `tia_list_plug_locations`, `tia_plug_module`, `tia_delete_hardware`, `tia_rename_hardware`, `tia_get_io_addresses`, `tia_list_network`, `tia_create_subnet`, `tia_connect_to_subnet`, `tia_create_io_system`, `tia_connect_to_io_system`, `tia_set_network_node`, `tia_export_aml`, `tia_import_aml`, `tia_export_project_files`, `tia_archive_project`, `tia_retrieve_project`, `tia_export_cross_references` |
| **12** | [Technology Objects](#category-12-technology-objects-4-tools) | 4 | `tia_list_technology_objects`, `tia_get_technology_object_parameters`, `tia_create_technology_object`, `tia_set_technology_object_parameters` |

## MCP Documentation Resources

The server exposes these read-only Markdown resources through `resources/list` and `resources/read`:

| URI | Contents |
| :--- | :--- |
| `tia://docs/tools-reference` | Detailed tool arguments and return shapes. |
| `tia://docs/skills-and-tools` | Server, skills, and tools overview. |
| `tia://docs/agent-prompting-and-system-functions` | Agent prompting guidance and WinCC Unified system functions. |
| `tia://docs/hmi-javascript-type-definitions` | Bundled TypeScript declaration subset for documented WinCC Unified runtime APIs. |

Use the live tool schemas as the authority for currently available tool names and arguments.
The bundled Openness assemblies cover authoring APIs, not the complete WinCC Unified runtime-script declaration surface. The JavaScript type-definition resource is an explicitly scoped subset based on APIs documented by this server; it is not a Siemens-generated V21 declaration file. It omits unverified tag-read and direct item-object access APIs.

---

## Category 1: Connection & Session Management (8 Tools)

### 1. `tia_get_status`
- **Description:** Returns the live connection state of the MCP server, active process ID (PID), open project name, file path, and whether the server owns the TIA instance.
- **Parameters:** None (`{}`)
- **Request Example:**
  ```json
  {
    "jsonrpc": "2.0",
    "id": 1,
    "method": "tools/call",
    "params": {
      "name": "tia_get_status",
      "arguments": {}
    }
  }
  ```
- **Response Example:**
  ```json
  {
    "IsConnected": true,
    "AttachedProcessId": 16028,
    "ProjectName": "TestMCP-DDup",
    "ProjectPath": "D:\\TIA_Project\\_AEs\\TestMCP-DDup\\TestMCP-DDup.ap21",
    "OwnsPortalInstance": false
  }
  ```

---

### 2. `tia_list_processes`
- **Description:** Lists all running TIA Portal instances on the host with their PID, project name, path, and UI mode.
- **Parameters:** None (`{}`)
- **Response Example:**
  ```json
  [
    {
      "Id": 16028,
      "ProjectPath": "D:\\TIA_Project\\_AEs\\TestMCP-DDup\\TestMCP-DDup.ap21",
      "ProjectName": "TestMCP-DDup",
      "Mode": "WithUserInterface"
    }
  ]
  ```

---

### 3. `tia_connect`
- **Description:** Attaches the server to the running TIA Portal process with this PID (from `tia_list_processes`). There is no fallback to another instance, and the server never attaches on its own.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `process_id` | integer | Yes | - | Target TIA Portal process ID (PID). |
- **Request Example:**
  ```json
  {
    "jsonrpc": "2.0",
    "id": 2,
    "method": "tools/call",
    "params": {
      "name": "tia_connect",
      "arguments": { "process_id": 16028 }
    }
  }
  ```
- **Response Example:**
  ```json
  {
    "Success": true,
    "ProcessId": 16028,
    "ProjectName": "TestMCP-DDup",
    "ProjectPath": "D:\\TIA_Project\\_AEs\\TestMCP-DDup\\TestMCP-DDup.ap21",
    "Message": "Successfully attached to TIA Portal PID 16028 with project 'TestMCP-DDup'."
  }
  ```

---

### 4. `tia_disconnect`
- **Description:** Detaches from the active TIA Portal instance and releases COM references without closing TIA Portal.
- **Parameters:** None (`{}`)
- **Response Example:** `"Disconnected from TIA Portal."`

---

### 5. `tia_get_project_info`
- **Description:** Returns metadata for the currently active project.
- **Parameters:** None (`{}`)
- **Response Example:**
  ```json
  {
    "Name": "TestMCP-DDup",
    "Path": "D:\\TIA_Project\\_AEs\\TestMCP-DDup\\TestMCP-DDup.ap21",
    "Author": "Engineering User",
    "Comment": "Automated Mixing Line Project",
    "DateCreated": "2026-09-20 10:14:00",
    "DateModified": "2026-09-30 18:45:00",
    "IsModified": true,
    "AttachedProcessId": 16028
  }
  ```

---

### 6. `tia_open_project`
- **Description:** Launches a new TIA Portal instance and opens a specified project file (`.ap21`, `.ap20`).
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `project_path` | string | **Yes** | — | Absolute path to the `.ap21` project file. |
  | `with_gui` | boolean | No | `true` | `true` for GUI, `false` for headless background mode. |
- **Request Example:**
  ```json
  {
    "name": "tia_open_project",
    "arguments": {
      "project_path": "D:\\TIA_Project\\_AEs\\TestMCP-DDup\\TestMCP-DDup.ap21",
      "with_gui": true
    }
  }
  ```

---

### 7. `tia_save_project`
- **Description:** Persists all modifications to the active project file.
- **Parameters:** None (`{}`)
- **Response Example:** `"Project saved successfully."`

---

### 8. `tia_close_project`
- **Description:** Closes the current project without terminating the TIA Portal instance.
- **Parameters:** None (`{}`)
- **Response Example:** `"Project closed."`

---

## Category 2: Hardware & Device Topology (2 Tools)

### 9. `tia_list_devices`
- **Description:** Lists all top-level devices (PLCs, HMIs, PC systems, drives, distributed I/O racks).
- **Parameters:** None (`{}`)
- **Response Example:**
  ```json
  [
    {
      "Name": "PLC_1",
      "TypeIdentifier": "OrderNumber:6ES7 516-3AN02-0AB0/V3.1",
      "DeviceGroup": "Line1"
    },
    {
      "Name": "PC-System_1",
      "TypeIdentifier": "OrderNumber:6AV2 107-...",
      "DeviceGroup": "HMI"
    }
  ]
  ```

---

### 10. `tia_get_device_tree`
- **Description:** Recursively inspects hardware slots, racks, interface modules, and subnets of a device.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `device_name` | string | **Yes** | — | Target device name (e.g. `"PLC_1"`). |

---

## Category 3: PLC Software, Blocks & Compilation (21 Tools)

### 11. `tia_list_plcs`
- **Description:** Lists all PLCs with block counts, UDT counts, and tag table counts.
- **Parameters:** None (`{}`)
- **Response Example:**
  ```json
  [
    {
      "DeviceName": "PLC_1",
      "ItemName": "PLC_1",
      "PlcName": "PLC_1",
      "BlockCount": 24,
      "TypeCount": 8,
      "TagTableCount": 5
    }
  ]
  ```

---

### 12. `tia_list_blocks`
- **Description:** Enumerate all program blocks (OB, FB, FC, DB) with language and consistency state.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `plc_name` | string | No | null | Target PLC (defaults to first PLC). |
  | `block_type` | string | No | null | Filter: `"OB"`, `"FB"`, `"FC"`, `"DB"`. |
  | `folder` | string | No | null | Subfolder inside Program Blocks. |
- **Response Example:**
  ```json
  [
    {
      "Name": "Main",
      "Number": 1,
      "BlockType": "OB",
      "ProgrammingLanguage": "SCL",
      "IsConsistent": true,
      "IsKnowHowProtected": false
    },
    {
      "Name": "FB_MixingLine",
      "Number": 10,
      "BlockType": "FB",
      "ProgrammingLanguage": "SCL",
      "IsConsistent": true,
      "IsKnowHowProtected": false
    }
  ]
  ```

---

### 13. `tia_get_block_code`
- **Description:** Reads the code of a PLC block. With `format: "auto"` (default), SCL, STL and DB blocks return the source text that TIA Portal generates (`ExternalSourceGroup.GenerateSource`) in `Source`, without SimaticML. LAD, FBD and GRAPH blocks, and blocks whose source cannot be generated (for example know-how protected blocks, or while TIA Portal is online with the PLC), return SimaticML in `XmlContent`, and `Warnings` gives the reason. When no block has the name, a PLC data type (UDT) of that name is used.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `block_name` | string | **Yes** | — | Block name, or UDT name (e.g. `"FB_MixingLine"`, `"UDT_Motor"`). |
  | `plc_name` | string | No | null | Target PLC name. |
  | `format` | string | No | `"auto"` | `"auto"`, `"source"` (fails instead of falling back to SimaticML) or `"xml"` (always SimaticML). `"scl"` is accepted as an alias of `"source"`. |
  | `include_xml` | boolean | No | `false` | Also return SimaticML in `XmlContent` when source text is returned. A failed SimaticML export is then reported in `Warnings` instead of failing the call. |
  | `with_dependencies` | boolean | No | `false` | Generate the source together with the blocks and types it depends on. |
- **Result fields:** `Name`, `Number`, `BlockType`, `ProgrammingLanguage`, `Format` (`source` or `xml`), `Source`, `SourceFileExtension` (`.scl`, `.awl`, `.db` or `.udt`), `IsKnowHowProtected`, `XmlContent` (only with `format: "xml"`, `include_xml: true` or the fallback), `Warnings`, and `ExtractedScl`.
- **UDTs:** a UDT result has `BlockType` `"PlcType"`, `ProgrammingLanguage` `"UDT"` and `Number` 0.
- **Re-import:** only the source of a single SCL block generated without `with_dependencies` can be edited and passed to `tia_import_scl_block`. STL (`.awl`) and UDT (`.udt`) source and output with dependencies cannot.
- **Changed in v2.14.0:** before, the tool always returned `XmlContent` and an `ExtractedScl` built from the XML. Callers that read `XmlContent` must now pass `format: "xml"` or `include_xml: true`. `ExtractedScl` is deprecated: it is a copy of `Source` for SCL blocks and will be removed in a later release.
- **Response Example:**
  ```json
  {
    "Name": "FB_MixingLine",
    "Number": 10,
    "BlockType": "FB",
    "ProgrammingLanguage": "SCL",
    "Format": "source",
    "SourceFileExtension": ".scl",
    "IsKnowHowProtected": false,
    "Source": "FUNCTION_BLOCK \"FB_MixingLine\"\n...\nEND_FUNCTION_BLOCK\n",
    "ExtractedScl": "FUNCTION_BLOCK \"FB_MixingLine\"\n...\nEND_FUNCTION_BLOCK\n",
    "Warnings": []
  }
  ```
- **Not verified against a live TIA Portal:** the generated `.db`, `.awl` and `.udt` content, the fallback while online, and re-import of generated SCL.

---

### 14. `tia_import_block`
- **Description:** Imports or updates a block from SimaticML XML (`ImportOptions.Override`; no backup is taken). The result is text.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `xml_content` | string | One required | — | SimaticML XML string. Use this or `file_path`, not both. |
  | `file_path` | string | One required | — | Fully qualified server-local `.xml` path (`X:\...` or UNC), at most 50 MiB, passed directly to Openness. Prefer it for large blocks, for example a file from `tia_export_project_files`. |
  | `overwrite` | boolean | No | `true` | Overwrite existing block. |
  | `plc_name` | string | No | null | Target PLC name. |
  | `folder` | string | No | null | Subfolder inside Program Blocks. |
- **OB cycle settings:** `CyclicTime` and `PhaseOffset` in SimaticML are microseconds (100 ms = `<CyclicTime>100000</CyclicTime>`). OBs have no `PhaseShift` attribute (use `PhaseOffset`), and `CyclicApplicationCycleTime` is read-only. The XML is imported unchanged. For imported cyclic interrupt OBs, the stored `CyclicTime` and `PhaseOffset` are read back on extra lines, and suspicious settings are listed under `Warnings:` (for example `CyclicTime` below 1000 us, a value that is not a whole number of milliseconds, a `PhaseShift` element, or `PhaseOffset` not smaller than `CyclicTime`). If the import fails, the same hints are added to the error.

---

### 14a. `tia_import_scl_block`
- **Description:** Imports a complete SCL source for one PLC block. When a block with the same name exists, it is backed up, deleted, and replaced; a failed import attempts to restore the backup. This is whole-block replacement, not an in-place edit. Compilation defaults to enabled; compilation errors are returned and do not roll back the import.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `scl_code` | string | One required | — | Complete source for exactly one `FUNCTION`, `FUNCTION_BLOCK`, `ORGANIZATION_BLOCK`, or `DATA_BLOCK`. Use this or `file_path`, not both. |
  | `file_path` | string | One required | — | Fully qualified server-local `.scl` path, at most 50 MiB. Read as UTF-8 unless the file has a BOM; a file that is not valid UTF-8 (for example ANSI) is rejected before any change. |
  | `plc_name` | string | No | null | Target PLC name. |
  | `folder` | string | No | null | Existing PLC block group path; when omitted, preserves the existing block's group or uses the root group for a new block. |
  | `backup` | boolean | No | `true` | Export the existing same-name block as a backup before replacing it. `false` skips the backup and returns a warning; a failed import then cannot restore the old block. |
  | `compile_after_import` | boolean | No | `true` | Compile the target PLC after generation. Compilation errors do not trigger rollback. |
  | `open_after_import` | boolean | No | `false` | Open the imported block in the TIA Portal editor. |
- **Order of checks:** the destination folder is resolved before anything changes. With `backup: true`, an inconsistent existing block is compiled once on its own first. If it stays inconsistent, the compile fails, or the block is know-how protected, the import fails without replacing the block; the error says whether that compile ran. Retry with `backup: false` only when losing the old block on a failed import is acceptable.
- **Name space:** the import is refused, and nothing changes, if the block name equals the name of a technology object (case-insensitive).
- **Encoding:** the temporary `.scl` file is written as UTF-8 with BOM, so non-ASCII text in titles and comments is kept.
- **New result fields:** `SourceFile`, `BackupTaken`, `ExistingWasConsistent` (read whenever a block is replaced; null only if unreadable), `PreImportBlockCompilation`, `PreviousBlockNumber` and `BlockNumber`. A warning appears when the block number changes, because a replace deletes the block and regenerates it; the old number is not restored. The `Compilation` object keeps `State`, `ErrorCount`, `WarningCount` and `Messages` (no `UpToDate`).
- **Not verified against a live TIA Portal:** the pre-import compile of an inconsistent block, the block-number change, and non-ASCII text in imported blocks.

---

### 15. `tia_delete_block`
- **Description:** Deletes a program block (OB/FB/FC/DB) from the PLC. The output is text. If the name belongs to a PLC data type, the error points to `tia_delete_plc_type`.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `block_name` | string | **Yes** | — | Block name to delete. |
  | `plc_name` | string | No | null | Target PLC name. |

---

### 16. `tia_list_plc_types`
- **Description:** Lists all User Data Types (UDTs) configured in the PLC.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `plc_name` | string | No | null | Target PLC name. |

---

### 17. `tia_get_plc_type`
- **Description:** Exports a User Data Type (UDT) as SimaticML XML.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `type_name` | string | **Yes** | — | UDT name (e.g. `"typeMotorControl"`). |
  | `plc_name` | string | No | null | Target PLC name. |

---

### 18. `tia_import_plc_type`
- **Description:** Imports or replaces a User Data Type (UDT) from SimaticML XML. Importing a UDT under a new name creates a second type; use `tia_rename_plc_type` to rename.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `xml_content` | string | One required | — | SimaticML XML definition. Use this or `file_path`, not both. |
  | `file_path` | string | One required | — | Fully qualified server-local `.xml` path, at most 50 MiB. |
  | `overwrite` | boolean | No | `true` | Overwrite if type exists. |
  | `plc_name` | string | No | null | Target PLC name. |

---

### 19. `tia_compile`
- **Description:** Compiles PLC software via Openness `ICompilable`. Returns error counts, warnings, and compiler messages with line numbers.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `plc_name` | string | No | null | Target PLC name. |
- **Response Example:**
  ```json
  {
    "State": "Success",
    "ErrorCount": 0,
    "WarningCount": 0,
    "Messages": [
      {
        "DateTime": "2026-09-30 18:30:00",
        "Description": "Compilation completed successfully",
        "Path": "PLC_1",
        "State": "Success",
        "WarningCount": 0,
        "ErrorCount": 0
      }
    ]
  }
  ```
- **Up-to-date projects:** TIA Portal compiles only changed blocks, and Openness V21 has no rebuild-all option. The tool compares the blocks' compile dates before and after. When the compile succeeds with no errors and no warnings and no compile date changed, the result adds `"UpToDate": true` and a `Note`: nothing was compiled, and warnings from an earlier compile are not shown again. Use `tia_get_compile_state`, or *Compile > Software (rebuild all blocks)* in TIA Portal. If some compile dates cannot be read, `UpToDate` is decided from the compiler message text only.

---

### `tia_get_compile_state`
- **Description:** Read-only. Lists the compile state of PLC blocks and PLC data types without compiling.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `plc_name` | string | No | null | Exact PLC software name; required when the project has several PLCs. |
  | `group_path` | string | No | null | Block group to list, with its subgroups. Omit or use `""` for all blocks. Types are always listed from the root. |
  | `only_inconsistent` | boolean | No | `false` | List only entries that are inconsistent or whose state is unknown. Totals always count everything. |
  | `include_types` | boolean | No | `true` | Also list PLC data types. |
- **Returns:** per block `IsConsistent` (null when TIA does not report it), `IsKnowHowProtected`, `CompileDate`, `ModifiedDate`, `CodeModifiedDate` and `InterfaceModifiedDate` (ISO 8601); per type `IsConsistent` and `ModifiedDate`; totals `BlockCount`, `ConsistentBlocks`, `InconsistentBlocks`, `UnknownBlocks`, `TypeCount`, `InconsistentTypes`, `UnknownTypes`; and a `Note`.
- Openness cannot read the warnings TIA stored from an earlier compile.

---

### `tia_rename_block`
- **Description:** Renames one program block (OB/FB/FC/DB) in place and verifies the new name by read-back. The block number is unchanged.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `block_name` | string | **Yes** | — | Exact current block name. |
  | `new_name` | string | **Yes** | — | New name; no leading or trailing whitespace, control characters or path separators. |
  | `plc_name` | string | No | null | Exact PLC software name; required when the project has several PLCs. |
  | `group_path` | string | No | null | Exact block folder. Omit to search all folders; `""` means the root folder only. |
  | `on_conflict` | string | No | `"fail"` | Only `"fail"` is supported. |
- **Collision check:** the rename fails with status `collision`, and nothing changes, if any program block, PLC data type, system block or system data type in the PLC already has the new name (case-insensitive). Objects inside S7-1500 software units are not searched.
- **Not found:** when `group_path` is given and the name is not in that folder, the message names the folders where it exists. If the name belongs to a UDT, the message points to `tia_rename_plc_type`.
- **Result:** `{ Success, Operation, Status, PlcName, ObjectName, TargetPath, Partial, Message, Warnings }`. If TIA rejects the name and the read-back shows the old name unchanged, `Status` is `api_failure` with `Partial: false`.
- **Not verified against a live TIA Portal:** whether callers and instance DBs follow the rename, and the behaviour for know-how protected or library-linked blocks.

### `tia_rename_plc_type`
- **Description:** Renames one PLC data type (UDT) in place (no delete, re-import or overwrite) and verifies the new name by read-back.
- **Parameters:** `type_name` and `new_name` (required); `plc_name`, `group_path` and `on_conflict` (`"fail"` only) as for `tia_rename_block`.
- Same collision check, folder hints and result as `tia_rename_block`. Importing a UDT under a new name creates a second type; use this tool to rename.
- **Not verified against a live TIA Portal:** whether blocks, DBs, other UDTs and HMI tags that use the type follow the rename.

### `tia_delete_plc_type`
- **Description:** Deletes one PLC data type (UDT) by exact, case-sensitive name. TIA Portal decides whether the delete is allowed; its answer is reported. Blocks, DBs and other UDTs that used the type become inconsistent until changed, so run `tia_compile` afterwards.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `type_name` | string | **Yes** | — | Exact UDT name. |
  | `plc_name` | string | No | null | Exact PLC software name; required when the project has several PLCs. |
  | `group_path` | string | No | null | Exact type folder, e.g. `"HMI/Manual"`. Omit to search all folders; `""` means the root folder only. |
- Program blocks are deleted with `tia_delete_block`. Objects inside S7-1500 software units are not searched.
- **Not verified against a live TIA Portal:** what TIA does when the type is still in use, and the exact message.

### `tia_set_block_attribute`
- **Description:** Sets one allow-listed OB attribute in place with `PlcBlock.SetAttribute` (no delete or re-import) and reads it back. The project is not compiled or saved; run `tia_compile` afterwards.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `block_name` | string | **Yes** | — | Exact OB name, e.g. `"Cyclic interrupt"`. |
  | `attribute_name` | string | **Yes** | — | `"CyclicTime"`, `"PhaseOffset"` (cyclic interrupt OBs only) or `"PriorityNumber"`. |
  | `value` | integer | **Yes** | — | New value in `unit`. `CyclicTime` 1 to 60000 ms; `PhaseOffset` smaller than `CyclicTime`. |
  | `unit` | string | For time values | — | `"ms"` or `"us"`. Required for `CyclicTime` and `PhaseOffset`; must be omitted for `PriorityNumber`. |
  | `expected_current_value` | integer | No | null | Guard in the same unit. If the current value differs, the status is `conflict` and nothing is written. |
  | `plc_name` | string | No | null | Exact PLC software name. |
  | `group_path` | string | No | null | Exact block group; required when the block name is duplicated. `""` is the root group. |
- Openness and SimaticML store microseconds: 100 ms = 100000 us.
- The attribute is written only when TIA Portal lists it as writable for that OB (`GetAttributeInfos`); otherwise the status is `not_supported` and nothing is written. F-CPU safety OBs are not supported.
- **Status values:** `updated`, `unchanged`, `conflict`, `invalid_input`, `invalid_target`, `protected`, `not_supported`, `not_found`, `ambiguous`, `verification_failed`, `api_failure`.
- **Sample Invocation:** `{ "block_name": "Cyclic interrupt", "attribute_name": "CyclicTime", "value": 100, "unit": "ms" }` sets the OB to 100 ms.
- **Not verified against a live TIA Portal:** whether TIA lists `CyclicTime` and `PhaseOffset` as writable. If it does not, the tool returns `not_supported` for them.

---

### `tia_get_db_member_attributes`
- **Description:** Read the V21-reported attributes of one exact direct member of a global DB.
- **Parameters:** Required `db_name` and `member_name`; optional exact `plc_name` and `group_path`.
- **Returns:** Every attribute reported for the selected direct member, with values only when its V21 access mode permits reading. Nested paths, dedicated datatype, stable ID, and revision fields are not claimed.

## Category 4: WinCC Unified HMI Screen & Widget Engineering (51 Tools)

### 20. `hmi_list_targets`
- **Description:** Enumerates all WinCC Unified runtime devices and software containers.
- **Parameters:** None (`{}`)
- **Response Example:**
  ```json
  [
    {
      "DeviceName": "PC-System_1",
      "DeviceItemName": "HMI_RT_1",
      "SoftwareName": "HMI_RT_1",
      "ScreenCount": 2,
      "TagTableCount": 3
    }
  ]
  ```

---

### 21. `hmi_list_screens`
- **Description:** Lists all screens in the target HMI, including recursively nested screen groups, with resolution and item count.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `target_name` | string | No | null | HMI software target name. |
- **Response Example:**
  ```json
  [
    { "Name": "Screen_1", "Width": 1920, "Height": 1080, "ItemCount": 108 },
    { "Name": "Screen_2", "Width": 1920, "Height": 1080, "ItemCount": 150 }
  ]
  ```

---

### 22. `hmi_create_screen`
- **Description:** Creates or ensures existence of an HMI screen with specific dimensions.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `screen_name` | string | **Yes** | — | Screen name (e.g. `"Screen_2"`). |
  | `width` | integer | No | null | Screen width in pixels. |
  | `height` | integer | No | null | Screen height in pixels. |
  | `target_name` | string | No | null | Target HMI software. |

---

### 23. `hmi_delete_screen`
- **Description:** Deletes a screen from the HMI software target.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `screen_name` | string | **Yes** | — | Screen name to delete. |
  | `target_name` | string | No | null | Target HMI software. |

---

### 24. `hmi_get_screen_items`
- **Description:** Reads every element (button, label, shape, IO field) on a screen with positions, colors, event scripts, and dynamizations.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `screen_name` | string | **Yes** | — | Screen name to inspect. |
  | `target_name` | string | No | null | Target HMI software. |
- **Response Example:**
  ```json
  [
    {
      "Name": "btn_M101_Start",
      "Type": "HmiButton",
      "Left": 40,
      "Top": 100,
      "Width": 90,
      "Height": 34,
      "BackColor": "#16A34A",
      "Text": "Start",
      "TappedScript": "Tags.SysFct.InvertBitInTag(\"HMI_MotorInfeed_Start\", 0);"
    }
  ]
  ```

---

### 25. `hmi_delete_screen_items`
- **Description:** Deletes specific screen items by name list or name prefix.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `screen_name` | string | **Yes** | — | Target screen. |
  | `item_names` | array of str | No | null | List of exact item names to delete. |
  | `prefix` | string | No | null | Prefix filter (e.g. `"M101_"`). |
  | `target_name` | string | No | null | Target HMI software. |

---

### 26. `hmi_add_label`
- **Description:** Adds or updates a text label with typography, alignment, and colors (automatically HTML-wrapped).
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `screen_name` | string | **Yes** | — | Target screen. |
  | `name` | string | **Yes** | — | Item name (e.g. `"lbl_Header"`). |
  | `text` | string | **Yes** | — | Display text. Plain Unicode text (ä, ö, å, °). The server wraps it as `<body><p>…</p></body>` and escapes it, so do not use HTML entities. Numeric character references such as `&#196;` or `&#xC4;` are decoded to the character before escaping; named entities such as `&amp;` stay literal. |
  | `left` | integer | **Yes** | — | X coordinate in pixels. |
  | `top` | integer | **Yes** | — | Y coordinate in pixels. |
  | `width` | integer | **Yes** | — | Width in pixels. |
  | `height` | integer | **Yes** | — | Height in pixels. |
  | `font_size` | number | No | `11.0` | Font size in points. |
  | `is_bold` | boolean | No | `false` | Bold text weight. |
  | `align` | string | No | `"Center"` | `"Left"`, `"Center"`, or `"Right"`. |
  | `fore_color` | string | No | null | Text color (hex `#FFFFFF` or named). |
  | `back_color` | string | No | null | Background fill color. |
  | `target_name` | string | No | null | Target HMI software. |

---

### 27. `hmi_add_button`
- **Description:** Adds or updates a button with caption, colors, pressed state tag, and OnClick/Tapped JavaScript event handler.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `screen_name` | string | **Yes** | — | Target screen. |
  | `name` | string | **Yes** | — | Button item name. |
  | `text` | string | **Yes** | — | Button caption. Plain Unicode text (ä, ö, å, °). The server wraps it as `<body><p>…</p></body>` and escapes it, so do not use HTML entities. Numeric character references such as `&#196;` or `&#xC4;` are decoded to the character before escaping; named entities such as `&amp;` stay literal. |
  | `left`, `top`, `width`, `height` | integer | **Yes** | — | Geometry in pixels. |
  | `back_color` | string | No | null | Button background color. |
  | `fore_color` | string | No | null | Button text color. |
  | `pressed_tag` | string | No | null | Bound tag for pressed state. |
  | `tapped_script` | string | No | null | JavaScript code to execute on tap. |
  | `target_name` | string | No | null | Target HMI software. |
- **Sample Invocation:**
  ```json
  {
    "screen_name": "Screen_2",
    "name": "btn_M101_Start",
    "text": "Start",
    "left": 40, "top": 100, "width": 90, "height": 34,
    "back_color": "#16A34A", "fore_color": "white",
    "tapped_script": "Tags.SysFct.InvertBitInTag(\"HMI_MotorInfeed_Start\", 0);"
  }
  ```

---

### 28. `hmi_add_toggle_switch`
- **Description:** Adds or updates a toggle switch with independent normal/alternate captions, bound to an HMI tag's `IsAlternateState`.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `screen_name`, `name`, `off_text`, `on_text` | string | **Yes** | — | Target screen, item name, and captions for normal and alternate states. Captions: plain Unicode text (ä, ö, å, °). The server wraps it as `<body><p>…</p></body>` and escapes it, so do not use HTML entities. Numeric character references such as `&#196;` or `&#xC4;` are decoded to the character before escaping; named entities such as `&amp;` stay literal. |
  | `left`, `top`, `width`, `height` | integer | **Yes** | — | Switch geometry. |
  | `process_tag` | string | **Yes** | — | Existing HMI tag bound to `IsAlternateState`. |
  | `target_name` | string | No | null | Target HMI software. |

---

### 29. `hmi_add_shape`
- **Description:** Adds or updates a shape (`"Rectangle"` or `"Circle"`) with dimensions, center/radius, fill and border.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `screen_name` | string | **Yes** | — | Target screen. |
  | `name` | string | **Yes** | — | Shape item name. |
  | `shape_type` | string | **Yes** | — | `"Rectangle"` or `"Circle"`. |
  | `left`, `top`, `width`, `height` | integer | No | null | Geometry for Rectangle. |
  | `center_x`, `center_y`, `radius` | integer | No | null | Coordinates for Circle. |
  | `back_color` | string | No | null | Fill color. |
  | `border_color` | string | No | null | Border stroke color. |
  | `border_width` | integer | No | `1` | Border width in px. |
  | `target_name` | string | No | null | Target HMI software. |

---

### 30. `hmi_add_io_field`
- **Description:** Adds or updates an IO Field bound to an HMI tag for numeric or string display.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `screen_name` | string | **Yes** | — | Target screen. |
  | `name` | string | **Yes** | — | IO Field item name. |
  | `left`, `top`, `width`, `height` | integer | **Yes** | — | Geometry in pixels. |
  | `mode` | string | No | `"Output"` | `"Output"` or `"InputOutput"`. |
  | `process_tag` | string | No | null | Bound HMI tag for ProcessValue. |
  | `output_format` | string | No | null | Output format, e.g. `"{F1} °C"` (one decimal and a unit). Omit to keep the current format. Plain Unicode text, not HTML; numeric references such as `&#176;` are decoded. |
  | `target_name` | string | No | null | Target HMI software. |

---

### 31. `hmi_set_tag_dynamization`
- **Description:** Binds a screen item property (e.g. `BackColor`, `Visible`) to an HMI tag using `ConditionType.Range` or `ConditionType.Singlebit`.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `screen_name` | string | **Yes** | — | Target screen. |
  | `item_name` | string | **Yes** | — | Target screen item name. |
  | `property_name` | string | **Yes** | — | Property to dynamize (`"BackColor"`). |
  | `tag_name` | string | **Yes** | — | HMI tag name. |
  | `condition_type` | string | No | `"None"` | `"None"`, `"Range"`, or `"Singlebit"`. |
  | `ranges` | array | No | null | For `Range`, provide numeric bounds. For `Singlebit`, provide exactly one `0..0` and one `1..1` entry; values map the off/on states. |
  | `target_name` | string | No | null | Target HMI software. |
- **Existing dynamizations:** an existing Singlebit table is updated in place; its bitmask entries are not deleted. Switching an existing mapping table that has entries to another condition type (for example Singlebit to Range), or replacing a non-tag dynamization, fails: call `hmi_delete_dynamization` first. `"None"` only sets the tag and leaves an existing table unchanged. All changes run in one transaction and are rolled back on failure.
- **Sample Invocation:**
  ```json
  {
    "screen_name": "Screen_2",
    "item_name": "circ_M101",
    "property_name": "BackColor",
    "tag_name": "HMI_MotorInfeed_RunFb",
    "condition_type": "Range",
    "ranges": [
      { "from": 0, "to": 0, "value": "gray" },
      { "from": 1, "to": 1, "value": "green" }
    ]
  }
  ```

### `hmi_update_dynamization_entry`
- **Description:** Updates selected values or flashing settings on one existing mapping-table entry in a tag or expression dynamization. Entry indices are zero-based and preserve all other entries and conditions.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `screen_name` | string | **Yes** | — | Target screen. |
  | `item_name` | string | **Yes** | — | Target screen item name. |
  | `property_name` | string | **Yes** | — | Dynamized property, such as `BackColor`. |
  | `entry_index` | integer | **Yes** | — | Zero-based mapping-table entry index. |
  | `new_value` | object | No | — | Optional `{ "type": "Color", "value": "#E80000" }` replacement for the entry's primary value. |
  | `new_alternate_value` | object | No | — | Optional typed replacement for the alternate value, using the same `{ "type": "...", "value": "..." }` shape. |
  | `new_flashing` | boolean | No | — | Optional flashing enable state. |
  | `new_flashing_rate` | string | No | — | Optional TIA flashing rate enum name. |
  | `target_name` | string | No | null | Target HMI software. |
- Mapping value types use the exported CLR names: `String`, `Boolean`, `Color`, `Byte`, `Int16`, `UInt16`, `Int32`, `UInt32`, `Int64`, `UInt64`, `Single`, `Double`, or `Decimal`. The supplied type must match the existing entry value type.
- **Sample Invocation:**
  ```json
  {
    "screen_name": "Main_UnitControlSideBar",
    "item_name": "UCS_Ind_Alarm",
    "property_name": "BackColor",
    "entry_index": 1,
    "new_value": { "type": "Color", "value": "#E80000" }
  }
  ```

---

### `hmi_delete_dynamization`
- **Description:** Deletes only the selected item's dynamization for one property. The item and all its other properties and dynamizations are preserved; the operation is verified and left unsaved.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `screen_name` | string | **Yes** | — | Target screen. |
  | `item_name` | string | **Yes** | — | Target screen item. |
  | `property_name` | string | **Yes** | — | Dynamized property, such as `BackColor`. |
  | `target_name` | string | No | null | Target HMI software. |
- The tool fails if the item or dynamization does not exist. It does not save the project.
- **Sample Invocation:**
  ```json
  {
    "screen_name": "Main_UnitControlSideBar_ForTesting",
    "item_name": "UCS_Btn_2",
    "property_name": "BackColor",
    "target_name": "HMI_RT_1"
  }
  ```

### 32. `hmi_update_button_scripts`
- **Description:** Modifies existing button scripts **in-place** without deleting screen items, running Openness JavaScript syntax checking.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `screen_name` | string | **Yes** | — | Target screen. |
  | `find_text` | string | No | null | Text to search for (e.g. `"SetBitInTag"`). |
  | `replace_text` | string | No | null | Replacement text (e.g. `"InvertBitInTag"`). |
  | `new_script` | string | No | null | Exact new script override. |
  | `button_names` | array of str | No | null | Filter specific buttons. |
  | `target_name` | string | No | null | Target HMI software. |

---

### 33. `hmi_list_tag_tables`
- **Description:** Lists all HMI tag tables in the HMI software target.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `target_name` | string | No | null | Target HMI software. |

---

### 34. `hmi_list_tags`
- **Description:** Lists HMI tags with their table, connection, PLC tag binding, and data type.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `table_name` | string | No | null | Tag table name filter. |
  | `target_name` | string | No | null | Target HMI software. |

---

### 35. `hmi_create_tag`
- **Description:** Creates or updates an HMI tag linked to a PLC tag path.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `tag_name` | string | **Yes** | — | HMI tag name. |
  | `plc_tag` | string | **Yes** | — | PLC tag address (e.g. `"\"DB_MixingLine\".Line.MotorInfeed.RunFb"`). |
  | `connection` | string | No | `"HMI_Connection_1"` | HMI connection name. |
  | `table_name` | string | No | `"Default tag table"` | Target table name. |
  | `target_name` | string | No | null | Target HMI software. |

---

### 36. `hmi_create_discrete_alarm`
- **Description:** Creates or updates a discrete alarm after validating its HMI alarm class and trigger tag.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `alarm_name` | string | **Yes** | — | Discrete alarm name. |
  | `alarm_class` | string | **Yes** | — | Existing HMI alarm class name. |
  | `raised_state_tag` | string | **Yes** | — | Existing HMI tag used as the alarm trigger. |
  | `text` | string | **Yes** | — | Event text applied to active text languages. Plain Unicode text (ä, ö, å, °). The server wraps it as `<body><p>…</p></body>` and escapes it, so do not use HTML entities. Numeric character references such as `&#196;` or `&#xC4;` are decoded to the character before escaping; named entities such as `&amp;` stay literal. |
  | `raised_state_tag_bit_number` | integer | No | `0` | Trigger bit number. |
  | `trigger_mode` | string | No | `"OnRisingEdge"` | Discrete alarm trigger mode supported by TIA Portal V21. |
  | `acknowledgment_control_tag` | string | No | null | Optional existing HMI acknowledgment-control tag. |
  | `target_name` | string | No | null | Target HMI software. |

---

### 37. `hmi_update_tag_acquisition_cycles`
- **Description:** Batch updates the acquisition cycle (e.g. `T100ms`, `T1s`, `T500ms`) for HMI tags in WinCC Unified.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `cycle` | string | **Yes** | — | Target acquisition cycle name or interval (e.g. `"T100ms"`, `"T1s"`). |
  | `plc_only` | boolean | No | `true` | Only update PLC-connected tags (`Connection` or `PlcTag` populated). |
  | `table_name` | string | No | null | Specific tag table name (all tables if omitted). |
  | `target_name` | string | No | null | Target HMI software. |
- **Response Example:**
  ```json
  {
    "TotalTags": 75,
    "ModifiedCount": 75,
    "AcquisitionCycle": "T100ms",
    "UpdatedTags": [
      "Clock_Byte",
      "HMI_MotorInfeed_RunFb",
      "..."
    ]
  }
  ```

---

### 38. `hmi_set_tag_range`
- **Description:** Configures minimum and maximum range limits (constant values) on an HMI tag (`InitialMinValue` / `InitialMaxValue`).
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `tag_name` | string | **Yes** | — | HMI tag name. |
  | `min_value` | number | No | null | Minimum limit value (e.g. `0.0`). |
  | `max_value` | number | No | null | Maximum limit value (e.g. `1800.0`). |
  | `table_name` | string | No | null | Specific tag table name. |
  | `target_name` | string | No | null | Target HMI software. |
- **Response Example:**
  `"Tag 'HMI_MotorInfeed_SpeedSetpoint' range updated: Min=0, Max=1800."`

---

### 39. `hmi_create_data_log`
- **Description:** Creates a new DataLog in WinCC Unified HMI software.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `log_name` | string | **Yes** | — | Name of the DataLog to create (e.g. `"Process_DataLog"`). |
  | `target_name` | string | No | null | Target HMI software. |
- **Response Example:**
  `"DataLog 'Process_DataLog' created successfully."`

---

### 40. `hmi_assign_logging_tags`
- **Description:** Assigns multiple HMI tags to a DataLog with logging mode (`Cyclic`, `OnChange`, `OnDemand`) and cycle (`T1s`, `T500ms`, `T100ms`).
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `log_name` | string | **Yes** | — | Target DataLog name. |
  | `tag_names` | array | **Yes** | — | List of HMI tag names to assign. |
  | `cycle` | string | No | `"T1s"` | Logging cycle (`"T1s"`, `"T500ms"`, `"T100ms"`). |
  | `mode` | string | No | `"Cyclic"` | Logging mode (`"Cyclic"`, `"OnChange"`, `"OnDemand"`). |
  | `target_name` | string | No | null | Target HMI software. |
- **Response Example:**
  ```json
  {
    "DataLog": "Process_DataLog",
    "AssignedCount": 14,
    "Mode": "Cyclic",
    "Cycle": "T1s",
    "AssignedTags": [
      "HMI_MotorInfeed_SpeedAct",
      "HMI_MotorInfeed_CurrentAct",
      "HMI_TankCityWater_Level",
      "..."
    ]
  }
  ```

---

### 41. `hmi_compile`
- **Description:** Compiles a WinCC Unified HMI using Openness `ICompilable`. The HMI is selected like in the other HMI tools.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `target_name` | string | No | null | Exact HMI software, device item or device name (e.g. `"HMI_RT_1"` or `"HMI_1"`). When the project has exactly one HMI it is selected automatically. With several HMIs it is required, and the error lists the candidates. |
  | `target_device` | string | No | null | Deprecated alias of `target_name`, kept for existing clients. If both are given they must match. |
- `ICompilable` is taken from the first object that offers it: the HMI software's container, the device item that holds the HMI software, its parent device items, then the device. `hmi_import_json` and `hmi_patch_json` compile through the same path.
- **Sample Invocation:** `{ "target_name": "HMI_RT_1" }`
- **Response Example:**
  ```json
  {
    "State": "Success",
    "ErrorCount": 0,
    "WarningCount": 0,
    "Messages": [
      {
        "DateTime": "2026-09-30 23:01:25",
        "Description": "Compiling finished (errors: 0; warnings: 0)",
        "State": "Success"
      }
    ],
    "Target": "HMI_RT_1",
    "DeviceName": "PC-System_1",
    "CompiledObject": "SoftwareContainer of 'HMI_RT_1'"
  }
  ```
- **Not verified against a live TIA Portal:** which object returns `ICompilable`, and that HMI script errors are reported through it. Before v2.14.0 the device was compiled.

---

### `hmi_get_runtime_settings`
- **Description:** Read-only. Returns the WinCC Unified Runtime settings of an HMI (`HmiSoftware.RuntimeSettings`).
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `target_name` | string | No | null | Exact HMI device, device item or software name; required when several HMIs exist. |
- **Returns:** `TargetSoftwareName`, `StartScreen`, `StartScreenExists`, `ScreenResolution`, `AutoLogOffUrl`, `BitSelection`, `BitSelectionStrategyForResourceLists`, `BitSelectionStrategyForTagDynamization`, `CentralInputHint`, `CentralPanning`, `CentralZooming`, `EnableLanguageCompatibleFontFamilies`, `GmpEnabled`, `GeneralEsigCommentsStrategy`, `ReadErrors` (properties that could not be read) and `Validation` (Runtime settings validation errors and warnings per property).

### `hmi_set_start_screen`
- **Description:** Sets the WinCC Unified Runtime start screen in place. No screens or screen items are changed.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `screen_name` | string | **Yes** | — | Exact, case-sensitive name of an existing screen, also one inside a screen group. |
  | `on_conflict` | string | **Yes** | — | Only `"fail"` is supported. |
  | `expected_current_start_screen` | string | No | null | Guard: the start screen you expect now (`""` means none). If the current value differs, nothing changes. |
  | `target_name` | string | No | null | Exact HMI device, device item or software name. |
- The value is read back and the Runtime settings are validated. If the read-back differs (`verification_failed`) or validation reports a start-screen error (`validation_failed`), the previous start screen is restored, nothing is committed, and `StartScreen` in the result is the previous value. Validation errors on other settings are only reported.
- **Status values:** `changed`, `unchanged`, `validation_failed`, `verification_failed`, `api_failure`.
- **Result:** `{ Success, Operation, Status, TargetSoftwareName, OldStartScreen, StartScreen, Message, Warnings, Validation }`.
- For a frame screen with a screen window, set the frame screen as the start screen and set the window's content with `hmi_update_screen_item` (`Screen`).
- **Sample Invocation:** `{ "screen_name": "Frame", "on_conflict": "fail" }`
- **Not verified against a live TIA Portal:** that the value persists after save, that a grouped screen is accepted by its bare name, and that Runtime starts on the new screen.

---

### 42. `hmi_list_connections`
- **Description:** List all HMI communication connections in the WinCC Unified HMI software (name, communication driver, partner station, partner node, and disabled status).
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `device_name` | string | No | null | Target HMI device or software name (defaults to first HMI target). |
- **Response Example:**
  ```json
  [
    {
      "Name": "HMI_PLC_1_Connection_1",
      "CommunicationDriver": "SIMATIC S7 1200/1500",
      "Partner": "PLC_1",
      "Station": "PLC_1",
      "Node": "CPU 1511TF-1 PN, PROFINET interface (R0/S1)",
      "DisabledAtStartup": false,
      "Comment": ""
    }
  ]
  ```

---

### 43. `hmi_create_connection`
- **Description:** Create or configure an integrated HMI connection between a WinCC Unified HMI target and a partner PLC device/CPU over industrial Ethernet. Automatically creates the integrated hardware connection using `CommunicationManagement.Connections.Create<HmiConnection>` between local and partner CPU interfaces.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `connection_name` | string | **Yes** | — | Name of the HMI connection to create (e.g. `"HMI_PLC_1_Connection_1"`). |
  | `partner_plc_name` | string | **Yes** | — | Name of the partner PLC device in the project (e.g. `"PLC_1"`). |
  | `device_name` | string | No | null | Target HMI device or software name. |
  | `communication_driver` | string | No | `"SIMATIC S7 1200/1500"` | Communication driver. |
  | `disabled_at_startup` | boolean | No | `false` | Whether the connection is disabled at startup. |
  | `comment` | string | No | null | Optional comment describing the connection. |
- **Response Example:**
  ```json
  {
    "Success": true,
    "Message": "Created integrated HMI connection 'HMI_PLC_1_Connection_1' between 'PC-System_1' (X1: 192.168.0.1) and 'PLC_1' (X1: 192.168.0.101).",
    "Connection": {
      "Name": "HMI_PLC_1_Connection_1",
      "CommunicationDriver": "SIMATIC S7 1200/1500",
      "Partner": "PLC_1",
      "Station": "PLC_1",
      "Node": "CPU 1511TF-1 PN, PROFINET interface (R0/S1)",
      "DisabledAtStartup": false,
      "Comment": ""
    }
  }
  ```

---

### 44. `hmi_delete_connection`
- **Description:** Delete an HMI connection from WinCC Unified software and/or hardware communication management. Removes the connection from both hardware topology (`CommunicationManagement.Connections`) and runtime software connections (`HmiSoftware.Connections`).
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `connection_name` | string | **Yes** | — | Name of the HMI connection to delete (e.g. `"HMI_Connection_2"`). |
  | `device_name` | string | No | null | Target HMI device or software name. |
- **Response Example:**
  ```json
  {
    "Success": true,
    "ConnectionName": "HMI_Connection_2",
    "DeletedHardware": false,
    "DeletedSoftware": true,
    "Message": "Successfully deleted HMI connection 'HMI_Connection_2' (Hardware: False, Software: True)."
  }
  ```

---

### 45. `hmi_list_text_lists`
- **Description:** List all user-defined text lists on a WinCC Unified device with their integer values/ranges, default texts, and multilingual entries.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `device_name` | string | No | null | Target HMI device or software name (optional). |
- **Sample Invocation:**
  ```json
  {
    "name": "hmi_list_text_lists",
    "arguments": {
      "device_name": "PC-System_1"
    }
  }
  ```
- **Response Example:**
  ```json
  [
    {
      "Name": "testList",
      "Entries": [
        {
          "FromValue": 0,
          "ToValue": 0,
          "DefaultText": "zero",
          "MultilingualTexts": {
            "en-US": "zero",
            "fr-CA": "zero"
          }
        },
        {
          "FromValue": 1,
          "ToValue": 1,
          "DefaultText": "one",
          "MultilingualTexts": {
            "en-US": "one",
            "fr-CA": "one"
          }
        },
        {
          "FromValue": 2,
          "ToValue": 2,
          "DefaultText": "two",
          "MultilingualTexts": {
            "en-US": "two",
            "fr-CA": "two"
          }
        }
      ]
    }
  ]
  ```

---

### 46. `hmi_create_text_list`
- **Description:** Create a new text list on a WinCC Unified device with specified entries and values/ranges. Automatically discovers configured device runtime languages (`en-US`, `fr-CA`, etc.) and populates entries across all languages.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `list_name` | string | **Yes** | — | Name of the text list to create. |
  | `entries` | array | **Yes** | — | List of entries: `[{ "value": 0, "from_value": 0, "to_value": 0, "text": "zero", "multilingual_texts": { "en-US": "zero" } }]`. |
  | `device_name` | string | No | null | Target HMI device or software name (optional). |
- **Sample Invocation:**
  ```json
  {
    "name": "hmi_create_text_list",
    "arguments": {
      "list_name": "testList",
      "entries": [
        { "value": 0, "text": "zero" },
        { "value": 1, "text": "one" },
        { "value": 2, "text": "two" }
      ],
      "device_name": "PC-System_1"
    }
  }
  ```
- **Response Example:**
  ```json
  {
    "Success": true,
    "ListName": "testList",
    "EntryCount": 3,
    "Message": "Successfully created text list 'testList' with 3 entries."
  }
  ```

---

### 47. `hmi_delete_text_list`
- **Description:** Delete an existing HMI text list from a WinCC Unified device.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `list_name` | string | **Yes** | — | Name of the text list to delete. |
  | `device_name` | string | No | null | Target HMI device or software name (optional). |
- **Sample Invocation:**
  ```json
  {
    "name": "hmi_delete_text_list",
    "arguments": {
      "list_name": "statusList"
    }
  }
  ```
- **Response Example:**
  ```json
  {
    "Success": true,
    "ListName": "statusList",
    "EntryCount": 0,
    "Message": "Successfully deleted text list 'statusList'."
  }
  ```

---

### 48. `hmi_export_text_lists`
- **Description:** Export all HMI text lists on a WinCC Unified device to YAML files (`.hmi.yml` and `.TextLibrary.hmi.yml`) in a target directory.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `destination_folder` | string | **Yes** | — | Target directory on disk to receive exported files. |
  | `base_filename` | string | **Yes** | — | Base filename prefix for export (e.g. `"AllTextLists"`). |
  | `device_name` | string | No | null | Target HMI device or software name (optional). |
- **Response Example:**
  ```json
  [
    "C:\\Exports\\AllTextLists.hmi.yml",
    "C:\\Exports\\AllTextLists.TextLibrary.hmi.yml"
  ]
  ```

---

### 49. `tia_list_tag_tables`
- **Description:** Enumerate PLC tag tables in the CPU software container.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `plc_name` | string | No | null | Target PLC name. |

---

### 50. `tia_get_tags`
- **Description:** Read PLC tags (%I, %Q, %M addresses, data types, comments) from a PLC tag table.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `table_name` | string | No | null | PLC tag table name. |
  | `plc_name` | string | No | null | Target PLC name. |

---

### 51. `hmi_export_json`
- **Description:** Export a Unified HMI target to `tia-mcp-hmi/1` JSON files, including group-aware screens, tag tables, discrete alarms, optional styles, and SHA-256 file hashes.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `output_directory` | string | No | staged transfer | Optional server-local destination. Omit for a remote-readable export; response includes `transferId` and `files`. |
  | `compact` | boolean | No | `true` | Omit item properties equal to the TIA-created type defaults. |
  | `script_files` | boolean | No | `false` | Store event scripts as `.js` sidecar files rather than inline strings. |
  | `include_library` | boolean | No | `true` | Include the complete project type library in `library/HmiAssets.zal21`, including faceplates, dynamic SVGs, all committed versions and PLC dependencies. |
  | `target_name` | string | No | null | Target HMI software/device selector. |

### `hmi_read_export_file`
- **Purpose:** Fetch one file from a staged export. The result contains Base64-encoded bytes so JSON and script files round-trip exactly.
- **Parameters:** `transfer_id`, `relative_path`, optional `offset` (default 0), and optional `chunk_bytes` (default 262144; maximum 524288).
- **Chunking:** Decode `contentBase64`, append it at `offset`, then request the next chunk at `nextOffset` until `complete` is true. Each file is limited to 10 MiB and each staged export to 100 MiB.

### `hmi_begin_import`
- **Purpose:** Create a server-side staging area and return a `transferId` for a remote import.
- **Limits:** 10 MiB per file, 100 MiB per transfer, 5000 files, 512 KiB per uploaded chunk. Transfers expire after 24 hours.

### `hmi_upload_import_file`
- **Purpose:** Upload one relative-path file into a staged import as sequential Base64 chunks.
- **Parameters:** `transfer_id`, `relative_path`, `content_base64`, `offset`, and `total_bytes`. Start each file at offset 0; continue using `receivedBytes` until `complete` is true.

**JSON fidelity:** Tag and Expression dynamizations include their value converters,
ordered Simple/Range/Bitmask mapping entries, typed values, flashing settings, and raw
text markup. Bitmask entries preserve the API's `bitDynamizationType`, unsigned 64-bit
`condition`, and generated `relevant` mask. Faceplates include `containedType`; compact
defaults are cached per contained type. Internal tags include `dataType`; unset limits
are null. Unsupported dynamizations, mapping kinds, and tag-based limits fail explicitly
rather than exporting lossy placeholders.
Legacy fork exports that wrote the bitmask condition as `bitCondition` still import. Script dynamizations retain code,
global definition code, async mode and trigger type/duration/tag list. ResourceList
dynamizations retain the resource-list name and tag; the actual text/graphic list
must already exist in the destination (it is not embedded in this export).
Tag tables include nested group paths; structured tags include member names/types
for reference validation. Destination system tags are recognized without exporting
them as user-created tags.
Faceplates and custom widgets export typed `interface` values, including nested custom
properties. Legacy exports without `interface` leave instance values untouched.
Custom widgets export supported full properties (`compact: false`) and a manifest
warning: their creation identity is not serialized, so imports require
the widget to already exist. Complex control compositions are outside this format's
scope; this is not a complete project backup.
Compact exports also contain a hashed `defaults.json`, referenced by manifest
`defaultsFile`, for offline rendering. Imports continue to use destination TIA defaults.

**Native definitions:** `libraryFile` references a SHA-256-hashed V21 `.zal21` archive;
`libraryTypes` lists type names and type/version GUIDs. This transfers the actual
faceplate/SVG library definitions, not the metadata-only XML returned by
`LibraryTypeVersion.Export`. All project library types are included, even those used
by other targets. Export requires committed versions and saves only the temporary
global library, never the project. Set `include_library: false` for JSON-only exports.
Raw `.svghmi` extraction/direct source import is not implemented; import new sources
using TIA's library editor. Local schemas are under [schemas/svghmi](../schemas/svghmi).

---

### 52. `hmi_import_json`
- **Description:** Validate and upsert a versioned JSON export under an Openness transaction, apply declared verification requirements, and compile the HMI without saving the project.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `input_directory` | string | One required | — | Server-local directory containing `manifest.json` and referenced JSON/script files. |
  | `transfer_id` | string | One required | — | Completed upload ID from `hmi_begin_import`; use instead of `input_directory`. |
  | `prune_items` | boolean | No | `false` | Delete screen items absent from imported screen files. |
  | `dry_run` | boolean | No | `false` | Validate the files and target without making changes. |
  | `verify_hashes` | boolean | No | `true` | Verify exported hashes. Set false only for intentional edits that have not refreshed manifest hashes. |
  | `target_name` | string | No | null | Target HMI software/device selector. |

When `libraryFile` is present, import retrieves a temporary read-only global library,
validates its inventory and conflicts, and restores missing type versions inside the
project transaction before creating faceplate instances. Existing committed versions
with matching GUIDs are reused. Name/GUID/version conflicts fail, never overwrite.
Dry runs inspect the package but do not copy types into the project. Legacy packages
without a library archive require destination types to already exist.
`input_directory` and `transfer_id` are mutually exclusive. For remote OpenCode clients,
omit `output_directory` on export, fetch the returned file list with
`hmi_read_export_file`, then create an import transfer and upload the edited manifest
and all referenced files with `hmi_upload_import_file`. Use `hmi_import_json` with
`dry_run: true` before authorizing an actual import. Staging paths are confined to
server-managed transfer directories; no arbitrary server path is accepted by transfer
tools. Edited files must have refreshed manifest hashes, or the import preview must
explicitly set `verify_hashes: false`.
A dynamization reference may be a dotted UDT member path such as
`UnitParent_UnitData.UnitStatus.Error`; validation requires its root HMI tag to exist,
and the full member path is preserved when assigned to TIA.
Properties that already have the requested value are not written again. This matters for
compact exports, whose type defaults include properties such as `HmiIOField.ShowInputHint`
that Openness V21 can read but not set. A failing item or tag stops the import with its
name, the property and the inner Openness message (for example
`Item 'Sailio/TE6_IO' (HmiIOField) failed: ...`). Internal HMI tags may omit `connection`
and `plcTag`. A tag that has a connection but no PLC tag is skipped with a `warnings`
entry instead of failing the import; references to an existing tag of that name still resolve.

**Missing keys (since v2.14.0).** An item without a `text`, `dynamizations` or `events`
key, or with the key set to `null`, keeps that part of an existing item unchanged. `[]`
removes all dynamizations or events of the item. `text: {}` sets nothing in a non-compact
import and resets the texts to the type defaults in a compact import. A compact import
still resets omitted `props` to the type defaults; use item `compact: false` or
`hmi_patch_json` for partial property edits. Items with omitted sections are listed in
the result `warnings`. Before v2.14.0, such items lost their dynamizations and events.

**Flags enums.** Properties with a flags enum type, such as `WindowFlags`, take one member
name or names separated by `,` or `|` (export writes `"ShowCaption, CanClose"`). `None`
clears all flags. Names are case-insensitive; numbers and empty names are rejected. Enum
values are validated before any change (also in a dry run) and compared by value on
read-back.

**Singlebit and Bitmask mapping tables.** Bitmask entries cannot be deleted or reordered
through Openness. An existing Singlebit or Bitmask table is therefore updated in place
when the condition type, bit dynamization type, entry count, relevant masks and entry order
match TIA's, so an unchanged export imports again without errors. A changed condition type
or relevant mask (bit) needs `"onConflict": "replace"` on that dynamization: only the
dynamization is recreated, never the item. The relevant mask is read-only in Openness, so
a recreated table works only when TIA generates the requested bit. Entries listed in a
different order than TIA's are refused, also with `replace`; keep the exported order.
`dry_run: true` reports these mapping-table refusals before any change.

**Script read-back.** Event scripts and script dynamizations are written exactly as given.
The read-back ignores CRLF/LF differences, trailing spaces and tabs on each line, a BOM
and trailing line breaks. Other differences fail and name the first differing line.

**Plain-string texts** are escaped exactly as written and are not decoded, so an
export/import round trip is lossless: a text that literally contains `&#196;` stays
`&#196;`. Write plain Unicode in the document. Text and alarm `eventText` read-back
accepts `{ "html": ... }` and equal language maps.

**Screen item order (`zOrder`).** The item array order in a screen file is the stacking
order. Import never reorders existing items (V21 has no API for it). New items are created
in file order, and TIA decides where they land. After applying, the import compares each
imported screen's item order with the file. Set `"zOrder"` in `manifest.json` (all
screens) or in a screen file (that screen only):
  | Value | Effect |
  | :--- | :--- |
  | `"keep"` (default) | Differences are returned in `zOrderWarnings`; the import continues. |
  | `"require"` | A difference fails the import and rolls it back. |

A dry run reports existing items that are already out of file order. Fix the order with
*Arrange* in the TIA editor. Older server versions ignore `zOrder`. Not verified against
a live TIA Portal: whether the `ScreenItems` order equals the visual stacking order, and
whether the order inside the transaction equals the order after commit.

---

### 53. `hmi_patch_json`
- **Description:** Validate or apply guarded `set`, `bind`, and `delete` operations from a `tia-mcp-hmi-patch/1` file, then verify and compile without saving.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `file_path` | string | **Yes** | — | Path to the JSON patch file. |
  | `dry_run` | boolean | No | `false` | Validate preconditions and report operations without applying them. |
  | `target_name` | string | No | null | Target HMI software/device selector. |

A patch changes existing items only. It cannot create or rename items, or edit events,
scripts or expression dynamizations. A patch file is one JSON object of at most 10 MiB.
The same format is described in the `hmi-json` skill, section *Guarded patch workflow*.

**Root fields**
  | Field | Required | Meaning |
  | :--- | :---: | :--- |
  | `format` | Yes | Exactly `"tia-mcp-hmi-patch/1"`. |
  | `require` | No | Preconditions checked before any change. |
  | `operations` | Yes | Ordered list of `set`, `bind` and `delete` operations, at most 10,000. |
  | `verify` | No | Postconditions checked after the operations, before commit. |

**`require` and `verify`** have the same shape:
  | Field | Meaning |
  | :--- | :--- |
  | `screens[].name` | Screen name (required). |
  | `screens[].group` | Slash-separated screen group path; `null` or omitted for a root screen. |
  | `screens[].width`, `screens[].height` | Optional; must equal the screen size. |
  | `screens[].items[].name` | Item that must exist on that screen. |
  | `screens[].items[].type` | Optional exact item class name, for example `HmiIOField`. |
  | `tags[].name` | HMI tag that must exist. |
  | `tags[].plcTag` | Optional; the tag's PLC binding must equal this text exactly. |

`require` is checked against the project before the patch, including `screens[].items`.
After the operations, the screens, dimensions and tags in `require` are checked again
(items are not, so a patch may require an item and then delete it). `verify` checks
screens, dimensions, tags and the listed items with their types.

**Operation fields**
  | Field | Used by | Required | Meaning |
  | :--- | :--- | :---: | :--- |
  | `op` | all | Yes | `"set"`, `"bind"` or `"delete"` (lower case). |
  | `screen` | all | Yes | Screen name. |
  | `group` | all | No | Screen group path; `null` or omitted for a root screen. |
  | `item` | all | Yes | Existing item name. |
  | `requireType` | all | No | The item class name must match exactly, for example `HmiIOField`. |
  | `property` | set, bind | Yes | Openness property name. `set` accepts a dotted path into parts such as `Font.Size`; `bind` takes a top-level dynamizable property such as `ProcessValue` or `BackColor`. |
  | `value` | set | Yes | New value with the JSON type of the property: number, `true`/`false`, enum name (flags enums: names separated by `,` or `\|`), `#RRGGBB` color, or for a text property a string, language map or `{ "html": "..." }`. |
  | `tag` | bind | Yes | Existing HMI tag, member/array path, or system tag. |
  | `expectTag` | bind | No | Guard: the property must already have a tag dynamization bound to exactly this tag. |
  | `readOnly` | bind | No | `true`/`false`. Omitted: keeps the current value of an existing tag dynamization (`false` for a new one). |
  | `useIndirectAddressing` | bind | No | Same rule as `readOnly`. |
  | `onConflict` | bind | No | `"fail"` (default) or `"replace"`. `replace` allows replacing a non-tag (Expression, Script or ResourceList) dynamization with a tag dynamization. |
  | `requireNoDynamizations` | delete | No | Fail if the item has dynamizations (checked before the patch). |
  | `requireNoEvents` | delete | No | Fail if the item has event handlers (checked before the patch). |

Field names are case-insensitive. The values of `format` and `op` are case-sensitive;
`onConflict` is not. Unknown fields are ignored and listed in the result `warnings`;
check them, because a misspelled guard is not applied.

**`bind`** reuses an existing tag dynamization and writes only its tag (and the two
flags). It never changes the value converter or mapping table, so an existing Range,
Singlebit or Bitmask table stays as it is. Use it to retag Singlebit/Bitmask items without
re-importing their tables. Whether TIA adjusts the table when the new tag has a different
data type is not verified; read the item back after such a retag.

**Processing order**
1. Read the file, check `format`, check `require` (screens, dimensions, tags, items).
2. Validate every operation against the project state before the patch. An operation on
   an item that an earlier operation deletes is rejected.
3. With `dry_run: true`, stop and return the operation list. For `bind` it shows
   `expectTag`, `readOnly` and `useIndirectAddressing` (booleans when given, `"keep"` when
   omitted) and `onConflict`.
4. Otherwise, in one transaction: apply the operations in order, re-check `require`,
   check `verify`, commit.
5. Compile the HMI. The project is never saved.

A failure in steps 1 to 4 changes nothing (the transaction rolls back). A compile error in
step 5 is reported as an error, but the changes stay applied in memory and unsaved.

**Example:** retag a Singlebit lamp without touching its mapping table; the patch fails if
the lamp is not bound to the old tag now:
```json
{
  "format": "tia-mcp-hmi-patch/1",
  "require": {
    "tags": [ { "name": "Motor2_Running", "plcTag": "DB_Motors.Motor2.Running" } ]
  },
  "operations": [
    {
      "op": "bind",
      "screen": "Overview",
      "item": "Lamp_Motor2",
      "property": "BackColor",
      "tag": "Motor2_Running",
      "expectTag": "Motor1_Running"
    }
  ]
}
```

Not verified against a live TIA Portal: a retag of a Singlebit/Bitmask dynamization, a
re-bind that keeps `ReadOnly`, and a bind to a system tag.

---

### 68. `hmi_render_json`
- **Description:** Offline Windows rendering of `tia-mcp-hmi/1` exports to PNGs, numbered layout overlays, and JSON geometry/warnings reports. Does not connect to TIA, run scripts, or evaluate tag/expression state.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `input_directory` | string | **Yes** | — | Export directory containing `manifest.json`. |
  | `output_directory` | string | **Yes** | — | Separate output directory; existing output files are never overwritten. |
  | `screen_name` | string | No | all screens | Screen name or group/name path. Ambiguous names fail. |
  | `language` | string | No | en-US, else first manifest language | Exported text language. Missing translations are reported. |
  | `scale` | number | No | 2 | Output pixels per screen pixel; 0.5–4, max 8192 per axis and 32 megapixels per image. |
- **Outputs:** `render-index.json` plus `<screen>.png`, `<screen>.layout.png`, and `<screen>.layout.json`. The tool returns absolute paths and counts. Report IDs match overlay labels. Bounds `[x,y,width,height]` are unscaled screen-space axis-aligned bounds including ancestor transformations and rotation.
- **Resolution:** Shared styles, captured compact defaults, text/markup language selection, z-order, geometry, colors, fonts, static visibility, and nested screen windows with clipping/zoom/scroll. IO fields bound to runtime values display `?`.
- **Limits:** Legacy compact exports without `defaultsFile` use approximate omitted values and warn. Faceplates, custom widgets/SVG, alarm/trend controls and other unsupported controls are explicit geometry placeholders. Graphics are not embedded in the export. Font substitutions, rich-text/skin/window-adaption/chrome/compositing approximations are reported. Dynamic visibility/text/color remain static and are identified in the report. This is not a runtime screenshot or visual sign-off.
- **Integrity:** Rendering intentionally accepts edited JSON without checking manifest hashes. It never imports changes. Import still verifies hashes by default. Relative paths are confined to the input directory; cyclic nested windows and excessive dimensions fail explicitly.
- **CLI:** `TiaOpennessMcp.exe --render-hmi INPUT --output OUTPUT [--screen GROUP/NAME] [--language en-US] [--scale 2]`. This mode bypasses server startup and auto-connection.

### New in v2.2.0: Direct Item, Event, and Expression Operations

#### `hmi_get_screen_item`
- **Description:** Reads one item as a complete supported `tia-mcp-hmi/1` item document, including its type/properties, multilingual text, event scripts, and complete tag/expression value-converter state.
- **Parameters:** `screen_name` and `item_name` are required; `target_name` is optional.
- **Behavior:** Read-only. Expression formulas, conditions, mappings, alternates, flashing settings, and tag binding options are included where supported by V21.

#### `hmi_update_screen_item`
- **Description:** Updates one item in place and returns the full item readback. It never deletes or recreates the item.
- **Parameters:** `screen_name`, `item_name`, and non-empty `item_properties` object are required; `target_name` is optional.
- **Allowlisted properties:** use the names exactly as listed.
  - `Visible`, `Enabled`, `Left`, `Top`, `Width`, `Height`, `CenterX`, `CenterY`, `Radius`
  - `BackColor`, `ForeColor`, `BorderColor` (`#RRGGBB` or `#AARRGGBB`), `BorderWidth`
  - `OutputFormat` (IO fields)
  - `HorizontalTextAlignment` (`Left`/`Center`/`Right`/`Stretch`), `VerticalTextAlignment` (`Top`/`Center`/`Bottom`/`Stretch`)
  - `Screen` (screen windows: the exact name of an existing screen in the same HMI; empty is rejected)
  - `Text`
  - `Font`, as an object `{ Size, Weight (Light/Normal/SemiBold/Bold), Italic, Underline, StrikeOut, Name (Arial/TimesNewRoman/SimSun/SiemensSans), Bold }` or as dotted keys such as `"Font.Size": 14`. `Font.Bold` true/false is shorthand for `Weight` Bold/Normal; do not combine it with `Weight`. Each property may appear only once, nested or dotted.
- **Text:** a plain string becomes a single paragraph that keeps the existing `<body>` and first `<p>` attributes (for example `style="margin: 0; text-align: center;"`). Further paragraphs and inline formatting are dropped, and the result reports this under `warnings`. A string that starts with `<body` is written as raw markup and must be well-formed. `{ "html": "<body>...</body>" }` sets raw markup for every language. A language map such as `{ "en-US": "Start", "fi-FI": "Käynnistä" }` must cover every active language. The schema declares `Text` as `anyOf` string or object; clients that support only part of JSON Schema may not show it.
- **Behavior:** A property the item type does not have, a wrong value type, an unknown enum name, malformed markup or a missing screen fails before anything is changed. The update is applied transactionally and every supplied property is verified by reading the value back. If an updated property also has a dynamization, the static value is still written, the result gets a `warnings` entry, and the dynamization is left as it is. No project save is performed.
- **Sample Invocation:** `{ "screen_name": "Overview", "item_name": "btn_Start", "item_properties": { "Text": "Käynnistä", "Font": { "Size": 14, "Bold": true }, "Enabled": false } }`
- **Not verified against a live TIA Portal:** `Font.Size` round trip, `Text` on every item type, accepted `OutputFormat` values, `Screen` with grouped screens, and the `Enabled` setter on every item type.

#### `hmi_read_event_scripts`
- **Description:** Reads event scripts on a screen, or on one item when `item_name` is supplied, and returns supported event enum names for that target.
- **Parameters:** `screen_name` is required; `item_name` and `target_name` are optional.
- **Behavior:** Event names come from the V21 Openness event composition for the selected target type. Missing targets and unsupported event-handler APIs fail explicitly.

#### `hmi_set_event_script`
- **Description:** Creates or updates one event handler in place on a screen or item.
- **Parameters:** `screen_name`, `event_type`, and `script` are required; `item_name` and `target_name` are optional.
- **Behavior:** Only event values supported by the selected V21 handler enum are accepted. Other event handlers and item identity/layout are preserved. The new source is read back and checked; the read-back ignores line endings and trailing whitespace. Syntax diagnostics are returned when exposed by the API. No project save is performed.

#### `hmi_set_expression_dynamization`
- **Description:** Creates or updates the formula of one expression dynamization without reconciling or replacing other dynamizations or the existing expression's mapping entries.
- **Parameters:** `screen_name`, `item_name`, `property_name`, `expression`, and `result_type` are required; `on_conflict` (`fail` or `replace`) and `target_name` are optional.
- **Declared result types:** `Boolean`, `Color`, `String`, `Byte`, `Int16`, `UInt16`, `Int32`, `UInt32`, `Int64`, `UInt64`, `Single`, `Double`, and `Decimal`. The declared type must match the target property's CLR type. The formula must be non-empty; its expression semantics are not statically inferred by this tool.
- **Behavior:** Replacing a tag dynamization on the same property requires `on_conflict: "replace"`. Formula state and existing mapping entries are verified by readback. No project save or compile is performed.

#### `hmi_set_expression_dynamization_entries`
- **Description:** Replaces one property's ordered Expressions-tab condition/value rows and returns the verified readback.
- **Parameters:** `screen_name`, `item_name`, `property_name`, and non-empty `entries` are required; `on_conflict` (`fail` or `replace`) and `target_name` are optional.
- **Entry format:** Each row has a non-empty `condition` string and typed `value` object, for example `{ "condition": "AND32('StatusWord', 1) != 0", "value": { "type": "Int32", "value": "500" } }`. Values must match the target property's CLR type.
- **Behavior:** The supplied list replaces all condition rows for that property, in order. Existing same-property dynamizations require `on_conflict: "replace"`; other dynamizations, item properties, and events are preserved. TIA validates expression syntax; this tool does not compile or save the project.

### New in v2.4.0: Expression condition rows

The `hmi_set_expression_dynamization_entries` tool writes condition/value tables. It is separate from `hmi_set_expression_dynamization`, which writes a value formula.

### New in v2.3.0: Runtime JavaScript declarations

#### `hmi_get_js_type_definitions`
- **Description:** Returns the server-bundled TypeScript declarations for the WinCC Unified runtime APIs documented by this server.
- **Parameters:** None.
- **Behavior:** This is an offline, read-only reference operation and does not connect to TIA Portal. The returned declarations are a conservative subset, not Siemens' complete or vendor-generated V21 type definitions. Unverified tag-read and direct item-object access APIs are intentionally omitted.
- **Resource:** The same declaration file is available at `tia://docs/hmi-javascript-type-definitions` with MIME type `text/typescript`.

---

## Category 5: Siemens TestSuite Automation (2 Tools)

### 54. `tia_testsuite_list_tests`
- **Description:** Lists all configured application test cases in the project's TestSuite container.
- **Parameters:** None (`{}`)
- **Response Example:**
  ```json
  [
    {
      "Name": "TC_MixingLine_Sequence",
      "Scope": "PLC_1"
    }
  ]
  ```

---

### 55. `tia_testsuite_load_test`
- **Description:** Loads, imports, or reloads a `.tat` test file into the project with `ImportOptions.Override`.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `file_path` | string | **Yes** | — | Absolute path to `.tat` test file. |
- **Sample Invocation:**
  ```json
  {
    "name": "tia_testsuite_load_test",
    "arguments": {
      "file_path": "D:\\TIA_Project\\_AEs\\TestMCP-DDup\\UserFiles\\AI\\TC_MixingLine_Sequence.tat"
    }
  }
  ```
- **Response Example:**
  ```json
  {
    "Success": true,
    "Message": "Successfully loaded 1 test case(s) from 'TC_MixingLine_Sequence.tat'.",
    "LoadedCases": [
      {
        "Name": "TC_MixingLine_Sequence",
        "Scope": "PLC_1"
      }
    ]
  }
  ```

---

## Category 6: S7-PLCSIM Advanced Live Simulation & Testing (12 Tools)

**Supported simulator.** The `plcsim_*` tools support S7-PLCSIM Advanced V8.0 or newer
only (S7-1500 and ET 200SP virtual controllers). Standard S7-PLCSIM (V18-V21), and
therefore S7-1200 simulation, has no public API and is not supported.
`plcsim_list_instances` reports when standard S7-PLCSIM is running. If that instance has
TCP/IP communication enabled, `plc_online_connect` to its IP address may be able to read
tags (read-only; not verified against standard S7-PLCSIM).

The server loads the API from `Common Files\Siemens\PLCSIMADV\API\8.0`, or else from the
newest API folder of version 8.0 or later, and writes a note to stderr when it uses a
folder other than 8.0 (binary compatibility with such a folder is not verified). Older API
folders are never used. When the API is missing or the runtime is not running, every
`plcsim_*` tool error explains the cause and refers to `plcsim_list_instances`.

### 56. `plcsim_list_instances`
- **Description:** Lists the registered S7-PLCSIM Advanced instances with ID, name, operating state, CPU type and IP address, together with a diagnosis of the runtime and API.
- **Parameters:** None (`{}`)
- **Changed in v2.14.0 (breaking):** the result is an object, not an array. The elements of `instances` keep the previous fields. The call succeeds (`isError` false) also when S7-PLCSIM Advanced is not available, so check `runtimeAvailable`.
- **Result fields:** `runtimeAvailable`, `apiPath`, `apiVersion` (the `PLCSIMADV\API` folder version, e.g. `"8.0"`), `apiAssemblyVersion` (loaded assembly version, null until the API is loaded), `runtimeManagerAvailable`, `runtimeVersion`, `instances[]`, `standardPlcSimProcesses[]` (name and process ID of a running standard S7-PLCSIM), `message` (explains an empty list) and `error`.
- **Sample Response** (PLCSIM Advanced not installed, standard S7-PLCSIM running):
  ```json
  {
    "runtimeAvailable": false,
    "apiPath": null,
    "apiVersion": null,
    "apiAssemblyVersion": null,
    "runtimeManagerAvailable": null,
    "runtimeVersion": null,
    "instances": [],
    "standardPlcSimProcesses": [ { "name": "S7-PLCSIM", "processId": 1234 } ],
    "message": "S7-PLCSIM Advanced V8.0+ runtime not available: ... Only S7-PLCSIM Advanced V8.0+ is supported ...",
    "error": "..."
  }
  ```
  With a running PLCSIM Advanced runtime, `instances` holds objects with `ID`, `Name`, `OperatingState`, `CPUType` and `IP`, as before.
- **Not verified live:** the process names used to detect standard S7-PLCSIM V21, the field values with PLCSIM Advanced V8 installed, and API folders newer than 8.0.

---

### 57. `plcsim_connect`
- **Description:** Attaches directly to a virtual S7-PLCSIM Advanced instance by name, caching symbolic tag tables for zero-overhead live I/O.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `instance_name` | string | No | `"PLC_1"` | Target virtual controller name. |
- **Sample Invocation:**
  ```json
  {
    "name": "plcsim_connect",
    "arguments": { "instance_name": "PLC_1" }
  }
  ```
- **Response Example:**
  ```json
  {
    "Success": true,
    "InstanceName": "PLC_1",
    "ID": 0,
    "OperatingState": "Run",
    "OperatingMode": "Default",
    "CPUType": "CPU1511TF",
    "IP": "192.168.0.101",
    "ScaleFactor": 1.0,
    "TagCount": 314,
    "Message": "Successfully connected to S7-PLCSIM Advanced instance 'PLC_1'."
  }
  ```

---

### 58. `plcsim_disconnect`
- **Description:** Disconnects from the current virtual PLC instance and clears cached tag metadata.
- **Parameters:** None (`{}`)

---

### 59. `plcsim_get_status`
- **Description:** Retrieves real-time status of the connected virtual controller: state (`Run`/`Stop`), scale factor, CPU model, IP, and total tags in memory.
- **Parameters:** None (`{}`)
- **Response Example:**
  ```json
  {
    "Name": "PLC_1",
    "ID": 0,
    "OperatingState": "Run",
    "OperatingMode": "Default",
    "CPUType": "CPU1511TF",
    "IP": "192.168.0.101",
    "ScaleFactor": 2.0,
    "TagCount": 314
  }
  ```

---

### 60. `plcsim_set_operating_state`
- **Description:** Commands the virtual CPU operating mode (`Run`, `Stop`, or `MemoryReset`).
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `state` | string | **Yes** | — | Desired operating state (`"Run"`, `"Stop"`, `"MemoryReset"`). |

---

### 61. `plcsim_set_scale_factor`
- **Description:** Sets the simulation clock scale factor (e.g. `2.0` = 2x speed, `5.0` = 5x speed) to accelerate automated testing deterministically.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `scale_factor` | number | **Yes** | — | Positive speed scale multiplier (> 0.0). |

---

### 62. `plcsim_read_tag`
- **Description:** Reads the live value and data type of any variable from DBs, inputs, outputs, or markers by symbolic name.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `tag_name` | string | **Yes** | — | Symbolic tag name (e.g. `'DB_MixingLine.Status_StepNumber'`). |
- **Response Example:**
  ```json
  {
    "TagName": "DB_MixingLine.Status_StepNumber",
    "Value": 20,
    "DataType": "Int"
  }
  ```

---

### 63. `plcsim_write_tag`
- **Description:** Writes a live value to a symbolic variable in virtual PLC memory.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `tag_name` | string | **Yes** | — | Symbolic tag name (e.g. `'DB_MixingLine.Cmd_StartBatch'`). |
  | `value` | any | **Yes** | — | Value to write (boolean, integer, float, string). |

---

### 64. `plcsim_read_tags`
- **Description:** Efficient bulk read of multiple variables in a single call.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `tag_names` | array | **Yes** | — | List of symbolic tag names to read. |

---

### 65. `plcsim_write_tags`
- **Description:** Bulk write to multiple symbolic variables in a single call.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `tags` | object | **Yes** | — | Key-value dictionary mapping tag names to values. |

---

### 66. `plcsim_run_sequence`
- **Description:** Executes a generic automated online sequence test on any PLC sequence or state machine in S7-PLCSIM Advanced. Writes initial and parameter tags, pulses the start tag, tracks step number progression, samples specified monitored tags at ~80ms, and generates an execution telemetry report.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `sequence_name` | string | No | `"Sequence_01"` | Name/identifier for the sequence run. |
  | `start_command_tag` | string | **Yes** | — | Symbolic tag pulsed TRUE to trigger sequence start. |
  | `reset_command_tag` | string | No | null | Optional tag pulsed TRUE to reset alarms before start. |
  | `step_number_tag` | string | **Yes** | — | Integer tag holding current step/state index. |
  | `step_name_tag` | string | No | null | Optional string tag holding step description. |
  | `done_tag` | string | **Yes** | — | Boolean tag indicating sequence completion. |
  | `fault_tag` | string | No | null | Optional boolean tag indicating fault/alarm. |
  | `idle_step` | integer | No | `0` | Step number indicating idle standby state. |
  | `initial_tags` | object | No | null | Map of initial values written prior to test start. |
  | `parameter_tags` | object | No | null | Map of recipe or setpoint parameters written. |
  | `monitored_tags` | array | No | null | List of tag names to sample in real-time telemetry. |
  | `scale_factor` | number | No | `1.0` | Simulation time speed multiplier. |
  | `timeout_seconds` | integer | No | `60` | Max timeout before declaring test failure. |
- **Sample Response:**
  ```json
  {
    "Success": true,
    "SequenceName": "AssemblyLine_CycleA",
    "ScaleFactor": 2.0,
    "TotalDurationSeconds": 16.20,
    "StepTransitions": [
      { "StepNumber": 10, "StepName": "PREPARATION", "DurationSeconds": 2.10 },
      { "StepNumber": 20, "StepName": "ACTIVE_PROCESS", "DurationSeconds": 7.15 },
      { "StepNumber": 30, "StepName": "TRANSFER", "DurationSeconds": 3.40 },
      { "StepNumber": 40, "StepName": "COMPLETION", "DurationSeconds": 0.50 },
      { "StepNumber": 0, "StepName": "IDLE", "DurationSeconds": 0.0 }
    ],
    "Telemetry": [
      { "ElapsedSeconds": 0.08, "StepNumber": 10, "Values": { "DB_Sequence.actualSpeed": 0.0, "DB_Sequence.pressureBar": 4.5 } }
    ]
  }
  ```

---

### 67. `plcsim_run_mixing_sequence`
- **Description:** Executes an automated multi-parameter batch sequence test. Sets recipe parameters, triggers start, tracks step transitions, records 80ms telemetry, and generates an execution report.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `recipe_id` | string | No | `"RECIPE_01"` | Recipe identifier stored in DB. |
  | `water_liters` | number | No | `100.0` | Target water filling volume in liters. |
  | `additive_type` | integer | No | `1` | Additive flavor index (1, 2, 3). |
  | `additive_amount` | number | No | `20.0` | Target additive volume in liters. |
  | `sugar_kg` | number | No | `10.0` | Target sugar dosing mass in kg. |
  | `mix_time_seconds` | number | No | `3.0` | Agitation time setpoint in seconds. |
  | `mix_speed_rpm` | number | No | `1200.0` | Agitator drive speed in RPM. |
  | `scale_factor` | number | No | `1.0` | Simulation time speed multiplier. |
  | `timeout_seconds` | integer | No | `60` | Max timeout before declaring test failure. |
- **Sample Response:**
  ```json
  {
    "Success": true,
    "Recipe": {
      "RecipeID": "RECIPE_B_STANDARD",
      "WaterLiters": 160.0,
      "AdditiveType": 2,
      "AdditiveAmount": 25.0,
      "SugarKg": 12.0,
      "MixTimeSeconds": 3.5,
      "MixSpeedRpm": 1350.0
    },
    "TotalDurationSeconds": 18.91,
    "PeakTankLevel": 345.24,
    "PeakMixerSpeed": 1350.0,
    "StepTransitions": [
      { "StepNumber": 20, "StepName": "Stage 1: Infeed", "DurationSeconds": 2.76 },
      { "StepNumber": 30, "StepName": "Stage 2: Dosing", "DurationSeconds": 3.21 },
      { "StepNumber": 50, "StepName": "Stage 3: Blending", "DurationSeconds": 4.63 },
      { "StepNumber": 60, "StepName": "Stage 4: Transfer", "DurationSeconds": 5.55 },
      { "StepNumber": 65, "StepName": "Stage 5: Finalize", "DurationSeconds": 2.47 },
      { "StepNumber": 0, "StepName": "Idle / Ready", "DurationSeconds": 0.0 }
    ]
  }
  ```

---

## Category 7: Project, Library & Text Discovery (6 Tools)

All project-scoped tools require the exact full path of a project already open
in the connected TIA Portal instance. Collection tools cap `page_size` at 200
and return `Items`, `Offset`, `PageSize`, `TotalCount`, `HasMore`,
`EnumerationCompleted`, and a completeness note.

### `tia_list_global_libraries`
- **Description:** List accessible global-library metadata from the connected TIA Portal instance.
- **Parameters:** Optional `name_filter` (case-insensitive substring), `offset` (default 0), and `page_size` (default 50, maximum 200).
- **Returns:** Library name, path, library type, open state, and read-only state. The API does not expose a stable library ID or version in this listing.

### `tia_search_project_library_types`
- **Description:** List or search types in one open project's library.
- **Parameters:** Required `project_path`; optional `query` (literal case-insensitive substring in name or namespace), `offset`, and `page_size`.
- **Returns:** Type and version GUIDs, names, namespace, runtime type, folder, consistency status, and exposed version metadata.

### `tia_list_project_faceplates`
- **Description:** List faceplate library types visible in one open project's library.
- **Parameters:** Required `project_path`; optional `name_filter`, `offset`, and `page_size`.
- **Returns:** Faceplate type/version metadata. Interface metadata is not exposed by the verified V21 API surface used here.

### `tia_get_library_type_instances`
- **Description:** Read instances of an exact project-library type in one exact PLC scope.
- **Parameters:** Required `project_path`, `plc_name`, and `library_type_id`; optional exact `version_id` and `source_library`.
- **Returns:** Matching instances with readable names/IDs and parent locations, plus queried version metadata and an explicit completeness note. HMI-wide instance search and inferred consistency states are not claimed.

### `tia_list_project_text_categories`
- **Description:** List project text categories.
- **Parameters:** Required `project_path`; optional `query` (literal case-insensitive substring in category name or identifier), `offset`, and `page_size`.
- **Returns:** Category identifiers and names. The API does not expose category text counts or per-language counts.

### `tia_download_project_texts`
- **Description:** Export project texts with explicit primary and reference cultures.
- **Parameters:** Required `project_path`, `destination_file_path`, `primary_culture`, and `reference_culture`.
- **Behavior:** The destination directory must already exist and the destination file must not exist. The tool will not overwrite a file.
- **Returns:** Resolved project/output paths, normalized cultures, output byte count, and an explicit note that the API provides no exported-entry count or completeness indicator.

## Additional PLC & HMI Discovery Tools (8 Tools)

### `tia_get_block_info`
- **Parameters:** Required exact `block_name`; optional `plc_name` (required if more than one PLC is present) and exact `group_path` (required for duplicate names; empty string selects the root group).
- **Returns:** PLC/block identity, number, type, programming language, group path, and the verified `IsConsistent`/`IsKnowHowProtected` attribute read outcomes. Unsupported metadata is listed explicitly.

### `tia_get_block_attributes`
- **Parameters:** Required exact `block_name`; optional `plc_name` and `group_path`, with the same ambiguity rules as `tia_get_block_info`.
- **Returns:** Read status/value for the verified `IsConsistent` and `IsKnowHowProtected` attributes. An attribute read failure fails the operation; this does not claim arbitrary attribute enumeration or mutability.
- **OBs:** also `SecondaryType`, `CyclicTime` and `PhaseOffset` (value in microseconds with `Unit` `"us"` and `ValueMs`), `PriorityNumber` and the read-only `CyclicApplicationCycleTime`. An attribute the OB does not have, or that reads as empty, has `Status` `"not_supported"`. Change these with `tia_set_block_attribute`.

### `tia_get_tag_table_info`
- **Parameters:** Required exact `table_name`; optional `plc_name` and `group_path`, with the same ambiguity rules.
- **Returns:** PLC/table identity, group path, and contained-tag count.

### `tia_get_type_info`
- **Parameters:** Required exact `type_name`; optional `plc_name` and `group_path`, with the same ambiguity rules.
- **Returns:** PLC/type identity, group path, and verified `IsConsistent` read result. Unsupported metadata is listed explicitly.

### `tia_get_type_attributes`
- **Parameters:** Required exact `type_name`; optional `plc_name` and `group_path`, with the same ambiguity rules.
- **Returns:** Read status/value for the verified `IsConsistent` attribute only. An attribute read failure fails the operation.

### `tia_get_db_member_attributes`
- **Parameters:** Required exact `db_name` and direct `member_name`; optional `plc_name` and `group_path`.
- **Returns:** Complete V21-reported attribute enumeration for the selected direct global-DB member; no nested paths or inferred datatype fields.

### `hmi_list_screen_groups`
- **Parameters:** Optional `target_name`; if omitted, the project must contain exactly one HMI Unified target.
- **Returns:** Nested screen-group hierarchy and direct screen counts. Group names are descriptive, not stable IDs.

### `hmi_list_graphic_lists`
- **Parameters:** Optional `target_name`; if omitted, the project must contain exactly one HMI Unified target.
- **Returns:** Graphic-list names. Entries and file-backed assets are not included.

The case-by-case test plans for these Siemens-facing tools are
[PLC metadata](./plans/MCP_TOOL_TEST_PLAN_PLC.md),
[project/library/text discovery](./plans/MCP_TOOL_TEST_PLAN_PROJECT.md),
[PLC tag and constant reads](./plans/MCP_TOOL_TEST_PLAN_PLC_READS.md),
[DB-member attributes](./plans/MCP_TOOL_TEST_PLAN_PLC_SLICE_READS.md),
[tag-table attributes and block groups](./plans/MCP_TOOL_TEST_PLAN_PLC_GROUP_ATTRIBUTES.md),
[library-type instances](./plans/MCP_TOOL_TEST_PLAN_LIBRARY_TYPE_INSTANCES.md),
and [HMI discovery](./plans/MCP_TOOL_TEST_PLAN_HMI.md). They specify offline contract
checks and later live disposable-project verification; no live verification is
claimed here.

## Category 8: PLC, Project & HMI Operations (18 Tools)

All mutation tools reject conflicts rather than overwriting existing objects.
PLC selection is exact and must be unambiguous; an omitted PLC name is accepted
only when one PLC is present. HMI structure tools require
`on_conflict: "fail"`. Mutations are not claimed to be atomic or rollback-safe.

### PLC tag and user-constant reads

* `tia_get_plc_tag_info`: Required `tag_name` and `table_name`; optional exact
  `plc_name` and `group_path`. Returns supported tag type, logical address,
  comment, and resolved PLC/table/group identity.
* `tia_read_user_constant`: Required `constant_name`; optional exact
  `plc_name`, `table_name`, and `group_path`. A table may be omitted only when
  the name resolves uniquely in the selected PLC/group scope.

Both operations return only V21-supported properties and explicitly list
metadata not returned. They do not modify the project.

### Tag-table attributes and block groups

* `tia_get_tag_table_attributes`: Required `table_name`; optional exact
  `plc_name` and `group_path`. Returns V21-reported attribute names, values
  (or `not_readable` when the access mode does not permit reading), access
  modes, create relevance, and supported types. Stable IDs and revisions are
  not exposed.
* `tia_create_block_group`: Required `group_name`; optional exact `plc_name`
  and parent `parent_group_path`. Creates only under the exact selected block
  group; existing siblings are never overwritten. Description metadata is not
  claimed.

### PLC tag-table and user-constant mutations

* `tia_create_plc_tag_table`: Required `table_name`; optional exact `plc_name`
  and parent `group_path`. Creates only in the selected exact group.
* `tia_create_tagtable_group`: Required `group_name`; optional exact `plc_name`
  and `parent_group_path`.
* `tia_create_user_constant`: Required `table_name`, `constant_name`,
  `data_type`, and `value`; optional exact `plc_name` and `group_path`.
  Supported datatypes are Bool, the listed signed/unsigned integer types, Real,
  and LReal; values are validated before the API call.
* `tia_update_user_constant`: Required `table_name`, `constant_name`, and
  `value`; optional exact `plc_name` and `group_path`. Changes only the value,
  preserving the existing datatype.

These return a structured result with `Success`, `Status`, exact target path,
`Partial`, message, and warnings. Creation/update success is confirmed by
in-memory read-back; saving and reopening remains a separate verification step.

### Project creation

* `tia_create_project`: Required `destination_location` and `project_name`.
  The connected TIA Portal instance creates the project only in an existing
  empty directory. Existing files are never overwritten; verify and reopen the
  returned project path before relying on it.

### WinCC Unified structure operations

* `hmi_create_screen_group`: Required `group_name` and `on_conflict`; optional
  `parent_group_path` (empty means root) and `target_name`.
* `hmi_create_screen_under_group`: Required `parent_group_path`, `screen_name`,
  and `on_conflict`; optional positive `width`, `height`, and `target_name`.
* `hmi_create_tag_table`: Required `table_name` and `on_conflict: "fail"`;
  optional exact `target_name`. Existing tables are never overwritten.
* `hmi_rename_screen`: Required `screen_name`, `group_path`, `new_name`, and
  `on_conflict`; optional `target_name`. Renames in place.
* `hmi_rename_screen_group`: Required `group_path`, `new_name`, and
  `on_conflict`; optional `target_name`. Renames in place.
* `hmi_rename_tag_table`: Required `table_name`, `new_name`, and
  `on_conflict`; optional `target_name`. Renames in place and verifies the tag
  name set remains unchanged.

All six HMI structure operations accept only `on_conflict: "fail"` and return the
affected name/path plus relevant screen dimensions, item count, or tag count.

### WinCC Unified HMI tag delete and rename

#### `hmi_delete_tag`
- **Description:** Deletes one Unified HMI tag by exact, case-sensitive name, also in tag tables inside tag-table groups. Members of structured tags, system tags and whole tag tables cannot be deleted.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `tag_name` | string | **Yes** | — | Exact HMI tag name. |
  | `table_name` | string | No | null | Safety check: the table name or `group/table` path that must contain the tag. |
  | `target_name` | string | No | null | Exact HMI device, device item or software name; required when several HMIs exist. |
  | `on_referenced` | string | No | `"fail"` | `"fail"` keeps a tag that the scan finds referenced or could not fully check; `"ignore"` deletes it and returns the references that now point to a missing tag. |
  | `dry_run` | boolean | No | `false` | Only resolve the tag and report its references. |
- **Reference scan:** before deleting, a best-effort scan looks for references in:
  - alarms: raised-state, acknowledgment control and state, and alarm parameter tags; discrete alarm `TriggerBitAddress`; analog alarm `TriggerAddress`; analog alarm `ConditionValue` when it is a tag-name string
  - other tags: read, write and buffer handshake tags, limit values of type Tag, logging-tag trigger tags
  - the exclusive-operation control and status tags in the Runtime settings
  - screen and item dynamizations: tag, resource-list, tag-parameter and script trigger tags
  - button parts
  - script text, including the global definition area of event scripts

  References to members or elements of the tag (`Tag.X`, `Tag[2]`) count. Scan hits are in `References`; `BlockingReferenceCount` includes literal script hits. TIA cross-references ("used by") are listed separately in `CrossReferences` with `CrossReferenceCount`; they may repeat a scan hit and may include the tag's own logging relations. Categories the scan could not read are listed in `ScanFailures`.
- **Outcome:** with `on_referenced: "fail"`, a tag with scan hits or cross-references is not deleted (status `referenced`), and a tag whose scan was incomplete is not deleted either (status `scan_incomplete`). With `"ignore"`, the tag is deleted and the dangling references are returned; `hmi_compile` will report them. The delete runs in a transaction and is verified by exact-name read-back.
- **Not scanned** (listed in `NotScanned`): global script modules, faceplate and widget interface bindings, other Runtime settings, `ConditionValue` that is not a string, screen items nested in groups if `HmiScreen.ScreenItems` does not list them, plant object tags, scheduler, reports and recipes, other HMI devices, and saved HMI JSON exports.
- **Status values:** `deleted`, `dry_run`, `referenced`, `scan_incomplete`, `not_found`, `ambiguous`, `table_mismatch`, `member_tag`, `api_failure`, `verification_failed`.
- **Not verified against a live TIA Portal:** `HmiTag.Delete()` with commit, what happens to the tag's logging tags, the string format of alarm trigger addresses, and the scan time on a large project.

#### `hmi_rename_tag`
- **Description:** Renames one Unified HMI tag in place with TIA's own rename. The tag is never deleted and recreated; tags in grouped tag tables are included. The PLC tag is not renamed.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `tag_name` | string | **Yes** | — | Exact existing HMI tag name. |
  | `new_name` | string | **Yes** | — | New name. It must not exist yet (compared case-insensitively over all tag tables and system tags; plant object tags are not checked) and must not contain `.`, `[`, `]`, `{`, `}`, `"`, `'`, a backtick, `\` or `:`. |
  | `on_conflict` | string | **Yes** | — | Only `"fail"` is supported. |
  | `table_name` | string | No | null | Safety check: the table name or `group/table` path that must contain the tag. |
  | `target_name` | string | No | null | Exact HMI device, device item or software name. |
  | `script_references` | string | No | `"report"` | `"report"` only lists script occurrences of the old name; `"replace"` also rewrites string literals that are exactly the old name. |
  | `dry_run` | boolean | No | `false` | Only report what the rename would touch. |
- **Structured references:** the same places as in the `hmi_delete_tag` scan are read before the rename and read again after it. Each is reported in `StructuredReferences` with `Value`, `ExpectedAfter`, `ValueAfter` and `Updated`. The tool never rewrites structured references itself. If some still read the old name, the status is `renamed_with_stale_references` and `StaleReferenceCount` is set. TIA cross-references are listed in `CrossReferences` but are not read back.
- **Scripts:** `script_references: "replace"` rewrites only string literals that are exactly the old name (`"Old"` or `'Old'`), and only in scripts whose object has a syntax check. Each changed script is verified by read-back and by a syntax check that must not report more errors than before; nothing is committed on failure. Member literals (`"Old.X"`), templates, comments, identifiers, scripts with a regular-expression or unterminated literal, and scripts without a syntax check are only reported in `ScriptOccurrences` with `Replaced: false`.
- Logging tags named after the old tag keep their names; they are listed in `LoggingTagsNamedAfterOldName`. HMI JSON exports made before the rename still contain the old name, so export again before importing. Run with `dry_run: true` first.
- **Status values:** `renamed`, `renamed_with_stale_references`, `dry_run`, `conflict`, `not_found`, `ambiguous`, `table_mismatch`, `member_tag`, `api_failure`, `verification_failed`. In every outcome `Name` holds the requested new name and `OldName` the source name.
- **Not verified against a live TIA Portal:** whether TIA's rename updates each kind of reference (including the read-only alarm trigger addresses), whether a failed script rewrite restores the name, and whether event scripts expose a syntax check.

### WinCC Unified navigation button

* `hmi_create_navigation_button`: Requires exact `target_name`, source
  `screen_name`, `item_name`, plain-text `text`, `destination_screen`,
  position/dimensions, and `on_conflict: "fail"`; optional foreground and
  background colors. Creates a new button with a Tapped handler calling
  `HMIRuntime.UI.SysFct.ChangeScreen(destination, "~")`, wraps escaped caption
  text in the required HTML paragraph, syntax-checks the script, and verifies
  the button/action by read-back. Existing screen items are never replaced.
  HMI compilation, save/reopen, and runtime navigation remain separate tests.
The test plans for [PLC reads](./plans/MCP_TOOL_TEST_PLAN_PLC_READS.md),
[PLC mutations](./plans/MCP_TOOL_TEST_PLAN_PLC_MUTATIONS.md),
[tag-table attributes and block groups](./plans/MCP_TOOL_TEST_PLAN_PLC_GROUP_ATTRIBUTES.md),
[HMI navigation buttons](./plans/MCP_TOOL_TEST_PLAN_HMI_NAVIGATION.md),
[project creation](./plans/MCP_TOOL_TEST_PLAN_PROJECT_MUTATIONS.md), and
[HMI structure](./plans/MCP_TOOL_TEST_PLAN_HMI_STRUCTURE.md) include case-by-case
offline checks and live disposable-project procedures. No live mutation tests
are claimed.

---

## Category 9: Siemens Online Documentation (4 Tools)

These tools search and read the public Siemens documentation sites
`docs.tia.siemens.cloud` (TIA Portal, STEP 7, WinCC Unified, Openness API,
PLCSIM) and `docs.industrial-operations-x.siemens.cloud` through their Fluid
Topics REST API. They need internet access but no TIA Portal connection, and
return Markdown text. Publication lists and tables of contents are cached in
memory for 24 hours.

**References.** Results name pages with compact references:
`tia:<mapId>` for a publication and `tia:<mapId>/<contentId>` for a topic
(`iox:` for the Industrial Operations X site). Every `url` and `scope`
argument also accepts a reader URL copied from the browser. Set
`reference_format: "url"` to get reader URLs instead.

**Filters.** `product` and `version` are matched case-insensitively against
the catalog; an unknown value fails with the closest valid values. The
Openness API publications use product `TIAPortal`; WinCC Unified runtime
scripting uses `WinCC Unified`. Use `docs_list_publications` to see the rest.

### `docs_search`
- **Parameters:** `query` (required); optional `product`, `version`, `scope`,
  `limit` (1-50, default 10), `host`, `locale` (default `en-US`),
  `reference_format`.
- `scope` limits the search to one publication or one chapter subtree; host
  and locale then come from the scope, and `product`/`version` must be omitted.
  A chapter search reads at most 300 publication hits and says when its count
  may be incomplete.
- **Example:** `{ "query": "OpenScreenInPopup", "product": "WinCC Unified", "limit": 3 }`
  returns numbered hits with title, publication and version, breadcrumb,
  excerpt and reference.

### `docs_read_page`
- **Parameters:** `url` (required); optional `mode` (`full`, `outline`,
  `code`), `section`, `offset`, `max_chars` (1000-100000, default 20000),
  `reference_format`.
- The result starts with a header naming the publication, version, path and
  source reference. `mode: "outline"` lists numbered headings with their sizes
  and code-block counts. `section` takes an outline number or heading text;
  `"Example"` also matches `"Example: ..."` headings. `mode: "code"` returns
  only code blocks under their headings and combines with `section`.
- Long results are cut between blocks (a long code block or table is split at
  a line with its fence or header repeated) and end with
  `[truncated — call again with ..., offset=N]`.
- Links to other topics become compact references; images keep only their
  alt text.

### `docs_get_toc`
- **Parameters:** `url` (required, publication or topic); optional `depth`
  (1-6, default 2), `reference_format`.
- Returns an indented outline, one `- Title — reference` line per node, capped
  at 500 lines.

### `docs_list_publications`
- **Parameters:** all optional: `title_contains`, `product`, `version`,
  `host`, `locale`, `reference_format`.
- Returns up to 50 rows of `- **Title** — Product Version [locale] reference`,
  sorted by title.

Configuration: `docs.locale`, `docs.extraHosts` and `docs.minIntervalSeconds`
in `config.json`, or the `SIEMENS_DOCS_LOCALE`, `SIEMENS_DOCS_HOSTS` and
`SIEMENS_DOCS_MIN_INTERVAL` environment variables. Ported from
[siemens-docs-mcp](https://github.com/Czarnak/siemens-docs-mcp) (MIT).

---

## Category 10: Online PLC Values (6 Tools)

Read-only online access to a real S7-1200 or S7-1500 over S7CommPlus, by
symbolic name, including optimized data blocks. No TIA Portal or Openness
connection is needed.

**Requirements and security.** The PLC must be reachable on TCP port 102.
Projects from TIA Portal V17 or later (secure PG/HMI communication) use TLS
through the managed BouncyCastle library; older projects use the legacy
challenge login implemented by HarpoS7. `security_mode: "auto"` (default)
tries TLS first and falls back to the legacy login. A password is needed only
when the CPU's protection level requires one for reading. The driver client is
created with writes disabled, and there are no write or operating-state tools.

**Connection.** One connection is kept open across calls, like the TIA and
PLCSIM connections, and reconnects automatically after short network drops. It
is closed by `plc_online_disconnect`, by connecting to a different address, or
when the server stops.

### `plc_online_connect`
- **Parameters:** `address` (required, IP or host name); optional `password`,
  `username` (CPUs with user management), `timeout_ms` (1000-60000, default
  8000), `security_mode` (`auto`, `tls`, `legacy`).
- Connecting again to the same address reuses the open session.
- **Response:** `{ message, connected, state, address, securityMode,
  connectedAt, cpu: { plcName, projectName, orderNumber, firmware, tiaVersion },
  browsedVariables, lastError }`.

### `plc_online_get_status`
- No parameters. Same shape as `plc_online_connect`, plus `operatingState`
  (`Run`, `Stop`, `Startup`, ...) read live from the CPU.

### `plc_online_disconnect`
- No parameters; closes the connection.

### `plc_online_list_datablocks`
- **Response:** `{ address, count, datablocks: [{ number, name }] }`, sorted by number.

### `plc_online_browse`
- **Parameters:** optional `filter` (case-insensitive name text), `offset`,
  `limit` (1-1000, default 200), `refresh` (browse the PLC again after a download).
- The first call browses the whole PLC and caches the list for the connection.
- **Response:** `{ total, offset, returned, nextOffset, variables: [{ name, type, array }] }`.
  Names are full symbolic paths such as `DB_Process.Tank.LevelPct` or
  `DB_Process.Sets[0].Filters[0].Id`. An array of a simple type is one row
  whose `array` holds its declared bounds, e.g. `[0..3]`.

### `plc_online_read_tags`
- **Parameters:** `tags` (required, 1-200 symbolic names). An array can be read
  whole by its name or one element as `Name[3]`.
- **Response:** `{ address, readAt, tags: [{ name, type, quality, value }] }`.
  Bool values are `true`/`false`, numbers are numbers, strings and chars are
  strings, arrays are JSON arrays, `Time` is milliseconds. A name that cannot be
  resolved, or a value the PLC refuses, gets an `error` field without failing
  the other tags.

Uses [DotNetProjects.S7CommPlusDriver](https://github.com/dotnetprojects/S7CommPlusDriver)
(LGPL-3.0) with HarpoS7 and BouncyCastle; see `THIRD_PARTY_NOTICES.md`.

---

## Category 11: Hardware Configuration, Networks & Project Backup (19 Tools)

All tools work on the offline project in the attached TIA Portal instance. Nothing is downloaded to a device.
Hardware paths: `device_name` is the station name from `tia_list_devices`; `item_path` is the slash-separated
item path below the device as returned by these tools (e.g. `PLC_1/PROFINET interface_1`, `Rack_0`,
`FPNO/Interface`). Item names may contain `/` (e.g. `DI 14/DQ 10_1`); the full path still resolves. A single
unique item name also works. Mutating tools return `{ Success, DryRun, Message, Data }` and set `isError`
when `Success` is false.

Verified live against TIA Portal V21 with an S7-1200 (CPU 1214C DC/DC/DC V4.6, SM 1223) and an ABB ACS480
with FPNO-21 (GSDML-V2.45-ABB-DRIVES-20250423, DAP `FPNO_DAP_V6`, standard telegram 1).

### `tia_find_module_types`
- **Parameters:** `partial_type_identifier` (required). OrderNumber needs one complete part
  (`OrderNumber:6ES7 214-1AG40-0XB0`), GSD at least two (`GSD:GSDML-V2.45-ABB-DRIVES-20250423.XML/DAP`),
  System one (`System:Rack.ET200SP`). No wildcards.
- **Response data:** `{ TypeIdentifiers: [...sorted], ContainerTypes: { typeId: [...] } }`
  (`ModuleInformationProvider.FindModuleTypes` / `FindContainerTypes`).
- GSD/GSDML files must already be installed in TIA Portal; Openness V21 has no API for installing them.
  Useful GSD prefixes: `/DAP` (head modules for `tia_create_device`), `/M` (modules and telegrams for `tia_plug_module`).

### `tia_create_device`
- **Parameters:** `type_identifier`, `device_item_name` (required); `device_name`; `dry_run` (default `true`).
- Calls `Devices.CreateWithItem(typeIdentifier, deviceItemName, deviceName)`. Existing device names fail without change.
- Examples: `OrderNumber:6ES7 214-1AG40-0XB0/V4.6` (CPU 1214C), `GSD:GSDML-V2.45-ABB-DRIVES-20250423.XML/DAP/FPNO_DAP_V6` (ABB FPNO-21).
- **Response data:** device name, type identifier and the created item paths with position numbers.

### `tia_list_plug_locations`
- **Parameters:** `device_name` (required), `item_path`.
- Without `item_path`, lists every hardware object of the device that has free positions (`HardwareObject.GetPlugLocations`).
- **Response data:** `[{ ItemPath, Name, TypeIdentifier, FreeLocations: [{ PositionNumber, Label }], Plugged: [{ Name, PositionNumber, TypeIdentifier }] }]`.

### `tia_plug_module`
- **Parameters:** `device_name`, `type_identifier`, `name`, `position_number` (required); `parent_item_path`; `dry_run` (default `true`).
- Runs `CanPlugNew` and, with `dry_run=false`, `PlugNew`. S7-1200 signal modules go into `Rack_0` (positions 2-9);
  a GSDML drive's telegram goes into its `Rack` (e.g. `GSD:...XML/M/ID_MODULE_STD_TGM1` at position 1).
- **Response data:** new item path, position and the IO addresses of the item and its submodules.

### `tia_delete_hardware`
- **Parameters:** `device_name` (required), `item_path`, `confirm` (default `false`).
- Without `confirm=true` returns a preview with all sub-items. Built-in items (CPU, interfaces, ports) cannot be deleted.

### `tia_rename_hardware`
- **Parameters:** `device_name`, `new_name` (required); `item_path` (omit to rename the station).

### `tia_get_io_addresses`
- **Parameters:** `device_name` (omit for all devices).
- **Response data:** `[{ Device, ItemPath, TypeIdentifier, Addresses: [{ IoType, StartAddress, Length }] }]`.
  `StartAddress` is the byte address (`%IB`/`%QB`); `Length` is in bits (DI 14 = 16, standard telegram 1 = 32,
  i.e. `%IW68..%IW70` and `%QW64..%QW66`). Diagnosis addresses have `StartAddress` -1.

### `tia_list_network`
- **Parameters:** `device_name` (limits the interface list).
- **Response data:** `{ Subnets: [{ Name, TypeIdentifier, NetType, NodeCount, IoSystems: [{ Name, Number, ConnectedIoDevices }] }],
  Interfaces: [{ Device, InterfacePath, InterfaceOperatingMode, Nodes: [{ Name, Subnet, Address, SubnetMask, UseRouter,
  RouterAddress, IpProtocolSelection, PnDeviceNameAutoGeneration, PnDeviceName, PnDeviceNameConverted }],
  IoControllers: [{ IoSystem }], IoConnectors: [{ ConnectedToIoSystem, DeviceNumber }] }] }`.

### `tia_create_subnet`
- **Parameters:** `subnet_name` (required); `type_identifier` (default `System:Subnet.Ethernet`).

### `tia_connect_to_subnet`
- **Parameters:** `device_name`, `subnet_name` (required); `interface_path` (needed only when a device has several Ethernet interfaces).
- Calls `Node.ConnectToSubnet` on the interface's first node.

### `tia_create_io_system`
- **Parameters:** `device_name` (required, the controller); `interface_path`; `io_system_name`.
- Calls `IoController.CreateIoSystem`; connect the interface to a subnet first.

### `tia_connect_to_io_system`
- **Parameters:** `device_name`, `io_system_name` (required); `interface_path`.
- Calls `IoConnector.ConnectToIoSystem`; the IO device's interface must be on the IO system's subnet.

### `tia_set_network_node`
- **Parameters:** `device_name` (required); `interface_path`, `ip_address`, `subnet_mask`, `router_address`,
  `pn_device_name`, `pn_name_auto_generation`.
- Sets the node attributes `Address`, `SubnetMask`, `UseRouter`/`RouterAddress`, `PnDeviceNameAutoGeneration`, `PnDeviceName`.
  Values that already match are skipped. On an IO device `SubnetMask` is read-only (it follows the IO controller).

### `tia_export_aml`
- **Parameters:** `output_path` (required, `.aml`); `device_name`; `overwrite` (default `false`).
- `CaxProvider.Export` with a `TransferResult` (V19+ overload). Siemens Startdrive drives and most HMI devices are
  not part of CAx files; GSDML devices are.
- **Response data:** `{ Path, State, ErrorCount, WarningCount, SizeBytes, Messages }`.

### `tia_import_aml`
- **Parameters:** `aml_path` (required); `conflict_option` (`move_to_parking_lot` default, `retain_tia_device`,
  `overwrite_tia_device`); `dry_run` (default `true`).
- The preview lists the AML devices and those whose names already exist. The import itself
  (`CaxProvider.Import`) cannot be undone through Openness; archive first.
- When the controller is kept (`retain_tia_device`) and an IO device is imported, its IP and PROFINET name are
  restored but the IO system assignment is not; run `tia_connect_to_io_system` afterwards.

### `tia_export_project_files`
- **Parameters:** `output_directory` (required); `plc_name`; `include_hardware`, `include_scl`, `remove_stale` (all default `true`).
- Layout:
  ```
  tia-export.json                      sorted file list and errors, no timestamps
  hardware/project.aml                 CAx export of the project
  plc/<PLC>/blocks/<group>/<name>.xml  SimaticML of every block (+ <name>.scl for SCL blocks)
  plc/<PLC>/types/<group>/<name>.xml   PLC data types
  plc/<PLC>/tags/<group>/<name>.xml    PLC tag tables
  plc/<PLC>/watch|force/<group>/<name>.xml
  ```
- Deterministic: the SimaticML `DocumentInfo/Created` stamp and the AML `LastWritingDateTime` are removed, and the
  random AML element IDs are replaced by name-based GUIDs (references rewritten), so re-exporting an unchanged
  project changes no file. Normalised files still import (verified with `tia_import_block` and `tia_import_aml`).
- Files are written only when their content changes; files of deleted objects listed in the previous
  `tia-export.json` are removed (`remove_stale`). Inconsistent or know-how protected blocks are reported in
  `Errors` and keep their previous file; compile first.

### `tia_archive_project`
- **Parameters:** `target_directory` (required); `file_name` (default `<project>_<yyyyMMdd_HHmm>.zap21`);
  `mode` (`compressed` default, `discard_restorable_data_and_compressed`); `save_first` (default `false`).
- `Project.Archive` needs a saved project; existing files are never overwritten.

### `tia_retrieve_project`
- **Parameters:** `archive_path`, `target_directory` (required, empty or missing); `close_current` (default `false`).
- `Projects.Retrieve` opens the archive in the attached instance. An open project is closed only with
  `close_current=true` and only when it has no unsaved changes.

### `tia_export_cross_references`
- **Parameters:** `output_path` (required, `.jsonl`); `plc_name`; `object_names`; `filter`
  (`objects_with_references` default, `all_objects`, `objects_without_references`, `unused_objects`);
  `include_tags` (default `true`).
- Uses `CrossReferenceService.GetCrossReferences` on every block and PLC tag (tag tables have no such service).
- One JSON line per reference location: `{ root, source, sourceType, sourceAddress, sourcePath, device, reference,
  referenceTypeName, referencePath, location, referenceType, access, referencedAs, address, typeName }`.

---

## Category 12: Technology Objects (4 Tools)

These tools work on the PLC's *Technology objects* folder (`TechnologicalObjectGroup`). Technology
objects do not appear in `tia_list_blocks`. A PID instance DB that TIA generated from an SCL call is a plain
instance DB, not a technology object. Blocks and technology objects share one name space:
`tia_import_scl_block` refuses a block name that equals a technology object name. None of these tools has
been run against a live TIA Portal.

### `tia_list_technology_objects`
- **Parameters:** `plc_name` (optional, exact, case-sensitive).
- Lists the folder including subfolders. **Response data:** `[{ Name, Type, Version, Number, GroupPath, IsConsistent, Error }]`.
  `Type` is the Openness system-library name, e.g. `PID_Temp`.

### `tia_get_technology_object_parameters`
- **Parameters:** `name` (required, exact); `filter` (optional case-sensitive name prefix, e.g. `Config.`); `plc_name`.
- Returns each parameter's `Name` (the *Name in Openness*), `Value` (invariant text), `ClrType` and, when it
  could not be read, `Error`; a read error affects only that entry.
- Parameter names depend on the type and version. Example: PID_Temp V1.1 (S7-1200) has no `Config.ActivateHeating`;
  that parameter exists from V2.0. Read the names here before writing them.

### `tia_create_technology_object`
- **Parameters:** `type`, `name`, `version` (required); `plc_name`.
- `type` is the Openness system-library name: `PID_Temp`, `PID_Compact` or `PID_3Step`. `TO_PID_Temp`, `TO_PID_Compact`
  and `TO_PID_3Step` are accepted and mapped. Other names go to TIA unchanged, and TIA rejects unsupported ones;
  motion and counting objects are created without any hardware interconnection.
- `version` is `major.minor` (`1.1` or `V1.1`); there is no automatic latest version. S7-1200 with FW 4.2 or later supports
  PID_Temp 1.1, PID_Compact 2.3 or later and PID_3Step 2.3.
- The object is created in the root *Technology objects* folder (`TechnologicalObjects.Create`); groups are not supported.
- Fails with status `collision`, and changes nothing, when a technology object, a program block or a system block
  (for example the PID_Temp FB) already has the name (case-insensitive). If the existing block is a plain instance DB of
  the PID block, rename or delete it first, or choose another name.
- The type and the `major.minor` version are checked by read-back.
- Creating the object does not call it. Call it from a cyclic interrupt OB with the object as instance, e.g.
  `"PID_Temp_1"(Setpoint := ..., Input := ...)`; hardware I/O is wired through those call parameters.

### `tia_set_technology_object_parameters`
- **Parameters:** `name` (required); `parameters` (required, non-empty array of `{ name, value }`; each name at most once,
  compared case-insensitively); `plc_name`.
- `value` is a JSON string in invariant culture (e.g. `"true"`, `"5"`, `"0.5"`). It is converted to the CLR type of the
  parameter's current value: Boolean `true`/`false`, integers with a range check, finite float/double, char or string.
  Parameter names are exact and case-sensitive.
- All names and values are validated first; nothing is written if any check fails. The values are then written in
  order, and each is read back.
- **Per-parameter status:** `updated`; `api_failure` (TIA rejected the write; it may still have changed state);
  `verification_failed` (written, but the read-back failed or differs, e.g. TIA recalculated the value); `not_written`
  (not attempted).
- Not transactional: on a failure in the write phase `Partial` is `true`, later entries are `not_written`, earlier writes
  are not rolled back, and a warning says whether earlier parameters were written. Each entry lists `OldValue`,
  `RequestedValue`, `NewValue`, `ClrType` and `Status`.
- **Sample Invocation:**
  ```json
  {
    "name": "PID_Temp_1",
    "parameters": [
      { "name": "Config.InputPerOn", "value": "true" },
      { "name": "Config.ActivateCooling", "value": "false" }
    ]
  }
  ```
  Check the parameter names with `tia_get_technology_object_parameters` first; the names in this example are not
  verified for PID_Temp V1.1.
