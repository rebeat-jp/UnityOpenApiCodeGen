# Unity OpenAPI CodeGen

## What's Unity OpenAPI CodeGen

You can Access Rest API to use OpenAPI Generator in Unity.
This Plugin is able to operate the OpenAPI CodeGen, and setting on GUI.

## Required Environment

- Unity (2021.3) or later
- Docker when using the OpenAPI Generator provider
- Unity 6 and the optional `jp.rhycol.openapicodegen.source-generator` package when using the Source Generator provider

## Getting started

1. Add this package to Unity Package Manager from git url, and install this package.
2. Open setup window at `Window/OpenAPI Code Generator/Setup` in the Unity's menu, and input absolute Docker Path. After that, press the `Setup` button.  <img width="1680" alt="スクリーンショット 2025-05-06 15 55 50" src="https://github.com/user-attachments/assets/135020e4-4a5f-4333-86cb-af41383c2fde" />

3. Open generator window at `Window/OpenAPI Code Generator/Generator` in the Unity's menu. Input API Document file absolute URL and API Client code output absolute folder, and press the `Generate` button. <img width="1680" alt="スクリーンショット 2025-05-06 15 55 21" src="https://github.com/user-attachments/assets/cee3a098-6c02-4344-9697-c92ac5b2385e" />

## Generation providers

Select the project-wide provider from `Window/OpenAPI Code Generator/Settings`.
The Setup window configures Docker only and does not change the saved provider.

- `OpenApi = 0`: the existing OpenAPI Generator CLI running through Docker.
- `SourceGenerator = 1`: the optional incremental source-generator add-on for Unity 6.

Provider resolution never falls back to Docker. An unknown, unregistered, or
unavailable provider produces an explicit error instead.

Phase 2 registers and validates the Source Generator provider, but its Generate
operation intentionally reports that the local JSON pipeline is pending Phase 3.
Selecting an available Source Generator provider enables
`OPENAPI_CODEGEN_SOURCE_GENERATOR` for the active `NamedBuildTarget`; selecting
Docker removes it when that target is synchronized. Removing or updating the
add-on unregisters the provider and removes the define before Unity applies the
package transition.
Changing providers can therefore remove generated types and expose compile
errors in code that still references them.

## Custamize
You can set default API Document URL, default Client File Output Path, Docker Path, and Generating C# Client Option.
Look Settings window at `Window/OpenAPI Code Generator/Settings`.
<img width="1680" alt="スクリーンショット 2025-05-06 15 55 35" src="https://github.com/user-attachments/assets/c988a0df-b3e6-4815-becf-2161232d2f2c" />


## Ecosystem
- [OpenAPI Generator](https://openapi-generator.tech/)
- [UniTask](https://github.com/Cysharp/UniTask)
- [R3](https://github.com/Cysharp/R3)

## License

This package is under a [MIT License](LICENSE)
