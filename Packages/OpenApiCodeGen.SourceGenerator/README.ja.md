# OpenApiCodeGen Source Generator (Beta)

[English](README.md) · [OpenAPI MVP日本語対応表](Documentation~/SourceGenerators/OpenApiMvpSupportMatrix.ja.md)

Unity 6のRoslyn Source Generatorで型付きC# RESTクライアントを生成する追加パッケージです。
**生成機能はベータ版**です。基本パッケージ`jp.rhycol.openapicodegen`が必要です。

## 導入と生成

Unity 6000.0以降のプロジェクトの`Packages/manifest.json`に両パッケージを追加します。

```json
{
  "dependencies": {
    "jp.rhycol.openapicodegen": "https://github.com/rebeat-jp/UnityOpenApiCodeGen.git?path=/Packages/OpenApiCodeGen#0.5.0",
    "jp.rhycol.openapicodegen.source-generator": "https://github.com/rebeat-jp/UnityOpenApiCodeGen.git?path=/Packages/OpenApiCodeGen.SourceGenerator#0.5.0"
  }
}
```

追加パッケージは`com.unity.nuget.newtonsoft-json` `3.2.2`を使用します。`0.5.0`は公開予定のタグです。
公開前には両URLのタグ部分を同じ検証済みコミットSHAへ置き換えてください。

1. `Window/OpenAPI Code Generator/Settings`で`Source Generator (Beta)`を選び、API名とnamespaceを設定します。
2. `Window/OpenAPI Code Generator/Generator`でローカルの`.json`、`.yaml`、`.yml`、
   またはHTTP(S) URLと、`Assets`配下の出力フォルダーを指定して`Generate`を押します。
3. 生成先asmdefから`Unity.OpenApiCodeGen.SourceGenerator`を直接参照します。

`Generate`はURLの明示的な取得・更新操作です。バックグラウンド取得やDockerへの自動切替はありません。
失敗時は最後に成功したキャッシュとコンパイラー入力を保持します。内容が同一なら再コンパイルを要求しません。

## 入力と外部参照

EditorがJSON/YAMLと外部`$ref`の参照グラフを解決し、1つの`specId`につき1つのBundle v2
AdditionalFileを公開します。AnalyzerはそのBundleを読み、ネットワーク通信やファイル取得は行いません。

ローカル外部ファイルは、実パスとシンボリックリンクを解決した後もUnityプロジェクト内に限られます。
URLでは公開・非公開・loopbackホストと別ホストへのリダイレクトを利用できます。空白、userinfo、
HTTP(S)以外の方式、HTTPSからHTTPへの参照・リダイレクト、明示的な`file:`、リモート文書から
ローカルファイルへの参照は拒否されます。URLのqueryは現在の取得で識別に使いますが、設定、Bundle、
manifest、診断へ平文保存しません。次回の`Generate`時にはqueryを含むURLを再入力してください。

リモート文書の形式は最終URLの拡張子と`Content-Type`で判定します。既知の値が両方にある場合は
一致が必要です。JSONは`application/json`と`+json`、YAMLは`application/yaml`、
`application/x-yaml`、`text/yaml`、`text/x-yaml`、`text/x-yml`に対応します。
内容からの形式推測は行いません。1リクエスト30秒、グラフ全体120秒、1文書4 MiB、
グラフ全体32 MiB、最大64文書、リダイレクト5回、参照深度256の上限があります。

対応するOpenAPI要素、YAMLの範囲、診断は[日本語対応表](Documentation~/SourceGenerators/OpenApiMvpSupportMatrix.ja.md)に記載しています。
OpenAPI 3.1は標準schema dialectのみを受け付けます。独自dialectや`null`入りstring enumは診断で拒否します。

## 生成されたDTOとHTTPクライアント

クライアントは注入された`HttpClient`を使用します。DTOはJson.NETでシリアライズします。
任意かつschema上nullableなDTOプロパティは、未代入ならJSONから省略され、`null`を代入すると
明示的なJSON `null`を送ります。生成された`<PropertyName>Specified`を`false`へ戻すと、
同じDTOを再利用するときもそのプロパティを省略できます。

```csharp
var body = new UpdatePetRequest();
body.Nickname = null;             // "nickname": null を送る
body.NicknameSpecified = false;   // "nickname" を省略する
```

この例の型名とプロパティ名は説明用です。実際の名前は入力schemaから生成されます。
必須nullableパラメーターや、対応表の範囲外のwire formatは位置付き診断で拒否します。

成功した`Generate`では、`Library/OpenApiCodeGen/SourceGenerator/SpecCache/<specId>/`に
正規化Bundleとmanifest、`Assets/OpenApiCodeGen/Generated/SpecCache/`にコンパイラー用mirror、
選択した出力フォルダーに`<ApiName>.OpenApiDefinition.cs`を配置します。所有する定義のnamespace変更や
同一asmdef内でのフォルダー移動では、`specId`と既存`.meta`のGUIDを保持します。移動先の競合や
所有者が曖昧な場合は生成を止め、手動編集したファイルを保護します。

## 資料とライセンス

- [Bundle v2形式](Documentation~/SourceGenerators/NormalizedSpecBundleV2.md)
- [リリース・復旧手順](Documentation~/RELEASE.md)
- [MIT License](LICENSE.md)
