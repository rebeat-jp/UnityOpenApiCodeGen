# Changelog

## Unreleased

### Added

- Register the Source Generator with the base package's Generation Provider registry.
- Report analyzer availability without loading analyzer-only dependencies.
- Remove the provider define before add-on package transitions are applied.
- Add the runtime client-definition attribute and JSON document-format contract.
- Generate an owned attributed partial definition for the selected target assembly.
- Join multiple client definitions to normalized bundles by stable Spec ID.

### Changed

- Generate from local JSON through the normalized cache and compiler mirror without Docker fallback.
- Avoid rewriting unchanged cache and definition inputs or requesting unnecessary script compilation.

## [0.1.0]

### Added

- Add the source-generator UPM package and explicit analyzer owner assembly.
- Add the Editor-side Normalized Spec Bundle v1 normalizer and cache service.
