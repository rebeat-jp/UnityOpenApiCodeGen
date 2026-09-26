using System;
using System.Collections.Generic;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// enum 宣言のドメインモデル。
    /// Domain model for enum declaration.
    /// </summary>
    internal sealed class CSharpEnumDeclaration : CSharpTypeDeclaration
    {
        public IReadOnlyList<CSharpEnumMember> Members { get; }

        public CSharpEnumDeclaration(
            string name,
            IReadOnlyList<string> modifiers,
            IReadOnlyList<CSharpAttribute> attributes,
            IReadOnlyList<string> baseTypes,
            IReadOnlyList<CSharpEnumMember> members,
            IReadOnlyList<string> comments)
            : base(name, CSharpTypeKind.Enum, modifiers, attributes, Array.Empty<string>(), baseTypes, comments)
        {
            Members = members;
        }
    }
}
