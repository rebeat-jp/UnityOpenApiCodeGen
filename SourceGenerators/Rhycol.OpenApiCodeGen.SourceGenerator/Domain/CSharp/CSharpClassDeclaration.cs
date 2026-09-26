using System.Collections.Generic;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// クラス宣言のドメインモデル。
    /// Domain model for class declaration.
    /// </summary>
    internal sealed class CSharpClassDeclaration : CSharpTypeDeclaration
    {
        public IReadOnlyList<string> Members { get; }
        public IReadOnlyList<CSharpMethodDeclaration> Methods { get; }

        public CSharpClassDeclaration(
            string name,
            IReadOnlyList<string> modifiers,
            IReadOnlyList<CSharpAttribute> attributes,
            IReadOnlyList<string> genericParameters,
            IReadOnlyList<string> baseTypes,
            IReadOnlyList<string> members,
            IReadOnlyList<CSharpMethodDeclaration> methods,
            IReadOnlyList<string> comments)
            : base(name, CSharpTypeKind.Class, modifiers, attributes, genericParameters, baseTypes, comments)
        {
            Members = members;
            Methods = methods;
        }
    }
}
