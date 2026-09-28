# OpenApiCodeGen Source Generator (Beta)

[日本語](README.ja.md) · [日本語の対応表](Documentation~/SourceGenerators/OpenApiMvpSupportMatrix.ja.md)

Source Generator generation is in beta. Review the supported features before use. See the
[Support Matrix](Documentation~/SourceGenerators/OpenApiMvpSupportMatrix.md). The Editor provider is named **Source Generator (Beta)**.

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
`com.unity.nuget.newtonsoft-json` `3.2.2`. Select **Source Generator (Beta)** in
`Window/OpenAPI Code Generator/Settings`, then use
`Window/OpenAPI Code Generator/Generator`.

The examples target the planned `0.5.0` tag. Until it is published, use the
same reviewed commit SHA in both Git URLs.

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
For a referenced full OpenAPI document, the analyzer checks its own `openapi`
version and `jsonSchemaDialect`. A different major.minor version or unsupported
dialect receives a positioned diagnostic. Patch-version differences and bare
external schemas remain supported.

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
See the [OpenAPI MVP support matrix](Documentation~/SourceGenerators/OpenApiMvpSupportMatrix.md)
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

Change `PackageName` in Settings and Generate again to move an owned definition
to a new namespace in the same output folder with the same API name. The Editor
checks its generated source, ownership header, identity and SpecId, retains its
SpecId and `.meta` GUID, and rejects edits made while generation is running.
Publication failures restore this transaction's files without overwriting
external definition edits, including during later journal recovery.

Changing only the output folder within the same asmdef, API name and namespace
moves the uniquely owned definition and its `.meta`, retaining SpecId and GUID.
An occupied destination, ambiguous ownership, or an unsupported assembly boundary
stops generation. Change namespace in the existing folder first, then move the
output folder in a second Generate. Interrupted moves restore both paths from
the durable journal. If either definition or metadata is edited concurrently,
recovery preserves the entire moved definition group instead of mixing its paths.

The window reports progress and supports Cancel before publication. Generation
and startup recovery use process-local and OS file locks. A durable publication
journal retains compilation intent until the compilation request succeeds;
startup recovery retries that request or restores interrupted publication.
Cancellation after loading and before publication preserves the previous
artifacts and does not request compilation.

When a URL query is stripped from persisted display data, the result includes a
warning that the complete URL must be entered again. The manifest stores only
redacted source display values, source-key hashes, format, raw hashes, retrieval
kind, status, redirect count, and bundle hash.

## Generated contract and diagnostics

For supported OpenAPI 3.0.* and 3.1.* input, the generator emits public
mutable sealed Newtonsoft.Json DTOs, string enums with an exact wire-value converter,
`List<T>` collections, an async `HttpClient` client, and
`<ApiName>Exception`. Generated source is a regenerated public contract, not a
hand-edited file. The full operation, parameter, schema, and response surface
is documented in the [support matrix](Documentation~/SourceGenerators/OpenApiMvpSupportMatrix.md).

For an optional schema-nullable DTO property, assignment marks the property as
present: an assigned `null` writes JSON `null`, while an untouched property is
omitted. Set the generated `<PropertyName>Specified` flag to `false` to omit it
again. OpenAPI 3.1 supports the default or explicitly declared OAS base schema
dialect; unsupported dialects and string enums containing `null` receive
source-located diagnostics.
Named object DTO schemas must explicitly set `additionalProperties: false`.
Schemas that omit it, allow extra properties, or use unsupported assertions
such as `pattern`, `minimum`, and `minItems` receive `OACG101` instead of
generating a weaker contract. Duplicate string-enum wire values also receive
`OACG101`.

An optional non-nullable DTO property may be absent from JSON, but an explicit
JSON `null` raises `JsonSerializationException`. An unset property is omitted
when serialized. Passing `null` for a required non-nullable reference-type
request body raises `ArgumentNullException` before sending; a nullable required
body can send JSON `null`. Request JSON bodies use a dedicated Json.NET serializer
that does not inherit the host's `JsonConvert.DefaultSettings` metadata options;
the generated date converter and explicit JSON `null` values are retained.
A `//host/path` server URL uses the scheme from
`HttpClient.BaseAddress`; a `/path` server URL starts at the origin root,
ignoring the base address path and query. Both forms require a base address.
An omitted or empty root `servers` list defaults to `/` and therefore also
requires a base address. Absolute server URLs must use HTTP or HTTPS; other
schemes receive a positioned `OACG101`. Unmatched path-template braces and header
parameter names outside the ASCII HTTP field-name token syntax receive a positioned `OACG100`.
An explicit `baseUrl=""` override remains relative.
With a relative server URL, an operation at `/` includes a base address query once.
Request bodies send a concrete `application/json` or `application/<subtype>+json`
media type. A request that declares only `application/*+json` receives `OACG101`;
when a concrete type is also declared, that type is selected.
Partial wildcard subtypes such as `application/vnd.*+json` receive `OACG101`
for request and successful response content.

Serialization checks required non-nullable reference properties in request
DTOs, including nested DTOs, before the HTTP request is sent. Required nullable
members may send JSON `null`, and required value members may send their default
values. A non-nullable reference item in a successful response array cannot be
JSON `null`, including items in nested arrays or DTO properties. Inline enum
declaration order does not affect response contract comparison.
Request arrays with non-nullable reference items are checked before sending,
including nested arrays and DTO properties. Nullable items may be JSON `null`.
String enums accept only declared wire strings with exact case and spacing;
undeclared strings and JSON numbers are rejected.
Request `float` and `double` values must be finite, including values nested in
arrays and DTOs. `NaN` and either infinity raise `JsonSerializationException`
before the HTTP request is sent. Non-finite `float` and `double` path, query,
and header parameters raise `ArgumentOutOfRangeException` before sending;
omitted optional parameters remain valid. An optional query parameter passed as
`null` is omitted, while an explicit empty string is sent as `name=`. For a successful response that declares content and supplies a
`Content-Type`, the client checks it against the media types declared for that
status before deserializing; malformed, multiple, and conflicting values raise
`JsonSerializationException`. A specific status declaration takes precedence
over `2XX`. A successful response without declared content skips this media
check; responses without a `Content-Type` retain the existing behavior.
Every schema-declared successful response requires a nonempty JSON body; a
nullable schema accepts the JSON token `null`. Response JSON is checked for
RFC 8259 syntax, duplicate and unknown object properties, and scalar token
types before deserialization. `float` and `double` response values must be
finite. Mathematical integers such as `1.0` and `1e0` are accepted within the
generated CLR type's range. Json.NET `$id` / `$ref` / `$values` reference
metadata, including object wrappers around arrays, remains a documented
compatibility extension. Known `date`, `date-time`, and `uuid` response strings
are checked for their wire spelling before CLR conversion. Lexically valid
leap seconds or times outside CLR ranges can still fail during conversion.
Successful response JSON has a default syntax depth limit of 64. An explicitly configured
positive `JsonConvert.DefaultSettings.MaxDepth` overrides it; `null` or an
unset value keeps 64.

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
| `OACG107` | Warning: a generated type name was disambiguated with a deterministic suffix |

The analyzer reads both Bundle v1 and v2. Bundle v1 remains available for
backward compatibility; the Editor always writes Bundle v2. The complete
schema and reader rules are in
[Normalized Spec Bundle v2](Documentation~/SourceGenerators/NormalizedSpecBundleV2.md).

## Dependencies and provider transitions

The package has one packaged analyzer DLL. Newtonsoft.Json `3.2.2`, Roslyn
`4.3.1`, and YamlDotNet are not embedded in that DLL or in the UPM archive;
see [Third Party Notices](Third%20Party%20Notices.md). YAML parsing in the
Editor uses the repository's BCL-only lexer/parser, while JSON parsing uses the
Unity Newtonsoft package.

When Source Generator is selected and available, the base package synchronizes
`OPENAPI_CODEGEN_SOURCE_GENERATOR` for the active `NamedBuildTarget`. Removing
this add-on removes the define from every known `NamedBuildTarget` before Unity
applies the package transition. Updating the add-on temporarily removes it from
the active target. Code that references generated types may then require source
or provider changes.
