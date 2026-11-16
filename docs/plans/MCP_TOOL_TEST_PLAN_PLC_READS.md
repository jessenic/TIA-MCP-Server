# PLC Read Slice Test Plan

These are live cases for a disposable TIA Portal V21 project. The tool operations are read-only; compare the project state before and after the cases that assert no mutation. Runtime verification is required before claiming that TIA exposes each value for a particular project.

## `tia_get_plc_tag_info`

Registered handler signature:
`GetPlcTagInfo(string? plcName, string tableName, string tagName, string? groupPath = null)`

| Case | Setup / input | Expected result |
|---|---|---|
| Tag metadata in root table | One PLC; root table contains one tag; supply exact `plc_name`, `table_name`, and `tag_name`. | One record with exact PLC/tag/table names, data type, logical address, comment, empty group path, and declared `MetadataNotReturned` fields. |
| Tag metadata in nested group | Tag table exists under `Motors/Line1`; specify exact table and `group_path`. | One record identifying `Motors/Line1` with the same tag metadata. |
| Omitted PLC selector | One PLC only; omit `plc_name`. | Same record as the explicit-PLC case. |
| Multiple PLCs without selector | Project contains two PLCs with identical table/tag names; omit `plc_name`. | Reject as an ambiguous PLC selection; do not return either PLC's data. |
| Duplicate table name | Two groups contain a table with the same exact name; omit `group_path`. | Reject as ambiguous. |
| Exact duplicate-table disambiguation | Repeat with the correct exact `group_path`. | Read only the tag in that selected table. |
| Case-sensitive exact tag lookup | Change the case of `tag_name`. | Not-found result; no fallback to a similarly named tag. |
| Missing tag or table | Supply a nonexistent tag or table. | Not-found error naming the target and scope. |
| Invalid group path | Supply whitespace-only `group_path` or a path that does not contain the table. | Reject invalid or not-found scope; no broad search fallback. |
| Read-only invariant | Record project `IsModified` state and tag table contents, perform successful and unsuccessful reads. | No tag, table, or project-content mutation; pre-existing modified state is not changed by the operation. |

## `tia_read_user_constant`

Registered handler signature:
`ReadUserConstant(string? plcName, string constantName, string? tableName = null, string? groupPath = null)`

| Case | Setup / input | Expected result |
|---|---|---|
| Read constant in root table | One PLC; root table contains one constant; supply exact `constant_name` and `table_name`. | One record with declared type, literal value, table name, empty group path, and explicit `MetadataNotReturned` list. |
| Read constant in nested group | Constant is in a table under `Motors/Line1`; provide table and exact `group_path`. | Returns only that constant and its exact group/table identity. |
| Unique name without table | Constant name occurs once in all tables of the selected PLC; omit `table_name`. | Returns that single record with its owning table and group. |
| Root group scope | Omit `table_name`, specify `group_path` as an empty string, and create the matching constant only in a root table. | Searches only root-group tables; a same-named constant in a nested group does not create ambiguity. |
| Name scoped to a group | Omit `table_name`, provide exact `group_path`, and ensure the constant occurs once in that group subtree. | Returns the unique match within that group path. |
| Ambiguous name without table | Same exact constant name occurs in multiple tables; omit `table_name`. | Reject as ambiguous and require a table/group scope. |
| Exact table disambiguation | Repeat the duplicate-name setup with one exact `table_name` and `group_path` if needed. | Returns only the matching constant from the selected table. |
| Multiple PLCs without selector | Two PLCs contain the same constant name; omit `plc_name`. | Reject as ambiguous PLC selection. |
| Missing constant/table/group | Supply an unknown name, table, or group path. | Not-found error; no unscoped substitute is returned. |
| Exact-name behavior | Change constant-name case. | Not-found; no case-insensitive match. |
| Read-only invariant | Record project `IsModified` state and constant literal; read success and failure cases. | Constant value and project state remain unchanged. |

## V21 API verification and scope limits

The implementation uses only members present in the bundled-reference-backed project sources: `PlcTag.Name`, `DataTypeName`, and `LogicalAddress`; `PlcTagTable.Tags`; and `PlcTagTable.UserConstants` with `PlcUserConstant.Name`, `DataTypeName`, and `Value`. Existing project code already reads tag comments through `GetAttribute("Comment")`; this slice follows the same safe-read helper. The required Release build checks compile-time API availability but does not replace the live cases above.

Neither operation claims stable IDs or revisions. Tag-ID-based lookup is deferred because no stable tag ID is established. User-constant comment/other metadata is deferred because an exact supported member was not established. No writes, deletes, or PLC online/runtime reads are included.
