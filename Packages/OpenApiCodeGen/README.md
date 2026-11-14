# Unity OpenAPI CodeGen

[日本語](README.ja.md) · [日本語の対応表](Documentation~/SourceGenerators/OpenApiMvpSupportMatrix.ja.md)

## Purpose and audience

This base Unity package provides the OpenAPI CodeGen Editor windows and the
provider registry. It supports the Docker provider on Unity 2021.3 or later
and is also the required base package for the optional Source Generator add-on
on Unity 6.

Source Generator generation is in beta. Review the supported features before use. See the
[Support Matrix](Documentation~/SourceGenerators/OpenApiMvpSupportMatrix.md). The Editor provider is named **Source Generator (Beta)**.

## Install

For the Source Generator provider, add both packages to the Unity project's
`Packages/manifest.json`:

```json
{
  "dependencies": {
    "jp.rhycol.openapicodegen": "https://github.com/rebeat-jp/UnityOpenApiCodeGen.git?path=/Packages/OpenApiCodeGen#0.5.1",
    "jp.rhycol.openapicodegen.source-generator": "https://github.com/rebeat-jp/UnityOpenApiCodeGen.git?path=/Packages/OpenApiCodeGen.SourceGenerator#0.5.1"
  }
}
```

The base package is also usable by itself with the Docker provider. The
Source Generator add-on requires Unity 6000.0 or later. Since 0.6.0 it does
not install Newtonsoft.Json: install the libraries of the selected HTTP / JSON
combination yourself (by default `com.unity.nuget.newtonsoft-json` `3.2.2`;
`UnityWebRequest` and `System.Text.Json` are the alternatives).

These examples use the `0.5.1` release tag. To try an unreleased commit, replace it
with the same reviewed commit SHA in both Git URLs. See the
[Source Generator README](../OpenApiCodeGen.SourceGenerator/README.md) for
input support, references, and generated-code requirements.

## Use the provider UI

1. Select the project-wide provider in
   `Window/OpenAPI Code Generator/Settings`.
2. Configure Docker in `Window/OpenAPI Code Generator/Setup` only when using
   the Docker provider.
3. In `Window/OpenAPI Code Generator/Generator`, enter the document path or
   URL and an output folder under `Assets`, then press `Generate`.

While the Source Generator provider is selected, **Source Generator Libraries**
in the Settings window selects the HTTP implementation (HttpClient or
UnityWebRequest) and the JSON serializer (Newtonsoft.Json or System.Text.Json),
shows whether each choice is ready, and installs Newtonsoft.Json or the Unity
Web Request module on request. The Generator window disables `Generate` with
the reason and a link to Settings while a library of the selection is missing.
The library state is separate from the provider's availability, so a missing
library never removes `OPENAPI_CODEGEN_SOURCE_GENERATOR`. The library check needs the Default Output Folder Path to be set (the
libraries are checked against that folder's assembly). The Docker provider
UI is unchanged, also when the add-on is installed. See the
[Source Generator README](../OpenApiCodeGen.SourceGenerator/README.md) for the
requirements and diagnostics.

To read a document from a private GitHub repository, use its
`https://raw.githubusercontent.com/...` URL and set **GitHub Token** in the
User Settings section of `Window/OpenAPI Code Generator/Settings`. Both
providers send the token only to `https://raw.githubusercontent.com`, check
every redirect hop again, and store it in plain text in the per-user settings
file outside the Unity project. The Docker provider passes only the root
document to the container, so it cannot resolve relative external references;
use the Source Generator provider or a local copy for split documents.

`Generate` is the explicit fetch/refresh action for URL-based Source Generator
inputs. Provider resolution is strict: an unknown, unavailable, or failing
provider does not fall back to Docker.

The window displays progress, warnings, and cancellation separately. Closing
or rebinding the window cancels its outstanding generation and ignores stale
callbacks. URL query sanitization applies to Source Generator settings;
existing Docker URL settings retain their original value.
Settings edits and provider changes are saved in event order; a failed save is
reported in the Unity Console. Canceling an active Docker generation stops the
Docker CLI and attempts to remove its named container. If Docker is unavailable
and a container remains, inspect it with
`docker ps -a --filter name=openapi-codegen-` and remove that container.

When Source Generator is selected and available, the base package synchronizes
`OPENAPI_CODEGEN_SOURCE_GENERATOR` for the active `NamedBuildTarget`. Removing
the add-on removes the define from every known `NamedBuildTarget` before Unity
applies the package transition, including targets that are not active. Updating
the add-on temporarily removes it from the active target during the transition.
Code that still references generated types can therefore fail to compile after
switching providers.

## License

This package is distributed under the [MIT License](LICENSE.md).
