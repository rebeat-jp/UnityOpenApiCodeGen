# Changelog

## Unreleased

### Added

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

- Treat unsupported wire-affecting OpenAPI features as generation diagnostics instead of silently ignoring them.
- Generate from local `.json`, `.yaml`, or `.yml` through the normalized cache and
  compiler mirror without Docker fallback. The canonical bundle remains JSON
  for both raw formats, and JSON/YAML semantic and generated-source parity is
  verified for the shared MVP surface.
- Avoid rewriting unchanged cache and definition inputs or requesting unnecessary script compilation.

## [0.1.0]

### Added

- Add the source-generator UPM package and explicit analyzer owner assembly.
- Add the Editor-side Normalized Spec Bundle v1 normalizer and cache service.
