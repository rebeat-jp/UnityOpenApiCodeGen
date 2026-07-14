# Change Log

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/)
and this project adheres to [Semantic Versioning](http://semver.org/).

## unreleased

### Added

- Add public Editor generation-provider contracts and registry.
- Add explicit Docker and Source Generator provider selection.
- Synchronize `OPENAPI_CODEGEN_SOURCE_GENERATOR` for the active build target.
- Remove the Source Generator provider and define before package transitions.

### Changed

- Make the Setup window Docker-specific and route Generate through the saved provider.
- Keep an open Generator window synchronized with the saved provider.

### Fixed

- Stop unknown or unavailable providers from falling back to Docker.
- Avoid main-thread deadlocks in synchronous Docker provider generation.
- Preserve the Dedicated Server named build target during define synchronization.

## [0.4.0]

### Fixed

- change package name
- change namespace Begining by "Rhycol"

## [0.3.1]

### Fixed

- change package name in uxml

## [0.3.0]

### Feature

- Display Generated Result on Generate Menu

### Fixed

- Change Setting Menu Layout
- Independent From SwaggerGen
- Available to Use Relative Path in Output Path(from project root)

## [0.2.2]

### Fixed

- Change to EnumField at OpenAPI Library Setting
- Change to independent R3, UniTask, .NET.Json

## [0.2.1]

### Fixed

- Not work OpenApiCodeGenerator

## [0.2.0]

### Add

- Setting Menu

### Fixed

- Setup Menu
- Generate Client By Docker
- Save config file path is Changed.
- Save Setting by Json

## [0.1.0] - 2024-3-21

### Added

- Generate Rest API client code used Open API Generator.
