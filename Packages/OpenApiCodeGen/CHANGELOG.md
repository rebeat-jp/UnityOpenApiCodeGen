# Change Log

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/)
and this project adheres to [Semantic Versioning](http://semver.org/).

## Unreleased

## [0.5.0] - Unreleased

### Added

- Add public Editor generation-provider contracts and registry.
- Add explicit Docker and Source Generator provider selection.
- Synchronize `OPENAPI_CODEGEN_SOURCE_GENERATOR` for the active build target.
- Remove the Source Generator provider and define before package transitions.
- Pass the configured API name and namespace through the provider-neutral generation request.
- Document the `0.5.0` Git installation, provider transition, and rollback procedure.
- Add generation progress, cancellation, and warning states to the Generator window.
- Add serial cloud Unity verification, release evidence, and a manually started release workflow defaulting to dry-run.

### Changed

- Label Source Generator generation as beta in Settings, Generator, documentation, and add-on Package Manager metadata; preserve package IDs, versions, and saved provider values.

- Make the Setup window Docker-specific and route Generate through the saved provider.
- Keep an open Generator window synchronized with the saved provider.
- Keep the base package usable without the optional Source Generator add-on.

### Fixed

- Open the package-bundled offline Support Matrix instead of a feature-branch URL; verify its relative links in both tarballs.
- Exercise actual settings persistence from externally launched Unity consumers and detect cwd-dependent initialization with the production path definition.
- Resolve project settings from `Application.dataPath`, independent of the Editor process working directory.
- Keep API name and package name editable for Source Generator while disabling Docker-specific fields.
- Verify consumer compilation both without Test Framework and with Test Framework but without package testables.

- Stop unknown or unavailable providers from falling back to Docker.
- Avoid main-thread deadlocks in synchronous Docker provider generation.
- Preserve the Dedicated Server named build target during define synchronization.
- Preserve existing Docker URL settings while sanitizing Source Generator query data.
- Ignore stale async generation/provider callbacks after window disposal or rebinding.

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
