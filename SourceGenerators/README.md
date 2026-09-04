# Production Source Generator

## 目的と対象読者

このdirectoryには、`Rhycol.OpenApiCodeGen.SourceGenerator` のsource、tests、
build toolsを置きます。Analyzerは`netstandard2.0`、testsとbuild toolsは
`.NET SDK 10.0.301`を使用します。Unity利用者向けの入力範囲と制約は
[OpenAPI MVP support matrix](OpenApiMvpSupportMatrix.md)を参照してください。

## Phase 6/7の実装境界

Source Generator providerはlocal `.json`、`.yaml`、`.yml`とHTTP(S) URLを
受け付けます。`Generate`が明示的なfetch/refresh操作であり、バックグラウンド
通信やDocker fallbackはありません。Editorだけがnetwork/filesystemを使用して
参照グラフを解決し、`1 specId = 1 Bundle v2 = 1 AdditionalFile`として公開します。
AnalyzerはAdditionalFileのBundleとreference edgeだけを読み、Bundle v1も後方互換の
ために読めます。Editorが書く形式はv2です。

URLはpublic/private/loopbackとcross-host redirectを許可しますが、userinfo、空白、
非HTTP(S)、remote-to-local、HTTPS-to-HTTP direct/redirect、explicit `file:`は拒否します。
local external fileはreal pathとsymbolic link解決後もUnity project内に限定します。
queryは現在のfetch identityに使用しますが、project settings、Bundle、manifest、
diagnosticへ平文保存しません。次回は完全なURLを再入力してください。

Remote formatはfinal URL extensionとresponse `Content-Type`から決定します。両方が既知
なら一致が必要で、片方だけ既知ならそれを使います。どちらも不明、または競合する場合
は失敗し、content sniffingやformat hintは使いません。1 request 30秒、graph 120秒、
1 document 4 MiB、graph 32 MiB、64 documents、redirect 5回、reference depth 256の
hard limitがあります。

bare Schema external documentは対応します。bare non-Schema、`$id`、`$anchor`、
`$dynamicAnchor`、`$dynamicRef`は未対応です。詳細なBundle schemaは
[Normalized Spec Bundle v2](NormalizedSpecBundleV2.md)を参照してください。

## Build and test

Production testsだけを実行します。

```sh
SourceGenerators/scripts/test.sh
```

Release Analyzerをbuildし、assembly名、target framework、禁止されたassembly referenceを
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

`0.5.0` release candidateのtarballとchecksumは次で生成します。registry publish、tag作成、release
公開は行いません。

```sh
SourceGenerators/scripts/pack-release.sh
```

出力先は`artifacts/upm/0.5.0/`で、base/add-onのtarball、`release-manifest.json`、
`SHA256SUMS`を含みます。manifestのUnity gateは手動検証が完了するまで`not-run`です。

## Unity verification matrix

Unity Editorとlicenseが必要なmatrixはGitHub Actionsへ入れず、release前の手動gateとして実行します。

```sh
SourceGenerators/scripts/verify-unity-matrix.sh
```

対象versionはUnity `2021.3.19f1`（base）、`6000.0.23f1`、`6000.3.2f1`
（Source Generator add-on）です。tarball install、JSON/YAML、URL、external reference、
再Generate、without-addon、`CS8785`不在、package transition時のdefine除去を確認し、
version別XML/logと`unity-gate.json`を保存します。

### Current verification status

実装・追補後に確認済みなのは次の範囲です。

- .NET tests: **101/101**
- analyzer reproducibility、sync、package inspection、tarball reproducibility、
  checksum verification: **pass**
- Unity `2021.3.19f1` base tarball/EditMode: **36/36**
- Unity `6000.0.23f1`: initial **185/185**、regeneration **185/185**、
  without-addon **36/36**。Source Generator full flow: **pass**
- Unity `6000.3.2f1`: initial **185/185**、regeneration **185/185**、
  without-addon **36/36**。Source Generator full flow: **pass**
- aggregate manual evidence `artifacts/upm/0.5.0/unity-gate.json`: **passed**

manual gate自体は完了しています。ただし、GitHub ActionsのUnity jobは計画どおり追加して
いないため、#54とPhase 7全体はopenのままで、release-readyとは扱いません。公開前には
cleanなcommit済みtreeで`verify.sh`、`pack-release.sh`、Unity matrixを再実行し、
`release-manifest.json`と`SHA256SUMS`のprovenanceを更新してください。

## CI

`.github/workflows/source-generator-ci.yml`はpull request、および`main`/`develop`へのpushで
`SourceGenerators/scripts/verify.sh`を実行します。Unity Editor licenseを必要とするmatrixはCIへ
含めず、上記manual commandでrelease前に実行します。このため、manual gateの結果がpassでも
GitHub Actions Unity jobを要求する#54は未完了です。
