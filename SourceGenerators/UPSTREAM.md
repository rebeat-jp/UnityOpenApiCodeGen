# Upstream source snapshot

The production source-generator source and its original tests were imported as an unchanged snapshot for
GitHub issue #23.

| Field | Value |
|---|---|
| Repository | `https://github.com/rebeat-jp/UnityOpenApiCodeGen.SourceGenerator.git` |
| Branch | `feature/hirohiro/generating-api-client` |
| Commit | `fe599a8fdc05b1217f2deff9986abab13af71bf3` |
| Commit subject | `Add Roslyn code generator and Swagger C# client file generator` |
| Imported on | `2026-07-14` |

The snapshot consists of:

- `Rhycol.OpenApiCodeGen.SourceGenerator/` (imported as `UnityOpenAPICodeGenSourceGenerator/`)
- `Rhycol.OpenApiCodeGen.SourceGenerator.Tests/` (imported as `UnityOpenAPICodeGenSourceGenerator.Tests/`)
- `Rhycol.OpenApiCodeGen.SourceGenerator.slnx` (imported as `UnityOpenAPICodeGenSourceGenerator.slnx`)
- the upstream `.editorconfig`

Repository-specific workflows, editor settings, and agent instructions were not copied because they are not part of
the generator source or test contract.

The upstream repository did not contain a license file at the pinned commit. The copyright holder authorized this
snapshot to be incorporated under the destination repository's MIT License. Subsequent refactors remain covered by
that license, and this file preserves the source provenance and exact baseline revision.

The import commit must contain no formatting, namespace, design, or behavior changes. Those changes are deliberately
kept in later issue-specific commits.
