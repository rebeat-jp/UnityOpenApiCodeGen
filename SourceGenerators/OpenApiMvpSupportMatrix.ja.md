# OpenAPI MVP 対応表

[元の対応表](OpenApiMvpSupportMatrix.md)

**Source Generator (Beta)** による生成はベータです。
利用前にこの対応表で入力仕様と生成されるAPIの制約を確認してください。

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

| 対応状況 | 意味 |
| --- | --- |
| 対応 | 記載した条件で生成・実行対象です。 |
| 一部対応 | 記載した部分だけを生成します。条件外は診断となります。 |
| 非対応 | MVPの対象外です。診断となります。 |

## 入力と参照

| 項目 | 対応状況 | 条件・制約 |
| --- | --- | --- |
| 入力document | 対応 | localの`.json`、`.yaml`、`.yml` file、またはHTTP(S) URL。local extensionはcase-insensitive。 |
| OpenAPI version | 一部対応 | `3.0.*`と`3.1.*`のみ。 |
| OpenAPI 3.1 schema dialect | 一部対応 | 既定または明示的な`https://spec.openapis.org/oas/3.1/dialect/base`のみ。rootの`jsonSchemaDialect`とschemaの`$schema`で別dialectを指定すると`OACG101`、不正なURIは`OACG100`です。 |
| OpenAPI 3.2 / Swagger 2 | 非対応 | document versionは受け付けません。 |
| URL入力 | 対応 | public/private/loopback、cross-host redirectを許可。userinfo、空白、非HTTP(S)、HTTPS-to-HTTP direct/redirectは拒否。Generateが明示的なfetch/refresh操作で、Docker fallbackはありません。 |
| JSON/YAML semantic parity | 対応 | 各normalizerがshared `SpecNode`へ正規化し、同じsemantic/generation pipelineを通ります。 |
| `$ref` | 一部対応 | OpenAPI/Schema上の参照位置にあるinternal reference、またはEditorがBundle v2のedgeへ解決したexternal reference。example/default/enum/拡張データ内の同名キーはデータとして保持。fragmentはemptyまたはJSON Pointer。 |
| external `$ref` | 一部対応 | local external fileはreal/symbolic-link解決後もUnity project内。remote documentからlocal fileは不可。bare Schemaは可。 |
| unresolved / cyclic `$ref` | 非対応 | それぞれ`OACG102`、`OACG103`を報告します。`OACG104`はBundle v1のexternal-reference互換診断です。 |
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
| JSON-compatible scalar | 対応 | `null`、`true`/`false`、RFC 8259 number（integer/real）、string。 |
| literal / folded block scalar | 一部対応 | `|`/`>`、`+`/`-` chomping、explicit indent `1`–`9`。flow内は拒否。 |
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
| HTTP method | 対応 | `GET`、`POST`、`PUT`、`DELETE`、`PATCH`、`HEAD`、`OPTIONS`、`TRACE`。 |
| `servers` | 一部対応 | URLは0個または1個、variablesなし。 |
| parameter location | 一部対応 | path、query、headerのscalar parameterのみ。cookieは対象外です。 |
| parameter serialization | 一部対応 | defaultの`style`/`explode`のみ。`allowReserved: true`は対象外です。 |
| complex parameter | 非対応 | 直接定義・多段`$ref`ともarray/object等のcomplex parameterは対象外です。 |
| required nullable parameter | 非対応 | path/query/headerの`required: true`とnullableの組み合わせは`OACG101`です。参照チェーン全体を確認します。任意parameterのnull省略とDTOのnullableは維持します。 |
| dot-only path segment | 非対応 | 引数置換後のpath segmentが`.`／`..`（1回percent-decodeした表現を含む）なら、HTTP送信前に`ArgumentException`です。`.hidden`、`a.b`、`...`や通常の複合segmentは使用できます。 |
| header identity | 対応 | header名だけ大文字小文字を区別せず、operation側で上書きします。送信名はoperationの宣言を保持し、path/query名は区別します。 |
| request body | 一部対応 | パラメーター付きも含む`application/json`または`application/*+json`。type/subtypeを正規化して判定します。 |
| request charset | 一部対応 | UTF-8、UTF-16 LE/BE。未指定はUTF-8。不正なContent-Typeや未対応charsetは入力位置付き診断です。 |
| successful response | 一部対応 | 少なくとも1つの`2xx` responseが必要です。複数ある場合、contractは一致する必要があります。 |
| error/default response | 一部対応 | JSON以外の本文も許可します。schemaの構造・参照を検証し、成功本文のDTO生成制限は適用しません。例外は実際の本文を保持します。 |
| response extension | 対応 | operationのResponses Objectでは`x-*`を除外します。`components.responses`の`x-*`名は通常のResponse/Referenceです。 |
| response header | 非対応 | nonempty response headerは対象外です。 |

status codeはASCII数字で検証します。属性付きのジェネリックclassは`OACG005`で拒否します。

## スキーマ

以下は生成するrequest／成功response／DTOの制限です。非2xxと`default`の本文は構造・参照検証の対象です。

| 項目 | 対応状況 | 条件・制約 |
| --- | --- | --- |
| scalar | 対応 | `string`、`integer`、`number`、`boolean`。 |
| object | 一部対応 | named propertiesと`required`を持つobject。 |
| array | 一部対応 | supported schemaのarray。 |
| string enum | 一部対応 | generated C# enumに`StringEnumConverter`を付与します。OpenAPI 3.1の`type: [string, null]`でも`enum`に文字列しかなければ非nullableです。`null`を含むenumは`OACG101`です。 |
| direct schema reference | 対応 | supported schemaへの参照。多段参照のscalar／enumもnullableを保持します。 |
| `$ref` sibling | 一部対応 | 注釈、literalデータ、`x-*`と既存の3.0 `nullable`は保持します。合成が必要な`type`、`properties`、`required`などは、該当位置の`OACG101`で拒否します。 |
| nullable | 一部対応 | OpenAPI 3.0の`nullable`、OpenAPI 3.1の`type: [<type>, null]`。 |
| optional nullable DTO property | 対応 | 未代入はJSONから省略し、`null`代入は明示的なJSON `null`、値の代入はその値を送ります。生成される`<PropertyName>Specified`を`false`へ戻すと再び省略します。 |
| scalar format | 一部対応 | `integer`は通常`int`、`int64`は`long`。`number`は通常`double`、`float`は`float`、`decimal`は`decimal`。`date`、`date-time`、`uuid`は`DateTime`、`DateTimeOffset`、`Guid`へmappingします。 |
| map / free-form object | 非対応 | `additionalProperties`などのmap/free-form表現は対象外です。 |
| composition | 非対応 | `allOf`、`anyOf`、`oneOf`、`not`、discriminatorは対象外です。 |
| binary / readOnly / writeOnly | 非対応 | 生成contractでは対象外です。 |

`$ref`と併記できる注釈・データのキーは`$comment`、`default`、`deprecated`、
`description`、`example`、`examples`、`externalDocs`、`summary`、`title`、`xml`、`x-*`です。
OpenAPI 3.0では既存互換として`nullable`も扱います。

リクエストJSONの`format: date`は`yyyy-MM-dd`へ変換します。公開型の`DateTime`は維持し、
`date-time`の`DateTimeOffset`にはdate用converterを適用しません。

## 生成結果と利用上の制約

生成結果は次のpublic API contractです。

- public mutable sealed DTO（Json.NET attribute使用）
- 任意かつschema上nullableなDTO propertyの`<PropertyName>Specified`による存在状態の管理
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
