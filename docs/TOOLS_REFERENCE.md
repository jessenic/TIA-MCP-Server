# Siemens TIA Portal V21 Openness MCP Server
## Complete Tools Reference Dictionary (114 Tools)

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
| **3** | [PLC Software, Blocks & Compilation](#category-3-plc-software-blocks--compilation-16-tools) | 16 | `tia_list_plcs`, `tia_list_blocks`, `tia_get_block_code`, `tia_get_block_info`, `tia_get_block_attributes`, `tia_import_block`, `tia_import_scl_block`, `tia_delete_block`, `tia_list_plc_types`, `tia_get_plc_type`, `tia_get_type_info`, `tia_get_type_attributes`, `tia_get_tag_table_info`, `tia_import_plc_type`, `tia_compile`, `tia_get_db_member_attributes` |
| **4** | [WinCC Unified HMI Engineering](#category-4-wincc-unified-hmi-screen--widget-engineering-49-tools) | 49 | `hmi_list_targets`, `hmi_list_screens`, `hmi_list_screen_groups`, `hmi_list_graphic_lists`, `hmi_create_screen`, `hmi_delete_screen`, `hmi_get_screen_items`, `hmi_get_screen_item`, `hmi_update_screen_item`, `hmi_read_event_scripts`, `hmi_set_event_script`, `hmi_delete_screen_items`, `hmi_add_label`, `hmi_add_button`, `hmi_add_toggle_switch`, `hmi_add_shape`, `hmi_add_io_field`, `hmi_set_tag_dynamization`, `hmi_delete_dynamization`, `hmi_update_dynamization_entry`, `hmi_set_expression_dynamization`, `hmi_set_expression_dynamization_entries`, `hmi_get_js_type_definitions`, `hmi_update_button_scripts`, `hmi_list_tag_tables`, `hmi_list_tags`, `hmi_create_tag`, `hmi_create_discrete_alarm`, `hmi_update_tag_acquisition_cycles`, `hmi_set_tag_range`, `hmi_create_data_log`, `hmi_assign_logging_tags`, `hmi_list_connections`, `hmi_create_connection`, `hmi_delete_connection`, `hmi_list_text_lists`, `hmi_create_text_list`, `hmi_delete_text_list`, `hmi_export_text_lists`, `hmi_compile`, `tia_list_tag_tables`, `tia_get_tags`, `hmi_export_json`, `hmi_read_export_file`, `hmi_begin_import`, `hmi_upload_import_file`, `hmi_import_json`, `hmi_patch_json`, `hmi_render_json` |
| **5** | [Siemens TestSuite Automation](#category-5-siemens-testsuite-automation-2-tools) | 2 | `tia_testsuite_list_tests`, `tia_testsuite_load_test` |
| **6** | [S7-PLCSIM Advanced Live Simulation & Testing](#category-6-s7-plcsim-advanced-live-simulation--testing-12-tools) | 12 | `plcsim_list_instances`, `plcsim_connect`, `plcsim_disconnect`, `plcsim_get_status`, `plcsim_set_operating_state`, `plcsim_set_scale_factor`, `plcsim_read_tag`, `plcsim_write_tag`, `plcsim_read_tags`, `plcsim_write_tags`, `plcsim_run_sequence`, `plcsim_run_mixing_sequence` |
| **7** | [Project, Library & Text Discovery](#category-7-project-library--text-discovery-6-tools) | 6 | `tia_list_global_libraries`, `tia_search_project_library_types`, `tia_list_project_faceplates`, `tia_get_library_type_instances`, `tia_list_project_text_categories`, `tia_download_project_texts` |
| **8** | [PLC, Project & HMI Operations](#category-8-plc-project--hmi-operations-16-tools) | 16 | `tia_get_plc_tag_info`, `tia_read_user_constant`, `tia_get_tag_table_attributes`, `tia_create_plc_tag_table`, `tia_create_tagtable_group`, `tia_create_block_group`, `tia_create_user_constant`, `tia_update_user_constant`, `tia_create_project`, `hmi_create_screen_group`, `hmi_create_screen_under_group`, `hmi_create_tag_table`, `hmi_create_navigation_button`, `hmi_rename_screen`, `hmi_rename_screen_group`, `hmi_rename_tag_table` |
| **9** | [Siemens Online Documentation](#category-9-siemens-online-documentation-4-tools) | 4 | `docs_search`, `docs_read_page`, `docs_get_toc`, `docs_list_publications` |

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
- **Description:** Attaches the server to a running TIA Portal process. If `process_id` is omitted, attaches to the first running instance found.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `process_id` | integer | No | null | Target TIA Portal process ID (PID). |
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

## Category 3: PLC Software, Blocks & Compilation (16 Tools)

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
- **Description:** Exports block definition as SimaticML XML and extracts readable SCL logic.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `block_name` | string | **Yes** | — | Block name (e.g. `"FB_MixingLine"`). |
  | `plc_name` | string | No | null | Target PLC name. |
- **Response Example:**
  ```json
  {
    "Name": "FB_MixingLine",
    "Number": 10,
    "BlockType": "FB",
    "ProgrammingLanguage": "SCL",
    "XmlContent": "<?xml version=\"1.0\"...<Document>...</Document>",
    "ExtractedScl": "REGION Motor Control\n  IF #Cmd_Start THEN\n    #Motor_Run := TRUE;\n  END_IF;\nEND_REGION"
  }
  ```

---

### 14. `tia_import_block`
- **Description:** Imports or updates a block from SimaticML XML.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `xml_content` | string | **Yes** | — | SimaticML XML string. |
  | `overwrite` | boolean | No | `true` | Overwrite existing block. |
  | `plc_name` | string | No | null | Target PLC name. |
  | `folder` | string | No | null | Subfolder inside Program Blocks. |

---

### 14a. `tia_import_scl_block`
- **Description:** Imports a complete SCL source for one PLC block. When a block with the same name exists, it is backed up, deleted, and replaced; a failed import attempts to restore the backup. This is whole-block replacement, not an in-place edit. Compilation defaults to enabled; compilation errors are returned and do not roll back the import.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `scl_code` | string | **Yes** | — | Complete source for exactly one `FUNCTION`, `FUNCTION_BLOCK`, `ORGANIZATION_BLOCK`, or `DATA_BLOCK`. |
  | `plc_name` | string | No | null | Target PLC name. |
  | `folder` | string | No | null | Existing PLC block group path; when omitted, preserves the existing block's group or uses the root group for a new block. |
  | `compile_after_import` | boolean | No | `true` | Compile the target PLC after generation. Compilation errors do not trigger rollback. |
  | `open_after_import` | boolean | No | `false` | Open the imported block in the TIA Portal editor. |

---

### 15. `tia_delete_block`
- **Description:** Deletes a program block from the PLC.
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
- **Description:** Imports or replaces a User Data Type (UDT) from SimaticML XML.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `xml_content` | string | **Yes** | — | SimaticML XML definition. |
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

---

### `tia_get_db_member_attributes`
- **Description:** Read the V21-reported attributes of one exact direct member of a global DB.
- **Parameters:** Required `db_name` and `member_name`; optional exact `plc_name` and `group_path`.
- **Returns:** Every attribute reported for the selected direct member, with values only when its V21 access mode permits reading. Nested paths, dedicated datatype, stable ID, and revision fields are not claimed.

## Category 4: WinCC Unified HMI Screen & Widget Engineering (49 Tools)

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
  | `text` | string | **Yes** | — | Display text string. |
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
  | `text` | string | **Yes** | — | Button text caption. |
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
  | `screen_name`, `name`, `off_text`, `on_text` | string | **Yes** | — | Target screen, item name, and captions for normal and alternate states. |
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
  | `text` | string | **Yes** | — | Event text applied to active text languages. |
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
- **Description:** Compiles the WinCC Unified HMI device and runtime software using Openness `ICompilable`.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `target_device` | string | No | null | Target device name (e.g. `"PC-System_1"`). |
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
    ]
  }
  ```

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

---

### 53. `hmi_patch_json`
- **Description:** Validate or apply guarded `set`, `bind`, and `delete` operations from a `tia-mcp-hmi-patch/1` file, then verify and compile without saving.
- **Parameters:**
  | Parameter | Type | Required | Default | Description |
  | :--- | :---: | :---: | :---: | :--- |
  | `file_path` | string | **Yes** | — | Path to the JSON patch file. |
  | `dry_run` | boolean | No | `false` | Validate preconditions and report operations without applying them. |
  | `target_name` | string | No | null | Target HMI software/device selector. |

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
- **Allowlisted properties:** `Visible`, `Left`, `Top`, `Width`, `Height`, `CenterX`, `CenterY`, `Radius`, `BackColor`, `ForeColor`, `BorderColor`, and `BorderWidth`. Unknown or read-only properties and invalid value types fail explicitly.
- **Behavior:** Each requested property is validated before mutation; the update is applied transactionally and every supplied property is verified by readback. No project save is performed.

#### `hmi_read_event_scripts`
- **Description:** Reads event scripts on a screen, or on one item when `item_name` is supplied, and returns supported event enum names for that target.
- **Parameters:** `screen_name` is required; `item_name` and `target_name` are optional.
- **Behavior:** Event names come from the V21 Openness event composition for the selected target type. Missing targets and unsupported event-handler APIs fail explicitly.

#### `hmi_set_event_script`
- **Description:** Creates or updates one event handler in place on a screen or item.
- **Parameters:** `screen_name`, `event_type`, and `script` are required; `item_name` and `target_name` are optional.
- **Behavior:** Only event values supported by the selected V21 handler enum are accepted. Other event handlers and item identity/layout are preserved. The new source is read back and checked; syntax diagnostics are returned when exposed by the API. No project save is performed.

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

### 56. `plcsim_list_instances`
- **Description:** Lists all registered virtual S7-1500 controller instances managed by S7-PLCSIM Advanced runtime with ID, name, operating state, CPU type, and IP address.
- **Parameters:** None (`{}`)
- **Sample Response:**
  ```json
  [
    {
      "ID": 0,
      "Name": "PLC_1",
      "OperatingState": "Run",
      "CPUType": "CPU1511TF",
      "IP": "192.168.0.101"
    }
  ]
  ```

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

## Category 8: PLC, Project & HMI Operations (15 Tools)

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
