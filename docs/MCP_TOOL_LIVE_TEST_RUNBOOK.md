# MCP server update and live-test runbook

This runbook prepares the MCP server and executes the case-by-case plans for
the v2.9.0 TIA/PLC/HMI tool additions. All Siemens-facing tests must use an
approved disposable project. Do not test mutations against a production
project, physical PLC, or operating plant.

## 1. Prepare a safe test window

1. Schedule a maintenance window with the TIA/MCP server operator. Updating or
   restarting the server interrupts its persistent TIA Portal connection.
2. Save the current engineering project and make a separate backup/copy for
   testing. Confirm the test copy is clearly identifiable and is the project
   the MCP server will use.
3. Keep physical controllers and outputs out of scope. Do not download to a
   PLC, change CPU operating state, write live PLC values, start a sequence, or
   interact with production runtime controls as part of this tool test.
4. Record the current server version, TIA Portal V21 build, project-copy
   identifier, and the approved PLC/HMI target names. Keep this information
   local; do not commit project files, credentials, IP addresses, or raw
   engineering exports to this repository.

## 2. Update the Windows MCP server

1. On the Windows host running the MCP server, fetch/checkout the reviewed
   commit `0f04cd6` (or a later explicitly approved commit).
2. Preserve the existing `config.json` and any host-specific settings. Back up
   the current deployed server directory before replacing binaries.
3. Build the Release executable from the repository root:

   ```powershell
   dotnet build .\src\TiaOpennessMcp.csproj -c Release
   ```

4. Run the schema check against that exact Release executable:

   ```powershell
   .\scripts\Test-ToolSchemas.ps1 `
     -ExecutablePath .\src\bin\Release\net48\TiaOpennessMcp.exe
   ```

   This is an offline check; it must not require connecting to or modifying a
   TIA project.
5. Deploy the complete matching Release output and required dependencies,
   retaining the server's existing configuration. Prefer the existing
   executable path. The rebuilt executable has a new hash; have the Windows
   administrator refresh the Siemens Openness whitelist for that executable
   using the project's documented procedure, or confirm that the installed
   TIA policy already trusts the new hash.
6. Gracefully restart the MCP server and reconnect the MCP client. Do not
   restart TIA Portal unless separately required and approved.
7. Verify `/status` reports version `2.9.0`, then call MCP `tools/list`.
   Confirm 110 tools are exposed and these new tool names are present:

   - `tia_create_block_group`
   - `tia_get_tag_table_attributes`
   - `hmi_create_navigation_button`
   - `tia_get_library_type_instances`
   - `tia_get_db_member_attributes`

   If the version, count, or tool list is unexpected, stop; do not begin
   Siemens-side tests.

## 3. Confirm the active test project and baseline

1. The MCP-connected agent must call `tia_get_status` before any other
   project operation.
2. Compare the reported active project path/name and connection state with the
   approved disposable project. If disconnected, ambiguous, or pointed at any
   other project, stop and ask the operator. The agent must not switch
   projects, attach to another TIA process, or disconnect/reconnect on its own.
3. Use read-only discovery to record the exact PLC and HMI target names and
   relevant object names. Capture only the minimum test evidence needed.
4. Confirm the fixture has the objects needed by the plans: PLC tag tables and
   groups, a global DB with a direct member, a project-library type/instance
   when available, and a Unified HMI with two distinct screens. Missing
   fixtures should be marked `BLOCKED`, not worked around by selecting a
   similarly named production object.

## 4. Execute the test plans in passes

For each applicable case in the linked plan, record `PASS`, `FAIL`, `BLOCKED`,
or `NOT RUN`, the tool name, concise input selectors, expected outcome,
observed response, read-back evidence, and any project changes. Run all safe
invalid-selector and collision cases; do not induce COM/API faults against a
live TIA instance.

### Pass A — Read-only discovery

Run these before any mutation:

- [PLC metadata](./plans/MCP_TOOL_TEST_PLAN_PLC.md)
- [PLC tag and constant reads](./plans/MCP_TOOL_TEST_PLAN_PLC_READS.md)
- [DB-member attributes and deferred PLC text-list read](./plans/MCP_TOOL_TEST_PLAN_PLC_SLICE_READS.md)
- [Project/library/text discovery](./plans/MCP_TOOL_TEST_PLAN_PROJECT.md)
- [Library-type instances](./plans/MCP_TOOL_TEST_PLAN_LIBRARY_TYPE_INSTANCES.md)
- [HMI discovery](./plans/MCP_TOOL_TEST_PLAN_HMI.md)

Use exact PLC, DB/member, table/group, project, library-type/version, and HMI
selectors. Include the missing-target, case mismatch, ambiguity, and empty
result cases described by each plan. Reads must not change project content.
Mark unavailable fixture-dependent cases `BLOCKED`.

### Pass B — PLC and project mutations

Only proceed after the operator confirms the active project is the disposable
copy and authorizes project edits:

- [PLC tag/group/constant mutations](./plans/MCP_TOOL_TEST_PLAN_PLC_MUTATIONS.md)
- [Block-group creation and tag-table attribute read](./plans/MCP_TOOL_TEST_PLAN_PLC_GROUP_ATTRIBUTES.md)
- [Project creation](./plans/MCP_TOOL_TEST_PLAN_PROJECT_MUTATIONS.md)

Use unique test names with a clear prefix, for example
`MCP_TEST_20261116_`. Exercise create, exact read-back, safe conflict, and
invalid-selector cases. For the constant update case, create a test-only
constant first and update only that constant.

Test `tia_create_project` last and only with separate explicit operator
approval and a confirmed empty destination directory: it changes the
manager's active project context. Do not use the project currently under test
as its destination.

### Pass C — HMI structure and navigation

After confirming the same disposable project and the exact HMI target:

- [HMI structure mutations](./plans/MCP_TOOL_TEST_PLAN_HMI_STRUCTURE.md)
- [HMI navigation button](./plans/MCP_TOOL_TEST_PLAN_HMI_NAVIGATION.md)

Use test-only screen/group/item names. Read the screen back after each change.
Run `hmi_compile` with the HMI **device name** as `target_device`, as specified
in the HMI skill. Runtime navigation is a separate optional check and must
only target a safe test runtime with harmless screens. Do not activate controls
connected to real process tags.

### Pass D — Persistence and cleanup

1. Review every result, including `Partial` and warning fields. For uncertain
   mutation outcomes, inspect the object before any retry.
2. Save the disposable project only after the operator authorizes saving.
3. The MCP-connected agent must not call `tia_close_project`,
   `tia_disconnect`, or switch projects to test persistence. After the agent
   finishes, the operator may close/reopen the disposable project in a
   separately approved window, then rerun the read-back checks.
4. Record compile diagnostics and verify that no unrelated objects changed.
   Keep the test copy until the results are reviewed; discard it using the
   operator's normal project cleanup procedure.

## 5. Cases that are not executable as shipped

The following roadmap items have no implementation to test; do not invent
tool names or approximate their behavior. Their API rationale and deferred
acceptance cases are in these plans:

- [`read_project_texts`](./plans/MCP_TOOL_TEST_PLAN_READ_PROJECT_TEXTS.md)
- [`get_plc_textlists`](./plans/MCP_TOOL_TEST_PLAN_PLC_SLICE_READS.md)
- [`instantiate_library_type`](./plans/MCP_TOOL_TEST_PLAN_LIBRARY_TYPE_INSTANCES.md)
- [`hmi_create_template`](./plans/MCP_TOOL_TEST_PLAN_HMI_NAVIGATION.md)

Controlled API/read-back fault injection, if desired, belongs in an isolated
test harness and must not be induced by damaging a live engineering project.

## 6. Copyable instruction for the MCP-connected agent

Replace the bracketed fields before sending this instruction. The operator
must approve the exact disposable project and whether mutations and saving are
authorized.

```text
You are executing the TIA MCP live-test runbook in
docs/MCP_TOOL_LIVE_TEST_RUNBOOK.md for server v2.9.0.

Approved disposable project: [exact project name/path]
Approved PLC target(s): [exact names]
Approved HMI software/device target(s): [exact names]
Mutation authorization: [yes/no]
Save authorization: [yes/no]
Project-creation test authorization and empty destination, if any: [yes/no, exact path]

Safety and connection rules:
- First call tia_get_status. Do not call any other project tool first.
- If disconnected, if the active project does not exactly match the approved
  disposable project, or if any target is ambiguous, stop and report BLOCKED.
  Do not call tia_connect, tia_disconnect, tia_open_project, or
  tia_close_project to resolve the mismatch.
- Do not operate a physical PLC, download to a controller, change CPU state,
  write live process values, start a sequence, or activate production HMI
  controls. Do not run PLCSIM sequence tests for this tool-schema/engineering
  test.
- Start with read-only cases. Do not attempt mutations unless Mutation
  authorization is yes and the exact active project remains the approved copy.
- Use only exact target names and test-only object names. Before retrying any
  mutation, inspect the target if the prior response is partial, failed after
  mutation began, or uncertain.
- Do not close the project, disconnect, switch projects, or save unless the
  corresponding authorization above is yes. Project creation must be last,
  separately authorized, and use only the exact empty destination supplied.
- Do not reveal or store credentials, project source, raw exports, or unrelated
  project data in the test report.

Execution:
1. Confirm server version/tool availability through the connected MCP
   environment; expect v2.9.0 and 110 registered tools.
2. Call tia_get_status and report the active project/connection check without
   changing it.
3. Read the relevant docs/plans/MCP_TOOL_TEST_PLAN_*.md plans. Execute fixture-ready
   read-only cases first, then authorized PLC/project mutations, then
   authorized HMI cases. Follow each plan's exact inputs and expected results.
4. For every case, report PASS, FAIL, BLOCKED, or NOT RUN; tool name; concise
   selectors; expected versus actual result; read-back evidence; Partial and
   warnings; compile outcome if applicable. Redact sensitive details.
5. Do not fake API failures or claim save/reopen persistence or runtime
   navigation unless those checks were actually performed.
6. Stop immediately on any active-project mismatch, unexpected partial
   mutation, possible production target, or uncertain side effect. Ask the
   operator before continuing.

Return a concise results table, a list of deviations/blocked cases, and
confirmation that you left the TIA connection and project open as found.
Do not commit project-specific test logs or exports.
```

## 7. Run status (v2.9.0 live test, 2026-11-16)

Results below summarize a live test run. Some mutations ran on the working
project with approval; this deviated from the safety procedure and is detailed
below. No project-specific logs, exports, or credentials are committed.

| Pass | Result | Notes |
|---|---|---|
| A: Read-only discovery | PARTIAL | 31 cases recorded. Several planned success paths and paged listings were not run (see section 8). |
| B: PLC and project mutations | PARTIAL | Duplicate-create probes were rejected by TIA in this fixture; this does not establish that duplicate objects cannot exist in imported or older projects. PLC ambiguity is blocked pending a two-PLC project; group/path sub-checks remain unverified. |
| C: HMI structure and navigation | PARTIAL | Selected creation, rename, and collision checks passed. Runtime navigation was not run. |
| D: Persistence and cleanup | PASS WITH DEVIATION | Save/close/reopen checks passed with operator approval; the agent also closed/reopened projects and attached to new TIA processes contrary to this runbook's connection rules. |
| Project creation (`tia_create_project`) | PASS AFTER RETRY | C2 to C6 were run. The first two attempts returned partial results; the third succeeded. |

No aggregate pass count is assigned: the four execution passes include blocked
or unrun planned checks, while project creation is a separate operation rather
than a fifth pass.

**Build and checks (section 2):** Release build succeeded with 0 errors and 4 existing nullable warnings in `TiaManager.Hmi.cs`. The offline schema check passed for 111 exported schema files, run with a per-process execution-policy bypass. This does not establish the live `tools/list` count, which remains a separate check in section 2.

**Deviations**
- Pass B and Pass C mutations ran on the working project, with approval. Later mutations ran on a copy.
- The MCP agent closed and reopened projects and attached to new TIA processes, which the runbook forbids for the agent. Closing and reopening was done with operator approval.
- A `hmi_create_navigation_button` caption defect (styled caption rejected by TIA) was fixed in commit `9a1582e`. Commit message not corrected.
- An HMI tag-table test placeholder was bound to an empty PLC tag, which caused one compile error. Removed by the operator.

**Open items**
1. Openness whitelist: the Windows administrator must confirm the current `TiaOpennessMcp.exe` hash is trusted.
2. Runtime navigation (C6, Pass D): not run. Needs a safe test runtime.
3. Second review of the revised Pass B wording.

## 8. Sub-checks not verified

- Duplicate tag-table group names under one parent (not tested).
- Duplicate-name collision for constant groups under one parent (not verified for tag-table or constant groups).
- Ambiguous group-path resolution for tag tables and constants (not run).
- Exact-path resolution for tag tables and constants (not run).
- Several success paths in Pass A (nested constant reads, library-type instance success, DB-member success) and paged listings.
