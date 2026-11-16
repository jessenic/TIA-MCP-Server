# HMI Structure and Assets: Milestone 6 Implementation/Test Plan

## Scope and implementation decision

This package adds five in-place, narrowly scoped operations to the partial
`TiaManager` class:

1. Create a screen group.
2. Create a screen inside an existing group.
3. Rename a screen.
4. Rename a screen group.
5. Rename an HMI tag table.

Each operation requires `on_conflict: "fail"`. No overwrite mode is
implemented. Group selectors are exact, slash-separated group-name paths;
`""` means the root composition for operations that allow it. Group names
containing `/` are rejected so a created group always has an unambiguous path.
Screen names are scoped to their containing group. No stable IDs, revisions,
atomicity, or rollback are claimed. The methods mutate existing TIA objects
for renames and create new objects only for the two creation operations.

The implementation is in `src/Tia/TiaManager.HmiStructure.cs`, with registered
handlers in `src/Tools/ToolRegistry.cs` and matching schemas in `schemas/`.

## V21 API evidence

The signatures below were checked against the bundled
`src/lib/Siemens.Engineering.WinCCUnified.dll` metadata (TIA Portal V21 API):

| API type/member | Verified member signature | Use |
|---|---|---|
| `HmiSoftware.ScreenGroups` | property returning `HmiScreenGroupComposition` | Root group collection |
| `HmiScreenGroup.Groups` | property returning `HmiScreenGroupComposition` | Nested group collection |
| `HmiScreenGroup.Screens` | property returning `HmiScreenComposition` | Screens scoped to a group |
| `HmiScreenGroupComposition.Create(string)` | returns `HmiScreenGroup` | Create group in the selected parent composition |
| `HmiScreenGroupComposition.Find(string)` | returns `HmiScreenGroup` | Read-back by exact name |
| `HmiScreenGroup.Name` | `string` getter and setter | Rename existing group in place |
| `HmiSoftware.Screens` | property returning `HmiScreenComposition` | Root screen collection |
| `HmiScreenComposition.Create(string)` | returns `HmiScreen` | Create screen in the selected composition |
| `HmiScreenComposition.Find(string)` | returns `HmiScreen` | Read-back by exact name |
| `HmiScreenBase.Name` | `string` getter and setter | Rename existing screen in place |
| `HmiSoftware.TagTables` | property returning `HmiTagTableComposition` | HMI tag-table collection |
| `HmiTagTableComposition.Create(string)` | returns `HmiTagTable` | Create a table in the selected HMI software |
| `HmiTagTable.Delete()` | `void` | Remove a newly created test table during cleanup |
| `HmiTagTable.Name` | `string` getter and setter | Rename table in place |
| `HmiTagTable.Tags` | property returning the tag composition | Verify tag names remain readable after rename |

The API also exposes screen `Width`, `Height`, and `ScreenItems`; the creation
method applies optional positive dimensions and returns read-back dimensions
and item count. Exact public create/find methods are used; no reflection-based
fallback or guessed property name is used.

## Registered schemas and handler calls

Use the established tool schema builder conventions. For each schema below,
`target_name` is optional only when one HMI target resolves uniquely. In all
cases `on_conflict` is required and its only accepted value is `"fail"`.

| Registered tool | Schema fields | Handler call |
|---|---|---|
| `hmi_create_screen_group` | `group_name` (string, required), `parent_group_path` (string, optional; empty means root), `target_name` (string, optional), `on_conflict` (string enum `["fail"]`, required) | `TiaManager.Instance.CreateHmiScreenGroup(groupName, parentGroupPath, targetName, onConflict)` |
| `hmi_create_screen_under_group` | `parent_group_path` (string, required and non-empty), `screen_name` (string, required), `width`/`height` (positive integers, optional), `target_name` (string, optional), `on_conflict` (string enum `["fail"]`, required) | `TiaManager.Instance.CreateHmiScreenUnderGroup(parentGroupPath, screenName, width, height, targetName, onConflict)` |
| `hmi_create_tag_table` | `table_name` (string, required), `target_name` (string, optional), `on_conflict` (string enum `["fail"]`, required) | `TiaManager.Instance.CreateHmiTagTable(targetName, tableName, onConflict)` |
| `hmi_rename_screen` | `screen_name`, `group_path` (empty means root), `new_name`, `on_conflict` (all required strings except optional `target_name`) | `TiaManager.Instance.RenameHmiScreen(screenName, groupPath, newName, targetName, onConflict)` |
| `hmi_rename_screen_group` | `group_path` (required, non-empty), `new_name`, `on_conflict` (required strings), optional `target_name` | `TiaManager.Instance.RenameHmiScreenGroup(groupPath, newName, targetName, onConflict)` |
| `hmi_rename_tag_table` | `table_name`, `new_name`, `on_conflict` (required strings), optional `target_name` | `TiaManager.Instance.RenameHmiTagTable(tableName, newName, targetName, onConflict)` |

Successful calls return `HmiStructureMutationResult` with `Success=true`,
`Status=created|renamed`, operation, current name, optional prior name and
parent/group path, and operation-relevant dimensions/item count or tag entry
count. Supported API and read-back failures return `Success=false`, an error
status, `Partial=true`, message, and a read-before-retry warning; the tool call
is also marked as an error. It contains no revision or atomicity field.

## Case-by-case validation plan

No live TIA Portal or project tests were run during implementation. Selected
later live checks are recorded in the
[runbook](../MCP_TOOL_LIVE_TEST_RUNBOOK.md). Run the remaining cases against a
disposable V21 project with at least one HMI Unified target. Save a copy before
exercising writes, then read the result in TIA Portal/Openness and compile the
HMI. Record compile diagnostics; do not treat a tool's successful API call as
proof that the project compiles.

### `hmi_create_screen_group`

| Case | Procedure and expected result |
|---|---|
| Success | Create a root group and a nested group with `on_conflict="fail"`. Verify returned paths and use group discovery/read-back to confirm the new hierarchy and parent. Save and compile the disposable HMI. |
| Invalid input | Empty/whitespace or edge-whitespace name, a name containing `/`, malformed parent path (empty segment, leading/trailing slash), or any conflict policy other than `"fail"` must return an argument error without creating a group. |
| Missing/ambiguous target | A missing parent path must fail before creation. An omitted or selector-matched HMI target that resolves to zero/multiple targets must fail without mutation. |
| Conflict/dependency/API failure | Attempt an existing sibling name, including a case-only variant; expect conflict and no overwrite. Simulate a supported Siemens API exception or read-back mismatch; expect `Success=false`, `Status=api_failure|verification_failed`, and `Partial=true`. Inspect before retry; no rollback is claimed. |
| Result/read-back | Assert operation, name, parent path, and complete path. Verify the group is visible in the selected parent collection and remains present after save/reopen and compile. |

### `hmi_create_screen_under_group`

| Case | Procedure and expected result |
|---|---|
| Success | Create in a nested group with dimensions omitted, then with explicit positive dimensions. Verify name, group path, dimensions, and zero initial item count as applicable. Save/reopen and compile. |
| Invalid input | Empty/whitespace or edge-whitespace screen name, zero/negative/fractional width or height, root/empty parent path, malformed group path, or unsupported conflict policy must fail before creating a screen. |
| Missing/ambiguous target | Nonexistent group and zero/multiple matching HMI targets must fail without creating a root-level or alternate-group screen. |
| Conflict/dependency/API failure | Existing name in the selected group (including case-only collision) must fail. A same-named screen in a different group must not be mistaken for a sibling conflict. Supported API/read-back failures return `Success=false`, `Partial=true`, and an inspect-before-retry warning; no rollback is promised. |
| Result/read-back | Assert operation, name, parent group path, width, height, and item count. Confirm exactly one screen exists in the selected composition after save/reopen and that HMI compilation reports its status. |

### `hmi_rename_screen`

| Case | Procedure and expected result |
|---|---|
| Success | Rename a screen in root and in a nested group. Seed it with screen items and bindings first; verify the operation changes the existing screen's name in place, leaves its item count and items intact, and returns old/new names and path. Save/reopen and compile. |
| Invalid input | Empty/whitespace old or new name, malformed path, or unsupported conflict policy must fail without changing the screen name. |
| Missing/ambiguous target | Missing group, missing screen in the selected group, or zero/multiple HMI target resolution must fail. A same-named screen in a different group does not satisfy the exact selected group. |
| Conflict/dependency/API failure | Existing sibling screen name, including case-only collisions, must fail without overwriting it. Inject/API-observe a setter or read-back failure; expect `Success=false`, `Partial=true`, and inspect actual project state because a setter failure is not guaranteed atomic. |
| Result/read-back | Assert old/new names, group path, dimensions, and unchanged item count. Verify the screen and its existing item/tag/script bindings are still present after save/reopen and compile. |

### `hmi_rename_screen_group`

| Case | Procedure and expected result |
|---|---|
| Success | Rename a nested group containing child groups and screens. Verify the new path and that the same contents remain reachable under it. Save/reopen and compile. |
| Invalid input | Empty/whitespace new name, `/` in the new name, empty/root group path, malformed path, or unsupported conflict policy must fail without mutation. |
| Missing/ambiguous target | Missing group path or zero/multiple HMI target resolution must fail. Duplicate group names at a path segment must be reported as ambiguous. |
| Conflict/dependency/API failure | Existing sibling group name, including case-only variant, must fail. Exercise setter/read-back failure reporting; expect `Success=false`, `Partial=true`, and inspect project state because no rollback is claimed. |
| Result/read-back | Assert old/new name, parent path, and resulting group path. Verify child group and screen names remain available beneath the renamed group after save/reopen and compile. |

### `hmi_rename_tag_table`

| Case | Procedure and expected result |
|---|---|
| Success | Create a disposable test table with `hmi_create_tag_table`, add a test tag to it with `hmi_create_tag` using that `table_name`, then rename it. Verify the table is found under the new name and the exact sorted tag-name set remains unchanged. Save/reopen and compile; cleanup may call `HmiTagTable.Delete()` only on the table created by this test. |
| Invalid input | Empty/whitespace old or new name or unsupported conflict policy must fail without mutation. |
| Missing/ambiguous target | Missing exact table name or zero/multiple HMI target resolution must fail. Duplicate matching table names must be reported as ambiguous. |
| Conflict/dependency/API failure | Existing table name, including case-only variant, must fail. Exercise setter/read-back/API failure; expect `Success=false`, `Partial=true`, and inspect the actual table name and tag set because no atomicity/rollback is promised. |
| Result/read-back | Assert old/new names and unchanged entry count. Verify table and its tag names/bindings are retained after save/reopen and compile. |

### `hmi_create_tag_table`

| Case | Procedure and expected result |
|---|---|
| Success | Create a new table in one exact HMI software target. Verify `Status=created`, `Success=true`, `Partial=false`, and exact-name read-back before commit. |
| Invalid input | Empty/whitespace or edge-whitespace name, control characters, `/`, `\`, an inexact target name, or an `on_conflict` value other than `"fail"` (case-insensitive) must fail without creating a table. |
| Conflict/API failure | An existing table name, including a case-only variant, must fail without overwrite. Exercise API/read-back failure; expect `Success=false`, `Partial=true`, and an inspect-before-retry warning without a rollback claim. |
| Cleanup | Call `HmiTagTable.Delete()` only on the test table created by this case; never delete a pre-existing table. |

## Explicitly deferred

* **Screen duplication:** V21 `HmiScreenBase` exposes `Name` and `Delete`, and
  screen compositions expose `Create(string)`/`Find(string)`, but no verified
  clone/duplicate API. Rebuilding children would risk changing item identity,
  layout, bindings, and scripts.
* **Templates:** the bundled Unified assembly did not expose a screen-template
  type or a verified template creation API.
* **Devices and HMI users:** no supported creation API and required
  connection/credential semantics were not verified in the V21 assemblies.
* **Graphic-list creation/update/delete and entries:** `HmiGraphicList` exposes
  `Name`/`Delete`; the composition exposes `Find`, `Import`, and `Export`, but
  no typed `Create` method or entry collection. The untyped import/export
  format, conflict semantics, and reference-impact behavior are not verified.
  Existing graphic-list enumeration remains discovery-only.
* **Resource/dynamic lists:** the API exposes resource-list dynamization
  properties, but no verified collection/entry mutation and read-back workflow
  that validates resource compatibility and consumers.
* **Text-list updates:** text-list compositions expose import/export, but
  entry-level APIs and complete overwrite/read-back behavior were not verified
  here. No new mutation is added.
* **Individual HMI tag updates and faceplate/interface edits:** HMI faceplate
  interface entries expose a generic `Value` setter and `PropertyName`, but
  the API surface available here does not provide enough verified
  interface/type/direction/usage compatibility checks to safely change
  definitions or instance mappings. Only HMI tag-table rename is included.
* **Screen item rename and other property/binding changes:** no new operation
  is added; existing in-place update practices remain authoritative.

The implementation does not delete/recreate existing items, does not overwrite
on conflict, and does not claim that a multi-step API operation is atomic.
