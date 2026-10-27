---
name: hmi-json
description: >-
  Work with WinCC Unified HMI JSON exports from the TIA MCP server. Use when asked
  to export, inspect, edit, review, version, or import HMI screens, widgets, tags,
  alarms, dynamizations, event scripts, styles, or guarded JSON patches.
---

# WinCC Unified HMI JSON

Use the TIA MCP JSON tools to work declaratively with Unified HMI screens and related
objects. The export is a reviewable project artifact; it is not a substitute for
checking the selected TIA project, target, or impact before importing changes.

## When to use

- The user asks to export a Unified HMI target or inspect an existing HMI JSON export.
- The user wants to edit screen layout, text, properties, tags, alarms, or scripts in
  exported JSON rather than make a one-off series of widget calls.
- The user asks to import an edited export or apply a guarded HMI patch.

For Figma-to-HMI design conversion, use the `figma-to-wincc-unified` skill to produce
the screen design, then use this skill to serialize, review, and import that design.
For other Openness operations, use `tia-portal-openness`.

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
   follow the connection procedure in `tia-portal-openness`.
2. Resolve the intended target with `hmi_list_targets` when needed.
3. Call `hmi_export_json` with an absolute `output_directory`. For example:

   ```json
   {
     "output_directory": "C:\\Projects\\Plant\\hmi\\HMI_RT_1",
     "target_name": "HMI_RT_1",
     "compact": true,
     "script_files": false
   }
   ```

4. Read the returned directory, target name, counts, screen paths, and hashes. Inspect
   the manifest and relevant exported files before making claims about the design.
5. Keep the export structure intact and make focused edits. Preserve screen item array
   order: it represents visual stacking order.

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
`text`, `dynamizations`, and `events`, with an optional `style`.

Conventions:

- Keep JSON UTF-8, two-space indented, and preserve the existing key/property spelling.
- Property keys in `props` use the Openness property names (for example `Left`,
  `Width`, `BackColor`). Enums are strings by name. Colors use `#RRGGBB` or
  `#AARRGGBB`.
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
  `conditionType`, and ordered `entries`. Simple entries have `condition`; Range
  entries have typed `from` and `to`. Their `value` and `alternateValue` use
  `{ "type": "Boolean", "value": true }` (or the exported CLR primitive/color type).
  Preserve typed values and raw ProjectText markup verbatim, plus `flashing` and
  `flashingRate`. Do not replace mapping-table expressions with a guessed formula.
- Event scripts are JSON strings inline by default. With sidecars, each event has
  `scriptFile`; the manifest hashes the script file as well.
- Tags are grouped by tag table in `tags/*.json`. Discrete alarms are stored in
  `alarms/discrete.json`. Shared styles are property dictionaries in `styles.json`,
  referenced by an item's `style` name.
- Internal tags use `connection: "<Internal tag>"`, an empty `plcTag`, and `dataType`.
  An unset minimum/maximum is JSON null. Array-element bindings retain bracket syntax;
  validation accepts existing member identities or indices within declared array bounds.

## Editing an export

1. Make only the requested change. Preserve unrelated object properties, screen item
   order, group paths, active languages, tag-table names, and event types.
2. Before changing bindings, confirm the HMI tag exists in the export or target and
   confirm its PLC path and connection are the intended ones.
3. Before editing or removing an event or dynamization, inspect the whole item record;
   an empty `events` or `dynamizations` array is meaningful on import and removes
   existing handlers or dynamizations for that item.
4. Validate JSON syntax and cross-references: file references, screen identities,
   unique item/tag/alarm names, style references, alarm tags, dynamization tags, and
   event scripts. Screen width and height must be present and nonzero.
5. Exported file hashes become stale as soon as a referenced JSON or script file is
   edited. The importer verifies hashes by default. Prefer updating every changed
   file's SHA-256 entry in `manifest.json`; if hashes are intentionally not refreshed,
   pass `verify_hashes: false` to `hmi_import_json` and clearly disclose that
   integrity checks were bypassed. Do not change hashes for unedited files.
6. Import supports Tag and Expression dynamizations with Simple/Range converter
   mappings. Other dynamization/mapping kinds fail explicitly. Legacy
   `ExpressionDynamization` placeholders lack converter data: re-export rather than
   attempting to import them.

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
2. Run `hmi_import_json` with `dry_run: true`, the input directory, and exact target.
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
reconciled to the supplied arrays. With compact imports, omitted item properties and
text are reset to TIA-created type defaults. Review these effects before importing.

## Guarded patch workflow

Use `hmi_patch_json` for a narrow update to existing items when a full export/import is
unnecessary. Patch files use `tia-mcp-hmi-patch/1`, support `set`, `bind`, and `delete`,
and can include `require` and `verify` conditions. For example:

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
          { "name": "IO field_1", "type": "HmiIOField" }
        ]
      }
    ],
    "tags": [
      { "name": "DB_Params_FlowLower", "plcTag": "DB_Params.FlowLower" }
    ]
  },
  "operations": [
    {
      "op": "set",
      "screen": "ParameterScreen",
      "group": null,
      "item": "IO field_1",
      "requireType": "HmiIOField",
      "property": "Left",
      "value": 286
    },
    {
      "op": "bind",
      "screen": "ParameterScreen",
      "item": "IO field_1",
      "requireType": "HmiIOField",
      "property": "ProcessValue",
      "tag": "DB_Params_FlowLower",
      "readOnly": false,
      "onConflict": "fail"
    }
  ],
  "verify": {
    "screens": [
      {
        "name": "ParameterScreen",
        "group": null,
        "items": [
          { "name": "IO field_1", "type": "HmiIOField" }
        ]
      }
    ]
  }
}
```

`require` checks existing screens, optional dimensions, and tags before operations.
`verify` checks the resulting screens, dimensions, tags, and named item types.
`set` fails when the screen or item is missing and cannot rename an item. `bind` must
reference an existing HMI tag and refuses to replace a non-tag dynamization by default.
`delete` requires the existing item; use `requireNoDynamizations` and/or
`requireNoEvents` when deletion must be guarded against those dependencies. Always
use a dry run, review the operation list, and get authorization before applying a
patch. The patch also compiles and leaves the project unsaved.

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
- Bitmask converter entries and tag-based limits are unsupported and fail explicitly.
- Preserve validation and compiler diagnostics in the final report. Never describe
  an unsuccessful compile as a verified result.

For the authoritative tool argument descriptions, see
[the HMI JSON tool reference](../../../docs/TOOLS_REFERENCE.md#category-4-wincc-unified-hmi-screen--widget-engineering-35-tools).
