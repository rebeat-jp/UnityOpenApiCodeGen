using System.Collections.Generic;

namespace ReBeat.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// 構造体宣言のドメインモデル。
    /// Domain model for struct declaration.
    /// </summary>
    internal sealed class CSharpStructDeclaration : CSharpTypeDeclaration
    {
        public IReadOnlyList<string> Members { get; }
        public IReadOnlyList<CSharpMethodDeclaration> Methods { get; }

        public CSharpStructDeclaration(
            string name,
            IReadOnlyList<string> modifiers,
            IReadOnlyList<CSharpAttribute> attributes,
            IReadOnlyList<string> genericParameters,
            IReadOnlyList<string> baseTypes,
            IReadOnlyList<string> members,
            IReadOnlyList<CSharpMethodDeclaration> methods,
            IReadOnlyList<string> comments)
            : base(name, CSharpTypeKind.Struct, modifiers, attributes, genericParameters, baseTypes, comments)
        {
            Members = members;
            Methods = methods;
        }
    }
}
