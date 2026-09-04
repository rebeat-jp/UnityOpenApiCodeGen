# Third-party notices

This file identifies third-party components used to build or run the Source
Generator package. The package does not redistribute the listed DLLs in its
analyzer archive unless Unity supplies them through the declared package or
the analyzer build environment.

## Newtonsoft.Json via Unity package

- Package: `com.unity.nuget.newtonsoft-json`
- Version: `3.2.2`
- Role: Editor-side JSON parsing and generated client serialization contract
- License: MIT
- License and source: [James Newton-King/Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json)

The UPM package declares this dependency; `Newtonsoft.Json.dll` is not copied
into the packaged analyzer output.

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
