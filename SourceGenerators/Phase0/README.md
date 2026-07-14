# Phase 0 retained spike

Incremental Source Generator 導入ロードマップの Phase 0（GitHub #20〜#22）を、再実行可能な検証資産として保持するディレクトリです。本番の OpenAPI generator ではありません。

## 結果

2026-07-14 時点の結果です。

| 検証 | Unity 6000.0.23f1 | Unity 6000.3.2f1 | 判定 |
|---|---|---|---|
| 固定コード生成と生成型参照 | EditMode 成功 | EditMode 成功 | 成立 |
| AdditionalFile と asmdef scope | EditMode 成功 | EditMode 成功 | 成立 |
| UPM 依存の外部 Json.NET を analyzer から直接利用 | `CS8785` / `FileNotFoundException` | `CS8785` / `FileNotFoundException` | 不成立 |

6000.0.23f1 の検証は、現行プロジェクトの 6000.3 専用 package 群を除いた一時コピーと `com.unity.test-framework@1.4.5` で実行します。元の project、manifest、既存 Docker 経路は変更しません。

## 構成

- `Rhycol.OpenApiCodeGen.SourceGenerator.Phase0`: Newtonsoft 非依存の retained analyzer
- `Rhycol.OpenApiCodeGen.SourceGenerator.Phase0.Tests`: Roslyn driver による suffix filter と incremental step のテスト
- `Rhycol.OpenApiCodeGen.SourceGenerator.Phase0.JsonNetGate`: Json.NET 13.0.2 を compile 時だけ参照する失敗再現用 analyzer
- `JsonNetGateUnityFixture`: 一時 Unity project にだけコピーする gate fixture
- `scripts`: build、test、Unity matrix、negative gate の再実行スクリプト
- `../../Packages/OpenApiCodeGen.SourceGenerator.Phase0`: Unity embedded package
- `../../Assets/SourceGeneratorPhase0`: root / scoped / near-miss AdditionalFile fixture

## 実行方法

Repository root から実行します。
必要な環境は .NET SDK 10.0.301 以降の 10.0 patch、`rsync`、`xmllint`、および検証対象の Unity Editor です。使用 SDK は `global.json` で固定しています。

```sh
SourceGenerators/Phase0/scripts/test.sh
SourceGenerators/Phase0/scripts/verify-reproducible-build.sh
SourceGenerators/Phase0/scripts/verify-unity.sh 6000.3.2f1
SourceGenerators/Phase0/scripts/verify-unity.sh 6000.0.23f1
SourceGenerators/Phase0/scripts/verify-jsonnet-gate.sh 6000.3.2f1
SourceGenerators/Phase0/scripts/verify-jsonnet-gate.sh 6000.0.23f1
```

`test.sh` は Roslyn テスト後、Release build した baseline DLL を embedded package の `Runtime/Analyzers` へコピーし、別 checkout path の再buildとbyte-for-byteで一致することも確認します。Release analyzer は local pathやPDB pathを含めません。Json.NET gate は `artifacts/` にだけ出力され、通常の Unity project には import されません。

## #20: Unity analyzer contract

- Analyzer は `netstandard2.0`、`Microsoft.CodeAnalysis.CSharp` 4.3.1 で build する
- DLL の file name と assembly name を `Rhycol.OpenApiCodeGen.SourceGenerator.Phase0.dll` で一致させる
- DLL の `.meta` に大文字小文字を含めて正確な `RoslynAnalyzer` label を設定する
- PluginImporter の Any、Editor、WSA platform をすべて無効にする
- Analyzer を置いた asmdef と、その asmdef を参照する assembly が生成結果を受け取る
- 無関係な asmdef には analyzer を適用しない
- 検証用 test asmdef も `autoReferenced: false` とし、`Assembly-CSharp-Editor` へ analyzer を伝播させない

固定出力 `Phase0Fixed.Marker` と AdditionalFile 出力 `Phase0Additional.Root` / `Scoped` は通常コードから参照され、EditMode test で値を確認します。

## #21: AdditionalFile contract

Analyzer DLL basename に対応する exact suffix は次のとおりです。

```text
.<Analyzer DLL basename>.additionalfile
.Rhycol.OpenApiCodeGen.SourceGenerator.Phase0.additionalfile
```

Unity の命名規則は `<prefix>.<Analyzer DLL basename>.additionalfile` です。suffix は case-sensitive で、`<prefix>` に `.` は使用できません。`AdditionalTextsProvider` には他 analyzer 用や Unity 内部の入力も現れ得るため、generator 側でも `StringComparison.Ordinal` で exact suffix を filter します。

AdditionalFile の置き場所では assembly scope を限定できません。Assets 直下・対象 asmdef 配下・無関係な asmdef 配下の matching file は analyzer 対象 assembly に渡るため、scope は analyzer の配置と asmdef の参照関係で制御します。

Roslyn tracked-step test は次を固定します。

- 初回の matching input は `New`
- 同一 driver / 同一 input の再実行は `Cached`
- 1件だけ変更すると変更入力は `Modified`、未変更入力は `Cached`
- collect step は同一入力で `Cached`、入力変更時だけ `Modified`

Unity 6000.3.2f1 では、入力未変更の再オープン時に C# compiler action が発生しないこと、matching file を1件変更した場合に owner assembly と参照側 test assembly だけが再コンパイルされ、unrelated asmdef と `Assembly-CSharp-Editor` は再コンパイルされないことも確認しています。

## #22: Json.NET gate

Embedded package は再現条件として `com.unity.nuget.newtonsoft-json@3.2.2`（Json.NET package 13.0.2、assembly version 13.0.0.0）へ依存します。一方、通常利用する baseline analyzer 自体には Newtonsoft 参照がありません。

Gate analyzer は Json.NET を compile asset としてだけ参照し、`Newtonsoft.Json.dll` を build output や UPM package へコピーしません。Unity compiler の target reference に UPM の DLL が存在しても、別 load context で動く analyzer の依存解決には使われず、両 Unity version で次の失敗を再現します。
検証スクリプトは、`com.unity.nuget.newtonsoft-json@3.2.2`の解決、target compiler response内のNewtonsoft参照、gate DLLのanalyzer指定を確認したうえで、対象generatorに結び付いた警告を判定します。

```text
warning CS8785: Generator 'JsonNetGateIncrementalGenerator' failed to generate source.
System.IO.FileNotFoundException:
Could not load file or assembly 'Newtonsoft.Json, Version=13.0.0.0, ...'
```

このため本実装では analyzer から raw JSON を直接解析しません。Editor 側で Json.NET を使用して versioned・deterministic な `SpecNode` 中間表現へ正規化し、Newtonsoft 非依存の ISG へ AdditionalFile として渡す代替設計が必要です。Docker 方式への暗黙 fallback は行いません。

## Spike の制限

生成member名は Phase 0 fixture の `Root` / `Scoped` を検証するための最小実装です。C# keywordや正規化後に衝突するprefixへdiagnosticを出す本番仕様は含みません。OpenAPI解析、複数specの対応付け、Editor cache、Provider切替も対象外です。

## 参考

- [Unity: Create a source generator](https://docs.unity3d.com/6000.0/Documentation/Manual/create-source-generator.html)
- [Unity: Roslyn analyzer additional files](https://docs.unity3d.com/6000.0/Documentation/Manual/roslyn-analyzers-additional-files.html)
- [Unity: Analyzer scope and diagnostics](https://docs.unity3d.com/6000.0/Documentation/Manual/analyzer-scope-and-diagnostics.html)
