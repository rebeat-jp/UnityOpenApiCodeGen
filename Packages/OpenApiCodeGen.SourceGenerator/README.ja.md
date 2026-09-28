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

生成クライアントの固定メンバー名と同じAPI名は`OACG005`で拒否します。予約名の一覧は対応表を参照してください。

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
参照先が完全なOpenAPI文書の場合、その文書の`openapi`版と`jsonSchemaDialect`も確認します。
入口文書とmajor.minor版が異なる参照先、および未対応dialectは位置付き診断で拒否します。
版のpatch差と、`openapi`を持たない外部bare schemaは許可します。
string enumは宣言したwire文字列と大小文字・空白も含め完全一致で送受信します。重複値、JSON数値、未宣言値は拒否します。
名前付きobjectのDTO生成には`additionalProperties: false`の明示が必要です。省略や追加プロパティの許可、
`pattern`・`minimum`・`minItems`などの未対応assertionは`OACG101`になります。
header parameterはscalarに対応しますが、`Content-Language`や`Content-Encoding`などの
content専用headerは位置付き診断で拒否します。対象名は[日本語対応表](Documentation~/SourceGenerators/OpenApiMvpSupportMatrix.ja.md)を参照してください。

## 生成されたDTOとHTTPクライアント

クライアントは注入された`HttpClient`を使用します。DTOはJson.NETでシリアライズします。
リクエストJSON本文は専用のserializerで生成し、ホストの`JsonConvert.DefaultSettings`による
`$type`／`$id`／`$ref`などの追加を受けません。日付変換と明示的なJSON `null`は維持します。
任意かつschema上nullableなDTOプロパティは、未代入ならJSONから省略され、`null`を代入すると
明示的なJSON `null`を送ります。生成された`<PropertyName>Specified`を`false`へ戻すと、
同じDTOを再利用するときもそのプロパティを省略できます。
任意でもschema上非nullableなDTOプロパティは、JSONからの省略を許可しますが、
明示的なJSON `null`を受け取ると`JsonSerializationException`になります。
必須かつschema上非nullableな参照型プロパティが未設定なら、入れ子のDTOを含めて
シリアライズ時に検出し、HTTP送信を止めます。必須nullable項目のJSON `null`と
必須値型の`0`や`false`は送信できます。

```csharp
var body = new UpdatePetRequest();
body.Nickname = null;             // "nickname": null を送る
body.NicknameSpecified = false;   // "nickname" を省略する
```

この例の型名とプロパティ名は説明用です。実際の名前は入力schemaから生成されます。
任意かつschema上nullableなリクエスト本文では、生成メソッドに省略可能な`bodySpecified`引数が
追加されます。`body: null`だけなら本文を省略し、`body: null, bodySpecified: true`なら
JSON `null`を送ります。本文に値があれば通常どおり送信します。本文の引数名が異なる場合は
`<本文の引数名>Specified`となり、名前が衝突すると番号が付きます。
必須かつschema上非nullableな参照型の本文に`null`を渡すと、送信前に
`ArgumentNullException`になります。必須でもschema上nullableならJSON `null`を送信できます。
server URLが`//host/path`形式の場合は`HttpClient.BaseAddress`のschemeで解決します。
`/path`形式は同じoriginのrootから解決し、`BaseAddress`のpathとqueryを引き継ぎません。
どちらも`BaseAddress`が未設定なら送信前に`InvalidOperationException`になります。
root `servers`を省略するか空配列にした場合は`/`が既定値となり、同様に`BaseAddress`が必要です。
絶対server URLはHTTP(S)のみ対応し、FTPなどのschemeは位置付き`OACG101`で拒否します。
path templateで対応しない`{`／`}`は、`/pets}`や`/pets/{id}}`を含めて位置付き`OACG100`です。
header parameterの名前はASCIIのHTTP field-name tokenに限り、不正な名前は位置付き`OACG100`です。
明示的な`baseUrl=""`の上書きは相対URLのままです。
相対server URLとoperation pathが`/`の場合、`BaseAddress`のqueryは1回だけ付加します。
リクエスト本文には具体的な`application/json`または`application/<subtype>+json`を送信します。
`application/*+json`のみを宣言すると`OACG101`になり、具体型も宣言すると具体型を選びます。
`application/vnd.*+json`など部分的なwildcard subtypeは、リクエスト・成功レスポンスとも
生成時に`OACG101`で拒否します。

schema付き成功レスポンスの空・空白本文は、nullableでも`JsonSerializationException`になります。
JSON `null`はnullableなschemaでのみ受け取れます。
レスポンス配列の非nullableな参照型要素がJSON `null`の場合も、入れ子の配列やDTO内の配列を含めて
`JsonSerializationException`になります。複数の成功レスポンス間でinline enum値の宣言順だけが
異なる場合は、同じ契約として扱います。
リクエスト配列の非nullableな参照型要素も、入れ子の配列やDTO内の配列を含めて送信前に検査します。
nullableな要素にはJSON `null`を使用できます。
リクエスト中の`float`／`double`は、配列やDTO内も含めて有限値である必要があります。
`NaN`と正負の無限大はHTTP送信前に`JsonSerializationException`になります。
path／query／headerの`float`／`double`パラメーターも有限値に限り、非有限値は
HTTP送信前に`ArgumentOutOfRangeException`になります。任意パラメーターの省略は維持します。
任意queryに`null`を渡すと省略し、明示的な空文字列は`name=`として送信します。
成功レスポンスにcontent宣言と`Content-Type`があれば、そのstatusで宣言されたmedia typeと照合してから
本文を読みます。不正・複数・宣言と不一致の値は`JsonSerializationException`です。
個別statusの宣言は`2XX`より優先します。content宣言のない成功応答では`Content-Type`を照合せず、
`Content-Type`がない場合も従来どおり本文を読みます。
成功レスポンスのJSONは、デシリアライズ前にRFC 8259の構文、重複・未知property、
scalarのJSON token型を検査します。`float`／`double`は有限値に限り、`1.0`や`1e0`のような
数学的整数は生成CLR型の範囲内で受け入れます。Json.NETの`$id`／`$ref`／`$values`参照メタデータは
配列を包むJSON objectも含めて互換拡張として維持します。`date`／`date-time`／`uuid`は受信時に
字句形式を検査します。字句が正しくてもCLRで表せない時刻やうるう秒は変換時に拒否される場合があります。
成功レスポンスDTOに`$id`、`$ref`、`$type`、`$values`をデータpropertyとして宣言すると、
その位置で`OACG100`を報告します。リクエスト専用DTOではこれらの名前を使用できます。
受信JSONの深さ上限は通常64階層です。ホストが`JsonConvert.DefaultSettings.MaxDepth`に正の値を設定した場合はその値を使い、
`null`または未指定なら64階層を維持します。
必須nullableパラメーターや、対応表の範囲外のwire formatは位置付き診断で拒否します。

成功した`Generate`では、`Library/OpenApiCodeGen/SourceGenerator/SpecCache/<specId>/`に
正規化Bundleとmanifest、`Assets/OpenApiCodeGen/Generated/SpecCache/`にコンパイラー用mirror、
選択した出力フォルダーに`<ApiName>.OpenApiDefinition.cs`を配置します。所有する定義のnamespace変更や
同一asmdef内でのフォルダー移動では、`specId`と既存`.meta`のGUIDを保持します。移動先の競合や
所有者が曖昧な場合は生成を止め、手動編集したファイルを保護します。

基本パッケージは有効な`NamedBuildTarget`の`OPENAPI_CODEGEN_SOURCE_GENERATOR`を同期します。
この追加パッケージの削除時には、現在有効でないものも含むすべての既知ターゲットから
Unityが削除を適用する前にdefineを取り除きます。更新時は有効なターゲットから一時的に取り除きます。

## 資料とライセンス

- [Bundle v2形式](Documentation~/SourceGenerators/NormalizedSpecBundleV2.md)
- [リリース・復旧手順](Documentation~/RELEASE.md)
- [MIT License](LICENSE.md)
