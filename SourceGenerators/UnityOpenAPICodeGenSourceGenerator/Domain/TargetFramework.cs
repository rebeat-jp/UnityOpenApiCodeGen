using System;

namespace ReBeat.OpenApiCodeGen.SourceGenerator
{
    [Flags]
    /// <summary>
    /// 生成対象のフレームワーク。
    /// Target frameworks for generation.
    /// </summary>
    public enum TargetFramework
    {
        NetStandard2_0 = 0,
        NetStandard2_1 = 1 << 0,
        Net6_0 = 1 << 1,
        Net7_0 = 1 << 2,
        Net8_0 = 1 << 3,
        Net10_0 = 1 << 4,
    }
}
