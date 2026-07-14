using System.Collections.Generic;

namespace ReBeat.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// C# 型宣言の共通ベース。
    /// Base for C# type declarations.
    /// </summary>
    internal abstract class CSharpTypeDeclaration
    {
        public string Name { get; }
        public CSharpTypeKind Kind { get; }
        public IReadOnlyList<string> Modifiers { get; }
        public IReadOnlyList<CSharpAttribute> Attributes { get; }
        public IReadOnlyList<string> GenericParameters { get; }
        public IReadOnlyList<string> BaseTypes { get; }
        public IReadOnlyList<string> Comments { get; }

        protected CSharpTypeDeclaration(
            string name,
            CSharpTypeKind kind,
            IReadOnlyList<string> modifiers,
            IReadOnlyList<CSharpAttribute> attributes,
            IReadOnlyList<string> genericParameters,
            IReadOnlyList<string> baseTypes,
            IReadOnlyList<string> comments)
        {
            Name = name;
            Kind = kind;
            Modifiers = modifiers;
            Attributes = attributes;
            GenericParameters = genericParameters;
            BaseTypes = baseTypes;
            Comments = comments;
        }
    }
}
