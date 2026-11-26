---
name: siemens-tia-mcp-workflow
description: >-
  Route Siemens TIA Portal, WinCC Unified, HMI JSON, Figma-to-HMI, and
  S7-PLCSIM MCP requests to the right project skill. Use when deciding which
  TIA MCP workflow or skill applies before operating on a project or controller,
  and when checking the official Siemens documentation with the docs_* tools.
---

# TIA MCP Server Usage Workflow

Use this guide to select the focused skill before making TIA MCP calls. It is a
router, not a replacement for the selected skill: read that skill's full
instructions before acting, and use the live MCP tool descriptions for arguments.

## 1. Select the workflow

| User request | Read this skill |
| --- | --- |
| TIA connection/project status, devices, PLCs, blocks, tags, PLC edits or compile; direct WinCC Unified screen, widget, tag, connection, text-list, or HMI compile work; Siemens TestSuite | `siemens-tia-portal-openness` |
| HMI export, inspection, editing, rendering, patching, or import specifically using the TIA MCP JSON format | `siemens-hmi-json` |
| Recreate or adapt a Figma design into a WinCC Unified screen | `siemens-figma-to-wincc-unified`; also read `siemens-hmi-json` only if the user explicitly requests JSON export/import/patch work |
| Connect to S7-PLCSIM, inspect or write simulated tags, control the virtual CPU, or run an online sequence verification | `siemens-plcsim-sequence-tester` |
| Look up how a Siemens API, system function, SCL instruction, or setting works in the official documentation | This skill, section 5 below (no TIA connection needed) |
| Read current values from a running real PLC (not PLCSIM) by IP address | This skill, section 6 below (read-only, no TIA connection needed) |
| Add or change hardware (devices, modules, GSDML drives, PROFINET, IP addresses), read IO addresses, export/import AML, back up the project to files, archive or retrieve it, or export cross references | This skill, section 7 below |

OpenCode skill files are located at
`.opencode/skills/siemens-<skill-name>/SKILL.md`. Compatible agent skills remain
at `.agents/skills/<skill-name>/SKILL.md`. When a request spans workflows, read
each applicable skill. For example, a Figma design that must then be imported
through HMI JSON uses both the Figma and HMI JSON skills, with the HMI JSON
authorization requirements still applying.

## 2. Establish the target before operating

1. For TIA or HMI work, read `siemens-tia-portal-openness` and check `tia_get_status`
   before project operations. Identify the active project and intended device,
   PLC, or HMI target; discover targets rather than guessing when ambiguous.
2. For PLCSIM work, read `siemens-plcsim-sequence-tester` and check
   `plcsim_get_status` before simulation operations. Confirm that the target is
   the intended virtual controller, not a physical PLC.
3. Keep the persistent TIA and PLCSIM connections open. Do not disconnect,
   close, or tear down a session as routine cleanup. If a connection is missing,
   follow the selected skill's attach procedure rather than launching a
   duplicate instance.
4. Inspect the existing project objects, tags, and state needed for the request
   before changing or writing anything. Do not infer tag names or choose an
   arbitrary target.

## 3. Apply and verify the requested change

- Make only the requested change and follow the selected skill's authorization
  and safety requirements. In particular, HMI JSON use is opt-in: an ambiguous
  request to export or read an HMI is not authorization to use JSON; clarify
  first. Preview JSON imports and patches as described in `siemens-hmi-json`.
- Preserve existing HMI items when changing their behavior; use the in-place
  update workflow in `siemens-tia-portal-openness` rather than deleting and recreating
  them.
- For sequence verification, use the `siemens-plcsim-sequence-tester` workflow. Before
  starting a sequence, verify interlocks and readiness, reset faults as
  appropriate, and use the project's actual symbolic tags. Produce the
  required Markdown verification report with environment, recipe matrix,
  transition timings, telemetry, and pass/fail results.
- Compile the affected PLC or HMI after engineering changes using the
  appropriate skill's workflow. Report compiler results and diagnostics; do
  not claim success if verification failed.
- Do not save a project, import JSON, modify engineering objects, or write
  controller values unless the request and the selected skill authorize that
  action. Respect any more-specific safeguards in the selected skill.

## 4. Find exact tool arguments

The focused skills describe workflows and common tool use. For exact parameters
and return shapes, prefer the live MCP tool schema. Read the supporting MCP
resources when more context is useful:

- `tia://docs/tools-reference` for detailed tool arguments and return shapes.
- `tia://docs/skills-and-tools` for the server and skills overview.
- `tia://docs/agent-prompting-and-system-functions` for prompting guidance and
  WinCC Unified system functions.

If a documented tool is absent from the live tool list, verify the configured
server and tool availability instead of assuming a substitute.

## 5. Check the official Siemens documentation

The `docs_*` tools read the public Siemens documentation sites and need no TIA
Portal connection. Use them before relying on an Openness class or method, a
WinCC Unified `SysFct` or JavaScript API, or an SCL instruction you are not
sure about, and when a compile or import error names an API you do not know.

1. `docs_search` with a focused `query` and, when known, `product` and
   `version` (`TIAPortal` for the Openness API, `WinCC Unified`, `STEP 7`;
   `V21`). Use `docs_list_publications` if a filter value is rejected.
2. `docs_read_page` with the hit's reference and `mode: "outline"` first for
   long pages, then `section` or `mode: "code"` to read only what you need.
3. Narrow a follow-up search to one publication or chapter with `scope`.

Quote what the documentation says and cite the reference; if the documentation
and the server's own tool descriptions disagree, report the difference instead
of guessing.

## 6. Read online values from a real PLC

The `plc_online_*` tools read live values from a real S7-1200 or S7-1500 over
S7CommPlus. They need only the PLC's IP address, not TIA Portal, and are
read-only: there is no way to write values or change the operating state with
them. For S7-PLCSIM Advanced use the `plcsim_*` tools instead.

1. `plc_online_connect` with the `address` the user gave. Pass `password` only
   if the user provides one or the connection fails for access protection; never
   guess passwords. Check `plc_online_get_status` for the CPU identity and
   RUN/STOP state and confirm it is the PLC the user meant.
2. Find exact names with `plc_online_list_datablocks` and `plc_online_browse`
   (`filter` by data block or member name). Do not guess symbol names.
3. `plc_online_read_tags` with up to 200 full names per call. Read arrays whole
   or as `Name[3]`. Report each value with its quality; a `bad` quality or an
   `error` means the value is not known.
4. Leave the connection open for follow-up reads; `plc_online_disconnect` only
   when the user is done or wants another PLC.

Values are a snapshot at `readAt`. A production PLC may be controlling a live
process: say so if a value looks unexpected rather than suggesting changes.

## 7. Hardware, networks and project backup

These tools change the offline project only; never download to a device.

1. **Find the type identifier** with `tia_find_module_types` (e.g.
   `OrderNumber:6ES7 214-1AG40-0XB0` or `GSD:<gsdml file>.XML/DAP`). A GSDML
   device must be installed in TIA Portal first; Openness cannot install GSD
   files, so ask the user to do it (*Options > Manage general station
   description files*).
2. **Create and equip devices**: `tia_create_device` and `tia_plug_module` run
   as a preview by default (`dry_run=true`); show the result, then run with
   `dry_run=false`. Find free positions with `tia_list_plug_locations`.
   Deleting needs `tia_delete_hardware` with `confirm=true` after the preview.
3. **PROFINET**: `tia_create_subnet`, `tia_connect_to_subnet` for the PLC and
   every IO device, `tia_create_io_system` on the PLC, `tia_connect_to_io_system`
   for each IO device, then `tia_set_network_node` for IP addresses and
   PROFINET device names. Check with `tia_list_network`.
4. **Wire the program**: `tia_get_io_addresses` gives start byte and length in
   bits for modules and drive telegrams; use them for PLC tags.
5. Save with `tia_save_project` after hardware changes.
6. **Backup**: `tia_export_project_files` into the project repository's export
   folder gives stable files for Git; `tia_archive_project` makes a `.zap21`.
   Archive before `tia_import_aml`, which runs as a preview by default and
   cannot be undone.
7. **Cross references**: `tia_export_cross_references` writes JSONL that can be
   searched for every use of a tag, address or block.
