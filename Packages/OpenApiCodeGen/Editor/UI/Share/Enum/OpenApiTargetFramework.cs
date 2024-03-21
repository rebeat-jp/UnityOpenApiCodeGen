using System;

namespace ReBeat.OpenApiCodeGen.UI
{
    public enum OpenApiTargetFramework
    {
        DotnetStandard2_1
    }

    public static class OpenApiTargetFrameworkExtend
    {
        public static string ToConfigString(this OpenApiTargetFramework openApiTargetFramework)
        {
            return openApiTargetFramework switch
            {
                OpenApiTargetFramework.DotnetStandard2_1 => "netstandard2.1",
                _ => throw new ArgumentException("Cannot convert to string"),
            };
        }

        public static OpenApiTargetFramework ConvertFromString(string value)
        {
            return value switch
            {
                "netstandard2.1" => OpenApiTargetFramework.DotnetStandard2_1,
                _ => throw new ArgumentException($"Cannot convert from value: {value}"),
            };
        }
    }
}

