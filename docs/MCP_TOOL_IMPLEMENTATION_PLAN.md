# MCP Tool Implementation Plan

## 1. Scope and implementation principles

This document is the candidate tool specification and delivery roadmap for the existing TIA MCP Server. It describes tools for engineering projects containing controllers, libraries, operator-interface screens, and related artifacts. Treat each proposed capability as subject to validation against the repository's existing tools, TIA Portal V21 Openness APIs, and supported project types; do not assume every listed operation is available or safe to implement.

### Shared behavior

1. **Explicit context:** Every operation that reads or changes a project, library, device, block, screen, or other project-owned object must accept or resolve an explicit context. Reject ambiguous contexts rather than silently selecting the first open project or object.
2. **Stable identifiers:** Accept stable object IDs where available. Names may be accepted for convenience, but resolve them within the supplied context and return the resolved ID. Reject ambiguous name matches.
3. **Validation before mutation:** Validate required fields, ranges, object types, references, permissions, and current object state before making changes. Return field-specific validation errors.
4. **Safe mutations:** Make each operation atomic where the underlying API permits. For multi-object operations, return per-object outcomes and clearly state whether successful items were committed if others failed. Do not report success for a partial or rolled-back change.
5. **Explicit results:** Return structured data: operation status, affected object IDs/names, relevant values, warnings, and errors. Avoid success-shaped empty results when an operation failed.
6. **Concurrency:** Detect stale revisions or changed objects before writing. Return a conflict with enough current-state information for the caller to reread and retry.
7. **No implicit destructive behavior:** Require explicit IDs or exact selections for deletion, copying, replacement, and bulk updates. Never infer a target from a partial name.
8. **Consistent errors:** Distinguish invalid input, missing object, ambiguous reference, unsupported operation, permission failure, conflict, dependency failure, and underlying engineering-system failure. Include actionable details without exposing credentials or sensitive internals.
9. **Auditable changes:** For mutations, return a concise change summary and a correlation/operation ID. Where supported, preserve the underlying system's audit trail.
10. **Read-only discovery first:** Discovery tools should expose enough metadata for callers to construct valid subsequent requests. Mutating tools should not silently invent hardware identifiers, addresses, attributes, or object relationships.
11. **Testing:** For each tool, test valid inputs, missing and malformed inputs, nonexistent targets, ambiguous names, permission failures, stale revisions, dependency failures, and the reported result shape. Test destructive and bulk operations against disposable fixtures.

## 2. Library tools

### `open_global_library`
- **Inputs:** Library ID or exact name; optional access mode if supported.
- **Behavior:** Resolve the library, verify it is available and permitted, then open it in the requested mode. Do not create a library implicitly.
- **Result:** Library ID, name, version, access mode, and open status.
- **Failures:** Missing or ambiguous library, locked library, unsupported access mode, or permission failure.

### `close_global_library`
- **Inputs:** Open library ID; optional explicit save/discard choice if it has unsaved changes.
- **Behavior:** Check for unsaved changes. Require a clear save or discard decision when necessary; never discard implicitly.
- **Result:** Closed status and whether changes were saved.
- **Failures:** Library is not open, close is blocked, or save/discard fails.

### `list_global_libraries`
- **Inputs:** Optional name filter, page size, and continuation token.
- **Behavior:** Enumerate accessible global libraries with stable IDs and basic metadata.
- **Result:** Paginated library records and continuation token.
- **Failures:** Permission or enumeration errors; return no partial list as a complete result.

### `get_global_library`
- **Inputs:** Library ID or exact name.
- **Behavior:** Read the library's identity, version, state, and summary metadata without opening it for modification.
- **Result:** One library metadata record.
- **Failures:** Missing, ambiguous, inaccessible, or unreadable library.

### `get_project_library`
- **Inputs:** Engineering-project context.
- **Behavior:** Read the project's attached library metadata and its current status.
- **Result:** Attached library identity, version, and status; return an explicit “not attached” state when appropriate.
- **Failures:** Missing project context or failure to inspect the library.

### `search_global_library`
- **Inputs:** Library ID, query, optional object-type filters, and pagination.
- **Behavior:** Search names and supported metadata. Escape query syntax safely and apply filters before paging.
- **Result:** Matching object IDs, names, types, versions, and concise metadata.
- **Failures:** Invalid filters, missing library, or search failure.

### `search_project_library`
- **Inputs:** Project context, query, optional object-type filters, and pagination.
- **Behavior:** Search only the library attached to the supplied project.
- **Result:** Matching objects with IDs and types, plus paging information.
- **Failures:** Missing project, absent library, invalid filters, or search failure.

### `update_project_library_from_global`
- **Inputs:** Project context, source global-library ID, explicit version or update policy, and selected objects if partial update is supported.
- **Behavior:** Preview compatibility and conflicts; update only after validation. Preserve project-local changes unless the caller explicitly requests replacement.
- **Result:** Updated object list, skipped objects, conflicts, and resulting library version.
- **Failures:** Incompatible versions, locked project, unresolved conflicts, or partial update; report per-object results.

### `update_global_library_from_project`
- **Inputs:** Project context, destination global-library ID, selected objects or explicit full-update request, and target version policy.
- **Behavior:** Validate write permission and destination compatibility. Show or return a conflict summary before applying overwrites.
- **Result:** Published objects, skipped objects, conflicts, and resulting library version.
- **Failures:** Permission denial, incompatible objects, locked destination, or conflict.

### `list_available_faceplates`
- **Inputs:** Library or project context and optional filter.
- **Behavior:** List faceplates available for use in that context, with interface and version metadata.
- **Result:** Faceplate IDs, names, interfaces, versions, and source libraries.
- **Failures:** Missing context or inability to enumerate faceplates.

### `get_library_type_instances`
- **Inputs:** Library context and library-type ID.
- **Behavior:** Find instances of the specified library type in the selected scope.
- **Result:** Instance IDs, names, locations, and version/consistency state.
- **Failures:** Unknown type, missing context, or incomplete enumeration.

### `instantiate_library_type`
- **Inputs:** Target project/library context, library-type ID, instance name, optional parent, and explicitly supplied property overrides.
- **Behavior:** Verify type compatibility, name availability, and required interface values; create one instance without guessing omitted mandatory values.
- **Result:** Created instance ID, resolved name, applied overrides, and warnings.
- **Failures:** Duplicate name, incompatible type, missing required values, or creation failure.

### `instantiate_device_from_master_copy`
- **Inputs:** Target project, master-copy ID, destination name, and optional placement/connection settings.
- **Behavior:** Validate the master copy and target compatibility, instantiate it, then apply only valid explicit settings.
- **Result:** Device ID, name, source master-copy ID, and resulting configuration summary.
- **Failures:** Missing master copy, incompatible target, occupied placement, or invalid connection.

### `instantiate_module_from_master_copy`
- **Inputs:** Target device, module master-copy ID, destination name, and slot/location.
- **Behavior:** Verify module/device compatibility and slot availability before instantiation.
- **Result:** Module ID, device ID, slot, and configuration summary.
- **Failures:** Invalid slot, incompatible module, duplicate name, or dependency failure.

### `instantiate_sw_master_copy`
- **Inputs:** Target project, software master-copy ID, destination name, and optional parent.
- **Behavior:** Validate software compatibility and required dependencies, then instantiate without silently overwriting existing objects.
- **Result:** Created software object IDs and dependency summary.
- **Failures:** Missing or incompatible master copy, name collision, or dependency failure.

## 3. Cross-reference tools

### `get_cross_references`
- **Inputs:** Project context, source object ID, optional reference kinds and direction.
- **Behavior:** Find references to or from the object, normalize locations, and identify unresolved references separately.
- **Result:** Referencing object, reference kind, direction, and source location for each match.
- **Failures:** Unknown source object, unsupported reference kind, or incomplete index; disclose incompleteness.

### `get_callers`
- **Inputs:** Project context, block ID, optional direct/transitive depth and filters.
- **Behavior:** Traverse calls to the selected block. Prevent cycles and distinguish direct from indirect callers.
- **Result:** Caller blocks and call-site locations, with depth and traversal-completeness metadata.
- **Failures:** Unknown/non-callable block, invalid depth, or incomplete project analysis.

### `get_callees`
- **Inputs:** Project context, block ID, optional direct/transitive depth and filters.
- **Behavior:** Traverse calls made by the selected block, handling recursion and unresolved targets explicitly.
- **Result:** Called blocks, call-site locations, depth, and unresolved-call entries.
- **Failures:** Unknown/non-callable block, invalid depth, or incomplete project analysis.

## 4. Project-text tools

### `read_project_texts`
- **Inputs:** Project context, optional category, language, key/name filters, and pagination.
- **Behavior:** Read matching text entries without modifying the project. Preserve original text and language metadata.
- **Result:** Text IDs/keys, category, language, value, and paging information.
- **Failures:** Invalid language/category, missing project, or read failure.

### `write_project_texts`
- **Inputs:** Project context and a non-empty set of text entries with key, category, language, and value.
- **Behavior:** Validate uniqueness and lengths, then create/update entries atomically where possible. Require explicit overwrite behavior for conflicting keys.
- **Result:** Per-entry created/updated status and final IDs/keys.
- **Failures:** Invalid entries, conflict, unsupported language, or partial write; clearly identify committed items.

### `download_project_texts`
- **Inputs:** Project context, optional categories/languages, export format, and optional destination identifier.
- **Behavior:** Produce a portable export without changing source entries. Escape values according to the selected format.
- **Result:** Export metadata and either file/content reference or serialized payload, including counts and filters used.
- **Failures:** Unsupported format, invalid selection, export failure, or size limit.

### `get_project_text_categories`
- **Inputs:** Project context and optional language.
- **Behavior:** Enumerate categories present in the project and optionally report counts by language.
- **Result:** Category IDs/names and available-language/count metadata.
- **Failures:** Missing context or enumeration failure.

## 5. PLC project operations

### Reference-project comparison and transfer

#### `compare_with_reference_project`
- **Inputs:** Source project, reference project, optional object types/names, and comparison depth.
- **Behavior:** Compare selected objects by stable identity and relevant attributes/code. Classify added, removed, changed, and equal items; do not mutate either project.
- **Result:** Structured difference report with object paths and field-level differences where available.
- **Failures:** Incompatible project versions, inaccessible reference, or incomplete comparison.

#### `copy_from_reference_project`
- **Inputs:** Destination project, reference project, explicit source object IDs, destination mapping, and conflict policy.
- **Behavior:** Validate dependencies and destination conflicts first. Copy only explicitly selected objects; never replace by default.
- **Result:** Per-object copied/skipped/conflict status and new destination IDs.
- **Failures:** Missing dependency, name collision, incompatible object, or partial copy.

#### `read_from_reference_project`
- **Inputs:** Reference project and explicit object IDs or filters.
- **Behavior:** Read requested objects from the reference without changing it or the active project.
- **Result:** Object records with code/attributes available to the caller and source identity.
- **Failures:** Missing objects, unsupported object types, or read failure.

### Blocks, groups, and code formats

#### `create_block_group`
- **Inputs:** Project, exact group name, optional parent group and description.
- **Behavior:** Validate name and parent, then create a group without moving existing blocks unless explicitly requested.
- **Result:** Group ID, name, and parent.
- **Failures:** Duplicate name, invalid parent, or unsupported nesting.

#### `duplicate_in_plc`
- **Inputs:** Project, source object ID, destination name, optional parent, and explicit reference-rewrite policy.
- **Behavior:** Duplicate the selected PLC object, validate the new name, and report references that are copied, rewritten, or left unchanged.
- **Result:** New object ID and a summary of copied contents and reference handling.
- **Failures:** Unsupported object, name collision, invalid rewrite, or dependency failure.

#### `open_block_in_editor`
- **Inputs:** Project and block ID.
- **Behavior:** Request that the engineering environment open the block in its editor; do not change block contents.
- **Result:** Opened status, block identity, and editor/session reference if available.
- **Failures:** Missing block, unavailable editor, or access denial.

#### `import_lad_block`
- **Inputs:** Project, target block/group, LAD representation or supported file reference, and explicit overwrite policy.
- **Behavior:** Parse and validate networks, operands, and references before import. Require a clear policy for an existing target.
- **Result:** Imported block ID, network count, validation messages, and compile status if compilation is requested.
- **Failures:** Parse/validation error, unsupported instruction, unresolved operand, or conflict.

#### `import_nvt`
- **Inputs:** Project, target block, NVT payload/file reference, and explicit overwrite policy.
- **Behavior:** Validate the NVT format and target compatibility; preserve source text where possible.
- **Result:** Imported object identity, parsed item count, and warnings.
- **Failures:** Invalid format, incompatible target, or unresolved references.

#### `get_lad_block_code`
- **Inputs:** Project and block ID.
- **Behavior:** Read the block's LAD representation and associated metadata without changing it.
- **Result:** LAD content, block identity, and revision/compile metadata.
- **Failures:** Unknown block, unsupported language, or unavailable code.

#### `get_nvt_code`
- **Inputs:** Project and NVT object/block ID.
- **Behavior:** Return the stored NVT representation and metadata.
- **Result:** NVT content, identity, and revision.
- **Failures:** Unknown object or unsupported representation.

#### `get_nvt_list`
- **Inputs:** Project and optional parent/filter/page settings.
- **Behavior:** Enumerate NVT objects visible in the selected scope.
- **Result:** IDs, names, object types, and paging metadata.
- **Failures:** Invalid filter or enumeration failure.

#### `get_udt_code`
- **Inputs:** Project and UDT ID.
- **Behavior:** Read the type declaration in a stable, loss-minimizing representation.
- **Result:** UDT code/content, identity, and revision.
- **Failures:** Unknown UDT, unsupported representation, or read failure.

#### `import_udt`
- **Inputs:** Project, UDT declaration, destination name/group, and explicit conflict policy.
- **Behavior:** Parse fields and types, validate dependencies and naming, then create or explicitly replace the UDT.
- **Result:** UDT ID, imported fields, dependency status, and warnings.
- **Failures:** Invalid declaration, missing type dependency, duplicate name, or incompatible replacement.

### Tags, tag tables, and constants

#### `create_plc_tag`
- **Inputs:** Project, tag table, tag name, data type, address or symbolic assignment, and optional comment.
- **Behavior:** Validate datatype/address compatibility and uniqueness before creating the tag.
- **Result:** Tag ID, resolved address/type, and table identity.
- **Failures:** Invalid address/type, collision, or missing table.

#### `delete_plc_tag`
- **Inputs:** Project and exact tag ID.
- **Behavior:** Check references and require explicit confirmation/policy when deletion would break consumers.
- **Result:** Deleted ID and reference-impact summary.
- **Failures:** Missing tag, protected tag, or dependency block.

#### `create_plc_tag_table`
- **Inputs:** Project, table name, optional parent/group, and optional initial tags.
- **Behavior:** Validate name and initial entries; create as one transaction when possible.
- **Result:** Table ID and per-tag creation results.
- **Failures:** Duplicate table/tag names, invalid initial tag, or partial transaction.

#### `delete_plc_tag_table`
- **Inputs:** Project and exact table ID; explicit policy for contained tags.
- **Behavior:** Inspect contained tags and references; delete only under the supplied policy.
- **Result:** Deleted table and child-object counts.
- **Failures:** Non-empty/protected table, dependent references, or permission failure.

#### `get_plc_tag_info`
- **Inputs:** Project and tag ID or exact name plus table scope.
- **Behavior:** Resolve one tag and return its complete supported metadata.
- **Result:** Name, type, address, comment, table, and revision.
- **Failures:** Missing/ambiguous tag or unsupported metadata.

#### `create_user_constant`
- **Inputs:** Project, constant name, datatype, literal value, and optional comment.
- **Behavior:** Validate literal against datatype and namespace uniqueness, then create.
- **Result:** Constant ID and normalized literal/type.
- **Failures:** Invalid literal, duplicate name, or unsupported datatype.

#### `read_user_constant`
- **Inputs:** Project and constant ID or exact name.
- **Behavior:** Read the constant's declared type, literal, and metadata.
- **Result:** Constant record and revision.
- **Failures:** Missing or ambiguous constant.

#### `update_user_constant`
- **Inputs:** Project, constant ID, new value and/or supported metadata, expected revision.
- **Behavior:** Validate the new literal and revision; update only supplied fields.
- **Result:** Updated constant and changed fields.
- **Failures:** Type mismatch, stale revision, or read-only constant.

#### `delete_user_constant`
- **Inputs:** Project and exact constant ID.
- **Behavior:** Check references and delete only if allowed by explicit policy.
- **Result:** Deleted ID and dependency summary.
- **Failures:** Missing/protected constant or unresolved references.

#### `create_tagtable_group`
- **Inputs:** Project, group name, optional parent group.
- **Behavior:** Validate hierarchy and name, then create the tag-table group.
- **Result:** Group ID and parent.
- **Failures:** Duplicate name or invalid parent.

### Blocks, attributes, and type metadata

#### `get_block_attributes`
- **Inputs:** Project and block ID.
- **Behavior:** Return supported block attributes and their mutability/read-only status.
- **Result:** Attribute/value map plus block revision.
- **Failures:** Unknown block or unsupported block type.

#### `get_block_info`
- **Inputs:** Project and block ID or exact scoped name.
- **Behavior:** Return identity, kind, language, interface summary, version, and compile state.
- **Result:** One block information record.
- **Failures:** Missing/ambiguous block.

#### `get_db_member_attributes`
- **Inputs:** Project, DB ID, and member ID/path.
- **Behavior:** Read supported member attributes and datatype details.
- **Result:** Member path, type, attributes, and revision.
- **Failures:** Unknown DB/member or unsupported member kind.

#### `set_db_member_attributes`
- **Inputs:** Project, DB/member IDs, allowed attribute/value pairs, expected revision.
- **Behavior:** Validate writable properties and datatype constraints, then apply the update atomically.
- **Result:** Updated attributes and revision.
- **Failures:** Unknown/read-only property, invalid value, stale revision, or compile constraint.

#### `get_tag_table_attributes`
- **Inputs:** Project and tag-table ID.
- **Behavior:** Return supported table attributes and mutability metadata.
- **Result:** Attribute/value map and revision.
- **Failures:** Unknown table.

#### `get_tag_table_info`
- **Inputs:** Project and tag-table ID or exact scoped name.
- **Behavior:** Return table identity, parent, contained-tag count, and table metadata.
- **Result:** One table information record.
- **Failures:** Missing or ambiguous table.

#### `set_tag_table_attributes`
- **Inputs:** Project, table ID, writable attribute/value pairs, expected revision.
- **Behavior:** Validate each attribute and update only those explicitly supplied.
- **Result:** Updated values and revision.
- **Failures:** Read-only/invalid attribute, stale revision, or naming conflict.

#### `get_type_info`
- **Inputs:** Project and type ID or exact name.
- **Behavior:** Return type identity, kind, version, field/interface summary, and dependencies.
- **Result:** Type information record.
- **Failures:** Missing/ambiguous type or unsupported type.

#### `get_type_attributes`
- **Inputs:** Project and type ID.
- **Behavior:** Enumerate type attributes, current values, allowed values, and whether each is writable.
- **Result:** Attribute descriptors and revision.
- **Failures:** Unknown type or unsupported attribute metadata.

#### `set_type_properties`
- **Inputs:** Project, type ID, explicit writable property/value pairs, expected revision.
- **Behavior:** Validate against the type's declared property metadata; reject guessed or unsupported property names.
- **Result:** Updated properties and resulting revision.
- **Failures:** Invalid/read-only property, datatype mismatch, stale revision, or dependent compile error.

#### `set_block_properties`
- **Inputs:** Project, block ID, explicit writable property/value pairs, expected revision.
- **Behavior:** Validate supported block properties and apply them without changing code unless requested.
- **Result:** Updated values and revision.
- **Failures:** Unknown/read-only property, stale revision, or invalid value.

### Rename and type/property operations

#### `rename_block`
- **Inputs:** Project, block ID, new name, expected revision, and explicit reference-update choice.
- **Behavior:** Check namespace uniqueness and update references only when supported and requested.
- **Result:** Old/new names and reference-update summary.
- **Failures:** Invalid/duplicate name, stale revision, or references that cannot be safely rewritten.

#### `rename_udt`
- **Inputs:** Project, UDT ID, new name, expected revision, explicit reference-update choice.
- **Behavior:** Validate namespace and dependent declarations before renaming.
- **Result:** New identity/name and affected-reference summary.
- **Failures:** Duplicate name, stale revision, or incompatible dependent references.

#### `rename_tag_table`
- **Inputs:** Project, table ID, new name, expected revision.
- **Behavior:** Validate uniqueness in the correct scope and preserve table contents.
- **Result:** Old/new names and revision.
- **Failures:** Duplicate/invalid name or stale revision.

### Project-level PLC support

#### `create_project`
- **Inputs:** Destination location, project name, supported template/version options.
- **Behavior:** Validate location and name, create the project, and return its stable identity. Do not overwrite an existing project.
- **Result:** Project ID, name, location, and initial status.
- **Failures:** Invalid location/name, existing destination, unsupported version, or creation failure.

#### `get_project_intellisense`
- **Inputs:** Project context and optional language/block scope.
- **Behavior:** Build or retrieve symbols, types, functions, and valid completions for that scope.
- **Result:** Structured completion/symbol data with source locations and freshness metadata.
- **Failures:** Unsupported language, invalid scope, or unavailable/stale index.

#### `get_plc_textlists`
- **Inputs:** Project context and optional language/filter/page settings.
- **Behavior:** Enumerate PLC text lists separately from general project text entries.
- **Result:** Text-list IDs, names, languages, entry counts, and paging metadata.
- **Failures:** Invalid filter or enumeration failure.

## 6. HMI engineering tools

### Screen structure, devices, and reusable assets

#### `create_hmi_screen_under_group`
- **Inputs:** HMI context, parent group ID, screen name, optional properties.
- **Behavior:** Validate parent and name, create the screen inside that group.
- **Result:** Screen ID, group ID, and applied properties.
- **Failures:** Missing group, duplicate name, or invalid screen properties.

#### `hmi_create_screen_group`
- **Inputs:** HMI context, group name, optional parent group.
- **Behavior:** Validate hierarchy and uniqueness, then create the group.
- **Result:** Group ID and parent.
- **Failures:** Duplicate name or invalid parent.

#### `hmi_create_template`
- **Inputs:** HMI context, template name, optional base template and supported properties.
- **Behavior:** Validate inherited properties and references, then create a reusable template.
- **Result:** Template ID, name, base, and property summary.
- **Failures:** Invalid inheritance, name conflict, or unsupported property.

#### `hmi_create_device`
- **Inputs:** HMI context, device name/type, target connection settings.
- **Behavior:** Validate runtime/device type and connection settings; create without assuming a target.
- **Result:** Device ID, type, and connection status.
- **Failures:** Unsupported type, invalid connection, or duplicate name.

#### `hmi_create_user`
- **Inputs:** HMI context, user name, explicitly supplied role/group assignments, and credential reference if required.
- **Behavior:** Create the user without returning secrets. Enforce password/credential handling through a secure credential channel.
- **Result:** User ID, assigned roles, and status; never return a password.
- **Failures:** Duplicate user, invalid role, weak/invalid credential, or permission denial.

#### `hmi_create_navigation_button`
- **Inputs:** Screen ID, button properties, destination screen/action, optional style.
- **Behavior:** Validate the destination and button properties, then create a button with an explicit navigation action.
- **Result:** Item ID and resolved destination/action.
- **Failures:** Missing destination, invalid style/property, or duplicate item identity.

#### `hmi_duplicate_screen`
- **Inputs:** Source screen ID, target group, new name, and explicit policy for copying scripts/dynamics.
- **Behavior:** Copy supported children and properties, remap internal references, and report external references left unchanged.
- **Result:** New screen ID and copied/omitted child counts.
- **Failures:** Name collision, unsupported child, or reference-remapping failure.

#### `hmi_rename_screen`
- **Inputs:** Screen ID, new name, expected revision.
- **Behavior:** Validate uniqueness and preserve screen identity and links.
- **Result:** Old/new names and revision.
- **Failures:** Duplicate name or stale revision.

#### `hmi_rename_screen_item`
- **Inputs:** Screen ID, item ID, new name, expected revision.
- **Behavior:** Validate name uniqueness within the screen and preserve bindings/events.
- **Result:** Old/new names and revision.
- **Failures:** Missing item, collision, or stale revision.

#### `hmi_create_graphiclist`
- **Inputs:** HMI context, list name, entries/assets, optional language or resolution metadata.
- **Behavior:** Validate asset references and entry uniqueness, then create the list.
- **Result:** Graphic-list ID and entry results.
- **Failures:** Missing asset, invalid entry, or name conflict.

#### `hmi_get_graphiclists`
- **Inputs:** HMI context, optional filter and paging.
- **Behavior:** Enumerate graphic lists and summarize entries.
- **Result:** IDs, names, counts, metadata, and continuation token.
- **Failures:** Invalid filter or enumeration failure.

#### `hmi_update_graphiclist`
- **Inputs:** Graphic-list ID, explicit entry additions/updates/removals, expected revision.
- **Behavior:** Validate all referenced assets and apply the requested patch atomically where possible.
- **Result:** Changed entry IDs and new revision.
- **Failures:** Missing list/asset, duplicate key, or stale revision.

#### `hmi_delete_graphiclist`
- **Inputs:** Graphic-list ID and explicit reference policy.
- **Behavior:** Check screen/item references; do not break references silently.
- **Result:** Deleted ID and reference-impact summary.
- **Failures:** Referenced/protected list or permission failure.

#### `set_faceplate_interfaces`
- **Inputs:** Faceplate type ID and a complete explicit interface definition or patch.
- **Behavior:** Validate interface names, directions, datatypes, and compatibility with existing uses.
- **Result:** Applied interface and compatibility summary.
- **Failures:** Invalid type, duplicate interface member, incompatible use, or stale revision.

#### `set_faceplate_type`
- **Inputs:** Faceplate instance ID and target faceplate type ID.
- **Behavior:** Verify interface compatibility and map only explicitly supplied values.
- **Result:** Updated instance type and mapping report.
- **Failures:** Incompatible interfaces, missing required values, or invalid target.

### Resource lists

#### `hmi_create_resource_list_dynamic`
- **Inputs:** HMI context, resource-list name, entries, and explicit dynamic condition/selection.
- **Behavior:** Validate resource references and condition syntax, then create the dynamic list.
- **Result:** List ID and validated entries/condition.
- **Failures:** Missing resource, invalid condition, or duplicate name.

#### `hmi_create_screen_resource_list_dynamic`
- **Inputs:** Screen ID, item/property target, resource-list definition, and condition.
- **Behavior:** Validate the target property and resource compatibility before binding.
- **Result:** Dynamic binding ID and resolved target.
- **Failures:** Missing target, incompatible resource, or invalid condition.

#### `hmi_update_resource_list_dynamic`
- **Inputs:** Dynamic resource-list ID, explicit patch, expected revision.
- **Behavior:** Validate new entries/conditions and preserve unchanged entries.
- **Result:** Updated list and changed-entry summary.
- **Failures:** Invalid resource/condition or stale revision.

#### `hmi_update_screen_resource_list_dynamic`
- **Inputs:** Screen binding ID, explicit patch, expected revision.
- **Behavior:** Validate target and resource compatibility before updating the binding.
- **Result:** Updated binding and revision.
- **Failures:** Missing binding, incompatible target, or stale revision.

#### `hmi_delete_resource_list_dynamic`
- **Inputs:** Dynamic resource-list ID.
- **Behavior:** Check consumers, then delete only under an explicit reference policy.
- **Result:** Deleted ID and consumer summary.
- **Failures:** Referenced/protected list or missing object.

#### `hmi_delete_screen_resource_list_dynamic`
- **Inputs:** Screen binding ID.
- **Behavior:** Remove only the selected binding; leave its resources intact.
- **Result:** Deleted binding ID and screen identity.
- **Failures:** Missing binding or permission failure.

### Scripts and event definitions

#### `hmi_create_global_script_module`
- **Inputs:** HMI context, module name, language, source, and optional exports.
- **Behavior:** Validate syntax and uniqueness; create the module without executing it.
- **Result:** Module ID, source revision, exports, and validation diagnostics.
- **Failures:** Parse error, unsupported language/API, or duplicate name.

#### `hmi_update_global_script_module`
- **Inputs:** Module ID, source and/or metadata patch, expected revision.
- **Behavior:** Validate new source before saving; do not run it during update.
- **Result:** Updated revision, diagnostics, and changed fields.
- **Failures:** Syntax/API validation error or stale revision.

#### `hmi_delete_global_script_module`
- **Inputs:** Module ID and explicit policy for dependent scripts.
- **Behavior:** Find references and refuse unsafe deletion unless the caller explicitly resolves them.
- **Result:** Deleted ID and dependent-reference summary.
- **Failures:** Referenced module, protected module, or missing ID.

#### `hmi_get_single_global_script_dynamic_by_name`
- **Inputs:** HMI context, exact script/dynamic name.
- **Behavior:** Resolve exactly one matching global dynamic and return its definition.
- **Result:** Dynamic ID, definition, source/condition, and revision.
- **Failures:** No match or multiple matches.

#### `hmi_list_all_global_script_dynamic_names`
- **Inputs:** HMI context and optional page settings.
- **Behavior:** Enumerate names and IDs without returning full script bodies.
- **Result:** Paginated names/IDs and continuation token.
- **Failures:** Enumeration failure or invalid paging.

#### `hmi_get_supported_event`
- **Inputs:** HMI context and optional screen/item type.
- **Behavior:** Return events supported for the supplied object type and runtime.
- **Result:** Event names, argument schemas, and applicability.
- **Failures:** Unsupported object type or inability to determine runtime capabilities.

#### `hmi_get_screen_item_events`
- **Inputs:** Screen ID and item ID.
- **Behavior:** Read event bindings for the item without modifying them.
- **Result:** Event names, handlers, script references, and revision.
- **Failures:** Missing item or unsupported item type.

#### `hmi_create_screen_item_event`
- **Inputs:** Screen/item ID, supported event, handler/action definition.
- **Behavior:** Validate the event against the item's supported-event list; create one binding.
- **Result:** Event-binding ID, event name, and handler reference.
- **Failures:** Unsupported event, invalid handler, or duplicate binding.

#### `hmi_create_screen_level_event_definition`
- **Inputs:** Screen ID, supported event, handler definition.
- **Behavior:** Validate screen-level event support and handler references, then bind.
- **Result:** Definition ID and resolved handler.
- **Failures:** Unsupported event, invalid handler, or duplicate definition.

#### `hmi_check_screen_level_event_definition`
- **Inputs:** Screen ID and proposed or existing event definition.
- **Behavior:** Validate event support, required arguments, handler references, and conflicts without changing the screen.
- **Result:** Valid/invalid status with field-specific diagnostics.
- **Failures:** Missing screen or inability to validate.

#### `hmi_create_screen_prop_dyn_definition`
- **Inputs:** Screen ID, property path, expression/condition, and expected value type.
- **Behavior:** Validate that the property is dynamically writable and expression output is type-compatible.
- **Result:** Definition ID, normalized expression, and diagnostics.
- **Failures:** Unsupported property, invalid expression, or type mismatch.

#### `hmi_check_screen_prop_dyn_definition`
- **Inputs:** Screen ID and proposed/existing property dynamic.
- **Behavior:** Perform validation only; do not save or execute the expression.
- **Result:** Validation status and diagnostics.
- **Failures:** Missing screen or validator failure.

### Script and tag dynamizations

#### `hmi_create_screen_tag_dynamic`
- **Inputs:** Screen/item/property target, tag ID, and binding mode.
- **Behavior:** Validate tag datatype and target-property compatibility, then bind the tag.
- **Result:** Dynamic-binding ID and resolved tag/target.
- **Failures:** Missing tag/target or incompatible type.

#### `hmi_update_screen_tag_dynamic`
- **Inputs:** Binding ID and explicit tag/property/mode patch, expected revision.
- **Behavior:** Validate the new mapping and update without changing the tag itself.
- **Result:** Updated binding and revision.
- **Failures:** Invalid mapping or stale revision.

#### `hmi_delete_screen_tag_dynamic`
- **Inputs:** Binding ID.
- **Behavior:** Remove the binding only; preserve the tag and screen item.
- **Result:** Deleted binding ID.
- **Failures:** Missing or protected binding.

#### `hmi_create_script_dynamic`
- **Inputs:** HMI context, dynamic name, script/condition, target type, and explicit execution trigger.
- **Behavior:** Validate syntax, supported APIs, and trigger compatibility; store without executing.
- **Result:** Dynamic ID and validation diagnostics.
- **Failures:** Invalid syntax/API, unsupported trigger, or duplicate name.

#### `hmi_update_script_dynamic`
- **Inputs:** Dynamic ID, source/condition patch, expected revision.
- **Behavior:** Validate the updated definition before saving.
- **Result:** Updated revision and diagnostics.
- **Failures:** Validation error or stale revision.

#### `hmi_delete_script_dynamic`
- **Inputs:** Dynamic ID.
- **Behavior:** Check references and delete only the selected dynamic.
- **Result:** Deleted ID and consumer summary.
- **Failures:** Referenced/protected dynamic or missing ID.

#### `hmi_create_screen_script_dynamic`
- **Inputs:** Screen ID, script/condition, trigger, and optional target property.
- **Behavior:** Validate screen-level applicability and source, then create a screen-scoped dynamic.
- **Result:** Dynamic ID and diagnostics.
- **Failures:** Invalid trigger/source or missing screen.

#### `hmi_update_screen_script_dynamic`
- **Inputs:** Screen dynamic ID, explicit patch, expected revision.
- **Behavior:** Validate and update the selected definition.
- **Result:** Updated definition and revision.
- **Failures:** Invalid patch or stale revision.

#### `hmi_delete_screen_script_dynamic`
- **Inputs:** Screen dynamic ID.
- **Behavior:** Remove the selected dynamic and report dependent references.
- **Result:** Deleted ID.
- **Failures:** Missing/protected dynamic or dependent-object constraint.

#### `hmi_create_screen_script_dynamic_on_event`
- **Inputs:** Screen ID, supported event, script source/handler.
- **Behavior:** Validate event support and script contract before binding the handler.
- **Result:** Dynamic/event-binding ID and diagnostics.
- **Failures:** Unsupported event, invalid script, or duplicate binding.

#### `hmi_update_screen_script_dynamic_on_event`
- **Inputs:** Event dynamic ID, handler/source patch, expected revision.
- **Behavior:** Validate the handler against the event signature, then update.
- **Result:** Updated binding and revision.
- **Failures:** Signature mismatch or stale revision.

#### `hmi_delete_screen_script_dynamic_on_event`
- **Inputs:** Event dynamic ID.
- **Behavior:** Delete only that event binding.
- **Result:** Deleted binding ID.
- **Failures:** Missing/protected binding.

#### `hmi_create_screen_script_dynamic_on_property`
- **Inputs:** Screen ID, property path, expression/source, expected value type.
- **Behavior:** Check property mutability and output type before creating the binding.
- **Result:** Property dynamic ID and diagnostics.
- **Failures:** Unsupported property, invalid expression, or type mismatch.

#### `hmi_update_screen_script_dynamic_on_property`
- **Inputs:** Property dynamic ID, expression/source patch, expected revision.
- **Behavior:** Validate the expression and preserve unchanged binding settings.
- **Result:** Updated binding and revision.
- **Failures:** Invalid expression or stale revision.

#### `hmi_delete_screen_script_dynamic_on_property`
- **Inputs:** Property dynamic ID.
- **Behavior:** Remove the selected property binding without deleting the property or item.
- **Result:** Deleted binding ID.
- **Failures:** Missing/protected binding.

#### `hmi_create_screen_item_script_dynamic_on_property`
- **Inputs:** Screen/item ID, property path, expression/source, expected value type.
- **Behavior:** Validate item-specific property support and expression type before binding.
- **Result:** Binding ID and diagnostics.
- **Failures:** Missing item, unsupported property, or type mismatch.

#### `hmi_update_screen_item_script_dynamic_on_property`
- **Inputs:** Item-property binding ID, expression/source patch, expected revision.
- **Behavior:** Validate and update the selected binding.
- **Result:** Updated binding and revision.
- **Failures:** Invalid expression or stale revision.

#### `hmi_delete_screen_item_script_dynamic_on_property`
- **Inputs:** Item-property binding ID.
- **Behavior:** Remove only the binding.
- **Result:** Deleted binding ID.
- **Failures:** Missing/protected binding.

#### `hmi_update_screen_item_script_dynamic_on_event`
- **Inputs:** Item event-binding ID, handler/source patch, expected revision.
- **Behavior:** Validate the handler against the item's event signature and update.
- **Result:** Updated handler/binding and revision.
- **Failures:** Unsupported event, invalid handler, or stale revision.

#### `hmi_delete_screen_item_script_dynamic_on_event`
- **Inputs:** Item event-binding ID.
- **Behavior:** Remove only the selected handler binding.
- **Result:** Deleted binding ID.
- **Failures:** Missing/protected binding.

#### `delete_screen_dynamic`
- **Inputs:** Screen dynamic ID.
- **Behavior:** Delete the exact dynamic definition, after checking references.
- **Result:** Deleted ID and reference summary.
- **Failures:** Missing ID, dependent references, or permission failure.

#### `delete_screen_item_dynamic`
- **Inputs:** Screen ID, item ID, and dynamic ID.
- **Behavior:** Verify ownership and delete only the selected item dynamic.
- **Result:** Deleted dynamic ID.
- **Failures:** Ownership mismatch, missing object, or reference constraint.

#### `hmi_delete_from_collection_screen_item`
- **Inputs:** Screen/item ID, collection property, and exact member ID.
- **Behavior:** Remove one member from a supported collection; do not replace the whole collection.
- **Result:** Removed member ID and remaining count.
- **Failures:** Unsupported collection, missing member, or protected collection.

### Tags, tables, and text lists

#### `hmi_create_tag_table`
- **Inputs:** HMI context, table name, optional folder and metadata.
- **Behavior:** Validate uniqueness and create an empty table or explicitly supplied initial entries.
- **Result:** Table ID, name, and entry count.
- **Failures:** Duplicate name or invalid metadata.

#### `hmi_create_tag_dynamic`
- **Inputs:** HMI tag ID, acquisition/binding configuration, and explicit trigger/condition.
- **Behavior:** Validate tag type and runtime support, then create the dynamic behavior without executing it.
- **Result:** Dynamic ID and normalized configuration.
- **Failures:** Invalid tag/configuration or unsupported trigger.

#### `hmi_update_tag`
- **Inputs:** Tag ID, writable property/value patch, expected revision.
- **Behavior:** Validate each property and preserve unspecified values.
- **Result:** Updated tag and revision.
- **Failures:** Unknown/read-only property, invalid value, or stale revision.

#### `hmi_delete_tag`
- **Inputs:** Tag ID and explicit policy for existing bindings.
- **Behavior:** Check screen, alarm, logging, and script references; refuse unsafe deletion unless the caller resolves them.
- **Result:** Deleted ID and affected-reference summary.
- **Failures:** Referenced/protected tag or missing ID.

#### `hmi_update_tag_dynamic`
- **Inputs:** Dynamic ID and explicit configuration patch, expected revision.
- **Behavior:** Validate tag compatibility and update only the selected dynamic.
- **Result:** Updated dynamic and revision.
- **Failures:** Invalid configuration or stale revision.

#### `hmi_delete_tag_dynamic`
- **Inputs:** Tag-dynamic ID.
- **Behavior:** Remove the dynamic binding, not the underlying tag.
- **Result:** Deleted dynamic ID.
- **Failures:** Missing/protected binding.

#### `hmi_create_textlist`
- **Inputs:** HMI context, list name, language set, entries, and optional values.
- **Behavior:** Validate language coverage, unique entry keys, and value constraints before creation.
- **Result:** Text-list ID and entry/language counts.
- **Failures:** Invalid language, duplicate key, or name conflict.

#### `hmi_update_textlist`
- **Inputs:** Text-list ID, explicit entry/language patch, expected revision.
- **Behavior:** Apply only supplied changes and preserve untouched translations.
- **Result:** Updated list, changed entries, and revision.
- **Failures:** Missing entry/list, invalid translation, or stale revision.

## 7. Repository-aware delivery roadmap

The inventory above is intentionally broad. Deliver it in small, releasable slices rather than implementing every proposed tool in one pass. Each milestone below is a checklist and a gate: defer individual tools when the installed Siemens API does not support them or when their behavior cannot be validated safely.

### Milestone 0 — Scope, overlap, and API feasibility

**Goal:** Decide what is genuinely new and technically supportable before adding public MCP tools.

- [ ] Inventory existing MCP tool names, schemas, handlers, and manager methods; map each proposed tool to **existing / extend / new / defer**.
- [ ] Identify overlapping tools and choose whether to improve the existing contract or add a new name. Do not register duplicate or alias tools without a concrete compatibility need.
- [ ] For each new or extended capability, identify the exact V21 Openness object/API and supported object types. Record unsupported APIs and runtime prerequisites.
- [ ] Classify mutations by reversibility, dependency impact, and whether TIA provides transactions, stable IDs, revision tokens, or reliable rollback.
- [ ] Agree on the first release slice and explicitly defer the rest; do not treat the number of proposed tools as a release requirement.

**Done when:** There is a reviewed mapping and a short feasibility note for every proposed tool in scope; unsupported capabilities are explicitly deferred rather than approximated.

**Delegation boundary:** API research can be split by Library, PLC, HMI, and Project/Text domains. One coordinator owns the mapping and resolves overlaps.

### Milestone 1 — Shared MCP contracts and test seam

**Goal:** Establish only the shared behavior that the first implementation slices need.

- [ ] Define consistent success/error payloads for validation, missing/ambiguous targets, unsupported operations, conflicts, dependency failures, and Siemens API failures.
- [ ] Standardize typed schema requirements, optional/default handling, bounded list results, and pagination only for collections that need it.
- [ ] Define project/device/object resolution using capabilities the current TIA API actually exposes. Return resolved names and IDs where available; do not invent stable IDs or revision numbers.
- [ ] Define safe mutation conventions: validate before writing, state partial-commit behavior, and provide operation summaries. Add expected-revision checks only where a trustworthy revision source exists.
- [ ] Add focused automated tests for shared parsing, validation, serialization, and error/result shapes without requiring a live TIA Portal instance.
- [ ] Keep registration and cross-domain contract changes coordinated; avoid parallel edits to the same central registry file.

**Done when:** A representative read tool and mutation tool can use the shared conventions, and their contract tests pass. No broad abstraction is added without a current consumer.

**Depends on:** Milestone 0.

### Milestone 2 — Read-only discovery: PLC and project structure

**Goal:** Implement dependable reads that let callers identify targets before changing them.

- [ ] Implement the feasible PLC metadata reads: block/type/tag-table/tag information, block attributes/info, DB member attributes, tag-table attributes/info, type information/attributes, and PLC text-list discovery.
- [ ] Implement project/device discovery needed to resolve and scope these reads.
- [ ] For unavailable metadata, return an explicit unsupported/incomplete result rather than guessing or silently omitting it.
- [ ] Add tests for valid reads, missing targets, ambiguous names, unsupported types, and result shape.

**Done when:** Each delivered read has a scoped target, predictable structured output, and focused tests; the user can discover valid targets without relying on mutation tools.

**Depends on:** Milestones 0–1.

**Delegation boundary:** PLC block/type reads and tag/table reads can be implemented in separate `TiaManager` partial files. Coordinate any edits to `ToolRegistry` through one integrator.

### Milestone 3 — Read-only discovery: libraries, references, texts, and HMI

**Goal:** Complete the read surface before broadening mutation scope.

- [ ] Implement feasible global/project library metadata and search/list operations, available faceplates, and library-type instance discovery.
- [ ] Implement cross-reference, caller/callee, and project/reference-project reads only where V21 APIs provide sufficiently complete results; disclose incomplete traversal/indexing.
- [ ] Implement project text/category reads and portable text downloads for formats supported by the project API.
- [ ] Implement HMI screen/group/template/device/asset and event/dynamic discovery that the current Unified APIs expose.
- [ ] Add focused tests for filters, paging, empty results, missing/ambiguous targets, and incomplete results.

**Done when:** Each delivered read reports its scope and completeness accurately, has tests, and introduces no project mutations.

**Depends on:** Milestones 0–1. Independent domain work can run in parallel if code ownership is separated.

### Milestone 4 — Low-risk PLC and project-content mutations

**Goal:** Add narrowly scoped create/update/rename operations with explicit validation and observable outcomes.

- [ ] Implement the feasible tag, tag-table/group, and user-constant create/update operations.
- [ ] Implement supported block/UDT rename and property updates only after confirming reference behavior and writable attributes.
- [ ] Implement project-text writes and project creation only when supported by the actual API; require explicit overwrite behavior for text conflicts and never overwrite an existing project destination.
- [ ] Validate names, types, addresses, values, parent/target compatibility, and collisions before mutation.
- [ ] Return the affected identity, normalized values, warnings, and whether any operation was partial.
- [ ] Add focused tests for invalid input, collision, missing dependency, API failure, and persisted-state/read-back where a disposable project is available.

**Done when:** Each operation is individually tested and can be verified by a subsequent read; unsupported atomicity or revision checking is stated rather than implied.

**Depends on:** Milestones 1–2 and the corresponding read support in Milestone 2.

### Milestone 5 — PLC destructive, replacement, and transfer operations

**Goal:** Implement higher-risk operations only after impact checks and recovery behavior are proven.

- [ ] Implement deletes for tags, tables, constants, and blocks with exact target resolution and dependency/reference checks.
- [ ] Implement import/replace, duplication, master-copy instantiation, and reference-project copy operations only for validated formats and object types.
- [ ] Implement global/project library open/close, synchronization, and publish operations only with explicit save/discard/overwrite policies and a conflict preview.
- [ ] Implement compare/copy/update workflows with an explicit preview or conflict result before applying changes.
- [ ] Test rollback/restoration and partial completion against disposable projects; report exactly what committed if the platform cannot provide a transaction.
- [ ] Require explicit overwrite, delete, and conflict policies; never silently replace an existing object.

**Done when:** Destructive and transfer tests verify both the returned impact summary and the resulting project state, including failure paths.

**Depends on:** Milestones 2 and 4; do not begin an operation until its dependency and recovery behavior is understood.

### Milestone 6 — HMI structure and content operations

**Goal:** Extend HMI capability in small groups while preserving existing screens and bindings.

- [ ] Add supported screen/group/template/device creation, duplication, and rename operations with exact target resolution.
- [ ] Add graphic/resource-list operations only for asset types and collection APIs verified in V21.
- [ ] Add supported tag/text-list updates and faceplate/interface operations after confirming type compatibility and preservation of existing bindings.
- [ ] Add HMI user creation only if credentials can be supplied through a secure channel; never return or log credential values.
- [ ] Preserve the existing in-place update approach for screen items and scripts; do not delete/recreate items as a shortcut.
- [ ] Add dry-run/read-back or compile verification where supported, and focused tests for references, collisions, and partial outcomes.

**Done when:** Each implemented HMI mutation preserves unrelated item identity/layout/bindings and is verified through read-back or compile diagnostics.

**Depends on:** Milestones 1 and 3. Split by HMI object family, but use one owner for shared HMI helpers and tool registration.

### Milestone 7 — HMI events, scripts, and dynamics

**Goal:** Add programmable HMI behavior with explicit validation and no execution during authoring.

- [ ] Implement supported event and dynamic discovery/validation first.
- [ ] Add create/update/delete operations for global scripts, screen/item events, tag dynamics, and property dynamics only where the V21 API supports inspection and in-place updates.
- [ ] Validate event signatures, writable property/type compatibility, references, and supported runtime APIs before saving.
- [ ] Do not execute user-supplied scripts during validation or authoring; surface compiler/runtime validation limitations clearly.
- [ ] Test round-trip read-back, duplicate bindings, stale/missing references, and compile failures on disposable HMI projects.

**Done when:** The operation cannot silently bind an unsupported event/property, and successful writes can be read back and compiled where supported.

**Depends on:** Milestones 3 and 6.

### Milestone 8 — Integration, documentation, and release

**Goal:** Make the delivered slice safe to consume and maintain.

- [ ] Run focused tests plus the repository build; run live integration tests on disposable V21 projects for all changed Siemens-facing operations.
- [ ] Verify MCP tool names, schemas, required fields, errors, bounded outputs, and documented side effects using the actual protocol surface.
- [ ] Update `TOOLS_REFERENCE.md`, `SKILLS_AND_TOOLS.md`, relevant schemas/examples, and user-facing descriptions for tools that ship.
- [ ] Verify failure logs and responses do not disclose credentials or sensitive project data.
- [ ] Review partial-failure semantics, connection persistence, and compatibility with existing tools.
- [ ] For every code or behavior release, bump the `/status` server version using the running endpoint when available, otherwise the repository's reported-version source; keep project version metadata aligned.
- [ ] Record deferred tools and known API limitations so the next implementation slice starts from an explicit backlog.

**Done when:** Build and relevant tests pass, live engineering behavior is verified for the shipped slice, docs match the registered tools, and version metadata is aligned.

**Depends on:** The implementation milestones for the release slice.

## 8. Suggested agent work packages

Use these as bounded tasks for separate implementation agents. Start domain agents after Milestone 1 contracts are agreed. Keep each package to a small tool group and require a report of files changed, tests run, V21 prerequisites, and anything deferred.

| Work package | Suggested scope | Keep out of scope |
|---|---|---|
| PLC reads | Block/type/tag/table metadata and related tests (Milestone 2) | Shared result contracts and central registration ownership |
| Library reads | Library search/metadata, faceplates, library-type instances (Milestone 3) | Library synchronization or overwrites |
| Project references/texts | Cross-reference reads, project comparison/reads, text reads (Milestone 3) | Mutating project copy/sync |
| PLC safe writes | Tags, tables, constants, validated metadata updates (Milestone 4) | Deletes and broad replacement workflows |
| Library writes | Library lifecycle, synchronization, and publish (Milestone 5) | Any operation without conflict preview and safe save/discard behavior |
| PLC transfer | Deletion, imports/replacement, duplication, project/library transfer (Milestone 5) | Any operation without disposable-project verification |
| HMI structure/assets | Screens/groups/templates/assets, text lists, safe updates (Milestone 6) | Executing scripts or changing unsupported properties |
| HMI behavior | Events, scripts, tag/property dynamics (Milestone 7) | Runtime script execution or unverified API assumptions |
| Integration owner | Shared contracts, `ToolRegistry` integration, docs/version and final test pass | Concurrent feature implementation in domain-owned files |

### Agent handoff and merge rules

- [ ] Assign one owner per shared file. Domain agents should prefer separate `TiaManager` partial files and focused test files; the integration owner merges registrations and shared contracts.
- [ ] Give each agent exact tool names, milestone, expected result/error shapes, allowed files, prerequisites, and a stop condition.
- [ ] Require agents to preserve existing behavior, avoid unrelated refactors, and report unsupported APIs instead of approximating them.
- [ ] Review every mutation for target selection, reference impact, partial commit behavior, and test evidence before merging.
- [ ] Mark a tool complete only after its schema, implementation, focused tests, documentation, and (for Siemens-facing behavior) disposable-project verification are complete.

## 9. Per-tool completion checklist

A tool is ready only when all applicable items are checked:

- [ ] Its feasibility and overlap classification are recorded.
- [ ] Its name, schema, required fields, defaults, and target-resolution rules are documented.
- [ ] Success and failure results are structured and distinguish invalid input from Siemens/API failures.
- [ ] Mutation boundaries, side effects, partial-commit behavior, and destructive policies are explicit.
- [ ] Focused tests cover normal behavior and relevant invalid, missing, ambiguous, conflict, and dependency cases.
- [ ] Siemens-facing behavior is verified against a disposable project, or clearly marked unverified/deferred.
- [ ] User-facing tool reference and examples match the actual implementation.
