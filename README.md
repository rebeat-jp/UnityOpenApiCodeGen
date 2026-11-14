# Unity OpenAPI CodeGen

[日本語](README.ja.md)

Unity OpenAPI CodeGen generates strongly typed C# REST clients from OpenAPI
documents. This repository contains the base package (Docker provider and
Editor UI) and the optional Source Generator package (Unity 6, local or URL
documents).

Source Generator generation is in beta. Review the supported features before use. See the
[Support Matrix](SourceGenerators/OpenApiMvpSupportMatrix.md). The Editor provider is named **Source Generator (Beta)**.
The Editor's **Open Support Matrix** button opens the package-bundled HTML;
both packages include the matrix and its relative reference documents for offline use.

## Requirements

| Package | Minimum Unity | Additional requirement |
| --- | --- | --- |
| `jp.rhycol.openapicodegen` | 2021.3 | Docker only when the Docker provider is selected |
| `jp.rhycol.openapicodegen.source-generator` | 6000.0 | The base package and the libraries of the selected combination (see [Choose the HTTP and JSON libraries](#choose-the-http-and-json-libraries)); by default Newtonsoft.Json 13.0 or later, which you install yourself |

## Install from Git

Add both entries to the Unity project's `Packages/manifest.json` when using
the Source Generator provider. Unity resolves these as separate Git packages.

```json
{
  "dependencies": {
    "jp.rhycol.openapicodegen": "https://github.com/rebeat-jp/UnityOpenApiCodeGen.git?path=/Packages/OpenApiCodeGen#0.5.1",
    "jp.rhycol.openapicodegen.source-generator": "https://github.com/rebeat-jp/UnityOpenApiCodeGen.git?path=/Packages/OpenApiCodeGen.SourceGenerator#0.5.1"
  }
}
```

Since 0.6.0 the Source Generator package no longer depends on
`com.unity.nuget.newtonsoft-json`: installing it does not install Newtonsoft.Json.
It does not bundle Newtonsoft.Json, System.Text.Json, Roslyn, or YamlDotNet
DLLs. Install the libraries of the combination you select (next section).
Upgrading from 0.5.x with the default combination requires installing
`com.unity.nuget.newtonsoft-json` `3.2.2` yourself; until then existing
definitions report `OACG011`.

These examples use the `0.5.1` release tag. To try an unreleased commit,
replace `0.5.1` with the same reviewed commit SHA in both Git URLs.

## Getting started

1. Open `Window/OpenAPI Code Generator/Settings` and select `Docker` or
   `Source Generator (Beta)`.
2. If Docker is selected, configure the Docker executable in the Setup window.
   Setup is Docker-specific and does not switch the saved provider.
3. Open `Window/OpenAPI Code Generator/Generator`. Enter the document path or
   HTTP(S) URL and an output folder under `Assets`, then press `Generate`.
   `Generate` is the explicit fetch/refresh operation for URL inputs; there is
   no background fetch.
4. For Source Generator output, the target assembly definition must directly
   reference `Unity.OpenApiCodeGen.SourceGenerator`.

The Source Generator accepts local `.json`, `.yaml`, and `.yml` documents and
HTTP(S) URLs. Remote format is selected from the final URL extension and
`Content-Type`; they must agree when both are recognized. JSON content types
include `application/json` and `+json`; YAML includes
`application/yaml`, `application/x-yaml`, `text/yaml`, `text/x-yaml`, and
`text/x-yml`. Content sniffing is not used.

External references are loaded by the Editor and published as one deterministic
Bundle v2 per `specId`; the analyzer reads only the resulting AdditionalFile.
Local external files must remain inside the Unity project after real-path and
symbolic-link checks. URL inputs may include public, private, or loopback
hosts, but userinfo, non-HTTP(S) schemes, remote-to-local references, and
HTTPS-to-HTTP redirects are rejected. URL queries are used for the current
fetch identity and must be entered again on the next Generate; they are not
persisted in project settings, bundles, manifests, or diagnostics.

Invalid input leaves the last successful cache and compiler mirror intact but
the Generate operation still fails. A byte-identical Generate does not request
script compilation. Provider selection never falls back to Docker; removing
the add-on removes its provider define before the package transition is
applied.

The Generator window reports progress, warnings, and cancellation. Cancel
stops Source Generator work before publication; once publication begins, it
finishes as one protected operation. Docker settings retain their existing
URL behavior.

## Choose the HTTP and JSON libraries

A Source Generator client is generated for one HTTP implementation and one JSON
serializer. The default is **HttpClient + Newtonsoft.Json**; all four
combinations of the two options below are implemented.

| Choice | What the project needs | Minimum |
| --- | --- | --- |
| HTTP: `HttpClient` (default) | `System.Net.Http` (normally available without installing anything) | - |
| HTTP: `UnityWebRequest` | The Unity Web Request module (`com.unity.modules.unitywebrequest`) and `System.Net.Http`, because the adapter still builds `HttpRequestMessage` / `HttpContent` | - |
| JSON: `Newtonsoft.Json` (default) | `com.unity.nuget.newtonsoft-json` `3.2.2`, or any other Newtonsoft.Json reference | 13.0 |
| JSON: `System.Text.Json` | UnityNuGet `org.nuget.system.text.json` (which brings `System.Text.Encodings.Web`), or the DLLs through NuGetForUnity or by hand | 8.0 |

- **Select** the combination in `Window/OpenAPI Code Generator/Settings`,
  **Source Generator Libraries**. The section shows the state of each choice,
  disables choices that are not ready, and offers to install Newtonsoft.Json or
  enable the Unity Web Request module. System.Text.Json is shown as manual
  steps (UnityNuGet scoped registry plus `org.nuget.system.text.json` 8.0 or
  later); the Editor never edits `Packages/manifest.json` for it. The libraries
  are detected from what the output assembly references, so NuGetForUnity or
  manually placed DLLs work too.
- The Generator window shows the combination and its state, and disables
  `Generate` with the reason and a link to Settings while a library is missing.
- The choice is stored in the definition file as the named properties
  `HttpTransport` / `JsonSerializer` of `OpenApiClientDefinitionAttribute`
  (for example `HttpTransport = OpenApiHttpTransport.UnityWebRequest`). The
  defaults are not written, so definition files created before 0.6.0 stay
  unchanged and keep using HttpClient + Newtonsoft.Json.
- **WebGL**: choose `UnityWebRequest`. `System.Net.Http.HttpClient` does not
  work in WebGL players; the Settings window warns, and the analyzer reports
  warning `OACG014`.

The analyzer makes the final decision when it compiles the project and reports
these diagnostics for a client definition:

| ID | Severity | Reported when |
| --- | --- | --- |
| `OACG010` | Error | The definition names an unknown `HttpTransport` / `JsonSerializer` value, or a combination without an implementation |
| `OACG011` | Error | The selected JSON library is not referenced |
| `OACG012` | Error | The selected HTTP implementation is not referenced (`UnityWebRequest` also needs `System.Net.Http`) |
| `OACG013` | Error | The referenced library is older than the minimum (Newtonsoft.Json 13.0, System.Text.Json 8.0) |
| `OACG014` | Warning | `HttpClient` is selected while `UNITY_WEBGL` is defined |

While one of the errors applies, the client is not generated. The
[support matrix](SourceGenerators/OpenApiMvpSupportMatrix.md) lists the
differences between System.Text.Json and Newtonsoft.Json, the UnityWebRequest
limitations, and the IL2CPP / WebGL Player verification results (Unity
`6000.3.2f1`).

## Support and release documents

- [Source Generator README](Packages/OpenApiCodeGen.SourceGenerator/README.md)
- [OpenAPI support matrix](SourceGenerators/OpenApiMvpSupportMatrix.md)
- [Normalized Spec Bundle v2](SourceGenerators/NormalizedSpecBundleV2.md)
- [Release and rollback procedure](RELEASE.md)

## CI and release preparation

[Source Generator CI](https://github.com/rebeat-jp/UnityOpenApiCodeGen/actions/workflows/source-generator-ci.yml)
checks .NET tests, analyzer reproducibility/synchronization, and UPM contents.
It verifies one clean-commit candidate on Unity `2021.3.19f1`, `6000.0.23f1`,
and `6000.3.2f1` in serial GitHub-hosted jobs, then runs the CD dry-run.
Fork PRs receive the .NET/package checks without Unity credentials.

Use the successful run and its `verified-release-<run-id>-<attempt>` artifact
for the exact commit under review. The manifest binds the candidate packages
to all three editors' logs, test XML, and checksums. Missing or failed evidence
blocks release.

Manual workflows can open DLL-only update PRs or start a release, whose
default is dry-run. The base retains OpenUPM Git-tag tracking; the add-on uses
the verified GitHub Release tarball after its
[initial OpenUPM registration](SourceGenerators/OpenUPM/README.md).
See [RELEASE.md](RELEASE.md) for inputs, retry and rollback procedures.

## License

This repository is distributed under the [MIT License](LICENSE).
