---
name: tia-mcp-workflow
description: >-
  Route Siemens TIA Portal, WinCC Unified, HMI JSON, Figma-to-HMI, and
  S7-PLCSIM MCP requests to the right project skill. Use when deciding which
  TIA MCP workflow or skill applies before operating on a project or controller.
---

# TIA MCP Server Usage Workflow

Use this guide to select the focused skill before making TIA MCP calls. It is a
router, not a replacement for the selected skill: read that skill's full
instructions before acting, and use the live MCP tool descriptions for arguments.

## 1. Select the workflow

| User request | Read this skill |
| --- | --- |
| TIA connection/project status, devices, PLCs, blocks, tags, PLC edits or compile; direct WinCC Unified screen, widget, tag, connection, text-list, or HMI compile work; Siemens TestSuite | `tia-portal-openness` |
| HMI export, inspection, editing, rendering, patching, or import specifically using the TIA MCP JSON format | `hmi-json` |
| Recreate or adapt a Figma design into a WinCC Unified screen | `figma-to-wincc-unified`; also read `hmi-json` only if the user explicitly requests JSON export/import/patch work |
| Connect to S7-PLCSIM, inspect or write simulated tags, control the virtual CPU, or run an online sequence verification | `plcsim-sequence-tester` |

The skills are located at `.opencode/skills/<skill-name>/SKILL.md` for OpenCode
and `.agents/skills/<skill-name>/SKILL.md` for compatible agent tooling. When a
request spans workflows, read each applicable skill. For example, a Figma design
that must then be imported through HMI JSON uses both the Figma and HMI JSON
skills, with the HMI JSON authorization requirements still applying.

## 2. Establish the target before operating

1. For TIA or HMI work, read `tia-portal-openness` and check `tia_get_status`
   before project operations. Identify the active project and intended device,
   PLC, or HMI target; discover targets rather than guessing when ambiguous.
2. For PLCSIM work, read `plcsim-sequence-tester` and check
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
  first. Preview JSON imports and patches as described in `hmi-json`.
- Preserve existing HMI items when changing their behavior; use the in-place
  update workflow in `tia-portal-openness` rather than deleting and recreating
  them.
- For sequence verification, use the `plcsim-sequence-tester` workflow. Before
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
