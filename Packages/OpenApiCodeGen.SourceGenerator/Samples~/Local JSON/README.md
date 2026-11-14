# Local JSON sample

This sample is a minimal OpenAPI 3.1 JSON document for the `Items API`.

## Required libraries

The generated client uses **HttpClient** and **Newtonsoft.Json** unless the
Source Generator settings choose otherwise. Since 0.6.0 the Source Generator
package does not install Newtonsoft.Json itself: install the
`com.unity.nuget.newtonsoft-json` package (`3.2.2`) in your project first.
Without it, Generate is disabled in the Generator window and a definition that
already exists reports `OACG011` when it is compiled. The Settings window
(`Window/OpenAPI Code Generator/Settings`, **Source Generator Libraries**)
shows which libraries are ready and can install Newtonsoft.Json for you.

1. Install the base and Source Generator packages (and Newtonsoft.Json, see above).
2. Select **Source Generator** in `Window/OpenAPI Code Generator/Settings`.
3. In the Generator window, select `openapi.json`, choose an output folder
   under `Assets` that belongs to an asmdef referencing
   `Unity.OpenApiCodeGen.SourceGenerator`, and press **Generate**.
4. Confirm that the generated client and the `specId` cache are created.

The same input can be regenerated without requesting compilation when the
Bundle bytes and generated definition are unchanged.
