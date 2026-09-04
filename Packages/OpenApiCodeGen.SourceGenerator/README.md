# OpenApiCodeGen Source Generator

## Purpose and audience

This optional package generates a deterministic C# client through Unity's
Roslyn incremental source-generator pipeline. It is intended for Unity 6
projects that want generation from local OpenAPI JSON/YAML or an HTTP(S) URL.
The base package `jp.rhycol.openapicodegen` is required.

## Install and select the provider

Add both packages to the Unity project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "jp.rhycol.openapicodegen": "https://github.com/rebeat-jp/UnityOpenApiCodeGen.git?path=/Packages/OpenApiCodeGen#0.5.0",
    "jp.rhycol.openapicodegen.source-generator": "https://github.com/rebeat-jp/UnityOpenApiCodeGen.git?path=/Packages/OpenApiCodeGen.SourceGenerator#0.5.0"
  }
}
```

The add-on requires Unity `6000.0` or later, base package `0.5.0`, and
`com.unity.nuget.newtonsoft-json` `3.2.2`. Select **Source Generator** in
`Window/OpenAPI Code Generator/Settings`, then use
`Window/OpenAPI Code Generator/Generator`.

The target assembly definition must directly reference
`Unity.OpenApiCodeGen.SourceGenerator`. Generated output must be under
`Assets`. `Generate` is the explicit fetch/refresh operation; the provider
does not fetch in the background and never falls back to Docker.

## Input and external references

Local `.json`, `.yaml`, and `.yml` paths are supported. HTTP(S) inputs may use
public, private, or loopback hosts, including cross-host redirects. The
following are rejected: whitespace or userinfo in a URL, non-HTTP(S) schemes,
HTTPS-to-HTTP direct references or redirects, explicit `file:` references, and
remote documents that reference local files. Local external files must remain
inside the Unity project after real-path and symbolic-link checks.

For a remote document, the final URL extension and response `Content-Type`
determine the format. If both are recognized they must agree; one recognized
source is sufficient, and neither recognized source is an error. JSON accepts
`application/json` and `+json`. YAML accepts `application/yaml`,
`application/x-yaml`, `text/yaml`, `text/x-yaml`, and `text/x-yml`. The provider
does not sniff content or accept a format hint.

URL queries are part of the fetch identity for the current Generate operation.
They are not written in plain text to project settings, Bundle v2, the
manifest, or diagnostics. Re-enter the complete URL, including its query, on
the next Generate. URL fragments are not accepted on the root input; external
fragments must be empty or an RFC 6901 JSON Pointer.

The Editor fetches and parses the complete reference graph. The analyzer reads
only one canonical Bundle v2 AdditionalFile for the `specId`, so a graph is
published as one generation unit. Bare root Schema documents are supported for
external schema references. Bare non-Schema documents and `$id`, `$anchor`,
`$dynamicAnchor`, and `$dynamicRef` are not supported.

Hard limits are 30 seconds per request, 120 seconds per graph, 4 MiB per
document, 32 MiB per graph, 64 documents, five redirects, and reference depth
256. Automatic retry and ETag revalidation are not performed.

## YAML input subset

YAML is a bounded YAML 1.2-compatible subset, not a general-purpose YAML
implementation. It supports block and flow mappings/sequences, simple string
keys, JSON-compatible scalars, quoted/plain strings, literal/folded block
scalars, comments, anchors, and aliases. In a flow collection, plain values
containing `,`, `[`, `]`, `{`, or `}` must be quoted; URL scheme colons such as
`https://` are accepted.

Tags, directives, merge keys, multiple documents, complex keys, and block
scalars inside flow collections are rejected with source-located diagnostics.
See the [OpenAPI MVP support matrix](../../SourceGenerators/OpenApiMvpSupportMatrix.md)
for the complete YAML subset and diagnostic limits.

## Published files and incremental behavior

After a successful Generate, the Editor publishes the complete graph before
requesting script compilation:

```text
Library/OpenApiCodeGen/SourceGenerator/SpecCache/<specId>/normalized-v2.json
Library/OpenApiCodeGen/SourceGenerator/SpecCache/<specId>/manifest-v1.json
Assets/OpenApiCodeGen/Generated/SpecCache/
  <specId>.Rhycol.OpenApiCodeGen.SourceGenerator.additionalfile
<selected output folder>/<ApiName>.OpenApiDefinition.cs
```

The cache Bundle and compiler mirror are canonical UTF-8 JSON with LF and two
space indentation. A byte-identical Generate keeps the owned definition and
compiler input unchanged and does not request script compilation. A fetch,
parse, reference, or publication failure leaves the last successful cache and
mirror intact but returns failure; the failed input is not published.

When a URL query is stripped from persisted display data, the result includes a
warning that the complete URL must be entered again. The manifest stores only
redacted source display values, source-key hashes, format, raw hashes, retrieval
kind, status, redirect count, and bundle hash.

## Generated contract and diagnostics

For supported OpenAPI 3.0.* and 3.1.* input, the generator emits public
mutable sealed Newtonsoft.Json DTOs, string enums with `StringEnumConverter`,
`List<T>` collections, an async `HttpClient` client, and
`<ApiName>Exception`. Generated source is a regenerated public contract, not a
hand-edited file. The full operation, parameter, schema, and response surface
is documented in the [support matrix](../../SourceGenerators/OpenApiMvpSupportMatrix.md).

Bundle and semantic failures use stable diagnostics including:

| ID | Meaning |
| --- | --- |
| `OACG001` | Invalid Bundle structure or reference edge |
| `OACG101` | Unsupported element or identity keyword |
| `OACG102` | Unresolved document or JSON Pointer target |
| `OACG103` | Cyclic reference |
| `OACG104` | Legacy v1 external-reference diagnostic |
| `OACG105` | Inconsistent response |
| `OACG106` | Invalid identifier |

The analyzer reads both Bundle v1 and v2. Bundle v1 remains available for
backward compatibility; the Editor always writes Bundle v2. The complete
schema and reader rules are in
[Normalized Spec Bundle v2](../../SourceGenerators/NormalizedSpecBundleV2.md).

## Dependencies and provider transitions

The package has one packaged analyzer DLL. Newtonsoft.Json `3.2.2`, Roslyn
`4.3.1`, and YamlDotNet are not embedded in that DLL or in the UPM archive;
see [Third Party Notices](Third%20Party%20Notices.md). YAML parsing in the
Editor uses the repository's BCL-only lexer/parser, while JSON parsing uses the
Unity Newtonsoft package.

When Source Generator is selected and available, the base package synchronizes
`OPENAPI_CODEGEN_SOURCE_GENERATOR` for the active `NamedBuildTarget`. Removing
or updating this add-on removes the define before Unity applies the package
transition. Code that references generated types may then require source or
provider changes.
