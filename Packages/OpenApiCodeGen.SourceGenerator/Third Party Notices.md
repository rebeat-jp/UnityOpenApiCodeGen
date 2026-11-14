# Third-party notices

This file identifies third-party components used to build the Source
Generator package or by the clients it generates. The package does not
redistribute any of the listed DLLs, neither in its analyzer archive nor in the
UPM package.

## Newtonsoft.Json (installed by the user)

- Package: `com.unity.nuget.newtonsoft-json` (`3.2.2` is the version the Editor
  installs on request), or any other Newtonsoft.Json reference of version 13.0
  or later
- Role: JSON serialization of generated clients that select Newtonsoft.Json
  (the default); the generated code references it
- License: MIT
- License and source: [James Newton-King/Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json)

Before 0.6.0 the UPM package declared `com.unity.nuget.newtonsoft-json` as a
dependency. Since 0.6.0 it does not: the package neither declares nor bundles
Newtonsoft.Json, and the Editor assembly does not reference it. A project that
generates Newtonsoft.Json clients installs it itself. `Newtonsoft.Json.dll` is
not copied into the packaged analyzer output.

## System.Text.Json (installed by the user)

- Package: UnityNuGet `org.nuget.system.text.json` `8.0` or later (which brings
  `System.Text.Encodings.Web`), or the DLLs added through NuGetForUnity or by hand
- Role: JSON serialization of generated clients that select System.Text.Json;
  the generated code references it and `System.Text.Encodings.Web`
- License: MIT
- License and source: [dotnet/runtime](https://github.com/dotnet/runtime)

System.Text.Json is optional and is never installed or declared by this
package. The Editor only shows the manual installation steps and never edits
`Packages/manifest.json` for it. No System.Text.Json DLL is redistributed.

## Microsoft.CodeAnalysis / Roslyn

- Version used by the analyzer build: `4.3.1`
- Role: Roslyn incremental generator and `AddSource` emission
- License: MIT
- License and source: [dotnet/roslyn](https://github.com/dotnet/roslyn)

Roslyn assemblies are analyzer build/runtime dependencies and are not bundled
inside the UPM analyzer archive. The release verification rejects packaged
Roslyn DLLs.

## YAML parser

The Editor YAML reader is the repository's BCL-only lexer/parser. YamlDotNet is
not a dependency and no YamlDotNet DLL is redistributed.
