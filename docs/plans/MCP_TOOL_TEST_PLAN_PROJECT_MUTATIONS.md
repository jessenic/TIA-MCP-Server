# Project Mutation Tools: Test Plan and API Boundaries

This plan covers the bounded Milestone 4 project-level write work package,
including the registered `tia_create_project` handler and schema. It does not
claim live TIA Portal verification.

## Local API verification

The bundled `src/lib/Siemens.Engineering.Base.dll` reports assembly version
`21.0.0.0`. Its metadata exposes:

- `TiaPortal.Projects` as `ProjectComposition`.
- `ProjectComposition.Create(DirectoryInfo, string)` returning `Project`.
- `Project.Path` and `Project.Name` for identifying the returned project.
- `MultilingualText.Items` as an item composition, and
  `MultilingualTextItem.Language` (read-only) and `Text` (read/write).
- `TextCategory.Identifier` and `TextCategory.Name`.

The Create overload is the only project-creation overload verified from the
bundled V21 API. It exposes no template, version, or overwrite option. The
implementation therefore accepts only an existing empty destination directory
and a plain project name, checks that the returned path is inside that directory,
and does not attempt rollback. If the call throws after invocation, its result
reports that side effects may have occurred.

`write_project_texts` is deferred. The public metadata does not establish how to
create a text entry, how a key maps to the text/category object model, or which
language/category attributes are valid creation arguments. `TextCategory` has no
typed text-entry collection, and `MultilingualText` only exposes its items
composition; the generic composition `Create` signature does not reveal the
creation-argument contract. Guessing those details could overwrite or misplace
project content, so this operation must wait for a verified V21 creation
contract.

## Registered handler signature

The handler and strict JSON schema are registered in `ToolRegistry.cs` and
`schemas/tia_create_project.json`.

### `tia_create_project`

```json
{
  "destination_location": "C:\\TIA\\Disposable\\NewProject",
  "project_name": "NewProject"
}
```

Manager signature:

```csharp
ProjectCreationResult CreateProject(string destinationLocation, string projectName)
```

The destination must be an existing, empty directory. Existing/non-empty
locations are rejected; there is no overwrite mode. The connected TIA Portal
V21 instance supplies the project version, and this API overload provides no
template/version selection. The result exposes the path returned by TIA and
distinguishes known partial results from an unknown outcome after an exception.

### `write_project_texts` (deferred)

Proposed handler shape after API verification:

```json
{
  "project_path": "C:\\TIA\\Disposable\\Existing\\Existing.ap21",
  "entries": [
    { "key": "Status.Ready", "category": "General", "language": "en-US", "value": "Ready" }
  ],
  "on_conflict": "reject"
}
```

Proposed manager signature:

```csharp
ProjectTextWriteResult WriteProjectTexts(
    string projectPath,
    IReadOnlyList<ProjectTextWriteEntry> entries,
    ProjectTextConflictBehavior onConflict)
```

`project_path` must resolve to one exact open project. Conflict behavior must be
explicit (`reject` or `overwrite`); omitted behavior must never silently replace
an entry. The entry/category/language contract must be revised to match the
verified V21 API before a handler is added.

## Case-by-case tests

### `create_project`

| Case | Setup / action | Expected result |
|---|---|---|
| C1 — create in disposable empty directory | Call `tia_get_status` first; connect to the intended TIA V21 instance if needed, then create under a new, empty disposable location with a valid name. | Success; returned `Project.Name` matches the request and returned path is within the requested location. Confirm the manager's current project info now reports the created project while the TIA connection remains attached. |
| C2 — persisted identity | After C1, inspect the returned path and reopen that exact project in a disposable test instance. | The created project is present and opens; record actual path and project name. |
| C3 — reject non-empty location | Put a sentinel file or directory in the location, then call create. | Validation fails before calling `Projects.Create`; sentinel remains unchanged. |
| C4 — reject existing project destination | Pre-create a disposable project or any content in the location, then call create. | Validation fails before calling `Projects.Create`; existing project and files are unchanged. |
| C5 — invalid name | Try empty/whitespace, path separators, invalid filename characters, trailing dot/space, and `.ap21` suffix. | Validation fails before API invocation; destination remains unchanged. |
| C6 — missing location | Supply a nonexistent location or a file path. | Validation fails before API invocation; no path is created. |
| C7 — no TIA connection | Disconnect the test instance and call create with otherwise valid arguments. | Operation fails before mutation with a connection requirement. |
| C8 — API failure/partial outcome | In an isolated disposable environment, induce a TIA-side creation failure after invocation. | Result marks `Partial` and `MutationOutcomeUnknown`; message instructs inspection before retry; no automatic deletion/rollback occurs. |
| C9 — returned path mismatch | Use a controlled test double if available, or record the result of a disposable live test where the returned path/name differs from expectations. | Result reports a known partial creation with the returned identity/path; it does not claim success or roll back. |

### `write_project_texts` (after API contract is verified)

| Case | Setup / action | Expected result |
|---|---|---|
| W1 — create new entry | In a disposable project, write one known-valid category/key/language/value with `on_conflict: reject`. | Created status; subsequent project-text read returns the exact value and language. |
| W2 — duplicate keys in request | Submit duplicate category/key/language identities in one batch. | Entire request rejected before mutation. |
| W3 — existing conflict, reject | Submit an entry that already exists with `on_conflict: reject`. | Conflict reported; existing value unchanged and no other entries committed. |
| W4 — existing conflict, overwrite | Submit one existing and one new entry with explicit overwrite. | Per-entry created/updated status; read-back confirms both values. |
| W5 — invalid category/language/value | Submit an unavailable category, unsupported language, empty key, or over-limit value. | Validation fails before mutation, using limits and language rules established by V21 API metadata/live verification. |
| W6 — API failure within a batch | In a disposable project, arrange for a later item to fail after an earlier item was accepted. | Result lists committed and uncommitted identities and marks partial completion; no atomicity is claimed unless separately proven. |
| W7 — no project / ambiguous project | Use a missing project path and then a path resolving to multiple open projects. | Both are rejected before writes; neither project is changed. |

Do not run W1–W7 until the V21 text-entry creation and conflict contracts have
been verified.

## Validation status

The implementation work package was compile-validated with the repository
Release build only. A later live `tia_create_project` run is recorded in the
[runbook](../MCP_TOOL_LIVE_TEST_RUNBOOK.md); project-text writes remain
deferred.
