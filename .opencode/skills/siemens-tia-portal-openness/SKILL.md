---
name: siemens-tia-portal-openness
description: >-
  Interact with Siemens TIA Portal V21 and WinCC Unified HMI via the persistent Openness MCP server connector.
  Provides workflows for checking status, inspecting devices & PLCs, managing SCL/SimaticML blocks,
  creating and modifying WinCC Unified screens and widgets, configuring range dynamizations, updating button scripts in-place,
  compiling PLCs and HMIs with error diagnostics, and executing Siemens TestSuite tests.
---

# Siemens TIA Portal V21 Openness Skill

This skill guides AI agents on controlling and automating Siemens TIA Portal V21 through the persistent TIA Openness MCP connector.

## 1. Architecture & Connection Management

The TIA Openness MCP server operates as a persistent daemon process maintaining an open connection to Siemens TIA Portal (`connexion must stay`):
- **Configuration**: Loaded from `config.json` (host, port default `5001`, mode `HttpAndStdio`). The server never attaches to TIA Portal on its own: call `tia_list_processes`, then `tia_connect` with a `process_id`.
- **Persistence**: Because TIA Openness instances and projects take time to initialize, the connection stays alive continuously across agent turns.
- **Transports**:
  - Direct HTTP JSON-RPC endpoint: `POST http://localhost:5001/mcp`
  - Server-Sent Events (SSE): `GET http://localhost:5001/sse`
  - Health/Status endpoint: `GET http://localhost:5001/status`
  - Standard stdio MCP transport for agent runners.

### Checking Status & Connecting
1. **`tia_get_status`**: Always check server and TIA Openness status first:
   ```json
   {}
   ```
   Returns `IsConnected`, `AttachedProcessId`, `ProjectName`, `ProjectPath`.
2. **`tia_list_processes`**: List all running TIA Portal instances if not connected.
3. **`tia_connect`**: Attach to a running instance:
   ```json
   { "process_id": 1512 }
   ```
4. **`tia_open_project`**: Open a project from disk if no instance is active:
   ```json
   { "project_path": "D:\\TIA_Project\\_AEs\\TestMCP-DDup\\TestMCP-DDup.ap21", "with_gui": true }
   ```

---

## 2. PLC Software & Program Blocks

### Inspecting Software & Blocks
- **`tia_list_plcs`**: Discover CPU device items and software containers.
- **`tia_list_blocks`**: List all blocks (OB, FB, FC, DB) with programming language and compile consistency status.
- **`tia_get_block_code`**: Read a block's code. By default (`format: "auto"`) SCL, STL and DB blocks, and PLC data types, return the source text TIA Portal generates in `Source`; LAD, FBD and GRAPH blocks return SimaticML in `XmlContent`. Pass `format: "xml"` or `include_xml: true` when you need the SimaticML. The source of one SCL block (without `with_dependencies`) can be edited and passed to `tia_import_scl_block`. `ExtractedScl` is deprecated.
- **`tia_get_compile_state`**: Read-only list of block and UDT consistency and compile dates, without compiling.

### Authoring & Updating Blocks
- **`tia_import_block`**: Import or replace block logic from SimaticML XML, inline or from a server-local file (prefer `file_path` for large blocks):
  ```json
  { "file_path": "C:\\Projects\\Plant\\export\\plc\\PLC_1\\blocks\\Main.xml", "overwrite": true }
  ```
  OB `CyclicTime` and `PhaseOffset` in SimaticML are microseconds (100 ms = `100000`). Read the `Warnings:` lines in the result.
- **`tia_import_scl_block`**: Import a complete `FUNCTION`, `FUNCTION_BLOCK`, `ORGANIZATION_BLOCK`, or `DATA_BLOCK` from `scl_code` or from a UTF-8 `.scl` `file_path`. Same-name blocks are replaced as whole blocks using backup/delete/reimport; import failures attempt to restore the backup. An inconsistent existing block is compiled once first; if it stays inconsistent the import stops without changes. Pass `backup: false` only when losing the old block on a failed import is acceptable. Compilation is enabled by default, and compile errors do not roll back the imported block. The block number can change on replace; the result warns when it does.
- **`tia_rename_block`** / **`tia_rename_plc_type`**: Rename a block or UDT in place. Do not re-import under a new name; that creates a second object.
- **`tia_delete_block`** / **`tia_delete_plc_type`**: Delete a program block or a UDT. Users of a deleted UDT become inconsistent; compile afterwards.
- **`tia_set_block_attribute`**: Change an OB's `CyclicTime`, `PhaseOffset` or `PriorityNumber` in place, with an explicit unit:
  ```json
  { "block_name": "Cyclic interrupt", "attribute_name": "CyclicTime", "value": 100, "unit": "ms" }
  ```
- **`tia_compile`**: Always compile after modifying PLC blocks or UDTs:
  ```json
  { "plc_name": "PLC_1" }
  ```
  Returns `State`, `ErrorCount`, `WarningCount`, and detailed compiler messages with file paths and line numbers. TIA compiles only changed blocks; when nothing needed compiling the result has `UpToDate: true` and earlier warnings are not shown again.

### Technology Objects
- **`tia_list_technology_objects`**, **`tia_get_technology_object_parameters`**: List technology objects and read their parameters (names differ per type and version; read them before writing).
- **`tia_create_technology_object`**: Create e.g. `PID_Temp` with an explicit version (`"1.1"` on S7-1200). It fails if a block or technology object already has the name.
- **`tia_set_technology_object_parameters`**: Write parameters as strings; each is read back, and earlier writes are not rolled back when a later one fails.
- Creating the object does not call it: call it from a cyclic interrupt OB (`"PID_Temp_1"(...)`) with `tia_import_scl_block`.

---

## 3. WinCC Unified HMI Screen & Widget Engineering

### Discovery & Screen Lifecycle
- **`hmi_list_targets`**: Discover all HMI runtime devices and software containers.
- **`hmi_list_screens`**: Enumerate screens with dimensions (`Width`, `Height`) and item counts.
- **`hmi_create_screen`**: Create a screen with target resolution:
  ```json
  { "screen_name": "Screen_2", "width": 1920, "height": 1080 }
  ```
- **`hmi_get_screen_items`**: Inspect all widgets, shapes, text, scripts, and dynamizations on a screen.
- **`hmi_get_js_type_definitions`**: Get the server-bundled TypeScript declaration subset for runtime APIs documented by this server. It is not the complete Siemens V21 API; see `tia://docs/hmi-javascript-type-definitions`.

### Creating Widgets & Controls
Pass texts as plain Unicode (ä, ö, °), not HTML entities; the server escapes and wraps them.
- **`hmi_add_label`**: Add a text label or banner (text is HTML-escaped and wrapped automatically):
  ```json
  {
    "screen_name": "Screen_2",
    "name": "lbl_Title",
    "text": "MOTOR OVERVIEW",
    "left": 40, "top": 30, "width": 400, "height": 40,
    "font_size": 18, "is_bold": true, "align": "Center",
    "fore_color": "white", "back_color": "#1E293B"
  }
  ```
- **`hmi_add_button`**: Add a control button with an OnClick/Tapped JavaScript handler:
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
- **`hmi_add_shape`**: Add indicator shapes:
  - Circle (e.g. pilot light, motor status):
    ```json
    {
      "screen_name": "Screen_2",
      "name": "circ_M101",
      "shape_type": "Circle",
      "center_x": 160, "center_y": 140, "radius": 18,
      "back_color": "gray", "border_color": "white", "border_width": 2
    }
    ```
  - Rectangle (e.g. background cards, valve indicators):
    ```json
    {
      "screen_name": "Screen_2",
      "name": "rect_Card1",
      "shape_type": "Rectangle",
      "left": 30, "top": 80, "width": 320, "height": 220,
      "back_color": "#1E293B", "border_color": "#334155", "border_width": 1
    }
    ```
- **`hmi_add_io_field`**: Add numeric/text IO display bound to process tag:
  ```json
  {
    "screen_name": "Screen_2",
    "name": "io_ActualSpeed",
    "left": 180, "top": 150, "width": 100, "height": 30,
    "mode": "Output", "process_tag": "HMI_MotorInfeed_Speed"
  }
  ```

### Dynamization & Color Mapping
Use **single-object dynamization** instead of dual overlaid objects with visibility toggles. Use `ConditionType.Singlebit` for Boolean tags and `ConditionType.Range` for numeric range mappings:
- **`hmi_set_tag_dynamization`**, Boolean example:
  ```json
  {
    "screen_name": "Screen_2",
    "item_name": "circ_M101",
    "property_name": "BackColor",
    "tag_name": "HMI_MotorInfeed_RunFb",
    "condition_type": "Singlebit",
    "ranges": [
      { "from": 0, "to": 0, "value": "gray" },
      { "from": 1, "to": 1, "value": "green" }
    ]
  }
  ```

`hmi_set_tag_dynamization` sets the whole mapping. An existing Singlebit table is
updated in place; switching an existing table to another condition type fails, so
call `hmi_delete_dynamization` first. For an existing
dynamization where only one entry changes, use **`hmi_update_dynamization_entry`**;
its `entry_index` is zero-based, supplied value types must match the existing entry,
and unspecified fields and entries are preserved. Do not use the setter as a
single-entry editor.
```json
{
  "screen_name": "Screen_2",
  "item_name": "circ_M101",
  "property_name": "BackColor",
  "entry_index": 1,
  "new_value": { "type": "Color", "value": "#E80000" }
}
```

For WinCC Unified Expressions-tab condition/value tables, use
**`hmi_set_expression_dynamization_entries`**. Supply the complete ordered row
list; each row contains a non-empty `condition` expression and a typed `value`
whose type matches the target property. This replaces all rows for that
property, so use `on_conflict: "replace"` if any dynamization already exists
there. The tool preserves the property's default value and all unrelated
dynamizations, then verifies the ordered readback. TIA expression syntax is not
validated by the MCP tool, and it does not save or compile the project.
```json
{
  "screen_name": "Screen_2",
  "item_name": "btn_Position",
  "property_name": "Top",
  "entries": [
    { "condition": "AND32('StatusWord', 1) != 0", "value": { "type": "Int32", "value": "500" } },
    { "condition": "AND32('StatusWord', 2) != 0", "value": { "type": "Int32", "value": "400" } }
  ]
}
```

To remove only one item's dynamization for a property, use
**`hmi_delete_dynamization`** with the screen, item, property, and optional HMI
target. It preserves the item and all unrelated dynamizations, verifies that the
selected dynamization is gone, and does not save the project. Do not use
`hmi_set_tag_dynamization` with `condition_type: "None"` as a reset.

For a direct dynamization edit, check `tia_get_status` and identify the HMI target
first. Read the screen with `hmi_get_screen_items` before editing, verify the item,
property, existing tag, condition type, and mapping entry index, then apply the
appropriate direct tool. Do not use HMI JSON export/import for this direct workflow.
Read the screen again to verify the resulting tag dynamization and compile with
`hmi_compile` (pass `target_name` when the project has more than one HMI). For detailed arguments and response shapes, read the MCP resource
`tia://docs/tools-reference`; prefer the live tool schemas when available.

For JavaScript API references, use `hmi_get_js_type_definitions` or read
`tia://docs/hmi-javascript-type-definitions`. The bundled declarations are a
documented subset, not the complete Siemens V21 runtime API. Do not infer
support for APIs absent from that subset.

### In-Place Button Script Updating
When modifying existing button functions (e.g. changing `SetBitInTag` to `InvertBitInTag` or changing target tags), **do NOT delete and recreate screen items**. Modifying items in-place preserves layouts, object order, and styles:
- **`hmi_update_button_scripts`**:
  ```json
  {
    "screen_name": "Screen_2",
    "find_text": "SetBitInTag",
    "replace_text": "InvertBitInTag"
  }
  ```
  The MCP server applies changes directly to the Openness DOM and runs `handler.Script.SyntaxCheck()` to guarantee syntax validity.
- **`hmi_update_screen_item`**: Change other item properties in place (position, size, colors, `Visible`, `Enabled`, `OutputFormat`, text alignment, a screen window's `Screen`, `Text`, `Font`). A plain `Text` string keeps the existing paragraph style:
  ```json
  {
    "screen_name": "Screen_2",
    "item_name": "btn_M101_Start",
    "item_properties": { "Text": "Käynnistä", "Font": { "Size": 14, "Bold": true } }
  }
  ```

### HMI Tag Management & Acquisition Cycles
- **`hmi_list_tags`**: List all tags, tables, PLC bindings, data types, and acquisition cycles.
- **`hmi_create_tag`**: Create or update an HMI tag linked to a PLC tag path:
  ```json
  {
    "tag_name": "HMI_MotorInfeed_RunFb",
    "plc_tag": "DB_MixingLine.Line.MotorInfeed.RunFb",
    "connection": "HMI_Connection_1",
    "table_name": "Default tag table"
  }
  ```
- **`hmi_update_tag_acquisition_cycles`**: Batch update acquisition cycles for PLC-connected or internal tags (e.g. `T100ms`, `T1s`):
  ```json
  {
    "cycle": "T100ms",
    "plc_only": true
  }
  ```
- **`hmi_set_tag_range`**: Configure minimum and maximum limit constants (`InitialMinValue` / `InitialMaxValue`) on an HMI tag (e.g. speeds between 0 and 1800):
  ```json
  {
    "tag_name": "HMI_MotorInfeed_SpeedSetpoint",
    "min_value": 0.0,
    "max_value": 1800.0
  }
  ```
- **`hmi_create_data_log`**: Create a persistent DataLog in the WinCC Unified runtime:
  ```json
  {
    "log_name": "Process_DataLog"
  }
  ```
- **`hmi_assign_logging_tags`**: Assign analog/digital tags to a DataLog with mode (`Cyclic`, `OnChange`, `OnDemand`) and cycle (`T1s`, `T500ms`, `T100ms`):
  ```json
  {
    "log_name": "Process_DataLog",
    "tag_names": [
      "HMI_MotorInfeed_SpeedAct",
      "HMI_MotorInfeed_CurrentAct",
      "HMI_TankCityWater_Level"
    ],
    "cycle": "T1s",
    "mode": "Cyclic"
  }
  ```
- **`hmi_delete_tag`**: Delete one HMI tag. Run with `dry_run: true` first. With the default `on_referenced: "fail"`, a tag that is still referenced, or whose reference scan was incomplete, is not deleted; read `References`, `CrossReferences` and `NotScanned` in the result.
- **`hmi_rename_tag`**: Rename one HMI tag in place (never delete and recreate it):
  ```json
  { "tag_name": "HMI_Motor1_Run", "new_name": "HMI_Motor1_Running", "on_conflict": "fail", "dry_run": true }
  ```
  After the rename, check `StructuredReferences` for references that still read the old name (`renamed_with_stale_references`). `script_references: "replace"` rewrites only script string literals that are exactly the old name. The PLC tag is not renamed, and old HMI JSON exports still contain the old name.
### Integrated HMI Connections & Communication Management
- **`hmi_list_connections`**: Enumerate all configured HMI communication links on an HMI target:
  ```json
  {
    "device_name": "PC-System_1"
  }
  ```
  Returns connection name, communication driver (`SIMATIC S7 1200/1500`), partner station name (`PLC_1`), partner node description, and startup disabled status.
- **`hmi_create_connection`**: Establish or configure an integrated hardware HMI connection to a partner PLC:
  ```json
  {
    "connection_name": "HMI_PLC_1_Connection_1",
    "partner_plc_name": "PLC_1",
    "device_name": "PC-System_1",
    "communication_driver": "SIMATIC S7 1200/1500",
    "disabled_at_startup": false,
    "comment": "Integrated S7-1500 Ethernet communication link"
  }
  ```
  *Openness Integration Note*: An integrated HMI connection binds hardware nodes via `CommunicationManagement.Connections.Create<HmiConnection>(localNode, partnerCpuItem, partnerNode)`. TIA Openness automatically links the software connection in WinCC Unified runtime to the partner CPU, subnet, and interface.
- **`hmi_delete_connection`**: Delete an HMI connection from WinCC Unified software and/or hardware communication management:
  ```json
  {
    "connection_name": "HMI_Connection_2",
    "device_name": "PC-System_1"
  }
  ```
  Safely removes dangling or unneeded HMI connections from both hardware topology (`CommunicationManagement.Connections`) and runtime software connections (`HmiSoftware.Connections`), eliminating compiler errors.

### HMI Text Lists Management
WinCC Unified V21 manages multilingual text lists through its Version 2.0 YAML specification (`.hmi.yml` and `.TextLibrary.hmi.yml`):
- **`hmi_list_text_lists`**: List all user-defined text lists on the target Unified device along with their integer values/ranges, default texts, and multilingual entries:
  ```json
  {
    "device_name": "PC-System_1"
  }
  ```
- **`hmi_create_text_list`**: Create a new text list with decimal value or range entries and automatic multilingual propagation:
  ```json
  {
    "list_name": "testList",
    "entries": [
      { "value": 0, "text": "zero" },
      { "value": 1, "text": "one" },
      { "value": 2, "text": "two" }
    ],
    "device_name": "PC-System_1"
  }
  ```
  *Note*: Accepts optional `from_value`, `to_value`, and `multilingual_texts` dictionary per entry.
- **`hmi_delete_text_list`**: Delete an existing HMI text list by name:
  ```json
  {
    "list_name": "testList",
    "device_name": "PC-System_1"
  }
  ```
- **`hmi_export_text_lists`**: Export all HMI text lists to a directory in `.hmi.yml` and `.TextLibrary.hmi.yml` format:
  ```json
  {
    "destination_folder": "C:\\path\\to\\export",
    "base_filename": "AllTextLists",
    "device_name": "PC-System_1"
  }
  ```

### Runtime Settings
- **`hmi_get_runtime_settings`**: Read the Runtime settings, including the start screen and validation messages.
- **`hmi_set_start_screen`**: Set the start screen in place:
  ```json
  { "screen_name": "Frame", "on_conflict": "fail" }
  ```

### HMI Compilation
Always compile the HMI target after modifying screens or tags to ensure runtime consistency:
- **`hmi_compile`**: The only HMI in a project is selected automatically; with several HMIs pass the HMI software, device item or device name as `target_name` (`target_device` is a deprecated alias):
  ```json
  { "target_name": "HMI_RT_1" }
  ```
  Verifies that JavaScript event scripts, tag bindings, and screen geometries compile with 0 errors. `CompiledObject` in the result names the object that was compiled.

---

## 4. Siemens TestSuite Automation

- **`tia_testsuite_list_tests`**: List test cases configured in the project.
- **`tia_testsuite_load_test`**: Import or update a `.tat` test case file from disk:
  ```json
  { "file_path": "D:\\TIA_Project\\_AEs\\TestMCP-DDup\\UserFiles\\AI\\TC_MixingLine_Sequence.tat" }
  ```
  Uses `ImportOptions.Override` to cleanly refresh test logic.

---

## 5. WinCC Unified Runtime System Functions Catalog (`SysFct`)

All system functions can be called directly in JavaScript scripts configured on buttons (Tapped event), screen events (Loaded, Unloaded), or cyclic tasks.

### Siemens Documentation Lookup

This catalog is a summary. Before writing SCL, Openness-driven changes, or
WinCC Unified scripts that use an instruction or API you have not verified,
check it with the `docs_*` tools (no TIA connection needed): `docs_search`
(for example `{ "query": "SysFct.OpenScreenInPopup", "product": "WinCC Unified", "version": "V21" }`),
then `docs_read_page` with `mode: "outline"`, `section`, or `mode: "code"`.
Use the documented parameter order and types rather than recalling them.

### 5.1 Tag System Functions (`Tags.SysFct.*`)
- **`Tags.SysFct.InvertBitInTag(tagName, bitNumber)`**: Invert (toggle) specific bit and send to PLC. Ideal for Start/Stop toggle buttons.
- **`Tags.SysFct.SetBitInTag(tagName, bitNumber)`**: Set specific bit to 1 (true).
- **`Tags.SysFct.ResetBitInTag(tagName, bitNumber)`**: Reset specific bit to 0 (false).
- **`Tags.SysFct.SetTagValue(tagName, value)`**: Write numeric, boolean, or string value directly.
- **`Tags.SysFct.IncreaseTag(tagName, stepValue)`**: Increment numeric tag value by step.
- **`Tags.SysFct.DecreaseTag(tagName, stepValue)`**: Decrement numeric tag value by step.
- **`Tags.SysFct.LinearScaling(sourceTag, sMin, sMax, targetTag, tMin, tMax)`**: Scale analog value between ranges.
- **`Tags.SysFct.ShiftAndMask(tag, shift, mask, targetTag)`**: Bitwise shift and mask operation.
- **`Tags.SysFct.UpdateTag(updateId)`**: Force one-shot update for cyclic tag group.

### 5.2 Screen Navigation & Window Functions (`HMIRuntime.UI.SysFct.*`)
- **`HMIRuntime.UI.SysFct.ChangeScreen(screenName, screenWindowPath)`**: Switch screen in window. **Parameter `screenWindowPath` must be `"~"`** for current window.
- **`HMIRuntime.UI.SysFct.ChangeScreenAsync(screenName, screenWindowPath)`**: Asynchronous screen switch.
- **`HMIRuntime.UI.SysFct.ChangeScreenAsyncByNumber(screenNumber, screenWindowPath)`**: Screen switch by index.
- **`HMIRuntime.UI.SysFct.OpenScreenInPopup(popupName, screenName, title, left, top, showHeader, flags)`**: Open floating modal/modeless popup window.
- **`HMIRuntime.UI.SysFct.ClosePopup(popupName)`**: Close specified popup window.
- **`HMIRuntime.UI.SysFct.ZoomIn(screenWindowPath, factor)`**: Zoom into screen window.
- **`HMIRuntime.UI.SysFct.ZoomOut(screenWindowPath, factor)`**: Zoom out of screen window.
- **`HMIRuntime.UI.SysFct.ResetZoom(screenWindowPath)`**: Reset zoom level to 100% (1.0).

### 5.3 Screen Item Properties & Focus (`HMIRuntime.UI.SysFct.*`)
- **`HMIRuntime.UI.SysFct.SetPropertyValue(itemPath, propertyName, value)`**: Programmatically write item property (e.g. BackColor).
- **`HMIRuntime.UI.SysFct.GetPropertyValue(itemPath, propertyName)`**: Read item property at runtime.
- **`HMIRuntime.UI.SysFct.SetFocusOnElement(itemName, screenWindowPath)`**: Set input keyboard/mouse focus.

### 5.4 Alarms & Event Functions (`HMIRuntime.Alarming.SysFct.*`)
- **`HMIRuntime.Alarming.SysFct.AcknowledgeAlarm(alarmId)`**: Acknowledge active alarm.
- **`HMIRuntime.Alarming.SysFct.ResetAlarm(alarmId)`**: Reset cleared alarm.
- **`HMIRuntime.Alarming.SysFct.ExportAlarmLog(logName, filePath, timeRange, format)`**: Export archived alarm log to CSV.
- **`HMIRuntime.Alarming.SysFct.RestoreAlarmLog(logName, filePath)`**: Restore alarm log backup.

### 5.5 Historical Logging & Process Data (`HMIRuntime.Logging.SysFct.*`)
- **`HMIRuntime.Logging.SysFct.StartTagLogging(logName)`**: Resume historical data logging.
- **`HMIRuntime.Logging.SysFct.StopTagLogging(logName)`**: Pause historical data logging.
- **`HMIRuntime.Logging.SysFct.ExportTagLog(logName, filePath, timeRange, format)`**: Export tag log to CSV.
- **`HMIRuntime.Logging.SysFct.RestoreTagLog(logName, filePath)`**: Restore historical tag segment.
- **`HMIRuntime.Logging.SysFct.WriteManualValue(logName, tagName, timestamp, value)`**: Insert manual audit/lab value.

### 5.6 User Management & Security (`HMIRuntime.UserManagement.SysFct.*`)
- **`HMIRuntime.UserManagement.SysFct.LogOff()`**: Log off active user.
- **`HMIRuntime.UserManagement.SysFct.ShowLogonDialog()`**: Display login credential dialog.
- **`HMIRuntime.UserManagement.SysFct.ChangePassword(userName, oldPwd, newPwd)`**: Change password.

### 5.7 Diagnostics, Programs & Connections
- **`HMIRuntime.UI.SysFct.StartProgram(programPath, parameters)`**: Launch external process on PC runtime.
- **`HMIRuntime.UI.SysFct.ShowSoftwareVersion()`**: Display runtime build and version dialog.
- **`HMIRuntime.UI.SysFct.ShowControlPanel()`**: Open SIMATIC Control Panel.
- **`HMIRuntime.Connections.SysFct.ChangeConnection(connectionName, ipAddress, slot, rack)`**: Re-route PLC connection.

---

## 6. Prompt Engineering Rules for AI Agents

To get the highest quality results when prompting agents equipped with this skill:
1. **Specify Exact Target & Scope**: Include HMI software name (`HMI_RT_1`), Screen (`Screen_2`), and CPU (`PLC_1`).
2. **State & Action Clarity**: Differentiate between toggle (`InvertBitInTag`), momentary set (`SetBitInTag`), or value writing (`SetTagValue`).
3. **Preserve Over Recreate**: For modifications, explicitly instruct: *"Modify existing items in-place using `hmi_update_button_scripts`; do not delete screen items."*
4. **Single-Object Dynamization**: Use `ConditionType.Singlebit` for Boolean status tags and `ConditionType.Range` for numeric ranges on `BackColor`, rather than overlapping visibility objects.
5. **Always Enforce Verification**: Instruct the agent to run `hmi_compile` or `tia_compile` and verify that the operation finishes with 0 errors.

---

## 7. Virtual Controller Simulation & Testing (S7-PLCSIM Advanced)

The MCP server integrates directly with **Siemens S7-PLCSIM Advanced API V8.0** (`Siemens.Simatic.Simulation.Runtime.Api.x64.dll`), allowing agents to validate PLC logic in live memory without physical hardware:

Only S7-PLCSIM Advanced V8.0 or newer is supported (S7-1500 and ET 200SP). Standard S7-PLCSIM, and therefore S7-1200 simulation, cannot be driven by the `plcsim_*` tools. `plcsim_list_instances` returns an object: check `runtimeAvailable` and `message`.

### 7.1 Key Tools
- **Connection & State**: `plcsim_list_instances`, `plcsim_connect`, `plcsim_disconnect`, `plcsim_get_status`, `plcsim_set_operating_state`.
- **Clock Scaling**: `plcsim_set_scale_factor` (e.g. `2.0` = 2x speed) for rapid automated testing.
- **Live Memory I/O**: `plcsim_read_tag`, `plcsim_write_tag`, `plcsim_read_tags`, `plcsim_write_tags` with zero COM overhead.
- **Automated Sequence Testing**: `plcsim_run_mixing_sequence` runs end-to-end recipe tests, captures 80ms telemetry, and generates validation reports.

### 7.2 Dedicated Verification Skill
For executing automated multi-parameter batch testing, recipe matrix comparison, and verification report artifact generation, use the companion skill:
👉 [`siemens-plcsim-sequence-tester` skill](../siemens-plcsim-sequence-tester/SKILL.md)
