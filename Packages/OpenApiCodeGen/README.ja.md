# Unity OpenAPI CodeGen 基本パッケージ

[English](README.md) · [Source Generator日本語対応表](Documentation~/SourceGenerators/OpenApiMvpSupportMatrix.ja.md)

このパッケージはOpenAPI CodeGenのEditor画面とprovider登録機構を提供します。Unity 2021.3以降で
Docker方式を使用できます。Unity 6で**Source Generator (Beta)**を使う場合も、基本パッケージが必要です。

## インストール

Source Generator方式では、Unityプロジェクトの`Packages/manifest.json`に両パッケージを追加します。

```json
{
  "dependencies": {
    "jp.rhycol.openapicodegen": "https://github.com/rebeat-jp/UnityOpenApiCodeGen.git?path=/Packages/OpenApiCodeGen#0.5.0",
    "jp.rhycol.openapicodegen.source-generator": "https://github.com/rebeat-jp/UnityOpenApiCodeGen.git?path=/Packages/OpenApiCodeGen.SourceGenerator#0.5.0"
  }
}
```

Docker方式には基本パッケージだけを使用できます。追加パッケージにはUnity 6000.0以降と
`com.unity.nuget.newtonsoft-json` `3.2.2`が必要です。`0.5.0`は公開予定のタグです。
公開前には、両URLのタグ部分を同じ検証済みコミットSHAへ置き換えてください。

## Editorでの操作

1. `Window/OpenAPI Code Generator/Settings`でproviderを選びます。
2. Docker方式を選んだ場合だけ、`Window/OpenAPI Code Generator/Setup`でDockerを設定します。
3. `Window/OpenAPI Code Generator/Generator`に仕様書のパスまたはURLと、`Assets`配下の
   出力フォルダーを入力し、`Generate`を押します。

URL入力のSource Generatorは、`Generate`の操作時に取得・更新します。providerが利用できない場合や
生成が失敗した場合にDockerへ自動切替はしません。進捗、警告、キャンセルはGenerator画面に表示します。

Source Generatorの入力と生成結果の詳細は[追加パッケージの日本語README](../OpenApiCodeGen.SourceGenerator/README.ja.md)と
[同梱した対応表](Documentation~/SourceGenerators/OpenApiMvpSupportMatrix.ja.md)を参照してください。
Source Generatorが利用可能なときは有効な`NamedBuildTarget`へ
`OPENAPI_CODEGEN_SOURCE_GENERATOR`を同期し、追加パッケージを削除するとdefineも取り除きます。
生成型を参照するコードは、追加パッケージを外した後にコンパイルエラーになることがあります。

## ライセンス

[MIT License](LICENSE.md)で公開しています。
