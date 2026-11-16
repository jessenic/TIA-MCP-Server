# HMI MCP Tool Implementation Plan

## Goal

Add the missing WinCC Unified HMI MCP capabilities needed to inspect and update
screen-item properties and event scripts, and to create, edit, and read
expression dynamizations. Preserve existing HMI item identities, layouts,
button scripts, and unrelated dynamizations.

This plan concerns **MCP server source code only**. It does not authorize
connecting to, reading from, modifying, compiling, or saving any live TIA
project.

## Execution plan

### 1. Audit the existing server implementation

- Map tool registration, handlers, schemas, Openness helper patterns, tests,
  and tool-reference documentation.
- Inspect the existing HMI JSON patch operation schema and determine whether
  its guarded operations already cover any requested capabilities.
- Confirm whether `tia_save_project` and a JS type-definition resource/tool
  already exist before proposing additions.
- Identify the server-reported version source and project/assembly version
  metadata.

**Exit condition:** A short inventory of reusable implementation and test
patterns, with a precise list of genuinely missing capabilities.

### 2. Verify supported WinCC Unified V21 APIs

Confirm the supported Openness/runtime surface for:

- Reading and updating `Visible`, `Top`, and other requested item properties.
- Creating and reading event scripts on screen items and screens, including
  supported event names and trigger types.
- Creating, editing, and fully reading expression dynamizations, including
  their applicability to `Visible` and `Top`.
- Reading tag values and accessing item properties from runtime scripts.
- Returning JavaScript type definitions through an MCP resource or tool.

Do not implement an API based only on an unverified example. Record version
constraints and return explicit errors for unsupported operations.

**Exit condition:** Each operation is mapped to a verified API, or explicitly
marked unsupported and excluded from implementation.

### 3. Design minimal tool contracts

Prefer a small set of general tools over screen- or button-specific duplicates.
Evaluate these capabilities against the audit:

- Read one item's complete supported property set and dynamizations.
- Update an allowlisted set of item properties in place.
- Read and write event scripts on supported items and screens.
- Read full dynamization state and create/update expression entries without
  replacing unrelated entries.
- Expose JavaScript type definitions using the server's existing MCP resource
  conventions, if available.

Define argument and result schemas, target identification, validation,
readback behavior, and error reporting before implementation. Keep existing
`hmi_get_screen_items` and `hmi_update_button_scripts` behavior compatible.

**Exit condition:** Contracts are consistent with existing MCP naming and
error-handling patterns and specify how existing state is preserved.

### 4. Implement generic item and event-script operations

- Add or extend the item read/update path using Openness objects in place.
- Support only verified, explicitly allowlisted properties; validate value
  types and report unsupported properties clearly.
- Add event-script read/write support for verified screen and item event types.
- Read back each successful update and return the resulting state.
- Do not delete/recreate items or overwrite button scripts unless explicitly
  requested by a future, separately authorized task.

### 5. Implement expression dynamization operations

- Keep the existing `hmi_set_expression_dynamization` formula operation
  separate. It sets `ValueConverter.Formula` and `IsFormulaSelected`; it does
  not write the Expressions-tab condition/value rows shown in TIA.
- Implemented `hmi_set_expression_dynamization_entries` as a separate
  ordered-entry writer. It sets `ConditionType.Expression`, uses simple
  condition rows with typed values, and leaves formula mode disabled. The
  existing default property value is not modified.
- The readback of `UCS_Btn_1.Top` on `Main_UnitControlSideBar` confirmed the V21
  Openness representation used by the writer: `ConditionType.Expression`,
  simple mapping entries whose condition is expression text, and typed mapped
  values. A live write still needs validation on a disposable HMI item.
- Treat the supplied ordered list as an explicit replacement of the target
  property's condition rows. Require an explicit replacement opt-in when an
  existing tag or expression dynamization would be replaced. Preserve every
  unrelated dynamization, event script, item property, and item identity.
- Validate the row list before mutation: non-empty condition text, supported
  target property and value type, Boolean condition semantics where the API
  exposes them, and valid row ordering. The tool cannot claim Siemens
  expression syntax is valid unless the V21 API provides a validator.
- Apply the table change transactionally, then read the item back and compare
  the ordered conditions and typed values exactly. Include the current
  condition/value entries in `hmi_get_screen_item` readback; keep
  `hmi_get_screen_items` as the summary view.
- Use `hmi_patch_json` only if its existing guards and operation schema are
  proven to support condition rows and preserve the same invariants;
  otherwise implement a direct Openness path. Keep the formula setter's
  existing contract backward-compatible.

**Focused verification:** On an authorized disposable HMI item, verify a
`Top` condition table with two ordered Boolean conditions and integer values
(for example, 500 and 400), and confirm its default `Top` value is unchanged.
Also verify empty conditions, incorrect value types, conflict handling,
readback order, and preservation of unrelated dynamizations. Live TIA
validation, compilation, and saving require separate authorization.

### 6. Test, document, and version

- Add focused tests for successful read/write/readback, invalid input, missing
  items, unsupported properties/events, and preservation of existing scripts
  and dynamizations.
- Update tool schemas and the tools reference documentation.
- Run the narrow relevant test suite, then the server build/type checks.
- Before choosing the next MCP server version, check the running server's
  `/status` endpoint when available; otherwise inspect the reported-version
  source. Bump the MCP server version and align assembly/project metadata as
  required by the repository's versioning convention.
- Do not invoke a live TIA save or compile as part of unit tests. Any live
  integration test requires a separately approved target and action.

**Completion criteria:** Tests and build pass; tool schemas and docs match the
implemented behavior; readback confirms writes through the test harness; and
the reported server version is updated.

## Codespace and live-project boundaries

- First verify that the Codespace has the required .NET SDK and can build the
  repository. TIA Openness/COM integration may require a Windows machine with
  TIA Portal and may not be runnable inside a Codespace; use tests or mocks for
  source-level validation when that integration is unavailable.
- Do not connect to or inspect a TIA project, PLC block, or live tag while
  implementing these server-side tools unless separately authorized.
- Do not save or compile a live project without explicit approval.

## Deferred sidebar use case

The previously approved sidebar parameters were a 72 px pitch, visibility bits
0–4 in button order, and a shared `bWithRobot` tag. The inspected HMI tag list
did not contain `bWithRobot`, and the active project was explicitly put on hold.
Therefore this plan does not include applying or validating those settings in
that project.
