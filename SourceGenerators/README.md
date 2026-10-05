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
`SHA256SUMS`を含みます。manifestのUnity gateは検証完了まで`not-run`です。
Nodeはルートの`.node-version`、npmは`11.19.0`を使用します。clean treeが必要で、
開発中だけ`--allow-dirty`を指定できます。dirty候補のcommitは`null`になり、公開には使えません。

## Unity verification matrix

GitHub Actionsは、cleanな同一commitから作成した同一tarballを、GitHub-hosted Linuxと
固定digestのGameCIイメージで検証します。Unity jobはEnvironment `UNITY_LICENSE`の
`UNITY_LICENSE`・`UNITY_EMAIL`・`UNITY_PASSWORD`を使用し、workflow間も逐次実行します。
fork PRへはSecretsを渡さず、.NET/package検証のみ実行します。

ローカルMacのEditorとlicenseでも同じmatrixを実行できます。

```sh
SourceGenerators/scripts/verify-unity-matrix.sh
```

対象versionはUnity `2021.3.19f1`（base）、`6000.0.23f1`、`6000.3.2f1`
（Source Generator add-on）です。tarball install、JSON/YAML、URL、external reference、
再Generate、without-addon、`CS8785`不在、package transition時のdefine除去を確認し、
version別XML/logと`unity-gate.json`を保存します。

### Evidence

CIの`verified-release-<run-id>-<attempt>` artifactに、tarball、manifest、SHA256SUMS、
各Unityのlog/XMLと集約gateを保存します。schema 2のvalidatorはcommit、run/attempt、
candidate fingerprint、全証拠hash、test XMLと終了コードを照合します。
欠落・改変・失敗・未実行のgateでは公開へ進めません。過去の件数ではなく、対象commitの
成功runとartifactを確認してください。

## CI

`.github/workflows/source-generator-ci.yml`はpull request、`main`/`develop`へのpush、
手動dispatchで共通の`source-generator-verify.yml`を呼びます。共通処理は.NET/package検証、
Unity matrix、証拠集約の後に、実際の公開スクリプトを`PUBLISH=false`で実行します。
これによりdefault branchへworkflowが入る前のPRでもCD dry-runを確認できます。
手動CIは`main` refから、`main`の祖先SHAだけを検証します。未マージ変更は通常のPR CIで検証します。

DLL更新は`source-generator-update-dll.yml`を`main`から手動実行し、
`base_branch=develop`または`main`を選びます。変更がある場合のみDLL専用PRと明示CIを作成し、
version・`.meta`・GUIDは維持します。

公開は`source-generator-release.yml`の`commit`・`version`・`publish`を指定します。
既定はdry-runです。dry-runと公開の両方で`main` refとその祖先commitだけを許可し、
タグ・既存assetを上書きしません。
OpenUPM確認はOIDCのためタグrefの別workflowへ渡します。
[リリース・再開・rollback手順](../RELEASE.md)と
[初回OpenUPM登録](OpenUPM/README.md)を参照してください。
