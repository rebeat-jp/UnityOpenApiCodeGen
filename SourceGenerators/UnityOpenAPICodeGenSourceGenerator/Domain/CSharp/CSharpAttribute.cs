using System.Collections.Generic;

namespace ReBeat.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// 属性のドメインモデル。
    /// Domain model for attribute.
    /// </summary>
    internal sealed class CSharpAttribute
    {
        public string Name { get; }
        public IReadOnlyList<string> Arguments { get; }

        public CSharpAttribute(string name, IReadOnlyList<string> arguments)
        {
            Name = name;
            Arguments = arguments;
        }
    }
}
