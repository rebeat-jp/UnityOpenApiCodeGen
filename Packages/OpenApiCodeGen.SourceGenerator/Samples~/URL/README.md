# URL sample

This sample uses the pinned raw GitHub fixture for the `0.5.0` package:

```text
https://raw.githubusercontent.com/rebeat-jp/UnityOpenApiCodeGen/0.5.0/Packages/OpenApiCodeGen.SourceGenerator/Samples~/URL/openapi.json
```

Paste that URL into the Generator window with **Source Generator** selected,
then press **Generate**. Generate is the explicit fetch/refresh action. The
Editor determines JSON format from the final URL and response metadata and
publishes the graph as one Bundle v2 AdditionalFile.

The URL fixture is documentation for a pinned release. Automated tests use a
loopback HTTP fixture rather than depending on the public network.
