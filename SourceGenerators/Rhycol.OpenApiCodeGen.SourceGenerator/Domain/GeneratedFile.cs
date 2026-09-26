namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// 生成結果の 1 ファイル分の出力。
    /// Output for a single generated file.
    /// </summary>
    public sealed class GeneratedFile
    {
        /// <summary>
        /// 出力するファイル名（パスなし）。
        /// File name to emit (no path).
        /// </summary>
        public string FileName { get; }
        /// <summary>
        /// 生成されたソースコード本文。
        /// Generated source content.
        /// </summary>
        public string Content { get; }

        /// <summary>
        /// 生成ファイル情報を作成する。
        /// Creates a generated file record.
        /// </summary>
        public GeneratedFile(string fileName, string content)
        {
            FileName = fileName;
            Content = content;
        }
    }
}
