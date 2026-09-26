using System.Collections.Generic;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Editor
{
    internal sealed class SpecArrayNode : SpecNode
    {
        internal SpecArrayNode(int line, int column, IReadOnlyList<SpecNode> items)
            : base(line, column)
        {
            Items = items;
        }

        internal IReadOnlyList<SpecNode> Items { get; }
    }
}
