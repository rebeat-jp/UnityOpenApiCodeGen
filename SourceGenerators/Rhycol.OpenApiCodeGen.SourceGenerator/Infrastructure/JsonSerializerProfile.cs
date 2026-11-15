using System;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// JSON ライブラリごとの差分（型対応・例外型）を 1 か所に閉じ込めるプロファイル。
    /// 属性・converter・トークン API を使う箇所は、プロファイルの種類で emitter を分ける
    /// （Newtonsoft 向けの出力は変えない）。
    /// The per-library differences (type mapping, exception type) in one place. Where attributes,
    /// converters or the token API are involved the emitters branch on the profile; the output for
    /// Newtonsoft.Json is unchanged.
    /// </summary>
    internal sealed class JsonSerializerProfile
    {
        private const string NewtonsoftSerializationException = "global::Newtonsoft.Json.JsonSerializationException";
        private const string NewtonsoftJsonException = "global::Newtonsoft.Json.JsonException";
        private const string NewtonsoftToken = "global::Newtonsoft.Json.Linq.JToken";
        private const string NewtonsoftObject = "global::Newtonsoft.Json.Linq.JObject";

        internal static readonly JsonSerializerProfile Newtonsoft =
            new JsonSerializerProfile(JsonSerializerKind.NewtonsoftJson);

        internal static readonly JsonSerializerProfile SystemTextJson =
            new JsonSerializerProfile(JsonSerializerKind.SystemTextJson);

        private JsonSerializerProfile(JsonSerializerKind kind)
        {
            Kind = kind;
        }

        internal static JsonSerializerProfile For(JsonSerializerKind kind)
        {
            switch (kind)
            {
                case JsonSerializerKind.NewtonsoftJson:
                    return Newtonsoft;
                case JsonSerializerKind.SystemTextJson:
                    return SystemTextJson;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        internal JsonSerializerKind Kind { get; }

        internal bool IsSystemTextJson => Kind == JsonSerializerKind.SystemTextJson;

        /// <summary>Any: a JSON value of an unknown shape.</summary>
        internal string AnyTypeName => IsSystemTextJson
            ? "global::System.Text.Json.JsonElement"
            : NewtonsoftToken;

        /// <summary>FreeObject: a JSON object without declared members.</summary>
        internal string FreeObjectTypeName => IsSystemTextJson
            ? "global::System.Text.Json.Nodes.JsonObject"
            : NewtonsoftObject;

        /// <summary>Value type of the extension data dictionary of an open DTO.</summary>
        internal string ExtensionDataValueTypeName => IsSystemTextJson
            ? "global::System.Text.Json.JsonElement"
            : NewtonsoftToken;

        /// <summary>The exception that reports malformed or contract-violating JSON.</summary>
        internal string SerializationExceptionName => IsSystemTextJson
            ? "global::System.Text.Json.JsonException"
            : NewtonsoftSerializationException;

        /// <summary>The base exception type of JSON errors (used in catch filters).</summary>
        internal string JsonExceptionName => IsSystemTextJson
            ? "global::System.Text.Json.JsonException"
            : NewtonsoftJsonException;

        internal string TypeName(GeneratedTypeModel type)
        {
            return GeneratedSourceEmitter.TypeName(type, this);
        }

    }
}
