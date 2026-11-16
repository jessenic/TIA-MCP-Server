# HMI Navigation Button Slice: Implementation and Test Plan

## Scope and implementation decision

This slice implements `hmi_create_navigation_button` only. It adds a new
`HmiButton` to an exact source screen and a Tapped script that navigates to an
exact existing destination screen. It does not update, delete, or replace
existing screen items. The tool supports a bounded style surface: position,
dimensions, and optional button foreground/background colors.

The method and strict schema are new and independent; no registry, shared
documentation, version, or tool-count files were changed. The handler proposed
for parent integration is:

```csharp
TiaManager.Instance.CreateHmiNavigationButton(
    targetName, screenName, itemName, text, destinationScreen,
    left, top, width, height, backColor, foreColor, onConflict)
```

`target_name` must exactly equal one HMI software name returned by
`hmi_list_targets`. Both source and destination screen names must be exact,
case-sensitive names that resolve uniquely within that selected HMI. The
destination is encoded as a JavaScript string literal; navigation uses
`HMIRuntime.UI.SysFct.ChangeScreen(destinationName, "~")`. The method syntax
checks the generated script and verifies the new button's name, geometry, text,
colors when requested, event type, and script read-back before committing its
TIA transaction.

## Bundled V21 API contract evidence

The contract was inspected from the bundled
`src/lib/Siemens.Engineering.WinCCUnified.dll` (assembly version 21.0.0.0):

| API member | Verified public contract | Use |
|---|---|---|
| `HmiSoftware.Screens` | `HmiScreenComposition` property | Enumerate/select screens in the selected HMI |
| `HmiScreen.ScreenItems` | `HmiScreenItemBaseComposition` property | Select and verify the source screen's item |
| `HmiScreenItemBaseComposition.Create<T>(string)` | Existing project code compiles calls as `Create<HmiButton>(name)`; non-generic `Create(string)` is also exposed | Create only a new typed button |
| `HmiButton.Left`, `Top`, `Width`, `Height` | Public properties used by existing button creation code | Apply and read back geometry |
| `HmiButton.Text` | `MultilingualText` property | Apply caption to all active language entries and read back |
| `HmiButton.EventHandlers` | `HmiButtonEventHandlerComposition` property | Create a Tapped handler |
| `HmiButtonEventHandlerComposition.Find/Create(HmiButtonEventType)` | Public lookup and create methods | Ensure a new handler, without replacing an existing one |
| `HmiButtonEventHandler.Script` | `IHmiScript` property | Set and verify the generated navigation action |
| `IHmiScript.ScriptCode` | Public getter/setter | Read-back verification |
| `IHmiScript.SyntaxCheck()` | Returns `HmiValidationResult` | Reject invalid generated event scripts before commit |

`HmiSoftware`'s exported public members in the bundled V21 assembly expose
screen and group compositions, but no template composition or template
creation method. No exported template type or create method was found in the
Unified API assembly. Accordingly `hmi_create_template` is explicitly deferred
rather than inferred from a similarly named concept or implemented through a
different object type.

## Case-by-case validation plan: `hmi_create_navigation_button`

No live TIA Portal project or runtime was used during implementation.
Subsequent selected live checks are recorded in the
[runbook](../MCP_TOOL_LIVE_TEST_RUNBOOK.md); runtime navigation remains
untested. Execute the remaining cases against a disposable V21 project with
at least two unique HMI software targets available; save a backup first.

| Case | Procedure and expected result |
|---|---|
| Basic creation and exact target selection | List targets and screens. Select the exact `SoftwareName` and create a button on an exact source screen, targeting a different exact screen. Verify returned target/source/destination names, item name, geometry, caption, and `ChangeScreen` action. Confirm no button is created in the other HMI target. |
| Source/destination exactness | Try case-only variants of target, source screen, and destination screen names; each must fail before mutation. Try a missing source or destination; each must fail. If duplicate screen names exist in different groups, selection by screen name must fail as ambiguous rather than choose one. |
| Invalid inputs | Test blank, whitespace-padded, and control-character target/screen/item names; empty or edge-whitespace text; negative `left`/`top`; zero `width`/`height`; invalid color; and an `on_conflict` value other than `"fail"`. Each must fail before a screen item is created. |
| Item conflict and in-place safety | Seed the source screen with an item of the requested name and retry, including a case-only variant. Expect failure and verify the original item's type, geometry, text, dynamics, bindings, event handlers, and identity remain unchanged. No overwrite or delete/recreate path is allowed. |
| API failure during creation/property writes | In a disposable project, induce or observe a supported engineering/API failure during item creation or a property/event write. Expect a failure result with `Partial=true` and an inspect-before-retry warning. Inspect the project before retrying; no rollback is claimed if TIA committed any partial side effect. |
| Script syntax/read-back failure | In an isolated test harness or disposable project, induce script syntax rejection or a read-back mismatch. Expect `Success=false`, `Status=verification_failed`, and `Partial=true`; do not automatically delete the new item. Inspect for an item/handler before retrying. |
| Successful read-back | Verify there is exactly one new `HmiButton`, every active language text equals the escaped HTML caption, requested style and geometry match, and its Tapped handler contains exactly the returned `ChangeScreen` call. Verify syntax errors are empty. For `#AARRGGBB` colors, verify the returned normalized color preserves alpha. |
| Persistence and runtime-visible behavior | Save the project, close/reopen it, and verify the button and script persist. Compile the HMI and record diagnostics. Start the target runtime, activate the button, and confirm the exact destination screen opens. This runtime interaction is required to validate behavior beyond API-level script syntax/read-back. |

The returned status `created` means the project object and event script passed
API read-back in the active TIA session; it does not claim that the project was
saved, that HMI compilation succeeds, or that runtime navigation was exercised.

## Explicitly deferred: `hmi_create_template`

The available V21 Unified assembly provides no verifiable template collection,
template type, or template creation method. The method is not implemented and
there is no schema for it. Reusing screen, faceplate, or library-type
compositions would guess at object semantics and inheritance behavior.

### Deferred acceptance/test plan

If a supported V21 API contract is later established, test at least: exact HMI
target selection; valid template creation and result/read-back; malformed or
unsupported properties; missing/ambiguous base template; direct and indirect
inheritance cycles; duplicate/case-only template names; API/read-back failures
and partial effects; save-close-reopen persistence; and a consumer/runtime
check demonstrating that the created object behaves as a reusable template.
