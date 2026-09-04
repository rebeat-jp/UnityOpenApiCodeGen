# External reference sample

`openapi.json` is the root OpenAPI 3.1 document and `models.yaml` is a bare
whole-document Schema referenced by `./models.yaml`.

1. Install the base and Source Generator packages and select **Source
   Generator**.
2. In the Generator window, select `openapi.json` and choose an output folder
   under `Assets` whose asmdef directly references
   `Unity.OpenApiCodeGen.SourceGenerator`.
3. Press **Generate**.

The Editor reads both files, validates the whole-document target, and publishes
one deterministic Bundle v2 containing the root document, the YAML document,
and their reference edge. The analyzer receives only that Bundle and does not
read the external file itself.
