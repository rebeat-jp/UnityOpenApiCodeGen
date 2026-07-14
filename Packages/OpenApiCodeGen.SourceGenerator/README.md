# OpenApiCodeGen Source Generator

This is the optional incremental source-generator add-on for
`jp.rhycol.openapicodegen`.

Phase 1 provides the UPM package boundary, the analyzer owner assembly, and an
Editor-only normalizer/cache service. Raw OpenAPI JSON is parsed in the Editor
with Json.NET and persisted as a deterministic Normalized Spec Bundle v1. The
analyzer consumes only the generated AdditionalFile and does not load Json.NET.

Phase 2 registers this add-on with the base package's public Generation Provider
registry. The provider is available only when the packaged analyzer DLL and its
`RoslynAnalyzer` metadata are valid. Select it from the base package Settings
window; it never falls back to Docker.

`NormalizedSpecCacheService` is intentionally internal and is not wired to the
Generate operation yet. During Phase 2, Generate returns an explicit message
that the local JSON pipeline is pending Phase 3. Normalization failures never
fall back to Docker.

When this provider is selected and available, the base package synchronizes
`OPENAPI_CODEGEN_SOURCE_GENERATOR` to the active `NamedBuildTarget`. Removing
or updating the add-on removes the define before Unity applies the package
transition. Selecting Docker removes it when that target is synchronized; code
that references generated types may then stop compiling.

The generated compiler mirror is stored at:

```text
Assets/OpenApiCodeGen/Generated/SpecCache/
  <specId>.Rhycol.OpenApiCodeGen.SourceGenerator.additionalfile
```

Assemblies that should receive generated code must explicitly reference the
`Unity.OpenApiCodeGen.SourceGenerator` owner assembly definition. Provider UI,
multiple specs per target assembly, YAML, URLs, and external `$ref` are not part
of Phase 1. Provider registration and define synchronization are included in
Phase 2, while partial-definition and cache-service wiring remain Phase 3 work.
