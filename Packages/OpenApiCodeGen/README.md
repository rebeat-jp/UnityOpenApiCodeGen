# Unity OpenAPI CodeGen

## Purpose and audience

This base Unity package provides the OpenAPI CodeGen Editor windows and the
provider registry. It supports the Docker provider on Unity 2021.3 or later
and is also the required base package for the optional Source Generator add-on
on Unity 6.

## Install

For the Source Generator provider, add both packages to the Unity project's
`Packages/manifest.json`:

```json
{
  "dependencies": {
    "jp.rhycol.openapicodegen": "https://github.com/rebeat-jp/UnityOpenApiCodeGen.git?path=/Packages/OpenApiCodeGen#0.5.0",
    "jp.rhycol.openapicodegen.source-generator": "https://github.com/rebeat-jp/UnityOpenApiCodeGen.git?path=/Packages/OpenApiCodeGen.SourceGenerator#0.5.0"
  }
}
```

The base package is also usable by itself with the Docker provider. The
Source Generator add-on requires Unity 6000.0 or later and
`com.unity.nuget.newtonsoft-json` `3.2.2`.

## Use the provider UI

1. Select the project-wide provider in
   `Window/OpenAPI Code Generator/Settings`.
2. Configure Docker in `Window/OpenAPI Code Generator/Setup` only when using
   the Docker provider.
3. In `Window/OpenAPI Code Generator/Generator`, enter the document path or
   URL and an output folder under `Assets`, then press `Generate`.

`Generate` is the explicit fetch/refresh action for URL-based Source Generator
inputs. Provider resolution is strict: an unknown, unavailable, or failing
provider does not fall back to Docker.

When Source Generator is selected and available, the base package synchronizes
`OPENAPI_CODEGEN_SOURCE_GENERATOR` for the active `NamedBuildTarget`. Removing
or updating the add-on removes the define before Unity applies the package
transition. Code that still references generated types can therefore fail to
compile after switching providers.

## License

This package is distributed under the [MIT License](LICENSE.md).
