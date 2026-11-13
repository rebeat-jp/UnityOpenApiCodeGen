# Changelog

## [Unreleased]

### Added

- Add warning `OACG108` ("Ignored OpenAPI element"), reported with a logical path when the analyzer deliberately ignores an OpenAPI element. It is currently reported for malformed `x-enum-*` arrays and for `const` values that cannot be enforced.
- Generate open objects, maps and free-form objects: an object schema that omits `additionalProperties` (or sets `true` / a schema) keeps undeclared members in an `IDictionary<string, JToken>` `[JsonExtensionData]` member and no longer rejects them in successful responses; `additionalProperties: <schema>` without properties becomes `Dictionary<string, T>`, and a plain `type: object` becomes `JObject`. `additionalProperties: false` stays a closed DTO.
- Infer `type` from `properties`, `items`, `enum` and `const`; map `{}`, the OpenAPI 3.1 boolean schema `true`, and multi-type arrays to `JToken`. The `false` schema reports `OACG101`.
- Allow recursive schemas through properties, array items and `additionalProperties` (self and mutual). Pure `$ref` alias cycles and cycles made only of arrays/maps still report `OACG103`.
- Allow schema `$ref` to any JSON Pointer (property schemas, `$defs`, extension keys) and to any fragment of an external document. The type name comes from the last pointer token and uses the existing `OACG107` collision handling.
- Support integer enums (explicit C# values, numeric wire, `long` when needed), enums containing `null` (nullable), enums without `type`, `x-enum-varnames` / `x-enumNames` and `x-enum-descriptions`. Number, boolean and mixed-type enums still report `OACG101`.
- Support `const` (OpenAPI 3.1): validated in successful responses and used as the initial value of request DTO properties.
- Support `readOnly` (not sent in requests, not required in requests) and `writeOnly` (may be absent from responses).
- Emit `title` / `description` / `default` as XML documentation and `deprecated: true` as `[Obsolete]` on schemas and properties; the generated files disable CS0612 / CS0618.
- A JSON-valued `DateTime` held by a `JToken` is no longer truncated to a date by the request serializer's date converter.
- Support schema composition. `allOf` is flattened into one sealed DTO (required is the union of the parts, closedness is evaluated on the composed object, and parts that declare a property with different types report `OACG101`); `allOf: [ { $ref } ]` with annotations stays a plain reference. In OpenAPI 3.1 a `$ref` with structural siblings (`properties`, `required`, ...) is treated as `allOf: [ { $ref }, { siblings } ]`.
- Support `oneOf` / `anyOf` with a `discriminator` (explicit `mapping` or the schema name): a sealed union wrapper class (`FromX`, implicit conversion, `TryGetX`, `AsX`) over plain member DTOs, with a polymorphic `JsonConverter` that cannot recurse into the members. A discriminator on a base schema whose subtypes use `allOf` is supported as well. Unknown discriminator values are rejected in responses; mappings that point to another file report `OACG101`.
- Support `oneOf` / `anyOf` without a discriminator: `[X, null]` becomes a nullable `X`, anything else is a union wrapper that keeps the raw `JToken` (`FromJToken`, `TryGetX`, `AsX`, primitive candidates included). Responses are checked structurally: `oneOf` must match exactly one candidate, `anyOf` at least one. When several `oneOf` candidates match, the candidates that declare every property of the response are preferred; `oneOf` candidates that cannot be told apart still fail.
- Evaluate `not` while validating successful responses (a value that matches the `not` schema is rejected). It never changes the generated type; a `not` that cannot be evaluated reliably is ignored with `OACG108`.
- Generate a method name for an operation without `operationId` from the HTTP method and the path (`GET /pets/{petId}` becomes `GetPetsByPetId`, `GET /` becomes `GetRoot`); explicit ids win and synthesized names are numbered on collisions.
- Support cookie parameters (sent as one `Cookie` header, values percent-encoded), array and object parameters with every `style` / `explode` combination (path `simple` / `label` / `matrix`, query `form` / `spaceDelimited` / `pipeDelimited` / `deepObject`, header `simple`, cookie `form`), `allowReserved: true` on query parameters, and parameter `content` with one JSON media type. Nested or untyped values stay `OACG101` (use `content`). With `HttpClientHandler.UseCookies=true` a manual `Cookie` header can be merged with or overridden by the `CookieContainer`.
- Follow Path Item `$ref` (internal, external, and OpenAPI 3.1 `components.pathItems`) and honor `servers` on a Path Item or an operation (operation, then Path Item, then document).
- Support several `servers` and server `variables`: the default client uses `servers[0]` with variable defaults substituted. New public members `static IReadOnlyList<string> Servers` and `static string BuildServerUrl(int index, IReadOnlyDictionary<string, string>? variables = null)` (variables are checked against `enum`), and `OperationServerUrls` to replace the URL of an operation that declares its own servers. The `baseUrl` constructor argument only replaces the document-level servers.
- Emit an operation's `deprecated` as `[Obsolete]`, its `description` as `<remarks>`, and a parameter's `description` / `deprecated` in the `<param>` documentation.
- Support `multipart/form-data` request bodies: every property of the object schema is a part (scalars as text parts, arrays as one part per item, nested objects as JSON parts, `format: binary` properties and arrays of them as file parts). File parts use the new generated `<ApiName>FileParameter` (`byte[]` or `Stream` with `FileName` and `ContentType`); a `Stream` is disposed together with the request. The Encoding Object (`contentType`, static `headers`) is honored.
- Support `application/x-www-form-urlencoded` request bodies, serialized with the query `form` style rules and the Encoding Object `style` / `explode` / `allowReserved` (values are percent-encoded; a space is `%20`).
- Support text (`text/*`, `application/xml` with a string schema) and binary (`application/octet-stream`, `image/*`, `*/*`, schema `format: binary` or no schema) request bodies. A wildcard binary media type adds an optional `<body>ContentType` argument.
- When an operation declares several request media types, the highest priority one (JSON, form-urlencoded, multipart, text, binary) keeps the method name and every other one gets an additional method (`<name>AsFormUrlEncoded`, `<name>AsMultipart`, `<name>AsText`, `<name>AsBinary`). `requestBody.required: false` sends no body for `null`.
- Support text (`string`) and binary (`byte[]`) successful responses, and send an `Accept` header (primary media type first) when an operation has a non-JSON response or several media types; the received `Content-Type` selects the decoder and an undeclared one raises `JsonSerializationException`.
- Return a status-keyed `<OperationName>Response` (`StatusCode`, `IsStatus200`, typed `Status200` ...) when the 2xx responses carry different contracts or a `2XX` range declares a body. A `default`-only operation treats `default` as the success contract, an omitted `responses` (OpenAPI 3.1) succeeds on any 2xx, and `3XX` declarations are accepted (HttpClient follows redirects; the final response decides).
- Typed errors: `<ApiName>Exception` keeps `StatusCode` and `ResponseBody` and adds `Error`, `TryGetError<T>(out T)` and `ErrorDeserializationException` for the declared JSON error body (exact status, `4XX` / `5XX`, then `default`). A body that cannot be deserialized stays available as text.
- Support response `headers` and `components.headers`: an operation whose success responses declare headers gets an added `<name>WithHttpInfo` method returning `<Name>HttpInfo` (status code, raw headers, typed headers, data). A missing required header or an invalid value raises `JsonSerializationException`.
- Support `format: binary` (and a non-text `contentMediaType` without `contentEncoding`) for multipart file parts, binary request bodies and binary responses. It stays `OACG101` inside JSON bodies, parameters, headers and form-urlencoded values.

### Changed

- `format: byte` now maps to `byte[]` (base64 on the wire, validated in successful responses) instead of `string`. Source that assigned or read these members as `string` must be updated.
- `deprecated: true` on a schema or DTO property now adds `[Obsolete]` to the generated member, so callers see compiler warnings (CS0612 / CS0618) for deprecated members.
- Object schemas that omit `additionalProperties` generate an open DTO with an extra `AdditionalProperties` member instead of reporting `OACG101`.
- `x-enum-varnames` / `x-enumNames` now name the generated C# enum members. A string enum that already declared these extensions previously got names derived from its values, so its member names (and any code using them) change.
- Generated DTOs with `readOnly` properties or an open `AdditionalProperties` member add an internal `PreserveAttribute` so Unity IL2CPP stripping keeps the reflection-only members.
- Successful response validation now also rejects a missing required DTO property and an undeclared enum value in the JSON check, before deserialization (the exception type is still `JsonSerializationException`; only the message differs).
- OpenAPI 3.1: a `$ref` with `type`, `properties`, `required` or other structural siblings no longer reports `OACG101`; it is composed as `allOf` (siblings that cannot be part of an object composition still report `OACG101`).
- `deprecated: true` on an operation now adds `[Obsolete]` to the generated method (the generated file disables CS0612 / CS0618).
- Header parameters named `Accept`, `Content-Type` or `Authorization` (any case) no longer report `OACG101`: they are ignored as the OpenAPI specification requires, with an `OACG108` warning, and are not method arguments. `Content-Type` was previously rejected as a content-only header.
- Query parameters with `allowReserved: true`, array / object parameters, non-default `style` / `explode`, cookie parameters, parameter `content`, several `servers`, server `variables`, Path Item `$ref` and path / operation `servers` are no longer rejected. An undeclared `{x}` in a server URL, a style that is invalid for the location (`OACG100`) or undefined for the value, and nested values still are.
- Generated clients gain the public members `Servers` and `BuildServerUrl`, and, when an operation or Path Item declares its own servers, `OperationServerUrls` (set at configuration time, not thread-safe; keys are generated method names), `OperationNames` (constants for those keys), `GetOperationServers` and `BuildOperationServerUrl`; schema types with those names are renumbered. Object parameters now also send primitive `additionalProperties` members after the declared ones, and an external file that is only a Path Item (`$ref`, `parameters`, `servers`, `summary`, `description` or an HTTP method, with no schema keyword) has its nested references collected.
- Success responses whose contracts differ no longer report `OACG105`; the operation returns a status-keyed result type instead. `OACG105` remains for different JSON schemas among the media types of one response or request body.
- `text/plain` (and other text / binary) successful response bodies, `2XX` responses with a body, `default`-only operations, omitted `responses` (OpenAPI 3.1), wildcard media types (`*/*`, `type/*`), nonempty response `headers` and `components.headers` are no longer rejected.
- Error responses are parsed leniently for typed errors: a JSON error schema that cannot be generated keeps the body as `ResponseBody` text only. Inline error schemas now generate DTOs named after the operation and status (`<operationId>Error<Status>`).
- A JSON success response schema that declared `format: binary` still reports `OACG101`, now with the binary schema's location.

## [0.5.1] - 2026-11-10

### Added

- Send the user-local GitHub token to `https://raw.githubusercontent.com` root documents and relative external references, never to other origins or redirect targets, and explain 401/403/404 responses from that origin.

## [0.5.0] - 2026-10-06

### Added

- Add cloud Unity verification, exact-candidate release evidence, a DLL-only update PR workflow, and manually started CD with a default dry-run.
- Prepare OpenUPM GitHub Release asset registration for the add-on.
- Report deterministic generated type-name disambiguation with warning `OACG107`.
- Add Phase 6 URL loading and multi-document Bundle v2 support for Issues
  #50–#52. The Editor resolves the complete graph and publishes one Bundle
  v2 AdditionalFile per `specId`; the analyzer consumes the edge map without
  network or filesystem access.
- Add deterministic URL policy, format detection, graph limits, redacted
  manifest metadata, atomic publication, and bare Schema external documents.
- Add the four UPM samples (local JSON, local YAML, URL, and external
  reference) and the `0.5.0` release/rollback documentation.
- Add Phase 5 YAML input support for Issues #46–#49: a BCL-only lexer/parser
  accepts local `.yaml` and `.yml` documents and lowers the supported YAML 1.2
  subset directly to the shared `SpecNode` tree. No YamlDotNet dependency or
  YAML-to-JSON intermediate is introduced.
- Support block/flow mappings and sequences, JSON-compatible scalars, quoted
  and plain strings, literal/folded block scalars with chomping and explicit
  indentation, comments, source order, and anchor/alias expansion. YAML
  source locations are retained through normalization.
- Report `YAML001`–`YAML015` parse and limit diagnostics with source path and
  1-based line/column, while preserving the last-known-good cache and compiler
  mirror for invalid YAML or UTF-8 input.
- Generate deterministic Newtonsoft.Json DTOs, HTTP clients, and API exceptions from the supported OpenAPI 3.0.* and 3.1.* local JSON/YAML MVP surface.
- Parse normalized bundles through an OpenAPI semantic model and internal reference resolver before Roslyn `AddSource` emission.
- Report OACG100–OACG106 diagnostics for invalid documents, unsupported elements, reference failures, inconsistent responses, and invalid identifiers.

- Register the Source Generator with the base package's Generation Provider registry.
- Report analyzer availability without loading analyzer-only dependencies.
- Remove the provider define before add-on package transitions are applied.
- Add the runtime client-definition attribute and document-format contract;
  Phase 5 extends `OpenApiDocumentFormat` with JSON (`0`) and YAML (`1`) and
  propagates it through provider, definition, cache, and analyzer matching.
- Generate an owned attributed partial definition for the selected target assembly.
- Join multiple client definitions to normalized bundles by stable Spec ID.

### Changed

- Label Source Generator generation as beta in Settings, Generator, documentation, and add-on Package Manager metadata; preserve package IDs, versions, and saved provider values.

- Accept local and HTTP(S) Source Generator inputs. `Generate` is the explicit
  fetch/refresh operation; URL queries are used for the current request and
  are not persisted in plain text.
- Read Bundle v1 for compatibility while the Editor writes Bundle v2. Resolve
  document identities as `(documentId, JSON Pointer)` and report unresolved
  targets and cycles with stable diagnostics.
- Treat unsupported wire-affecting OpenAPI features as generation diagnostics instead of silently ignoring them.
- Generate from local `.json`, `.yaml`, or `.yml` and HTTP(S) inputs through the
  normalized cache and compiler mirror without Docker fallback. The canonical
  Bundle remains JSON for both raw formats, and JSON/YAML semantic and
  generated-source parity is verified for the shared MVP surface.
- Avoid rewriting unchanged cache and definition inputs or requesting unnecessary script compilation.

### Fixed

- Move uniquely owned definitions between output folders in the same asmdef/API/namespace while preserving SpecId and GUID; recover interrupted moves and preserve concurrent edits as a definition/meta group.
- Diagnose unsupported `$ref` siblings and required nullable path/query/header parameters with `OACG101` instead of silently dropping schema constraints or failing on null at runtime.
- Reject dot-only path segments, including percent-encoded forms, before HTTP transmission.
- Bundle the offline Support Matrix and its relative references in both packages.
- Reserve container termination time independently of cleanup so license return retains its full allowance, and surface sanitized gate failure reasons without workflow-command injection.
- Normalize and validate parameterized JSON media types and request charsets before generation.
- Separate error-response schema validation from successful JSON body generation; preserve response extensions and error bodies.
- Preserve nullable through multi-hop references, reject complex referenced parameters, merge headers case-insensitively, and diagnose generic definitions and non-ASCII status codes.
- Permit owned namespace changes while retaining SpecId and GUID and protecting concurrent edits during publication and recovery.
- Cache semantic reference scans and completed schema validation, preserving literal data and YAML alias context.
- Bound Unity CI processes and retain partial failure evidence; compile consumers with Test Framework but without testables.

- Sanitize URL reference/query display data and unexpected generation failures without changing fetch identity.
- Preserve successful artifacts when generation is canceled after loading but before publication.
- Exclude concurrent generation/recovery across threads and Unity processes, including failed local reentry followed by a cross-process attempt.
- Retain durable compilation intent and recover interrupted publication without losing the compilation request.

## [0.1.0]

### Added

- Add the source-generator UPM package and explicit analyzer owner assembly.
- Add the Editor-side Normalized Spec Bundle v1 normalizer and cache service.
