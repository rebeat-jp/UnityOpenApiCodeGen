# OpenAPI 対応表

[元の対応表](OpenApiMvpSupportMatrix.md)

**Source Generator (Beta)** による生成はベータです。
利用前にこの対応表で入力仕様と生成されるAPIの制約を確認してください。

Editorの「Open Support Matrix」は、base packageに同梱したHTMLをブラウザーで開きます。
両パッケージにこの文書と参照資料のMarkdown／HTMLを同梱しており、公開タグやネット接続は不要です。
GitHub Actionsや外部仕様へのリンクを開く場合はネット接続が必要です。

## 目的と対象読者

この文書は、`Rhycol.OpenApiCodeGen.SourceGenerator`で生成できるOpenAPI documentの
対応範囲を、API仕様の作成者とUnity利用者が判断するためのmatrixです。
対象はSource Generator providerで選択したlocal `.json`、`.yaml`、`.yml`
document（extension case-insensitive）とHTTP(S) URLです。documentの版はSwagger 2.0（`swagger: "2.0"`）とOpenAPI 3.0／3.1／3.2です。Editorは参照グラフ全体を
canonical Normalized Spec Bundle v2へ正規化し、Analyzerは1 specIdにつき1 AdditionalFile
だけを読みます。Bundle v1はAnalyzerの後方互換入力として読み込めます。

生成器は、wire formatに影響する未対応要素を黙って省略または推測しません。
Unsupportedに分類された要素と、Partialの制約外へ出た要素は診断を報告し、生成を完了しません。
入力・bundle・definitionの診断`OACG001`–`OACG009`に加え、semantic generationではError `OACG100`–`OACG106`を使用します。型名の重複をsuffixで解消した場合はWarning `OACG107`を報告します。
意図して無視する要素（webhooksなどclientの呼び出しに無関係なもの、仕様上無視すべきheader parameterなど）は、wire contractを変えないWarning `OACG108`で報告します。
HTTP実装とJSONシリアライザーの選択と、必要なライブラリの不足は`OACG010`–`OACG014`で報告します（「ライブラリの選択（HTTPとJSON）」）。
診断IDごとの報告場面は「診断」、ギャップ別の最終状況と対象外の項目は「ギャップ別の最終状況」「対象外の項目」の節にまとめています。

| 対応状況 | 意味 |
| --- | --- |
| 対応 | 記載した条件で生成・実行対象です。 |
| 一部対応 | 記載した部分だけを生成します。条件外は診断となります。 |
| 非対応 | 対象外です。診断となります。 |

## 入力と参照

| 項目 | 対応状況 | 条件・制約 |
| --- | --- | --- |
| 入力document | 対応 | localの`.json`、`.yaml`、`.yml` file、またはHTTP(S) URL。local extensionはcase-insensitive。 |
| OpenAPI version | 一部対応 | `3.0.*`、`3.1.*`、`3.2.*`、およびSwagger 2.0（`swagger: "2.0"`）のみ。`3.2.*`は3.1と同じJSON Schema 2020-12 dialectとschema規則で扱い、3.2で加わった項目は下の「OpenAPI 3.2」にまとめています。 |
| OpenAPI 3.1 schema dialect | 一部対応 | 既定または明示的な`https://spec.openapis.org/oas/3.1/dialect/base`のみ（3.2は`https://spec.openapis.org/oas/3.2/dialect/`配下のOAS dialectも同様）。入口・外部完全OpenAPI文書の`jsonSchemaDialect`とschemaの`$schema`で別dialectを指定すると`OACG101`、不正なURIは`OACG100`です。 |
| Swagger 2 | 一部対応 | `swagger: "2.0"`のdocumentを、OpenAPI 3.0へ正規化してから同じ生成pipelineで処理します（下の「Swagger 2.0」）。`1.2`など他の版は`OACG101`です。 |
| URL入力 | 対応 | public/private/loopback、cross-host redirectを許可。userinfo、空白、非HTTP(S)、HTTPS-to-HTTP direct/redirectは拒否。Generateが明示的なfetch/refresh操作で、Docker fallbackはありません。 |
| private GitHub repository | 対応 | `https://raw.githubusercontent.com`のURLのみ。Settingsの**GitHub Token**を、originが完全一致するリクエストとリダイレクト先にだけ`Authorization: Bearer`で送信。相対外部参照にも送信。トークンはプロジェクト外のユーザー設定に平文保存し、Bundle/manifest/診断には書き込みません。`github.com`、REST contents API、GitHub Enterprise Serverは対象外。 |
| JSON/YAML semantic parity | 対応 | 各normalizerがshared `SpecNode`へ正規化し、同じsemantic/generation pipelineを通ります。 |
| `$ref` | 一部対応 | OpenAPI/Schema上の参照位置にあるinternal reference、またはEditorがBundle v2のedgeへ解決したexternal reference。example/default/enum/拡張データ内の同名キーはデータとして保持。fragmentはemptyまたはJSON Pointer。 |
| external `$ref` | 一部対応 | local external fileはreal/symbolic-link解決後もUnity project内。remote documentからlocal fileは不可。bare Schemaは可。参照先が完全なOpenAPI文書ならその`openapi`版と`jsonSchemaDialect`を検証し、入口とmajor.minor版が異なる場合は`OACG101`で拒否します。patch差は可。 Path Item `$ref`の参照先が`openapi`／`swagger`を持たないbareなPath Itemファイルは、HTTP methodを持つか、`$ref`・`parameters`・`servers`・`summary`・`description`だけを持ち、JSON Schema固有キーワード（`type`・`properties`・`items`・`allOf`・`enum`・`required`など）を持たない場合にPath Itemとして内部の参照を収集します。1つでもschemaキーワードがあればbare Schemaです（`$ref`だけのファイルはどちらでも同じ参照を収集します）。 |
| unresolved / cyclic `$ref` | 一部対応 | 解決できない参照は`OACG102`です。`$ref`のaliasだけの循環（A→B→A）と、array／mapだけを経由して名前付きDTO／enumで閉じない循環は`OACG103`です。property・items・additionalPropertiesを経由するobject／enumの自己再帰・相互再帰は許可します。`oneOf`／`anyOf`のunionラッパーもobject・enumと同じ名前付きの型なので、unionを経由する再帰（`Node: oneOf [Leaf, array of Node]`など）も許可します。`OACG104`はBundle v1のexternal-reference互換診断です。 |
| schema `$ref` target | 対応 | schemaの`$ref`は`#/components/schemas/<name>`に限らず、任意のJSON Pointer（例: `#/components/schemas/A/properties/b`、`$defs`配下、拡張キー配下）と、Bundle v2のexternal documentの任意fragmentを指せます。型名はpointer末尾のtokenから付け（末尾が配列indexなら直前のtokenを前置）、名前の衝突は既存どおり連番と`OACG107`で解決します。inline定義と同じschemaを指す参照は別の型になる場合があります。parameters／responses／requestBodiesなど他のcomponent参照は従来どおり`#/components/<section>/<name>`だけです。 |
| URL query / fragment | 一部対応 | queryはfetch identityに使いますが平文永続化しません。root URL fragment、userinfo、explicit `file:`は拒否。 |
| URL format detection | 対応 | final URL extensionとContent-Typeが既知なら一致必須。片方だけ既知は可、両方不明・競合は拒否。content sniffing/hintはなし。 |

### 外部参照グラフの上限

| 上限 | 値 |
| --- | ---: |
| 1リクエストのタイムアウト | 30秒 |
| グラフ全体のタイムアウト | 120秒 |
| 1文書の最大サイズ | 4 MiB |
| グラフ全体の最大サイズ | 32 MiB |
| 最大文書数 | 64 |
| 最大リダイレクト数 | 5 |
| 最大参照深度 | 256 |

Editorはfetch、filesystem read/write、format parsing、reference resolutionを担当します。
AnalyzerはAdditionalFile内のBundleとedge mapだけを読みます。Bundleは次のtop-level field順を
持つstrict UTF-8/LF/2-space JSONです。

```text
formatVersion, specId, rawSha256, rootDocumentId, documents, referenceEdges
```

詳細なfield、ordering、manifest、publication failureの扱いは
[Normalized Spec Bundle v2](NormalizedSpecBundleV2.md)を参照してください。

## YAMLの対応範囲

YAMLは全仕様対応ではなく、source generatorが安全に正規化できる bounded
YAML 1.2-compatible subsetです。subset外は黙ってJSONへ変換したり無視したり
せず、`YAML001`–`YAML015`のsource-located diagnosticで拒否します。

| 項目 | 対応状況 | 条件・制約 |
| --- | --- | --- |
| block mapping / sequence | 対応 | indentationを使うmapping・sequence、nested/compact composition。compact continuationは2-space step。indentless sequenceと任意幅のcompact indentationは対象外。tabsはindentationに使用不可。 |
| flow mapping / sequence | 対応 | nested、multiline、source orderを保持。flow collection内のblock scalarは対象外。 |
| mapping key | 一部対応 | simple string keyのみ。duplicate、complex/non-string key、merge key `<<`は拒否。 |
| comments / encoding | 対応 | comments、UTF-8 BOM、LF、CRLF。BOMはtreeから除きraw hashには含めます。 |
| quoted / plain scalar | 対応 | single/double quoteとplain scalar。YAML 1.1 implicit bool/date等はstringとして保持。 |
| 引用付きscalarのUnicode escape | 一部対応 | 隣接する高位・低位の`\u`サロゲート対と有効な`\U`は非BMP文字を保持します。孤立・逆順・不一致のサロゲートescapeはBundle生成前に位置付き`YAML006`です。 |
| JSON-compatible scalar | 対応 | `null`、`true`/`false`、RFC 8259 number（integer/real）、string。 |
| literal / folded block scalar | 一部対応 | `|`/`>`、`+`/`-` chomping、explicit indent `1`–`9`。`>`では内側の空行1行を改行1つ、空行2行を改行2つへfoldします。末尾空行はchompingに従います。flow内は拒否。 |
| flow plain delimiter | 一部対応 | plain valueに`,`, `[`, `]`, `{`, `}`を含める場合はquote必須。URL scheme colon（`https://`）は受理。 |
| anchor / alias | 一部対応 | anchor定義とalias展開をサポート。alias rootにはalias位置を付与。undefined/cycle/redefinitionは拒否。同一行のcompact anchor mapping（`- &a key: value`）は対象外で、nestedまたはflow valueとして記述する。 |
| tags / directives | 非対応 | explicit/custom tagとdirectiveは拒否。 |
| document stream | 非対応 | multiple document（`---`/`...`による複数document）は拒否。 |

### YAMLの入力位置と上限

Lexer tokenはsource path、1-based line/column、UTF-16 offset/lengthを持ちます。
bundleへ保存するのはsource pathと1-based line/columnで、offset/lengthはlexer/parser
内部の診断情報です。syntax diagnosticのlogical pathはroot（empty）で、semantic
diagnosticのlogical pathはnormalized treeのtraversalで復元します。

パーサーには次の上限があります。

| 上限 | 値 |
| --- | ---: |
| 最大入力文字数 | 4,194,304 |
| 最大トークン数 | 1,000,000 |
| 最大scalar文字数 | 1,048,576 |
| 最大alias数 | 4,096 |
| 最大anchor数 | 4,096 |
| 最大展開ノード数 | 1,000,000 |
| 最大ネスト深度 | 256 |

YAMLの診断IDは次のとおりです。

| ID | 意味 |
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

## 操作・パラメーター・レスポンス

| 項目 | 対応状況 | 条件・制約 |
| --- | --- | --- |
| HTTP method | 対応 | `GET`、`POST`、`PUT`、`DELETE`、`PATCH`、`HEAD`、`OPTIONS`、`TRACE`。OpenAPI 3.2では`QUERY`と`additionalOperations`の任意のmethodも扱います（下の「OpenAPI 3.2」）。 |
| `servers` | 一部対応 | root `servers`は複数指定でき、各`variables`（`default`必須、`enum`は任意）も使えます。既定のコンストラクターはservers[0]の各variableを`default`で置換したURLを使います（省略・空配列は`/`）。生成クライアントは`public static IReadOnlyList<string> Servers`（`url`テンプレートの一覧）と`public static string BuildServerUrl(int index, IReadOnlyDictionary<string, string>? variables = null)`を持ち、展開したURLは`baseUrl`引数として渡せます。`BuildServerUrl`は渡されたvariablesを`enum`で検証し、宣言のない名前・`enum`外の値は`ArgumentException`、範囲外の`index`は`ArgumentOutOfRangeException`です。`url`の未宣言`{x}`は`OACG101`、`default`が`enum`外・空の`enum`は`OACG100`です。絶対URLはHTTP(S)のみで、FTPなどは`OACG101`です。`//host/path`は`HttpClient.BaseAddress`のschemeで解決します。`/path`と既定の`/`は同じoriginのrootから解決し、`BaseAddress`のpathとqueryを引き継ぎません。`BaseAddress`が必要な場合に未設定なら送信前に`InvalidOperationException`です。明示的な空文字`baseUrl` overrideは相対URLとして扱います。相対server URLとoperation pathが`/`の場合、`BaseAddress`のqueryは1回だけ付加します。 |
| path / operationの`servers` | 一部対応 | operation、Path Item、documentの順に優先してserverを選びます。operationまたはPath Itemに`servers`があるoperationは、その先頭serverの各variableを`default`で置換したURLを使います。**コンストラクターの`baseUrl`はdocument levelのserversだけを置き換え**、path / operationのserversは置き換えません。実行時に差し替えるには、`public IDictionary<string, string> OperationServerUrls`（キーは生成メソッド名、値は展開済みのserver URL）へ設定します。このプロパティは、path / operationのserversを持つoperationがあるクライアントにだけ生成します。候補が複数あっても使うのは先頭だけです。候補の一覧と展開は`public static IReadOnlyList<string> GetOperationServers(string operationName)`と`public static string BuildOperationServerUrl(string operationName, int index, IReadOnlyDictionary<string, string>? variables = null)`（`BuildServerUrl`と同じ`enum`検証）で行い、operation名には生成メソッド名の定数を持つ`public static class OperationNames`を使います。`OperationServerUrls`のキーは生成メソッド名です（他のメンバーとの衝突回避で連番が付くことがあるため、`OperationNames`の定数を使ってください。未知のキーは無視されます）。`OperationServerUrls`は最初の送信より前（構成時）に設定してください。スレッド安全ではありません。 |
| server URLの検証 | 一部対応 | 不正な絶対URL風・相対URL参照は位置付き`OACG100`、`variables`に宣言のない`{...}`は`OACG101`です。不正な絶対URLを相対URLとして扱いません。有効なHTTP(S)・network-path・root-relative・path-relative形式は維持します。生のfragmentはoperation pathとの結合前に除き、エンコード済み`%23`はpath内に保持します。 |
| parameter location | 対応 | path、query、header、cookieのparameterを扱います。OpenAPI 3.2では`in: querystring`も扱います（下の「OpenAPI 3.2」）。3.0／3.1の`in: querystring`は従来どおり`OACG101`です。 |
| cookie parameter | 一部対応 | `Cookie`ヘッダーを1本にまとめて送信します（複数のcookie parameterは`; `で連結し、parameterの識別子順）。`form`styleで、`explode`の既定は`true`です。名前はRFC 6265のcookie-name（token）でなければ`OACG100`、値はRFC 3986のunreserved以外をpercent-encodeします（cookie-octetの部分集合）。配列・objectは`explode: true`なら`name=a; name=b`／`k=v; k=w`、`false`なら`name=a,b`／`name=k,v,k,w`です（区切りの`,`はOpenAPIの`form`どおりで、RFC 6265の厳密なcookie-octetには含まれません）。`HttpClientHandler.UseCookies=true`（既定）では`CookieContainer`の値が手動の`Cookie`ヘッダーと併用・上書きされることがあるため、cookie parameterを使う場合は`UseCookies=false`にするか、`CookieContainer`に値を入れてください（Unity / Monoでの上書き挙動は実機確認が必要です）。`Cookie`という名前のheader parameterとcookie parameterの併用は`OACG101`です。 |
| Parameter Objectの項目 | 一部対応 | OpenAPI 3.0／3.1／3.2の標準項目と`x-*`拡張を認識します。`requird`などの未知項目は、path／operationのparameterや参照先でも位置付き`OACG101`です。`schema`と`content`の併記は`OACG100`です。`example`／`examples`／`allowEmptyValue`は受理するだけで、送信値には影響しません。 |
| parameter serialization | 一部対応 | `style`／`explode`は次の組み合わせを対応します。path: `simple`（既定）・`label`・`matrix`、query: `form`（既定）・`spaceDelimited`・`pipeDelimited`・`deepObject`、header: `simple`、cookie: `form`。`explode`の既定は`form`だけ`true`、`deepObject`は省略時`true`（`false`の明示は仕様未定義で`OACG101`）です。locationに無効な`style`は`OACG100`、プリミティブへの`spaceDelimited`／`pipeDelimited`／`deepObject`、配列への`deepObject`、`spaceDelimited`／`pipeDelimited`の`explode: true`は仕様未定義のため`OACG101`です。任意parameterの`null`は省略し、明示的な空文字列は`name=`（query）として送信します。`allowEmptyValue`は省略される値の代替を表し、明示的な空文字列には適用しません。 |
| array / object parameter | 一部対応 | OpenAPI 3.xの「Style Examples」表（`color = blue,black,brown` / `R=100,G=200,B=150`／空値／単一値）どおりに送信します（テストが表の全セルを検証します）。対象はプリミティブの配列（`List<T>`）と、プリミティブpropertyだけのobject（DTOまたは`Dictionary<string, T>`）です。DTOは宣言したpropertyを名前の序数順にシリアライズします（`null`のpropertyは省略）。`additionalProperties`がプリミティブのschemaなら、追加memberを宣言propertyの後にキーの序数順で続けます（値が`null`のものは省略、プリミティブでない値は`ArgumentException`）。`additionalProperties`が省略・`true`の型なしopen DTOの追加memberは送りません。順序を指定したい場合は`Dictionary`を使ってください。配列要素の`null`は`ArgumentException`です。空の配列・objectは何も送りません（pathは空文字列）。値の区切りと`deepObject`の`[` `]`は表のとおりそのまま出力します（`\|`は`System.Uri`が`%7C`にします）。header値はpercent-encodeしません。入れ子の配列・object、`{}`・`JObject`・union・複数型のparameterは`style`では表せないため`OACG101`です（`content`を使います）。 |
| `allowReserved` | 一部対応 | `allowReserved: true`（queryのみ）は、予約文字`:/?[]@!$&'()*+,;=`と既存の`%XX`（RFC 6570の予約展開）を値でエンコードしません。`#`は区切りとしてURLのfragmentになりHTTP層で落ちるため、常に`%23`にします。名前と、`allowReserved`でないparameterは従来どおりエンコードします。 |
| parameter `content` | 一部対応 | `content`には`application/json`または具体的な`application/*+json`を1件だけ宣言でき、値をJSON直列化してからpercent-encodeして送ります（path・query・cookieはpercent-encode、headerはエンコードなしでASCIIだけ）。schemaは任意（object・array・`JToken`・union可）です。`style`／`explode`／`allowReserved`の併記は`OACG100`、複数のmedia typeや他のmedia typeは`OACG101`です。 |
| `operationId`の省略 | 対応 | `operationId`のないoperationは、HTTP methodと、pathのセグメントを単語に分けたPascalCaseから決定的に命名します（`{param}`は`By`＋名前）。`GET /pets/{petId}`は`GetPetsByPetId`、`GET /`は`GetRoot`、`GET /report.{format}`は`GetReportByFormat`です。ほかのoperationId（明示指定が優先）や同じ規則で生成された名前と重なる場合は、pathの序数順に`2`、`3`…を付けます。メソッド名としての衝突回避は通常の命名規則（`AllocateUniqueName`）が引き続き行います。明示した`operationId`の重複は従来どおり`OACG106`です。 |
| Path Item `$ref` / `components.pathItems` | 対応 | pathのPath Item `$ref`はinternalと、EditorがBundle v2のedgeへ解決したexternal documentの両方を辿ります（`#/paths/...`、OpenAPI 3.1の`#/components/pathItems/<name>`、他ファイルのfragment。`ref`先がさらに`$ref`でもよく、循環は`OACG103`）。`$ref`と併記できるのは`summary`／`description`／`x-*`だけで、operation・`parameters`・`servers`などの併記は仕様上未定義のため`OACG101`です。参照先のoperationはその参照元のpath keyで検証・命名します。`components.pathItems`は3.1のみ受理し（3.0は`OACG101`）、参照されたものだけを解析します。 |
| enum parameter formatting | 対応 | path／query／headerのstring enum値は専用のJson.NET serializerで宣言wire文字列へ変換し、ホストの`JsonConvert.DefaultSettings`のconverterやcontract resolverを引き継ぎません。任意parameterのnull省略は維持します。 |
| 非有限数値parameter | 非対応 | path／query／headerの`float`／`double`に`NaN`・正負の無限大を渡すと、HTTP送信前に`ArgumentOutOfRangeException`です。任意parameterの省略と有限値は使用できます。 |
| nested / untyped parameter | 非対応 | `style`で表せないparameter（入れ子のarray/object、`{}`・`JObject`・union・複数型）は直接定義・多段`$ref`ともに`OACG101`です。`content`を使うと任意のschemaを送れます。 |
| required nullable parameter | 非対応 | path/query/headerの`required: true`とnullableの組み合わせは`OACG101`です。参照チェーン全体を確認します。任意parameterのnull省略とDTOのnullableは維持します。 |
| dot-only path segment | 非対応 | 引数置換後のpath segmentが`.`／`..`（1回percent-decodeした表現を含む）なら、HTTP送信前に`ArgumentException`です。`.hidden`、`a.b`、`...`や通常の複合segmentは使用できます。 |
| path template braces | 一部対応 | 対応しない`{`／`}`は位置付き`OACG100`です。`/pets}`や`/pets/{id}}`も生成時に拒否します。 |
| path keyの区切り文字 | 非対応 | Pathsのキーに生の`?`・`#`があると、キーの位置で`OACG100`です。path内の文字ならpercent-encodeし、query値はquery parameterで宣言します。 |
| header identity | 対応 | header名だけ大文字小文字を区別せず、operation側で上書きします。送信名はoperationの宣言を保持し、path/query名は区別します。 |
| header parameter name | 一部対応 | HTTP field-nameのASCII tokenのみを許可し、空白・コロン・非ASCIIなど不正な名前は`name`の位置付き`OACG100`です。 |
| 無視するheader parameter | 一部対応 | `Accept`、`Content-Type`、`Authorization`は大小文字を問わず、OpenAPI仕様どおりparameterを無視し、名前の位置で`OACG108`のWarningを報告します。メソッド引数にもなりません。 |
| header parameterの値 | 一部対応 | 生成クライアントが受け付けるのはHTABと印字可能ASCII（U+0020～U+007E）だけです。CR・LF・DEL・非ASCII・サロゲートは`HttpClient.SendAsync`より前に`ArgumentException`です。省略した任意値は送信しません。 |
| content専用header parameter | 非対応 | `Allow`、`Content-Disposition`、`Content-Encoding`、`Content-Language`、`Content-Length`、`Content-Location`、`Content-MD5`、`Content-Range`、`Expires`、`Last-Modified`は大小文字を問わず`OACG101`で拒否します（`Content-Type`は上の無視対象です）。独自の`Content-*`名まで一律には拒否しません。通常headerの追加に失敗した場合も実行時に`InvalidOperationException`です。 |
| request body | 一部対応 | 複数のmedia typeを宣言できます。`application/json`（パラメーター付きも含む具体的な`application/<subtype>+json`。`application/*+json`だけの宣言は`OACG101`で、具体型と併記された場合は具体型を選びます）、`application/x-www-form-urlencoded`、`multipart/form-data`、テキスト（`text/*`、`application/xml`・`+xml`・`application/yaml`でschemaが`string`などのscalar）、バイナリ（`application/octet-stream`、`image/*`、`*/*`などその他のmedia typeで、schemaが`format: binary`・`contentMediaType`・省略）を扱います。type/subtypeを正規化して判定します。object・arrayのschemaの`application/xml`（`+xml`）など表せない組み合わせは該当位置の`OACG101`ですが、同じ`content`にJSONなど生成できる他のmedia typeがあれば、そのxmlの項目だけをmedia typeの位置の`OACG108`で無視して残りで生成します（xmlだけなら`OACG101`のままです。生成クライアントは`Accept`を送らないので、xmlを要求することもありません）。`requestBody.required: false`ならどのmedia typeでも`null`引数は本文なしで送信します（`<BodyParameterName>Specified`はJSONでschemaがnullableの場合だけです）。 |
| multiple request media types | 一部対応 | 複数のmedia typeは優先順位（JSON > form-urlencoded > multipart > text > binary）の最上位を主メソッド（operation名のまま）にし、残りは種別名の接尾辞を付けた追加メソッドとして生成します（`<name>AsFormUrlEncoded`、`<name>AsMultipart`、`<name>AsText`、`<name>AsBinary`。同じ種別が複数あれば2つ目以降は`<name>As<MediaType>`、例`AsImagePng`。名前が重なれば連番）。黙って捨てるmedia typeはありません。schemaが同じJSON media typeは1つにまとめ、最初の具体型をContent-Typeに送ります（schemaが異なれば`OACG105`）。どの追加メソッドも同じparameter・戻り値・例外です。 |
| multipart/form-data request | 一部対応 | object schemaの各propertyを1つのpartにします（DTOを引数に取り、`readOnly` propertyは送りません）。scalar・enum・`format: byte`は`ConvertToString`した文字列part（Content-Typeは`text/plain; charset=utf-8`）、配列は同名のpartを要素ごとに複数、`format: binary`（または`contentMediaType`）のpropertyと、その配列はファイルpart（`<ApiName>FileParameter`、配列は`List<<ApiName>FileParameter>`）、objectなど入れ子の値はJSON part（Content-Type `application/json`）です。boundaryは`MultipartFormDataContent`が付け、`Content-Disposition`は生成コードがRFC 7578に沿って組み立てます（`name`・`filename`は引用符で囲み`"`と``をエスケープ、CR/LFは`ArgumentException`、非ASCIIのpart名はUTF-8のpercent-encoding（netstandard2.0のHttpClientはヘッダーをUTF-8で書けないための妥協で、サーバーがpercent-decodeしなければフィールド名が一致しません。サロゲートペアは1コードポイントとして符号化します）、非ASCIIのファイル名はASCIIの代替名に`filename*=UTF-8''…`を併記。ファイル名は`FileParameter.FileName`があるときだけ）。必須のpartが`null`なら送信前に`ArgumentException`、任意のpartは`null`で省略します。multipartでは`encoding`の`style`／`explode`／`allowReserved`を使わず（`OACG108`）、配列は常に1要素1partです。 |
| application/x-www-form-urlencoded request | 一部対応 | object schemaの各propertyを、queryの`form`style規則（parameter serializationと共通）で`name=value`にして`&`で連結し、`StringContent`（Content-Type `application/x-www-form-urlencoded`）で送ります。値はRFC 3986のunreservedだけを残してpercent-encodeします。**空白は`+`ではなく`%20`**です（.NETの`FormUrlEncodedContent`と異なりますが、サーバーは両方を空白として読みます）。`encoding`の`style`／`explode`／`allowReserved`に従い、配列は既定`name=a&name=b`、`explode: false`なら`name=a,b`、`pipeDelimited`なら`name=a|b`などです。プリミティブだけのobject・mapはqueryと同じ展開規則、それ以外の入れ子の値はJSON文字列をpercent-encodeした値です。`format: binary`のpropertyは`OACG101`です。 |
| Encoding Object | 一部対応 | `encoding.<property>`の`contentType`（multipartのpart。カンマ区切りは先頭だけで`OACG108`）、`headers`（`schema.const`／`default`／`example`の静的な値を持つheaderだけをpartに付与。値のないheaderは`OACG108`、`Content-Type`は`contentType`を使い`OACG108`）、`style`／`explode`／`allowReserved`（form-urlencodedのみ）に対応します。宣言したpropertyが存在しなければ`OACG108`です。既定のContent-Typeは、文字列・scalarが`text/plain`、object・array of objectが`application/json`、ファイルが`application/octet-stream`（`contentMediaType`があればそれ）です。`FileParameter.ContentType`があればpartごとに優先します。 |
| binary request body | 一部対応 | `application/octet-stream`など上記のバイナリmedia typeは`byte[]`を`ByteArrayContent`で送ります。media typeが`image/*`・`*/*`などのwildcardの場合は、Content-Typeを選ぶ任意引数`<BodyParameterName>ContentType`（`string?`、`null`なら`application/octet-stream`）を加えます。トップレベルの`Stream`オーバーロードはなく、巨大なファイルはmultipartの`FileParameter`（`Stream`）で送ります。 |
| file part (`<ApiName>FileParameter`) | 対応 | `format: binary`のproperty（と`contentMediaType`を持つ非textのstring）は、生成する`public sealed class <ApiName>FileParameter`（`byte[]`または`Stream`、`FileName`、`ContentType`）を型にします。`Stream`は所有権をリクエストへ渡します。送信中に読まれ、リクエストと一緒に破棄されます（`StreamContent`の動作）ので、呼び出しごとに新しいstreamを渡し、送信後は使わないでください。送信前に例外が出た場合は破棄されません。`Stream`は再読込できないため、リトライや307/308リダイレクトによる本文の再送には使えません。コンストラクターは`byte[]`版と`Stream`版の2つです。 |
| TRACEのrequest body | 非対応 | `requestBody`を宣言するTRACEは、参照経由も含めてその位置の`OACG101`です。本文のないTRACEは使用できます。 |
| Reference Objectの隣接項目 | 一部対応 | Parameter・Request Body・Responseの`$ref`と並ぶ未対応項目は、その位置の`OACG101`です。OpenAPI 3.1では文字列の`summary`・`description`を許可し、3.0ではジェネレーターの厳格な入力規則として`$ref`のみを受け付けます。Schema参照の規則は別行を参照してください。 |
| 引用付きmedia parameter | 一部対応 | 構文上はHTABとDELを除くU+0020～U+00FFを許可し、U+00FFを超える文字は位置付き`OACG100`です。選択されるリクエストの`Content-Type`はさらにASCIIに限り、U+0080～U+00FFも位置付き`OACG100`です。成功レスポンスの宣言はHTTP quoted-stringの範囲を維持します。 |
| リクエストJSONシリアライズ | 対応 | リクエスト本文は専用のJson.NET serializerで生成し、ホストの`JsonConvert.DefaultSettings`から`$type`／`$id`／`$ref`などのメタデータ設定を引き継ぎません。日付変換と明示的なJSON `null`は維持します。 |
| wildcard media type | 一部対応 | `*/*`、`type/*`、`application/*+json`だけを受け付けます（request・response）。`application/vnd.*+json`など部分的なwildcard subtypeは生成時に`OACG101`です。`application/*+json`は成功responseのJSON、`*/*`と`type/*`はバイナリとして扱います（`*/*`のobject schemaは`OACG101`）。 |
| required non-nullable request body | 対応 | 参照型の本文に`null`を渡すと、HTTP送信前に`ArgumentNullException`です。schemaがnullableな必須本文はJSON `null`を送信できます。 |
| request array items | 対応 | 非nullableな参照型要素が`null`なら、本文直下・入れ子の配列・DTO内の配列とも、シリアライズ前に`JsonSerializationException`で拒否します。nullableな要素の`null`は送信できます。 |
| optional nullable request body | 対応 | `requestBody.required: false`かつschemaがnullableの場合、`null`引数は本文省略、非`null`引数はJSON本文を送信します。生成メソッドの`<BodyParameterName>Specified`を`true`にすると、`null`引数でも明示的なJSON `null`を送信します。名前が衝突する場合は番号を付けます。 |
| request charset | 一部対応 | UTF-8、UTF-16 LE/BE。未指定はUTF-8。不正なContent-Typeや未対応charsetは入力位置付き診断です。 |
| response charset | 一部対応 | schema付き成功JSON本文は厳密に復号します。引用付きの名前も含め宣言charsetを優先し、一致するBOMだけを除きます。charset未指定はUTF-8・UTF-32 LE・UTF-16 LE/BEのBOMを判定し、BOMもなければUTF-8です。不正なバイト列は`JsonSerializationException`です。charsetの対応範囲は実行環境の`Encoding.GetEncoding`に従い、未知のcharset名は復号時に`InvalidOperationException`です。空本文のJSON必須判定、エラー本文と本文型のない応答の読み取りは従来どおりです。 |
| successful response | 一部対応 | `2xx`（`2XX`範囲を含む）が成功です。全成功responseの本文contract（別名参照を終端まで解決し、実効nullable性・型・各値の境界が一致、inline enumの宣言順は無関係）が同じなら、従来どおり1つの型（本文なしは`Task`）を返します。**contractが異なる場合は、status別の結果型を返します（下の「status別の結果型」）。従来の`OACG105`は、1つのresponse内の複数のJSON media typeでschemaが異なる場合だけです。** schema付きJSON成功本文の空・空白はnullableでも`JsonSerializationException`、JSON `null`はnullable schemaだけで許可します。非nullableな配列要素がJSON `null`なら、入れ子の配列とDTO内の配列も含めて拒否します。 |
| status-keyed result type | 一部対応 | 成功responseの本文contractが異なる、または`2XX`範囲に本文がある、またはdefaultだけ・responses省略などのoperationは、主メソッドが`<OperationName>Response`（名前が重なれば連番）を返します。`StatusCode`、宣言ごとの`Is<Status>`（`IsStatus200`、`IsStatus2XX`、`IsStatusDefault`）、本文があるstatusの型付きアクセサ`Status200`（その応答でなければ`null`）を持ちます。判定順は完全一致のstatus、`2XX`範囲、`default`です。status別のメソッドは追加しません。 |
| text / binary response | 一部対応 | `text/*`・`application/xml`・`+xml`・`application/yaml`でschemaがscalar（`string`など）の成功本文は`string`（ほかのscalarは宣言型へ変換。変換できなければ`JsonSerializationException`）、`application/octet-stream`・`image/*`・`*/*`・schemaが`format: binary`または省略の成功本文は`byte[]`です。object・arrayのschemaの`application/xml`（`+xml`）は、同じ`content`に生成できる他のmedia typeがあればその項目だけを`OACG108`で無視します（なければ`OACG101`）。本文の文字コードはJSONと同じ規則（charset・BOM）で読みます。`HttpResponseMessage`を返すオーバーロードと`Stream`を返すメソッドは生成しません（巨大なファイルは`byte[]`に全読みします）。 |
| response `Accept` header | 一部対応 | status別のresultを返すoperationと、JSON以外の本文または複数のmedia typeを持つ成功responseのoperationは、`Accept`に処理できるmedia typeを優先順（JSON > text > binary）で送ります（先頭が主media type、残りは`q=0.9`、`0.8`…）。response内でschemaが主media typeと異なる型の別media typeは処理できないため`OACG108`で通知し、`Accept`にも入れず、受信すると拒否します。受信した`Content-Type`で処理を選び（先頭のmedia typeは`Content-Type`なしにも使用）、宣言にないmedia typeは`JsonSerializationException`です。従来のJSON単独のoperationは`Accept`を送りません。 |
| default-only / omitted responses / 3XX | 一部対応 | `2xx`がなく`default`だけなら、`default`を成功contractとみなし、任意の`2xx`で成功します（`2xx`と`default`が両方あれば`default`はエラーのcontractです）。`responses`を省略（OpenAPI 3.1）または`2xx`がない場合は、任意の`2xx`で成功する`Task`です（本文は読みません）。OpenAPI 3.0の`responses`省略・空は`OACG100`です。`3XX`の宣言は受理しますが生成物には影響せず、`HttpClient`の自動リダイレクトに従い**最終応答のstatusで成功・失敗を判定**します（`3xx`の最終応答は`2xx`でなければ`<ApiName>Exception`です）。 |
| 本文を持てないレスポンス | 非対応 | HEADの全status、および成功204・205では非空の`content`宣言を位置付き`OACG101`で拒否します。Response Objectの参照先も検査します。空の`content`と本文なしは使えます。`2XX`範囲は型付き本文を宣言できます（status別の結果型）。 |
| 成功レスポンスの`Content-Type` | 一部対応 | そのstatusにcontent宣言がある場合、headerを宣言media typeと照合し、不一致・不正・複数値は`JsonSerializationException`です。content宣言のない成功応答では`Content-Type`を照合しません。headerがない場合は従来どおり本文を読みます。.NETがheaderを先に正規化した場合、元の区切り空白は識別できません。 |
| 成功レスポンスのJSON | 一部対応 | RFC 8259の構文、重複しないproperty名、宣言schemaに合うJSON token型を本文と入れ子の値で検査します。closed DTO（`additionalProperties: false`）の未宣言propertyを拒否し（open DTOとmapは許可し、宣言した値型を検査します）、Json.NETの`$id`／`$ref`／`$values`参照メタデータを互換拡張として維持します。配列schemaでも`$id`／`$values`で包まれたJSON objectを受け付け、配列要素を検査します。整数は数学的整数値の`1.0`／`1e0`を許可し、小数・CLR型の範囲外を拒否します。型不一致・未知property・非有限`float`／`double`は`JsonSerializationException`です。DTOの必須property（`writeOnly`を除く）の欠落とenumの未宣言値も、変換前のJSON検査で`JsonSerializationException`にします（従来はJson.NETの変換時に拒否）。 受信JSONの構文検査は通常最大64階層で、ホストが`JsonConvert.DefaultSettings.MaxDepth`に正の値を設定した場合はその値を使い、`null`または未指定なら64階層を維持します。 |
| レスポンスJSONの変換設定 | 一部対応 | 生成するレスポンスserializerは独立したJson.NET設定を使い、ホストの`JsonConvert.DefaultSettings`にあるconverter・contract resolverは検査済みの値を変えません。引き継ぐのは正の`MaxDepth`だけで、未指定・`null`・不正値なら64です。生成DTOのconverterと対応済みの`$id`／`$ref`／`$values`メタデータは維持します。 |
| 成功レスポンスDTOのメタデータ名 | 非対応 | DTO propertyに`$id`、`$ref`、`$type`、`$values`を宣言すると、そのpropertyの位置で`OACG100`です。リクエスト専用DTOでは使用できます。schemaで宣言していないJson.NET参照メタデータの互換動作は維持します。 |
| error/default response | 一部対応 | 本文の構造・参照を検証し、成功本文のDTO生成制限は適用しません。`4xx`・`5xx`・`4XX`・`5XX`・`default`のJSON本文はschemaが生成できれば`<ApiName>Exception`から取得できます（下の「typed error」）。生成できないschemaは受理しますが、その位置で`OACG108`のWarningを報告し、本文は`ResponseBody`の文字列だけです。本文を宣言していない（またはJSON本文を持たない）`4xx`・`5xx` statusは「型なし」として扱い、`default`の型にはフォールバックしません（`default`は未宣言のstatusにだけ適用）。JSON以外のmedia type（`*/*`などのwildcardを含む）も受理します。例外は実際の本文を保持します。 |
| typed error | 対応 | `<ApiName>Exception`は従来の`StatusCode`・`ResponseBody`（文字列）を保ったまま、宣言されたJSON本文を逆シリアル化した`object? Error`と`bool TryGetError<T>(out T value)`、失敗理由の`Exception? ErrorDeserializationException`を持ちます。statusの選択は完全一致、`4XX`／`5XX`、`default`の順です。逆シリアル化に失敗（不正なJSON・必須property欠落など）しても例外を握りつぶさず、`ResponseBody`に本文を残して`Error`は`null`、失敗は`ErrorDeserializationException`に残します。既存の2引数コンストラクターは変わりません。エラーresponseのheaderは公開しません。 |
| response extension | 対応 | operationのResponses Objectでは`x-*`を除外します。`components.responses`の`x-*`名は通常のResponse/Referenceです。 |
| response header | 一部対応 | 成功responseの`headers`は、そのoperationに`<name>WithHttpInfo`メソッド（追加メソッド。`<Name>HttpInfo`クラスを返す）を生成して公開します。`StatusCode`、raw `Headers`（`IReadOnlyDictionary<string, string[]>`、大小文字を区別しない）、`Data`（通常のメソッドの戻り値）と、宣言headerごとの型付きプロパティ（`X-Rate-Limit`は`XRateLimit`）を持ちます。headerは`simple`style（scalarとscalarの配列。配列はカンマ区切り）、型は`int`・`double`・`bool`・`DateTime`・`Guid`・enumなどをinvariant cultureで読み、不正な値と`required`の欠落は`JsonSerializationException`です。通常のメソッドは同じ検査を行い`Data`だけを返します（headerを宣言したoperationだけが対象で、ほかのoperationに`WithHttpInfo`は生成しません）。header objectの`content`、objectのheader、`style: simple`以外は`OACG101`、`Content-Type`は仕様どおり`OACG108`で無視します。エラーresponseのheaderは検証だけで公開しません。`headers`はobjectが必要で、scalar・array・nullは位置付き`OACG100`です。 |
| `components.headers` | 対応 | `components.headers`と、responses・`encoding`のheaderからの`#/components/headers/<name>`参照を解決します（参照先は通常のHeader Objectと同じ規則で検証します）。 |
| Response Objectの項目 | 一部対応 | `description`・`headers`・`content`・`links`・`x-*`拡張（3.2では`summary`も）だけを認識します。`contents`などの未知項目は、成功・エラー・参照先の応答でも位置付き`OACG101`です。 |
| webhooks / callbacks / links | 一部対応 | root の`webhooks`、operation の`callbacks`、response の`links`（と`components.links`／`components.callbacks`）は受理して無視し、その要素自身の logical path（`/webhooks`、`/paths/~1pets/post/callbacks`、`/paths/~1pets/post/responses/201/links`、`/components/links`、`/components/callbacks`）で`OACG108`を報告します。client のメソッドやメンバーは生成しません。空の map は報告しません。無視した内容は**解析しません**。ノード自身が object であること（違えば通常のエラー）だけを確認するため、その内側の解決できない・external・未対応の`$ref`が`OACG102`や`OACG101`になることはなく、Editor もそこにある external `$ref`を収集・bundle しません。そこから参照される schema の DTO は、`components.schemas`に宣言されている場合だけ生成します（`components.schemas`は常に生成するため、callback から`#/components/schemas/Event`を参照する形は対象です）。callback や webhook の中だけに現れる inline schema と、そこでの external `$ref`だけで到達する schema は型を生成しません。 |
| components の各 section | 一部対応 | `schemas`、`parameters`、`requestBodies`、`responses`、`headers`、`securitySchemes`、`pathItems`（3.1）、`examples`、`links`、`callbacks`、`x-*`を受理します。`examples`はドキュメントだけなので報告せず、`links`と`callbacks`は`OACG108`です。それ以外の section（綴り間違いなど）は従来どおり`OACG101`です。 |
| ドキュメント専用の要素 | 対応 | root の`tags`・`externalDocs`、既定の`jsonSchemaDialect`、operation の`tags`・`externalDocs`、media type・parameter・header の`example`／`examples`（header の`example`は応答 header の固定値として引き続き使います）、あらゆる schema の`xml`、`x-*`拡張は、`description`と同様に黙って受理します。生成コードは変わりません。 |

status codeはASCII数字で検証します。属性付きのジェネリックclassは`OACG005`で拒否します。

## Security

| 項目 | 対応状況 | 条件・制約 |
| --- | --- | --- |
| `components.securitySchemes` | 対応 | `apiKey`（`in`は`header`／`query`／`cookie`、`name`必須。header・cookieの`name`はHTTP tokenに限り、headerの`name`が`Cookie`または`Content-*`（大小文字を問わない）の場合は`name`の位置付き`OACG100`です）、`http`（`scheme`必須。`bearer`／`basic`／その他の任意のscheme）、`oauth2`（`flows`と各flowの必須URL・`scopes`）、`openIdConnect`（`openIdConnectUrl`必須）、`mutualTLS`（OpenAPI 3.1以降）を解析します。scheme自体の`$ref`（internal）も辿ります。必須項目の欠落、`in`の不正値、未知の`type`、未知のoauth2 flow、3.0の`mutualTLS`は不正な項目の位置付き`OACG100`です。 |
| security requirement | 対応 | root `security`とoperation `security`を評価します。operationの`security`はrootを上書きし、`security: []`はそのoperationの認証をなくします。配列はOR、1つのobjectに並べた複数のschemeはANDです。`{}`（匿名可）は認証を要求しないrequirementとして扱い、他のrequirementより優先しません。未定義のschemeを参照すると、その参照の位置付き`OACG100`です。scopesは文字列配列として検証し、XML docにだけ出力します（送信前に検証・取得しません）。OpenAPI 3.1の「scheme名をURIで参照する」形は未定義schemeとして`OACG100`です。 |
| 資格情報API | 対応 | schemeごとにclientのpublicプロパティを生成します。名前は`components.securitySchemes`のキーをPascalCaseにした後ろに種別の語を付けます（衝突時は数字を付けて一意にします）。`apiKey`: `string? <Scheme>ApiKey`。`http bearer`／`oauth2`／`openIdConnect`: `string? <Scheme>AccessToken`と`Func<CancellationToken, Task<string?>>? <Scheme>AccessTokenProvider`。`http basic`: `string? <Scheme>Username`と`string? <Scheme>Password`。その他のhttp scheme: `string? <Scheme>Credentials`。`mutualTLS`は何も生成しません。XML docにschemeの`description`、`bearerFormat`、oauth2のflowとscopes、`openIdConnectUrl`を出力します。簡略名（`BearerToken`など）は生成しません。 |
| requirementの適用 | 対応 | 各operationの有効なrequirementを文書順に調べ、含まれるすべてのschemeに資格情報が設定済みの最初のrequirementを適用します（送信ごとに評価します）。どのrequirementも満たせないとき、および`{}`だけのときは認証なしで送ります（例外にはしません）。`apiKey`／`bearer`／`basic`／その他のhttp schemeは、nullまたは空の資格情報を未設定として扱います（`basic`は`Username`が`null`のときだけ未設定で、`Password`が`null`なら空のpasswordです）。 |
| `apiKey` の送信 | 対応 | header: `name`のheaderとして送信します。query: 既存のquery parameterの後ろに`name=value`（名前と値をpercent-encode）を足します。cookie: parameterのcookieと合わせて**1本の`Cookie`ヘッダー**にします（値はpercent-encode）。同名のheader parameterがあっても、そのschemeに資格情報が設定されているときはsecurityの値が優先します（設定されていないときはparameterの値を送ります）。cookieも同様で、cookie apiKeyに資格情報が設定されているときは同名のcookie parameterの部分を除き、その名前は`Cookie`ヘッダー内で1回だけ送ります。 |
| `http` の送信 | 対応 | `bearer`: `Authorization: Bearer <token>`。`basic`: `Authorization: Basic <base64(username:password)>`（UTF-8でバイト列にしてからBase64。`Username`に`:`を含むと`InvalidOperationException`です）。その他のscheme: `Authorization: <scheme> <credentials>`（`scheme`は仕様に書かれた表記のまま）。Authorizationが複数のschemeから出るANDでは最後のschemeが勝ちます。送信するheader値は既存のheader parameterと同じ規則（ASCII）で検査し、違反は`ArgumentException`です。 |
| `oauth2` / `openIdConnect` | 一部対応 | 外から渡されたtokenをBearerとして送るだけです（OpenAPI 3.2の`deviceAuthorization` flowと`oauth2MetadataUrl`も同じ扱いで、下の「OpenAPI 3.2」を参照）。flowによるtoken取得、更新、OpenID Connect discoveryは行いません（URLやscopesはXML docのみ）。tokenは`<Scheme>AccessToken`に設定するか、`<Scheme>AccessTokenProvider`で供給します。providerは設定されていればプロパティより優先し、送信ごとにそのリクエストの`CancellationToken`付きで呼びます。providerが`null`または空を返したときはプロパティを使います。 |
| `mutualTLS` | 一部対応 | 受理しますが、クライアント証明書の設定はclientではなく`HttpClientHandler`で行います（XML doc・生成コードでは何も送りません）。送るものがないため、`mutualTLS`だけのrequirementは匿名と同じ扱いで、他のrequirementを妨げません。このためOACG108のWarningは出しません。 |
| 資格情報の取り扱い | 対応 | 資格情報プロパティはスレッドセーフではありません（XML docに明記）。リクエストごとに1回だけ読みます。生成コードはログを出さず、例外メッセージに資格情報を含めません（header名とscheme名だけを使います）。 |

## スキーマ

以下は生成するrequest／成功response／DTOの制限です。非2xxと`default`の本文は構造・参照検証の対象です。

| 項目 | 対応状況 | 条件・制約 |
| --- | --- | --- |
| scalar | 対応 | `string`、`integer`、`number`、`boolean`。 |
| object | 対応 | named propertiesと`required`を持つobjectをDTOへ生成します。`additionalProperties`の省略または`true`はopen objectで、宣言外のpropertyを`[JsonExtensionData]`の`IDictionary<string, JToken>`メンバー（既定名`AdditionalProperties`、宣言済みpropertyと重なる場合は連番）に保持して再送し、成功応答の未知propertyも許可します。schema値の`additionalProperties`はpropertiesがあればopen objectとして同じく保持し、成功応答では各値をその型で検査します（値は`JToken`のまま取り出し、`ToObject<T>()`で変換します）。`additionalProperties: false`はclosedで、成功応答の未知propertyを拒否します。propertiesがなく`false`なら空のclosed DTOです。 |
| array | 一部対応 | supported schemaのarray。 |
| string / integer enum | 一部対応 | generated C# enumは宣言したwire値だけを完全一致で読み書きします。string enumは大小文字・空白も含む文字列を、integer enumは明示値付きのC# enum（値が`int`を超えれば`long`）と数値のwireを使い、JSON文字列・小数（`2.0`や`1e0`のような整数値の字句も）・未宣言値・未宣言のC# enum値を拒否します（整数トークンだけを受理）。`type`を省略したenumは値の型（すべてstringまたはすべてinteger）から推論します。stringとintegerの混在、number enum、boolean enumは`enum`の位置で`OACG101`です。`enum`に`null`を含むとnullable（`Priority?`）として扱います。`x-enum-varnames`／`x-enumNames`（前者を優先）はC#のmember名、`x-enum-descriptions`はmemberのXML docに使い、長さ不一致などの不正な指定は`OACG108`で無視します。名前のないinteger enumのmemberは`Value<n>`（負数は`ValueMinus<n>`）です。OpenAPI 3.1の`type: [string, null]`でも`enum`に`null`が無ければ非nullableです。重複値は`OACG101`です。 |
| direct schema reference | 対応 | supported schemaへの参照。多段参照のscalar／enumもnullableを保持します。 |
| `$ref` sibling | 一部対応 | 注釈、literalデータ、`x-*`と3.0の`nullable`は保持して参照のままです（3.1で`type`だけ、または`not`を併記した場合も参照のままです）。OpenAPI 3.1で`properties`、`required`、`additionalProperties`、`allOf`などの構造を併記すると`allOf: [ {$ref}, {siblings} ]`として扱い、下の`allOf`の規則で1つのDTOへ平坦化します。3.0では従来どおり、合成が必要な兄弟keywordを該当位置の`OACG101`で拒否します。 |
| nullable | 一部対応 | OpenAPI 3.0の`nullable`、OpenAPI 3.1の`type: [<type>, null]`。 |
| optional nullable DTO property | 対応 | 未代入はJSONから省略し、`null`代入は明示的なJSON `null`、値の代入はその値を送ります。生成される`<PropertyName>Specified`を`false`へ戻すと再び省略します。 |
| optional non-nullable DTO property | 対応 | JSONで省略できますが、明示的なJSON `null`を受信すると`JsonSerializationException`です。未設定値をシリアライズすると省略します。 |
| required non-nullable DTO property | 対応 | 参照型の必須項目が`null`ならシリアライズ中に検出し、リクエストを送信しません。入れ子のDTOにも適用します。必須nullable項目のJSON `null`と必須値型の既定値は送信できます。 |
| scalar format | 一部対応 | `integer`は通常`int`、`int64`は`long`。`number`は通常`double`、`float`は`float`、`decimal`は`decimal`。`date`、`date-time`、`uuid`は`DateTime`、`DateTimeOffset`、`Guid`へmappingします。成功レスポンスでは変換前にそれぞれ日付、タイムゾーン付き日時、ASCIIの8-4-4-4-12形式を字句検査します。字句が正しくてもCLRで表せない時刻・うるう秒などは変換時に拒否される場合があります。 `byte`は`byte[]`にmappingします（成功応答はbase64を検査）。 |
| formatの綴り | 一部対応 | `int64`、`float`、`decimal`、`date`、`date-time`、`uuid`、`byte`、`binary`は小文字の完全一致だけを特別扱いします。大文字を含む未知の綴りはschemaの基底型として扱います。 |
| リクエスト数値 | 対応 | `float`／`double`の`NaN`・正負の無限大は本文直下・配列・DTO内ともHTTP送信前に`JsonSerializationException`で拒否します。省略指定した任意項目は検査しません。 |
| map / free-form object | 対応 | propertiesがなく`additionalProperties`がschemaなら`Dictionary<string, T>`です。成功応答では各値を検査し（非nullableな参照型のnull、型不一致、Json.NETの`$id`／`$ref`／`$type`／`$values`キーを拒否）、requestでは値の型に応じたnull・非有限数を検査します。propertiesがなく`additionalProperties`が省略または`true`の`type: object`（free-form）は`JObject`です。 |
| any / type inference | 対応 | `{}`、OpenAPI 3.1のboolean schema `true`、`type: "null"`、およびnull以外の型を複数持つ3.1の型配列（例: `["string","integer"]`）は`JToken`です（JSON `null`を受け付けるため常にnullable）。`type`を省略した場合は、`properties`／`additionalProperties`があればobject、`items`があればarray、`enum`の値からstring／integer、`const`の値からその型を推論します。boolean schema `false`は`OACG101`です。parameterの`{}`や複数型は`OACG101`のままです。 |
| schema assertions（検証キーワード） | 一部対応 | 下の各行のキーワードを実行時に検証します。生成する型は変わりません。`if`／`then`／`else`、`dependentSchemas`、`unevaluatedProperties`／`unevaluatedItems`、`contentSchema`、未知のキーワードは、そのkeywordの位置で`OACG101`です。`oneOf`／`anyOf`と同じschemaに併記した検証キーワードも`OACG101`です（unionのメンバーへ移してください）。3.1のみのキーワード（`patternProperties`・`propertyNames`・`contains`・`minContains`・`maxContains`・`prefixItems`・`dependentRequired`）は3.0では`OACG101`です。型に当てはまらないキーワード（integerの`minLength`など）はJSON Schemaどおり無視します。値が不正（負の`minLength`、0以下の`multipleOf`、3.0で数値の`exclusiveMinimum`、3.1でbooleanの`exclusiveMinimum`、不正な正規表現など）なら位置付き`OACG100`です。`format`は従来どおり受理するだけで検証しません（`date`・`date-time`・`uuid`の既存の解析は変わりません）。`enum`に併記したキーワードはwire値（stringまたはintegerの値）に対して評価します。ファイルpart（`format: binary`）に併記したキーワードは評価できないため、そのkeywordの位置で`OACG108`です。3.1の`$ref`に併記したキーワードは、`$ref`先の型を変えずにその参照位置の制約として追加します（`allOf: [{ $ref }]`との併記も同様）。3.0は従来どおり`$ref`のsiblingを許可せず`OACG101`です。 |
| string: `minLength` / `maxLength` / `pattern` | 対応 | 長さはUTF-16のchar数ではなくUnicodeコードポイント数で数えます（サロゲートペアは1文字）。`pattern`は部分一致（アンカーなし。固定するなら`^`・`$`）です。.NETの`Regex`を生成時に決めたモードで評価します。ECMA-262にない構文（原子グループ`(?>`・インラインオプション`(?i)`・`\A` `\z` `\Z` `\G`・文字クラス減算など）を含むpattern、またはECMAScriptモードで作れないpatternは、既定の.NET意味論（`CultureInvariant`。`\d`・`\w`・`\s`がUnicode対応）で評価し、そのpatternの位置で`OACG108`を報告します。それ以外は`RegexOptions.ECMAScript`（`\d`・`\w`・`\s`・`\b`がASCII基準でECMA-262に近い）です（両オプションは併用できないためモードは1つです）。差異: `$`は.NETでは末尾の改行の前にも一致します（ECMA-262は文字列末尾だけ）。ECMAScriptモードの`\s`はASCII空白だけで、NBSPなどのUnicode空白を含むECMA-262の`\s`より狭くなります。lookbehindと名前付きグループは.NETにもECMA-262（ES2018）にもありますが、ECMAScriptモードでは細かな挙動がedge caseで異なることがあります。どちらのモードでも作れないpatternは生成時に位置付き`OACG100`です。`Regex`は生成クラスの`static readonly`フィールドとして1回だけ作ります（pattern単位で共有）。`RegexOptions.Compiled`はIL2CPPなどAOT環境でILを出力できないため使いません。照合timeoutは1秒で、超えると`RegexMatchTimeoutException`を内部例外に持つ通常の検証エラー（requestは`ArgumentException`、responseは`JsonSerializationException`）です。length／patternの対象は`string`、`format: byte`（base64文字列）、`uuid`です。`date`・`date-time`のstringはrequestでは日付型として直列化されるため長さとpatternを検査しません（応答は文字列として検査します）。XML docに`Must be 3..50 characters.`のような制約文を出力します。 |
| number: `minimum` / `maximum` / `exclusiveMinimum` / `exclusiveMaximum` / `multipleOf` | 対応 | OpenAPI 3.0はboolean形式（`minimum`と`exclusiveMinimum: true`で排他的な下限）、3.1は数値形式（`exclusiveMinimum: 0`。`minimum`と併用可）です。値はdecimalに収まれば`decimal`で、収まらなければ`double`で比較します。`double`の値はまず有効15桁に丸めるため、`0.1 + 0.2`は`0.3`として`multipleOf: 0.1`を満たし、`multipleOf: 0.01`の`19.99`も誤差なく判定します。decimalに収まらない値（絶対値が約7.9e28超・1e-27未満）の`multipleOf`は、商が整数から相対1e-9以内なら倍数とみなす許容誤差を使います。対象は`integer`／`number`のJSON値です（`int`・`long`・`float`・`double`・`decimal`）。 |
| array: `minItems` / `maxItems` / `uniqueItems` | 対応 | 要素数と一意性を検査します。`uniqueItems`はJSONの値として比較します（`1`と`1.0`は同じ、objectはメンバーの順序によらず同じ、arrayは位置ごと）。同じ値はハッシュで絞り込むため、要素が多くても二乗の比較にはなりません。 |
| object: `minProperties` / `maxProperties` | 対応 | JSONのmember数を検査します。open DTO（`[JsonExtensionData]`）は宣言propertyと追加memberの合計、`Dictionary<string, T>`とfree-form object（`JObject`）はその要素数です。requestではシリアライズ後のJSONを数えるため、readOnly propertyと未設定のoptional propertyは含みません。 |
| `patternProperties` / `propertyNames` / `dependentRequired`（3.1） | 一部対応 | `patternProperties`は、名前が`pattern`（部分一致）に一致するmemberの値をそのschemaで検査します（宣言propertyにも適用。mapとopen objectの追加memberの型は変わりません）。`propertyNames`は各memberの名前を、`dependentRequired`は「指定propertyがあれば列挙したpropertyも必要」を検査します。メッセージに含めるkeyは32文字に切り詰めます。subschemaが自分自身を（DTOを介さずに）参照する循環は`OACG103`です（DTOを介する再帰は可）。allOfの各partが持つobject用のキーワード（`minProperties`・`maxProperties`・`patternProperties`・`propertyNames`・`dependentRequired`）は部品ごとに保持し、すべて満たす必要があります。 |
| `contains` / `minContains` / `maxContains` / `prefixItems`（3.1） | 一部対応 | `contains`はschemaに一致する要素数を数え、`minContains`（既定1。0可）以上、`maxContains`以下を要求します。`prefixItems`は位置ごとのschemaで検査し、`items`は`prefixItems`より後の要素に適用します（`items: false`は`maxItems`を`prefixItems`の個数にします）。`prefixItems`を持つarrayの型は`items`の型ではなく`List<JToken>`です（要素の型が位置ごとに異なり、`items`の型では読めないため）。 |
| 検証の範囲と例外 | 一部対応 | requestは送信前に検証し、違反は`ArgumentException`（`ParamName`はparameter名またはbody引数名）でHTTPは送信しません（fake handlerは呼ばれません）。対象はJSON body（入れ子のDTO・配列要素・map値を含む）、form-urlencodedとmultipartのfield（ファイル以外）、text body、parameter（path・query・header・cookie・`content`）です。responseは受信時に検証し、違反は`JsonSerializationException`です。対象はJSON本文、response header、text本文で、`oneOf`／`anyOf`の候補判定と`not`の判定にも制約が効きます（制約で候補が決まります）。メッセージにはパス（`$.items[2].name`）とkeywordを含め、値そのものは含めません。readOnly propertyはrequestで検証せず、writeOnly propertyが応答に欠落しても許可します（存在すれば検証します）。securityの資格情報は対象外です。メッセージのパスにはmember名（データ由来）が入りますが、値や`propertyNames`違反のkeyは含めません。検証が行わない・制限される箇所のまとめ: requestでは`not`とunion（`oneOf`／`anyOf`）の候補値の制約を評価しません（応答だけ）。`oneOf`／`anyOf`と同じschemaに併記した検証キーワードは`OACG101`です（メンバー側へ移す）。`uniqueItems`は`1`と`1.0`を同じ値とみなします。 |
| `const` | 一部対応 | OpenAPI 3.1のscalar const（string／integer／number／boolean）。成功応答では値の一致を検査し（integer／numberは数値として比較するため`2.0`は`2`と等価）、不一致は`JsonSerializationException`です。request DTOでは、string／整数／浮動小数／decimal／booleanのDTO propertyの初期値になるだけで、request側で値は検証しません（nullableな任意propertyは初期値の対象外）。object／array値は`OACG101`、schema型と合わない値は`OACG100`です。`null`や単一のscalar型を持たないschemaのconst、`enum`と併記したconstは`OACG108`で無視します。 |
| description / title / deprecated / default | 対応 | `title`と`description`はDTO・property・enum（とmember）のXML docへ出力します。`deprecated: true`はschemaとpropertyに`[Obsolete]`を付けます（生成コード内は`#pragma warning disable CS0612, CS0618`）。`default`はXML docだけで、wire contractは変えません（object／arrayのdefaultは無視）。operationの`deprecated: true`は生成メソッドへ`[Obsolete]`を付け、`summary`は`<summary>`、`description`は`<remarks>`に出します。parameterの`deprecated`と`description`は`<param>`のXML docに反映します（C#の引数には`[Obsolete]`を付けられません）。 |
| `allOf` | 一部対応 | 各partの`properties`・`required`・`additionalProperties`を1つのsealed DTOへ平坦化します（`$ref`先は再帰的に展開し、入れ子の`allOf`も可）。`required`は和集合です。**closednessは合成後に評価**します。どれか1つのpart（または3.1の`$ref`siblings）が`additionalProperties: false`ならDTO全体をclosedにし、許可するpropertyは全partのpropertiesの和集合です（partごとの`false`を単純合算して、他のpartのpropertyを拒否することはしません）。`additionalProperties`がschemaのpartがあればtyped open（複数あれば同じschemaが必要）、それ以外はopen objectです。同名propertyの型がpart間で異なれば`OACG101`（2つ目の宣言の位置、1つ目はadditional location）、同じ型なら統合します。partが`object`以外の`type`・`items`・`enum`・`const`を持つ場合、partが`oneOf`／`anyOf`を持つ場合（discriminator付きのbaseを除く）は`OACG101`、どのpartも宣言しない`required`は`OACG100`、`allOf`の循環は`OACG103`です。part内の`not`は`OACG108`で無視します。`allOf: [ {$ref} ]`の1要素に`description`・`nullable`などの注釈だけを併記する定番の形は、新しい型を作らない参照です（nullableも保持）。 |
| `oneOf` / `anyOf` + discriminator | 一部対応 | discriminator付きの`oneOf`／`anyOf`は、sealedなunionラッパークラス（例: `Pet`）とメンバーのDTO（`Cat`、`Dog`）を生成します。メンバーのDTOはラッパーを継承しない通常のsealed DTOです。ラッパーに付ける`JsonConverter`が具象DTOの書き出しで再入して無限再帰することはありません。ラッパーは`FromCat(Cat)`・暗黙変換・`TryGetCat(out Cat)`・`AsCat()`、保持している具象値`Value`、選択値`DiscriminatorValue`を持ちます。メンバーは`$ref`でobjectを指す必要があります（inline、またはobject以外は、メンバーの位置の`OACG101`）。discriminator値は`mapping`（値はschema名、`#/components/schemas/X`のinternal pointer、または`oneOf`内の`$ref`と同じ文字列）と、mappingのないメンバーの暗黙値（schema名）で解決します。メンバーにmappingがあればその値だけが有効で、複数あれば最初の値を送ります。他ファイルを指すmapping、メンバーでないschemaを指すmappingは`OACG101`、複数のメンバーに同じ値を割り当てると`OACG100`です。メンバーDTOがdiscriminator propertyを宣言していなければ生成側が任意のstring propertyとして追加し、選択値が1つならDTOの初期値にします（宣言済みのstring propertyにも同じ初期値を付けます）。この初期値は検証せず、requiredにもしないため、unionを経由しない単独利用のDTO（discriminatorを持たない応答など）は有効なままです。discriminatorの有無と値の検査はunion（converterと成功応答の契約検査）だけが行います。requestはunionが常に選択値を書きます。成功応答は値でメンバーを選び、未知・欠落・stringでない値は`JsonSerializationException`で、選んだメンバーのDTO検査（closedなら未宣言property、必須property）も行います。ラッパーの`JsonConverter`クラスにはIL2CPP用の`PreserveAttribute`を付けます。 |
| discriminator付きbase（継承） | 一部対応 | `oneOf`／`anyOf`を持たず`discriminator`だけを持つschema（子が`allOf: [ {$ref: base}, … ]`で継承する形）は、mappingの対象と、同じdocumentの`components.schemas`で`allOf`にbaseを直接`$ref`しているschema（孫まで）をメンバーとするunionになります。baseの`properties`は子のDTOへ`allOf`で平坦化されます。base自身は（値が`Pet`などでも）メンバーにならず、そのdiscriminator値は未知の値として拒否します。子もmappingもない`discriminator`は`OACG108`で無視します。他のdocumentのschemaは子として探しません。`oneOf`＋discriminatorを持つschemaを子が`allOf`で継承する形では、子はそのschemaの`properties`だけを引き継ぎ、`oneOf`は引き継ぎません。 |
| `oneOf` / `anyOf`（discriminatorなし） | 一部対応 | `type: null`だけの部品を除いた候補が1つなら、その型のnullableに正規化します（`[X, null]`）。それ以外は成功応答の生の`JToken`を保持するunionラッパーです（`FromJToken(JToken)`、`ToJToken()`、候補ごとの`FromX(T)`・暗黙変換・`TryGetX(out T)`・`AsX()`。string／integerなどのプリミティブ候補も扱います。アクセサ名は型名で、`String`、`Int32`、`CatList`などです）。成功応答は候補ごとに既存のJSON契約検査を試し、`oneOf`はちょうど1件、`anyOf`は1件以上の一致を求めます（違反は一致件数つきの`JsonSerializationException`）。一致は構造検査（型、closed DTOの未宣言property、必須property、enumの値、const）で決めます。`oneOf`で複数の候補が一致した場合は、応答JSONのすべてのpropertyを宣言している候補（closedとみなした一致）に絞り込み、ちょうど1件ならそれを選びます（open DTO同士が重なる典型ケース）。絞り込んでも1件にならなければエラーです。`integer`と`number`のように区別できない`oneOf`や、同じpropertyだけを持つ候補は失敗し得るので、`required`・`additionalProperties: false`・`discriminator`で区別できるようにしてください。`anyOf`は絞り込まず1件以上の一致で足ります。`TryGetX`も同じ規則で候補を選びます。`TryGetX`は、`FromX`で渡した値か、保持しているJSONをJson.NETの厳密な設定（未宣言memberはエラー）で変換できるかで判定します。requestは渡された候補値または`JToken`をそのまま直列化します（候補値のrequest検査は行いません）。同じschemaへ`properties`／`required`など形を決めるkeywordを併記する、`allOf`と`oneOf`、`oneOf`と`anyOf`を併記する場合は`OACG101`です（keywordはメンバー側へ移します）。unionのparameterも`OACG101`です。 |
| `not` | 一部対応 | 成功応答の検査だけで評価し（`not`のschemaに一致した値を`JsonSerializationException`で拒否）、生成する型は変えません。schema・property・`$ref`先のどこの`not`も対象で、一致は構造検査（型、enum値、const、DTOの形）で決めます。`required`だけのような形を持たない`not`、boolean schema、再帰する`not`、未対応の要素を含む`not`は、全値を拒否してしまうか判定できないため`OACG108`で無視します。requestでは評価しません。 |
| readOnly / writeOnly | 対応 | `readOnly`なDTO propertyはrequestへ送信せず（生成される`ShouldSerialize<Name>()`が`false`）、requestのrequired検査からも外します。成功応答ではschemaの`required`どおり必須です。`writeOnly`なpropertyは成功応答で欠落しても許可し、requestではrequiredのままです。`$ref`先のschemaに付いた`readOnly`／`writeOnly`も、参照するpropertyに適用します。readOnly propertyの値はrequestで検証もしません。Unity IL2CPPのmanaged strippingでreflection専用メンバーが削除されないよう、`ShouldSerialize<Name>()`と`AdditionalProperties`にはinternalな`PreserveAttribute`を付け、`ShouldSerialize<Name>()`は`EditorBrowsable(Never)`にします。 |
| `format: byte` | 対応 | `byte[]`（Json.NET標準のbase64）へmappingします（従来は`string`）。成功応答は文字列かつ有効なbase64であることを検査し、path/query/header parameterは標準base64文字列で送ります。 |
| `format: binary` | 一部対応 | multipartのファイルpart（`<ApiName>FileParameter`）、バイナリのrequest本文（`byte[]`）、バイナリ応答（`byte[]`）で使えます。JSONの本文・DTO（JSON応答を含む）・parameter・header・form-urlencodedの値で参照されると、binaryのschemaの位置で`OACG101`です（binaryを含むschemaをcomponentsに置くことは可能で、使われた場所で判定します）。3.1の`type: string`＋`contentMediaType`（`contentEncoding`なし、JSON・text・xml以外）も同じ扱いです。大文字小文字の違う`format`は従来どおり通常のstringです。 |

`$ref`と併記できる注釈・データのキーは`$comment`、`default`、`deprecated`、
`description`、`example`、`examples`、`externalDocs`、`readOnly`、`summary`、`title`、`writeOnly`、`xml`、`x-*`です。
OpenAPI 3.0では既存互換として`nullable`も扱います。

リクエストJSONの`format: date`は`yyyy-MM-dd`へ変換します。公開型の`DateTime`は維持し、
`date-time`の`DateTimeOffset`にはdate用converterを適用しません。

## OpenAPI 3.2

OpenAPI 3.2のdocument（`openapi: 3.2.*`）は、3.1と同じJSON Schema 2020-12 dialectとschema規則で処理します。3.2だけの項目は3.0／3.1では従来どおり診断になります（`query`は`OACG101`など）。

| 項目 | 対応状況 | 条件・制約 |
| --- | --- | --- |
| Path Itemの`query` | 対応 | 安全なmethodで本文を持ちます。`new HttpMethod("QUERY")`で送り、requestBody・parameter・responseは他のoperationと同じです。`operationId`の省略時は`GET`などと同じ規則で命名します（`QueryPetsSearch`）。3.0／3.1の`query`は位置付き`OACG101`です。 |
| Path Itemの`additionalOperations` | 対応 | method名（HTTP token）からOperationへのmapで、各entryにクライアントメソッドを生成し、methodは**書かれたとおりの表記**（大小文字を含む）で送ります。tokenがHTTP tokenでない場合、`GET`・`PUT`・`POST`・`DELETE`・`OPTIONS`・`HEAD`・`PATCH`・`TRACE`・`QUERY`（大小文字を区別せず比較）と重複する場合は、そのentryの位置で`OACG100`です。`operationId`がなければ、method token（英数字だけを残し、先頭だけ大文字）とpathから決定的に命名し（`PURGE /pets`は`PurgePets`）、entryはtokenの序数順に処理します。名前の衝突は既存の`operationId`の一意化（連番）に従い、明示した`operationId`の重複は`OACG106`です。 |
| `in: querystring` parameter | 一部対応 | `content`が**クエリ文字列全体**を表します（`schema`との併記は`OACG100`、`content`は1つのmedia type）。`application/x-www-form-urlencoded`はobject schemaで、各propertyをform-urlencoded本文と同じ規則（`encoding`の`style`／`explode`／`allowReserved`）で直列化し`?`の後ろに付けます。具体的なJSON media typeは、JSON textをpercent-encodeしたものがクエリ文字列です。それ以外のmedia typeは`OACG101`です。1つのoperationに複数の`in: querystring`、または`in: query`との併記は、`OACG105`です。media typeは`components.mediaTypes`への`$ref`でも宣言できます。 |
| tagsの`parent`／`kind`／`summary` | 対応 | ドキュメント専用で、生成コードは変わりません。形式（文字列）と、`parent`が宣言済みのtagを指し循環しないことだけを検証します（違反は`OACG100`）。tag名の重複も`OACG100`ですが、これは3.2だけの検査で、3.0／3.1では検査しません。 |
| root `$self` | 一部対応 | 受理します。形式は文字列で、fragmentを持てません（違反は`OACG100`）。内部の`$ref`は基準URIに依存しません。他のdocumentへの相対`$ref`は、`$self`ではなくdocumentファイルの位置を基準に解決するため、参照先のdocumentがある場合（Bundleが複数documentのとき）だけ`$self`の位置で`OACG108`を報告します。 |
| media typeの`itemSchema` | 一部対応 | streaming（SSEと逐次処理）は対象外です。`text/event-stream`に`itemSchema`があれば`OACG101`、順次media type以外（`application/json`など）の`itemSchema`も`OACG101`です。`application/jsonl`・`application/x-ndjson`・`application/ndjson`・`application/json-seq`・`application/geo+json-seq`の`itemSchema`は解釈せず`OACG108`で通知し、本文は従来どおり`byte[]`（binary）です（itemごとの読み取りは行いません）。`itemSchema`は解析しないため、その中の`$ref`はEditorも収集せず、DTOも生成しません。Media Type Objectで`encoding`と`prefixEncoding`／`itemEncoding`を併記すると`OACG100`です（仕様上の禁止）。単独の`prefixEncoding`／`itemEncoding`（Media Type Object）と、Encoding Objectの入れ子の`encoding`／`prefixEncoding`／`itemEncoding`は`OACG108`で無視します。`itemSchema`は3.0／3.1では従来どおり未知の項目として無視します。 |
| discriminatorの`defaultMapping` | 対応 | discriminatorの値がmappingのどれにも一致しない、またはdiscriminatorの項目がない・文字列でないときに使うメンバーです。値はスキーマ名または参照で、`oneOf`／`anyOf`のメンバー（継承形のbaseでは子または参照先のschema）でなければ`OACG101`です。変換器は該当するJSON objectをそのメンバー型へ読みます（応答の契約検査も同じ。メンバー自身のschemaに合わない本文は従来どおり拒否します）。指定がなければ、従来どおり未知の値は`JsonSerializationException`です。書き出す値はそのメンバーの最初のdiscriminator値で、読んだ未知の値は保持しません。3.0／3.1では従来どおり無視します。 |
| `components.mediaTypes` | 一部対応 | `content`の各media typeを`$ref: '#/components/mediaTypes/<name>'`で参照できます（requestBody・成功／エラーresponse・`in: querystring`のcontent。他のdocumentの`<file>#/components/mediaTypes/<name>`も可）。componentはobjectと、あればそのschemaを検証し、参照元の`content`のキーがmedia typeを決めます。通常のparameterの`content`からの`$ref`は`OACG101`です。3.0／3.1の`components.mediaTypes`は`OACG101`です。 |
| `oauth2`の`deviceAuthorization` flowと`oauth2MetadataUrl` | 一部対応 | 受理し、URL・scopesをXML docに出すだけです（`deviceAuthorizationUrl`と`tokenUrl`が必須、なければ`OACG100`）。tokenの取得・更新とmetadataの取得は行わず、外から渡されたtokenをBearerで送ります。security schemeの`deprecated`は形式だけ検証します。3.0／3.1の`deviceAuthorization`は未知のflowとして`OACG100`です。 |
| Responseの`summary`と省略可能な`description` | 対応 | 3.2ではResponseの`summary`を受理し（ドキュメント専用）、`description`を省略できます。3.0／3.1の`summary`は`OACG101`、`description`の省略は`OACG100`です。 |
| その他の3.2の項目 | 対応 | Serverの`name`、Exampleの`dataValue`／`serializedValue`、XML Objectの`nodeType`、Tag Objectの`summary`は、ドキュメント専用として黙って受理します。 |

## Swagger 2.0

Swagger 2.0のdocument（`swagger: "2.0"`）は、意味解析の前にOpenAPI 3.0（`3.0.3`）の等価なtreeへ正規化してから、3.0と同じ処理（parser、生成model、出力、診断、実行時検証）に渡します。専用の生成器はなく、生成されるクライアントは、同じ内容を手で書いたOpenAPI 3.0 documentのものと一致します。YAMLのSwagger 2.0も同じです。`openapi`と`swagger`を併記したdocument、`"2.0"`以外の`swagger`の値（`"1.2"`、数値の`2.0`など）は、`swagger`の位置で`OACG101`です。

| 項目 | 対応状況 | 条件・制約 |
| --- | --- | --- |
| `host`・`basePath`・`schemes` | 一部対応 | `servers`になります。`schemes`の各値が1つのserverで、`https://<host><basePath>`の形です（`schemes`の順序が`servers`の順序です）。`schemes`がなければ`https`、`basePath`が`/`または省略なら末尾の`/`なし、`basePath`の末尾の`/`は除きます。`host`がなければ相対serverで、`basePath`（なければ`/`）だけです（`schemes`は使いません）。`host`も`basePath`も`schemes`もなければ`servers`は付けません。operationの`schemes`は、`host`があればそのoperationのserversになります（`host`がなければ相対serverに適用できないため、`schemes`の位置で`OACG108`を報告して無視します）。`ws`／`wss`は他の非HTTP(S) serverと同じく診断です。 |
| `definitions`・`parameters`・`responses`・`securityDefinitions` | 対応 | それぞれ`components.schemas`・`components.parameters`・`components.responses`・`components.securitySchemes`になります。`parameters`のうち`in: body`／`in: formData`は、使う側のoperationの`requestBody`になるため、componentにはなりません（使われないbody／formDataのparameterは検証しません）。 |
| `$ref` | 対応 | `#/definitions/X`は`#/components/schemas/X`、`#/parameters/X`は`#/components/parameters/X`、`#/responses/X`は`#/components/responses/X`に書き換えます。外部の`file.json#/definitions/X`も同じです（Editorは3.0と同じ規則で参照先のdocumentを集めます）。`#/definitions/`以外（schema）、`#/parameters/`・`#/responses/`以外（それぞれのparameter・response）を指す`$ref`、Path Itemの`$ref`は`OACG101`です。`$ref`の兄弟項目は3.0の規則（`description`、`x-nullable`→`nullable`など）に従います。 |
| 複数document（Swagger 2.0同士） | 一部対応 | Swagger 2.0のdocumentからSwagger 2.0のdocumentの`definitions`・`parameters`・`responses`を参照できます。参照先がOpenAPI 3.xのdocument、`openapi`も`swagger`も持たないbare schemaのfile、また3.xのdocumentからSwagger 2.0のdocumentへの参照は、参照の位置で`OACG101`です（版を混ぜられません）。他のdocumentのbody／formData parameterを`$ref`で使うことも`OACG101`です。 |
| `in: body` parameter | 対応 | `requestBody`になります。media typeはoperationの`consumes`、なければdocumentの`consumes`、なければ`application/json`で、列挙した各media typeに同じschemaを付けます。`description`・`required`は`requestBody`へ移ります。`consumes`にxmlがJSONと併記されている場合はxmlを`OACG108`で無視します（上の`produces`の行と同じ）。`schema`がない、またはbodyが複数なら`OACG100`、`in: formData`との併記も`OACG100`です。path itemのbody parameterは、operationの同名・同`in`のparameterで置き換わります。 |
| `in: formData` parameter | 対応 | 1つのobject schemaの`requestBody`になります（各parameterがproperty、`required: true`のものが`required`、`description`はpropertyのdescription、`type: file`は`type: string`／`format: binary`、`additionalProperties: false`）。`type: file`のparameterがあれば`multipart/form-data`、なければ`consumes`が挙げる`multipart/form-data`／`application/x-www-form-urlencoded`（両方なら両方）、どちらもなければ`application/x-www-form-urlencoded`です。url-encodedで配列のparameterは`collectionFormat`を`encoding`の`style`／`explode`に写します（下表の`query`と同じ。省略時の`csv`も含みます）。multipartの配列は要素ごとにpartを繰り返し、`collectionFormat`は使いません。parameter名の重複は`OACG100`です。 |
| 非body parameter | 対応 | `type`・`format`・`items`・`enum`・`default`・`minimum`／`maximum`／`exclusiveMinimum`／`exclusiveMaximum`・`minLength`／`maxLength`・`pattern`・`minItems`／`maxItems`・`uniqueItems`・`multipleOf`は`schema`へ移ります（`x-nullable: true`は`nullable: true`）。`name`・`in`・`description`・`required`・`allowEmptyValue`・`x-*`はそのままです。`type`がない、`type: file`（formData以外）、`in`が`query`／`header`／`path`／`formData`／`body`以外（`cookie`を含みます）は`OACG100`です。path itemのparameterはpath itemに残り、3.0と同じくoperationのparameterが同名・同`in`のものを置き換えます。 |
| `collectionFormat` | 一部対応 | 配列parameterだけに効きます（省略時は`csv`）。`query`は、`csv`→`style: form`＋`explode: false`、`ssv`→`spaceDelimited`、`pipes`→`pipeDelimited`、`multi`→`explode: true`（`form`の既定）です。`path`・`header`は`csv`（`simple`、既定）だけで、他の値は`OACG101`です。`tsv`は3.0に相当する表現がなく、どこでも`OACG101`です（`collectionFormat`の位置）。`items`の入れ子の`collectionFormat`は`csv`以外が`OACG101`です。 |
| `produces`と応答 | 対応 | responseの`schema`は、operationの`produces`、なければdocumentの`produces`の各media typeの`content`へ付きます（なければ`application/json`、`type: file`なら`application/octet-stream`）。`schema`のないresponseは`content`を持ちません。`components.responses`はdocumentの`produces`だけを使います（operationごとの`produces`は参照元で変えられません）。`examples`（media typeごとの例）はドキュメント専用なので黙って捨てます。`produces`／`consumes`に`application/xml`とJSONが併記され、schemaがobject・arrayの場合、xmlの項目は`OACG108`で無視してJSONで生成します（位置は`in: body`ならそのparameter、応答なら`schema`です。xmlだけなら`OACG101`です）。 |
| 応答の`type: file` | 対応 | `type: string`／`format: binary`になり、3.0と同じく`byte[]`で受け取ります。 |
| 応答の`headers` | 対応 | Header Objectになり、`type`・`format`・`items`・`enum`などは`schema`へ移ります。`description`は残ります。配列の`collectionFormat`は`csv`だけです。 |
| `securityDefinitions` | 対応 | `basic`→`type: http`＋`scheme: basic`、`apiKey`→そのまま、`oauth2`→`flows`です。flowは`implicit`→`implicit`、`password`→`password`、`application`→`clientCredentials`、`accessCode`→`authorizationCode`で、`authorizationUrl`・`tokenUrl`・`scopes`（なければ空）を移します。未知のflowは`OACG100`、上記以外の`type`は`OACG101`です。`security`（document・operation）は3.0と同じで、資格情報の扱いは「Security」の節に従います。 |
| schemaの項目 | 対応 | `x-nullable: true`→`nullable: true`（真偽値以外は`OACG100`）、文字列の`discriminator`→`{ propertyName }`（継承形のbaseとして扱います）、`type: file`→`type: string`／`format: binary`です。`allOf`・`readOnly`・`example`・`xml`・`externalDocs`・制約キーワード・`x-*`はそのまま3.0のschemaとして処理します。 |
| document直下の`info`・`tags`・`externalDocs`・`x-*` | 対応 | 3.0にも同じ項目があるため、そのまま受理します（ドキュメント専用で、診断はありません）。`x-ms-*`などのベンダー拡張も他の`x-*`と同じく黙って無視します（`x-ms-paths`のようにpathを増やす拡張は解釈しません）。それ以外の未知の項目は、3.0と同じく、項目を知っているobjectで診断になります。 |
| 診断の位置 | 対応 | 正規化したtreeの各nodeは、元のSwagger 2.0 nodeの行・桁・logical pathを持ちます。診断（`OACG100`／`OACG101`／`OACG102`／`OACG108`など）は、書いたSwagger 2.0の位置を指します。例: `definitions`のschemaの誤りは`/definitions/X/...`、formData parameterの誤りは`/paths/~1p/post/parameters/N`、body parameterのschemaは`/paths/~1p/post/parameters/N/schema/...`、応答のschemaは`/paths/~1p/get/responses/200/schema`です。複数のmedia typeへ複製したschemaは、どれも同じ元のnodeを指します。formData parameterをまとめたobject schema・`requestBody`の誤りは、最初のformData parameterの位置です。 |
| `OACG101`の対象 | 一部対応 | 3.0に相当する表現がないものは、黙って落とさず`OACG101`です: `collectionFormat: tsv`、`path`／`header`の`csv`以外の`collectionFormat`、Path Itemの`$ref`、`#/definitions/`・`#/parameters/`・`#/responses/`以外を指す`$ref`、複数documentの版の混在、他documentのbody／formData parameterの参照、`oauth2`・`basic`・`apiKey`以外の`securityDefinitions`のtype。 |
| 既存の別の生成器 | 対応 | `GenerationService.GenerateFromApiDocument`が使う、従来の別のSwagger用の生成器（`SwaggerCSharpClientFileGenerator`）は、そのまま残しています。Source Generatorの入口（`ApiClientCodeGenerator`）は使わず、Swagger 2.0は常にこの節の正規化とSource Generatorのpipelineを通ります。 |

## ギャップ別の最終状況

Source Generatorの仕様対応で洗い出した33項目（G01–G33）の最終状況です。詳細な条件は、右端の列が指す本文の節・行を参照してください。**対象外**の項目は診断を報告し、黙って省略しません。

| ID | 項目 | 対応状況 | 参照先・診断 |
| --- | --- | --- | --- |
| G01–G04 | open object（`additionalProperties`の省略または`true`。`[JsonExtensionData]`の`IDictionary<string, JToken>`） | 対応 | Schemaの「object」。`additionalProperties`の省略は従来の`OACG101`ではなくopen objectです |
| G01–G04 | map（`Dictionary<string, T>`）とfree-form object（`JObject`） | 対応 | Schemaの「map / free-form object」 |
| G01–G04 | 型の推論と`{}`・boolean schema `true`（`JToken`） | 対応 | Schemaの「any / type inference」。boolean schema `false`は`OACG101` |
| G01–G04 | 複数型の型配列（3.1）と`[X, null]` | 対応 | `JToken`、または`X`のnullable。「any / type inference」「`oneOf` / `anyOf`（discriminatorなし）」 |
| G01–G04 | 再帰schema | 一部対応 | property・items・additionalPropertiesとunion経由の再帰は可。`$ref`のaliasだけ、array／mapだけの循環は`OACG103`（「unresolved / cyclic `$ref`」） |
| G05 | `operationId`の欠落（method + pathから決定的に命名） | 対応 | 「`operationId`の省略」。重複は`OACG106` |
| G06–G09 | `allOf`の平坦化 | 一部対応 | 「`allOf`」。型の衝突は`OACG101`、循環は`OACG103` |
| G06–G09 | `oneOf`／`anyOf` + discriminator（mapping明示・暗黙、継承形のbase） | 一部対応 | 「`oneOf` / `anyOf` + discriminator」「discriminator付きbase」。メンバーがinline・object以外なら`OACG101` |
| G06–G09 | discriminatorなしのunion、`[X, null]`イディオム | 一部対応 | 「`oneOf` / `anyOf`（discriminatorなし）」 |
| G06–G09 | 3.1の`$ref`とsibling | 一部対応 | 「`$ref` sibling」。3.0で合成が必要なsiblingは`OACG101` |
| G06–G09 | `not` | 一部対応 | 「`not`」。成功応答の検査だけ。評価できない`not`は`OACG108` |
| G10 | `components`以外へのJSON Pointer、`$defs`、externalの任意fragment（schema参照） | 対応 | 「schema `$ref` target」 |
| G11–G14 | integer enum、`null`を含むenum、`type`のないenum | 対応 | 「string / integer enum」。number・boolean・混在のenumは`OACG101` |
| G11–G14 | `x-enum-varnames`／`x-enumNames`、`const` | 一部対応 | 「string / integer enum」「`const`」。不正な指定は`OACG108`、object／array値のconstは`OACG101` |
| G11–G14 | `readOnly`／`writeOnly` | 対応 | 「readOnly / writeOnly」 |
| G11–G14 | `description`／`deprecated`／`default`のXML docと`[Obsolete]` | 対応 | 「description / title / deprecated / default」 |
| G15 | `format: byte`（`byte[]`） | 対応 | 「`format: byte`」。従来の`string`から変わります |
| G15 | `format: binary` | 一部対応 | 「`format: binary`」。JSON本文・parameter・headerでは`OACG101` |
| G16–G20 | cookie parameter | 一部対応 | 「cookie parameter」 |
| G16–G20 | 配列・object parameterと全`style`／`explode` | 一部対応 | 「parameter serialization」「array / object parameter」。入れ子・型なしは`OACG101` |
| G16–G20 | `allowReserved`、parameterの`content` | 一部対応 | 「`allowReserved`」「parameter `content`」 |
| G16–G20 | `Accept`／`Content-Type`／`Authorization`のheader parameter | 一部対応 | parameterを無視して`OACG108`（「ignored header parameters」） |
| G16–G20 | Path Item `$ref`、`components.pathItems` | 対応 | 「Path Item `$ref` / `components.pathItems`」 |
| G16–G20 | path／operation単位の`servers` | 一部対応 | 「path / operationの`servers`」 |
| G21–G26 | requestBodyのmultipart、form-urlencoded、octet-stream、text、`encoding`、複数のmedia type | 一部対応 | 「request body」から「Encoding Object」までの各行。object・arrayのxmlは`OACG101`（他にJSONなどがあれば`OACG108`） |
| G21–G26 | text・binary応答 | 一部対応 | 「text / binary response」「response `Accept` header」 |
| G21–G26 | status別の結果型、defaultのみ・`responses`なし | 一部対応 | 「status-keyed result type」「default-only / omitted responses / 3XX」 |
| G21–G26 | 型付きエラー | 対応 | 「typed error」「error/default response」 |
| G21–G26 | response headers（`WithHttpInfo`） | 一部対応 | 「response header」 |
| G27 | `servers`の複数指定・`variables` | 一部対応 | 「`servers`」。`Servers`・`BuildServerUrl`・`OperationServerUrls` |
| G28 | security（全`securitySchemes`とrequirement） | 対応 | 「Security」の節。oauth2／OIDCは外から渡されたtokenをBearerで送るだけです（取得・更新・discoveryは対象外） |
| G29–G30 | 制約キーワードの実行時検証 | 対応 | 「schema assertions」から「検証の範囲と例外」までの各行。requestは`ArgumentException`、responseは`JsonSerializationException` |
| G29–G30 | `patternProperties`／`propertyNames`／`contains`／`prefixItems`／`dependentRequired`（3.1） | 一部対応 | 3.0では`OACG101`。`if`／`then`／`else`などは対象外 |
| G31 | webhooks／callbacks／links／`components.examples`など | 一部対応 | `OACG108`で受理し、client memberは生成しません（「webhooks / callbacks / links」「components の各 section」） |
| G32 | Swagger 2.0 | 一部対応 | 「Swagger 2.0」の節。OpenAPI 3.0へ正規化して同じpipelineで処理し、対応できない構成は`OACG101` |
| G33 | OpenAPI 3.2（`query`、`additionalOperations`、`in: querystring`、tagsの`parent`／`kind`、`$self`、`itemSchema`の非streaming部分） | 一部対応 | 「OpenAPI 3.2」の節。streamingは対象外 |

## 対象外の項目

次の項目は意図して対象外にしており、診断を報告するか、何も生成せずにドキュメントとして扱います。

| 項目 | 対応状況 | 結果 |
| --- | --- | --- |
| `$id`／`$anchor`／`$dynamicAnchor`／`$dynamicRef` | 非対応 | `OACG101` |
| `if`／`then`／`else`、`dependentSchemas`、`unevaluatedProperties`／`unevaluatedItems`、`contentSchema`、未知のschemaキーワード | 非対応 | そのキーワードの位置で`OACG101` |
| object・arrayのschemaの`application/xml`（`+xml`） | 非対応 | `OACG101`。同じ`content`に生成できる他のmedia typeがあれば、xmlの項目だけを`OACG108`で無視します |
| number・boolean・型が混在するenum、boolean schema `false` | 非対応 | `OACG101` |
| `required: true`かつnullableなpath／query／header parameter | 非対応 | `OACG101` |
| 入れ子・型なしのparameter（`style`で表せないもの） | 非対応 | `OACG101`（`content`を使います） |
| 既定以外の`jsonSchemaDialect`・`$schema` | 非対応 | `OACG101`（不正なURIは`OACG100`） |
| OpenAPI 3.2のstreaming（`text/event-stream`、`itemSchema`の逐次処理） | 非対応 | `text/event-stream`の`itemSchema`と、順次media type以外の`itemSchema`は`OACG101`。`application/jsonl`などの`itemSchema`は`OACG108`で通知し、本文は`byte[]`です |
| TRACEの`requestBody`、HEAD・204・205の非空`content` | 非対応 | `OACG101` |
| `Stream`や`HttpResponseMessage`を返すメソッド | 非対応 | 生成しません（巨大な本文は`byte[]`に全読みします） |
| `format`の検証（`email`、`uri`など） | 非対応 | 受理するだけで検証しません（`date`・`date-time`・`uuid`の解析は従来どおりです） |
| YAMLの拡張（tag、directive、複数document、merge key、複雑なkey） | 非対応 | `YAML001`–`YAML015`（「YAMLの対応範囲」） |
| oauth2 flowによるtoken取得・更新、OpenID Connect discovery | 非対応 | 行いません。外から渡されたtokenをBearerで送ります |
| `mutualTLS`のクライアント証明書の設定 | 非対応 | client側では行いません。`HttpClientHandler`で設定します |
| Swagger 2.0で3.0に相当する表現がないもの（`collectionFormat: tsv`など） | 非対応 | `OACG101`（「Swagger 2.0」） |

## 生成結果と利用上の制約

生成結果は次のpublic API contractです。

- public mutable sealed DTO（Newtonsoft.JsonではJson.NETのattribute、System.Text.JsonではSystem.Text.Jsonのattributeを使用）
- 任意かつschema上nullableなDTO propertyの`<PropertyName>Specified`による存在状態の管理
- 任意かつschema上nullableなrequest bodyの`<BodyParameterName>Specified`による明示的なJSON `null`送信
- 宣言したwire文字列を完全一致で扱うstring enum converterと`List<T>`
- `oneOf`／`anyOf`のsealed unionラッパークラス（`FromX`／`TryGetX`／`AsX`、discriminatorなしは`FromJToken`／`ToJToken`）と、internalな`JsonConverter`
- injected `HttpClient`を使うasync client API。documentまたはexplicit base URLを指定するconstructorを使用可能（`UnityWebRequest`を選んだclientは`HttpClient`の代わりに`baseAddress`を受け取ります）
- path/query/header/body、`JsonConvert`、`CancellationToken`、宣言された`2xx` response handling
- request本文のmedia type別メソッド（`<name>AsMultipart`など）、ファイルpartの`<ApiName>FileParameter`、status別の結果型`<OperationName>Response`、`<name>WithHttpInfo`と`<Name>HttpInfo`
- generated `<ApiName>Exception`（`Error`／`TryGetError<T>`／`ErrorDeserializationException`を追加）

生成モデルは、raw JSON/YAMLを各normalizerでnormalized bundleへ変換した後、OAS semantic parserとinternal reference resolverを通し、
Roslyn非依存のgeneration modelを作ってから、Roslynでparseしたdeterministic source emitterへ渡します。
emitterの結果を`AddSource`します。JSONシリアライザーとHTTP実装は、client定義の`JsonSerializer`／`HttpTransport`でclientごとに選びます。
既定はNewtonsoft.Jsonと`System.Net.Http.HttpClient`で、この文書の他の節は既定の出力を前提にしています（選択肢と差異は「ライブラリの選択（HTTPとJSON）」）。
Docker providerは別providerであり、fallbackまたは切替は行いません。

生成物はhand-editするsourceではなく、再Generateされるpublic contractです。spec IDを含むhint nameは
stableで、generated nameはordinalのdeterministic namingとsuffixによるcollision resolutionを使用します。
複数specでDTO名が重なる場合は、generated namespaceを分けてください。current generated typeは
namespace-levelのため、同じnamespaceには共存できません。

`apiName`が生成クライアントの固定メンバー名と一致すると`OACG005`です。予約名は
`_httpClient`、`_baseUrl`、`DateOnlyJsonConverterInstance`、`CreateRequestUri`、
`CombineAbsoluteUri`、`CombinePaths`、`CombineQueries`、`AppendQuery`、
`SplitPathAndQuery`、`ValidatePathSegments`、`CreateJsonContent`、
`ConvertToString`、`DateOnlyJsonConverter`、`ReadJsonResponseBodyAsync`、`HasPreamble`、
`Servers`、`BuildServerUrl`、`ExpandServerUrl`、`OperationServerUrls`、
`ResolveOperationServerUrl`、`OperationNames`、`GetOperationServers`、
`BuildOperationServerUrl`です。`UnityWebRequest`を選んだclientでは、次のメンバー名も予約名で、`apiName`が一致すると`OACG005`です:
`_baseAddress`、`RequestTimeout`、`ConfigureUnityWebRequest`、`SendUnityWebRequestAsync`、
`SetUnityRequestHeader`、`AddUnityResponseHeader`（`HttpClient`のclientで受理される名前は変わりません）。

生成するDTO・enumのwire converter・unionと、そのpropertyには、IL2CPPのManaged Stripping Level Highでもリフレクション専用のmemberを残すため、
internalな`PreserveAttribute`を付けます。attributeはclientごとに専用のnamespace `<generated namespace>.<ApiName>_Preserve`へ宣言します
（同じnamespaceに複数のclientを生成しても重複しません）。DTO・enum・unionのないspecでは生成しません。
同じnamespaceに利用者が`<ApiName>_Preserve`という名前の型を宣言すると、このnamespaceと衝突してコンパイルエラーになるため、別の名前にしてください。
この変更により、DTOまたはenumを持つspecの生成出力は、既定の組み合わせでも変わります（attributeの追加と、`PreserveAttribute`の宣言先がclientのnamespaceから`<ApiName>_Preserve`へ移ったこと）。
schemaの項目名が別項目のCLR名に`Specified`を付けた名前と衝突する場合、JSON上の名前を保ったまま
CLR名に番号を付けます。Json.NETがschemaのboolean値を存在フラグと誤認することを防ぎます。

## ライブラリの選択（HTTPとJSON）

client定義の`OpenApiClientDefinitionAttribute`の名前付きプロパティ`HttpTransport`（`OpenApiHttpTransport`: `HttpClient`／`UnityWebRequest`）と
`JsonSerializer`（`OpenApiJsonSerializer`: `NewtonsoftJson`／`SystemTextJson`）で、clientごとに選びます。省略時は
`HttpClient` + `Newtonsoft.Json`です。constructorの引数は変わらず、旧4引数形式の定義も受理します。Editorは既定値と異なるプロパティだけを
定義ファイルへ書き出すため、既存の定義ファイルはバイト単位で変わりません。選択を変えても`specId`とclient identityは変わりません。
Editorでは、Settingsウィンドウ（`Window/OpenAPI Code Generator/Settings`）の**Source Generator Libraries**で選び、Generatorウィンドウで準備状況を確認します
（不足していれば`Generate`を無効にします）。0.6.0からSource Generatorパッケージは`com.unity.nuget.newtonsoft-json`に依存せず、
必要なライブラリは利用者が導入します。

| 組み合わせ | Status | 条件・制約 |
| --- | --- | --- |
| `HttpClient` + `Newtonsoft.Json`（既定） | 対応 | 既定です。WebGL Playerでは動作しません（`OACG014`）。 |
| `HttpClient` + `System.Text.Json` | 対応 | 下の「System.Text.JsonとNewtonsoft.Jsonの差異」を除き、同じ契約です。WebGL Playerでは動作しない見込みです（`OACG014`。実測はNewtonsoft.Jsonの1セルのみ）。 |
| `UnityWebRequest` + `Newtonsoft.Json` | 対応 | 下の「UnityWebRequestの制約」があります。WebGLではこのtransportを選びます。 |
| `UnityWebRequest` + `System.Text.Json` | 対応 | 同上。 |
| 上記以外の値 | 非対応 | `OACG010` |

### 必要なライブラリ

Analyzerは、コンパイルが参照するアセンブリの公開型と、そのアセンブリ版（同じ型を複数の参照が持つ場合は最大の版）、`UNITY_WEBGL`シンボルで判定します。
選択していないライブラリの不足や版は報告しません。Errorがあるclientは何も生成せず、診断だけを報告します。
EditorのSettingsとGeneratorウィンドウは、出力先asmdefのPlayer用参照から事前に判定します（Editor専用のDLLは数えません）。
NuGetForUnityや手動配置のDLLでも、参照があれば検出します。

| 選択 | 必要な参照 | 最低版 | 不足・不一致の診断 |
| --- | --- | --- | --- |
| `HttpClient` | `System.Net.Http.HttpClient` | - | `OACG012` |
| `UnityWebRequest` | `UnityEngine.Networking.UnityWebRequest`（Unity Web Requestモジュール）と`System.Net.Http.HttpClient`（アダプターが`HttpRequestMessage`／`HttpContent`を使うため） | - | `OACG012` |
| `Newtonsoft.Json` | `Newtonsoft.Json.JsonConvert`（`com.unity.nuget.newtonsoft-json` 3.2.2など） | 13.0 | `OACG011`、版は`OACG013` |
| `System.Text.Json` | `System.Text.Json.JsonSerializer`（UnityNuGetの`org.nuget.system.text.json` 8.0以上。実行時に`System.Text.Encodings.Web`も必要） | 8.0 | `OACG011`、版は`OACG013` |

System.Text.Jsonの導入は手順の表示だけです（UnityNuGetのscoped registry `https://unitynuget-registry.openupm.com`（scope `org.nuget`）と
`org.nuget.system.text.json` 8.0以上）。Editorは`Packages/manifest.json`を自動編集しません。

### System.Text.JsonとNewtonsoft.Jsonの差異

生成するpublic APIの形は同じで、次の点が異なります。それ以外の契約（wire形式、制約の検証、診断）は同じになるよう実装し、
両方のシリアライザーで同じ種類のテストを実行しています。

| 項目 | Newtonsoft.Json | System.Text.Json |
| --- | --- | --- |
| `{}`・任意の値（Any） | `JToken` | `JsonElement` |
| propertiesのない`type: object`（FreeObject） | `JObject` | `System.Text.Json.Nodes.JsonObject` |
| open DTOの宣言外member（ExtensionData） | `[JsonExtensionData]`の`IDictionary<string, JToken>` | `[JsonExtensionData]`の`IDictionary<string, JsonElement>` |
| discriminatorなしunionの生の値 | `FromJToken`／`ToJToken` | `FromJsonElement`／`ToJsonElement`（`FromJsonElement`で作った値は既定のoptionsで読むため、clientのconverterは適用されません） |
| contractに違反したresponse・response header | `Newtonsoft.Json.JsonSerializationException` | `System.Text.Json.JsonException` |
| requestの制約違反 | `ArgumentException` | `ArgumentException`（同じ） |
| `<Property>Specified`・`readOnly`の規則、日付・整数のconverter | DTOの属性と`ShouldSerialize<Name>()` | clientの`JsonSerializerOptions`（`DefaultJsonTypeInfoResolver.Modifiers`）が持ちます。DTOを独自のoptionsでシリアライズしても適用されません |
| ホストの設定 | requestとresponseは独立したserializer。正の`JsonConvert.DefaultSettings.MaxDepth`が深さ上限64を上書き | グローバル設定は参照しません。深さ上限は64固定。`UnsafeRelaxedJsonEscaping`はHTTP本文専用で、出力をHTMLに埋め込まないでください |
| responseの`$id`／`$ref`／`$values`参照メタデータ | 互換拡張として受理 | 非対応 |
| integerの`2`と`2.0`・`1e0`（数学的に整数の字句） | DTOのproperty・配列要素はCLR型の範囲内で受理 | 同じ（clientのconverter）。ただしdiscriminatorなしunionの構造照合では、小数部または指数を持つ数はintegerとみなしません |
| 孤立したサロゲートescape（`\ud83d`など）を含むresponse | - | 有効なUTF-16ではないため`JsonException`で拒否 |
| 宣言したエラー本文（`Error`、`TryGetError<T>`） | 検査せずそのまま逆シリアル化。失敗は`ErrorDeserializationException` | 先に成功本文と同じ検査（重複名、closed DTOの宣言外member、整数の字句、enum値、必須member）を行います。失敗すると`Error`は`null`で、理由は`ErrorDeserializationException` |

長さの制約はどちらもUnicodeコードポイント数で数え（サロゲートペアは1文字）、responseの重複したmember名はどちらも拒否します。
この文書の他の節で`JToken`・`JObject`・`JsonSerializationException`・`$id`などと書いた箇所は、Newtonsoft.Jsonの場合の記述です。

### UnityWebRequestの制約

`UnityWebRequest`のclientは、request・response・検証の経路を`HttpClient`のclientと共有し（`HttpRequestMessage`／`HttpContent`／`HttpResponseMessage`を使うアダプター方式）、
最後の送信だけを`UnityWebRequest`で行います。constructorは`HttpClient`の代わりに`Uri? baseAddress`（と任意の`baseUrl`）を受け取ります。

| 項目 | Status | 条件・制約 |
| --- | --- | --- |
| 実行スレッド | 一部対応 | `UnityWebRequest`の生成・送信・破棄は、呼び出し元の`SynchronizationContext`（Unityのメインスレッド）で行います。送信前に`ConfigureAwait(false)`は使いません。clientのメソッドはメインスレッドから呼んでください。 |
| キャンセル | 対応 | トークンのキャンセルでそのコンテキストの`Abort()`を呼び、`OperationCanceledException`を投げます。コンテキストがなければキャンセルしたスレッドで`Abort()`が走ります。 |
| タイムアウト | 一部対応 | `RequestTimeout`（`TimeSpan?`、`null`は無制限）を秒単位に切り上げて`UnityWebRequest.timeout`へ渡します。超過すると`HttpClient.Timeout`と同様に、`InnerException`が`TimeoutException`の`TaskCanceledException`です。`HttpClient`の`Timeout`や`HttpClientHandler`の設定は使いません。 |
| エラー | 対応 | 通信失敗は`HttpRequestException`です。HTTPのエラーstatus（4xx・5xx）は例外ではなく通常の応答として扱い、`HttpClient`のclientと同じ規則（`<ApiName>Exception`など）に従います。 |
| 相対server URL | 一部対応 | `/v1`・`//host/v1`・`v1`のような相対server URLには、絶対`baseAddress`が必要です（`HttpClient.BaseAddress`に相当）。ない場合は呼び出し時に`InvalidOperationException`です。絶対server URLなら不要です。WebGLでは`new Uri(Application.absoluteURL)`などをbase addressに渡します。 |
| 送信前の設定 | 対応 | `ConfigureUnityWebRequest`が送信直前に各`UnityWebRequest`を受け取ります（certificate handler、リダイレクト上限など）。`UseCookies`やクライアント証明書など`HttpClientHandler`の設定は適用されません。 |
| 重複したレスポンスヘッダー | 一部対応 | `UnityWebRequest`が1つのカンマ区切りの値に結合します。レスポンスのreason phraseは取得できません。 |
| requestヘッダーの制限 | 一部対応 | 生成コードはすべてのrequestヘッダー（cookie parameterとcookieのapiKeyは1本の`Cookie`ヘッダー）を`UnityWebRequest.SetRequestHeader`で設定し、Unityが拒否したヘッダーは黙って捨てず、ヘッダー名を示す`InvalidOperationException`（元の例外を`InnerException`に保持）にします。どのヘッダーが拒否されるかはUnityとプラットフォームの判断で、`Host`、`Connection`、`Expect`、`Transfer-Encoding`や、WebGLでブラウザーが禁止するヘッダーは送れない場合があります。WebGLの制限（`Cookie`、`User-Agent`を含む）は未検証です。 |
| 本文のメモリ保持 | 一部対応 | request本文（`Stream`のファイルpartを含む）とresponse本文は`byte[]`としてメモリに保持します。 |
| 予約名 | 対応 | `_baseAddress`、`RequestTimeout`、`ConfigureUnityWebRequest`、`SendUnityWebRequestAsync`、`SetUnityRequestHeader`、`AddUnityResponseHeader`は、このtransportだけの予約API名です（`OACG005`）。 |
| WebGL | 対応 | WebGLのHTTP経路はこのtransportだけです。Player検証の結果は「Player互換性（IL2CPP・WebGL）」を参照してください。ブラウザーのsame-origin／CORSの規則に従います（別originは未検証）。 |

## 診断

診断は位置付き（logical path）で報告します。`OACG001`–`OACG009`はBundle・入力・client定義の診断、`OACG010`–`OACG014`はライブラリ選択の診断で、`OACG100`–`OACG108`はOpenAPI document（Swagger 2.0を含む）のsemantic generationの診断です。ErrorはGenerateを完了させず、Warningは生成を続けます。

| ID | 重大度 | Title | 報告する場面 |
| --- | --- | --- | --- |
| `OACG001` | Error | Malformed normalized spec bundle | Bundleの構造・参照edgeの不正 |
| `OACG002` | Error | Unsupported normalized spec bundle version | Bundleのformat versionが未対応 |
| `OACG003` | Error | OpenAPI generation failed | semantic generationが失敗した場合の通知 |
| `OACG004` | Error | Duplicate normalized spec bundle ID | 同じSpec IDを宣言するBundleが複数ある |
| `OACG005` | Error | Invalid OpenAPI client definition | `apiName`が生成メンバーの予約名（「生成結果と利用上の制約」。`UnityWebRequest`専用の予約名はそのtransportのときだけ）と一致する場合など |
| `OACG006` | Error | Missing normalized spec bundle | Spec IDに対応するBundleがない |
| `OACG007` | Error | Duplicate OpenAPI client definition ID | 同じSpec IDのclient定義が複数ある |
| `OACG008` | Error | Unsupported OpenAPI document format | document formatが`Json`（0）・`Yaml`（1）以外 |
| `OACG009` | Error | Normalized spec bundle ID mismatch | Bundle内のSpec IDとAdditionalFile名が一致しない |
| `OACG010` | Error | Unsupported library selection | client定義の`HttpTransport`／`JsonSerializer`が未知の値、または実装のない組み合わせ |
| `OACG011` | Error | JSON library is not referenced | 選択したJSONライブラリの公開型（`Newtonsoft.Json.JsonConvert`または`System.Text.Json.JsonSerializer`）が参照にない |
| `OACG012` | Error | HTTP implementation is not referenced | 選択したHTTP実装の公開型（`System.Net.Http.HttpClient`、`UnityWebRequest`では`UnityEngine.Networking.UnityWebRequest`と`System.Net.Http.HttpClient`）が参照にない |
| `OACG013` | Error | Referenced library version is too low | 参照しているライブラリが最低版未満（Newtonsoft.Json 13.0、System.Text.Json 8.0） |
| `OACG014` | Warning | HttpClient is not supported on WebGL | `UNITY_WEBGL`が定義されている状態で`HttpClient`を選んでいる。生成とコンパイルは通りますが、WebGL Playerでは動作しません |
| `OACG100` | Error | Invalid OpenAPI document | 仕様に反する、または矛盾した値（必須項目の欠落、不正な`pattern`・`style`・`$self`、`schema`と`content`の併記、未定義のsecurity scheme、重複するdiscriminator値・tag名など）。Swagger 2.0の正規化中の不正（body parameterの`schema`欠落、未知のflowなど）も含みます |
| `OACG101` | Error | Unsupported OpenAPI element | 対象外の要素（上の「対象外の項目」）、未知の項目（`requird`、未知のschemaキーワード、未知のcomponents section）、版に合わない項目（3.0／3.1の`query`・`in: querystring`、3.0のschemaで`patternProperties`など）、Swagger 2.0で3.0に相当しない表現 |
| `OACG102` | Error | Unresolved OpenAPI reference | `$ref`が文書・JSON Pointerとして存在しない場合。OACG108で無視するsubtree内の`$ref`は解析しないので報告しません |
| `OACG103` | Error | Cyclic OpenAPI reference | `$ref`のaliasだけ、array／mapだけを経由して名前付き型で閉じない循環、`allOf`の循環、DTOを介さないsubschemaの自己参照 |
| `OACG104` | Error | External OpenAPI reference is unsupported | Bundle v1のexternal参照（互換診断） |
| `OACG105` | Error | Inconsistent OpenAPI response contract | 1つのresponse・requestBody内でschemaが異なる複数のJSON media type、3.2で複数の`in: querystring`、または`in: querystring`と`in: query`の併記。status間でcontractが異なる成功responseはstatus別の結果型になり、報告しません |
| `OACG106` | Error | Invalid generated API identifier | 明示した`operationId`が複数のoperationで重複した場合（省略した`operationId`は連番で一意にするので報告しません） |
| `OACG107` | Warning | Generated type name collision | 型名が衝突し、決定的なsuffix（連番）で解消した場合 |
| `OACG108` | Warning | Ignored OpenAPI element | 要素を意図して無視した場合。wire contractは変わりません。ドキュメント専用の要素（`description`・`example`など）は黙って受理するので報告しません。主な場面: `Accept`／`Content-Type`／`Authorization`のheader parameter、webhooks・callbacks・links（とcomponentsの`links`・`callbacks`）、生成できる他のmedia typeがあるときのobject・arrayのxml、処理できない別media typeや生成できないerror schema、評価できない`not`・`const`・`x-enum-*`・`discriminator`、.NET構文を要する`pattern`、binaryに付いた制約キーワード、Encoding Objectの無視した項目、3.2の`itemSchema`（逐次media type）・`prefixEncoding`／`itemEncoding`・他documentを参照する`$self`、Swagger 2.0でhostがないときのoperationの`schemes` |

## Player互換性（IL2CPP・WebGL）

[#57](https://github.com/rebeat-jp/UnityOpenApiCodeGen/issues/57)の検証結果です。検証は`SourceGenerators/scripts/verify-player.js`
（`verify-player.sh`から実行）で行い、`SourceGenerators/BuildTools/UnityPlayerVerificationFixture/`のfixtureを使います。
fixtureはSource Generatorの本来の経路（Editor Writerの定義ファイル、normalized spec bundle、Analyzer）で4つのclient
（HttpClientまたはUnityWebRequest × Newtonsoft.JsonまたはSystem.Text.Json）を生成し、Player内でJSON往復（DTO、string／integer enum、
`date-time`、`date`、map、配列、discriminator付き`oneOf`）、GET 1回、POST 1回、constraint違反の事前拒否を実行します。
結果は各セルの`result.json`（ビルド・Playerログ付き）として`artifacts/player-gate/<Unity版>/<セルID>/`へ保存され、
要約を[`Evidence/6000.3.2f1.json`](BuildTools/UnityPlayerVerificationFixture/Evidence/6000.3.2f1.json)に残しています。

検証条件: Unity `6000.3.2f1`、Windows 11 x64ホスト、2026-11-14実行。Standalone Win64はIL2CPPで、WebGLはIL2CPP（WebGLに別のbackendはありません）。
ビルドは非Development、IL2CPPコード生成・WebGLの例外サポートなどのPlayer設定は既定のままです（WebGLは圧縮なし）。
WebGLはヘッドレスChrome（157）で、Playerと同一originのローカルHTTPサーバー（`127.0.0.1`）に対して実行しました。
System.Text.Jsonは`org.nuget.system.text.json` 8.0.5（UnityNuGet、netstandard2.0版）、Newtonsoft.Jsonは`com.unity.nuget.newtonsoft-json` 3.2.2です。

| Target | Backend | Managed Stripping | HTTP | JSON | 結果 | ビルド（秒） | チェック |
| --- | --- | --- | --- | --- | --- | ---: | --- |
| Standalone Win64 | IL2CPP | Minimal | HttpClient | Newtonsoft.Json | Pass | 157 | 21/21 passed |
| Standalone Win64 | IL2CPP | Minimal | HttpClient | System.Text.Json | Pass | 84 | 21/21 passed |
| Standalone Win64 | IL2CPP | Minimal | UnityWebRequest | Newtonsoft.Json | Pass | 87 | 21/21 passed |
| Standalone Win64 | IL2CPP | Minimal | UnityWebRequest | System.Text.Json | Pass | 71 | 21/21 passed |
| Standalone Win64 | IL2CPP | High | HttpClient | Newtonsoft.Json | Pass | 99 | 21/21 passed |
| Standalone Win64 | IL2CPP | High | HttpClient | System.Text.Json | Pass | 83 | 21/21 passed |
| Standalone Win64 | IL2CPP | High | UnityWebRequest | Newtonsoft.Json | Pass | 87 | 21/21 passed |
| Standalone Win64 | IL2CPP | High | UnityWebRequest | System.Text.Json | Pass | 74 | 21/21 passed |
| Standalone Win64 | Mono（比較） | Minimal | HttpClient | Newtonsoft.Json | Pass | 35 | 21/21 passed |
| Standalone Win64 | Mono（比較） | Minimal | UnityWebRequest | Newtonsoft.Json | Pass | 26 | 21/21 passed |
| WebGL | IL2CPP | Minimal | UnityWebRequest | Newtonsoft.Json | Pass | 181 | 21/21 passed |
| WebGL | IL2CPP | Minimal | UnityWebRequest | System.Text.Json | Pass | 120 | 21/21 passed |
| WebGL | IL2CPP | High | UnityWebRequest | Newtonsoft.Json | Pass | 166 | 21/21 passed |
| WebGL | IL2CPP | High | UnityWebRequest | System.Text.Json | Pass | 117 | 21/21 passed |
| WebGL | IL2CPP | Minimal | HttpClient | Newtonsoft.Json | 非対応（`OACG014`。Playerは90秒で無応答） | 175 | 0/1 passed |

- **Pass**: 21チェックがすべて成功し、サーバーがGET 1件・POST 1件を受信して、POST本文のwire形式（enumのwire値、日付、map、discriminatorとメンバー）も正しいことを確認しました。
- **WebGL × HttpClientは非対応**です。`UNITY_WEBGL`定義下でHttpClient transportを選ぶとAnalyzerが`OACG014`（Warning）を報告し、生成とビルドは成功しますが、
  Playerのprobeは完了せず、サーバーはrequestを1件も受信しません（90秒のwatchdogで失敗を記録）。WebGLでは`UnityWebRequest`を選んでください。実測したのはNewtonsoft.Jsonの1セルだけです。原因はtransport（`System.Net.Http.HttpClient`のソケットがWebGLのブラウザー環境に無いこと）でserializerとは無関係なので、System.Text.Jsonも同様と判断しており、個別には実測していません。
- **Managed Stripping Level High**: 生成物に何も加えない初回の実行は、Standalone IL2CPP 4セルとWebGL 2セルで失敗しました。
  Newtonsoft.Jsonは列挙型のwire converterを生成できず（`No parameterless constructor defined for '...ColorWireConverter'`）、
  System.Text.Jsonはsetterを持たないpropertyとして`JsonRequired`のpropertyを拒否しました（`JsonPropertyInfo 'radius' ... is marked required but does not specify a setter`）。
  どちらも、リフレクションだけで使うメンバー（converterのコンストラクター、コードから設定しないDTO propertyのaccessor）がHighで削除されるためです。
  型への`PreserveAttribute`だけではproperty accessorが残らないことも確認しました（型単位だけの再実行は、System.Text.Jsonで同じ失敗、Newtonsoft.Jsonでunionメンバーの検査失敗でした）。
  そのため生成器は、DTO型・enumのwire converter・各DTO propertyと`<Name>Specified`・`OnSerializing`コールバックに、internalな`PreserveAttribute`を付けます
  （Unityのlinkerは名前で認識します。`link.xml`は生成assembly名を持てないため使いません）。attributeはclientごとの専用namespace（`<生成namespace>.<ApiName>_Preserve`）に宣言するので、同じnamespaceに複数のclientを生成しても重複しません（型名が衝突する場合は連番を付けます）。
  DTO・enum・unionを持たないspecでは、attributeもそのファイルも生成しません。この変更後に全セルがPassしました。
- 対象外（未検証）: Unity `6000.0.23f1`と`2021.3`、Linux・macOS・Android・iOSのPlayer、Development Player、Managed Stripping Level Low／Medium／Disabled、
  IL2CPPのコード生成設定とWebGLの例外サポートの既定以外、別origin（CORS）への通信、WebGLの圧縮形式、Safari／Firefox。
  Linux Standalone IL2CPPとWebGLの手動CI（`.github/workflows/source-generator-player.yml`）は用意しましたが、まだ実行していません。
- WebGLでUnityWebRequestを使う場合、server URLが相対（`/api`など）なら、`new Uri(Application.absoluteURL)`のような絶対URIをbase addressに渡す必要があります。

## 検証済み環境

GitHub ActionsのUnity検証とCD dry-runは実装済みです。レビュー対応前のclean commit
`adefab2c5bf367c3ecf5971fa555f1cbb68afa24`では、
[run 34987295723](https://github.com/rebeat-jp/UnityOpenApiCodeGen/actions/runs/34987295723)
で3版のUnity検証とCD dry-runが成功しています。

以下の検証結果は2026-09-20時点の記録で、その後に追加した対応範囲（上の各節）のテスト件数や、現在のテスト件数を表すものではありません。

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
IL2CPP/WebGLのPlayer互換性は、上の「Player互換性（IL2CPP・WebGL）」にUnity `6000.3.2f1`での検証結果を記録しています（[#57](https://github.com/rebeat-jp/UnityOpenApiCodeGen/issues/57)）。正式タグ・公開・OpenUPM初回登録は
[#62](https://github.com/rebeat-jp/UnityOpenApiCodeGen/issues/62)の別作業です。
