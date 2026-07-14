using System.Collections.Generic;

namespace ReBeat.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// C# ファイルを表現するドメインモデル。
    /// Domain model for a C# file.
    /// </summary>
    internal sealed class CSharpFile
    {
        public string FileName { get; }
        public string Namespace { get; }
        public IReadOnlyList<string> Usings { get; }
        public IReadOnlyList<CSharpTypeDeclaration> Types { get; }

        public CSharpFile(
            string fileName,
            string @namespace,
            IReadOnlyList<string> usings,
            IReadOnlyList<CSharpTypeDeclaration> types)
        {
            FileName = fileName;
            Namespace = @namespace;
            Usings = usings;
            Types = types;
        }
    }
}
