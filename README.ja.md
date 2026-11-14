# Unity OpenAPI CodeGen

[English](README.md) · [Source Generatorの対応表](SourceGenerators/OpenApiMvpSupportMatrix.ja.md)

Unity OpenAPI CodeGenは、OpenAPI文書から型付きのC# RESTクライアントを生成します。
このリポジトリには、Editor画面とDocker方式を提供する基本パッケージと、Unity 6向けの
Source Generator追加パッケージがあります。**Source Generatorによる生成はベータ版**です。

## 動作要件

| パッケージ | 最低Unity版 | 追加要件 |
| --- | --- | --- |
| `jp.rhycol.openapicodegen` | 2021.3 | Docker方式を選ぶ場合のみDocker |
| `jp.rhycol.openapicodegen.source-generator` | 6000.0 | 基本パッケージと、選んだ組み合わせのライブラリ（[HTTPとJSONのライブラリを選ぶ](#httpとjsonのライブラリを選ぶ)を参照）。既定ではNewtonsoft.Json 13.0以上を利用者が導入します |

## インストール

Source Generatorを使う場合は、Unityプロジェクトの`Packages/manifest.json`に両方を追加します。

```json
{
  "dependencies": {
    "jp.rhycol.openapicodegen": "https://github.com/rebeat-jp/UnityOpenApiCodeGen.git?path=/Packages/OpenApiCodeGen#0.5.1",
    "jp.rhycol.openapicodegen.source-generator": "https://github.com/rebeat-jp/UnityOpenApiCodeGen.git?path=/Packages/OpenApiCodeGen.SourceGenerator#0.5.1"
  }
}
```

例は`0.5.1`のリリースタグを使います。未リリースのコミットを試す場合は、両URLの`0.5.1`を同じ検証済みコミットSHAへ
置き換えてください。基本パッケージだけを導入してDocker方式を使うこともできます。

0.6.0から、Source Generatorパッケージは`com.unity.nuget.newtonsoft-json`に依存しません。追加パッケージを入れても
Newtonsoft.Jsonは導入されず、Newtonsoft.Json・System.Text.Json・Roslyn・YamlDotNetのDLLも同梱しません。
選んだ組み合わせのライブラリは利用者が導入します（次の節）。0.5.xから既定の組み合わせのまま更新する場合は、
`com.unity.nuget.newtonsoft-json` `3.2.2`を導入してください。導入するまで、既存の定義は`OACG011`になります。

## 生成手順

1. `Window/OpenAPI Code Generator/Settings`で`Docker`または`Source Generator (Beta)`を選びます。
2. Docker方式では、`Window/OpenAPI Code Generator/Setup`でDocker実行ファイルを設定します。
3. `Window/OpenAPI Code Generator/Generator`でOpenAPI文書のパスまたはHTTP(S) URLと、
   `Assets`配下の出力フォルダーを指定して`Generate`を押します。URLの取得・更新はこの操作時だけです。
4. Source Generatorの生成先asmdefから`Unity.OpenApiCodeGen.SourceGenerator`を直接参照します。

Source Generatorはローカルの`.json`、`.yaml`、`.yml`とHTTP(S) URLを受け付けます。
外部`$ref`はEditorが解決し、1つの仕様を1つのBundle v2とAdditionalFileとして公開します。
Analyzer自身はネットワークとファイルシステムへアクセスしません。対応するOpenAPI要素、YAMLの範囲、
診断と上限は[日本語対応表](SourceGenerators/OpenApiMvpSupportMatrix.ja.md)を確認してください。

ローカル外部参照はシンボリックリンク解決後もUnityプロジェクト内に限られます。URLでは公開・非公開・
loopbackホストを利用できますが、userinfo、HTTP(S)以外の方式、HTTPSからHTTPへの参照・
リダイレクト、リモート文書からローカルファイルへの参照は拒否されます。query付きURLは次回の
`Generate`時に再入力してください。queryは設定・Bundle・診断へ平文で保存されません。

生成に失敗しても最後に成功したキャッシュとコンパイラー入力は保持されます。内容が変わらなければ
スクリプトの再コンパイルは要求しません。キャンセルは公開処理の開始前まで受け付けます。

## HTTPとJSONのライブラリを選ぶ

Source Generatorのclientは、1つのHTTP実装と1つのJSONシリアライザー向けに生成されます。既定は
**HttpClient + Newtonsoft.Json**で、下の2つの選択肢の4通りの組み合わせがすべて実装済みです。

| 選択肢 | プロジェクトに必要なもの | 最低版 |
| --- | --- | --- |
| HTTP: `HttpClient`（既定） | `System.Net.Http`（通常は何も導入せずに使えます） | - |
| HTTP: `UnityWebRequest` | Unity Web Requestモジュール（`com.unity.modules.unitywebrequest`）と`System.Net.Http`。アダプターが`HttpRequestMessage`／`HttpContent`を組み立てるためです | - |
| JSON: `Newtonsoft.Json`（既定） | `com.unity.nuget.newtonsoft-json` `3.2.2`、または他のNewtonsoft.Json参照 | 13.0 |
| JSON: `System.Text.Json` | UnityNuGetの`org.nuget.system.text.json`（`System.Text.Encodings.Web`も付属）、またはNuGetForUnityや手動配置のDLL | 8.0 |

- **選択**は`Window/OpenAPI Code Generator/Settings`の**Source Generator Libraries**で行います。選択肢ごとの状態を表示し、
  準備できていない選択肢は選べません。Newtonsoft.JsonとUnity Web Requestモジュールは導入ボタンがあります。
  System.Text.Jsonは手順の表示だけです（UnityNuGetのscoped registryと`org.nuget.system.text.json` 8.0以上）。
  Editorは`Packages/manifest.json`を自動編集しません。ライブラリは出力アセンブリの参照から検出するため、
  NuGetForUnityや手動配置のDLLでも動作します。
- Generatorウィンドウには組み合わせと準備状況を表示し、ライブラリが不足している間は理由とSettingsへの導線を
  示して`Generate`を無効にします。
- 選択は、定義ファイルの`OpenApiClientDefinitionAttribute`の名前付きプロパティ`HttpTransport`／`JsonSerializer`
  （例: `HttpTransport = OpenApiHttpTransport.UnityWebRequest`）として保存されます。既定値は書き出さないため、
  0.6.0より前に作った定義ファイルは変わらず、HttpClient + Newtonsoft.Jsonのままです。
- **WebGL**では`UnityWebRequest`を選んでください。`System.Net.Http.HttpClient`はWebGLのPlayerでは動作しません。
  Settingsが警告を表示し、Analyzerは警告`OACG014`を報告します。

最終的な判定は、プロジェクトのコンパイル時にAnalyzerが行います。client定義に対して次の診断を報告します。

| ID | 重大度 | 報告する場面 |
| --- | --- | --- |
| `OACG010` | Error | `HttpTransport`／`JsonSerializer`の値が不明、または実装のない組み合わせ |
| `OACG011` | Error | 選択したJSONライブラリが参照されていない |
| `OACG012` | Error | 選択したHTTP実装が参照されていない（`UnityWebRequest`は`System.Net.Http`も必要） |
| `OACG013` | Error | 参照しているライブラリが最低版未満（Newtonsoft.Json 13.0、System.Text.Json 8.0） |
| `OACG014` | Warning | `UNITY_WEBGL`が定義されている状態で`HttpClient`を選んでいる |

Errorのあるclientは生成されません。System.Text.JsonとNewtonsoft.Jsonの挙動の違い、UnityWebRequestの制約、
IL2CPP／WebGL Playerの検証結果（Unity `6000.3.2f1`）は[日本語対応表](SourceGenerators/OpenApiMvpSupportMatrix.ja.md)に
まとめています。

## 資料と検証

- [基本パッケージの日本語README](Packages/OpenApiCodeGen/README.ja.md)
- [Source Generatorの日本語README](Packages/OpenApiCodeGen.SourceGenerator/README.ja.md)
- [OpenAPI日本語対応表](SourceGenerators/OpenApiMvpSupportMatrix.ja.md)
- [Bundle v2形式](SourceGenerators/NormalizedSpecBundleV2.md)
- [リリース・復旧手順](RELEASE.md)

CIは.NET、同梱Analyzer、パッケージ内容と、Unity `2021.3.19f1`、`6000.0.23f1`、
`6000.3.2f1`の検証を行います。同じ候補から作った検証結果がそろった場合にCDのdry-runを実行します。
正式公開とOpenUPM登録は別作業です。現在の結果は[PR #56](https://github.com/rebeat-jp/UnityOpenApiCodeGen/pull/56)で確認できます。

## ライセンス

[MIT License](LICENSE)で公開しています。
