# Production Source Generator

## 目的と対象読者

このdirectoryには、`Rhycol.OpenApiCodeGen.SourceGenerator` のsource、tests、
build toolsを置きます。Analyzerは`netstandard2.0`、testsとbuild toolsは
`.NET SDK 10.0.301`を使用します。Unity利用者向けの入力範囲と制約は
[OpenAPI support matrix](OpenApiMvpSupportMatrix.md)を参照してください。

## 実装境界

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

入力documentはSwagger 2.0（`swagger: "2.0"`）とOpenAPI 3.0／3.1／3.2です。Swagger 2.0は
AnalyzerがOpenAPI 3.0相当のtreeへ正規化してから同じpipelineで処理し、診断は元の
Swagger 2.0の位置を指します。

bare Schema external documentと、`openapi`／`swagger`を持たないbare Path Item file
（HTTP method、または`$ref`・`parameters`・`servers`・`summary`・`description`だけを持ち、
schemaキーワードを持たないもの）は対応します。それ以外のbare non-Schema、`$id`、`$anchor`、
`$dynamicAnchor`、`$dynamicRef`は未対応です。

Editorが`$ref`を収集する場所は、Analyzerが解析する場所と一致させています。
webhooks、callbacks、links、`components.examples`、Media Typeの`itemSchema`はAnalyzerが解析せず
`OACG108`で通知する（またはドキュメント専用の）ため、その内側の`$ref`は収集せず、解決できない
参照があってもbundleは失敗しません。OpenAPI 3.2の`query`、`additionalOperations`、
`components.mediaTypes`と、Swagger 2.0の`definitions`・`parameters`・`responses`は収集します。
Bundleの形式は変わりません。詳細なBundle schemaは
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

`0.6.0-preview.2` release candidateのtarballとchecksumは次で生成します。registry publish、tag作成、release
公開は行いません。

```sh
SourceGenerators/scripts/pack-release.sh
```

出力先は`artifacts/upm/0.6.0-preview.2/`で、base/add-onのtarball、`release-manifest.json`、
`SHA256SUMS`を含みます。manifestのUnity gateは検証完了まで`not-run`です。
Nodeはルートの`.node-version`、npmは`11.19.0`を使用します。clean treeが必要で、
開発中だけ`--allow-dirty`を指定できます。dirty候補のcommitは`null`になり、公開には使えません。

## Unity verification matrix

GitHub Actionsは、cleanな同一commitから作成した同一tarballを、GitHub-hosted Linuxと
固定digestのGameCIイメージで検証します。Unity jobはEnvironment `UNITY_LICENSE`の
`UNITY_LICENSE`・`UNITY_EMAIL`・`UNITY_PASSWORD`を使用し、workflow間も逐次実行します。
Unity jobはEnvironmentの承認待ちがあるため、PRでは`unity-gate`ラベルを付けた同一repositoryのPRと
手動dispatch、release workflowでだけ実行します（`main`/`develop`へのpushでは実行しません）。
fork PRへはSecretsを渡さず、ラベルの有無によらず.NET/package検証のみ実行します。

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

## リリース前のローカル確認

修正の確認にreleaseとUnity CIの承認を待たなくて済むよう、速い順に3段階あります。

### 段階1: specをUnityなしで検査する

実物のOpenAPI document（YAML/JSON）をEditorのloader、source generator、コンパイラへ
通し、全診断を一覧します。Unityは不要で、数秒で終わります。

```sh
SourceGenerators/scripts/check-spec.sh /path/to/openapi.yaml
SourceGenerators/scripts/check-spec.sh /path/to/spec.yaml --output /tmp/generated-client
```

- 出力にYAMLの診断（`YAML002`など。行:列付き）、generatorの診断（`OACG101`など。
  severity・id・位置・message）、コンパイルエラー、件数のsummaryが表示されます。
  errorが1件でもあれば失敗します。
- `--output <dir>`を指定したときだけ、生成コード（`Generated.cs`）をそのdirectoryへ書きます。
  repositoryの作業ツリー内は指定できません。
- specはコピーしません。specのあるdirectoryをproject rootとして読むため、相対`$ref`は
  そのdirectory内のファイルだけ解決できます（外へ出る参照はloaderが拒否します）。
  APIクラス名・namespaceは既定値、serializerはNewtonsoft.Jsonです。
- 機密のspecをこのrepositoryへ置かないでください。scriptは実行前後で`git status --porcelain`を
  比較し、変化があれば失敗します。
- 実体は`ExternalSpecCheckTests`で、環境変数`OPENAPI_CODEGEN_CHECK_SPEC`が未設定なら何もせず
  成功するため、通常の`test.sh`やCIには影響しません。

### 段階2: 利用側のUnity projectでこのworktreeを直接使う

利用側のUnity projectの`Packages/manifest.json`で、baseとadd-onの**両方**を、このrepositoryの
worktreeへの`file:`参照へ一時的に変えます。

```json
{
  "dependencies": {
    "jp.rhycol.openapicodegen": "file:/absolute/path/to/UnityOpenApiCodeGen/Packages/OpenApiCodeGen",
    "jp.rhycol.openapicodegen.source-generator": "file:/absolute/path/to/UnityOpenApiCodeGen/Packages/OpenApiCodeGen.SourceGenerator"
  }
}
```

- Editor側のC#（`Packages/OpenApiCodeGen*/Editor`など）の変更は、Unityが再コンパイルすれば即反映されます。
- `SourceGenerators/Rhycol.OpenApiCodeGen.SourceGenerator/**`のanalyzerを変更した場合は、
  `SourceGenerators/scripts/sync-analyzer.sh`でUPM内のDLLを更新してからUnityへ戻ります。
- `manifest.json`（と`packages-lock.json`）のこの変更は利用側でcommitしません。
- このrepositoryのcheckoutを切り替えると利用側へ波及するので、確認用に専用のworktree
  （`git worktree add ../UnityOpenApiCodeGen-check <branch>`）を作ってそこを参照してください。

### 段階3: Git URLとcommit SHAで取り込む

branchをpushした後は、READMEの「Add both entries」の例と同じ形で、`#`の後ろをreview済みの
commit SHAにしたGit URLを両パッケージへ指定します。release前の候補の検証手順と、Unity CIの
実行条件（`unity-gate`ラベル）は[RELEASE.md](../RELEASE.md)を参照してください。

## CI

`.github/workflows/source-generator-ci.yml`はpull request（opened/synchronize/reopened/labeled）、
`main`/`develop`へのpush、手動dispatchで共通の`source-generator-verify.yml`を呼びます。
Unity jobの実行条件は[RELEASE.md](../RELEASE.md)の「When the Unity job runs」を参照してください。共通処理は.NET/package検証、
Unity matrix、証拠集約の後に、実際の公開スクリプトを`PUBLISH=false`で実行します。
Unity matrixと証拠集約はUnity jobが有効なとき（`unity-gate`ラベル付きの同一repositoryのPR、
または手動dispatch）だけ動くため、この`PUBLISH=false`の確認もそのときに限られます。
default branchへworkflowが入る前のPRでも、ラベルを付ければCD dry-runを確認できます。
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
