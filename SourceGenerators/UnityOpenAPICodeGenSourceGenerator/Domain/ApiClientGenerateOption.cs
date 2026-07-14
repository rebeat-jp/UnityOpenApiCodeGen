namespace ReBeat.OpenApiCodeGen.SourceGenerator
{
    public class ApiClientGenerateOption
    {
        /// <summary>
        /// API クライアントのクラス名。
        /// API client class name.
        /// </summary>
        public string ApiName { get; set; } = "Api";
        /// <summary>
        /// 生成コードの名前空間。
        /// Namespace for generated code.
        /// </summary>
        public string Namespace { get; set; } = "ReBeat.OpenApiCodeGen.Generated";
        /// <summary>
        /// 使用する HTTP ライブラリ。
        /// HTTP client library.
        /// </summary>
        public HttpLibrary HttpLibraryType { get; set; } = HttpLibrary.UnityWebRequest;
        /// <summary>
        /// 使用する JSON ライブラリ。
        /// JSON library.
        /// </summary>
        public JsonLibrary JsonLibraryType { get; set; } = JsonLibrary.SystemTextJson;
        /// <summary>
        /// 対象フレームワーク。
        /// Target frameworks.
        /// </summary>
        public TargetFramework TargetFrameworks { get; set; } = TargetFramework.NetStandard2_0;
        /// <summary>
        /// データモデル生成で record を使う。
        /// Use record for data model generation.
        /// </summary>
        public bool UseRecordForDataModel { get; set; } = false;
        /// <summary>
        /// インターフェースの接頭辞。
        /// Interface prefix.
        /// </summary>
        public string InterfacePrefix { get; set; } = "I";
        /// <summary>
        /// 参照型の nullable を有効にする。
        /// Use nullable reference types.
        /// </summary>
        public bool UseNullableReferenceTypes { get; set; } = true;
        /// <summary>
        /// OneOf 判定の最適化を有効にする。
        /// Use OneOf discriminator lookup optimization.
        /// </summary>
        public bool UseOneOfDiscriminatorLookup { get; set; } = false;
        /// <summary>
        /// 非 public API を生成する。
        /// Generate non-public API.
        /// </summary>
        public bool NonPublicApi { get; set; } = true;
        /// <summary>
        /// 優先する media type 一覧（先勝）。
        /// Preferred media types (first match wins).
        /// </summary>
        public string[] MediaTypePriority { get; set; } = new[]
        {
            "application/json",
            "application/*+json",
            "text/json",
            "*/*"
        };

    }
}
