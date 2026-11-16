# PLC block-group and tag-table-attribute slice — test plan

## Scope and API boundary

This slice implements two operations in `TiaManager.PlcGroupAttributes.cs`.
It does not register tools or change the shared registry/documentation.

The bundled `src/lib/Siemens.Engineering.Step7.dll` metadata confirms
`PlcBlockGroup.Groups.Create(string)`, `PlcBlockGroup.Groups`, and
`PlcBlockUserGroup.Name`/`Parent`. `CreateBlockGroup` uses the existing exact
PLC resolver and mutation name/path validation; group path segments are
resolved with ordinal exact matching because the existing general block-group
finder is case-insensitive and does not provide the required exact semantics.
The method reads the returned group back from the selected parent before
reporting success.

The same bundled API exposes `PlcTagTable.GetAttributeInfos()`,
`GetAttribute(string)`, and `EngineeringAttributeInfo` fields `Name`,
`AccessMode`, `CreateRelevance`, and `SupportedTypes`. Attribute values are
read only when the API says the access mode is `Read` or `ReadWrite`;
write-only attributes are returned with `ValueStatus=not_readable`. The
implementation reports only API-provided attributes and mutability metadata.
The contract used here exposes no stable tag-table ID or revision, so neither
is invented. Both operations address the currently open project; no project
switching or creation is performed.

## Proposed parent integration signatures

The methods are public on the `TiaManager` partial class, ready for handlers:

```csharp
TiaManager.Instance.CreateBlockGroup(
    string? plcName, string groupName, string? parentGroupPath = null);

TiaManager.Instance.GetTagTableAttributes(
    string? plcName, string tableName, string? groupPath = null);
```

Suggested strict tool names and schema files:

- `tia_create_block_group` — `schemas/tia_create_block_group.json`
- `tia_get_tag_table_attributes` — `schemas/tia_get_tag_table_attributes.json`

The create input intentionally has no description field: the verified group
composition contract provides `Create(string)` only. The plan's requested
description parameter is deferred until a V21 creation overload or supported
description attribute is established. The result reports the group's exact
name and path, not an ID: no stable group ID member is established.

## Case-by-case live verification

Run on a disposable V21 project after confirming the connected instance and
active project. Record project identity, PLC name, TIA build, and state before
each case. No live TIA operation was run during implementation; subsequent
selected checks are recorded in the
[runbook](../MCP_TOOL_LIVE_TEST_RUNBOOK.md).

### `tia_create_block_group`

| Case | Setup / input | Expected result and checks |
|---|---|---|
| Root creation | One PLC; choose a unique `group_name`; omit `parent_group_path`. | `Success=true`, `Status=created`, exact group name and root path; exact-name read-back from root confirms the created group. |
| Nested exact parent | Existing `Motors/Line1`; pass exactly that `parent_group_path` and a unique name. | The group is created only under that parent; returned `TargetPath` is `Motors/Line1/<name>`. |
| Exact target selection | Create same-named parent groups in distinct branches, or case-differing parent names where the project permits. Pass one exact path. | Only that exact, ordinal-matching parent is selected; no first-match or case-insensitive fallback. |
| Multiple PLCs | Two PLC software objects; omit `plc_name`, then repeat with each exact name. | Omission is rejected as ambiguous; explicit exact selection creates only under the selected PLC. |
| Same-scope collision | A block group of the same name already exists under the selected parent; repeat with a case-only variant. | `collision`, no second group, and no existing group changed. Same group name in a different parent does not block creation. |
| Malformed name/path | Blank or whitespace group name, slash/backslash/control characters in name, leading/trailing slash, repeated slash, path edge whitespace, or backslash path separator. | `invalid_input`; no create call and unchanged parent group count. |
| Missing parent | Use a syntactically valid but nonexistent exact parent path. | `not_found`; no group is created elsewhere or at root. |
| API failure | On a disposable target or controlled API test, cause `Groups.Create` to throw an engineering/COM error. | `api_failure`; `Partial` indicates mutation call started, so inspect by exact read-back before retrying. |
| Read-back failure | Use a controlled test seam/API test where create returns but exact-name read-back is absent or non-unique. | `verification_failed`, `Partial=true`; do not infer rollback. |
| Persisted result | Successfully create root and nested groups, save, close, and reopen. | Exact hierarchy remains; no unrelated groups or blocks are moved. |

### `tia_get_tag_table_attributes`

| Case | Setup / input | Expected result and checks |
|---|---|---|
| Read attributes | Select one existing exact tag table. | Returns the exact PLC/table/group identity and only attributes reported by `GetAttributeInfos`; each includes the API's access mode, create relevance, supported types, value or `not_readable` status. |
| Exact duplicate-name selection | Put same-named tables under distinct groups; omit `group_path`, then supply each exact group path. | Unscoped duplicate is rejected as ambiguous; exact path returns only that table's attributes. |
| PLC selection | Multiple PLCs; omit `plc_name`, then supply exact names. | Omission is rejected when ambiguous; selected PLC identity matches the exact request. |
| Case-sensitive exact lookup | Change the case of `table_name` or `group_path`. | Not found; no fallback to another case variant. |
| Missing table/group/PLC | Supply an unknown exact table, path, or PLC name. | Not-found result/error; no unrelated target is returned. |
| Malformed selectors | Supply whitespace-only group path or a path that does not contain the table. | Reject invalid or not-found scope; no broad search fallback. |
| Attribute read failure | Cause `GetAttributeInfos` or a readable attribute's `GetAttribute` call to fail on a disposable target/test double. | Operation fails without changing project content; no partial values are presented as a complete result. |
| No-mutation invariant | Record project `IsModified`, table metadata, and attribute values before valid and invalid calls. | Values, table count, and project content remain unchanged; read-only access must not mark the project modified. |
| No revision/ID claim | Inspect response fields. | `MetadataNotReturned` reports `stableId` and `revision`; neither is fabricated. |

## Explicit deferrals

- Group descriptions are not accepted or written; no matching `Create` overload
  or supported description property is claimed.
- Stable IDs and revision values are not returned for block groups or tag
  tables.
- `set_tag_table_attributes`, block-group rename/move/delete, and any
  operations whose writable member contract or reference behavior is not
  verified are deferred.
- Live TIA Portal behavior, persistence, and project dirty-state behavior still
  require the case-by-case tests above.
