# Unity OpenAPI CodeGen

Unity OpenAPI CodeGen generates strongly typed C# REST clients from OpenAPI
documents. This repository contains the base package (Docker provider and
Editor UI) and the optional Source Generator package (Unity 6, local or URL
documents).

## Requirements

| Package | Minimum Unity | Additional requirement |
| --- | --- | --- |
| `jp.rhycol.openapicodegen` | 2021.3 | Docker only when the Docker provider is selected |
| `jp.rhycol.openapicodegen.source-generator` | 6000.0 | The base package and Unity's Newtonsoft Json package |

## Install from Git

Add both entries to the Unity project's `Packages/manifest.json` when using
the Source Generator provider. Unity resolves these as separate Git packages.

```json
{
  "dependencies": {
    "jp.rhycol.openapicodegen": "https://github.com/rebeat-jp/UnityOpenApiCodeGen.git?path=/Packages/OpenApiCodeGen#0.5.0",
    "jp.rhycol.openapicodegen.source-generator": "https://github.com/rebeat-jp/UnityOpenApiCodeGen.git?path=/Packages/OpenApiCodeGen.SourceGenerator#0.5.0"
  }
}
```

The Source Generator package depends on
`com.unity.nuget.newtonsoft-json` `3.2.2`. It does not bundle Newtonsoft.Json,
Roslyn, or YamlDotNet DLLs.

## Getting started

1. Open `Window/OpenAPI Code Generator/Settings` and select `Docker` or
   `Source Generator`.
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

## Support and release documents

- [Source Generator README](Packages/OpenApiCodeGen.SourceGenerator/README.md)
- [OpenAPI MVP support matrix](SourceGenerators/OpenApiMvpSupportMatrix.md)
- [Normalized Spec Bundle v2](SourceGenerators/NormalizedSpecBundleV2.md)
- [Release and rollback procedure](RELEASE.md)

## Current verification status

The `0.5.0` candidate has passing automated evidence: the Source Generator
suite is **101/101**, and analyzer reproducibility, analyzer synchronization,
package inspection, tarball reproducibility, and checksum verification pass.
The manual Unity tarball gate also passes: base Unity `2021.3.19f1` is
**36/36**; Unity `6000.0.23f1` and `6000.3.2f1` each pass the Source Generator
initial run **185/185**, regeneration **185/185**, and without-add-on run
**36/36**. The aggregate evidence is recorded in
`artifacts/upm/0.5.0/unity-gate.json`.

The repository is not release-ready yet. The Unity GitHub Actions job required
by #54 is intentionally not implemented, so #54 and Phase 7 remain open even
though the manual gate passed. See [RELEASE.md](RELEASE.md) for the clean-tree
repack, publication boundary, and rollback procedure.

## License

This repository is distributed under the [MIT License](LICENSE).
