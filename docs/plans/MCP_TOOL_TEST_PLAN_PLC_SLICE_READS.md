# Bounded PLC read slice — case-by-case test plan

## Scope and V21 API evidence

This package adds `GetDbMemberAttributes` to an independent `TiaManager`
partial source file and provides one strict schema. It does not change the
central registry, shared documentation, version metadata, or existing files.

The bundled `src/lib/Siemens.Engineering.Step7.dll` metadata exposes
`GlobalDB`, `PlcBlockInterface.Members`, and `Member.Name`. The implementation
resolves the interface through the DB's reported composition metadata by its
exact V21 type, then reads `Members`. `Member` exposes the inherited
engineering-object attribute enumeration and read APIs.
`EngineeringAttributeInfo` exposes the attribute name, access mode, create
relevance, and supported types. The implementation uses these exact members
only, compares PLC/DB/member names ordinally, and returns the complete
attribute-info enumeration for one direct member.

The bounded operation is:

```csharp
TiaManager.Instance.GetDbMemberAttributes(
    string? plcName,
    string dbName,
    string memberName,
    string? groupPath = null);
```

It applies only to a `GlobalDB` and its direct interface members. It reads
values only for attributes whose V21 access mode is `Read` or `ReadWrite`.
It does not attempt writes, infer nested-member composition names, or guess a
datatype attribute. A dedicated datatype field, revision, stable ID, and
nested member paths are explicitly not returned. If the member exposes a
datatype-related attribute through `GetAttributeInfos`, it is included as a
raw API-reported attribute without interpretation.

## Case-by-case live verification

Run on a disposable V21 project after recording connected TIA instance,
project path, PLC, block, and project dirty state. This implementation was
compile-checked only during implementation; subsequent selected checks are
recorded in the [runbook](../MCP_TOOL_LIVE_TEST_RUNBOOK.md).

### `tia_get_db_member_attributes`

| Case | Setup / input | Expected result and checks |
|---|---|---|
| Exact PLC, DB, and member | One PLC with one global DB containing a direct member; provide exact names. | One result identifies the exact PLC, DB, group, and member. Every API-reported member attribute is present once with its access mode, create relevance, supported types, and readable value or `not_readable` status. |
| PLC ambiguity | Project has multiple PLCs; omit `plc_name`, then repeat with each exact PLC name. | Omission fails as ambiguous; exact PLC selections return only blocks owned by the requested PLC. |
| DB ambiguity and group selection | Two DBs have the same exact name in different block groups. Omit `group_path`, then provide each exact path. | Unscoped lookup fails as ambiguous; an exact group path selects only its DB. No first-match fallback. |
| Member exactness | Request a direct member by exact name, then change case or whitespace. | Exact name resolves; case variant and malformed whitespace input do not resolve another member. Matching is ordinal and direct-only. |
| Nested path | Request a nested path that does not exactly match a direct member name. | Not found; no traversal occurs. This operation does not claim nested-path support. |
| Malformed selectors | Supply whitespace-only DB/member names, whitespace-only `plc_name`, or whitespace-only/nonexistent `group_path`. | Invalid selectors are rejected; a well-formed but unknown exact path is not found. No broader fallback occurs. |
| Ambiguous member | Use a controlled test double that reports two direct members with the same exact name. | The operation rejects ambiguity instead of returning either member. |
| Wrong block kind | Select an FB, FC, instance DB, or other non-global-DB block by exact name. | Rejected as unsupported; no interface is inferred from another block kind. |
| Missing PLC, DB, or member | Supply an unknown exact PLC, DB, group path, or direct member name. | Not found; no unrelated PLC, block, or similarly named member is returned. |
| Unsupported metadata | Use a DB/member whose V21 interface omits a dedicated datatype, revision, or ID. | Raw reported attribute details are returned; `MetadataNotReturned` and the completeness note identify omitted dedicated fields. Nothing is fabricated. |
| Write-only attribute | Use a member with an attribute whose metadata says `Write`. | Attribute metadata is present with `ValueStatus=not_readable`; no `GetAttribute` read is attempted for it. |
| Empty attribute enumeration | Use an API test double or supported member returning zero `GetAttributeInfos` items. | Successful result with an empty `Attributes` list and direct-member completeness note; no guessed fields are injected. |
| Enumeration completeness | Select a member with multiple known V21 attributes; independently enumerate its `GetAttributeInfos` in a controlled harness and compare names/counts. | Response contains every reported attribute exactly once. Readable values are retrieved; unreadable items remain represented with status. |
| API errors | Force composition lookup, `GetAttributeInfos`, or a readable `GetAttribute` call to fail in a controlled test. | The operation fails visibly; no partial response is presented as complete. No mutation or fallback to guessed attributes occurs. |
| Read-only invariant | Record project `IsModified`, DB interface/member identity, values, and group/block counts. Call valid and invalid inputs. | No project content or value changes; no create/set/delete/import/save API is invoked; project dirty state remains unchanged. |

## `get_plc_textlists` — explicitly deferred

No schema, handler, or source method is included for this operation. The
bundled V21 API exposes `PlcSoftware.PlcAlarmTextlistGroup`, with separate
system and user alarm-textlist compositions. These are specifically alarm
textlists, not verified evidence of a complete PLC-wide text-list collection.
The available textlist type metadata does not establish a complete, stable
contract for the requested language enumeration, entry counts, optional
filtering, or paging semantics. Returning only alarm-list names as a complete
PLC text-list inventory would therefore be misleading.

Before reconsidering this operation, verify a V21 API surface that provides
the complete intended PLC text-list domain, stable identifiers/names, language
and entry enumeration/counts, and reliable full-enumeration semantics. Then
test exact PLC selection, malformed/missing/ambiguous targets, unsupported
metadata, API errors, empty results, paging/filter completeness, and read-only
invariants before adding a schema or handler.
