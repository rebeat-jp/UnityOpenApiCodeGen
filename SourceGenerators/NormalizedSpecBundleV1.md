# Normalized Spec Bundle v1

## 目的

`Normalized Spec Bundle` は、Unity Editor と Incremental Source Generator の境界で使用する
versioned な中間形式です。

- Editor は raw JSON を `Newtonsoft.Json` で検証し、source location 付きの `SpecNode` へ変換する
- Editor は `SpecNode` を本書の canonical JSON として永続化する
- Source Generator は canonical JSON だけを読み、raw JSON を解析しない
- Source Generator は `Newtonsoft.Json`、`System.Text.Json`、`YamlDotNet`、Unity API を参照しない
- Source Generator はネットワークアクセスとディスク書き込みを行わない

この契約は、Unity の Analyzer load context から UPM の `Newtonsoft.Json` を直接解決できないために
導入します。Docker 生成への暗黙 fallback は行いません。

## ファイル契約

Analyzer assembly 名を `Rhycol.OpenApiCodeGen.SourceGenerator.dll` とし、compiler mirror は次の
case-sensitive suffix を持ちます。

```text
.Rhycol.OpenApiCodeGen.SourceGenerator.additionalfile
```

Phase 1 の標準ファイル名は次のとおりです。`specId` は lower-case の Guid `N` format です。

```text
<specId>.Rhycol.OpenApiCodeGen.SourceGenerator.additionalfile
```

Analyzer は suffix が完全一致しない AdditionalFile を無視します。Phase 1 では対象 assembly ごとに
bundle は最大1件です。複数件を検出した場合は diagnostic にし、どれかを暗黙選択しません。

## Envelope schema

v1 の field 順序は次で固定します。

```json
{
  "formatVersion": 1,
  "specId": "0123456789abcdef0123456789abcdef",
  "rawSha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
  "rootDocumentId": "root",
  "documents": [
    {
      "documentId": "root",
      "sourcePath": "Assets/Specs/openapi.json",
      "root": {
        "kind": "object",
        "line": 1,
        "column": 1,
        "properties": []
      }
    }
  ]
}
```

### Envelope fields

| Field | Contract |
|---|---|
| `formatVersion` | JSON integer。v1 reader は `1` だけを受理する |
| `specId` | 32文字のlower-case hexadecimal Guid `N` format |
| `rawSha256` | raw input bytes のSHA-256。64文字のlower-case hexadecimal |
| `rootDocumentId` | v1では常に`root` |
| `documents` | v1では1件のみ。将来のexternal reference用に配列として予約する |

### Document fields

| Field | Contract |
|---|---|
| `documentId` | v1では常に`root` |
| `sourcePath` | project内はproject-relative、区切りは`/`。project外は正規化済みabsolute path |
| `root` | document rootのnode。JSON objectに限定せず全JSON valueを表現できる |

相対pathにcheckout固有のabsolute prefixを含めません。source identity が変わった場合は、raw bytesが
同じでもbundleを更新します。

## Node schema

全nodeはfield順`kind`、`line`、`column`を共通に持ちます。`line`と`column`は1-basedで、値tokenの
開始位置です。

### Object

```json
{
  "kind": "object",
  "line": 1,
  "column": 1,
  "properties": [
    {
      "name": "openapi",
      "line": 2,
      "column": 3,
      "value": {
        "kind": "string",
        "line": 2,
        "column": 14,
        "value": "3.0.3"
      }
    }
  ]
}
```

- `properties` はraw JSONのsource orderを保持する
- property entryの`line`と`column`はproperty名tokenの開始位置を表す
- duplicate property名はnormalization errorとし、後勝ち・先勝ちにしない

### Array

```json
{
  "kind": "array",
  "line": 1,
  "column": 1,
  "items": []
}
```

`items` はsource orderを保持します。

### String

```json
{
  "kind": "string",
  "line": 1,
  "column": 1,
  "value": "text"
}
```

`value` はdecode済みUnicode stringです。Unicode normalizationは行いません。

### Number

```json
{
  "kind": "number",
  "line": 1,
  "column": 1,
  "numberKind": "integer",
  "value": "123"
}
```

- `numberKind` はfractionまたはexponentを持たない場合`integer`、それ以外は`real`
- `value` は検証済みRFC 8259 number tokenの元lexemeを保持する
- culture依存のparse、丸め、指数展開、`-0`の変換を行わない

### Boolean

```json
{
  "kind": "boolean",
  "line": 1,
  "column": 1,
  "value": true
}
```

### Null

```json
{
  "kind": "null",
  "line": 1,
  "column": 1
}
```

## Logical path

Logical path はbundleへ重複保存せず、tree traversal時にRFC 6901 JSON Pointerとして決定的に復元します。

- rootは空文字列
- object propertyは`/<escaped-name>`
- array itemは`/<zero-based-index>`
- `~`は`~0`、`/`は`~1`へescapeする

Diagnostic は`sourcePath`、1-based line/column、logical pathを必ず保持します。

## Canonical serialization

- encodingはstrict UTF-8、BOMなし
- newlineはLFのみ
- indentationは2 spaces
- document末尾にLFを1つ付与する
- field順は本書に記載した順序で固定する
- object propertyとarray itemはsource orderを保持する
- stringはRFC 8259に従い、quote、backslash、U+0000〜U+001Fをescapeする
- non-ASCII characterはUnicode normalizationせずUTF-8で出力する
- timestamp、absolute cache path、random valueを含めない
- 同じ`formatVersion`、`specId`、source identity、raw bytesからbyte-identicalな出力を得る

v1 readerはschemaをstrictに検証します。未知field、field順違反、missing field、duplicate field、不正な型は
malformed bundleです。将来のschema変更は`formatVersion`を上げ、古いAnalyzerは明示的なunsupported
version diagnosticを返します。

## Raw JSON normalization

- input hashはdecode前のraw bytesから計算する
- UTF-8 BOMは入力として許可するがhashには含め、decode後のtreeには含めない
- invalid UTF-8、comment、trailing comma、duplicate property、複数root valueを拒否する
- 最大nesting depthは256
- `Newtonsoft.Json`のdate自動変換を無効化する
- 構文・制約違反はsource file、line、column、可能な範囲のlogical path付きで返す

OpenAPIの意味解析はnormalizerで行いません。normalizerはJSON syntaxをlosslessな`SpecNode`へ変換する責務だけを
持ちます。

## Cache contract

Authoritative cache:

```text
Library/OpenApiCodeGen/SourceGenerator/SpecCache/<specId>/normalized-v1.json
```

Compiler mirror:

```text
Assets/OpenApiCodeGen/Generated/SpecCache/<specId>.Rhycol.OpenApiCodeGen.SourceGenerator.additionalfile
```

- authoritative cacheとcompiler mirrorのcontentはbyte-identical
- compiler mirrorは再生成可能な生成物としてsource controlから除外する
- committed integration fixtureは標準cache pathとは別のfixture directoryへ置く
- cache hit keyは`formatVersion`、`specId`、source identity、`rawSha256`
- cache hitかつ両fileが同一なら書き込みもtimestamp更新も行わない
- authoritative cacheが正常でmirrorが欠落・破損している場合はmirrorだけを修復する
- raw JSON不正時はどちらも書き換えず、最後に成功したcacheとmirrorを保持してGenerateを失敗させる
- validな更新はmemory上で完全なbundleを生成後、temporary fileからfile単位でatomic replaceする
- mirror更新に失敗した場合はエラーを返し、Analyzerが読む旧mirrorを破壊しない。次回Generateで再同期する
- AssetDatabase refresh/importはmirrorが実際に変わった場合だけ行う

最後に成功したmirrorが残っていても、EditorのGenerate結果は失敗です。別ProviderやDockerを起動して成功扱いには
しません。

## Analyzer contract

- `netstandard2.0`、`Microsoft.CodeAnalysis.CSharp` 4.3.1でbuildする
- BCL-onlyの専用readerでbundleを読む
- raw `.openapi.json`を解析しない
- `Newtonsoft.Json`、`System.Text.Json`、`YamlDotNet`をassembly referenceに持たない
- AdditionalFileをcase-sensitive exact suffixでfilterする
- 対象assemblyにbundleがない場合は何も生成しない
- network、environment依存の取得、file writeを行わない

Diagnostic ID:

| ID | Severity | Meaning |
|---|---|---|
| `OACG001` | Error | malformed normalized bundle |
| `OACG002` | Error | unsupported `formatVersion` |
| `OACG003` | Error | OpenAPI mappingまたはcode generation failure |
| `OACG004` | Error | Phase 1対象assemblyに複数bundleが存在する |

`OACG003`はraw sourceのpath、line、column、logical pathを使用します。bundle schema自体の問題はcompiler
mirrorのpathを使用します。

## Compatibility and migration

- Editor writerとAnalyzer readerは同じpackage versionで配布する
- writerは常に現在versionを出力し、古いauthoritative cacheを現在versionへ再生成する
- readerは対応していないversionを推測して読まない
- format migrationはEditorがraw inputから再生成する。Analyzer内でcache migrationしない
- Phase 1では1 spec / 1 root document / 1 target assemblyを上限とする
- 複数specと属性による対応付けは後続Phaseで追加する

## Verification requirements

- 同じinputを2回normalizeしたbytesとSHA-256が一致する
- cache hit時にauthoritative cacheとmirrorのmtimeが変わらない
- raw input変更時だけ両fileが更新される
- invalid raw JSONで最後の正常cacheを上書きしない
- source locationとlogical pathがAnalyzer diagnosticへ引き継がれる
- committed Analyzerに禁止assembly referenceと依存DLLが存在しない
- Unity 6000.0.23f1と6000.3.2f1のclean compileでmirrorから生成できる
- Source Generator失敗時にDocker生成が呼ばれない
