namespace ReBeat.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// CSharpFile からソースコードを生成する。
    /// Generates source code from CSharpFile.
    /// </summary>
    internal interface ICSharpCodeGenerator
    {
        /// <summary>
        /// CSharpFile を実際のソースファイルとして生成する。
        /// Generates source file from CSharpFile.
        /// </summary>
        GeneratedFile Generate(CSharpFile file);
    }
}
