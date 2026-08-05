# Production Source Generator

このdirectoryには、`Rhycol.OpenApiCodeGen.SourceGenerator`のsource、tests、build toolsを置きます。
Analyzerは`netstandard2.0`、testsとbuild toolsは.NET SDK 10.0.301を使用します。

## Phase 4/5 OpenAPI generation

Source Generator providerはlocal `.json`、`.yaml`、`.yml`（extension
case-insensitive）を入力として受け付けます。JSONはJson.NET、YAMLはBCL-only
lexer/parserで構文解析し、どちらもshared `SpecNode`へ直接正規化します。
Normalized Spec Bundle v1のcanonical envelopeはraw形式に関係なくJSONです。
その後、OAS semantic parserとinternal `$ref` resolverでRoslyn非依存の
generation modelを作成し、Roslynでparseするdeterministic emitterがsourceを
生成してanalyzerが`AddSource`します。対応範囲、YAML subset、診断、generated
APIと利用制約は[OpenAPI MVP support matrix](OpenApiMvpSupportMatrix.md)を参照してください。

YAML parserはYAML全仕様を実装しません。block/flow mapping・sequence、simple
string key、JSON-compatible scalar、quoted/plain scalar、literal/folded block
scalar、chomping、explicit indent、anchor/aliasを対象にします。flow内の
delimiter（`,`, `[`, `]`, `{`, `}`）を含むplain valueはquoteが必要です。一方で
URL scheme colon（`https://`）は受理します。merge key、tag、directive、複数
document、complex key、flow内block scalarは拒否します。YAML parser用asmdefは
`refs=[]`、`noEngineReferences=true`の専用BCL-only構成で、YamlDotNet依存と
YAML→JSON中間変換はありません。

wire formatへ影響する未対応のOpenAPI機能はsilentに無視せず、diagnosticとして生成を失敗させます。
生成されたpublic APIは再Generateされるcontractであり、手編集するsourceではありません。

ProviderはURLを受理せず、Dockerへfallbackしません。invalid YAML/UTF-8はsource
pathと1-based line/columnを持つ`YAML001`–`YAML015`で失敗し、last-known-good
authoritative cacheとcompiler mirrorを保持します。Analyzerのdocument format
契約はJSON=`0`、YAML=`1`で、未知値だけが`OACG008`になります。

## Build and test

Production testsだけを実行します。

```sh
SourceGenerators/scripts/test.sh
```

決定的なRelease Analyzerをbuildし、assembly名、target framework、禁止されたassembly referenceを
検査します。このcommandはUPM内のtracked DLLを変更しません。

```sh
SourceGenerators/scripts/build.sh
```

検証済みbuildをembedded UPM packageへ同期する唯一のcommandです。Analyzer sourceを変更したときは
明示的に実行し、DLL差分をsource差分と一緒にreviewします。

```sh
SourceGenerators/scripts/sync-analyzer.sh
```

tracked DLLを変更せず、現在のRelease buildとbyte-for-byte一致することだけを確認できます。

```sh
SourceGenerators/scripts/verify-analyzer-sync.sh
```

CIと同じ.NET検証一式は次のcommandで実行します。Production tests、異なるcheckout pathでの再現build、
absolute path非混入、tracked DLL同期、UPM内のDLL数・RoslynAnalyzer metadata・禁止dependencyを検査します。

```sh
SourceGenerators/scripts/verify.sh
```

## Unity verification matrix

Unity 2021.3.19f1、6000.0.23f1、6000.3.2f1をUnity Hubの標準pathへinstallしたmacOS環境では、
base packageのminimum-version checkを含むlocal matrixを実行できます。

```sh
SourceGenerators/scripts/verify-unity-matrix.sh
```

特定versionだけを確認する場合は次のcommandを使用します。

```sh
SourceGenerators/scripts/verify-base-unity.sh 2021.3.19f1
SourceGenerators/scripts/verify-unity.sh 6000.3.2f1
```

Unity検証はtemporary projectだけを変更し、次を確認します。

- generated clientが存在しない初期projectのclean compile
- SettingsでSource Generatorを選択し、登録済みpublic providerへlocal raw JSONを渡す縦断生成
- authoritative cache、compiler mirror、属性付きpartial definitionの生成
- generated clientとraw JSON由来operation methodをreflectionで参照するtarget assemblyのcompileとEditMode tests
- 同じinputの再GenerateでspecIdと生成物が変わらず、compileを要求しないこと
- AnalyzerとAdditionalFileのasmdef reference scope
- unchanged reopenで不要なC# compileが発生しないこと
- operationIdを変えたraw JSONの再GenerateでもspecIdを維持し、生成memberを更新して対象assemblyだけをrecompileすること
- Docker Providerを解決不能にした状態でもSource Generator生成が完了すること
- `CS8785`が発生しないこと
- 稼働中Editorでadd-onを外す前にdefineが除去され、base-onlyへ再compileできること
- add-onを除いたprojectでもbase packageのEditMode testsが通ること

このmatrixでは.NET tests 79/79、deterministic analyzerのbyte-for-byte再現、packaged analyzer
sync SHA256 prefix `78b18d5f`、UPM dependency/reference checksを確認済みです。Unityではbase
packageの2021.3.19f1（33/33）と、Source Generator add-onの6000.0.23f1（157/157）および
6000.3.2f1（157/157）のvertical verification、regeneration/without-addon、`CS8785`なしを
確認済みです。未検証のUnity versionへこの結果を拡張してはなりません。対応機能の詳細は
[OpenAPI MVP support matrix](OpenApiMvpSupportMatrix.md)を参照してください。

## CI

`.github/workflows/source-generator-ci.yml`はpull request、および`main`/`develop`へのpushで
`SourceGenerators/scripts/verify.sh`を実行します。Unity Editor licenseを必要とするmatrixはCIへ含めず、
上記local commandでrelease前に実行します。
