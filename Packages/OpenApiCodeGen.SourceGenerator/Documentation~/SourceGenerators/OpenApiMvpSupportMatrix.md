# OpenAPI MVP support matrix

[日本語版](OpenApiMvpSupportMatrix.ja.md)

**Source Generator (Beta)** による生成はベータです。
Source Generator generation is in beta. Review the supported features before use.

Editorの「Open Support Matrix」は、base packageに同梱したHTMLをブラウザーで開きます。
両パッケージにこの文書と参照資料のMarkdown／HTMLを同梱しており、公開タグやネット接続は不要です。
GitHub Actionsや外部仕様へのリンクを開く場合はネット接続が必要です。

## 目的と対象読者

この文書は、`Rhycol.OpenApiCodeGen.SourceGenerator`で生成できるOpenAPI documentの
MVP範囲を、API仕様の作成者とUnity利用者が判断するためのmatrixです。
対象はSource Generator providerで選択したlocal `.json`、`.yaml`、`.yml`
document（extension case-insensitive）とHTTP(S) URLです。Editorは参照グラフ全体を
canonical Normalized Spec Bundle v2へ正規化し、Analyzerは1 specIdにつき1 AdditionalFile
だけを読みます。Bundle v1はAnalyzerの後方互換入力として読み込めます。

生成器は、wire formatに影響する未対応要素を黙って省略または推測しません。
Unsupportedに分類された要素と、Partialの制約外へ出た要素は診断を報告し、生成を完了しません。
入力・bundle・definitionの既存診断`OACG001`–`OACG009`に加え、semantic generationでは
`OACG100`–`OACG106`を使用します。型名の重複をsuffixで解消した場合はwarning `OACG107`を、無視した要素があればwarning `OACG108`を報告します。

| Status | 意味 |
| --- | --- |
| Supported | 記載した条件で生成・実行対象です。 |
| Partial | 記載した部分だけを生成します。条件外は診断となります。 |
| Unsupported | MVPの対象外です。診断となります。 |

## 入力と参照

| 項目 | Status | 条件・制約 |
| --- | --- | --- |
| 入力document | Supported | localの`.json`、`.yaml`、`.yml` file、またはHTTP(S) URL。local extensionはcase-insensitive。 |
| OpenAPI version | Partial | `3.0.*`と`3.1.*`のみ。 |
| OpenAPI 3.1 schema dialect | Partial | 既定または明示的な`https://spec.openapis.org/oas/3.1/dialect/base`のみ。入口・外部完全OpenAPI文書の`jsonSchemaDialect`とschemaの`$schema`で別dialectを指定すると`OACG101`、不正なURIは`OACG100`です。 |
| OpenAPI 3.2 / Swagger 2 | Unsupported | document versionは受け付けません。 |
| URL入力 | Supported | public/private/loopback、cross-host redirectを許可。userinfo、空白、非HTTP(S)、HTTPS-to-HTTP direct/redirectは拒否。Generateが明示的なfetch/refresh操作で、Docker fallbackはありません。 |
| private GitHub repository | Supported | `https://raw.githubusercontent.com`のURLのみ。Settingsの**GitHub Token**を、originが完全一致するリクエストとリダイレクト先にだけ`Authorization: Bearer`で送信。相対外部参照にも送信。トークンはプロジェクト外のユーザー設定に平文保存し、Bundle/manifest/診断には書き込みません。`github.com`、REST contents API、GitHub Enterprise Serverは対象外。 |
| JSON/YAML semantic parity | Supported | 各normalizerがshared `SpecNode`へ正規化し、同じsemantic/generation pipelineを通ります。 |
| `$ref` | Partial | OpenAPI/Schema上の参照位置にあるinternal reference、またはEditorがBundle v2のedgeへ解決したexternal reference。example/default/enum/拡張データ内の同名キーはデータとして保持。fragmentはemptyまたはJSON Pointer。 |
| external `$ref` | Partial | local external fileはreal/symbolic-link解決後もUnity project内。remote documentからlocal fileは不可。bare Schemaは可。参照先が完全なOpenAPI文書ならその`openapi`版と`jsonSchemaDialect`を検証し、入口とmajor.minor版が異なる場合は`OACG101`で拒否します。patch差は可。 Path Item `$ref`の参照先が`openapi`／`swagger`を持たないbareなPath Itemファイルは、HTTP methodを持つか、`$ref`・`parameters`・`servers`・`summary`・`description`だけを持ち、JSON Schema固有キーワード（`type`・`properties`・`items`・`allOf`・`enum`・`required`など）を持たない場合にPath Itemとして内部の参照を収集します。1つでもschemaキーワードがあればbare Schemaです（`$ref`だけのファイルはどちらでも同じ参照を収集します）。 |
| unresolved / cyclic `$ref` | Partial | 解決できない参照は`OACG102`です。`$ref`のaliasだけの循環（A→B→A）と、array／mapだけを経由して名前付きDTO／enumで閉じない循環は`OACG103`です。property・items・additionalPropertiesを経由するobject／enumの自己再帰・相互再帰は許可します。`oneOf`／`anyOf`のunionラッパーもobject・enumと同じ名前付きの型なので、unionを経由する再帰（`Node: oneOf [Leaf, array of Node]`など）も許可します。`OACG104`はBundle v1のexternal-reference互換診断です。 |
| schema `$ref` target | Supported | schemaの`$ref`は`#/components/schemas/<name>`に限らず、任意のJSON Pointer（例: `#/components/schemas/A/properties/b`、`$defs`配下、拡張キー配下）と、Bundle v2のexternal documentの任意fragmentを指せます。型名はpointer末尾のtokenから付け（末尾が配列indexなら直前のtokenを前置）、名前の衝突は既存どおり連番と`OACG107`で解決します。inline定義と同じschemaを指す参照は別の型になる場合があります。parameters／responses／requestBodiesなど他のcomponent参照は従来どおり`#/components/<section>/<name>`だけです。 |
| URL query / fragment | Partial | queryはfetch identityに使いますが平文永続化しません。root URL fragment、userinfo、explicit `file:`は拒否。 |
| URL format detection | Supported | final URL extensionとContent-Typeが既知なら一致必須。片方だけ既知は可、両方不明・競合は拒否。content sniffing/hintはなし。 |

### External graph limits

| Limit | Value |
| --- | ---: |
| Request timeout | 30 seconds |
| Graph timeout | 120 seconds |
| Maximum document size | 4 MiB |
| Maximum graph size | 32 MiB |
| Maximum documents | 64 |
| Maximum redirects | 5 |
| Maximum reference depth | 256 |

Editorはfetch、filesystem read/write、format parsing、reference resolutionを担当します。
AnalyzerはAdditionalFile内のBundleとedge mapだけを読みます。Bundleは次のtop-level field順を
持つstrict UTF-8/LF/2-space JSONです。

```text
formatVersion, specId, rawSha256, rootDocumentId, documents, referenceEdges
```

詳細なfield、ordering、manifest、publication failureの扱いは
[Normalized Spec Bundle v2](NormalizedSpecBundleV2.md)を参照してください。

## YAML subset

YAMLは全仕様対応ではなく、source generatorが安全に正規化できる bounded
YAML 1.2-compatible subsetです。subset外は黙ってJSONへ変換したり無視したり
せず、`YAML001`–`YAML015`のsource-located diagnosticで拒否します。

| 項目 | Status | 条件・制約 |
| --- | --- | --- |
| block mapping / sequence | Supported | indentationを使うmapping・sequence、nested/compact composition。compact continuationは2-space step。indentless sequenceと任意幅のcompact indentationは対象外。tabsはindentationに使用不可。 |
| flow mapping / sequence | Supported | nested、multiline、source orderを保持。flow collection内のblock scalarは対象外。 |
| mapping key | Partial | simple string keyのみ。duplicate、complex/non-string key、merge key `<<`は拒否。 |
| comments / encoding | Supported | comments、UTF-8 BOM、LF、CRLF。BOMはtreeから除きraw hashには含めます。 |
| quoted / plain scalar | Supported | single/double quoteとplain scalar。YAML 1.1 implicit bool/date等はstringとして保持。 |
| Unicode escapes in quoted scalars | Partial | Adjacent high/low `\u` surrogate escapes and valid `\U` scalars preserve non-BMP characters. Isolated, reversed, or mismatched surrogate escapes receive positioned `YAML006` before bundle generation. |
| JSON-compatible scalar | Supported | `null`、`true`/`false`、RFC 8259 number（integer/real）、string。 |
| literal / folded block scalar | Partial | `|`/`>`、`+`/`-` chomping、explicit indent `1`–`9`。`>`では内側の空行1行を改行1つ、空行2行を改行2つへfoldします。末尾空行はchompingに従います。flow内は拒否。 |
| flow plain delimiter | Partial | plain valueに`,`, `[`, `]`, `{`, `}`を含める場合はquote必須。URL scheme colon（`https://`）は受理。 |
| anchor / alias | Partial | anchor定義とalias展開をサポート。alias rootにはalias位置を付与。undefined/cycle/redefinitionは拒否。同一行のcompact anchor mapping（`- &a key: value`）は対象外で、nestedまたはflow valueとして記述する。 |
| tags / directives | Unsupported | explicit/custom tagとdirectiveは拒否。 |
| document stream | Unsupported | multiple document（`---`/`...`による複数document）は拒否。 |

### YAML source locations and limits

Lexer tokenはsource path、1-based line/column、UTF-16 offset/lengthを持ちます。
bundleへ保存するのはsource pathと1-based line/columnで、offset/lengthはlexer/parser
内部の診断情報です。syntax diagnosticのlogical pathはroot（empty）で、semantic
diagnosticのlogical pathはnormalized treeのtraversalで復元します。

Parser limits are hard bounds:

| Limit | Value |
| --- | ---: |
| Maximum input characters | 4,194,304 |
| Maximum tokens | 1,000,000 |
| Maximum scalar characters | 1,048,576 |
| Maximum aliases | 4,096 |
| Maximum anchors | 4,096 |
| Maximum expanded nodes | 1,000,000 |
| Maximum nesting depth | 256 |

YAML diagnostic IDs are stable within Phase 5:

| ID | Meaning |
| --- | --- |
| `YAML001` | Lexical error（invalid UTF-8など） |
| `YAML002` | Invalid indentation |
| `YAML003` | Invalid document |
| `YAML004` | Invalid mapping |
| `YAML005` | Invalid sequence |
| `YAML006` | Invalid scalar |
| `YAML007` | Duplicate key |
| `YAML008` | Unsupported tag |
| `YAML009` | Unsupported directive |
| `YAML010` | Multiple documents |
| `YAML011` | Complex key |
| `YAML012` | Undefined alias |
| `YAML013` | Alias cycle |
| `YAML014` | Anchor redefinition |
| `YAML015` | Limit exceeded |

## Operations、parameters、responses

| 項目 | Status | 条件・制約 |
| --- | --- | --- |
| HTTP method | Supported | `GET`、`POST`、`PUT`、`DELETE`、`PATCH`、`HEAD`、`OPTIONS`、`TRACE`。 |
| `servers` | Partial | root `servers`は複数指定でき、各`variables`（`default`必須、`enum`は任意）も使えます。既定のコンストラクターはservers[0]の各variableを`default`で置換したURLを使います（省略・空配列は`/`）。生成クライアントは`public static IReadOnlyList<string> Servers`（`url`テンプレートの一覧）と`public static string BuildServerUrl(int index, IReadOnlyDictionary<string, string>? variables = null)`を持ち、展開したURLは`baseUrl`引数として渡せます。`BuildServerUrl`は渡されたvariablesを`enum`で検証し、宣言のない名前・`enum`外の値は`ArgumentException`、範囲外の`index`は`ArgumentOutOfRangeException`です。`url`の未宣言`{x}`は`OACG101`、`default`が`enum`外・空の`enum`は`OACG100`です。絶対URLはHTTP(S)のみで、FTPなどは`OACG101`です。`//host/path`は`HttpClient.BaseAddress`のschemeで解決します。`/path`と既定の`/`は同じoriginのrootから解決し、`BaseAddress`のpathとqueryを引き継ぎません。`BaseAddress`が必要な場合に未設定なら送信前に`InvalidOperationException`です。明示的な空文字`baseUrl` overrideは相対URLとして扱います。相対server URLとoperation pathが`/`の場合、`BaseAddress`のqueryは1回だけ付加します。 |
| path / operationの`servers` | Partial | operation、Path Item、documentの順に優先してserverを選びます。operationまたはPath Itemに`servers`があるoperationは、その先頭serverの各variableを`default`で置換したURLを使います。**コンストラクターの`baseUrl`はdocument levelのserversだけを置き換え**、path / operationのserversは置き換えません。実行時に差し替えるには、`public IDictionary<string, string> OperationServerUrls`（キーは生成メソッド名、値は展開済みのserver URL）へ設定します。このプロパティは、path / operationのserversを持つoperationがあるクライアントにだけ生成します。候補が複数あっても使うのは先頭だけです。候補の一覧と展開は`public static IReadOnlyList<string> GetOperationServers(string operationName)`と`public static string BuildOperationServerUrl(string operationName, int index, IReadOnlyDictionary<string, string>? variables = null)`（`BuildServerUrl`と同じ`enum`検証）で行い、operation名には生成メソッド名の定数を持つ`public static class OperationNames`を使います。`OperationServerUrls`のキーは生成メソッド名です（他のメンバーとの衝突回避で連番が付くことがあるため、`OperationNames`の定数を使ってください。未知のキーは無視されます）。`OperationServerUrls`は最初の送信より前（構成時）に設定してください。スレッド安全ではありません。 |
| server URL validation | Partial | Malformed absolute-looking or relative URL references receive positioned `OACG100`; a `{...}` placeholder without a matching `variables` entry receives `OACG101`. Invalid absolute URLs no longer fall back to relative resolution. Valid HTTP(S), network-path, root-relative, and path-relative forms remain supported. A raw fragment is omitted before joining the operation path, while encoded `%23` remains path data. |
| parameter location | Supported | path、query、header、cookieのparameterを扱います。 |
| cookie parameter | Partial | `Cookie`ヘッダーを1本にまとめて送信します（複数のcookie parameterは`; `で連結し、parameterの識別子順）。`form`styleで、`explode`の既定は`true`です。名前はRFC 6265のcookie-name（token）でなければ`OACG100`、値はRFC 3986のunreserved以外をpercent-encodeします（cookie-octetの部分集合）。配列・objectは`explode: true`なら`name=a; name=b`／`k=v; k=w`、`false`なら`name=a,b`／`name=k,v,k,w`です（区切りの`,`はOpenAPIの`form`どおりで、RFC 6265の厳密なcookie-octetには含まれません）。`HttpClientHandler.UseCookies=true`（既定）では`CookieContainer`の値が手動の`Cookie`ヘッダーと併用・上書きされることがあるため、cookie parameterを使う場合は`UseCookies=false`にするか、`CookieContainer`に値を入れてください（Unity / Monoでの上書き挙動は実機確認が必要です）。`Cookie`という名前のheader parameterとcookie parameterの併用は`OACG101`です。 |
| Parameter Object fields | Partial | Standard OpenAPI 3.0/3.1 fields and `x-*` extensions are recognized. Unknown fields such as `requird` receive positioned `OACG101`, including path/operation parameters and reference targets. `schema`と`content`の併記は`OACG100`です。`example`／`examples`／`allowEmptyValue`は受理するだけで、送信値には影響しません。 |
| parameter serialization | Partial | `style`／`explode`は次の組み合わせを対応します。path: `simple`（既定）・`label`・`matrix`、query: `form`（既定）・`spaceDelimited`・`pipeDelimited`・`deepObject`、header: `simple`、cookie: `form`。`explode`の既定は`form`だけ`true`、`deepObject`は省略時`true`（`false`の明示は仕様未定義で`OACG101`）です。locationに無効な`style`は`OACG100`、プリミティブへの`spaceDelimited`／`pipeDelimited`／`deepObject`、配列への`deepObject`、`spaceDelimited`／`pipeDelimited`の`explode: true`は仕様未定義のため`OACG101`です。任意parameterの`null`は省略し、明示的な空文字列は`name=`（query）として送信します。`allowEmptyValue`は省略される値の代替を表し、明示的な空文字列には適用しません。 |
| array / object parameter | Partial | OpenAPI 3.xの「Style Examples」表（`color = blue,black,brown` / `R=100,G=200,B=150`／空値／単一値）どおりに送信します（テストが表の全セルを検証します）。対象はプリミティブの配列（`List<T>`）と、プリミティブpropertyだけのobject（DTOまたは`Dictionary<string, T>`）です。DTOは宣言したpropertyを名前の序数順にシリアライズします（`null`のpropertyは省略）。`additionalProperties`がプリミティブのschemaなら、追加memberを宣言propertyの後にキーの序数順で続けます（値が`null`のものは省略、プリミティブでない値は`ArgumentException`）。`additionalProperties`が省略・`true`の型なしopen DTOの追加memberは送りません。順序を指定したい場合は`Dictionary`を使ってください。配列要素の`null`は`ArgumentException`です。空の配列・objectは何も送りません（pathは空文字列）。値の区切りと`deepObject`の`[` `]`は表のとおりそのまま出力します（`\|`は`System.Uri`が`%7C`にします）。header値はpercent-encodeしません。入れ子の配列・object、`{}`・`JObject`・union・複数型のparameterは`style`では表せないため`OACG101`です（`content`を使います）。 |
| `allowReserved` | Partial | `allowReserved: true`（queryのみ）は、予約文字`:/?[]@!$&'()*+,;=`と既存の`%XX`（RFC 6570の予約展開）を値でエンコードしません。`#`は区切りとしてURLのfragmentになりHTTP層で落ちるため、常に`%23`にします。名前と、`allowReserved`でないparameterは従来どおりエンコードします。 |
| parameter `content` | Partial | `content`には`application/json`または具体的な`application/*+json`を1件だけ宣言でき、値をJSON直列化してからpercent-encodeして送ります（path・query・cookieはpercent-encode、headerはエンコードなしでASCIIだけ）。schemaは任意（object・array・`JToken`・union可）です。`style`／`explode`／`allowReserved`の併記は`OACG100`、複数のmedia typeや他のmedia typeは`OACG101`です。 |
| `operationId`の省略 | Supported | `operationId`のないoperationは、HTTP methodと、pathのセグメントを単語に分けたPascalCaseから決定的に命名します（`{param}`は`By`＋名前）。`GET /pets/{petId}`は`GetPetsByPetId`、`GET /`は`GetRoot`、`GET /report.{format}`は`GetReportByFormat`です。ほかのoperationId（明示指定が優先）や同じ規則で生成された名前と重なる場合は、pathの序数順に`2`、`3`…を付けます。メソッド名としての衝突回避は通常の命名規則（`AllocateUniqueName`）が引き続き行います。明示した`operationId`の重複は従来どおり`OACG106`です。 |
| Path Item `$ref` / `components.pathItems` | Supported | pathのPath Item `$ref`はinternalと、EditorがBundle v2のedgeへ解決したexternal documentの両方を辿ります（`#/paths/...`、OpenAPI 3.1の`#/components/pathItems/<name>`、他ファイルのfragment。`ref`先がさらに`$ref`でもよく、循環は`OACG103`）。`$ref`と併記できるのは`summary`／`description`／`x-*`だけで、operation・`parameters`・`servers`などの併記は仕様上未定義のため`OACG101`です。参照先のoperationはその参照元のpath keyで検証・命名します。`components.pathItems`は3.1のみ受理し（3.0は`OACG101`）、参照されたものだけを解析します。 |
| enum parameter formatting | Supported | String-enum path/query/header values use an isolated Json.NET serializer and declared wire strings; host `JsonConvert.DefaultSettings` converters or contract resolvers cannot rewrite them. Optional null parameters remain omitted. |
| non-finite numeric parameter | Unsupported | path／query／headerの`float`／`double`に`NaN`・正負の無限大を渡すと、HTTP送信前に`ArgumentOutOfRangeException`です。任意parameterの省略と有限値は使用できます。 |
| nested / untyped parameter | Unsupported | `style`で表せないparameter（入れ子のarray/object、`{}`・`JObject`・union・複数型）は直接定義・多段`$ref`ともに`OACG101`です。`content`を使うと任意のschemaを送れます。 |
| required nullable parameter | Unsupported | path/query/headerの`required: true`とnullableの組み合わせは`OACG101`です。参照チェーン全体を確認します。任意parameterのnull省略とDTOのnullableは維持します。 |
| dot-only path segment | Unsupported | 引数置換後のpath segmentが`.`／`..`（1回percent-decodeした表現を含む）なら、HTTP送信前に`ArgumentException`です。`.hidden`、`a.b`、`...`や通常の複合segmentは使用できます。 |
| path template braces | Partial | 対応しない`{`／`}`は位置付き`OACG100`です。`/pets}`や`/pets/{id}}`も生成時に拒否します。 |
| path key delimiters | Unsupported | Raw `?` or `#` in a Paths key receives positioned `OACG100`; percent-encoded path data remains available. Declare query values as query parameters. |
| header identity | Supported | header名だけ大文字小文字を区別せず、operation側で上書きします。送信名はoperationの宣言を保持し、path/query名は区別します。 |
| header parameter name | Partial | HTTP field-nameのASCII tokenのみを許可し、空白・コロン・非ASCIIなど不正な名前は`name`の位置付き`OACG100`です。 |
| ignored header parameters | Partial | `Accept`、`Content-Type`、`Authorization`は大小文字を問わず、OpenAPI仕様どおりparameterを無視し、名前の位置で`OACG108`のWarningを報告します。メソッド引数にもなりません。 |
| header parameter value | Partial | Generated clients accept HTAB and printable ASCII U+0020–U+007E only. Other characters, including CR, LF, DEL, non-ASCII and surrogates, raise `ArgumentException` before `HttpClient.SendAsync`. Omitted optional values remain omitted. |
| content専用header parameter | Unsupported | `Allow`、`Content-Disposition`、`Content-Encoding`、`Content-Language`、`Content-Length`、`Content-Location`、`Content-MD5`、`Content-Range`、`Expires`、`Last-Modified`は大小文字を問わず`OACG101`で拒否します（`Content-Type`は上の無視対象です）。独自の`Content-*`名まで一律には拒否しません。通常headerの追加に失敗した場合も実行時に`InvalidOperationException`です。 |
| request body | Partial | 複数のmedia typeを宣言できます。`application/json`（パラメーター付きも含む具体的な`application/<subtype>+json`。`application/*+json`だけの宣言は`OACG101`で、具体型と併記された場合は具体型を選びます）、`application/x-www-form-urlencoded`、`multipart/form-data`、テキスト（`text/*`、`application/xml`・`+xml`・`application/yaml`でschemaが`string`などのscalar）、バイナリ（`application/octet-stream`、`image/*`、`*/*`などその他のmedia typeで、schemaが`format: binary`・`contentMediaType`・省略）を扱います。type/subtypeを正規化して判定します。object schemaの`application/xml`など表せない組み合わせは該当位置の`OACG101`です。`requestBody.required: false`ならどのmedia typeでも`null`引数は本文なしで送信します（`<BodyParameterName>Specified`はJSONでschemaがnullableの場合だけです）。 |
| multiple request media types | Partial | 複数のmedia typeは優先順位（JSON > form-urlencoded > multipart > text > binary）の最上位を主メソッド（operation名のまま）にし、残りは種別名の接尾辞を付けた追加メソッドとして生成します（`<name>AsFormUrlEncoded`、`<name>AsMultipart`、`<name>AsText`、`<name>AsBinary`。同じ種別が複数あれば2つ目以降は`<name>As<MediaType>`、例`AsImagePng`。名前が重なれば連番）。黙って捨てるmedia typeはありません。schemaが同じJSON media typeは1つにまとめ、最初の具体型をContent-Typeに送ります（schemaが異なれば`OACG105`）。どの追加メソッドも同じparameter・戻り値・例外です。 |
| multipart/form-data request | Partial | object schemaの各propertyを1つのpartにします（DTOを引数に取り、`readOnly` propertyは送りません）。scalar・enum・`format: byte`は`ConvertToString`した文字列part（Content-Typeは`text/plain; charset=utf-8`）、配列は同名のpartを要素ごとに複数、`format: binary`（または`contentMediaType`）のpropertyと、その配列はファイルpart（`<ApiName>FileParameter`、配列は`List<<ApiName>FileParameter>`）、objectなど入れ子の値はJSON part（Content-Type `application/json`）です。boundaryは`MultipartFormDataContent`が付け、`Content-Disposition`は生成コードがRFC 7578に沿って組み立てます（`name`・`filename`は引用符で囲み`"`と``をエスケープ、CR/LFは`ArgumentException`、非ASCIIのpart名はUTF-8のpercent-encoding（netstandard2.0のHttpClientはヘッダーをUTF-8で書けないための妥協で、サーバーがpercent-decodeしなければフィールド名が一致しません。サロゲートペアは1コードポイントとして符号化します）、非ASCIIのファイル名はASCIIの代替名に`filename*=UTF-8''…`を併記。ファイル名は`FileParameter.FileName`があるときだけ）。必須のpartが`null`なら送信前に`ArgumentException`、任意のpartは`null`で省略します。multipartでは`encoding`の`style`／`explode`／`allowReserved`を使わず（`OACG108`）、配列は常に1要素1partです。 |
| application/x-www-form-urlencoded request | Partial | object schemaの各propertyを、queryの`form`style規則（parameter serializationと共通）で`name=value`にして`&`で連結し、`StringContent`（Content-Type `application/x-www-form-urlencoded`）で送ります。値はRFC 3986のunreservedだけを残してpercent-encodeします。**空白は`+`ではなく`%20`**です（.NETの`FormUrlEncodedContent`と異なりますが、サーバーは両方を空白として読みます）。`encoding`の`style`／`explode`／`allowReserved`に従い、配列は既定`name=a&name=b`、`explode: false`なら`name=a,b`、`pipeDelimited`なら`name=a|b`などです。プリミティブだけのobject・mapはqueryと同じ展開規則、それ以外の入れ子の値はJSON文字列をpercent-encodeした値です。`format: binary`のpropertyは`OACG101`です。 |
| Encoding Object | Partial | `encoding.<property>`の`contentType`（multipartのpart。カンマ区切りは先頭だけで`OACG108`）、`headers`（`schema.const`／`default`／`example`の静的な値を持つheaderだけをpartに付与。値のないheaderは`OACG108`、`Content-Type`は`contentType`を使い`OACG108`）、`style`／`explode`／`allowReserved`（form-urlencodedのみ）に対応します。宣言したpropertyが存在しなければ`OACG108`です。既定のContent-Typeは、文字列・scalarが`text/plain`、object・array of objectが`application/json`、ファイルが`application/octet-stream`（`contentMediaType`があればそれ）です。`FileParameter.ContentType`があればpartごとに優先します。 |
| binary request body | Partial | `application/octet-stream`など上記のバイナリmedia typeは`byte[]`を`ByteArrayContent`で送ります。media typeが`image/*`・`*/*`などのwildcardの場合は、Content-Typeを選ぶ任意引数`<BodyParameterName>ContentType`（`string?`、`null`なら`application/octet-stream`）を加えます。トップレベルの`Stream`オーバーロードはなく、巨大なファイルはmultipartの`FileParameter`（`Stream`）で送ります。 |
| file part (`<ApiName>FileParameter`) | Supported | `format: binary`のproperty（と`contentMediaType`を持つ非textのstring）は、生成する`public sealed class <ApiName>FileParameter`（`byte[]`または`Stream`、`FileName`、`ContentType`）を型にします。`Stream`は所有権をリクエストへ渡します。送信中に読まれ、リクエストと一緒に破棄されます（`StreamContent`の動作）ので、呼び出しごとに新しいstreamを渡し、送信後は使わないでください。送信前に例外が出た場合は破棄されません。`Stream`は再読込できないため、リトライや307/308リダイレクトによる本文の再送には使えません。コンストラクターは`byte[]`版と`Stream`版の2つです。 |
| TRACE request body | Unsupported | A TRACE operation declaring `requestBody`, including a reference, receives positioned `OACG101`. Bodyless TRACE remains supported. |
| Reference Object siblings | Partial | Parameter, Request Body, and Response references reject unsupported sibling fields with positioned `OACG101`. OpenAPI 3.1 permits string `summary` and `description` annotations; 3.0 accepts only `$ref` under this generator's strict input policy. Schema reference siblings follow the separate schema rule below. |
| quoted media parameters | Partial | Syntax permits HTAB and U+0020–U+00FF except DEL; characters above U+00FF receive positioned `OACG100`. The selected request `Content-Type` is further restricted to ASCII, so U+0080–U+00FF also receives positioned `OACG100` there. Successful response declarations retain the HTTP quoted-string range. |
| request JSON serialization | Supported | リクエスト本文は専用のJson.NET serializerで生成し、ホストの`JsonConvert.DefaultSettings`から`$type`／`$id`／`$ref`などのメタデータ設定を引き継ぎません。日付変換と明示的なJSON `null`は維持します。 |
| wildcard media type | Partial | `*/*`、`type/*`、`application/*+json`だけを受け付けます（request・response）。`application/vnd.*+json`など部分的なwildcard subtypeは生成時に`OACG101`です。`application/*+json`は成功responseのJSON、`*/*`と`type/*`はバイナリとして扱います（`*/*`のobject schemaは`OACG101`）。 |
| required non-nullable request body | Supported | 参照型の本文に`null`を渡すとHTTP送信前に`ArgumentNullException`です。schemaがnullableな必須本文はJSON `null`を送信できます。 |
| request array items | Supported | 非nullableな参照型要素が`null`なら、本文直下・入れ子の配列・DTO内の配列とも、シリアライズ前に`JsonSerializationException`で拒否します。nullableな要素の`null`は送信できます。 |
| optional nullable request body | Supported | `requestBody.required: false`かつschemaがnullableの場合、`null`引数は本文省略、非`null`引数はJSON本文を送信します。生成メソッドの`<BodyParameterName>Specified`を`true`にすると、`null`引数でも明示的なJSON `null`を送信します。名前が衝突する場合は番号を付けます。 |
| request charset | Partial | UTF-8、UTF-16 LE/BE。未指定はUTF-8。不正なContent-Typeや未対応charsetは入力位置付き診断です。 |
| response charset | Partial | Schema-declared successful JSON bodies use strict decoding. The declared charset, including quoted names, takes precedence and only its matching BOM is removed. Without a charset, UTF-8, UTF-32 LE, and UTF-16 LE/BE BOMs are recognized; otherwise UTF-8 is used. Invalid encoded bytes raise `JsonSerializationException`. Charset availability follows the runtime's `Encoding.GetEncoding`; unknown charset names raise `InvalidOperationException` during decoding. Empty bodies retain the existing JSON-body requirement; error and bodyless responses retain their existing reading behavior. |
| successful response | Partial | `2xx`（`2XX`範囲を含む）が成功です。全成功responseの本文contract（別名参照を終端まで解決し、実効nullable性・型・各値の境界が一致、inline enumの宣言順は無関係）が同じなら、従来どおり1つの型（本文なしは`Task`）を返します。**contractが異なる場合は、status別の結果型を返します（下の「status別の結果型」）。従来の`OACG105`は、1つのresponse内の複数のJSON media typeでschemaが異なる場合だけです。** schema付きJSON成功本文の空・空白はnullableでも`JsonSerializationException`、JSON `null`はnullable schemaだけで許可します。非nullableな配列要素がJSON `null`なら、入れ子の配列とDTO内の配列も含めて拒否します。 |
| status-keyed result type | Partial | 成功responseの本文contractが異なる、または`2XX`範囲に本文がある、またはdefaultだけ・responses省略などのoperationは、主メソッドが`<OperationName>Response`（名前が重なれば連番）を返します。`StatusCode`、宣言ごとの`Is<Status>`（`IsStatus200`、`IsStatus2XX`、`IsStatusDefault`）、本文があるstatusの型付きアクセサ`Status200`（その応答でなければ`null`）を持ちます。判定順は完全一致のstatus、`2XX`範囲、`default`です。status別のメソッドは追加しません。 |
| text / binary response | Partial | `text/*`・`application/xml`・`+xml`・`application/yaml`でschemaがscalar（`string`など）の成功本文は`string`（ほかのscalarは宣言型へ変換。変換できなければ`JsonSerializationException`）、`application/octet-stream`・`image/*`・`*/*`・schemaが`format: binary`または省略の成功本文は`byte[]`です。本文の文字コードはJSONと同じ規則（charset・BOM）で読みます。`HttpResponseMessage`を返すオーバーロードと`Stream`を返すメソッドは生成しません（巨大なファイルは`byte[]`に全読みします）。 |
| response `Accept` header | Partial | status別のresultを返すoperationと、JSON以外の本文または複数のmedia typeを持つ成功responseのoperationは、`Accept`に処理できるmedia typeを優先順（JSON > text > binary）で送ります（先頭が主media type、残りは`q=0.9`、`0.8`…）。response内でschemaが主media typeと異なる型の別media typeは処理できないため`OACG108`で通知し、`Accept`にも入れず、受信すると拒否します。受信した`Content-Type`で処理を選び（先頭のmedia typeは`Content-Type`なしにも使用）、宣言にないmedia typeは`JsonSerializationException`です。従来のJSON単独のoperationは`Accept`を送りません。 |
| default-only / omitted responses / 3XX | Partial | `2xx`がなく`default`だけなら、`default`を成功contractとみなし、任意の`2xx`で成功します（`2xx`と`default`が両方あれば`default`はエラーのcontractです）。`responses`を省略（OpenAPI 3.1）または`2xx`がない場合は、任意の`2xx`で成功する`Task`です（本文は読みません）。OpenAPI 3.0の`responses`省略・空は`OACG100`です。`3XX`の宣言は受理しますが生成物には影響せず、`HttpClient`の自動リダイレクトに従い**最終応答のstatusで成功・失敗を判定**します（`3xx`の最終応答は`2xx`でなければ`<ApiName>Exception`です）。 |
| bodyless response content | Unsupported | HEAD responses at any status, and successful 204 or 205 responses cannot declare nonempty `content`; referenced Response Objects are checked too. The content location receives `OACG101`. Empty `content` and bodyless responses remain supported. A `2XX` range may now declare a typed body (see the status-keyed result type). |
| successful response `Content-Type` | Partial | そのstatusにcontent宣言がある場合、headerを宣言media typeと照合し、不一致・不正・複数値は`JsonSerializationException`です。content宣言のない成功応答では`Content-Type`を照合しません。headerがない場合は従来どおり本文を読みます。.NETがheaderを先に正規化した場合、元の区切り空白は識別できません。 |
| successful response JSON | Partial | RFC 8259の構文、重複しないproperty名、宣言schemaに合うJSON token型を本文と入れ子の値で検査します。closed DTO（`additionalProperties: false`）の未宣言propertyを拒否し（open DTOとmapは許可し、宣言した値型を検査します）、Json.NETの`$id`／`$ref`／`$values`参照メタデータを互換拡張として維持します。配列schemaでも`$id`／`$values`で包まれたJSON objectを受け付け、配列要素を検査します。整数は数学的整数値の`1.0`／`1e0`を許可し、小数・CLR型の範囲外を拒否します。型不一致・未知property・非有限`float`／`double`は`JsonSerializationException`です。DTOの必須property（`writeOnly`を除く）の欠落とenumの未宣言値も、変換前のJSON検査で`JsonSerializationException`にします（従来はJson.NETの変換時に拒否）。 受信JSONの構文検査は通常最大64階層で、ホストが`JsonConvert.DefaultSettings.MaxDepth`に正の値を設定した場合はその値を使い、`null`または未指定なら64階層を維持します。 |
| response JSON conversion settings | Partial | The generated response serializer uses its own Json.NET defaults, so host `JsonConvert.DefaultSettings` converters and contract resolvers cannot change validated values. A positive host `MaxDepth` is the only inherited setting; absent, null, and invalid values use 64. Generated DTO converters and supported `$id`/`$ref`/`$values` metadata remain active. |
| response DTO metadata names | Unsupported | A successful response DTO cannot declare `$id`, `$ref`, `$type`, or `$values` as data properties; the property location receives `OACG100`. Request-only DTOs may declare them. Json.NET reference metadata remains supported where these names are not declared as schema properties. |
| error/default response | Partial | 本文の構造・参照を検証し、成功本文のDTO生成制限は適用しません。`4xx`・`5xx`・`4XX`・`5XX`・`default`のJSON本文はschemaが生成できれば`<ApiName>Exception`から取得できます（下の「typed error」）。生成できないschemaは受理しますが、その位置で`OACG108`のWarningを報告し、本文は`ResponseBody`の文字列だけです。本文を宣言していない（またはJSON本文を持たない）`4xx`・`5xx` statusは「型なし」として扱い、`default`の型にはフォールバックしません（`default`は未宣言のstatusにだけ適用）。JSON以外のmedia type（`*/*`などのwildcardを含む）も受理します。例外は実際の本文を保持します。 |
| typed error | Supported | `<ApiName>Exception`は従来の`StatusCode`・`ResponseBody`（文字列）を保ったまま、宣言されたJSON本文を逆シリアル化した`object? Error`と`bool TryGetError<T>(out T value)`、失敗理由の`Exception? ErrorDeserializationException`を持ちます。statusの選択は完全一致、`4XX`／`5XX`、`default`の順です。逆シリアル化に失敗（不正なJSON・必須property欠落など）しても例外を握りつぶさず、`ResponseBody`に本文を残して`Error`は`null`、失敗は`ErrorDeserializationException`に残します。既存の2引数コンストラクターは変わりません。エラーresponseのheaderは公開しません。 |
| response extension | Supported | operationのResponses Objectでは`x-*`を除外します。`components.responses`の`x-*`名は通常のResponse/Referenceです。 |
| response header | Partial | 成功responseの`headers`は、そのoperationに`<name>WithHttpInfo`メソッド（追加メソッド。`<Name>HttpInfo`クラスを返す）を生成して公開します。`StatusCode`、raw `Headers`（`IReadOnlyDictionary<string, string[]>`、大小文字を区別しない）、`Data`（通常のメソッドの戻り値）と、宣言headerごとの型付きプロパティ（`X-Rate-Limit`は`XRateLimit`）を持ちます。headerは`simple`style（scalarとscalarの配列。配列はカンマ区切り）、型は`int`・`double`・`bool`・`DateTime`・`Guid`・enumなどをinvariant cultureで読み、不正な値と`required`の欠落は`JsonSerializationException`です。通常のメソッドは同じ検査を行い`Data`だけを返します（headerを宣言したoperationだけが対象で、ほかのoperationに`WithHttpInfo`は生成しません）。header objectの`content`、objectのheader、`style: simple`以外は`OACG101`、`Content-Type`は仕様どおり`OACG108`で無視します。エラーresponseのheaderは検証だけで公開しません。`headers`はobjectが必要で、scalar・array・nullは位置付き`OACG100`です。 |
| `components.headers` | Supported | `components.headers`と、responses・`encoding`のheaderからの`#/components/headers/<name>`参照を解決します（参照先は通常のHeader Objectと同じ規則で検証します）。 |
| Response Object fields | Partial | Only `description`, `headers`, `content`, `links`, and `x-*` extensions are recognized. Unknown fields, including misspelled `contents`, receive positioned `OACG101` in success, error, and referenced responses. |

status codeはASCII数字で検証します。属性付きのジェネリックclassは`OACG005`で拒否します。

## Schema

以下は生成するrequest／成功response／DTOの制限です。非2xxと`default`の本文は構造・参照検証の対象です。

| 項目 | Status | 条件・制約 |
| --- | --- | --- |
| scalar | Supported | `string`、`integer`、`number`、`boolean`。 |
| object | Supported | named propertiesと`required`を持つobjectをDTOへ生成します。`additionalProperties`の省略または`true`はopen objectで、宣言外のpropertyを`[JsonExtensionData]`の`IDictionary<string, JToken>`メンバー（既定名`AdditionalProperties`、宣言済みpropertyと重なる場合は連番）に保持して再送し、成功応答の未知propertyも許可します。schema値の`additionalProperties`はpropertiesがあればopen objectとして同じく保持し、成功応答では各値をその型で検査します（値は`JToken`のまま取り出し、`ToObject<T>()`で変換します）。`additionalProperties: false`はclosedで、成功応答の未知propertyを拒否します。propertiesがなく`false`なら空のclosed DTOです。 |
| array | Partial | supported schemaのarray。 |
| string / integer enum | Partial | generated C# enumは宣言したwire値だけを完全一致で読み書きします。string enumは大小文字・空白も含む文字列を、integer enumは明示値付きのC# enum（値が`int`を超えれば`long`）と数値のwireを使い、JSON文字列・小数（`2.0`や`1e0`のような整数値の字句も）・未宣言値・未宣言のC# enum値を拒否します（整数トークンだけを受理）。`type`を省略したenumは値の型（すべてstringまたはすべてinteger）から推論します。stringとintegerの混在、number enum、boolean enumは`enum`の位置で`OACG101`です。`enum`に`null`を含むとnullable（`Priority?`）として扱います。`x-enum-varnames`／`x-enumNames`（前者を優先）はC#のmember名、`x-enum-descriptions`はmemberのXML docに使い、長さ不一致などの不正な指定は`OACG108`で無視します。名前のないinteger enumのmemberは`Value<n>`（負数は`ValueMinus<n>`）です。OpenAPI 3.1の`type: [string, null]`でも`enum`に`null`が無ければ非nullableです。重複値は`OACG101`です。 |
| direct schema reference | Supported | supported schemaへの参照。多段参照のscalar／enumもnullableを保持します。 |
| `$ref` sibling | Partial | 注釈、literalデータ、`x-*`と3.0の`nullable`は保持して参照のままです（3.1で`type`だけ、または`not`を併記した場合も参照のままです）。OpenAPI 3.1で`properties`、`required`、`additionalProperties`、`allOf`などの構造を併記すると`allOf: [ {$ref}, {siblings} ]`として扱い、下の`allOf`の規則で1つのDTOへ平坦化します。3.0では従来どおり、合成が必要な兄弟keywordを該当位置の`OACG101`で拒否します。 |
| nullable | Partial | OpenAPI 3.0の`nullable`、OpenAPI 3.1の`type: [<type>, null]`。 |
| optional nullable DTO property | Supported | 未代入はJSONから省略し、`null`代入は明示的なJSON `null`、値の代入はその値を送ります。生成される`<PropertyName>Specified`を`false`へ戻すと再び省略します。 |
| optional non-nullable DTO property | Supported | JSONからの省略は許可し、明示的なJSON `null`は`JsonSerializationException`です。未設定値はシリアライズ時に省略します。 |
| required non-nullable DTO property | Supported | 参照型の必須項目が`null`ならシリアライズ中に検出し、HTTP送信を止めます。入れ子のDTOも対象です。必須nullable項目のJSON `null`と必須値型の既定値は送信できます。 |
| scalar format | Partial | `integer`は通常`int`、`int64`は`long`。`number`は通常`double`、`float`は`float`、`decimal`は`decimal`。`date`、`date-time`、`uuid`は`DateTime`、`DateTimeOffset`、`Guid`へmappingします。成功レスポンスでは変換前にそれぞれ日付、タイムゾーン付き日時、ASCIIの8-4-4-4-12形式を字句検査します。字句が正しくてもCLRで表せない時刻・うるう秒などは変換時に拒否される場合があります。 `byte`は`byte[]`にmappingします（成功応答はbase64を検査）。 |
| format spelling | Partial | Only exact lowercase `int64`, `float`, `decimal`, `date`, `date-time`, `uuid`, `byte`, and `binary` trigger their special handling. Other casing is treated as an unrecognized format and uses the base schema type. |
| request number | Supported | `float`／`double`の`NaN`・正負の無限大は本文直下・配列・DTO内ともHTTP送信前に`JsonSerializationException`で拒否します。省略指定した任意項目は検査しません。 |
| map / free-form object | Supported | propertiesがなく`additionalProperties`がschemaなら`Dictionary<string, T>`です。成功応答では各値を検査し（非nullableな参照型のnull、型不一致、Json.NETの`$id`／`$ref`／`$type`／`$values`キーを拒否）、requestでは値の型に応じたnull・非有限数を検査します。propertiesがなく`additionalProperties`が省略または`true`の`type: object`（free-form）は`JObject`です。 |
| any / type inference | Supported | `{}`、OpenAPI 3.1のboolean schema `true`、`type: "null"`、およびnull以外の型を複数持つ3.1の型配列（例: `["string","integer"]`）は`JToken`です（JSON `null`を受け付けるため常にnullable）。`type`を省略した場合は、`properties`／`additionalProperties`があればobject、`items`があればarray、`enum`の値からstring／integer、`const`の値からその型を推論します。boolean schema `false`は`OACG101`です。parameterの`{}`や複数型は`OACG101`のままです。 |
| schema assertions | Unsupported | 生成モデルが保持しない`pattern`、`minLength`、`minimum`、`minItems`などのassertionは、そのkeywordの位置で`OACG101`です。説明用annotation、`default`、`x-*`は許可します。`const`と`$defs`はOpenAPI 3.1のkeywordで、3.0では`OACG101`です。 |
| `const` | Partial | OpenAPI 3.1のscalar const（string／integer／number／boolean）。成功応答では値の一致を検査し（integer／numberは数値として比較するため`2.0`は`2`と等価）、不一致は`JsonSerializationException`です。request DTOでは、string／整数／浮動小数／decimal／booleanのDTO propertyの初期値になるだけで、request側で値は検証しません（nullableな任意propertyは初期値の対象外）。object／array値は`OACG101`、schema型と合わない値は`OACG100`です。`null`や単一のscalar型を持たないschemaのconst、`enum`と併記したconstは`OACG108`で無視します。 |
| description / title / deprecated / default | Supported | `title`と`description`はDTO・property・enum（とmember）のXML docへ出力します。`deprecated: true`はschemaとpropertyに`[Obsolete]`を付けます（生成コード内は`#pragma warning disable CS0612, CS0618`）。`default`はXML docだけで、wire contractは変えません（object／arrayのdefaultは無視）。operationの`deprecated: true`は生成メソッドへ`[Obsolete]`を付け、`summary`は`<summary>`、`description`は`<remarks>`に出します。parameterの`deprecated`と`description`は`<param>`のXML docに反映します（C#の引数には`[Obsolete]`を付けられません）。 |
| `allOf` | Partial | 各partの`properties`・`required`・`additionalProperties`を1つのsealed DTOへ平坦化します（`$ref`先は再帰的に展開し、入れ子の`allOf`も可）。`required`は和集合です。**closednessは合成後に評価**します。どれか1つのpart（または3.1の`$ref`siblings）が`additionalProperties: false`ならDTO全体をclosedにし、許可するpropertyは全partのpropertiesの和集合です（partごとの`false`を単純合算して、他のpartのpropertyを拒否することはしません）。`additionalProperties`がschemaのpartがあればtyped open（複数あれば同じschemaが必要）、それ以外はopen objectです。同名propertyの型がpart間で異なれば`OACG101`（2つ目の宣言の位置、1つ目はadditional location）、同じ型なら統合します。partが`object`以外の`type`・`items`・`enum`・`const`を持つ場合、partが`oneOf`／`anyOf`を持つ場合（discriminator付きのbaseを除く）は`OACG101`、どのpartも宣言しない`required`は`OACG100`、`allOf`の循環は`OACG103`です。part内の`not`は`OACG108`で無視します。`allOf: [ {$ref} ]`の1要素に`description`・`nullable`などの注釈だけを併記する定番の形は、新しい型を作らない参照です（nullableも保持）。 |
| `oneOf` / `anyOf` + discriminator | Partial | discriminator付きの`oneOf`／`anyOf`は、sealedなunionラッパークラス（例: `Pet`）とメンバーのDTO（`Cat`、`Dog`）を生成します。メンバーのDTOはラッパーを継承しない通常のsealed DTOです。ラッパーに付ける`JsonConverter`が具象DTOの書き出しで再入して無限再帰することはありません。ラッパーは`FromCat(Cat)`・暗黙変換・`TryGetCat(out Cat)`・`AsCat()`、保持している具象値`Value`、選択値`DiscriminatorValue`を持ちます。メンバーは`$ref`でobjectを指す必要があります（inline、またはobject以外は、メンバーの位置の`OACG101`）。discriminator値は`mapping`（値はschema名、`#/components/schemas/X`のinternal pointer、または`oneOf`内の`$ref`と同じ文字列）と、mappingのないメンバーの暗黙値（schema名）で解決します。メンバーにmappingがあればその値だけが有効で、複数あれば最初の値を送ります。他ファイルを指すmapping、メンバーでないschemaを指すmappingは`OACG101`、複数のメンバーに同じ値を割り当てると`OACG100`です。メンバーDTOがdiscriminator propertyを宣言していなければ生成側が任意のstring propertyとして追加し、選択値が1つならDTOの初期値にします（宣言済みのstring propertyにも同じ初期値を付けます）。この初期値は検証せず、requiredにもしないため、unionを経由しない単独利用のDTO（discriminatorを持たない応答など）は有効なままです。discriminatorの有無と値の検査はunion（converterと成功応答の契約検査）だけが行います。requestはunionが常に選択値を書きます。成功応答は値でメンバーを選び、未知・欠落・stringでない値は`JsonSerializationException`で、選んだメンバーのDTO検査（closedなら未宣言property、必須property）も行います。ラッパーの`JsonConverter`クラスにはIL2CPP用の`PreserveAttribute`を付けます。 |
| discriminator付きbase（継承） | Partial | `oneOf`／`anyOf`を持たず`discriminator`だけを持つschema（子が`allOf: [ {$ref: base}, … ]`で継承する形）は、mappingの対象と、同じdocumentの`components.schemas`で`allOf`にbaseを直接`$ref`しているschema（孫まで）をメンバーとするunionになります。baseの`properties`は子のDTOへ`allOf`で平坦化されます。base自身は（値が`Pet`などでも）メンバーにならず、そのdiscriminator値は未知の値として拒否します。子もmappingもない`discriminator`は`OACG108`で無視します。他のdocumentのschemaは子として探しません。`oneOf`＋discriminatorを持つschemaを子が`allOf`で継承する形では、子はそのschemaの`properties`だけを引き継ぎ、`oneOf`は引き継ぎません。 |
| `oneOf` / `anyOf`（discriminatorなし） | Partial | `type: null`だけの部品を除いた候補が1つなら、その型のnullableに正規化します（`[X, null]`）。それ以外は成功応答の生の`JToken`を保持するunionラッパーです（`FromJToken(JToken)`、`ToJToken()`、候補ごとの`FromX(T)`・暗黙変換・`TryGetX(out T)`・`AsX()`。string／integerなどのプリミティブ候補も扱います。アクセサ名は型名で、`String`、`Int32`、`CatList`などです）。成功応答は候補ごとに既存のJSON契約検査を試し、`oneOf`はちょうど1件、`anyOf`は1件以上の一致を求めます（違反は一致件数つきの`JsonSerializationException`）。一致は構造検査（型、closed DTOの未宣言property、必須property、enumの値、const）で決めます。`oneOf`で複数の候補が一致した場合は、応答JSONのすべてのpropertyを宣言している候補（closedとみなした一致）に絞り込み、ちょうど1件ならそれを選びます（open DTO同士が重なる典型ケース）。絞り込んでも1件にならなければエラーです。`integer`と`number`のように区別できない`oneOf`や、同じpropertyだけを持つ候補は失敗し得るので、`required`・`additionalProperties: false`・`discriminator`で区別できるようにしてください。`anyOf`は絞り込まず1件以上の一致で足ります。`TryGetX`も同じ規則で候補を選びます。`TryGetX`は、`FromX`で渡した値か、保持しているJSONをJson.NETの厳密な設定（未宣言memberはエラー）で変換できるかで判定します。requestは渡された候補値または`JToken`をそのまま直列化します（候補値のrequest検査は行いません）。同じschemaへ`properties`／`required`など形を決めるkeywordを併記する、`allOf`と`oneOf`、`oneOf`と`anyOf`を併記する場合は`OACG101`です（keywordはメンバー側へ移します）。unionのparameterも`OACG101`です。 |
| `not` | Partial | 成功応答の検査だけで評価し（`not`のschemaに一致した値を`JsonSerializationException`で拒否）、生成する型は変えません。schema・property・`$ref`先のどこの`not`も対象で、一致は構造検査（型、enum値、const、DTOの形）で決めます。`required`だけのような形を持たない`not`、boolean schema、再帰する`not`、未対応の要素を含む`not`は、全値を拒否してしまうか判定できないため`OACG108`で無視します。requestでは評価しません。 |
| readOnly / writeOnly | Supported | `readOnly`なDTO propertyはrequestへ送信せず（生成される`ShouldSerialize<Name>()`が`false`）、requestのrequired検査からも外します。成功応答ではschemaの`required`どおり必須です。`writeOnly`なpropertyは成功応答で欠落しても許可し、requestではrequiredのままです。`$ref`先のschemaに付いた`readOnly`／`writeOnly`も、参照するpropertyに適用します。readOnly propertyの値はrequestで検証もしません。Unity IL2CPPのmanaged strippingでreflection専用メンバーが削除されないよう、`ShouldSerialize<Name>()`と`AdditionalProperties`にはinternalな`PreserveAttribute`を付け、`ShouldSerialize<Name>()`は`EditorBrowsable(Never)`にします。 |
| `format: byte` | Supported | `byte[]`（Json.NET標準のbase64）へmappingします（従来は`string`）。成功応答は文字列かつ有効なbase64であることを検査し、path/query/header parameterは標準base64文字列で送ります。 |
| `format: binary` | Partial | multipartのファイルpart（`<ApiName>FileParameter`）、バイナリのrequest本文（`byte[]`）、バイナリ応答（`byte[]`）で使えます。JSONの本文・DTO（JSON応答を含む）・parameter・header・form-urlencodedの値で参照されると、binaryのschemaの位置で`OACG101`です（binaryを含むschemaをcomponentsに置くことは可能で、使われた場所で判定します）。3.1の`type: string`＋`contentMediaType`（`contentEncoding`なし、JSON・text・xml以外）も同じ扱いです。大文字小文字の違う`format`は従来どおり通常のstringです。 |

`$ref`と併記できる注釈・データのキーは`$comment`、`default`、`deprecated`、
`description`、`example`、`examples`、`externalDocs`、`readOnly`、`summary`、`title`、`writeOnly`、`xml`、`x-*`です。
OpenAPI 3.0では既存互換として`nullable`も扱います。

リクエストJSONの`format: date`は`yyyy-MM-dd`へ変換します。公開型の`DateTime`は維持し、
`date-time`の`DateTimeOffset`にはdate用converterを適用しません。

## Generated outputと利用上の制約

生成結果は次のpublic API contractです。

- public mutable sealed DTO（Json.NET attribute使用）
- 任意かつschema上nullableなDTO propertyの`<PropertyName>Specified`による存在状態の管理
- 任意かつschema上nullableなrequest bodyの`<BodyParameterName>Specified`による明示的なJSON `null`送信
- 宣言したwire文字列を完全一致で扱うstring enum converterと`List<T>`
- `oneOf`／`anyOf`のsealed unionラッパークラス（`FromX`／`TryGetX`／`AsX`、discriminatorなしは`FromJToken`／`ToJToken`）と、internalな`JsonConverter`
- injected `HttpClient`を使うasync client API。documentまたはexplicit base URLを指定するconstructorを使用可能
- path/query/header/body、`JsonConvert`、`CancellationToken`、宣言された`2xx` response handling
- request本文のmedia type別メソッド（`<name>AsMultipart`など）、ファイルpartの`<ApiName>FileParameter`、status別の結果型`<OperationName>Response`、`<name>WithHttpInfo`と`<Name>HttpInfo`
- generated `<ApiName>Exception`（`Error`／`TryGetError<T>`／`ErrorDeserializationException`を追加）

生成モデルは、raw JSON/YAMLを各normalizerでnormalized bundleへ変換した後、OAS semantic parserとinternal reference resolverを通し、
Roslyn非依存のgeneration modelを作ってから、Roslynでparseしたdeterministic source emitterへ渡します。
emitterの結果を`AddSource`します。serializer/backendは固定で、generated clientは
Newtonsoft.Jsonと`System.Net.Http.HttpClient`を使用します。Docker providerは別providerであり、
fallbackまたは切替は行いません。

生成物はhand-editするsourceではなく、再Generateされるpublic contractです。spec IDを含むhint nameは
stableで、generated nameはordinalのdeterministic namingとsuffixによるcollision resolutionを使用します。
複数specでDTO名が重なる場合は、generated namespaceを分けてください。current generated typeは
namespace-levelのため、同じnamespaceには共存できません。

An `apiName` equal to a fixed generated client member receives `OACG005`.
Reserved names are `_httpClient`, `_baseUrl`, `DateOnlyJsonConverterInstance`,
`CreateRequestUri`, `CombineAbsoluteUri`, `CombinePaths`, `CombineQueries`,
`AppendQuery`, `SplitPathAndQuery`, `ValidatePathSegments`, `CreateJsonContent`,
`ConvertToString`, and `DateOnlyJsonConverter`.
Schema members whose CLR names would match another member's `Name + "Specified"`
are assigned a unique CLR name while retaining their JSON wire names. This
prevents Json.NET from mistaking a schema boolean for a presence flag.

## 診断

| ID | 意味 |
| --- | --- |
| `OACG005` | Invalid client definition, including an `apiName` reserved by a generated member |
| `OACG100` | InvalidDocument |
| `OACG101` | UnsupportedElement（bare non-Schema、`$id`/`$anchor`/`$dynamicAnchor`/`$dynamicRef`など） |
| `OACG102` | Unresolved document or JSON Pointer target |
| `OACG103` | Cyclic reference |
| `OACG104` | ExternalReference（Bundle v1 compatibility） |
| `OACG105` | InconsistentResponse（1つのresponse・requestBody内で、schemaが異なる複数のJSON media typeを宣言した場合だけ。status間でcontractが異なる成功responseはstatus別の結果型になります） |
| `OACG106` | InvalidIdentifier |
| `OACG107` | Warning: deterministic type-name collision resolution |
| `OACG108` | Warning: ignored OpenAPI element, reported with its logical path (never changes the wire contract) |

## 検証済み環境

GitHub ActionsのUnity検証とCD dry-runは実装済みです。レビュー対応前のclean commit
`adefab2c5bf367c3ecf5971fa555f1cbb68afa24`では、
[run 34987295723](https://github.com/rebeat-jp/UnityOpenApiCodeGen/actions/runs/34987295723)
で3版のUnity検証とCD dry-runが成功しています。

2026-09-20の追加レビュー対応後のローカル検証では、.NET 168/168、Node 50/50が成功しました。
Analyzer再現ビルド・同梱DLL同期・package inspection・tarball再現性と、同一候補を使った
次のUnity検証も成功しています。すべて失敗・スキップ0件です。

| Unity | 初回EditMode | 再生成 | add-on除去後 |
| --- | ---: | ---: | ---: |
| `2021.3.19f1`（base） | 58/58 | 対象外 | 対象外 |
| `6000.0.23f1` | 258/258 | 258/258 | 58/58 |
| `6000.3.2f1` | 258/258 | 258/258 | 58/58 |

baseはTest Framework未導入と導入済み・`testables`なしのconsumer compileが成功しています。
両Unity 6でもTest Framework導入済み・`testables`なしで、本番assemblyの生成と
両パッケージのtest assembly不在を確認しました。
追加テストでは出力フォルダー移行、SpecId／GUID保持、再起動後の復旧、競合編集保護、
同梱資料と日本語・空白を含むfile URIを確認しました。Unityは外部cwdから起動しても
内部cwdをprojectへ戻すため、設定の保存先と起動元への書き込み不在をEditorで検証し、
cwd依存の旧実装を検出するテストは実際の設定パス定義と明示的なUnity dataPathを使って.NETでも実行します。
Settings／Generatorは`Source Generator (Beta)`を表示し、ベータ説明とSupport Matrixへの
導線を持ちます。利用不可時もベータ表示を維持します。ApiName/PackageNameを編集でき、
Docker専用項目は無効になります。実UXMLテストに加え、Unity Editorで利用可能／不可と
狭い幅での説明文の折り返しを確認しました。Unity 2021.3のネイティブpopup操作は
このmacOS環境で標準PopupFieldでもEditor終了を再現したため、候補の実表示はUnity 6で確認しています。
共有`$ref`の解析完了結果はdocument ID＋pointerで再利用し、循環診断は維持します。
比較用の深さ14のDAGでは割当量が893,361,000 bytesから254,416 bytesへ減少しました
（ローカル測定値であり、実行時間のCI閾値には使いません）。
文書走査では870,986 bytes／8,000 schemaの同一入力で、割り当てが約843 MBから
約305 MBへ減少しました。エラーschema検証も成功済み参照を再利用し、深さ15の
共有DAGで約155 MBから約9.8 MBへ減少しました。

このローカル候補は`--allow-dirty`で作成した開発用証拠です。公開に使うclean commitと
最新のCI結果・成果物は[PR #56](https://github.com/rebeat-jp/UnityOpenApiCodeGen/pull/56)
で確認し、[RELEASE.md](../RELEASE.md)の承認・検証手順に従ってください。
IL2CPP/WebGLのPlayer互換性は未検証で、[#57](https://github.com/rebeat-jp/UnityOpenApiCodeGen/issues/57)
で追跡します。正式タグ・公開・OpenUPM初回登録は
[#62](https://github.com/rebeat-jp/UnityOpenApiCodeGen/issues/62)の別作業です。
