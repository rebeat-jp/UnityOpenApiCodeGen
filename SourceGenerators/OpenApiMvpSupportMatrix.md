# OpenAPI MVP support matrix

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
`OACG100`–`OACG106`を使用します。

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
| OpenAPI 3.2 / Swagger 2 | Unsupported | document versionは受け付けません。 |
| URL入力 | Supported | public/private/loopback、cross-host redirectを許可。userinfo、空白、非HTTP(S)、HTTPS-to-HTTP direct/redirectは拒否。Generateが明示的なfetch/refresh操作で、Docker fallbackはありません。 |
| JSON/YAML semantic parity | Supported | 各normalizerがshared `SpecNode`へ正規化し、同じsemantic/generation pipelineを通ります。 |
| `$ref` | Partial | internal reference、またはEditorがBundle v2のreference edgeへ解決したexternal reference。fragmentはemptyまたはJSON Pointer。 |
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
| complex parameter | Unsupported | array/object等のcomplex parameterは対象外です。 |
| request body | Partial | JSON media typeのみ。`application/json`または`application/*+json`。 |
| successful response | Partial | 少なくとも1つの`2xx` responseが必要です。複数ある場合、contractは一致する必要があります。 |
| response header | Unsupported | nonempty response headerは対象外です。 |

## Schema

| 項目 | Status | 条件・制約 |
| --- | --- | --- |
| scalar | Supported | `string`、`integer`、`number`、`boolean`。 |
| object | Partial | named propertiesと`required`を持つobject。 |
| array | Partial | supported schemaのarray。 |
| string enum | Supported | generated C# enumに`StringEnumConverter`を付与します。 |
| direct schema reference | Supported | supported schemaへのdirect internal reference。 |
| nullable | Partial | OpenAPI 3.0の`nullable`、OpenAPI 3.1の`type: [<type>, null]`。 |
| scalar format | Partial | `integer`は通常`int`、`int64`は`long`。`number`は通常`double`、`float`は`float`、`decimal`は`decimal`。`date`、`date-time`、`uuid`は`DateTime`、`DateTimeOffset`、`Guid`へmappingします。 |
| map / free-form object | Unsupported | `additionalProperties`などのmap/free-form表現は対象外です。 |
| composition | Unsupported | `allOf`、`anyOf`、`oneOf`、`not`、discriminatorは対象外です。 |
| binary / readOnly / writeOnly | Unsupported | 生成contractでは対象外です。 |

## Generated outputと利用上の制約

生成結果は次のpublic API contractです。

- public mutable sealed DTO（Json.NET attribute使用）
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

## 検証済み環境

現在の検証では、.NET testsは101/101、analyzer reproducibility・sync・package inspection・
tarball reproducibility・checksum verificationはpassです。base Unity `2021.3.19f1`のtarball
EditMode gateは36/36でした。Unity `6000.0.23f1`はinitial 185/185、regeneration 185/185、
without-addon 36/36、Unity `6000.3.2f1`もinitial 185/185、regeneration 185/185、
without-addon 36/36で、両方のSource Generator full flowがpassしています。aggregate gateは
`artifacts/upm/0.5.0/unity-gate.json`に`passed`として保存されています。

これはmanual Unity gateの結果であり、GitHub ActionsのUnity jobが実装されたことを意味しません。
そのjobを意図的に追加していないため、#54とPhase 7全体はopenのままで、release-readyとは扱いません。
公開前にはcleanなcommit済みtreeで再pack・再検証し、tarballとmanifestのprovenanceを確定してください。
