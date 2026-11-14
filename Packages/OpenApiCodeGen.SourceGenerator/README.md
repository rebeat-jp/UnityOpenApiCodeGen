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

The add-on requires Unity `6000.0` or later, base package `0.5.1`, and the
libraries of the selected HTTP / JSON combination (see
[Choose the HTTP and JSON libraries](#choose-the-http-and-json-libraries)).
Since 0.6.0 the add-on no longer installs `com.unity.nuget.newtonsoft-json`.
Select **Source Generator (Beta)** in
`Window/OpenAPI Code Generator/Settings`, then use
`Window/OpenAPI Code Generator/Generator`.

The examples use the `0.5.1` release tag. To try an unreleased commit, use the
same reviewed commit SHA in both Git URLs.

The target assembly definition must directly reference
`Unity.OpenApiCodeGen.SourceGenerator`. Generated output must be under
`Assets`. `Generate` is the explicit fetch/refresh operation; the provider
does not fetch in the background and never falls back to Docker.
An API name that matches a fixed generated client member is rejected with
`OACG005`; the reserved names are listed in the support matrix. A type named
`<ApiName>_Preserve` that you declare in the same namespace as the client
conflicts with the namespace that the generated `PreserveAttribute` lives in
(see the support matrix).

## Choose the HTTP and JSON libraries

A client is generated for one HTTP implementation and one JSON serializer. The
default is **HttpClient + Newtonsoft.Json**. All four combinations are
implemented; the project must provide the libraries of the selected one.

| Choice | What the project needs | Minimum |
| --- | --- | --- |
| HTTP: `HttpClient` (default) | `System.Net.Http` (normally available without installing anything) | - |
| HTTP: `UnityWebRequest` | The Unity Web Request module (`com.unity.modules.unitywebrequest`) and `System.Net.Http`; the adapter still builds `HttpRequestMessage` / `HttpContent` and only the last step uses `UnityWebRequest` | - |
| JSON: `Newtonsoft.Json` (default) | `com.unity.nuget.newtonsoft-json` `3.2.2`, or another Newtonsoft.Json reference | 13.0 |
| JSON: `System.Text.Json` | UnityNuGet `org.nuget.system.text.json` (it brings `System.Text.Encodings.Web`, which the generated code needs at run time), or the DLLs through NuGetForUnity or by hand | 8.0 |

**Select** the combination in `Window/OpenAPI Code Generator/Settings`, section
**Source Generator Libraries**. Each choice shows its state (installed, missing,
not referenced by the output assembly, too old) and a choice that is not ready
cannot be selected. Newtonsoft.Json and the Unity Web Request module have an
install button (`Client.Add`). System.Text.Json is shown as manual steps only:
add the UnityNuGet scoped registry
(`https://unitynuget-registry.openupm.com`, scope `org.nuget`) and the
dependency `org.nuget.system.text.json` 8.0 or later. The Editor never edits
`Packages/manifest.json` for it. A library is detected from what the output
assembly's player references, so NuGetForUnity or manually placed DLLs are found
too, while an Editor-only DLL is not counted. The Generator window shows the
combination and its state, and disables **Generate** with the reason and a link
to Settings while a library is missing. The provider checks again when
Generate runs and returns the same guidance as the failure message. The state
is separate from the provider's availability: a missing library never removes
`OPENAPI_CODEGEN_SOURCE_GENERATOR`, so existing definitions are not switched
off.

The choice is stored in the definition file as the named properties
`HttpTransport` and `JsonSerializer` of `OpenApiClientDefinitionAttribute`
(enums `OpenApiHttpTransport { HttpClient, UnityWebRequest }` and
`OpenApiJsonSerializer { NewtonsoftJson, SystemTextJson }`; the constructor is
unchanged):

```csharp
[global::Rhycol.OpenApiCodeGen.SourceGenerator.OpenApiClientDefinitionAttribute(
    "petstore", "PetApi", "MyGame.Api",
    global::Rhycol.OpenApiCodeGen.SourceGenerator.OpenApiDocumentFormat.Json,
    HttpTransport = global::Rhycol.OpenApiCodeGen.SourceGenerator.OpenApiHttpTransport.UnityWebRequest,
    JsonSerializer = global::Rhycol.OpenApiCodeGen.SourceGenerator.OpenApiJsonSerializer.SystemTextJson)]
public partial class PetApi { }
```

The Editor writes a property only when it differs from the default, so a
definition created before 0.6.0 stays byte-identical and keeps using HttpClient
+ Newtonsoft.Json. Changing the selection does not change the `specId` or the
client identity.

### WebGL

Select `UnityWebRequest` for WebGL. `System.Net.Http.HttpClient` does not work
in a WebGL player: the Settings window warns when the active build target is
WebGL and HttpClient is selected, and the analyzer reports warning `OACG014`
when `UNITY_WEBGL` is defined. The code still compiles; the player simply never
completes a request (observed in the player verification). Relative server URLs
need an absolute base address with UnityWebRequest; pass
`new Uri(Application.absoluteURL)` to resolve against the hosting page.

### Diagnostics for the selection

| ID | Severity | Reported when |
| --- | --- | --- |
| `OACG010` | Error | The definition names an unknown `HttpTransport` / `JsonSerializer` value, or a combination without an implementation |
| `OACG011` | Error | The selected JSON library is not referenced (`Newtonsoft.Json.JsonConvert` or `System.Text.Json.JsonSerializer` is not found) |
| `OACG012` | Error | The selected HTTP implementation is not referenced (`System.Net.Http.HttpClient`; `UnityEngine.Networking.UnityWebRequest` and `System.Net.Http.HttpClient` for `UnityWebRequest`) |
| `OACG013` | Error | The referenced library is older than the minimum: Newtonsoft.Json 13.0, System.Text.Json 8.0 |
| `OACG014` | Warning | `HttpClient` is selected while `UNITY_WEBGL` is defined |

While an error applies, nothing is generated for that client. If several
references define the same library, the highest assembly version counts.

### System.Text.Json and Newtonsoft.Json

The generated public API has the same shape with both serializers; these are
the differences:

| Aspect | Newtonsoft.Json | System.Text.Json |
| --- | --- | --- |
| `{}` / any value (`Any`) | `JToken` | `JsonElement` |
| Free-form `type: object` | `JObject` | `System.Text.Json.Nodes.JsonObject` |
| Open DTO members | `IDictionary<string, JToken>` with `[JsonExtensionData]` | `IDictionary<string, JsonElement>` with `[JsonExtensionData]` |
| Raw value of a union without discriminator | `FromJToken` / `ToJToken` | `FromJsonElement` / `ToJsonElement` (a value made with `FromJsonElement` is read with default options, so the client's converters do not apply) |
| Response that violates the contract | `Newtonsoft.Json.JsonSerializationException` | `System.Text.Json.JsonException` |
| `<Property>Specified` and `readOnly` rules, date and integer converters | Attributes and `ShouldSerialize<Name>()` on the DTO | In the client's `JsonSerializerOptions` (`DefaultJsonTypeInfoResolver.Modifiers`); serializing a DTO with your own options does not apply them |
| Host settings | Isolated serializers; a positive `JsonConvert.DefaultSettings.MaxDepth` overrides the depth limit 64 | No global settings; the depth limit is fixed at 64; `UnsafeRelaxedJsonEscaping` is meant for HTTP bodies, do not embed its output in HTML |
| `$id` / `$ref` / `$values` reference metadata in responses | Accepted as a compatibility extension | Not supported |
| Integral numbers such as `2.0`, `1e0` for integer properties | Accepted within the CLR range | Accepted within the CLR range (converters of the client); in the structural match of a union without discriminator a number with a fraction or exponent is not an integer |
| Declared error body (`Error`, `TryGetError<T>`) | Deserialized as is; a failure is kept in `ErrorDeserializationException` | Checked like a success body first (repeated names, undeclared members of a closed DTO, integer spelling, enum values, required members); a failure leaves `Error` null and is kept in `ErrorDeserializationException` |

Length constraints count Unicode code points (a surrogate pair is one
character) with both serializers, and a repeated member name in a response is
rejected by both. With System.Text.Json a response that contains an unpaired
surrogate escape (such as `\ud83d`) is rejected with `JsonException`, because
the text is not valid UTF-16.

### UnityWebRequest limitations

The client built for `UnityWebRequest` takes `new PetApi(Uri? baseAddress)` or
`new PetApi(Uri? baseAddress, string baseUrl)` instead of an `HttpClient`.

- The `UnityWebRequest` is created, sent and disposed on the
  `SynchronizationContext` of the caller, which has to be the Unity main thread
  (nothing before the send uses `ConfigureAwait(false)`). Call the client
  methods from the main thread.
- Cancelling the token calls `Abort()` on that context and throws
  `OperationCanceledException`. `RequestTimeout` (`TimeSpan?`, `null` waits
  without a limit) is rounded up to whole seconds; a timeout throws
  `TaskCanceledException` whose `InnerException` is a `TimeoutException`, as
  `HttpClient.Timeout` does. A network failure throws `HttpRequestException`;
  an HTTP error status is returned, not thrown.
- A relative server URL (`/v1`, `//host/v1`, `v1`) needs an absolute
  `baseAddress` (the counterpart of `HttpClient.BaseAddress`); without it the
  call throws `InvalidOperationException`. An absolute server URL needs none.
- `ConfigureUnityWebRequest` receives every request right before it is sent
  (certificate handler, redirect limit, ...). `HttpClientHandler` settings such
  as `UseCookies` or client certificates do not apply.
- UnityWebRequest merges the values of a repeated response header into one
  comma-separated value, and the response reason phrase is not available.
- The generated code sets every request header (cookie parameters and cookie API
  keys go into one `Cookie` header) with `UnityWebRequest.SetRequestHeader`. When
  Unity refuses a header, the call throws `InvalidOperationException` naming the
  header (the original exception is the `InnerException`) instead of dropping it.
  Which headers are refused is up to Unity and the platform: for example
  `Host`, `Connection`, `Expect` and `Transfer-Encoding`, and headers that the
  browser forbids on WebGL may not be sendable. The WebGL header restrictions
  (including `Cookie` and `User-Agent`) are not verified.
- Request and response bodies are held in memory (`byte[]`), including `Stream`
  file parts.
- The members `_baseAddress`, `RequestTimeout`, `ConfigureUnityWebRequest`,
  `SendUnityWebRequestAsync`, `SetUnityRequestHeader` and
  `AddUnityResponseHeader` are reserved API names for this transport only.

### Player verification and IL2CPP stripping

Issue #57 is covered for Unity `6000.3.2f1`: Standalone Win64 IL2CPP (Managed
Stripping Minimal and High, both transports, both serializers) and WebGL
IL2CPP (UnityWebRequest, both serializers, Minimal and High) pass all 21 probe
checks; WebGL with HttpClient is not supported (only the Newtonsoft.Json cell was measured; the cause is the transport, so System.Text.Json is judged the same without a separate measurement). The results and what is not
verified are in the
[support matrix](Documentation~/SourceGenerators/OpenApiMvpSupportMatrix.md).
So that High stripping keeps the reflection-only members, generated DTOs, enum
wire converters, unions and their properties carry an internal
`PreserveAttribute`, declared in the namespace
`<generated namespace>.<ApiName>_Preserve`. A spec without DTOs, enums and
unions generates no such attribute.

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
See the [OpenAPI support matrix](Documentation~/SourceGenerators/OpenApiMvpSupportMatrix.md)
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

## Supported versions and main capabilities

| Input | Support |
| --- | --- |
| Swagger 2.0 (`swagger: "2.0"`) | Normalized to OpenAPI 3.0, then generated by the same pipeline. Diagnostics point at the Swagger 2.0 source. |
| OpenAPI 3.0.* | Supported. |
| OpenAPI 3.1.* | Supported, including the default OAS 3.1 schema dialect and the 3.1 JSON Schema keywords. |
| OpenAPI 3.2.* | Supported without streaming: `query`, `additionalOperations`, `in: querystring`, `components.mediaTypes`, tag `parent` / `kind`. |

Main capabilities: open objects, maps and free-form objects; `allOf`, `oneOf` / `anyOf` and
`discriminator`; integer and nullable enums; `readOnly` / `writeOnly`; every parameter location
and `style`; JSON, form-urlencoded, multipart, text and binary request bodies; typed and status-keyed
responses, typed errors and response headers; several servers with variables; security schemes;
run-time constraint validation; and OACG108 warnings for elements that are deliberately ignored.
Out-of-scope elements (for example `$dynamicRef`, `if` / `then` / `else`, streaming, token
acquisition for oauth2) keep a positioned diagnostic. The
[support matrix](Documentation~/SourceGenerators/OpenApiMvpSupportMatrix.md) lists the final status of
every element and every diagnostic.

## Usage notes

The snippets below use a hypothetical API named `PetApi`. Method names follow `operationId` (or the name
derived from the method and path); the members shown exist in every generated client that uses the feature.

### Authentication

Each security scheme becomes public properties named after the `components.securitySchemes` key
(`bearerAuth` becomes `BearerAuth...`). A request uses the first requirement whose schemes all have a
credential and is otherwise sent without authentication.

```csharp
var client = new PetApi(httpClient);
client.HeaderKeyApiKey = "secret";               // apiKey: string? <Scheme>ApiKey
client.BasicAuthUsername = "user";               // http basic: <Scheme>Username / <Scheme>Password
client.BasicAuthPassword = "pass";
client.BearerAuthAccessToken = "token";          // http bearer / oauth2 / openIdConnect
client.OauthAccessTokenProvider = async ct =>    // Func<CancellationToken, Task<string?>>?
    await tokenStore.GetAccessTokenAsync(ct);    // preferred over the property; called for every request
```

oauth2 and openIdConnect only send the token you supply as a Bearer credential; they never fetch or refresh
one. Other `http` schemes use `<Scheme>Credentials`. Set the properties before sending (they are not thread safe).

### Server selection

```csharp
IReadOnlyList<string> templates = PetApi.Servers;               // the server URL templates
string url = PetApi.BuildServerUrl(0, new Dictionary<string, string> { ["region"] = "eu" });
var client = new PetApi(httpClient, url);                       // baseUrl replaces the document-level servers

// Operations that declare their own servers (Path Item or operation level):
client.OperationServerUrls[PetApi.OperationNames.search] = "https://search.example.com";
```

`BuildServerUrl` validates the variables against their `enum` (`ArgumentException`, or
`ArgumentOutOfRangeException` for a bad index). The default constructor uses `servers[0]` with the `default`
values. `OperationServerUrls` and `OperationNames` exist only when some operation declares servers; set
`OperationServerUrls` before the first request. `GetOperationServers` and `BuildOperationServerUrl` list and
expand the candidates of one operation.

### Runtime constraint validation

`minLength`, `maxLength`, `pattern`, `minimum` / `maximum` and their exclusive forms, `multipleOf`,
`minItems`, `maxItems`, `uniqueItems`, `minProperties`, `maxProperties` and the 3.1 object / array keywords
are enforced at run time:

```csharp
try { await client.createPet(new Pet { Name = "x" }); }          // schema: minLength 3
catch (ArgumentException e) { /* request rejected before sending; e.ParamName names the argument */ }

try { var pet = await client.getPet(1); }
catch (Newtonsoft.Json.JsonSerializationException e) { /* the response violated a constraint (or the schema); System.Text.Json: System.Text.Json.JsonException */ }
```

A request violation throws `ArgumentException` and the `HttpClient` is never called; a response violation throws
`JsonSerializationException` (`System.Text.Json.JsonException` with System.Text.Json). Messages contain the path (`$.items[2].name`) and the keyword, not the value.
This is a behavior change for specifications that previously had these keywords rejected.

### File parameters and multipart

A `format: binary` property of a multipart object schema is a generated `<ApiName>FileParameter`:

```csharp
var file = new PetApiFileParameter(File.OpenRead(path), "avatar.png", "image/png"); // or (byte[] data, ...)
await client.uploadPetAsMultipart(new PetForm { Name = "Rex", Avatar = file });
```

A `Stream` is disposed together with the request, so pass a new stream per call. A binary request body is a
`byte[]`; a wildcard media type adds an optional `<body>ContentType` argument. When an operation declares
several request media types, the highest priority one keeps the operation name and the others get
`<name>AsFormUrlEncoded`, `<name>AsMultipart`, `<name>AsText` or `<name>AsBinary`.

### Typed responses and errors

```csharp
var result = await client.getStatus();                  // status-keyed result when the 2xx contracts differ
if (result.IsStatus200) Use(result.Status200!);         // StatusCode, Is<Status>, typed Status<Code>

var info = await client.getWithHeadersWithHttpInfo();  // only for operations that declare response headers
Use(info.Data, info.StatusCode, info.Headers);          // plus one typed property per declared header

try { await client.getPet(1); }
catch (PetApiException e)                               // <ApiName>Exception
{
    var status = e.StatusCode; var body = e.ResponseBody;      // always available
    if (e.TryGetError<Problem>(out var problem)) { }           // declared JSON error body
    var parseFailure = e.ErrorDeserializationException;        // set when the body could not be parsed
}
```

A text success body is a `string` and a binary one is a `byte[]`. The error `Error` property is `null` when
the status declares no JSON body or deserialization failed; `ResponseBody` always keeps the text.

### OACG108 warnings

`OACG108` is a warning, not an error: the analyzer ignored an element on purpose and the generated wire
contract is unaffected. The message carries the logical path of the ignored element. Typical causes:
`Accept` / `Content-Type` / `Authorization` header parameters (the specification says to ignore them),
`webhooks`, operation `callbacks` and response `links` (no client member is generated; schemas declared in
`components.schemas` still get a DTO), an `application/xml` entry next to a generable media type, and
a `pattern` that needs .NET syntax. See the `OACG108` row of the support matrix for the complete list.

## Generated contract and diagnostics

For supported OpenAPI 3.0.*, 3.1.* and 3.2.* input (and Swagger 2.0 input, see below), the generator emits public
mutable sealed DTOs (Newtonsoft.Json by default, or System.Text.Json), string enums with an exact wire-value converter,
`List<T>` collections, an async client (`HttpClient` by default, or `UnityWebRequest`), and
`<ApiName>Exception`. The text below describes the default Newtonsoft.Json + HttpClient output; see
[Choose the HTTP and JSON libraries](#choose-the-http-and-json-libraries) for what differs with the other choices.
Generated source is a regenerated public contract, not a
hand-edited file. The full operation, parameter, schema, and response surface
is documented in the [support matrix](Documentation~/SourceGenerators/OpenApiMvpSupportMatrix.md).

For an optional schema-nullable DTO property, assignment marks the property as
present: an assigned `null` writes JSON `null`, while an untouched property is
omitted. Set the generated `<PropertyName>Specified` flag to `false` to omit it
again. OpenAPI 3.1 and 3.2 support the default or explicitly declared OAS base schema
dialect; unsupported dialects receive
source-located diagnostics. OpenAPI 3.2 additionally accepts the `query` method,
`additionalOperations`, `in: querystring` parameters, `components.mediaTypes` and the
discriminator `defaultMapping`; streaming (`text/event-stream`, incremental `itemSchema`)
is not supported (see the support matrix).

Swagger 2.0 documents (`swagger: "2.0"`) are accepted too: they are normalized into an equivalent
OpenAPI 3.0 document (`host`/`basePath`/`schemes` become `servers`, `definitions` become
`components.schemas`, `body` and `formData` parameters become a `requestBody`, `collectionFormat`
becomes `style`/`explode`, `securityDefinitions` become `securitySchemes`, `x-nullable` becomes
`nullable`) before the same generation pipeline runs, and diagnostics point at the original
Swagger 2.0 location. Swagger 2.0 constructs with no OpenAPI 3.0 equivalent (`collectionFormat: tsv`,
mixed Swagger 2.0 / OpenAPI 3.x references) receive `OACG101` (see the support matrix).

When a schema member's generated CLR name would be interpreted as another
member's `Specified` flag by Json.NET, the generator assigns a unique CLR name
and keeps its JSON property name.
Object schemas that omit `additionalProperties` or set it to `true` or a schema
generate an open DTO whose undeclared members are kept in an
`IDictionary<string, JToken>` `[JsonExtensionData]` member
(`AdditionalProperties`); set `additionalProperties: false` for a closed DTO
that rejects unknown response properties. Validation keywords (`minLength`,
`maxLength`, `pattern`, `minimum`, `maximum`, `exclusiveMinimum`,
`exclusiveMaximum`, `multipleOf`, `minItems`, `maxItems`, `uniqueItems`,
`minProperties`, `maxProperties`, and in OpenAPI 3.1 `patternProperties`,
`propertyNames`, `contains`, `minContains`, `maxContains`, `prefixItems`,
`dependentRequired`) are checked at run time and never change the generated
types: a request that violates one is rejected before it is sent
(`ArgumentException`), and a response or response header that violates one raises
`JsonSerializationException`. Lengths count Unicode code points and `pattern` is
an unanchored .NET `Regex` (ECMAScript mode unless the pattern needs .NET syntax, reported as `OACG108`; see the
support matrix). `if` / `then` / `else`, `dependentSchemas` and
`unevaluated*` still receive `OACG101`. Duplicate enum wire values also receive
`OACG101`.
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
Security schemes (`components.securitySchemes`: `apiKey` in header / query / cookie, `http` bearer / basic /
any other scheme, `oauth2`, `openIdConnect`, `mutualTLS`) and security requirements (document and operation
level; `security: []` disables authentication) are supported. The generated client gets one public property
per scheme: `string? <Scheme>ApiKey`, `string? <Scheme>AccessToken` plus an async
`Func<CancellationToken, Task<string?>>? <Scheme>AccessTokenProvider` (bearer, oauth2, openIdConnect), `string? <Scheme>Username` /
`<Scheme>Password` (basic) or `string? <Scheme>Credentials` (other http schemes). A request uses the first
requirement (the OR list in document order) whose schemes (AND) all have a credential; when none does, it is sent
without authentication. oauth2 and openIdConnect only send a token you supply as a Bearer credential: no token is
fetched or refreshed. `mutualTLS` needs no property; configure the client certificate on the `HttpClientHandler`.
An API key in a cookie shares the single `Cookie` header with cookie parameters. The credential properties are not
thread safe; set them before sending. A malformed scheme or a requirement that names an undefined scheme receives a
positioned `OACG100`.
An explicit `baseUrl=""` override remains relative.
With a relative server URL, an operation at `/` includes a base address query once.
Request bodies support `application/json` (a concrete `application/<subtype>+json`
also works), `application/x-www-form-urlencoded`, `multipart/form-data`, text
(`text/*`, `application/xml` with a string schema) and binary (`application/octet-stream`,
`image/*`, `*/*`, or a schema with `format: binary`). When an operation declares several
media types, the highest priority one (JSON, form-urlencoded, multipart, text, binary)
keeps the operation name and each other one gets its own method (`CreatePetAsMultipart`
style: `<name>AsFormUrlEncoded`, `<name>AsMultipart`, `<name>AsText`, `<name>AsBinary`).
A multipart part that is a file (`format: binary`) is a generated `<ApiName>FileParameter`
(`byte[]` or `Stream`, with `FileName` and `ContentType`); a `Stream` is handed to the request
and disposed with it, so pass a new stream per call. The Encoding Object (`contentType`, static
`headers`, `style` / `explode` / `allowReserved`) is honored. Form-urlencoded values are
percent-encoded (a space is `%20`, not `+`). A request that declares only `application/*+json`
receives `OACG101`; object schemas in `application/xml` receive `OACG101`.
Partial wildcard subtypes such as `application/vnd.*+json` receive `OACG101`.
Successful responses may be JSON, text (`string`) or binary (`byte[]`). When the 2xx responses
carry different contracts, or a `2XX` range has a body, the method returns a status-keyed
`<OperationName>Response` (`StatusCode`, `IsStatus200`, `Status200`, ...). A `default`-only
operation treats `default` as the success contract, and an omitted `responses` (OpenAPI 3.1)
succeeds on any 2xx. `3XX` declarations are accepted; HttpClient follows redirects and the final
response decides. Typed errors: `<ApiName>Exception` keeps `StatusCode` and `ResponseBody` and adds
`Error`, `TryGetError<T>(out T)` and `ErrorDeserializationException` for the declared JSON error body
(exact status, `4XX` / `5XX`, then `default`). Declared response `headers` (and
`components.headers`) are exposed by an added `<name>WithHttpInfo` method (status code, raw and
typed headers, data); a missing required header raises `JsonSerializationException`.
HEAD responses and successful 204 or 205 responses cannot declare body content, including through
a response reference (`OACG101`).
Parameter Objects recognize standard OpenAPI 3.0/3.1/3.2 fields and `x-*` extensions.
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

| ID | Title | Notes |
| --- | --- | --- |
| `OACG001` | Malformed normalized spec bundle | Invalid Bundle structure or reference edge |
| `OACG002` | Unsupported normalized spec bundle version | Unknown Bundle format version |
| `OACG003` | OpenAPI generation failed | Generic generation failure |
| `OACG004` | Duplicate normalized spec bundle ID | Several Bundles declare the same Spec ID |
| `OACG005` | Invalid OpenAPI client definition | Including a reserved API name |
| `OACG006` | Missing normalized spec bundle | No Bundle for the Spec ID |
| `OACG007` | Duplicate OpenAPI client definition ID | Several client definitions use the same Spec ID |
| `OACG008` | Unsupported OpenAPI document format | Document format other than Json (0) / Yaml (1) |
| `OACG009` | Normalized spec bundle ID mismatch | Bundle Spec ID differs from the AdditionalFile name |
| `OACG010` | Unsupported library selection | Unknown `HttpTransport` / `JsonSerializer` value or a combination without an implementation |
| `OACG011` | JSON library is not referenced | The selected Newtonsoft.Json or System.Text.Json is not referenced |
| `OACG012` | HTTP implementation is not referenced | `System.Net.Http`, or the Unity Web Request module (plus `System.Net.Http`) is missing |
| `OACG013` | Referenced library version is too low | Newtonsoft.Json below 13.0 or System.Text.Json below 8.0 |
| `OACG014` | HttpClient is not supported on WebGL | Warning: `HttpClient` is selected while `UNITY_WEBGL` is defined |
| `OACG100` | Invalid OpenAPI document | Including response DTO metadata name conflicts |
| `OACG101` | Unsupported OpenAPI element | Including identity keywords |
| `OACG102` | Unresolved OpenAPI reference | Document or JSON Pointer target |
| `OACG103` | Cyclic OpenAPI reference |  |
| `OACG104` | External OpenAPI reference is unsupported | Legacy v1 external-reference diagnostic |
| `OACG105` | Inconsistent OpenAPI response contract |  |
| `OACG106` | Invalid generated API identifier | A duplicated explicit `operationId` |
| `OACG107` | Generated type name collision | Warning: the name was disambiguated with a deterministic suffix |
| `OACG108` | Ignored OpenAPI element | Warning: reported with its logical path; never changes the wire contract |

The analyzer reads both Bundle v1 and v2. Bundle v1 remains available for
backward compatibility; the Editor always writes Bundle v2. The complete
schema and reader rules are in
[Normalized Spec Bundle v2](Documentation~/SourceGenerators/NormalizedSpecBundleV2.md).

## Dependencies and provider transitions

The package has one packaged analyzer DLL. Newtonsoft.Json, System.Text.Json,
Roslyn `4.3.1`, and YamlDotNet are not embedded in that DLL or in the UPM
archive, and the package declares no dependency on any of them except the base
package; see [Third Party Notices](Third%20Party%20Notices.md). The Editor
assembly does not reference Newtonsoft.Json either: YAML and JSON parsing in the
Editor use the repository's BCL-only lexer/parser. The generated client needs
the libraries of the selected combination in the project (see
[Choose the HTTP and JSON libraries](#choose-the-http-and-json-libraries)).

When Source Generator is selected and available, the base package synchronizes
`OPENAPI_CODEGEN_SOURCE_GENERATOR` for the active `NamedBuildTarget`. Removing
this add-on removes the define from every known `NamedBuildTarget` before Unity
applies the package transition. Updating the add-on temporarily removes it from
the active target. Code that references generated types may then require source
or provider changes.
