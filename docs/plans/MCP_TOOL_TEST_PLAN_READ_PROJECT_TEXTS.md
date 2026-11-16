# Deferred read-project-texts test plan

## Status and API evidence

`read_project_texts` is deferred and is not registered or implemented. The bundled V21 public API does not provide a verified project-wide enumeration from which exact text-entry keys, categories, languages, and values can be returned. No live project was opened or modified.

The bundled `src/lib/Siemens.Engineering.Base.dll` was inspected through its exported public members:

- `Siemens.Engineering.Project` exposes `TextCategories`, and the inherited `ExportProjectTexts` methods. The overloads accept either a pair of `CultureInfo` values or language/category/object collections, but both write to a caller-supplied `FileInfo` and return `void`.
- `Siemens.Engineering.TextCategory` exposes only `Identifier`, `Name`, `Parent`, and the generic engineering-object attribute methods; it has no text-entry collection or enumeration method.
- `Siemens.Engineering.MultilingualText` exposes `Items`, whose `MultilingualTextItem` exposes `Language` and `Text`. This is a per-object multilingual-text composition; the API does not expose a project-wide collection that associates each item with the required entry key and category.
- The export overload's output format and exact entry/key mapping are not established by its signature or a result contract. Parsing a guessed export format would not establish exact keys/categories and is deliberately excluded.

No schema is checked into the registered `schemas/` directory because no
handler is implemented. No `TiaManager` partial source was added: adding a
method that fabricated entries, guessed export parsing, or always threw would
not be a valid implementation of this conditional slice.

## Required acceptance cases if a supported enumeration API becomes available

Use a disposable TIA Portal V21 project containing entries across at least two categories and two languages. Each test must compare the exact API-visible keys, category identifiers, language identifiers/names, and values—not UI-rendered or normalized approximations. A read must not save or otherwise modify the project.

| Case | Procedure | Expected result |
|---|---|---|
| API capability gate | Verify the selected V21 API directly exposes an enumerable project-wide entry collection, and that each entry supplies or deterministically resolves an exact key, category, language, and value. Verify enumeration/completeness semantics. | Implement only if all fields and enumeration scope are documented by the bundled API. Otherwise retain deferred status; do not infer entries from category names or parse undocumented export files. |
| Exact open project | Supply the exact full path of one open project. | Return only that project's entries; do not open, select, or save a project. The proposed handler should reuse `ResolveOpenProject`. |
| Missing or blank project | Supply a nonexistent/not-open path, blank path, and whitespace-only path. | Not-open path fails as missing; blank/whitespace fails validation. No fallback to a project name or active project. |
| Ambiguous project | Arrange two connected project objects resolving to the same exact path using a controlled resolver fixture if TIA disallows this live fixture. | Fail explicitly as ambiguous; return no entries. |
| Successful unfiltered enumeration | Read a fixture with entries in multiple categories/languages. | Every entry appears exactly once per key/category/language value, with exact source strings and category/language identity. Stable ordering must be defined before implementation. |
| Empty project / empty categories | Read a project with no entries and one with categories but no entries. | Return a completed empty result; do not synthesize rows for categories. |
| Category filter | Filter by exact category identifier and by category name if name filtering is supported. Include similarly named categories. | Only matching category entries appear; identifier/name matching semantics are explicit and no fuzzy category selection occurs for an exact filter. Unknown category fails or returns empty according to the final API contract, consistently. |
| Language filter | Filter for a language present in the project, a valid but absent language, malformed language, and case variants. | Preserve source language identity; invalid language fails clearly; absent valid language returns empty. Case sensitivity/normalization must match documented V21 language identity and must not merge distinct languages. |
| Key and name filters | Use exact keys, repeated substrings, mixed case, punctuation, and strings that could be interpreted as patterns. | Filters are literal, not regex/glob/query syntax; returned keys and names remain unmodified. Duplicate keys across categories/languages remain distinct. |
| Pagination boundaries | Test defaults, first/middle/last pages, an offset equal to total count, offset beyond total count, page size 1/200, negative offset, and page sizes 0/201. | Pages do not skip or duplicate entries; counts apply after filtering; empty terminal pages are valid; invalid bounds fail before enumeration. |
| Exact value preservation | Include empty string, whitespace-only value, leading/trailing spaces, embedded newlines, quotes, backslashes, non-ASCII text, and supplementary Unicode characters. | Values are returned unchanged; do not trim, normalize, translate, or substitute missing values. If the V21 API cannot distinguish missing from empty, report that limitation rather than inventing a value. |
| Enumeration/API failure | Cause the collection or entry accessor to fail through a controlled fixture/test seam. | Return an explicit failure, never a successful partial result or `EnumerationCompleted=true`. |
| Read-only/no-side-effects | Record project modified state and source files before the call; perform filtered and paged reads. | No writes, imports, saves, file exports, or project modified-state changes. |
| Strict input schema | Validate the proposed schema with required `project_path`, each optional filter/page parameter, unknown properties, empty required path, and page bounds. | Reject undeclared properties and invalid bounds; the schema remains unregistered until the feature has an implementation. |

## Proposed parent handler signature

If a suitable V21 enumeration contract is verified, use:

```csharp
ReadProjectTexts(
    string projectPath,
    string? category = null,
    string? language = null,
    string? keyFilter = null,
    string? nameFilter = null,
    int offset = 0,
    int pageSize = 50)
```

The result should contain exact entry key, category identifier (and category name only if the API provides it), language identity, and unmodified value, plus page metadata. This signature is a proposal only; it is not implemented or callable in the current server.
