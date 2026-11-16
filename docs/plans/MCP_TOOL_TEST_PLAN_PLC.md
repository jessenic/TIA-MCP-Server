# PLC Metadata Discovery Test Plan

This plan covers the read-only methods added in `TiaManager.PlcMetadata.cs`.
The methods do not save, compile, export, or mutate the project. They report
the PLC name and scoped object name/group path; they do not synthesize object
IDs or revision numbers.

## Implemented operation contracts

All selectors use exact ordinal names. `plc_name` may be omitted only when
exactly one PLC exists in the open project. When a name is duplicated within
that PLC, `group_path` is required to identify the object. An empty required
object name is invalid. No selector match is a missing-target error; multiple
matches are an ambiguity error.
An omitted `plc_name` resolves only when exactly one PLC exists; zero PLCs are
missing and multiple PLCs are ambiguous. A supplied empty `group_path` selects
the root group exactly, while omission leaves the group unspecified. A supplied
blank/whitespace `plc_name` or whitespace-only `group_path` is invalid.

| Manager method | Returned metadata | Explicitly not returned |
|---|---|---|
| `GetPlcBlockInfo(plcName, blockName, groupPath)` | PLC name, block name/number/kind/language, group path, `IsConsistent` and `IsKnowHowProtected` attribute read statuses/values | Stable ID, interface summary, version, compile state |
| `GetPlcBlockAttributes(plcName, blockName, groupPath)` | The same two known attributes, each marked `available` when its V21 read succeeds | Arbitrary attribute enumeration, writability, revision |
| `GetPlcTagTableInfo(plcName, tableName, groupPath)` | PLC name, table name, group path, contained-tag count | Stable ID and tag-table attributes |
| `GetPlcTypeInfo(plcName, typeName, groupPath)` | PLC name, type name, group path, `IsConsistent` attribute read status/value | Stable ID, version, field summary, dependencies |
| `GetPlcTypeAttributes(plcName, typeName, groupPath)` | The known `IsConsistent` attribute, marked `available` when its V21 read succeeds | Arbitrary attribute enumeration, writability, revision |

For attribute reads, each requested attribute is marked `available` only after
the V21 read succeeds. An unsupported attribute or Openness read exception
fails the tool call rather than returning a partial success-shaped record.
Exceptions during project/PLC/object resolution are likewise surfaced.

## Case-by-case checks

Run each case against a separate disposable TIA Portal V21 project copy. Use
the listed manager method directly or its registered MCP tool. JSON field
names below correspond to the returned DTOs and normal JSON serialization.

Before the per-tool cases, verify that a closed/missing project fails
explicitly, an omitted `plc_name` fails with no PLC or with multiple PLCs,
and an exact `plc_name` selects the intended PLC when the project has more
than one. Verify that a supplied whitespace-only PLC/group selector is
rejected rather than treated as omitted.

### `GetPlcBlockInfo` / `tia_get_block_info`

1. **Success and result shape:** Select a PLC with one known block. Call with
   `plcName`, exact `blockName`, and its `groupPath` (empty for the root group).
   Verify `PlcName`, `Name`, `Number`, `BlockType`, `ProgrammingLanguage`, and
   `GroupPath`; `Attributes` contains the two named reads with `Status` set to
   `available` and the value in `Value`; `MetadataNotReturned` lists the omitted
   metadata. Verify the project remains unmodified.
2. **Missing:** Use a nonexistent exact block name. Expect an
   `ArgumentException`/tool error stating that the block was not found; no
   success-shaped empty object.
3. **Ambiguous:** In the disposable copy, create two same-named blocks in
   different block folders and omit `groupPath`. Expect an ambiguity error.
   Repeat with each exact path and verify each unique target resolves.
4. **Invalid:** Pass an empty/whitespace block name. Expect an argument error.
   A nonexistent `groupPath` also resolves as missing, not as a partial match.
5. **Unsupported/API failure:** If a known attribute cannot be read for the
   selected block kind, verify the tool returns an error and no partial
   metadata record. If project/PLC traversal itself throws, verify the same
   explicit error behavior rather than a fabricated record.

### `GetPlcBlockAttributes` / `tia_get_block_attributes`

1. **Success and result shape:** Call for a uniquely selected block. Verify the
   result has `PlcName`, `ObjectName`, `GroupPath`, and exactly the known
   `IsConsistent` and `IsKnowHowProtected` reads; each has `Name`, `Status`,
   and `Value`. No revision or mutability fields are present.
2. **Missing:** Use a nonexistent block; expect a missing-target tool error.
3. **Ambiguous:** Use duplicate block names without `groupPath`; expect an
   ambiguity error. Add the exact path and verify resolution.
4. **Invalid:** Use a blank block name; expect an argument error.
5. **Unsupported/API failure:** For a valid block with an unreadable known
   attribute, verify the tool returns an error rather than an `unavailable`
   placeholder or empty attribute list.

### `GetPlcTagTableInfo` / `tia_get_tag_table_info`

1. **Success and result shape:** Select a table with known tags. Verify
   `PlcName`, `Name`, `GroupPath`, `ContainedTagCount`, and
   `MetadataNotReturned` (`stableId`, `tableAttributes`). Check the count
   against the table's tag collection. A valid empty table must return count
   zero.
2. **Missing:** Use an unknown exact table name; expect a missing-target error.
3. **Ambiguous:** Create same-named tables in separate table groups, omit the
   path, and expect an ambiguity error. Verify exact group paths resolve
   separately.
4. **Invalid:** Use a blank table name; expect an argument error. A supplied
   nonexistent group path is a missing-target result.
5. **Unsupported/API failure:** If reading the selected table's tag
   collection fails, verify the operation surfaces the API error; it must not
   report a zero count as a fallback.

### `GetPlcTypeInfo` / `tia_get_type_info`

1. **Success and result shape:** Select a PLC type by exact name and path.
   Verify `PlcName`, `Name`, `GroupPath`, the `IsConsistent` read result, and
   `MetadataNotReturned` (`stableId`, `version`, `fieldSummary`,
   `dependencies`). Verify no XML export or project modification occurs.
2. **Missing:** Use an unknown exact type name; expect a missing-target error.
3. **Ambiguous:** Create duplicate type names in separate type groups, omit
   the path, and expect an ambiguity error. Verify a precise path resolves.
4. **Invalid:** Use a blank type name; expect an argument error. A supplied
   nonexistent group path resolves as missing.
5. **Unsupported/API failure:** Verify an unreadable `IsConsistent` attribute
   surfaces as a tool error; failures in type-group traversal must not become
   fabricated metadata.

### `GetPlcTypeAttributes` / `tia_get_type_attributes`

1. **Success and result shape:** Query a unique type. Verify
   `PlcName`, `ObjectName`, `GroupPath`, and one `IsConsistent` attribute with
   `Status` and `Value`; no revision or writability is implied.
2. **Missing:** Use an unknown type name; expect a missing-target error.
3. **Ambiguous:** Use duplicate type names without a group path; expect an
   ambiguity error, then verify exact path selection.
4. **Invalid:** Use a blank type name; expect an argument error.
5. **Unsupported/API failure:** Verify an unreadable consistency attribute
   is returned as a tool error and is not converted to a placeholder or empty
   result.

## Live disposable-project procedure

1. On a Windows x64 test machine, install licensed TIA Portal V21 with the
   Openness option and its V21 PublicAPI .NET Framework 4.8 assemblies.
   Create a disposable project copy with one PLC and representative OB/FB/FC/
   DB blocks, a UDT, and tag tables. Add duplicate names in separate user
   groups only for the ambiguity cases above.
2. Start TIA Portal, open the copy, and connect the MCP server to that running
   instance. Confirm the selected project and PLC before running any case.
3. Run each success/failure case above and capture the returned JSON or tool
   error. If a known attribute is unavailable, retain the explicit tool error;
   do not expect a partial result or infer the missing value.
4. Compare block/table/type group paths and table tag counts with the TIA
   Portal project tree. Check project modified state before and after each
   query; none of these methods should change it.
5. Close the disposable project without saving any unrelated setup changes.

No live TIA Portal or disposable-project execution was performed during
implementation. Subsequent selected checks are recorded in the
[runbook](../MCP_TOOL_LIVE_TEST_RUNBOOK.md); the remaining cases still require
live verification.

## Deferred operations

- **DB member attributes:** Deferred. The bundled Step7 assembly includes
  interface/member-related symbols, but this slice did not verify a usable
  V21 member traversal contract and the exact supported member attribute and
  datatype semantics. Do not infer member IDs or fabricate a revision.
- **Tag-table attributes:** Deferred. The verified table surface used here
  provides table names and tag collections, but no verified table attribute
  allowlist/mutability contract was established.
- **Type attribute enumeration:** Only the existing `IsConsistent` attribute
  read is exposed. Enumeration, allowed values, and mutability are deferred
  because no verified attribute-descriptor contract was established.
- **PLC text-list discovery:** Deferred. The bundled assembly contains alarm
  text-list-related symbols, but this slice did not verify a project-level
  collection/selector and result semantics for PLC text lists. The existing
  HMI text-list API is not evidence for the PLC API.
- **PLC project/device discovery:** Existing project/device/PLC discovery
  methods are reused; this slice does not add project/device discovery tools.

## Registered MCP tools

Each tool is registered in `ToolRegistry`; handlers serialize the manager DTO
with `CallToolResult.Json` and return manager exceptions as tool errors.

| MCP tool | JSON input schema | Handler |
|---|---|---|
| `tia_get_block_info` | Required `block_name` (string); optional `plc_name` (string) and `group_path` (string). If `plc_name` is omitted, the current project must contain exactly one PLC. | `TiaManager.Instance.GetPlcBlockInfo(plcName, blockName, groupPath)` |
| `tia_get_block_attributes` | Required `block_name` (string); optional `plc_name` and `group_path` (strings), with the same PLC-selection rule. | `TiaManager.Instance.GetPlcBlockAttributes(plcName, blockName, groupPath)` |
| `tia_get_tag_table_info` | Required `table_name` (string); optional `plc_name` and `group_path` (strings), with the same PLC-selection rule. | `TiaManager.Instance.GetPlcTagTableInfo(plcName, tableName, groupPath)` |
| `tia_get_type_info` | Required `type_name` (string); optional `plc_name` and `group_path` (strings), with the same PLC-selection rule. | `TiaManager.Instance.GetPlcTypeInfo(plcName, typeName, groupPath)` |
| `tia_get_type_attributes` | Required `type_name` (string); optional `plc_name` and `group_path` (strings), with the same PLC-selection rule. | `TiaManager.Instance.GetPlcTypeAttributes(plcName, typeName, groupPath)` |
