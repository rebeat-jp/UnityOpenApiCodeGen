# OpenApiCodeGen Source Generator

This is the optional incremental source-generator add-on for
`jp.rhycol.openapicodegen`.

The add-on owns the runtime declaration attribute, the Editor-side generation
provider, and the packaged Roslyn analyzer. Raw OpenAPI JSON is parsed in the
Editor with Json.NET. Raw OpenAPI YAML is decoded by the BCL-only YAML
lexer/parser. Both formats are lowered directly to the shared `SpecNode` tree
and persisted as a deterministic Normalized Spec Bundle v1. The canonical
bundle is JSON regardless of the raw input format. The analyzer consumes only
the generated AdditionalFile and does not load Json.NET or a YAML library. It
resolves the supported OpenAPI semantic model from that bundle and emits
deterministic C# source through Roslyn `AddSource`.

The add-on registers with the base package's public Generation Provider
registry. It is available only when the packaged analyzer DLL and its
`RoslynAnalyzer` metadata are valid. Select **Source Generator** in the base
package Settings window, then Generate from a local `.json`, `.yaml`, or
`.yml` document. Extension matching is case-insensitive. The provider rejects
URLs and never falls back to Docker.

### YAML input subset

YAML input is a bounded YAML 1.2-compatible subset, not a general-purpose YAML
implementation. It supports block and flow mappings/sequences, nested and
compact sequence composition, simple string keys in source order, comments,
BOM/LF/CRLF input, quoted/plain strings, JSON-compatible `null`/boolean/
RFC 8259 number scalars, literal (`|`) and folded (`>`) block scalars with
`+`/`-` chomping and explicit indentation indicators `1`–`9`, and anchors with
aliases. Alias expansion preserves the alias node's root source location.

In a flow collection, plain scalar values containing `,`, `[`, `]`, `{`, or
`}` delimiters must be quoted. A URL scheme colon such as `https://` is
accepted. YAML 1.1 implicit booleans, dates, and similar values remain
strings; they are not silently type-converted. Merge keys (`<<`), tags,
directives, multiple documents, complex/non-string keys, and block scalars
inside flow collections are rejected with a YAML diagnostic.

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
returns failure. The failed input is not published to the definition, cache,
compiler mirror, or script compilation.

For supported OpenAPI 3.0.* and 3.1.* local JSON or YAML documents, generation creates
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
verified separately. URLs, external `$ref`, OpenAPI 3.2, and Swagger 2 are
unsupported. Unsupported wire-affecting features are reported as diagnostics
rather than silently ignored. YAML subset rules, source locations, limits,
diagnostic IDs, and the full MVP input, schema, operation, response, and
generated-output constraints are documented in
the repository's
[OpenAPI MVP support matrix](https://github.com/rebeat-jp/UnityOpenApiCodeGen/blob/main/SourceGenerators/OpenApiMvpSupportMatrix.md).
