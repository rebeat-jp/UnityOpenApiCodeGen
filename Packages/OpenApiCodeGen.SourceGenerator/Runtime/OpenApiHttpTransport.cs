namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// 生成クライアントが使用する HTTP 実装。
    /// Identifies the HTTP implementation used by a generated client.
    /// </summary>
    public enum OpenApiHttpTransport
    {
        HttpClient = 0,
        UnityWebRequest = 1,
    }
}
