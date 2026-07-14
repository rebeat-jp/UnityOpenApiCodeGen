# OpenApiCodeGen Source Generator

This is the optional incremental source-generator add-on for
`jp.rhycol.openapicodegen`.

Phase 1 provides the UPM package boundary, the analyzer owner assembly, and an
Editor-only normalizer/cache service. Raw OpenAPI JSON is parsed in the Editor
with Json.NET and persisted as a deterministic Normalized Spec Bundle v1. The
analyzer consumes only the generated AdditionalFile and does not load Json.NET.

`NormalizedSpecCacheService` is intentionally internal and is not wired to the
existing Docker-backed Generate menu in Phase 1. A later Provider phase will
invoke this boundary explicitly; normalization failures never fall back to
Docker.

The generated compiler mirror is stored at:

```text
Assets/OpenApiCodeGen/Generated/SpecCache/
  <specId>.Rhycol.OpenApiCodeGen.SourceGenerator.additionalfile
```

Assemblies that should receive generated code must explicitly reference the
`Unity.OpenApiCodeGen.SourceGenerator` owner assembly definition. Provider UI,
multiple specs per target assembly, YAML, URLs, and external `$ref` are not part
of Phase 1.
