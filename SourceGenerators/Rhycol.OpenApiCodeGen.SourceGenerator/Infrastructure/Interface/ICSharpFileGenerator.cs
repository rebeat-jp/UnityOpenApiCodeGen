namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// API ドキュメントから CSharpFile を生成する。
    /// Generates CSharpFile from API document.
    /// </summary>
    internal interface ICSharpFileGenerator<TDocument>
    {
        /// <summary>
        /// ドキュメントと生成オプションから CSharpFile を生成する。
        /// Generates CSharpFile from document and options.
        /// </summary>
        CSharpFile Generate(TDocument document, ApiClientGenerateOption option);
    }
}
