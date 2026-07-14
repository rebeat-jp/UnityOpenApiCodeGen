using System.Collections.Generic;

namespace ReBeat.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// メソッド宣言のドメインモデル。
    /// Domain model for a method declaration.
    /// </summary>
    internal sealed class CSharpMethodDeclaration
    {
        public string Name { get; }
        public string ReturnType { get; }
        public IReadOnlyList<string> Modifiers { get; }
        public IReadOnlyList<CSharpAttribute> Attributes { get; }
        public IReadOnlyList<string> GenericParameters { get; }
        public IReadOnlyList<string> Constraints { get; }
        public IReadOnlyList<CSharpParameter> Parameters { get; }
        public string? Body { get; }

        public CSharpMethodDeclaration(
            string name,
            string returnType,
            IReadOnlyList<string> modifiers,
            IReadOnlyList<CSharpAttribute> attributes,
            IReadOnlyList<string> genericParameters,
            IReadOnlyList<string> constraints,
            IReadOnlyList<CSharpParameter> parameters,
            string? body)
        {
            Name = name;
            ReturnType = returnType;
            Modifiers = modifiers;
            Attributes = attributes;
            GenericParameters = genericParameters;
            Constraints = constraints;
            Parameters = parameters;
            Body = body;
        }
    }
}
