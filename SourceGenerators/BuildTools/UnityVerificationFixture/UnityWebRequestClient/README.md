# UnityWebRequest client snapshot

`Generated/UwrApi.g.cs` is the output of the Source Generator for `Specs/openapi.json` with
`HttpTransport = UnityWebRequest` and `JsonSerializer = NewtonsoftJson` (the API name is `UwrApi`, the namespace
`Rhycol.OpenApiCodeGen.UnityWebRequestVerification`). It is compiled as ordinary source by the
`Unity.OpenApiCodeGen.SourceGenerator.Verification.UnityWebRequest` assembly so the EditMode tests
(`UnityWebRequestTransportUnityTests`) can run the generated adapter against the real `UnityWebRequest`.

The .NET test `UnityWebRequestFixtureSnapshotTests` fails when the snapshot differs from the current generator
output. Regenerate it with `UPDATE_UNITY_FIXTURE_SNAPSHOT=1 dotnet test -c Release --filter UnityWebRequestFixtureSnapshotTests`
in `SourceGenerators/Rhycol.OpenApiCodeGen.SourceGenerator.Tests` and review the diff.

## Limits

- The snapshot covers one combination only: UnityWebRequest with Newtonsoft.Json. The System.Text.Json combination is
  covered by the .NET tests but not by Unity.
- The snapshot does not go through the generator pipeline in Unity (no `[OpenApiClientDefinition]`, no
  analyzer, no additional file). The existing gate allows one definition (one spec) and one compiler mirror, and
  a second definition would need a second spec. Once the Editor can write `HttpTransport` (P4), a client generated
  through the real pipeline can replace the snapshot.
- The tests do not use `Newtonsoft.Json.Linq`: the Tests asmdef did not resolve `Newtonsoft.Json` in the local
  verification project. The asmdef has `overrideReferences: false`, but the precompiled Newtonsoft assembly of
  the package was not referenced by it (the cause was not investigated; the generated client in the other
  asmdef compiles against Newtonsoft). The tests read the JSON with a regular expression instead.
