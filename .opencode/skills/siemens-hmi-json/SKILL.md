---
name: siemens-hmi-json
description: >-
  Work with WinCC Unified HMI JSON exports from the TIA MCP server. Use only when the
  user explicitly requests an HMI JSON operation or authorizes this skill. Clarify
  vague requests to export or read out an HMI when JSON intent is not stated.
---

# WinCC Unified HMI JSON

Use the TIA MCP JSON tools to work declaratively with Unified HMI screens and related
objects. The export is a reviewable project artifact; it is not a substitute for
checking the selected TIA project, target, or impact before importing changes.

## When to use

- Use this skill only when the user explicitly requests an HMI JSON operation (for
  example, “export this HMI to JSON,” “inspect this HMI JSON export,” or “edit/import
  this JSON”) or explicitly authorizes using the skill.
- A request such as “export this HMI” or “read out this HMI” without mentioning JSON is
  ambiguous; ask whether the user wants a JSON export before using this skill.
- Do not infer authorization merely because the task could be performed with HMI JSON.

For Figma-to-HMI design conversion, use the `siemens-figma-to-wincc-unified` skill to produce
the screen design. Use this skill to serialize, review, or import that design only if
the user explicitly requests JSON work or authorizes this skill. For other Openness
operations, use `siemens-tia-portal-openness`.

## Safety and scope

- Use only with WinCC Unified HMI targets. Do not treat Classic WinCC HMI as compatible.
- Before exporting, confirm the active TIA connection/project and identify the intended
  HMI target. Use `hmi_list_targets` when the target is unclear; pass its software name
  as `target_name` when more than one target exists. Never silently choose an arbitrary
  target.
- Export writes files to the requested local `output_directory`. Keep exports inside
  the workspace when they are meant to be version-controlled; do not overwrite or
  delete an existing export unless the user requested that destination.
- An import or patch changes the selected project's in-memory engineering objects.
  Preview with `dry_run: true` first and get user authorization before applying any
  engineering changes. These tools compile but do not save the project.
- Never call `tia_save_project` as part of this workflow unless the user separately
  and explicitly asks to save.
- `prune_items: true` deletes existing screen items absent from imported screen files.
  Keep it false unless the user explicitly intends to remove those items.
- JSON item names are immutable in patches. To rename an item, plan a deliberate
  replacement and account for its event handlers, dynamizations, and references.
- Treat JavaScript and all imported JSON as engineering input. Review scripts and
  target/tag bindings before applying them.

## Export workflow

1. Check the TIA connection and active project using `tia_get_status`. If not connected,
   follow the connection procedure in `siemens-tia-portal-openness`.
2. Resolve the intended target with `hmi_list_targets` when needed.
3. Call `hmi_export_json` with an absolute `output_directory` when the caller can
   access the server filesystem. For remote clients without a shared filesystem, omit
   `output_directory`; the tool creates a staged transfer and returns a `transferId`
   and `files` list. Read each listed file through `hmi_read_export_file`, decoding the
   returned Base64 chunks in order.

   ```json
   {
     "output_directory": "C:\\Projects\\Plant\\hmi\\HMI_RT_1",
     "target_name": "HMI_RT_1",
     "compact": true,
     "script_files": false
   }
   ```

4. Read the returned local directory or fetch the staged `manifest.json`, target name,
   screen files, and other relevant files. Inspect them before making claims about the
   design; the manifest contains the file hashes.
5. Keep the export structure intact and make focused edits. Preserve screen item array
   order: it represents visual stacking order. Import cannot reorder existing items
   (V21 Openness has no reorder API) and TIA decides where newly created items land;
   see `zOrder` under the import workflow.

For remote transfers, create an import staging area with `hmi_begin_import`, then upload
the edited `manifest.json` and every referenced file with `hmi_upload_import_file`.
Files must be uploaded as sequential Base64 chunks, starting at offset 0 and continuing
from each response's `receivedBytes`; each file is limited to 10 MiB and each transfer
to 100 MiB. Staging transfers expire after 24 hours.

`compact` defaults to true and omits properties equal to the TIA-created defaults for
that item type. `script_files` defaults to false. When true, scripts are written as
`.js` sidecars and referenced from their event records.
Faceplate defaults and creation use the exported `containedType` as well as the item
type. Preserve that identity, including its library version. Custom widgets export
supported full properties with item-level `compact: false`; inspect manifest `warnings`.

If default-item creation fails during a compact export, report the error and retry
with `compact: false` into a fresh directory. Non-compact export reads the supported
properties without creating temporary default items. Do not equate export success
with import support.

## Export layout and data conventions

An export contains:

```text
<output_directory>/
  manifest.json
  screens/
    0001-<screen>.json
  tags/
    0001-<tag-table>.json
  alarms/
    discrete.json
  styles.json
  defaults.json               # captured type/contained-type defaults for compact previews
  scripts/                    # only when script_files is true and scripts exist
```

The manifest records `format` (`tia-mcp-hmi/1`), target and TIA version, languages,
export timestamp, compact mode, file references, and SHA-256 hashes. Screen groups are
stored as slash-separated `group` paths; root screens have `group: null`. Screen files
contain `props`, `text`, and ordered `items`. Items contain `name`, `type`, `props`,
`text`, `dynamizations`, and `events`, with an optional `style`. Export always writes
all three item sections. On import, an item without a `text`, `dynamizations` or
`events` key (or with the key set to `null`) leaves that section of an existing item
unchanged; `[]` removes all dynamizations or events.

Conventions:

- Keep JSON UTF-8, two-space indented, and preserve the existing key/property spelling.
- Property keys in `props` use the Openness property names (for example `Left`,
  `Width`, `BackColor`). Enums are strings by name. Flags enums such as `WindowFlags`
  take one name or several names separated by `,` or `|` (export writes the form
  `"ShowCaption, CanClose"`); `"None"` clears all flags and numbers are rejected.
  Colors use `#RRGGBB` or `#AARRGGBB`.
- Text that is plain across all configured languages can be a string. Use a language
  map such as `{ "en-US": "...", "fi-FI": "..." }` for differing translations. Use
  `{ "html": "<body><p>...</p></body>" }` for raw markup. A language map must include
  every language configured on the target.
- Tag dynamizations use `{ "type": "Tag", "property": "...", "tag": "...",
  "readOnly": false }`. Bind only to existing HMI tags. Do not silently replace a
  non-tag dynamization; conflicts fail unless the input explicitly requests
  `"onConflict": "replace"`.
- Expression dynamizations use `type: "Expression"` and a required `valueConverter`.
  Both tag and expression converters preserve `formula`, `isFormulaSelected`,
  `conditionType`, and ordered `entries`. Simple entries have a string `condition`;
  Range entries have typed `from` and `to`. Bitmask entries preserve the enum
  `bitDynamizationType`, unsigned 64-bit `condition`, and TIA-generated unsigned
  64-bit `relevant` mask. Their `value` and `alternateValue` use
  `{ "type": "Boolean", "value": true }` (or the exported CLR primitive/color type).
  Preserve typed values and raw ProjectText markup verbatim, plus `flashing` and
  `flashingRate`. Do not replace mapping-table expressions with a guessed formula.
- Event scripts are JSON strings inline by default. With sidecars, each event has
  `scriptFile`; the manifest hashes the script file as well. Import read-back compares
  event and script-dynamization code ignoring CRLF/LF differences and trailing spaces
  or tabs at line ends and at the end; any other difference fails and names the first
  differing line. Scripts are always written exactly as supplied.
- Tags are grouped by tag table in `tags/*.json`. Discrete alarms are stored in
  `alarms/discrete.json`. Shared styles are property dictionaries in `styles.json`,
  referenced by an item's `style` name.
- Internal tags use `connection: "<Internal tag>"`, an empty `plcTag`, and `dataType`.
  An unset minimum/maximum is JSON null. Array-element bindings retain bracket syntax;
  dotted UDT member bindings retain the full path and require an existing root HMI tag.
  Import validation checks that the dotted suffix is well-formed, but does not resolve
  member names against the PLC UDT; the TIA assignment/compile remains authoritative.
  Array-element validation checks indices against declared array bounds.

## Editing an export

1. Make only the requested change. Preserve unrelated object properties, screen item
   order, group paths, active languages, tag-table names, and event types.
2. Before changing bindings, confirm the HMI tag exists in the export or target and
   confirm its PLC path and connection are the intended ones.
3. Before editing or removing an event or dynamization, inspect the whole item record;
   an empty `events` or `dynamizations` array is meaningful on import and removes
   existing handlers or dynamizations for that item. Omit the key (or set it to
   `null`) to leave that section unchanged. `text: {}` sets no texts in a non-compact
   import and resets texts to the type defaults in a compact import; omit `text` to
   keep the existing texts. Omitted `props` are still reset to type defaults in a
   compact import, so use item `compact: false` or `hmi_patch_json` for partial
   property edits.
4. Validate JSON syntax and cross-references: file references, screen identities,
   unique item/tag/alarm names, style references, alarm tags, dynamization tags, and
   event scripts. Screen width and height must be present and nonzero.
5. Exported file hashes become stale as soon as a referenced JSON or script file is
   edited. The importer verifies hashes by default. Prefer updating every changed
   file's SHA-256 entry in `manifest.json`; if hashes are intentionally not refreshed,
   pass `verify_hashes: false` to `hmi_import_json` and clearly disclose that
   integrity checks were bypassed. Do not change hashes for unedited files.
6. Import supports Tag and Expression dynamizations with Simple/Range/Bitmask
   converter mappings. Setting `Singlebit` may cause TIA to create its bitmask entries
   automatically; reuse those entries and create them only if the table is empty.
   Import verifies generated relevant masks against the JSON. Bitmask entries cannot
   be deleted through Openness, so an existing Singlebit or Bitmask table is updated
   in place when the condition type, bit dynamization type, entry count and relevant
   masks match; an unchanged export re-imports cleanly. A different condition type or
   relevant mask (bit) needs `"onConflict": "replace"` on that dynamization, which
   recreates only the dynamization, never the item. The relevant mask is read-only in
   Openness, so a recreated table works only when TIA generates the requested bit;
   otherwise change the bit in TIA Portal. Keep the bitmask entries in TIA's exported
   order: Openness cannot reorder them, so a reordered list is refused, also with
   `replace`. A dry run reports these refusals. To retag such items, use `bind` in
   `hmi_patch_json`. Other
   dynamization/mapping kinds fail explicitly. Legacy `ExpressionDynamization`
   placeholders lack converter data: re-export rather than attempting to import them.

## Singlebit setter and tool availability

- `hmi_set_tag_dynamization` with `condition_type: "Singlebit"` sets the off/on values
  of the mapping table; an existing Singlebit table is updated in place. Switching an
  existing table that has entries to another condition type (for example Singlebit to
  Range), or replacing a non-tag dynamization, fails: call `hmi_delete_dynamization`
  first. `condition_type: "None"` only sets the tag and leaves an existing table as
  it is. The setter is not a one-entry editor. Use `hmi_update_dynamization_entry` for
  a targeted update to an existing row.
- TIA may generate Singlebit entries when the condition type is assigned, even if an
  initial Openness collection read appears empty. If a setter call fails, assume the
  in-memory object may have been partially changed: inspect it before retrying. Do not
  blindly call the setter again or use `condition_type: "None"` as a reset.
- Use `hmi_delete_dynamization` to remove one dynamization directly by screen, item,
  and property. It preserves other dynamizations and the item, verifies the deletion,
  and leaves the project unsaved.
- If a tool documented here is missing from the live MCP tool list, refresh or reconnect
  the client and verify its configured server endpoint before concluding the tool is
  unavailable. Do not substitute a whole-mapping setter for an in-place entry update.

## In-place dynamization entry updates

Use `hmi_update_dynamization_entry` when changing values or flashing settings on one
existing mapping-table entry is more targeted than reimporting a screen. This directly
modifies the selected project's in-memory engineering object; confirm the active
project and HMI target, inspect the current dynamization, and get user authorization
before calling it. It does not save the project.

- Identify the item and dynamized property with `screen_name`, `item_name`, and
  `property_name`. The existing dynamization must be a Tag or Expression dynamization.
- `entry_index` is the zero-based position in the ordered mapping table, not a condition
  value or bit number. Inspect the exported converter entries first to choose the index;
  for example, the second entry has index `1`.
- Set one or more of `new_value`, `new_alternate_value`, `new_flashing`, or
  `new_flashing_rate`. At least one is required. Value objects use
  `{ "type": "Color", "value": "#E80000" }`; the type must match the existing entry's
  CLR value type (`Color`, `String`, `Boolean`, `Byte`, `Int16`, `UInt16`, `Int32`,
  `UInt32`, `Int64`, `UInt64`, `Single`, `Double`, or `Decimal`). Numeric and boolean
  values are encoded as strings in this tool input.
- The tool changes only the selected entry's supplied fields. It preserves the
  condition, mapping table order, and all unspecified fields and entries.
- For a direct Openness update, verify the result with `hmi_get_screen_items` on the
  same screen. Its `BitmaskEntries` include the Singlebit condition, relevant mask,
  and value. Compare the returned dynamization state before and after the edit; do
  not export/import JSON solely to verify a direct tool update. Compile the target
  with `hmi_compile`. If a field changed by the updater is not present in the direct
  readout, state that verification limitation rather than assuming it was preserved.

Example: change the second BackColor entry to red:

```json
{
  "screen_name": "Main_UnitControlSideBar",
  "item_name": "UCS_Ind_Alarm",
  "property_name": "BackColor",
  "entry_index": 1,
  "new_value": { "type": "Color", "value": "#E80000" },
  "target_name": "HMI_RT_1"
}
```

## Render images for AI layout checking

Use `hmi_render_json` directly on the exported files; it is offline and does not
require an active TIA connection, import authorization, or a compile:

```json
{
  "input_directory": "C:\\Projects\\Plant\\hmi\\HMI_RT_1",
  "output_directory": "C:\\Projects\\Plant\\previews\\review-1",
  "language": "en-US",
  "scale": 2
}
```

Optionally supply `screen_name` (a name or group/name path). Omit it for all screens.
The equivalent CLI is `TiaOpennessMcp.exe --render-hmi INPUT --output OUTPUT
--language en-US --scale 2`. The CLI bypasses MCP/server auto-connection entirely.

1. Prefer full exports or new compact exports containing manifest `defaultsFile`
   and hashed `defaults.json`. Old compact files render with an explicit warning:
   missing type defaults cannot be reconstructed exactly offline.
2. Read `render-index.json` or tool-returned paths. Inspect the clean lossless PNG
   first; default 2x scale improves text readability without lossy compression.
3. Use the numbered `.layout.png` with `.layout.json` to identify exact item paths.
   The IDs match; bounds `[x,y,width,height]` are unscaled screen coordinates,
   including nested window transforms. Reports retain stacking order and identify
   visibility, clipping, potential text overflow, unknown values and placeholders.
4. Inspect warnings before asserting a defect. Static nested screen windows are
   composed recursively with clipping/zoom/scroll, but scripts and tag/expression
   values are never executed. Bound IO values show `?`; runtime-dependent visibility,
   text, and colors are flagged and use exported static state.
5. Faceplates, custom SVG/widgets, alarm/trend controls and unsupported compositions
   appear as hatched geometry placeholders, not invented runtime artwork. Graphics
   are not exported. Missing fonts, rich-text and other skin/compositing/window
   approximations are disclosed. Install the original fonts for trustworthy text
   widths; overflow detection is a heuristic.
6. Report suspected problems with item path, overlay ID, bounds, and the relevant
   fidelity warning. Do not equate preview appearance with a verified runtime defect.
   Render again into a fresh output directory after editing. Outputs are never
   overwritten. Rendering ignores hashes to allow draft review; import still checks
   them by default.

The renderer does not modify JSON or TIA. Scale accepts 0.5 through 4, with a maximum
8192 pixels per output axis and 32 megapixels. Explicit language selection makes
reviews reproducible; otherwise en-US is preferred, then the first manifest language.

## Import workflow

1. Confirm with the user the exact project, HMI target, files, and intended changes.
2. Run `hmi_import_json` with `dry_run: true`, either the input directory or completed
   transfer ID, and exact target. Provide exactly one of `input_directory` and
   `transfer_id`.
   Use `verify_hashes: true` when hashes were refreshed. If files were intentionally
   edited without refreshing hashes, use `verify_hashes: false` for the preview and
   clearly note the integrity-check bypass.
3. Check the dry-run result for expected screen, item, tag, and alarm counts. Resolve
   validation errors; do not bypass precondition, type, path, language, or reference
   failures.
4. Only after authorization, run the same import with `dry_run: false`. Keep
   `prune_items: false` unless deletion of all absent items on imported screens was
   specifically requested.
5. Report whether compilation succeeded, its diagnostics, and that the project remains
   unsaved. On compilation errors, report that the import was applied in memory and
   was not saved; do not state that it was rolled back.

An import upserts screens, widgets, tags, and alarms in the listed files; it does not
delete screens or tag/alarm objects omitted from the export. For each imported item,
the supplied properties and text are applied, while `events` and `dynamizations` are
reconciled to the supplied arrays. A missing or `null` `text`, `events` or
`dynamizations` key leaves that section unchanged (the result `warnings` lists such
items); `[]` deletes all events or dynamizations of the item. With compact imports,
omitted item properties are reset to TIA-created type defaults, and so are texts when
the item has a `text` key. Screen-level `props` and `text` are applied as given.
Review these effects before importing.

Screen item order: the item array order is the stacking order. Import never reorders
existing items, and V21 Openness cannot create an item at a given position, so new
items are created in file order and TIA decides where they land (they have been seen
behind existing items). After applying, the import compares the item order of each
imported screen with the file. Set `"zOrder"` in `manifest.json` (all screens) or in
a screen file (that screen only): `"keep"` (default) reports differences in the
result's `zOrderWarnings`; `"require"` fails and rolls the import back. A dry run
reports existing items that are already out of file order. Fix the order with Arrange
(Bring to front / Send to back) in the TIA editor. Older server versions ignore
`zOrder`.

## Guarded patch workflow

Use `hmi_patch_json` for a narrow update to existing items when a full export/import is
unnecessary. A patch file is one JSON object in the `tia-mcp-hmi-patch/1` format (at
most 10 MiB). It changes existing items only: it cannot create items, rename items,
or edit events, scripts or expression dynamizations.

### Root fields

| Field | Required | Meaning |
|---|---|---|
| `format` | yes | Exactly `"tia-mcp-hmi-patch/1"`. |
| `require` | no | Preconditions checked before any change (see below). |
| `operations` | yes | Ordered list of `set`, `bind` and `delete` operations, at most 10,000. |
| `verify` | no | Postconditions checked after the operations, before commit. |

`require` and `verify` have the same shape:

| Field | Meaning |
|---|---|
| `screens[].name` | Screen name (required). |
| `screens[].group` | Slash-separated screen group path; `null` or omitted for a root screen. |
| `screens[].width`, `screens[].height` | Optional; must equal the screen size. |
| `screens[].items[].name` | Item that must exist on that screen. |
| `screens[].items[].type` | Optional exact item class name, for example `HmiIOField`. |
| `tags[].name` | HMI tag that must exist. |
| `tags[].plcTag` | Optional; the tag's PLC binding must equal this text exactly. |

`require` is checked against the project before the patch, including
`screens[].items`. After the operations, the screens, dimensions and tags in `require`
are checked again (items are not, so a patch may require an item and then delete it).
`verify` checks screens, dimensions, tags and the listed items with their types.

### Operation fields

| Field | Used by | Required | Meaning |
|---|---|---|---|
| `op` | all | yes | `"set"`, `"bind"` or `"delete"` (lower case). |
| `screen` | all | yes | Screen name. |
| `group` | all | no | Screen group path; `null` or omitted for a root screen. |
| `item` | all | yes | Existing item name. |
| `requireType` | all | no | The item class name must match exactly, for example `HmiIOField`. |
| `property` | set, bind | yes | Openness property name. `set` accepts a dotted path into parts such as `Font.Size`; `bind` takes a top-level dynamizable property such as `ProcessValue` or `BackColor`. |
| `value` | set | yes | New value with the JSON type of the property: number, `true`/`false`, enum name (flags enums: names separated by `,` or `\|`), `#RRGGBB` color, or for a text property a string, language map or `{ "html": "..." }`. |
| `tag` | bind | yes | Existing HMI tag, member/array path, or system tag. |
| `expectTag` | bind | no | Guard: the property must already have a tag dynamization bound to exactly this tag. |
| `readOnly` | bind | no | `true`/`false`. Omitted: keeps the current value of an existing tag dynamization (`false` for a new one). |
| `useIndirectAddressing` | bind | no | Same rule as `readOnly`. |
| `onConflict` | bind | no | `"fail"` (default) or `"replace"`. `replace` allows replacing a non-tag (Expression, Script or ResourceList) dynamization with a tag dynamization. |
| `requireNoDynamizations` | delete | no | Fail if the item has dynamizations (checked before the patch). |
| `requireNoEvents` | delete | no | Fail if the item has event handlers (checked before the patch). |

Field names are case-insensitive. The values of `format` and `op` are case-sensitive;
`onConflict` is not. Unknown fields are ignored and listed in the result's `warnings`;
check them, because a misspelled guard is not applied.

`bind` semantics: an existing tag dynamization is reused and only its tag (and the
two flags) are written. The server never touches the value converter or mapping table
on `bind`, so an existing Range, Singlebit or Bitmask table stays as it is. This makes
`bind` the way to retag Singlebit/Bitmask items without reimporting their tables
(issue #6). Whether TIA itself adjusts the table when the new tag has a different data
type has not been verified; read the item back after a retag to a different type.

### Processing order

1. Read the file, check `format`, check `require` (screens, dimensions, tags, items).
2. Validate every operation against the project state before the patch. An operation
   on an item that an earlier operation deletes is rejected.
3. With `dry_run: true`, stop and return the operation list. For `bind` it shows
   `expectTag`, `readOnly` and `useIndirectAddressing` (`"keep"` when omitted) and
   `onConflict`.
4. Otherwise, in one transaction: apply the operations in order, re-check `require`,
   check `verify`, commit.
5. Compile the HMI. The project is never saved.

A failure in steps 1 to 4 changes nothing (the transaction rolls back). A compile error
in step 5 is reported as an error, but the changes stay applied in memory and unsaved;
do not report them as rolled back.

### Examples

Move an IO field and set its text in all languages, guarded by item type:

```json
{
  "format": "tia-mcp-hmi-patch/1",
  "require": {
    "screens": [
      {
        "name": "ParameterScreen",
        "group": null,
        "width": 800,
        "height": 450,
        "items": [
          { "name": "IO field_1", "type": "HmiIOField" },
          { "name": "Label_1", "type": "HmiText" }
        ]
      }
    ]
  },
  "operations": [
    {
      "op": "set",
      "screen": "ParameterScreen",
      "item": "IO field_1",
      "requireType": "HmiIOField",
      "property": "Left",
      "value": 286
    },
    {
      "op": "set",
      "screen": "ParameterScreen",
      "item": "Label_1",
      "property": "Text",
      "value": { "en-US": "Flow lower limit", "fi-FI": "Virtauksen alaraja" }
    }
  ],
  "verify": {
    "screens": [
      {
        "name": "ParameterScreen",
        "items": [ { "name": "IO field_1", "type": "HmiIOField" } ]
      }
    ]
  }
}
```

Retag a Singlebit status lamp without touching its mapping table; the patch fails if
the lamp is not currently bound to the old tag:

```json
{
  "format": "tia-mcp-hmi-patch/1",
  "require": {
    "tags": [ { "name": "Motor2_Running", "plcTag": "DB_Motors.Motor2.Running" } ]
  },
  "operations": [
    {
      "op": "bind",
      "screen": "Overview",
      "item": "Lamp_Motor2",
      "property": "BackColor",
      "tag": "Motor2_Running",
      "expectTag": "Motor1_Running"
    }
  ]
}
```

Delete an item only if it has no dynamizations and no events:

```json
{
  "format": "tia-mcp-hmi-patch/1",
  "operations": [
    {
      "op": "delete",
      "screen": "Overview",
      "item": "Old_Label",
      "requireType": "HmiText",
      "requireNoDynamizations": true,
      "requireNoEvents": true
    }
  ]
}
```

Always use a dry run, review the operation list and `warnings`, and get authorization
before applying a patch.

## Limits and failure handling

- Import validates the file format, file paths, hashes (unless explicitly disabled),
  screen dimensions, supported properties, language coverage, and tag/alarm/style
  references before applying changes.
- Manifest paths must be relative to the export directory. Never redirect a manifest
  reference outside that directory.
- Import supports the screen item types resolved by the server; unsupported or
  ambiguous types fail validation. Do not silently rewrite an unknown type.
- A successful dry run is validation only; it does not prove that the target HMI will
  compile after an import.
- This is not a complete project backup. Complex control compositions and custom
  widget interfaces are not serialized. Custom widgets must already exist in the
  destination; their creation identity is not exposed by the V21 public API.
  Their existing interface is left untouched, not recreated from JSON.
- Unsupported converter entry kinds and tag-based limits fail explicitly.
- Preserve validation and compiler diagnostics in the final report. Never describe
  an unsuccessful compile as a verified result.

For detailed tool arguments and return shapes, read the MCP resource
`tia://docs/tools-reference` (Category 4: WinCC Unified HMI Engineering). Prefer
the live MCP tool schemas when available.
