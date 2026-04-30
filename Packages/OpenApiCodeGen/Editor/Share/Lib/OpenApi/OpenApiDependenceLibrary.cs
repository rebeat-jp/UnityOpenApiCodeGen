using ReBeat.OpenApiCodeGen.Core;

namespace ReBeat.OpenApiCodeGen.UI
{
    public enum OpenApiDependenceLibrary
    {
        UnityWebRequest,
        GenericHost,
        HttpClient,
        RestSharp
    }

    public static class OpenApiDependenceLibraryExtend
    {
        public static string ToConfigString(this OpenApiDependenceLibrary openApiDependenceLibrary)
        {
            return openApiDependenceLibrary switch
            {
                OpenApiDependenceLibrary.UnityWebRequest => "unityWebRequest",
                OpenApiDependenceLibrary.GenericHost => "generichost",
                OpenApiDependenceLibrary.HttpClient => "httpclient",
                OpenApiDependenceLibrary.RestSharp => "restsharp",
                _ => throw new DomainException("OpenAPI依存ライブラリを設定文字列に変換できません。"),

            };
        }

        public static OpenApiDependenceLibrary ConvertFromString(string value)
        {
            return value.ToLower() switch
            {
                "unitywebrequest" => OpenApiDependenceLibrary.UnityWebRequest,
                "generichost" => OpenApiDependenceLibrary.GenericHost,
                "httpclient" => OpenApiDependenceLibrary.HttpClient,
                "restsharp" => OpenApiDependenceLibrary.RestSharp,
                _ => throw new DomainException($"OpenAPI依存ライブラリに変換できない値です。value: {value}"),
            };
        }
    }
}
