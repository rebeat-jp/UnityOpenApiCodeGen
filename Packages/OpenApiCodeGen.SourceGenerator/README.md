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
    "jp.rhycol.openapicodegen": "https://github.com/rebeat-jp/UnityOpenApiCodeGen.git?path=/Packages/OpenApiCodeGen#0.5.1",
    "jp.rhycol.openapicodegen.source-generator": "https://github.com/rebeat-jp/UnityOpenApiCodeGen.git?path=/Packages/OpenApiCodeGen.SourceGenerator#0.5.1"
  }
}
```

The add-on requires Unity `6000.0` or later, base package `0.5.1`, and
`com.unity.nuget.newtonsoft-json` `3.2.2`. Select **Source Generator (Beta)** in
`Window/OpenAPI Code Generator/Settings`, then use
`Window/OpenAPI Code Generator/Generator`.

The examples use the `0.5.1` release tag. To try an unreleased commit, use the
same reviewed commit SHA in both Git URLs.

The target assembly definition must directly reference
`Unity.OpenApiCodeGen.SourceGenerator`. Generated output must be under
`Assets`. `Generate` is the explicit fetch/refresh operation; the provider
does not fetch in the background and never falls back to Docker.
An API name that matches a fixed generated client member is rejected with
`OACG005`; the reserved names are listed in the support matrix.

## Input and external references

Local `.json`, `.yaml`, and `.yml` paths are supported. HTTP(S) inputs may use
public, private, or loopback hosts, including cross-host redirects. The
following are rejected: whitespace or userinfo in a URL, non-HTTP(S) schemes,
HTTPS-to-HTTP direct references or redirects, explicit `file:` references, and
remote documents that reference local files. Local external files must remain
inside the Unity project after real-path and symbolic-link checks.

### Private GitHub repositories

To read a document from a private GitHub repository, enter its
`https://raw.githubusercontent.com/<owner>/<repo>/<ref>/<path>` URL and set
**GitHub Token** under User Settings in `Window/OpenAPI Code Generator/Settings`.
The token is sent as `Authorization: Bearer` only to requests whose origin is
exactly `https://raw.githubusercontent.com`. Every redirect hop is checked
again, so a redirect to another host never receives the token. Relative
external references in the same repository are fetched with the token.

The token is stored in plain text in the per-user settings file outside the
Unity project. It is never written to project settings, Bundle v2, the
manifest, or diagnostics. Use a fine-grained personal access token limited to
the repository with read-only Contents permission. `github.com` pages, the REST
contents API, and GitHub Enterprise Server hosts are not supported. When an
allowed GitHub URL returns 401, 403, or 404, the error says whether the token
is missing or was rejected.

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
Double-quoted Unicode escapes preserve valid non-BMP `\u` surrogate pairs;
isolated or mismatched surrogates receive positioned `YAML006`.

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
After restoration, Generate re-reads the owned definition before publishing,
so the first retry uses the recovered file while later user edits remain protected.
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
dialect; unsupported dialects receive
source-located diagnostics.
When a schema member's generated CLR name would be interpreted as another
member's `Specified` flag by Json.NET, the generator assigns a unique CLR name
and keeps its JSON property name.
Object schemas that omit `additionalProperties` or set it to `true` or a schema
generate an open DTO whose undeclared members are kept in an
`IDictionary<string, JToken>` `[JsonExtensionData]` member
(`AdditionalProperties`); set `additionalProperties: false` for a closed DTO
that rejects unknown response properties. Unsupported assertions such as
`pattern`, `minimum`, and `minItems` receive `OACG101` instead of generating a
weaker contract. Duplicate enum wire values also receive `OACG101`.
`allOf` is flattened into one DTO, and `oneOf` / `anyOf` become a wrapper class
(`FromX`, `TryGetX`, `AsX`) that is selected by the `discriminator` when there is
one, or by structural matching of the response JSON when there is not;
`[X, null]` is a nullable `X`. When several `oneOf` candidates match, the ones that declare every property of the response win; candidates that cannot be told apart fail. `not` is evaluated only for successful
responses. See the support matrix for the exact rules.

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
schemes receive a positioned `OACG101`. Malformed absolute-looking and relative
server URL references receive positioned `OACG100`; a `{...}` placeholder without a matching server `variables` entry
receives `OACG101`. A server URL fragment is omitted when combining its base
path with an operation path; an encoded `%23` stays in the path.
Unknown Response Object fields such as `contents` receive positioned `OACG101`.
Unmatched path-template braces and header parameter names outside the ASCII
HTTP field-name token syntax receive a positioned `OACG100`.
Raw `?` and `#` in a Paths key also receive positioned `OACG100`; encode path
data and declare query parameters separately.
`Accept`, `Content-Type` and `Authorization` header parameters are ignored as the
OpenAPI specification requires (any case) and reported as an `OACG108` warning.
Header values accept only HTAB and printable ASCII (U+0020–U+007E);
other characters raise `ArgumentException` before sending. A TRACE operation with `requestBody`
receives `OACG101`, including when the body is referenced.
Several `servers` and server `variables` are supported: the default client uses
`servers[0]` with each variable's `default` substituted. The generated client
exposes `Servers` (the URL templates) and
`BuildServerUrl(int index, IReadOnlyDictionary<string, string>? variables = null)`
(variables are validated against their `enum`); pass the result as the `baseUrl`
constructor argument. Servers declared on a Path Item or an operation take
precedence over the document servers for those operations; the `baseUrl`
argument only replaces the document-level servers, and the `OperationServerUrls`
property replaces an operation-level URL at run time. An operation without
`operationId` is named from its method and path (`GET /pets/{petId}` becomes
`GetPetsByPetId`). Path Item `$ref` (and OpenAPI 3.1 `components.pathItems`) is followed.
Path, query, header and cookie parameters support primitives, arrays of
primitives and objects of primitive properties in every OpenAPI `style` /
`explode` combination, `allowReserved` for query parameters, and `content` with
a JSON media type. Cookie parameters are sent in one `Cookie` header; set
`HttpClientHandler.UseCookies = false` (or use the `CookieContainer`) because a
manual `Cookie` header can otherwise be merged with or replaced by the container.
An explicit `baseUrl=""` override remains relative.
With a relative server URL, an operation at `/` includes a base address query once.
Request bodies send a concrete `application/json` or `application/<subtype>+json`
media type. A request that declares only `application/*+json` receives `OACG101`;
when a concrete type is also declared, that type is selected.
Partial wildcard subtypes such as `application/vnd.*+json` receive `OACG101`
for request and successful response content.
HEAD responses and successful 204, 205, or `2XX` responses cannot declare
body content, including through a response reference (`OACG101`). A typed
`2XX` contract is unsupported because that range includes 204 and 205.
Present response `headers` must be an object; nonempty maps remain unsupported.
Parameter Objects recognize standard OpenAPI 3.0/3.1 fields and `x-*` extensions.
Unknown fields such as `requird` receive positioned `OACG101`, including in
path-level parameters, components, and external reference targets.
Parameter, Request Body, and Response `$ref` siblings are checked: OpenAPI 3.1
allows string `summary` and `description`, while this generator accepts only
`$ref` in OpenAPI 3.0 to avoid silently ignored contract fields.
Quoted media parameters allow HTAB and U+0020–U+00FF except DEL; characters
above U+00FF receive `OACG100`. The selected request `Content-Type` must be
ASCII; non-ASCII quoted characters there also receive `OACG100`. Successful
response declarations retain the quoted-string range. Recognized schema formats (`int64`, `float`,
`decimal`, `date`, `date-time`, `uuid`, and `binary`) require exact lowercase
spelling; other casing uses the base schema type.

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
Path, query, and header enum values use an isolated Json.NET serializer, so
host `JsonConvert.DefaultSettings` converters or contract resolvers cannot rewrite them.
Request `float` and `double` values must be finite, including values nested in
arrays and DTOs. `NaN` and either infinity raise `JsonSerializationException`
before the HTTP request is sent. Non-finite `float` and `double` path, query,
and header parameters raise `ArgumentOutOfRangeException` before sending;
omitted optional parameters remain valid. An optional query parameter passed as
`null` is omitted, while an explicit empty string is sent as `name=`. For a successful response that declares content and supplies a
`Content-Type`, the client checks it against the media types declared for that
status before deserializing; malformed, multiple, and conflicting values raise
`JsonSerializationException`. A successful response without declared content skips this media
check; responses without a `Content-Type` retain the existing behavior.
Successful JSON bodies are decoded strictly with the declared charset, including
quoted names. Only a matching BOM is removed. Without a charset, UTF-8,
UTF-32 LE, and UTF-16 LE/BE BOMs are recognized; otherwise UTF-8 is used.
Invalid encoded bytes raise `JsonSerializationException`. Error bodies and
successful responses without a body type retain their existing reading behavior.
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
Successful response DTOs cannot declare `$id`, `$ref`, `$type`, or `$values`
as data properties; generation reports positioned `OACG100`. Request-only
DTOs may declare these names.
Successful response JSON has a default syntax depth limit of 64. An explicitly
configured positive `JsonConvert.DefaultSettings.MaxDepth` overrides it;
`null` or an unset value keeps 64. Response conversion uses an isolated
Json.NET serializer, so host converters and contract resolvers cannot change
values after schema validation.

Bundle and semantic failures use stable diagnostics including:

| ID | Meaning |
| --- | --- |
| `OACG001` | Invalid Bundle structure or reference edge |
| `OACG005` | Invalid client definition, including a reserved API name |
| `OACG100` | Invalid document, including response DTO metadata name conflicts |
| `OACG101` | Unsupported element or identity keyword |
| `OACG102` | Unresolved document or JSON Pointer target |
| `OACG103` | Cyclic reference |
| `OACG104` | Legacy v1 external-reference diagnostic |
| `OACG105` | Inconsistent response |
| `OACG106` | Invalid identifier |
| `OACG107` | Warning: a generated type name was disambiguated with a deterministic suffix |
| `OACG108` | Warning: an OpenAPI element was ignored; reported with its logical path and never changes the wire contract |

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
