using System;

namespace ReBeat.OpenApiCodeGen.UI
{
    public enum OpenApiDependenceLibrary
    {
        UnityWebRequest,
    }

    public static class OpenApiDependenceLibraryExtend
    {
        public static string ToConfigString(this OpenApiDependenceLibrary openApiDependenceLibrary)
        {
            return openApiDependenceLibrary switch
            {
                OpenApiDependenceLibrary.UnityWebRequest => "unityWebRequest",
                _ => throw new ArgumentException("Cannot convert to string"),
            };
        }

        public static OpenApiDependenceLibrary ConvertFromString(string value)
        {
            return value switch
            {
                "unityWebRequest" => OpenApiDependenceLibrary.UnityWebRequest,
                _ => throw new NotImplementedException(),
            };
        }
    }
}