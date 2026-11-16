# Milestone 3 — project/library/reference/text discovery test plan

This plan covers only the read-only methods in `TiaManager.ProjectDiscovery.cs`. The tools are registered in `ToolRegistry` and have checked-in schemas. The implementation task did not perform live TIA Portal or disposable-project verification; subsequent selected checks are recorded in the [runbook](../MCP_TOOL_LIVE_TEST_RUNBOOK.md), and remaining cases still require live verification.

## Shared setup and contracts

- Use a disposable TIA Portal V21 project/library fixture and a TIA instance with the relevant project already open. Connect explicitly to that instance before invoking methods.
- Project-scoped methods require the exact `Project.Path.FullName` for one open project in that connected instance. They do not auto-connect, select the current project, open a project, or fall back to a name. A missing match throws `FileNotFoundException`; multiple exact-path matches throw `InvalidOperationException`.
- Page inputs require `offset >= 0` and `1 <= pageSize <= 200`. Invalid values throw `ArgumentOutOfRangeException`.
- Page results contain `Items`, `Offset`, `PageSize`, `TotalCount` (after filtering), `HasMore`, `EnumerationCompleted`, and `CompletenessNote`. Exceptions during enumeration return no partial page.
- `EnumerationCompleted` means the exposed V21 collection traversal returned without throwing; it does not assert an undocumented freshness/index guarantee.

## `ListGlobalLibraries(string? nameFilter = null, int offset = 0, int pageSize = 50)`

**Result shape:** `DiscoveryPage<GlobalLibraryMetadata>`; each item contains the API-provided `Name`, full `Path` (locator, not a fabricated ID), `LibraryType`, `IsOpen`, and `IsReadOnly`. No version or stable ID is reported because `GlobalLibraryInfo` exposes neither.

| Case | Procedure and expected result |
|---|---|
| Success / empty | With the test library present, list without a filter; verify records match `TiaPortal.GlobalLibraries.GetGlobalLibraryInfos()`. With no accessible global libraries, expect an empty page and `TotalCount == 0`. |
| Missing target | Not applicable: this operation enumerates rather than resolves one library. A filter with no match is the empty-result case. |
| Ambiguous target | Not applicable: filters do not resolve a single library. Place two same-named fixture libraries at different paths; both must appear, separately, ordered by name then path. |
| Invalid input | Test negative offset and page sizes 0 and 201; each must throw `ArgumentOutOfRangeException`. |
| Filter / page | Filter by a case-insensitive name substring; verify unrelated names are absent and `TotalCount` reflects filtered records. Use a page size of 1 across multiple records; verify stable ordering, offsets, and `HasMore`, including an offset beyond the final item. |
| API failure | Using a controlled API-failure seam or invalidated test connection, make `GetGlobalLibraryInfos()` fail; expect an exception and no returned partial result. |
| Live disposable-project steps | In TIA V21, add/open two disposable global libraries with distinct and then duplicate names, invoke unfiltered and filtered pages, and compare reported paths, flags, and ordering with the UI/API-visible libraries. Do not edit or save the libraries. |

## `SearchProjectLibraryTypes(string projectPath, string? query = null, int offset = 0, int pageSize = 50)`

**Result shape:** A page of `ProjectLibraryTypeMetadata` records containing API GUIDs for type and version IDs, name, namespace, runtime type kind, folder path, consistency status, and exposed version metadata (`Version`, state, default flag, source library). An empty query enumerates all visible project-library types; non-empty queries are literal case-insensitive substrings of name or namespace.

| Case | Procedure and expected result |
|---|---|
| Success / empty | In a disposable project with library types, verify all visible folder types and their API GUIDs/versions are returned. In an empty project library, expect an empty page with completed traversal. |
| Missing project | Pass an exact path not open in the connected TIA instance; expect `FileNotFoundException`. Blank/whitespace paths throw `ArgumentException`. |
| Ambiguous project | A duplicate exact path in the connected instance must throw `InvalidOperationException`; TIA may prevent constructing this live fixture, so validate the resolver branch with a controlled project-collection test double if available. |
| Invalid input | Test negative offset and page sizes 0 and 201; expect `ArgumentOutOfRangeException`. Query is literal text, not a query language; no syntax is rejected or interpreted. |
| Filter / page | Search a unique type-name fragment and a namespace fragment, including mixed case; verify only matches are returned. Page several types with size 1 and verify deterministic ordering, filtered `TotalCount`, `HasMore`, and out-of-range empty pages. |
| API failure | Cause a test collection/type-folder enumeration to throw; expect the API exception and no partial page. Verify a missing project-library API object also fails explicitly rather than silently returning an invented empty library. |
| Live disposable-project steps | Create types in root and nested library folders with multiple versions; invoke empty and literal searches, compare GUIDs/names/folder traversal/version states to TIA Portal, and page results. Do not change or release types. |

## `ListProjectFaceplates(string projectPath, string? nameFilter = null, int offset = 0, int pageSize = 50)`

**Result shape:** The same page/type records as library search, limited to runtime type `Siemens.Engineering.Hmi.Faceplate.FaceplateLibraryType`. Interface metadata is intentionally not returned: that V21 type has no interface property in the bundled API surface.

| Case | Procedure and expected result |
|---|---|
| Success / empty | Verify faceplate types in the project library are returned with GUID, name, versions, state, and source-library metadata. A project with no faceplate type returns an empty completed page. |
| Missing / ambiguous project | Use the shared exact-path cases above; expect `FileNotFoundException` or `InvalidOperationException`, respectively. |
| Invalid input | Test invalid page values as above. A blank name filter means no filtering. |
| Filter / page | Use a case-insensitive literal name substring and verify non-faceplate library types never appear. Page multiple faceplates and verify ordering and counts. |
| API failure | Cause type-folder enumeration to throw; expect an exception and no partial page. |
| Live disposable-project steps | In a disposable V21 HMI project/library, add at least two faceplate library types, one unrelated library type, and multiple versions; compare the returned subset and metadata to the project library. Verify the API/UI does not expose interface members through this type object before extending the result contract. |

## `ListProjectTextCategories(string projectPath, string? query = null, int offset = 0, int pageSize = 50)`

**Result shape:** `DiscoveryPage<ProjectTextCategoryMetadata>` with only API-provided category `Identifier` and `Name`. Category entry counts and per-language counts are not reported because the category collection exposes neither.

| Case | Procedure and expected result |
|---|---|
| Success / empty | Verify category identifiers/names from `Project.TextCategories`; an empty collection yields an empty completed page. |
| Missing / ambiguous project | Use the shared exact-path cases; expect `FileNotFoundException` or `InvalidOperationException`. |
| Invalid input | Test invalid page values as above; whitespace query is treated as no filter. |
| Filter / page | Search by case-insensitive category name and identifier fragments; verify result counts and page boundaries. |
| API failure | Make the category collection enumerator fail via a controlled test seam; expect an exception and no partial page. |
| Live disposable-project steps | In a disposable project with localized project text, inspect category identifiers/names, call the method with and without a category filter, and verify paging. Compare only category metadata; do not infer text counts or languages from categories. |

## `DownloadProjectTexts(string projectPath, string destinationFilePath, string primaryCultureName, string referenceCultureName)`

**Result shape:** `ProjectTextDownloadResult` contains resolved project and output paths, normalized culture names, output byte length, `ExportCompleted`, and a note that the V21 export API returns no entry count/completeness indicator. It deliberately does not claim an extension-specific format or count.

| Case | Procedure and expected result |
|---|---|
| Success | Export the selected project's text using the V21 `ProjectBase.ExportProjectTexts(FileInfo, CultureInfo, CultureInfo)` overload to a new file under an existing test output folder. Verify the returned file exists and the result path/length/cultures match it. |
| Missing / ambiguous project | Use the shared exact-path cases; expect `FileNotFoundException` or `InvalidOperationException`. |
| Invalid input | Blank destination/culture throws `ArgumentException`; invalid culture throws `CultureNotFoundException`; missing output directory throws `DirectoryNotFoundException`; directory-as-destination throws `ArgumentException`; existing output file throws `IOException` and is not overwritten. |
| Filter / page | Not applicable: the verified V21 overload exports using primary/reference cultures and accepts neither category filters nor paging. |
| API failure | Make the V21 export call throw; expect the exception and no success result. The implementation exports to a unique same-directory temporary file and removes it on failure; verify the requested destination remains absent. |
| Live disposable-project steps | Add localized text in at least two project categories/languages in a disposable project. Export to a new isolated file with explicit valid primary/reference cultures, verify it is readable in TIA Portal's supported text workflow, and compare selected entries manually. Re-run against an existing destination to verify it is preserved. No live result is claimed by this implementation task. |

## Evaluated but deferred

- **Global-library content search:** `GetGlobalLibraryInfos()` yields metadata only. Browsing type contents requires `GlobalLibraryComposition.Open(GlobalLibraryInfo)`; unlike the file-based overload, that API has no `OpenMode` argument. No read-only opening contract was verified, so this slice does not open a global library behind a discovery call.
- **Project library metadata / availability:** The attached `ProjectLibrary` has a type folder but no exposed attached/not-attached state, library identity, or version property. The type search reports only actual type metadata and does not invent an attached-library status.
- **Library-type instances:** `LibraryTypeVersion.FindInstances` requires `IInstanceSearchScope`. The bundled V21 APIs expose `HmiTarget` as a scope, but the WinCC Unified `HmiSoftware` used by this server does not implement that interface; no generic project/Unified target scope was verified.
- **Cross references, callers, and callees:** `CrossReferenceService.GetCrossReferences` accepts only the `CrossReferenceFilter` enum (`AllObjects`, `ObjectsWithReferences`, `ObjectsWithoutReferences`, `UnusedObjects`), not a source object. The returned collection is project-wide and has no API-provided completeness/freshness indicator. This slice cannot safely promise exact selected-object references or call graph traversal.
- **Reference-project reads/comparison:** The verified `ProjectComposition.Open` API opens a project into the TIA instance. No project-level reference comparison/read API was verified; library compare APIs compare library/software objects, not arbitrary selected objects in two projects. This slice does not open another project, change active project context, or invent an object comparison.
- **Text entry listing/category counts:** `Project.TextCategories` exposes identifiers/names, and `ProjectBase.ExportProjectTexts` can export to a caller-supplied file for two cultures. No direct read-entry collection or export result/count/format contract was verified in the bundled API. The export result therefore reports only the API-produced file and does not advertise an extension-specific format or completeness count.
