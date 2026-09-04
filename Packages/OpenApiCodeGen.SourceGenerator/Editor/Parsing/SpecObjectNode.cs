using System.Collections.Generic;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Editor
{
    internal sealed class SpecObjectNode : SpecNode
    {
        internal SpecObjectNode(int line, int column, IReadOnlyList<SpecProperty> properties)
            : base(line, column)
        {
            Properties = properties;
        }

        internal IReadOnlyList<SpecProperty> Properties { get; }
    }
}
