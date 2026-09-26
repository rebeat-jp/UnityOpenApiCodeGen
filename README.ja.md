# Unity OpenAPI CodeGen

[English](README.md) · [Source Generatorの対応表](SourceGenerators/OpenApiMvpSupportMatrix.ja.md)

Unity OpenAPI CodeGenは、OpenAPI文書から型付きのC# RESTクライアントを生成します。
このリポジトリには、Editor画面とDocker方式を提供する基本パッケージと、Unity 6向けの
Source Generator追加パッケージがあります。**Source Generatorによる生成はベータ版**です。

## 動作要件

| パッケージ | 最低Unity版 | 追加要件 |
| --- | --- | --- |
| `jp.rhycol.openapicodegen` | 2021.3 | Docker方式を選ぶ場合のみDocker |
| `jp.rhycol.openapicodegen.source-generator` | 6000.0 | 基本パッケージと`com.unity.nuget.newtonsoft-json` `3.2.2` |

## インストール

Source Generatorを使う場合は、Unityプロジェクトの`Packages/manifest.json`に両方を追加します。

```json
{
  "dependencies": {
    "jp.rhycol.openapicodegen": "https://github.com/rebeat-jp/UnityOpenApiCodeGen.git?path=/Packages/OpenApiCodeGen#0.5.0",
    "jp.rhycol.openapicodegen.source-generator": "https://github.com/rebeat-jp/UnityOpenApiCodeGen.git?path=/Packages/OpenApiCodeGen.SourceGenerator#0.5.0"
  }
}
```

`0.5.0`は公開予定のタグです。公開前に試す場合は、両URLの`0.5.0`を同じ検証済みコミットSHAへ
置き換えてください。基本パッケージだけを導入してDocker方式を使うこともできます。

## 生成手順

1. `Window/OpenAPI Code Generator/Settings`で`Docker`または`Source Generator (Beta)`を選びます。
2. Docker方式では、`Window/OpenAPI Code Generator/Setup`でDocker実行ファイルを設定します。
3. `Window/OpenAPI Code Generator/Generator`でOpenAPI文書のパスまたはHTTP(S) URLと、
   `Assets`配下の出力フォルダーを指定して`Generate`を押します。URLの取得・更新はこの操作時だけです。
4. Source Generatorの生成先asmdefから`Unity.OpenApiCodeGen.SourceGenerator`を直接参照します。

Source Generatorはローカルの`.json`、`.yaml`、`.yml`とHTTP(S) URLを受け付けます。
外部`$ref`はEditorが解決し、1つの仕様を1つのBundle v2とAdditionalFileとして公開します。
Analyzer自身はネットワークとファイルシステムへアクセスしません。対応するOpenAPI要素、YAMLの範囲、
診断と上限は[日本語対応表](SourceGenerators/OpenApiMvpSupportMatrix.ja.md)を確認してください。

ローカル外部参照はシンボリックリンク解決後もUnityプロジェクト内に限られます。URLでは公開・非公開・
loopbackホストを利用できますが、userinfo、HTTP(S)以外の方式、HTTPSからHTTPへの参照・
リダイレクト、リモート文書からローカルファイルへの参照は拒否されます。query付きURLは次回の
`Generate`時に再入力してください。queryは設定・Bundle・診断へ平文で保存されません。

生成に失敗しても最後に成功したキャッシュとコンパイラー入力は保持されます。内容が変わらなければ
スクリプトの再コンパイルは要求しません。キャンセルは公開処理の開始前まで受け付けます。

## 資料と検証

- [基本パッケージの日本語README](Packages/OpenApiCodeGen/README.ja.md)
- [Source Generatorの日本語README](Packages/OpenApiCodeGen.SourceGenerator/README.ja.md)
- [OpenAPI MVP日本語対応表](SourceGenerators/OpenApiMvpSupportMatrix.ja.md)
- [Bundle v2形式](SourceGenerators/NormalizedSpecBundleV2.md)
- [リリース・復旧手順](RELEASE.md)

CIは.NET、同梱Analyzer、パッケージ内容と、Unity `2021.3.19f1`、`6000.0.23f1`、
`6000.3.2f1`の検証を行います。同じ候補から作った検証結果がそろった場合にCDのdry-runを実行します。
正式公開とOpenUPM登録は別作業です。現在の結果は[PR #56](https://github.com/rebeat-jp/UnityOpenApiCodeGen/pull/56)で確認できます。

## ライセンス

[MIT License](LICENSE)で公開しています。
