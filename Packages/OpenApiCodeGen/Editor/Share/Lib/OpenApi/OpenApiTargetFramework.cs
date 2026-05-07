using System;
using Rhycol.OpenApiCodeGen.Core;

namespace Rhycol.OpenApiCodeGen.UI
{
    [Flags]
    public enum OpenApiTargetFramework
    {
        DotnetStandard2_1 = 0,
    }

    public static class OpenApiTargetFrameworkExtend
    {
        public static string ToConfigString(this OpenApiTargetFramework openApiTargetFramework)
        {
            return openApiTargetFramework switch
            {
                OpenApiTargetFramework.DotnetStandard2_1 => "netstandard2.1",
                _ => throw new DomainException("OpenAPIターゲットフレームワークを設定文字列に変換できません。"),
            };
        }

        public static OpenApiTargetFramework Parse(string value)
        {
            return value switch
            {
                "netstandard2.1" => OpenApiTargetFramework.DotnetStandard2_1,
                _ => throw new DomainException($"OpenAPIターゲットフレームワークに変換できない値です。value: {value}"),
            };
        }
    }
}

