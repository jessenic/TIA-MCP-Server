# HMI discovery test plan — Milestone 3 read-only slice

## Implemented operation: screen-group hierarchy

**Manager API:** `HmiScreenGroupsDiscovery ListHmiScreenGroups(string? targetName = null)`

**Registered tool:** `hmi_list_screen_groups`

**Input schema:** optional `target_name` string. An omitted or whitespace-only selector follows the existing `FindHmiSoftware` behavior: select the sole HMI Unified target, or fail when the selection is missing or ambiguous.

**Handler:** calls `TiaManager.Instance.ListHmiScreenGroups(targetName)` and serializes the returned object. It does not add a separate screen, item, or target read.

**Returned JSON shape:** `{ Scope, Completeness, Groups: [{ Name, DirectScreenCount, ChildGroups: [...] }] }`. The hierarchy preserves parent-child relationships. Group names are display data, not unique identifiers. `DirectScreenCount` counts only screens directly in that group. The result does not repeat screen names, screen properties, or items.

| Case | Setup / input | Expected result |
|---|---|---|
| Normal | One selected Unified HMI with root and nested groups, including screens directly in more than one group. | Nested output mirrors the public `ScreenGroups` tree; each group reports its own direct `Screens.Count`. `Scope` and `Completeness` are non-empty. |
| Empty result | Selected HMI has no screen groups. | Successful response with `Groups: []`; no synthetic root group. |
| Missing target | Nonblank `target_name` matches no Unified HMI. | Existing resolver `InvalidOperationException`; no partial discovery object. |
| Ambiguous target | Omitted selector with multiple HMI targets, or a selector matching multiple targets. | Existing resolver `InvalidOperationException` naming the ambiguity; no partial discovery object. |
| Invalid selector | Null and whitespace-only selectors are accepted as unspecified under existing resolver semantics. An unmatched nonblank selector follows the missing-target case; there is no separate path or ID input to parse. | No new validation behavior or ID/revision assumptions. |
| Unsupported | A non-Unified/classic HMI is selected, or a version/runtime does not expose this V21 `ScreenGroups` surface. | Non-Unified targets are not resolved as HMI Unified software; an API surface failure propagates. No fallback enumeration is claimed. |
| API failure | Inject/observe a failure while enumerating `ScreenGroups`, `Groups`, or `Screens`. | Exception propagates; no partial result is returned and no project mutation occurs. |

## Implemented operation: graphic-list names

**Manager API:** `HmiGraphicListsDiscovery ListHmiGraphicLists(string? targetName = null)`

**Registered tool:** `hmi_list_graphic_lists`

**Input schema:** optional `target_name` string, with the same resolver behavior as `hmi_list_screen_groups`.

**Handler:** calls `TiaManager.Instance.ListHmiGraphicLists(targetName)` and serializes the returned object. It does not add a separate target lookup in the handler.

**Returned JSON shape:** `{ Scope, Completeness, GraphicLists: [{ Name }] }`. The completeness statement is limited to the public `HmiGraphicLists` composition and explicitly excludes list entries and file-backed graphic assets.

| Case | Setup / input | Expected result |
|---|---|---|
| Normal | Selected Unified HMI contains one or more graphic lists. | One `{ Name }` per item in `HmiGraphicLists`, with non-empty `Scope`/`Completeness`. |
| Empty result | Selected HMI has no graphic lists. | Successful response with `GraphicLists: []`. |
| Missing target | Nonblank `target_name` matches no Unified HMI. | Existing resolver `InvalidOperationException`; no partial discovery object. |
| Ambiguous target | Omitted selector with multiple HMI targets, or a selector matching multiple targets. | Existing resolver `InvalidOperationException`; no partial discovery object. |
| Invalid selector | Null and whitespace-only selectors are accepted as unspecified; an unmatched nonblank selector follows the missing-target case. No graphic-list name filter or identifier is accepted. | No new validation behavior. |
| Unsupported | A non-Unified/classic HMI is selected, or the graphic-list composition is unavailable in the runtime/API. | Target resolution or API failure is surfaced; no claims about graphic files, list entries, or other asset stores. |
| API failure | Inject/observe an exception while enumerating the composition or reading a list name. | Exception propagates; no partial result is returned and no project mutation occurs. |

## Existing reads and deferred coverage

- `ListHmiScreens`, `GetHmiScreenItems`, `GetHmiEventScripts`, `ListHmiTargets`, and HMI JSON export already cover overlapping screen, item, event-handler/script, target, and broad HMI export reads. This slice does not add replacements for them.
- `GetHmiEventScripts(screenName, itemName, targetName)` already reports the enum event types exposed for a screen/item and enumerates its event handlers and script text. It does not establish support for every HMI object family or event binding outside those surfaces.
- `GetHmiScreenItems` reports some tag dynamization information but does not represent every dynamization type. HMI JSON export serializes additional definitions, and the importer has private validation routines; neither justifies adding a duplicate screen/item read or implying a standalone, complete dynamic-definition/validation API here. A pure validation endpoint is deferred until a separate, exact contract can avoid target/item rereads and make clear that expressions/scripts are not executed.
- The bundled V21 WinCC Unified assembly exposes `HmiSoftware.ScreenGroups` and `HmiSoftware.HmiGraphicLists`, plus screen-group `Groups`, `Screens`, and `Name` and graphic-list `Name`. It contains no `HmiTemplate` type. No verified public API for complete template inventory, graphic-list entries, or file-backed asset inventory was identified. Those are deferred rather than inferred from screen groups or graphic lists.
- Device/software target discovery is already provided by `ListHmiTargets` and `FindHmiSoftware`; this slice adds no duplicate device metadata endpoint. It also introduces no IDs, revisions, or event/dynamic mutation.

## Additional live verification on a disposable project

Selected live checks are recorded in the
[runbook](../MCP_TOOL_LIVE_TEST_RUNBOOK.md). Run the remaining cases below;
the runbook's `NOT RUN` and `BLOCKED` results are not passes.

Prerequisites: Windows with TIA Portal V21 and the matching V21 public API installed; an attached TIA Portal V21 session with a disposable project containing at least one WinCC Unified device. Keep the existing persistent TIA connection alive, and verify status before operations. A non-Windows build or bundled reference DLL inspection is not live-project verification.

1. Make a disposable HMI project with a root-level group, a nested group, screens at both levels, and one ungrouped screen. Record the tree and direct screen counts in TIA Portal.
2. With exactly one HMI target, call `hmi_list_screen_groups` without a selector and with the exact target selector. Compare group names, nesting, and direct counts with the project; confirm ungrouped screens do not become groups.
3. Test an HMI with no groups; confirm an empty `Groups` array. Use a project with two HMI targets to verify omitted and multiply matching selectors fail as described; use an unmatched selector for the missing case.
4. Create two graphic lists in the disposable HMI and call `hmi_list_graphic_lists`. Compare names with the V21 UI. Repeat with no graphic lists and with multiple HMI targets.
5. Before and after every call, compare the project’s relevant object counts and save/dirty state. Confirm no screen, group, graphic list, item, script, or dynamic was created, changed, or deleted.
6. Record TIA Portal/API version, project/runtime type, exact request, returned shape, and any API diagnostics. Do not treat metadata inspection or compilation as a substitute for these live checks.
