# OpenAPI MVP support matrix

## 目的と対象読者

この文書は、`Rhycol.OpenApiCodeGen.SourceGenerator`で生成できるOpenAPI documentの
MVP範囲を、API仕様の作成者とUnity利用者が判断するためのmatrixです。
対象はlocal JSON documentをSource Generator providerで生成する場合です。

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
| 入力document | Supported | localの単一JSON file。 |
| OpenAPI version | Partial | `3.0.*`と`3.1.*`のみ。 |
| OpenAPI 3.2 / Swagger 2 | Unsupported | document versionは受け付けません。 |
| YAML、URL入力 | Unsupported | providerはlocal JSONのみを受け付けます。 |
| `$ref` | Partial | `components/schemas`、`parameters`、`requestBodies`、`responses`へのdirect internal referenceのみ。 |
| external / unresolved / cyclic `$ref` | Unsupported | それぞれ`OACG104`、`OACG102`、`OACG103`を報告します。 |

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

生成モデルは、normalized bundleからOAS semantic parserとinternal reference resolverを通し、
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
| `OACG101` | UnsupportedElement |
| `OACG102` | UnresolvedReference |
| `OACG103` | CyclicReference |
| `OACG104` | ExternalReference |
| `OACG105` | InconsistentResponse |
| `OACG106` | InvalidIdentifier |

## 検証済み環境

現在の検証では、.NET testsは75/75、analyzer出力はdeterministicでbyte-identical、packageの
analyzer sync/reference checksは成功しています。base packageはUnity 2021.3で別途33/33を
検証済みです。Source Generator add-onのminimum Unity versionは6000.0です。Unity 6000.0および
6000.3では、DTO/Json.NETを含むSource Generatorのvertical matrix、unchanged/regeneration/scope/
live removalを検証済みです。未検証のUnity versionへこの結果を拡張してはなりません。
