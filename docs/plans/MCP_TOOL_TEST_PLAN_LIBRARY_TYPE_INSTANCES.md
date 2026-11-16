# MCP tool test plan: project-library type instances

## Implemented operation

### `GetLibraryTypeInstances(string projectPath, string plcName, string libraryTypeId, string? versionId = null, string? sourceLibrary = null)`

**Tool name:** `tia_get_library_type_instances`

**Bundled V21 API contract:** `LibraryTypeVersion.FindInstances(IInstanceSearchScope)` returns `LibraryTypeInstanceInfo` values. In the bundled API, `PlcSoftware` implements `IInstanceSearchScope`; the current WinCC Unified `HmiSoftware` does not. This operation therefore requires one exact PLC software scope and does not claim to search HMI targets or the whole project. `LibraryTypeInstanceInfo` exposes the instance object, parent object, and associated type version. Instance ID and name are read only if the selected object's runtime attribute metadata exposes readable `Guid` and `Name` attributes; the implementation does not invent an ID or infer a consistency state.

**Resolution semantics:** `project_path` selects exactly one already-open project by normalized full path. `plc_name` is a required ordinal, case-sensitive selector in that project. `library_type_id` and optional `version_id` must be canonical GUID strings. The type ID is resolved by traversing the selected project's `ProjectLibrary.TypeFolder`. If multiple type objects have the same ID, an exact version and/or source selector must narrow the candidates to one type; otherwise the operation fails as ambiguous. `source_library`, when supplied, is compared ordinally with `LibraryTypeVersion.OriginalLibrary`. Omitting both optional selectors queries all versions of the uniquely resolved type. The result always includes every version queried (including its state and source), even when that version has no matching instances. An exact known type/version with no matches returns a completed empty instance list.

| Case | Procedure | Expected result |
|---|---|---|
| Success, exact type/version/source | In a disposable project, create/import a PLC library type with at least one instance. Query using its exact type GUID, version GUID, and exact `OriginalLibrary` value. | Completed result reports the selected project, PLC, type, version, source, and matching instances. For each instance, verify reported readable GUID/name attributes and parent-name location against TIA Portal. |
| Success, all versions | Query a unique type GUID without `version_id` or `source_library` where at least two versions exist. | Instances from each version are returned with each associated version GUID, version number, state, and source; no version is selected by default. |
| Empty result | Query a known type/version in a PLC scope containing no instances of it. | `EnumerationCompleted == true`, `Instances` is empty, and no placeholder instance is returned. |
| Missing type / version / source | Use an unknown valid type GUID; then an unknown valid version GUID; then a valid source string not present in the selected type's versions. | The relevant selector fails with `FileNotFoundException`; no partial result is returned. |
| Ambiguous type selector | In a controlled fixture exposing duplicate project-library type GUIDs, query without selectors that identify one type; repeat with a selector that still matches multiple candidates. | `InvalidOperationException`; no arbitrary candidate is selected. |
| Ambiguous PLC selector | Create or locate two PLC software objects with the same exact name in the selected project. | `InvalidOperationException`; the operation does not choose the first PLC. |
| Exact selectors | Query with a PLC name whose case differs; with a path to another project; and with a `source_library` differing only in case. | Exact PLC/source mismatch is not silently treated as a match; unresolved project/PLC/source selectors fail without querying another context. |
| Invalid selectors | Try blank project or PLC names, malformed/non-canonical type and version GUIDs, and explicitly empty/whitespace optional selectors. | `ArgumentException`; `version_id` and `source_library` are not treated as omitted when explicitly blank. |
| API failure | In a controlled test seam, make project-library traversal, PLC service lookup, `FindInstances`, or instance attribute access throw an Engineering/COM/API exception. | The exception is propagated; no success or partial enumeration result is returned. |
| Unexpected version / duplicate ID | In a controlled API seam, return an instance associated with a different version or duplicate readable instance GUIDs. | `InvalidOperationException`; inconsistent results are not silently merged. |
| Read-only behavior | Snapshot the project/library and selected PLC's relevant object counts and type/version state, call the tool repeatedly, then compare snapshots. | No objects, names, versions, values, statuses, or project save state are changed by discovery. |
| Attribute metadata unavailable | Query an instance whose runtime metadata does not expose a readable `Guid` and/or `Name`. | The corresponding ID/name is null; location uses only available parent names. Result completeness text does not claim those values or instance consistency were supplied. |

## Deferred operation

### `instantiate_library_type`

No schema or handler is proposed. The bundled V21 `LibraryTypeVersion` surface verifies instance enumeration, version editing/release, and generic engineering-object APIs, but does not expose a type-independent, strongly typed `Instantiate` operation with a verified target/name/parent/property-overrides contract. The generic `IEngineeringObject.Create` surface is not enough to prove the required per-type creation interface, mandatory-property behavior, collision semantics, or rollback/partial-mutation behavior. Implementing this without type-specific API evidence would guess at creation semantics and could leave partial project mutations. Revisit only after the relevant V21 type-specific creation contract and required-value semantics can be verified.
