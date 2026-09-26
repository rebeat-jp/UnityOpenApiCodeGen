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
`OACG100`–`OACG106`を使用します。型名の重複をsuffixで解消した場合はwarning `OACG107`を報告します。

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
| OpenAPI 3.1 schema dialect | Partial | 既定または明示的な`https://spec.openapis.org/oas/3.1/dialect/base`のみ。rootの`jsonSchemaDialect`とschemaの`$schema`で別dialectを指定すると`OACG101`、不正なURIは`OACG100`です。 |
| OpenAPI 3.2 / Swagger 2 | Unsupported | document versionは受け付けません。 |
| URL入力 | Supported | public/private/loopback、cross-host redirectを許可。userinfo、空白、非HTTP(S)、HTTPS-to-HTTP direct/redirectは拒否。Generateが明示的なfetch/refresh操作で、Docker fallbackはありません。 |
| JSON/YAML semantic parity | Supported | 各normalizerがshared `SpecNode`へ正規化し、同じsemantic/generation pipelineを通ります。 |
| `$ref` | Partial | OpenAPI/Schema上の参照位置にあるinternal reference、またはEditorがBundle v2のedgeへ解決したexternal reference。example/default/enum/拡張データ内の同名キーはデータとして保持。fragmentはemptyまたはJSON Pointer。 |
| external `$ref` | Partial | local external fileはreal/symbolic-link解決後もUnity project内。remote documentからlocal fileは不可。bare Schemaは可。 |
| unresolved / cyclic `$ref` | Unsupported | それぞれ`OACG102`、`OACG103`を報告します。`OACG104`はBundle v1のexternal-reference互換診断です。 |
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
| JSON-compatible scalar | Supported | `null`、`true`/`false`、RFC 8259 number（integer/real）、string。 |
| literal / folded block scalar | Partial | `|`/`>`、`+`/`-` chomping、explicit indent `1`–`9`。flow内は拒否。 |
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
| `servers` | Partial | URLは0個または1個、variablesなし。 |
| parameter location | Partial | path、query、headerのscalar parameterのみ。cookieは対象外です。 |
| parameter serialization | Partial | defaultの`style`/`explode`のみ。`allowReserved: true`は対象外です。 |
| complex parameter | Unsupported | 直接定義・多段`$ref`ともarray/object等のcomplex parameterは対象外です。 |
| required nullable parameter | Unsupported | path/query/headerの`required: true`とnullableの組み合わせは`OACG101`です。参照チェーン全体を確認します。任意parameterのnull省略とDTOのnullableは維持します。 |
| dot-only path segment | Unsupported | 引数置換後のpath segmentが`.`／`..`（1回percent-decodeした表現を含む）なら、HTTP送信前に`ArgumentException`です。`.hidden`、`a.b`、`...`や通常の複合segmentは使用できます。 |
| header identity | Supported | header名だけ大文字小文字を区別せず、operation側で上書きします。送信名はoperationの宣言を保持し、path/query名は区別します。 |
| request body | Partial | パラメーター付きも含む`application/json`または`application/*+json`。type/subtypeを正規化して判定します。 |
| optional nullable request body | Supported | `requestBody.required: false`かつschemaがnullableの場合、`null`引数は本文省略、非`null`引数はJSON本文を送信します。生成メソッドの`<BodyParameterName>Specified`を`true`にすると、`null`引数でも明示的なJSON `null`を送信します。名前が衝突する場合は番号を付けます。 |
| request charset | Partial | UTF-8、UTF-16 LE/BE。未指定はUTF-8。不正なContent-Typeや未対応charsetは入力位置付き診断です。 |
| successful response | Partial | 少なくとも1つの`2xx` responseが必要です。複数ある場合、別名参照を終端まで解決し、実効nullable性と型の同一性を含むcontractが一致する必要があります。非nullableな成功本文が空またはJSON `null`なら実行時に`JsonSerializationException`です。 |
| error/default response | Partial | JSON以外の本文も許可します。schemaの構造・参照を検証し、成功本文のDTO生成制限は適用しません。例外は実際の本文を保持します。 |
| response extension | Supported | operationのResponses Objectでは`x-*`を除外します。`components.responses`の`x-*`名は通常のResponse/Referenceです。 |
| response header | Unsupported | nonempty response headerは対象外です。 |

status codeはASCII数字で検証します。属性付きのジェネリックclassは`OACG005`で拒否します。

## Schema

以下は生成するrequest／成功response／DTOの制限です。非2xxと`default`の本文は構造・参照検証の対象です。

| 項目 | Status | 条件・制約 |
| --- | --- | --- |
| scalar | Supported | `string`、`integer`、`number`、`boolean`。 |
| object | Partial | named propertiesと`required`を持つobject。 |
| array | Partial | supported schemaのarray。 |
| string enum | Partial | generated C# enumに`StringEnumConverter`を付与します。OpenAPI 3.1の`type: [string, null]`でも`enum`に文字列しかなければ非nullableです。`null`を含むenumは`OACG101`です。 |
| direct schema reference | Supported | supported schemaへの参照。多段参照のscalar／enumもnullableを保持します。 |
| `$ref` sibling | Partial | 注釈、literalデータ、`x-*`と既存の3.0 `nullable`は保持します。合成が必要な`type`、`properties`、`required`などは、該当位置の`OACG101`で拒否します。 |
| nullable | Partial | OpenAPI 3.0の`nullable`、OpenAPI 3.1の`type: [<type>, null]`。 |
| optional nullable DTO property | Supported | 未代入はJSONから省略し、`null`代入は明示的なJSON `null`、値の代入はその値を送ります。生成される`<PropertyName>Specified`を`false`へ戻すと再び省略します。 |
| scalar format | Partial | `integer`は通常`int`、`int64`は`long`。`number`は通常`double`、`float`は`float`、`decimal`は`decimal`。`date`、`date-time`、`uuid`は`DateTime`、`DateTimeOffset`、`Guid`へmappingします。 |
| map / free-form object | Unsupported | `additionalProperties`などのmap/free-form表現は対象外です。 |
| composition | Unsupported | `allOf`、`anyOf`、`oneOf`、`not`、discriminatorは対象外です。 |
| binary / readOnly / writeOnly | Unsupported | 生成contractでは対象外です。 |

`$ref`と併記できる注釈・データのキーは`$comment`、`default`、`deprecated`、
`description`、`example`、`examples`、`externalDocs`、`summary`、`title`、`xml`、`x-*`です。
OpenAPI 3.0では既存互換として`nullable`も扱います。

リクエストJSONの`format: date`は`yyyy-MM-dd`へ変換します。公開型の`DateTime`は維持し、
`date-time`の`DateTimeOffset`にはdate用converterを適用しません。

## Generated outputと利用上の制約

生成結果は次のpublic API contractです。

- public mutable sealed DTO（Json.NET attribute使用）
- 任意かつschema上nullableなDTO propertyの`<PropertyName>Specified`による存在状態の管理
- 任意かつschema上nullableなrequest bodyの`<BodyParameterName>Specified`による明示的なJSON `null`送信
- `StringEnumConverter`付きのstring enumと`List<T>`
- injected `HttpClient`を使うasync client API。documentまたはexplicit base URLを指定するconstructorを使用可能
- path/query/header/body、`JsonConvert`、`CancellationToken`、宣言された`2xx` response handling
- generated `<ApiName>Exception`

生成モデルは、raw JSON/YAMLを各normalizerでnormalized bundleへ変換した後、OAS semantic parserとinternal reference resolverを通し、
Roslyn非依存のgeneration modelを作ってから、Roslynでparseしたdeterministic source emitterへ渡します。
emitterの結果を`AddSource`します。serializer/backendは固定で、generated clientは
Newtonsoft.Jsonと`System.Net.Http.HttpClient`を使用します。Docker providerは別providerであり、
fallbackまたは切替は行いません。

生成物はhand-editするsourceではなく、再Generateされるpublic contractです。spec IDを含むhint nameは
stableで、generated nameはordinalのdeterministic namingとsuffixによるcollision resolutionを使用します。
複数specでDTO名が重なる場合は、generated namespaceを分けてください。current generated typeは
namespace-levelのため、同じnamespaceには共存できません。

## 診断

| ID | 意味 |
| --- | --- |
| `OACG100` | InvalidDocument |
| `OACG101` | UnsupportedElement（bare non-Schema、`$id`/`$anchor`/`$dynamicAnchor`/`$dynamicRef`など） |
| `OACG102` | Unresolved document or JSON Pointer target |
| `OACG103` | Cyclic reference |
| `OACG104` | ExternalReference（Bundle v1 compatibility） |
| `OACG105` | InconsistentResponse |
| `OACG106` | InvalidIdentifier |
| `OACG107` | Warning: deterministic type-name collision resolution |

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
