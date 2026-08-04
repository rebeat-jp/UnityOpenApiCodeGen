# OpenApiCodeGen Source Generator

This is the optional incremental source-generator add-on for
`jp.rhycol.openapicodegen`.

The add-on owns the runtime declaration attribute, the Editor-side generation
provider, and the packaged Roslyn analyzer. Raw OpenAPI JSON is parsed in the
Editor with Json.NET and persisted as a deterministic Normalized Spec Bundle
v1. The analyzer consumes only the generated AdditionalFile and does not load
Json.NET. In Phase 4, it resolves the supported OpenAPI semantic model from
that bundle and emits deterministic C# source through Roslyn `AddSource`.

The add-on registers with the base package's public Generation Provider
registry. It is available only when the packaged analyzer DLL and its
`RoslynAnalyzer` metadata are valid. Select **Source Generator** in the base
package Settings window, then Generate from a local `.json` document. The
provider does not accept URLs or YAML and never falls back to Docker.

The output folder must be under `Assets` and inside an asmdef that directly
references `Unity.OpenApiCodeGen.SourceGenerator`. Generate publishes all of
the following before requesting script compilation:

```text
Library/OpenApiCodeGen/SourceGenerator/SpecCache/
  <specId>/normalized-v1.json

Assets/OpenApiCodeGen/Generated/SpecCache/
  <specId>.Rhycol.OpenApiCodeGen.SourceGenerator.additionalfile

<selected output folder>/
  <ApiName>.OpenApiDefinition.cs
```

The definition is an owned partial class decorated with
`OpenApiClientDefinitionAttribute`. The analyzer joins definitions and bundles
by `specId`, so multiple clients can coexist in one target assembly without
selecting a bundle by file order. A byte-identical Generate reuses the owned
definition and cache without requesting script compilation. Invalid or
unsupported input leaves the last successful cache and mirror intact but still
returns failure.

For supported OpenAPI 3.0.* and 3.1.* local JSON documents, generation creates
public mutable sealed Newtonsoft.Json DTOs, string enums with
`StringEnumConverter`, `List<T>` collections, an async `HttpClient` client,
and a generated `<ApiName>Exception`. Client methods support the constrained
path/query/header/body and JSON request/response surface, use `JsonConvert`
and `CancellationToken`, and handle declared `2xx` responses. The serializer
and backend are fixed to Newtonsoft.Json and `System.Net.Http.HttpClient`;
the Docker provider remains separate and is never a fallback or switch.

Generated source is a regenerated public contract, not a hand-edited source.
Names are deterministic, including ordinal collision suffixes and stable
spec-ID hint names. DTO names from multiple specs must use distinct generated
namespaces because generated types are namespace-level.

When this provider is selected and available, the base package synchronizes
`OPENAPI_CODEGEN_SOURCE_GENERATOR` to the active `NamedBuildTarget`. Removing
or updating the add-on removes the define before Unity applies the package
transition. Selecting Docker removes it when that target is synchronized; code
that references generated types may then stop compiling.

Assemblies that should receive generated code must explicitly reference the
`Unity.OpenApiCodeGen.SourceGenerator` owner assembly definition. The add-on
minimum Unity version is 6000.0; the base package's Unity 2021.3 support is
verified separately. YAML, URLs, external `$ref`, OpenAPI 3.2, and Swagger 2
are unsupported. Unsupported wire-affecting features are reported as
diagnostics rather than silently ignored. The full MVP input, schema, operation,
response, diagnostic, and generated-output constraints are documented in
the repository's
[OpenAPI MVP support matrix](https://github.com/rebeat-jp/UnityOpenApiCodeGen/blob/main/SourceGenerators/OpenApiMvpSupportMatrix.md).
