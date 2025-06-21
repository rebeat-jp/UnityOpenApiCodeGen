using System;

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
                _ => throw new ArgumentException("Cannot convert to string"),

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
                _ => throw new NotImplementedException(),
            };
        }
    }
}