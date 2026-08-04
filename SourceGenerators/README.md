# Production Source Generator

このdirectoryには、`Rhycol.OpenApiCodeGen.SourceGenerator`のsource、tests、build toolsを置きます。
Analyzerは`netstandard2.0`、testsとbuild toolsは.NET SDK 10.0.301を使用します。

## Phase 4 OpenAPI generation

Source Generator providerはlocal JSONをNormalized Spec Bundleへ正規化し、OAS semantic parserと
internal `$ref` resolverでRoslyn非依存のgeneration modelを作成します。その後、Roslynでparseする
deterministic emitterがsourceを生成し、analyzerが`AddSource`します。対応範囲、診断、generated APIと
利用制約は[OpenAPI MVP support matrix](OpenApiMvpSupportMatrix.md)を参照してください。

wire formatへ影響する未対応のOpenAPI機能はsilentに無視せず、diagnosticとして生成を失敗させます。
生成されたpublic APIは再Generateされるcontractであり、手編集するsourceではありません。

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

このmatrixではbase packageのUnity 2021.3.19f1（33/33）と、Source Generator add-onのUnity
6000.0.23f1および6000.3.2f1のvertical verificationを確認済みです。対応機能の詳細は
[OpenAPI MVP support matrix](OpenApiMvpSupportMatrix.md)を参照してください。

## CI

`.github/workflows/source-generator-ci.yml`はpull request、および`main`/`develop`へのpushで
`SourceGenerators/scripts/verify.sh`を実行します。Unity Editor licenseを必要とするmatrixはCIへ含めず、
上記local commandでrelease前に実行します。
