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

### Changed

- `format: byte` now maps to `byte[]` (base64 on the wire, validated in successful responses) instead of `string`. Source that assigned or read these members as `string` must be updated.
- `deprecated: true` on a schema or DTO property now adds `[Obsolete]` to the generated member, so callers see compiler warnings (CS0612 / CS0618) for deprecated members.
- Object schemas that omit `additionalProperties` generate an open DTO with an extra `AdditionalProperties` member instead of reporting `OACG101`.
- `x-enum-varnames` / `x-enumNames` now name the generated C# enum members. A string enum that already declared these extensions previously got names derived from its values, so its member names (and any code using them) change.
- Generated DTOs with `readOnly` properties or an open `AdditionalProperties` member add an internal `PreserveAttribute` so Unity IL2CPP stripping keeps the reflection-only members.

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
