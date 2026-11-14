# OpenApiCodeGen Source Generator (Beta)

[English](README.md) · [OpenAPI MVP日本語対応表](Documentation~/SourceGenerators/OpenApiMvpSupportMatrix.ja.md)

Unity 6のRoslyn Source Generatorで型付きC# RESTクライアントを生成する追加パッケージです。
**生成機能はベータ版**です。基本パッケージ`jp.rhycol.openapicodegen`が必要です。

## 導入と生成

Unity 6000.0以降のプロジェクトの`Packages/manifest.json`に両パッケージを追加します。

```json
{
  "dependencies": {
    "jp.rhycol.openapicodegen": "https://github.com/rebeat-jp/UnityOpenApiCodeGen.git?path=/Packages/OpenApiCodeGen#0.5.1",
    "jp.rhycol.openapicodegen.source-generator": "https://github.com/rebeat-jp/UnityOpenApiCodeGen.git?path=/Packages/OpenApiCodeGen.SourceGenerator#0.5.1"
  }
}
```

追加パッケージは`com.unity.nuget.newtonsoft-json` `3.2.2`を使用します。例は`0.5.1`のリリースタグを使います。
未リリースのコミットを試す場合は、両URLのタグ部分を同じ検証済みコミットSHAへ置き換えてください。

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

### GitHubのprivateリポジトリ

privateリポジトリの文書は`https://raw.githubusercontent.com/<owner>/<repo>/<ref>/<path>`のURLを指定し、
`Window/OpenAPI Code Generator/Settings`のUser Settingsで**GitHub Token**を設定します。
トークンは送信先のoriginが`https://raw.githubusercontent.com`と完全に一致するリクエストにだけ
`Authorization: Bearer`で送ります。リダイレクトのたびに判定し直すため、別ホストへのリダイレクト先には送りません。
同じリポジトリ内の相対外部参照もトークン付きで取得します。

トークンはUnityプロジェクト外のユーザー設定ファイルに平文で保存し、プロジェクト設定、Bundle v2、
manifest、診断には書き込みません。対象リポジトリに限定した、Contents読み取り専用のfine-grained
personal access tokenを使ってください。`github.com`のページ、REST contents API、GitHub Enterprise Serverの
ホストには対応しません。許可されたGitHub URLが401、403、404を返した場合は、トークンが未設定か
拒否されたかをエラーで示します。

リモート文書の形式は最終URLの拡張子と`Content-Type`で判定します。既知の値が両方にある場合は
一致が必要です。JSONは`application/json`と`+json`、YAMLは`application/yaml`、
`application/x-yaml`、`text/yaml`、`text/x-yaml`、`text/x-yml`に対応します。
内容からの形式推測は行いません。1リクエスト30秒、グラフ全体120秒、1文書4 MiB、
グラフ全体32 MiB、最大64文書、リダイレクト5回、参照深度256の上限があります。

対応するOpenAPI要素、YAMLの範囲、診断は[日本語対応表](Documentation~/SourceGenerators/OpenApiMvpSupportMatrix.ja.md)に記載しています。
YAMLの二重引用符内では有効な`\u`サロゲート対を非BMP文字として保持し、
孤立・不一致のサロゲートは位置付き`YAML006`で拒否します。
OpenAPI 3.1は標準schema dialectのみを受け付けます。独自dialectは診断で拒否します。
参照先が完全なOpenAPI文書の場合、その文書の`openapi`版と`jsonSchemaDialect`も確認します。
入口文書とmajor.minor版が異なる参照先、および未対応dialectは位置付き診断で拒否します。
版のpatch差と、`openapi`を持たない外部bare schemaは許可します。
string enumは宣言したwire文字列と大小文字・空白も含め完全一致で送受信します。重複値、JSON数値、未宣言値は拒否します。
path／query／headerのenum値も専用のJson.NET serializerで変換し、ホストの
`JsonConvert.DefaultSettings`のconverterやcontract resolverによる書き換えを防ぎます。
`additionalProperties`を省略した、または`true`やschemaを指定したobjectは、宣言外のメンバーを
`[JsonExtensionData]`の`IDictionary<string, JToken>`（`AdditionalProperties`）に保持するopen DTOになります。
未知のresponse propertyを拒否するclosed DTOにするには`additionalProperties: false`を指定します。
検証キーワード（`minLength`・`maxLength`・`pattern`・`minimum`・`maximum`・`exclusiveMinimum`・
`exclusiveMaximum`・`multipleOf`・`minItems`・`maxItems`・`uniqueItems`・`minProperties`・
`maxProperties`と、OpenAPI 3.1の`patternProperties`・`propertyNames`・`contains`・`minContains`・
`maxContains`・`prefixItems`・`dependentRequired`）は実行時に検証し、生成する型は変えません。
違反するrequestは送信前に`ArgumentException`で、違反するresponse・response headerは
`JsonSerializationException`で拒否します。長さはUnicodeコードポイントで数え、`pattern`は
アンカーなしの.NET `Regex`です（.NET構文が必要なpatternを除きECMAScriptモードで、必要な場合は`OACG108`。対応表を参照）。
`if`／`then`／`else`・`dependentSchemas`・`unevaluated*`は引き続き`OACG101`です。
`allOf`は1つのDTOへ平坦化し、`oneOf`／`anyOf`はラッパークラス（`FromX`／`TryGetX`／`AsX`）になります。
`discriminator`があればその値で、なければ成功応答のJSONの構造検査で候補を決めます。`[X, null]`はnullableな`X`です。`oneOf`で複数の候補が一致したときは、応答のすべてのpropertyを宣言している候補を選び、区別できない候補は失敗します。
`not`は成功応答の検査だけで評価します。詳細は対応表を参照してください。
path・query・header・cookieのparameterに対応しますが、`Content-Language`や`Content-Encoding`などの
content専用headerは位置付き診断で拒否します。対象名は[日本語対応表](Documentation~/SourceGenerators/OpenApiMvpSupportMatrix.ja.md)を参照してください。

## 生成されたDTOとHTTPクライアント

クライアントは注入された`HttpClient`を使用します。DTOはJson.NETでシリアライズします。
リクエストJSON本文は専用のserializerで生成し、ホストの`JsonConvert.DefaultSettings`による
`$type`／`$id`／`$ref`などの追加を受けません。日付変換と明示的なJSON `null`は維持します。
任意かつschema上nullableなDTOプロパティは、未代入ならJSONから省略され、`null`を代入すると
明示的なJSON `null`を送ります。生成された`<PropertyName>Specified`を`false`へ戻すと、
同じDTOを再利用するときもそのプロパティを省略できます。
schemaの項目名が別項目の`Specified`フラグとCLR名で衝突する場合は、JSON上の名前を保ち、
生成するCLR名に番号を付けます。
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
不正な絶対URL風・相対URL参照は位置付き`OACG100`、`variables`に宣言のない`{...}`は`OACG101`です。server URLのfragmentは操作pathとの結合前に除き、
エンコード済みの`%23`はpath内に保持します。
Response Objectの`contents`など未知項目も位置付き`OACG101`です。
Parameter ObjectもOpenAPI 3.0／3.1の標準項目と`x-*`拡張を認識し、`requird`などの未知項目は
path直下・components・外部参照先を含めて位置付き`OACG101`です。
path templateで対応しない`{`／`}`は、`/pets}`や`/pets/{id}}`を含めて位置付き`OACG100`です。
header parameterの名前はASCIIのHTTP field-name tokenに限り、不正な名前は位置付き`OACG100`です。
Pathsのキーに生の`?`・`#`がある場合も位置付き`OACG100`です。path内の文字は
percent-encodeし、query値はquery parameterで宣言します。
`Accept`・`Content-Type`・`Authorization`のheader parameterは、OpenAPI仕様どおり大小文字を問わず無視し、
`OACG108`のWarningを報告します。
header値はHTABと印字可能ASCII（U+0020～U+007E）だけを受け付け、
それ以外は送信前に`ArgumentException`になります。
`requestBody`を宣言したTRACEは、参照経由も含めて`OACG101`です。
複数の`servers`とserverの`variables`に対応します。既定のクライアントは`servers[0]`の各variableを
`default`で置換したURLを使います。生成クライアントは`Servers`（URLテンプレートの一覧）と
`BuildServerUrl(int index, IReadOnlyDictionary<string, string>? variables = null)`
（variablesは`enum`で検証）を公開しており、結果を`baseUrl`引数として渡せます。
Path Itemまたはoperationに`servers`がある場合、そのoperationではdocumentのserversより優先します。
`baseUrl`引数が置き換えるのはdocument levelのserversだけで、operation levelのURLは
`OperationServerUrls`プロパティで実行時に置き換えます。`operationId`のないoperationは
メソッドとpathから命名します（`GET /pets/{petId}`は`GetPetsByPetId`）。Path Itemの`$ref`
（とOpenAPI 3.1の`components.pathItems`）も辿ります。
path・query・header・cookieのparameterは、プリミティブ、プリミティブの配列、プリミティブpropertyだけの
objectを、OpenAPIの`style`／`explode`のすべての組み合わせで送信できます。queryの`allowReserved`と、
JSON media typeの`content`にも対応します。cookie parameterは1本の`Cookie`ヘッダーで送信します。
手動の`Cookie`ヘッダーは`CookieContainer`の値と併用・上書きされることがあるため、
`HttpClientHandler.UseCookies = false`にするか`CookieContainer`を使ってください。
securityScheme（`components.securitySchemes`: header／query／cookieの`apiKey`、`http`の`bearer`／`basic`／その他のscheme、
`oauth2`、`openIdConnect`、`mutualTLS`）とsecurity requirement（document・operation単位。`security: []`で認証なし）に
対応します。生成クライアントにはschemeごとにpublicプロパティが付きます: `string? <Scheme>ApiKey`、
`string? <Scheme>AccessToken`と非同期の`Func<CancellationToken, Task<string?>>? <Scheme>AccessTokenProvider`
（bearer・oauth2・openIdConnect）、`string? <Scheme>Username`／`<Scheme>Password`（basic）、
`string? <Scheme>Credentials`（その他のhttp scheme）。送信時は、文書順のrequirement（OR）のうち、含まれるすべてのscheme（AND）に
資格情報が設定された最初のものを使い、満たせるものがなければ認証なしで送ります。oauth2とopenIdConnectは、
渡されたtokenをBearerとして送るだけで、tokenの取得・更新はしません。`mutualTLS`はプロパティを持たず、
クライアント証明書は`HttpClientHandler`に設定します。cookieのapiKeyは、cookie parameterと同じ1本の`Cookie`ヘッダーにまとめます。
資格情報プロパティはスレッドセーフではないため、送信前に設定してください。不正なschemeと、未定義のschemeを指すrequirementは
位置付き`OACG100`です。
明示的な`baseUrl=""`の上書きは相対URLのままです。
相対server URLとoperation pathが`/`の場合、`BaseAddress`のqueryは1回だけ付加します。
リクエスト本文は`application/json`（具体的な`application/<subtype>+json`も可）、
`application/x-www-form-urlencoded`、`multipart/form-data`、テキスト（`text/*`と、schemaが
stringの`application/xml`）、バイナリ（`application/octet-stream`、`image/*`、`*/*`、または
`format: binary`のschema）を送信できます。複数のmedia typeを宣言したoperationは、優先順位が最上位
（JSON、form-urlencoded、multipart、text、binary）のmedia typeがoperation名のメソッドになり、
残りは`<name>AsFormUrlEncoded`、`<name>AsMultipart`、`<name>AsText`、`<name>AsBinary`の
追加メソッド（`CreatePetAsMultipart`の形）になります。multipartのファイルpart（`format: binary`）は
生成する`<ApiName>FileParameter`（`byte[]`または`Stream`、`FileName`、`ContentType`）で、`Stream`は
リクエストに渡されて一緒に破棄されるので、呼び出しごとに新しいstreamを渡してください。
Encoding Object（`contentType`、静的な`headers`、`style`／`explode`／`allowReserved`）に対応します。
form-urlencodedの値はpercent-encodeします（空白は`+`ではなく`%20`）。`application/*+json`のみを宣言すると
`OACG101`、`application/xml`のobject schemaも`OACG101`です。`application/vnd.*+json`など
部分的なwildcard subtypeも`OACG101`で拒否します。
成功レスポンスはJSON、テキスト（`string`）、バイナリ（`byte[]`）に対応します。2xxのcontractが異なる場合や
`2XX`範囲に本文がある場合は、status別の`<OperationName>Response`（`StatusCode`、`IsStatus200`、
`Status200`など）を返します。`default`だけのoperationは`default`を成功contractとみなし、
`responses`の省略（OpenAPI 3.1）は任意の`2xx`で成功します。`3XX`の宣言は受理し、`HttpClient`の
リダイレクト後の最終応答で判定します。型付きエラーとして、`<ApiName>Exception`は`StatusCode`と
`ResponseBody`に加え、宣言されたJSONエラー本文の`Error`、`TryGetError<T>(out T)`、
`ErrorDeserializationException`を持ちます（完全一致のstatus、`4XX`／`5XX`、`default`の順）。
responseの`headers`（と`components.headers`）は追加メソッド`<name>WithHttpInfo`で公開します
（status、raw・型付きheader、データ）。必須headerの欠落は`JsonSerializationException`です。
HEADの応答と成功204・205には、参照先を含めて本文の`content`を宣言できません（`OACG101`）。
Parameter・Request Body・Responseの`$ref`と並ぶ項目も検査します。OpenAPI 3.1では
文字列の`summary`・`description`を許可し、3.0では契約項目の黙示的な無視を避けるため
ジェネレーターの入力規則として`$ref`のみを受け付けます。
引用付きmedia parameterはHTABと、DELを除くU+0020～U+00FFを許可します。
U+00FFを超える文字は`OACG100`です。選択されるリクエストの`Content-Type`はASCIIに限り、
非ASCIIの引用文字も`OACG100`です。成功レスポンスの宣言は元の範囲を維持します。
`int64`、`float`、`decimal`、`date`、
`date-time`、`uuid`、`binary`のformatは小文字の完全一致だけを特別扱いし、
大文字を含む綴りはschemaの基底型として扱います。

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
content宣言のない成功応答では`Content-Type`を照合せず、
`Content-Type`がない場合も従来どおり本文を読みます。
成功JSON本文は引用付きの名前も含め宣言charsetで厳密に復号し、一致するBOMだけを除きます。
charset未指定はUTF-8・UTF-32 LE・UTF-16 LE/BEのBOMを判定し、BOMもなければUTF-8です。
不正なバイト列は`JsonSerializationException`です。エラー本文と本文型のない成功応答の
読み取りは従来どおりです。
成功レスポンスのJSONは、デシリアライズ前にRFC 8259の構文、重複・未知property、
scalarのJSON token型を検査します。`float`／`double`は有限値に限り、`1.0`や`1e0`のような
数学的整数は生成CLR型の範囲内で受け入れます。Json.NETの`$id`／`$ref`／`$values`参照メタデータは
配列を包むJSON objectも含めて互換拡張として維持します。`date`／`date-time`／`uuid`は受信時に
字句形式を検査します。字句が正しくてもCLRで表せない時刻やうるう秒は変換時に拒否される場合があります。
成功レスポンスDTOに`$id`、`$ref`、`$type`、`$values`をデータpropertyとして宣言すると、
その位置で`OACG100`を報告します。リクエスト専用DTOではこれらの名前を使用できます。
受信JSONの深さ上限は通常64階層です。ホストが`JsonConvert.DefaultSettings.MaxDepth`に正の値を設定した場合はその値を使い、
`null`または未指定なら64階層を維持します。
レスポンスの変換は独立したJson.NET serializerを使い、ホストのconverterやcontract resolverに
検査済みの値を変更させません。
必須nullableパラメーターや、対応表の範囲外のwire formatは位置付き診断で拒否します。

成功した`Generate`では、`Library/OpenApiCodeGen/SourceGenerator/SpecCache/<specId>/`に
正規化Bundleとmanifest、`Assets/OpenApiCodeGen/Generated/SpecCache/`にコンパイラー用mirror、
選択した出力フォルダーに`<ApiName>.OpenApiDefinition.cs`を配置します。所有する定義のnamespace変更や
同一asmdef内でのフォルダー移動では、`specId`と既存`.meta`のGUIDを保持します。移動先の競合や
所有者が曖昧な場合は生成を止め、手動編集したファイルを保護します。
公開が中断して次の`Generate`で復旧した場合は、復旧後の定義を読み直してから公開します。
最初の再実行で復旧を反映し、その後のユーザー編集は引き続き保護します。

基本パッケージは有効な`NamedBuildTarget`の`OPENAPI_CODEGEN_SOURCE_GENERATOR`を同期します。
この追加パッケージの削除時には、現在有効でないものも含むすべての既知ターゲットから
Unityが削除を適用する前にdefineを取り除きます。更新時は有効なターゲットから一時的に取り除きます。

## 資料とライセンス

- [Bundle v2形式](Documentation~/SourceGenerators/NormalizedSpecBundleV2.md)
- [リリース・復旧手順](Documentation~/RELEASE.md)
- [MIT License](LICENSE.md)
