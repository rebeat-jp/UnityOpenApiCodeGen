namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// 生成クライアントが使用する JSON シリアライザ。
    /// Identifies the JSON serializer used by a generated client.
    /// </summary>
    public enum OpenApiJsonSerializer
    {
        NewtonsoftJson = 0,
        SystemTextJson = 1,
    }
}
