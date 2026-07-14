using System.Collections.Generic;

namespace ReBeat.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// インターフェース宣言のドメインモデル。
    /// Domain model for interface declaration.
    /// </summary>
    internal sealed class CSharpInterfaceDeclaration : CSharpTypeDeclaration
    {
        public IReadOnlyList<string> Members { get; }

        public CSharpInterfaceDeclaration(
            string name,
            IReadOnlyList<string> modifiers,
            IReadOnlyList<CSharpAttribute> attributes,
            IReadOnlyList<string> genericParameters,
            IReadOnlyList<string> baseTypes,
            IReadOnlyList<string> members,
            IReadOnlyList<string> comments)
            : base(name, CSharpTypeKind.Interface, modifiers, attributes, genericParameters, baseTypes, comments)
        {
            Members = members;
        }
    }
}
