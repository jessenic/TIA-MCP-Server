# Siemens TIA Portal V21 Openness MCP Server
## Complete Skills & Tools Reference Manual

**Version:** 2.14.0
**Target Platform:** Siemens TIA Portal V21 (PublicAPI .NET Framework 4.8 x64)  
**Supported Transports:** HTTP JSON-RPC 2.0, Server-Sent Events (SSE), stdio MCP  
**Default Endpoints:** `http://localhost:5001/mcp`, `http://localhost:5001/sse`, `http://localhost:5001/status`

---

## 1. Architecture & Connection Persistence

The TIA Openness MCP Server functions as a high-performance, persistent connector between AI agents and Siemens TIA Portal. 

The server also provides read-only MCP documentation resources:

| URI | Contents |
| --- | --- |
| `tia://docs/tools-reference` | Detailed tool arguments and return shapes. |
| `tia://docs/skills-and-tools` | This skills and tools overview. |
| `tia://docs/agent-prompting-and-system-functions` | Prompting guidance and WinCC Unified system functions. |

Read these through the MCP resource interface. Live tool schemas remain authoritative for tool availability and arguments.

```
 +--------------------------------------------------------------------------------+
 |                           AI Agent / Client Applications                       |
 +--------------------+----------------------------+-------------------------------+
                      |                            |
       HTTP JSON-RPC  |  Server-Sent Events (SSE)  |  Standard I/O (MCP stdio)
       POST /mcp      |  GET /sse                  |  stdin / stdout
                      v                            v
 +--------------------------------------------------------------------------------+
 |                     TiaOpennessMcp Daemon Process (Port 5001)                  |
 |                                                                                |
 |  [ServerConfig]              [HttpMcpServer]              [McpServer]          |
 |  - config.json               - Multi-client SSE Sessions  - stdio framing      |
 |  - Host:Port / Mode          - POST /rpc, POST /mcp       - stderr logging     |
 |  - Explicit PID attach       - GET /status health check                        |
 |                                                                                |
 |  [TiaManager Singleton]                                                        |
 |  - Holds live Openness COM object references (TiaPortal, Project)              |
 |  - Guarantees persistent connection across turns ("connexion must stay")       |
 |  - Dynamic assembly resolution to Siemens PublicAPI                            |
 +-------------------------------------+------------------------------------------+
                                       | Siemens Openness COM / .NET API
                                       v
 +--------------------------------------------------------------------------------+
 |                    Siemens TIA Portal V21 Process (Active Project)             |
 |  - Hardware Configuration & Topology Engine                                    |
 |  - PLC Software Container (OB, FB, FC, DB, UDT, TagTables, TestSuite)         |
 |  - WinCC Unified PC Runtime (Screens, ScreenItems, Dynamizations, JS Engine)   |
 |  - ICompilable Native Diagnostic Compiler                                      |
 +--------------------------------------------------------------------------------+
```

### Key Architectural Tenets:
1. **Always-On Connection (`connexion must stay`)**: TIA Portal and project instances are heavy COM entities. The server maintains the `TiaPortal` and `Project` instances alive inside the `TiaManager` singleton so every agent turn executes instantly without re-attaching or reopening.
2. **Dual Transport**: Supports standard stdio MCP transport for direct terminal/editor agents, and concurrently exposes an HTTP/SSE server on a configurable TCP port for browser tools, external sidecars, or multi-agent orchestrators.
3. **Clean-Room Assembly Loading**: Siemens assemblies (`Siemens.Engineering.*`) are resolved dynamically at runtime from the local installation directory, avoiding unauthorized binary redistribution.

---

## 2. Server Configuration (`config.json`)

The server reads configuration from `config.json` located in its working or executable directory:

```json
{
  "server": {
    "host": "http://localhost:5001/",
    "port": 5001,
    "mode": "HttpAndStdio",
    "enableCors": true
  },
  "tia": {
    "keepAliveConnection": true,
    "apiPath": "C:\\Program Files\\Siemens\\Automation\\Portal V21\\PublicAPI\\V21\\net48"
  }
}
```

### Configuration Parameters:
- **`server.port`** *(int)*: TCP port for the HTTP/SSE listener (default: `5001`).
- **`server.mode`** *(string)*: Transport mode:
  - `"HttpAndStdio"`: Runs HTTP/SSE server in background while handling stdio messages.
  - `"Http"`: Standalone daemon serving HTTP/SSE only.
  - `"Stdio"`: Classic MCP stdio server only.
- **`server.enableCors`** *(bool)*: Emits CORS headers (`*`) on all HTTP responses for web/agent browser access.
- **`tia.autoConnect`**, **`tia.preferredProcessId`**: obsolete and ignored. The server never attaches on its own (a busy TIA Portal, such as one running a firmware update, would otherwise stall the MCP handshake); clients call `tia_connect` with a `process_id`.
- **`tia.keepAliveConnection`** *(bool)*: Retains project instance across all client sessions.
- **`docs.locale`** *(string)*: Default locale for the `docs_*` tools (default: `"en-US"`).
- **`docs.extraHosts`** *(string[])*: Extra Fluid Topics documentation hosts, added after `docs.tia.siemens.cloud` and `docs.industrial-operations-x.siemens.cloud`.
- **`docs.minIntervalSeconds`** *(number)*: Minimum seconds between requests to one documentation host (default: `0.3`).
- The environment variables `SIEMENS_DOCS_HOSTS` (comma-separated extra hosts), `SIEMENS_DOCS_LOCALE` and `SIEMENS_DOCS_MIN_INTERVAL` override these settings.

---

## 3. The AI Agent Skill (`tia-portal-openness`)

The `tia-portal-openness` skill instructs AI agents on standard operating procedures for engineering TIA Portal projects.

### 3.1 Workflow Sequence
```
  [1. Check Status]
         │
         ▼
  `tia_get_status` ──► Connected? ──No──► `tia_list_processes` ──► `tia_connect`
         │ Yes
         ├────────────────────────────────────────┬─────────────────────────────────────┐
         ▼                                        ▼                                     ▼
[2. Hardware & PLCs]                     [3. WinCC Unified HMI]               [4. TestSuite Tests]
  - `tia_list_devices`                     - `hmi_list_targets`                 - `tia_testsuite_list_tests`
  - `tia_list_plcs`                        - `hmi_list_screens`                 - `tia_testsuite_load_test`
  - `tia_list_blocks`                      - `hmi_get_screen_items`
  -                                        - `hmi_get_screen_item` / `hmi_update_screen_item`
  - `tia_get_block_code`                   - `hmi_add_label` / `button`
  - `tia_import_block` / `tia_import_scl_block` - `hmi_set_tag_dynamization`
         │                                 - `hmi_read_event_scripts` / `hmi_set_event_script`
         │                                 - `hmi_set_expression_dynamization`
         │                                 - `hmi_set_expression_dynamization_entries`
         │                                 - `hmi_get_js_type_definitions`
         │                                 - `hmi_update_button_scripts`
         ▼                                        │
  `tia_compile` (PLC)                             ▼
                                           `hmi_compile` (HMI)
```

### 3.2 WinCC Unified JavaScript Rules & Best Practices
1. **Bit Setting & Toggling**:
   - Set Bit: `Tags.SysFct.SetBitInTag("TagName", bitIndex);`
   - Invert (Toggle) Bit: `Tags.SysFct.InvertBitInTag("TagName", bitIndex);`
   - Reset Bit: `Tags.SysFct.ResetBitInTag("TagName", bitIndex);`
2. **Screen Navigation**:
   - `HMIRuntime.UI.SysFct.ChangeScreen("TargetScreen", "~");`
   - *Crucial:* Second argument must be `"~"` to signify the current screen window.
3. **HTML Label & Button Wrapping**:
   - WinCC Unified labels and buttons render text via mini-HTML. Plain text strings must be wrapped in `<body><p>Text</p></body>`. The MCP server's `hmi_add_label` and `hmi_add_button` handle this automatically.
4. **Single-Object Range Dynamization**:
   - Instead of creating two overlapping shapes and toggling their visibility, configure a single shape with a `ConditionType.Range` dynamization on `BackColor`.
   - Range 0..0 -> Inactive color (e.g. gray `#94A3B8`).
   - Range 1..1 -> Active color (e.g. green `#22C55E` or red `#EF4444`).
5. **In-Place Script Updates**:
   - When modifying button actions, use `hmi_update_button_scripts` rather than deleting and recreating items. This maintains coordinate positioning, z-ordering, and screen element identifiers intact.

---

## 4. Complete Tool Reference (153 Tools)

---

### Category 1: Connection & Session Management (8 Tools)

#### 1. `tia_get_status`
- **Description:** Retrieve current connection status, attached process ID, open project name, file path, and instance ownership.
- **Input Parameters:** None (`{}`)
- **Returns:**
  ```json
  {
    "IsConnected": true,
    "AttachedProcessId": 16028,
    "ProjectName": "TestMCP-DDup",
    "ProjectPath": "D:\\TIA_Project\\_AEs\\TestMCP-DDup\\TestMCP-DDup.ap21",
    "OwnsPortalInstance": false
  }
  ```

#### 2. `tia_list_processes`
- **Description:** Enumerate all running Siemens TIA Portal instances on the machine with PID, open project path, and mode.
- **Input Parameters:** None (`{}`)
- **Returns:** Array of process objects (`Id`, `ProjectPath`, `ProjectName`, `Mode`).

#### 3. `tia_connect`
- **Description:** Attach the MCP server to the running TIA Portal instance with this PID (from `tia_list_processes`). The server never attaches on its own.
- **Input Parameters:**
  - `process_id` *(integer, required)*: PID of the TIA Portal instance.
- **Sample Invocation:**
  ```json
  { "process_id": 16028 }
  ```

#### 4. `tia_disconnect`
- **Description:** Detach from the active TIA Portal instance and release COM references.
- **Input Parameters:** None (`{}`)

#### 5. `tia_get_project_info`
- **Description:** Retrieve metadata of the currently open project (name, path, author, comments, timestamps, modified flag).
- **Input Parameters:** None (`{}`)

#### 6. `tia_open_project`
- **Description:** Launch a new TIA Portal instance and open a project file (`.ap21`, `.ap20`) from disk.
- **Input Parameters:**
  - `project_path` *(string, required)*: Full path to `.ap21` file.
  - `with_gui` *(boolean, optional, default: true)*: Launch with graphical UI or headless.

#### 7. `tia_save_project`
- **Description:** Save all pending changes to the open project file.
- **Input Parameters:** None (`{}`)

#### 8. `tia_close_project`
- **Description:** Close the currently open project without closing TIA Portal.
- **Input Parameters:** None (`{}`)

---

### Category 2: Hardware & Device Topology (2 Tools)

#### 9. `tia_list_devices`
- **Description:** Enumerate all devices (PLCs, HMIs, Drives, distributed I/O racks) in the active project.
- **Input Parameters:** None (`{}`)
- **Returns:** Array of devices with `Name`, `TypeIdentifier`, and `DeviceGroup`.

#### 10. `tia_get_device_tree`
- **Description:** Inspect hardware topology, racks, slots, sub-modules, and communication interfaces for a specified device.
- **Input Parameters:**
  - `device_name` *(string, required)*: Name of the device (e.g. `"PLC_1"`).

---

### Category 3: PLC Software, Blocks & Compilation (21 Tools)

#### 11. `tia_list_plcs`
- **Description:** List all CPU devices and summarize their software metrics (block counts, UDT counts, tag table counts).
- **Input Parameters:** None (`{}`)

#### 12. `tia_list_blocks`
- **Description:** Enumerate all program blocks (OB, FB, FC, DB) with language (SCL, LAD, FBD, STL) and compilation consistency.
- **Input Parameters:**
  - `plc_name` *(string, optional)*: Target PLC name (defaults to first PLC).
  - `block_type` *(string, optional)*: Filter by type (`"OB"`, `"FB"`, `"FC"`, `"DB"`).
  - `folder` *(string, optional)*: Subfolder path inside Program Blocks.

#### 13. `tia_get_block_code`
- **Description:** Read the code of a block. By default SCL, STL and DB blocks return the source text TIA Portal generates (`Source`); LAD, FBD and GRAPH blocks, and blocks whose source cannot be generated, return SimaticML (`XmlContent`) with the reason in `Warnings`. When no block has the name, a PLC data type (UDT) of that name is used. `ExtractedScl` is deprecated (a copy of `Source` for SCL blocks).
- **Input Parameters:**
  - `block_name` *(string, required)*: Name of the block or UDT (e.g. `"FB_MixingLine"`).
  - `plc_name` *(string, optional)*: Target PLC name.
  - `format` *(string, optional, default: "auto")*: `"auto"`, `"source"` (no fallback) or `"xml"` (always SimaticML).
  - `include_xml` *(boolean, optional, default: false)*: Also return SimaticML when source text is returned.
  - `with_dependencies` *(boolean, optional, default: false)*: Include the blocks and types the block depends on. Such output cannot be passed to `tia_import_scl_block`.

#### 14. `tia_import_block`
- **Description:** Import or update a block from SimaticML XML. OB `CyclicTime` and `PhaseOffset` are microseconds in SimaticML; suspicious OB settings are listed under `Warnings:` in the text result.
- **Input Parameters:**
  - `xml_content` *(string)*: Complete SimaticML XML string. Give this or `file_path`.
  - `file_path` *(string)*: Fully qualified server-local `.xml` path (at most 50 MiB). Give this or `xml_content`.
  - `overwrite` *(boolean, optional, default: true)*: Overwrite existing block.
  - `plc_name` *(string, optional)*: Target PLC.
  - `folder` *(string, optional)*: Subfolder inside Program Blocks.

#### 14a. `tia_import_scl_block`
- **Description:** Import a complete SCL block source. Existing blocks with the same name are backed up, deleted, and replaced; failed imports attempt to restore the backup. This is whole-block replacement, not an in-place edit. Compilation defaults to enabled, and compiler errors do not roll back the import. An inconsistent existing block is compiled once before the backup; if it stays inconsistent or is know-how protected, nothing is replaced unless `backup` is false. A block name that equals a technology object name is refused.
- **Input Parameters:**
  - `scl_code` *(string)*: Complete source for exactly one `FUNCTION`, `FUNCTION_BLOCK`, `ORGANIZATION_BLOCK`, or `DATA_BLOCK`. Give this or `file_path`.
  - `file_path` *(string)*: Fully qualified server-local `.scl` path; read as UTF-8 unless it has a BOM, and rejected if it is not valid UTF-8. Give this or `scl_code`.
  - `plc_name` *(string, optional)*: Target PLC.
  - `folder` *(string, optional)*: Existing PLC block group path. If omitted, an existing block's group is preserved; new blocks go in the root group.
  - `backup` *(boolean, optional, default: true)*: Back up the existing block first. With false, a failed import cannot restore the old block.
  - `compile_after_import` *(boolean, optional, default: true)*: Compile after generation.
  - `open_after_import` *(boolean, optional, default: false)*: Open the imported block in the TIA Portal editor.

#### 15. `tia_delete_block`
- **Description:** Delete a program block from the PLC software container. PLC data types are deleted with `tia_delete_plc_type`.
- **Input Parameters:**
  - `block_name` *(string, required)*: Name of block to delete.
  - `plc_name` *(string, optional)*: Target PLC.

#### 16. `tia_list_plc_types`
- **Description:** List all PLC User Data Types (UDTs) in the project.
- **Input Parameters:**
  - `plc_name` *(string, optional)*: Target PLC name.

#### 17. `tia_get_plc_type`
- **Description:** Export a User Data Type (UDT) as SimaticML XML.
- **Input Parameters:**
  - `type_name` *(string, required)*: Name of the UDT (e.g. `"typeMotorControl"`).
  - `plc_name` *(string, optional)*: Target PLC.

#### 18. `tia_import_plc_type`
- **Description:** Import or update a User Data Type (UDT) from SimaticML XML. Importing under a new name creates a second type; use `tia_rename_plc_type` to rename.
- **Input Parameters:**
  - `xml_content` *(string)*: SimaticML XML string. Give this or `file_path`.
  - `file_path` *(string)*: Fully qualified server-local `.xml` path. Give this or `xml_content`.
  - `overwrite` *(boolean, optional, default: true)*: Overwrite existing UDT.
  - `plc_name` *(string, optional)*: Target PLC.

#### 19. `tia_compile`
- **Description:** Compile PLC software to check syntax, consistency, and generate machine code.
- **Input Parameters:**
  - `plc_name` *(string, optional)*: Target PLC name.
- **Returns:**
  ```json
  {
    "State": "Success",
    "ErrorCount": 0,
    "WarningCount": 0,
    "Messages": [
      {
        "DateTime": "2026-09-30 18:30:00",
        "Description": "Block compilation successful",
        "Path": "PLC_1/Program Blocks/FB_MixingLine",
        "State": "Success"
      }
    ]
  }
  ```
- TIA Portal compiles only changed blocks, and Openness V21 has no rebuild-all option. When nothing needed compiling, the result adds `"UpToDate": true` and a `Note`, and warnings from an earlier compile are not shown again.

#### `tia_get_compile_state`
- **Description:** Read-only. Per block `IsConsistent`, `IsKnowHowProtected` and compile/modify dates; per PLC data type `IsConsistent` and `ModifiedDate`; totals. Compiles nothing.
- **Input Parameters:** `plc_name`, `group_path`, `only_inconsistent` *(default: false)*, `include_types` *(default: true)*, all optional.

#### `tia_rename_block` / `tia_rename_plc_type`
- **Description:** Rename a program block or a PLC data type in place, verified by read-back. Fails with `collision` when any block, data type, system block or system data type already has the new name (case-insensitive). The block number is unchanged.
- **Input Parameters:** `block_name` or `type_name` *(string, required)*, `new_name` *(string, required)*, `plc_name`, `group_path`, `on_conflict` *(`"fail"` only)*.

#### `tia_delete_plc_type`
- **Description:** Delete one PLC data type (UDT) by exact name. TIA Portal decides whether the delete is allowed; users of the type become inconsistent until changed.
- **Input Parameters:** `type_name` *(string, required)*, `plc_name`, `group_path`.

#### `tia_set_block_attribute`
- **Description:** Set `CyclicTime`, `PhaseOffset` (cyclic interrupt OBs) or `PriorityNumber` of an OB in place, with read-back. Written only when TIA lists the attribute as writable; otherwise `not_supported`. The project is not compiled or saved.
- **Input Parameters:** `block_name`, `attribute_name`, `value` *(integer)* (required); `unit` (`"ms"` or `"us"`, required for time values; 100 ms = 100000 us), `expected_current_value`, `plc_name`, `group_path`.

---

### Category 4: WinCC Unified HMI Screen & Widget Engineering (51 Tools)

#### 20. `hmi_list_targets`
- **Description:** Discover all HMI runtime devices and software containers in the project.
- **Input Parameters:** None (`{}`)
- **Returns:** Array of HMI targets (`DeviceName`, `SoftwareName`, `ScreenCount`, `TagTableCount`).

#### 21. `hmi_list_screens`
- **Description:** List all screens in the target HMI with dimensions and screen item counts.
- **Input Parameters:**
  - `target_name` *(string, optional)*: HMI software name.
- **Returns:**
  ```json
  [
    { "Name": "Screen_1", "Width": 1920, "Height": 1080, "ItemCount": 108 },
    { "Name": "Screen_2", "Width": 1920, "Height": 1080, "ItemCount": 150 }
  ]
  ```

#### 22. `hmi_create_screen`
- **Description:** Create or ensure existence of an HMI screen with specific dimensions.
- **Input Parameters:**
  - `screen_name` *(string, required)*: Name of screen (e.g. `"Screen_2"`).
  - `width` *(integer, optional)*: Width in pixels (e.g. `1920`).
  - `height` *(integer, optional)*: Height in pixels (e.g. `1080`).
  - `target_name` *(string, optional)*: HMI software name.

#### 23. `hmi_delete_screen`
- **Description:** Delete an HMI screen from the project.
- **Input Parameters:**
  - `screen_name` *(string, required)*: Name of screen.
  - `target_name` *(string, optional)*: HMI software name.

#### 24. `hmi_get_screen_items`
- **Description:** Detailed readout of all objects (buttons, labels, shapes, IO fields) on a screen with positions, colors, scripts, and dynamizations.
- **Input Parameters:**
  - `screen_name` *(string, required)*: Name of screen.
  - `target_name` *(string, optional)*: HMI software name.
- **Returns:** Array of objects with `Left`, `Top`, `Width`, `Height`, `BackColor`, `BorderColor`, `Text`, `TappedScript`, and `Dynamizations`.

For complete supported state on one item, use `hmi_get_screen_item`. To update its allowlisted properties without replacing the item, use `hmi_update_screen_item`. Use `hmi_read_event_scripts` / `hmi_set_event_script` for supported screen or item events. Use `hmi_set_expression_dynamization` to update an expression formula while preserving mapping rows, or `hmi_set_expression_dynamization_entries` to replace the ordered Expressions-tab condition/value rows for one property. The latter requires explicit `on_conflict: "replace"` when a same-property dynamization already exists. Both direct updates verify readback and do not save the project.

Use `hmi_get_js_type_definitions` or the read-only resource `tia://docs/hmi-javascript-type-definitions` for the server-bundled TypeScript declaration subset. It documents only runtime APIs referenced by this server; it is not a complete Siemens-generated V21 API definition and omits unverified tag-read and direct item-object access APIs.

#### 25. `hmi_delete_screen_items`
- **Description:** Delete specific screen items by name list or prefix (e.g. `"M101_"`).
- **Input Parameters:**
  - `screen_name` *(string, required)*: Screen name.
  - `item_names` *(array of string, optional)*: Specific item names.
  - `prefix` *(string, optional)*: Prefix filter.
  - `target_name` *(string, optional)*: HMI software name.

#### 26. `hmi_add_label`
- **Description:** Add or update a label / text box with typography, alignment, and colors.
- **Input Parameters:**
  - `screen_name` *(string, required)*: Target screen.
  - `name` *(string, required)*: Item name (e.g. `"lbl_Header"`).
  - `text` *(string, required)*: Display text as plain Unicode (no HTML entities; numeric references such as `&#196;` are decoded).
  - `left`, `top`, `width`, `height` *(integers, required)*: Geometry.
  - `font_size` *(number, optional, default: 11)*: Font size in pt.
  - `is_bold` *(boolean, optional, default: false)*: Bold text.
  - `align` *(string, optional, default: "Center")*: `"Left"`, `"Center"`, `"Right"`.
  - `fore_color` *(string, optional)*: Hex or named color (`"#FFFFFF"`).
  - `back_color` *(string, optional)*: Background color (`"transparent"`).
  - `target_name` *(string, optional)*: Target HMI software.

#### 27. `hmi_add_button`
- **Description:** Add or update a button with caption, colors, pressed state tag, and OnClick/Tapped JavaScript script.
- **Input Parameters:**
  - `screen_name` *(string, required)*: Screen name.
  - `name` *(string, required)*: Button item name.
  - `text` *(string, required)*: Button caption as plain Unicode (no HTML entities; numeric references are decoded).
  - `left`, `top`, `width`, `height` *(integers, required)*: Geometry.
  - `back_color` *(string, optional)*: Button color (e.g. `"#16A34A"`).
  - `fore_color` *(string, optional)*: Text color.
  - `pressed_tag` *(string, optional)*: HMI tag for pressed state.
  - `tapped_script` *(string, optional)*: JavaScript script for Tapped event.
  - `target_name` *(string, optional)*: Target HMI software.
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

#### 28. `hmi_add_shape`
- **Description:** Add or update a shape (`"Rectangle"` or `"Circle"`) with center/radius or coordinates, fill and border.
- **Input Parameters:**
  - `screen_name` *(string, required)*: Screen name.
  - `name` *(string, required)*: Shape item name.
  - `shape_type` *(string, required)*: `"Rectangle"` or `"Circle"`.
  - `left`, `top`, `width`, `height` *(integers, optional)*: Used for Rectangle.
  - `center_x`, `center_y`, `radius` *(integers, optional)*: Used for Circle.
  - `back_color` *(string, optional)*: Fill color.
  - `border_color` *(string, optional)*: Border color.
  - `border_width` *(integer, optional, default: 1)*: Border width in px.
  - `target_name` *(string, optional)*: Target HMI software.

#### 29. `hmi_add_io_field`
- **Description:** Add or update an IO Field bound to an HMI tag.
- **Input Parameters:**
  - `screen_name` *(string, required)*: Screen name.
  - `name` *(string, required)*: Item name.
  - `left`, `top`, `width`, `height` *(integers, required)*: Geometry.
  - `mode` *(string, optional, default: "Output")*: `"Output"` or `"InputOutput"`.
  - `process_tag` *(string, optional)*: HMI tag bound to ProcessValue.
  - `output_format` *(string, optional)*: Output format, e.g. `"{F1} °C"`; omit to keep the current format.
  - `target_name` *(string, optional)*: Target HMI software.

#### 30. `hmi_set_tag_dynamization`
- **Description:** Configure tag dynamization on an item property using numeric ranges or Boolean `Singlebit` mapping.
- **Input Parameters:**
  - `screen_name` *(string, required)*: Screen name.
  - `item_name` *(string, required)*: Item name.
  - `property_name` *(string, required)*: Property to dynamize (`"BackColor"`, `"Visible"`).
  - `tag_name` *(string, required)*: HMI tag name.
  - `condition_type` *(string, optional, default: "None")*: `"None"`, `"Range"`, or `"Singlebit"`.
  - `ranges` *(array of objects, optional)*: For `"Range"`, numeric lower/upper bounds; for `"Singlebit"`, exactly one `0..0` and one `1..1` entry whose values map the off/on states.
  - `target_name` *(string, optional)*: Target HMI software.
- An existing Singlebit table is updated in place. Switching a table that has entries to another condition type, or replacing a non-tag dynamization, fails: call `hmi_delete_dynamization` first. All changes run in one transaction.
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

#### `hmi_update_dynamization_entry`
- **Description:** Update one existing tag or expression dynamization mapping entry in place.
- **Input Parameters:**
  - `screen_name`, `item_name`, `property_name` *(string, required)*: Identify the screen item and its dynamized property.
  - `entry_index` *(integer, required)*: Zero-based mapping-table entry index.
  - `new_value`, `new_alternate_value` *(object, optional)*: Typed value object, for example `{ "type": "Color", "value": "#E80000" }`. The supplied CLR type must match the existing entry value.
  - `new_flashing` *(boolean, optional)*: Update the entry's flashing enable state.
  - `new_flashing_rate` *(string, optional)*: TIA flashing rate enum name.
  - `target_name` *(string, optional)*: Target HMI software.

#### `hmi_delete_dynamization`
- **Description:** Delete only one item's dynamization for a selected property. The screen item and its other dynamizations are preserved; the operation is verified and does not save the project.
- **Input Parameters:**
  - `screen_name`, `item_name`, `property_name` *(string, required)*: Identify the screen item and the dynamized property to remove.
  - `target_name` *(string, optional)*: Target HMI software.

#### 31. `hmi_update_button_scripts`
- **Description:** Modify button event scripts **in-place** without deleting items. Supports substring search/replace or unconditional script setting, running Openness JavaScript syntax checking.
- **Input Parameters:**
  - `screen_name` *(string, required)*: Screen name.
  - `find_text` *(string, optional)*: Text to find (e.g. `"SetBitInTag"`).
  - `replace_text` *(string, optional)*: Replacement text (e.g. `"InvertBitInTag"`).
  - `new_script` *(string, optional)*: Complete script code override.
  - `button_names` *(array of string, optional)*: Filter specific buttons.
  - `target_name` *(string, optional)*: Target HMI software.
- **Sample Invocation:**
  ```json
  {
    "screen_name": "Screen_2",
    "find_text": "SetBitInTag",
    "replace_text": "InvertBitInTag"
  }
  ```

#### 32. `hmi_list_tag_tables`
- **Description:** Enumerate all HMI tag tables in the HMI software target.
- **Input Parameters:**
  - `target_name` *(string, optional)*: Target HMI software.

#### 33. `hmi_list_tags`
- **Description:** List HMI tags with their table, connection, PLC tag binding, and data type.
- **Input Parameters:**
  - `table_name` *(string, optional)*: Tag table filter.
  - `target_name` *(string, optional)*: Target HMI software.

#### 34. `hmi_create_tag`
- **Description:** Create or update an HMI tag linked to a PLC tag path.
- **Input Parameters:**
  - `tag_name` *(string, required)*: HMI tag name.
  - `plc_tag` *(string, required)*: PLC tag path (e.g. `"DB_MixingLine.Line.MotorInfeed.RunFb"`).
  - `connection` *(string, optional, default: "HMI_Connection_1")*: HMI connection name.
  - `table_name` *(string, optional, default: "Default tag table")*: Target table.
  - `target_name` *(string, optional)*: Target HMI software.

#### 35. `hmi_update_tag_acquisition_cycles`
- **Description:** Batch update acquisition cycles (e.g. `T100ms`, `T1s`, `T500ms`) for HMI tags in WinCC Unified.
- **Input Parameters:**
  - `cycle` *(string, required)*: Target acquisition cycle (e.g. `"T100ms"`, `"T1s"`).
  - `plc_only` *(boolean, optional, default: true)*: Only update PLC-connected tags.
  - `table_name` *(string, optional)*: Specific tag table (all tables if omitted).
  - `target_name` *(string, optional)*: Target HMI software.

#### 36. `hmi_set_tag_range`
- **Description:** Configure minimum and maximum constant limits (`InitialMinValue` / `InitialMaxValue`) on an HMI tag.
- **Input Parameters:**
  - `tag_name` *(string, required)*: Target HMI tag name.
  - `min_value` *(number, optional)*: Lower limit value.
  - `max_value` *(number, optional)*: Upper limit value.
  - `table_name` *(string, optional)*: Specific tag table.
  - `target_name` *(string, optional)*: Target HMI software.

#### 37. `hmi_create_data_log`
- **Description:** Create a new DataLog in WinCC Unified HMI software.
- **Input Parameters:**
  - `log_name` *(string, required)*: Name of the DataLog (e.g. `"Process_DataLog"`).
  - `target_name` *(string, optional)*: Target HMI software.

#### 38. `hmi_assign_logging_tags`
- **Description:** Assign multiple HMI tags to a DataLog with logging mode and cycle.
- **Input Parameters:**
  - `log_name` *(string, required)*: Target DataLog name.
  - `tag_names` *(array, required)*: List of HMI tag names to log.
  - `cycle` *(string, optional, default: "T1s")*: Logging cycle interval.
  - `mode` *(string, optional, default: "Cyclic")*: Logging mode (`"Cyclic"`, `"OnChange"`, `"OnDemand"`).
  - `target_name` *(string, optional)*: Target HMI software.

#### 39. `hmi_compile`
- **Description:** Compile a WinCC Unified HMI via Openness `ICompilable`. The HMI is selected like in the other HMI tools; the only HMI in a project is selected automatically. Returns compilation state, message logs, error counts, and `Target`, `DeviceName` and `CompiledObject`.
- **Input Parameters:**
  - `target_name` *(string, optional)*: Exact HMI software, device item or device name (e.g. `"HMI_RT_1"`). Required when the project has several HMIs; the error lists them.
  - `target_device` *(string, optional)*: Deprecated alias of `target_name`.
- **Sample Invocation & Output:**
  ```json
  { "target_name": "HMI_RT_1" }
  ```
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

#### `hmi_get_runtime_settings`
- **Description:** Read-only. Returns the WinCC Unified Runtime settings: start screen (and whether it exists), screen resolution, auto log-off URL, input hint, panning, zooming, bit selection, font, GMP and e-signature settings, read errors and validation messages.
- **Input Parameters:**
  - `target_name` *(string, optional)*: Target HMI.

#### `hmi_set_start_screen`
- **Description:** Set the Runtime start screen in place, read it back and validate the Runtime settings. On a start-screen validation error or a wrong read-back, the previous start screen is restored and nothing is committed.
- **Input Parameters:**
  - `screen_name` *(string, required)*: Exact name of an existing screen (also inside a screen group).
  - `on_conflict` *(string, required)*: `"fail"`.
  - `expected_current_start_screen` *(string, optional)*: Guard; `""` means no start screen.
  - `target_name` *(string, optional)*: Target HMI.

#### 40. `hmi_list_connections`
- **Description:** List all configured HMI communication links (name, driver, partner station, partner node, and disabled status).
- **Input Parameters:**
  - `device_name` *(string, optional)*: Target HMI device or software name.

#### 41. `hmi_create_connection`
- **Description:** Create or configure an integrated HMI connection between an HMI target and partner PLC device/CPU via Openness `CommunicationManagement.Connections.Create<HmiConnection>`.
- **Input Parameters:**
  - `connection_name` *(string, required)*: Connection name (e.g. `"HMI_PLC_1_Connection_1"`).
  - `partner_plc_name` *(string, required)*: Partner PLC name (e.g. `"PLC_1"`).
  - `device_name` *(string, optional)*: Target HMI device name.
  - `communication_driver` *(string, optional, default: "SIMATIC S7 1200/1500")*: Driver name.
  - `disabled_at_startup` *(boolean, optional, default: false)*: Disabled at startup flag.
  - `comment` *(string, optional)*: Optional comment.

#### 42. `hmi_delete_connection`
- **Description:** Delete an HMI connection from WinCC Unified software and/or hardware communication management.
- **Input Parameters:**
  - `connection_name` *(string, required)*: Connection name to delete (e.g. `"HMI_Connection_2"`).
  - `device_name` *(string, optional)*: Target HMI device name.

#### 43. `hmi_list_text_lists`
- **Description:** List all user-defined text lists on a WinCC Unified device with their integer values/ranges, default texts, and multilingual entries.
- **Input Parameters:**
  - `device_name` *(string, optional)*: Target HMI device or software name.
- **Returns:** Array of text lists with `Name`, `Entries` (values, ranges, default text, multilingual dictionary).

#### 44. `hmi_create_text_list`
- **Description:** Create a new text list on a WinCC Unified device with specified entries and values/ranges. Automatically discovers configured device runtime languages (`en-US`, `fr-CA`, etc.) and populates entries across all languages via Version 2.0 YAML import.
- **Input Parameters:**
  - `list_name` *(string, required)*: Name of the text list to create.
  - `entries` *(array, required)*: List of entries: `[{ "value": 0, "from_value": 0, "to_value": 0, "text": "zero", "multilingual_texts": { "en-US": "zero" } }]`.
  - `device_name` *(string, optional)*: Target HMI device or software name.

#### 45. `hmi_delete_text_list`
- **Description:** Delete an existing HMI text list by name from a WinCC Unified device.
- **Input Parameters:**
  - `list_name` *(string, required)*: Name of the text list to delete.
  - `device_name` *(string, optional)*: Target HMI device or software name.

#### 46. `hmi_export_text_lists`
- **Description:** Export all HMI text lists on a WinCC Unified device to YAML files (`.hmi.yml` and `.TextLibrary.hmi.yml`) in a target directory.
- **Input Parameters:**
  - `destination_folder` *(string, required)*: Target directory on disk.
  - `base_filename` *(string, required)*: Base filename prefix for export (e.g. `"AllTextLists"`).
  - `device_name` *(string, optional)*: Target HMI device or software name.

#### 47. `tia_list_tag_tables`
- **Description:** Enumerate PLC tag tables in the CPU software container.
- **Input Parameters:**
  - `plc_name` *(string, optional)*: Target PLC.

#### 48. `tia_get_tags`
- **Description:** Read PLC tags (%I, %Q, %M addresses, data types, comments) from a PLC tag table.
- **Input Parameters:**
  - `table_name` *(string, optional)*: PLC tag table name.
  - `plc_name` *(string, optional)*: Target PLC.

---

### Category 5: Siemens TestSuite Automation (2 Tools)

#### 49. `tia_testsuite_list_tests`
- **Description:** Enumerate configured application test cases in the project's TestSuite container.
- **Input Parameters:** None (`{}`)
- **Returns:**
  ```json
  [
    {
      "Name": "TC_MixingLine_Sequence",
      "Scope": "PLC_1"
    }
  ]
  ```

#### 50. `tia_testsuite_load_test`
- **Description:** Load, import, or reload a `.tat` test case file into the project with `ImportOptions.Override`.
- **Input Parameters:**
  - `file_path` *(string, required)*: Path to the `.tat` file on disk.
- **Sample Invocation:**
  ```json
  {
    "file_path": "D:\\TIA_Project\\_AEs\\TestMCP-DDup\\UserFiles\\AI\\TC_MixingLine_Sequence.tat"
  }
  ```

---

### Category 6: S7-PLCSIM Advanced Live Simulation & Testing (12 Tools)

The `plcsim_*` tools support S7-PLCSIM Advanced V8.0 or newer only (S7-1500 and ET 200SP virtual controllers). Standard S7-PLCSIM (V18-V21), and therefore S7-1200 simulation, has no public API and is not supported. If a standard S7-PLCSIM instance has TCP/IP communication enabled, `plc_online_connect` to its IP address may be able to read tags (read-only; not verified).

#### 51. `plcsim_list_instances`
- **Description:** Returns an object, not an array (changed in v2.14.0): `runtimeAvailable`, `apiPath`, `apiVersion`, `apiAssemblyVersion`, `runtimeManagerAvailable`, `runtimeVersion`, `instances[]` (ID, name, operating state, CPU type, IP), `standardPlcSimProcesses[]`, `message` and `error`. The call succeeds also when the runtime is not available; check `runtimeAvailable`.
- **Input Parameters:** *(none)*

#### 52. `plcsim_connect`
- **Description:** Attaches directly to a virtual S7-PLCSIM Advanced instance by name, caching symbolic tag tables for zero-overhead live memory I/O.
- **Input Parameters:**
  - `instance_name` *(string, optional, default: `"PLC_1"`)*: Target virtual controller instance name.

#### 53. `plcsim_disconnect`
- **Description:** Disconnects from the current virtual PLC instance and clears cached tag metadata.
- **Input Parameters:** *(none)*

#### 54. `plcsim_get_status`
- **Description:** Queries real-time status: name, operating state (`Run`/`Stop`), scale factor, CPU model, IP, and total tags in memory.
- **Input Parameters:** *(none)*

#### 55. `plcsim_set_operating_state`
- **Description:** Commands the virtual CPU operating mode (`Run`, `Stop`, or `MemoryReset`).
- **Input Parameters:**
  - `state` *(string, required)*: Desired operating state (`"Run"`, `"Stop"`, `"MemoryReset"`).

#### 56. `plcsim_set_scale_factor`
- **Description:** Sets the simulation clock scale factor (e.g. `2.0` = 2x speed, `5.0` = 5x speed) to accelerate automated testing deterministically.
- **Input Parameters:**
  - `scale_factor` *(number, required)*: Positive speed scale multiplier (> 0.0).

#### 57. `plcsim_read_tag`
- **Description:** Reads the live value and data type of any variable from DBs, inputs, outputs, or markers by symbolic name.
- **Input Parameters:**
  - `tag_name` *(string, required)*: Symbolic tag name (e.g. `'DB_Sequence.currentStep'`).

#### 58. `plcsim_write_tag`
- **Description:** Writes a live value to a symbolic variable in virtual PLC memory.
- **Input Parameters:**
  - `tag_name` *(string, required)*: Symbolic tag name (e.g. `'DB_Sequence.cmdStart'`).
  - `value` *(any, required)*: Value to write (boolean, integer, float, string).

#### 59. `plcsim_read_tags`
- **Description:** Efficient bulk read of multiple variables in a single call.
- **Input Parameters:**
  - `tag_names` *(array of strings, required)*: List of symbolic tag names to read.

#### 60. `plcsim_write_tags`
- **Description:** Bulk write to multiple symbolic variables in a single call.
- **Input Parameters:**
  - `tags` *(object, required)*: Key-value dictionary mapping tag names to values.

#### 61. `plcsim_run_sequence`
- **Description:** Executes a generic automated online sequence test on any PLC sequence or state machine. Sets initial/parameter tags, pulses start, tracks step transitions, samples monitored tags at ~80ms, and generates an execution report.
- **Input Parameters:**
  - `start_command_tag` *(string, required)*: Tag name pulsed TRUE to initiate sequence.
  - `step_number_tag` *(string, required)*: Tag name holding current step number.
  - `done_tag` *(string, required)*: Tag name indicating sequence completion.
  - `sequence_name` *(string, optional, default: `"Sequence_01"`)*: Descriptive sequence identifier.
  - `reset_command_tag` *(string, optional)*: Tag name pulsed TRUE to reset alarms before start.
  - `step_name_tag` *(string, optional)*: String tag holding step description.
  - `fault_tag` *(string, optional)*: Tag name indicating fault/alarm.
  - `idle_step` *(integer, optional, default: `0`)*: Step number indicating idle standby state.
  - `initial_tags` *(object, optional)*: Initial values written before test start.
  - `parameter_tags` *(object, optional)*: Recipe or setpoint parameters applied before start.
  - `monitored_tags` *(array of strings, optional)*: Tag names to sample in real-time telemetry.
  - `scale_factor` *(number, optional, default: `1.0`)*: Virtual controller speed multiplier.
  - `timeout_seconds` *(integer, optional, default: `60`)*: Max timeout in seconds.

#### 62. `plcsim_run_mixing_sequence`
- **Description:** Executes an automated multi-parameter batch sequence test. Sets recipe parameters, triggers start, tracks step transitions, records 80ms telemetry, and generates an execution report.
- **Input Parameters:**
  - `recipe_id` *(string, optional, default: `"RECIPE_01"`)*: Recipe identifier.
  - `water_liters` *(number, optional, default: `100.0`)*: Target liquid volume.
  - `additive_type` *(integer, optional, default: `1`)*: Ingredient type index.
  - `additive_amount` *(number, optional, default: `20.0`)*: Target ingredient amount.
  - `sugar_kg` *(number, optional, default: `10.0`)*: Target dry ingredient mass.
  - `mix_time_seconds` *(number, optional, default: `3.0`)*: Active duration in seconds.
  - `mix_speed_rpm` *(number, optional, default: `1200.0`)*: Drive speed setpoint in RPM.
  - `scale_factor` *(number, optional, default: `1.0`)*: Simulation speed multiplier.
  - `timeout_seconds` *(integer, optional, default: `60`)*: Max timeout before declaring test failure.

---

### Category 7: Project, Library & Text Discovery (6 Tools)

#### `tia_list_global_libraries`
- Lists accessible global-library metadata from the connected TIA Portal instance. Optional `name_filter`, `offset`, and `page_size` parameters; page size is limited to 200.

#### `tia_search_project_library_types`
- Lists or searches library types in the project selected by exact open `project_path`; optional literal `query`, `offset`, and `page_size`.

#### `tia_list_project_faceplates`
- Lists faceplate library types in the exact open project's library; optional `name_filter`, `offset`, and `page_size`.

#### `tia_get_library_type_instances`
- Lists instances of an exact project-library type in one exact PLC scope; optional exact version and source-library selectors narrow the query. HMI-wide instance search is not claimed.

#### `tia_list_project_text_categories`
- Lists category identifiers and names for an exact open `project_path`; optional literal query and bounded paging.

#### `tia_download_project_texts`
- Exports texts from an exact open project with explicit `primary_culture` and `reference_culture`. Requires a new destination file in an existing directory and never overwrites an existing file.

### Additional PLC & HMI Discovery (8 Tools)

- `tia_get_block_info` / `tia_get_block_attributes`: exact PLC block metadata and supported attribute reads; for OBs also the cycle attributes (`CyclicTime` and `PhaseOffset` in microseconds, `PriorityNumber`).
- `tia_get_tag_table_info`: exact PLC tag-table scope and tag count.
- `tia_get_type_info` / `tia_get_type_attributes`: exact PLC type metadata and supported consistency read.
- `tia_get_db_member_attributes`: V21-reported attributes for one exact direct global-DB member; nested paths and inferred datatype fields are not claimed.
- `hmi_list_screen_groups`: nested screen-group hierarchy and direct screen counts for one unambiguous HMI Unified target.
- `hmi_list_graphic_lists`: graphic-list names for one unambiguous HMI Unified target.

The tools report only V21 metadata verified by this implementation. See the
[PLC](./plans/MCP_TOOL_TEST_PLAN_PLC.md),
[DB-member attributes](./plans/MCP_TOOL_TEST_PLAN_PLC_SLICE_READS.md),
[project/library/text](./plans/MCP_TOOL_TEST_PLAN_PROJECT.md), and
[HMI](./plans/MCP_TOOL_TEST_PLAN_HMI.md) test plans for per-case offline checks and
later live disposable-project verification.

### Category 8: PLC, Project & HMI Operations (18 Tools)

- `tia_get_plc_tag_info` / `tia_read_user_constant`: Read exact PLC tag
  metadata or a constant's declared type and literal, rejecting ambiguous
  targets.
- `tia_get_tag_table_attributes`: Read V21-reported table attributes,
  including values where permitted, access modes, and supported types.
- `tia_create_plc_tag_table` / `tia_create_tagtable_group`: Create tag tables
  and groups in exact PLC group paths without replacing existing objects.
- `tia_create_block_group`: Create a block group under an exact parent path
  without replacing an existing group.
- `tia_create_user_constant` / `tia_update_user_constant`: Create supported
  constants or update only an existing constant's value, with validation and
  read-back.
- `tia_create_project`: Create a project in an existing empty destination
  directory through the connected TIA Portal instance.
- `hmi_create_screen_group` / `hmi_create_screen_under_group`: Create HMI
  structure under exact group paths.
- `hmi_create_tag_table`: Create a tag table without overwriting an existing
  table; accepts only `on_conflict: "fail"`.
- `hmi_create_navigation_button`: Create a new button with a syntax-checked
  Tapped action that navigates to an exact existing screen. Captions are
  escaped and wrapped in the required HTML paragraph; existing items are not
  overwritten.
- `hmi_rename_screen`, `hmi_rename_screen_group`, and `hmi_rename_tag_table`:
  Rename existing objects in place. All HMI writes require explicit
  `on_conflict: "fail"`.
- `hmi_delete_tag`: Delete one HMI tag by exact name after a best-effort
  reference scan. With the default `on_referenced: "fail"`, a referenced tag,
  or one whose scan was incomplete, is not deleted. `dry_run` only reports.
- `hmi_rename_tag`: Rename one HMI tag in place. References found before the
  rename are read back and reported; the tool does not rewrite them.
  `script_references: "replace"` also rewrites script string literals that are
  exactly the old name. Run with `dry_run: true` first.

Mutations are non-transactional and have not been tested against a live TIA
project here. Use the per-case plans:
[PLC reads](./plans/MCP_TOOL_TEST_PLAN_PLC_READS.md),
[library-type instances](./plans/MCP_TOOL_TEST_PLAN_LIBRARY_TYPE_INSTANCES.md),
[PLC mutations](./plans/MCP_TOOL_TEST_PLAN_PLC_MUTATIONS.md),
[tag-table attributes and block groups](./plans/MCP_TOOL_TEST_PLAN_PLC_GROUP_ATTRIBUTES.md),
[project creation](./plans/MCP_TOOL_TEST_PLAN_PROJECT_MUTATIONS.md),
[HMI structure](./plans/MCP_TOOL_TEST_PLAN_HMI_STRUCTURE.md), and
[HMI navigation buttons](./plans/MCP_TOOL_TEST_PLAN_HMI_NAVIGATION.md).

### Category 11: Hardware Configuration, Networks & Project Backup (19 Tools)
- **Devices and modules:** `tia_find_module_types`, `tia_create_device`, `tia_list_plug_locations`, `tia_plug_module`, `tia_rename_hardware`, `tia_delete_hardware`, `tia_get_io_addresses`. Creation and plugging preview by default (`dry_run`); deletion needs `confirm=true`.
- **PROFINET:** `tia_list_network`, `tia_create_subnet`, `tia_connect_to_subnet`, `tia_create_io_system`, `tia_connect_to_io_system`, `tia_set_network_node`.
- **Exchange and backup:** `tia_export_aml`, `tia_import_aml` (preview by default), `tia_export_project_files` (stable files for Git), `tia_archive_project`, `tia_retrieve_project`, `tia_export_cross_references` (JSONL).
- GSD/GSDML files cannot be installed through Openness V21; install them in TIA Portal first. See `tia://docs/tools-reference`, Category 11.

### Category 12: Technology Objects (4 Tools)
- `tia_list_technology_objects`: technology objects with type, version, DB number and folder. They are not listed by `tia_list_blocks`.
- `tia_get_technology_object_parameters`: parameter names (*Name in Openness*), values and CLR types; optional name prefix `filter`.
- `tia_create_technology_object`: create `PID_Temp`, `PID_Compact`, `PID_3Step` or another type TIA accepts, with an explicit `major.minor` version. Fails on a name collision with a technology object, block or system block.
- `tia_set_technology_object_parameters`: values are strings converted to each parameter's CLR type; all are validated first, then written in order and read back. Earlier writes are not rolled back when a later one fails.

**Recipe: PID_Temp on S7-1200** (not run against a live TIA Portal):
1. `tia_list_blocks`: look for a plain PID_Temp instance DB left from an earlier SCL import. Rename or delete it, or choose another name.
2. `tia_create_technology_object` with `type: "PID_Temp"`, `name: "PID_Temp_1"`, `version: "1.1"`.
3. `tia_get_technology_object_parameters` with `name: "PID_Temp_1"` and `filter: "Config."` to learn the exact names.
4. `tia_set_technology_object_parameters` with string values for the names found in step 3.
5. `tia_import_scl_block` with a cyclic interrupt OB that calls `"PID_Temp_1"(...)`.
6. `tia_compile`.

---

## 5. Skills Overview

### 1. `figma-to-wincc-unified`
- **Path:** `.agents/skills/figma-to-wincc-unified/SKILL.md`
- **Purpose:** Converts visual Figma HMI designs and design tokens into pixel-accurate Siemens WinCC Unified V21 screens.

### 2. `tia-portal-openness`
- **Path:** `.agents/skills/tia-portal-openness/SKILL.md`
- **Purpose:** Comprehensive programmatic engineering of Siemens TIA Portal V21 hardware, PLC logic, and WinCC Unified HMI screens.

### 3. `plcsim-sequence-tester`
- **Path:** `.agents/skills/plcsim-sequence-tester/SKILL.md`
- **Purpose:** Hardware-in-the-loop automated sequence verification tests on Siemens S7-PLCSIM Advanced virtual controllers. Runs multi-parameter sequence tests, records high-resolution telemetry, and compiles verification reports.

---

## 6. Diagnostic Verification Checklist

| Test Item | Verification Procedure | Expected Result | Status |
| :--- | :--- | :--- | :---: |
| **Server Startup** | `.\TiaOpennessMcp.exe` | Loads `config.json`, starts HTTP on port 5001 | **PASSED** |
| **Health Check** | `GET http://localhost:5001/status` | Returns connection state, PID, and the registered tool count (153 in v2.14.0). | **Runtime check pending** |
| **Tool Registry** | `POST /mcp` with `tools/list` | Lists all 153 registered tool definitions in v2.14.0. | **Runtime check pending** |
| **PLCSIM Live Connect** | `plcsim_connect` on `PLC_1` | Attaches to virtual S7-1500 controller, caches tags | **PASSED** |
| **Live Tag I/O** | `plcsim_read_tag` & `write_tag` | Reads/writes DB variables with zero COM overhead | **PASSED** |
| **Online Sequence Testing** | `.\TiaOpennessMcp.exe --run-sequence-test` | Executes parameterized sequence validation | **PASSED** |
| **Validation Report Artifact** | Automated workflow output | Generates `SAMPLE_VERIFICATION_REPORT.md` | **PASSED** |
| **Screen Discovery** | `hmi_list_screens` | Returns active screens with widget counts | **PASSED** |
| **Text Lists** | `hmi_list_text_lists` | Returns configured text lists | **PASSED** |
| **TestSuite Lookup**| `tia_testsuite_list_tests` | Returns configured test cases | **PASSED** |
| **HMI Compilation** | `hmi_compile` on Unified target | Returns `State: Success`, `Errors: 0` | **PASSED** |
