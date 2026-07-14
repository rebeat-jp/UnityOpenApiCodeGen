using System.Collections.Generic;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Editor
{
    internal abstract class SpecNode
    {
        protected SpecNode(int line, int column)
        {
            Line = line;
            Column = column;
        }

        internal int Line { get; }

        internal int Column { get; }
    }

    internal sealed class SpecObjectNode : SpecNode
    {
        internal SpecObjectNode(int line, int column, IReadOnlyList<SpecProperty> properties)
            : base(line, column)
        {
            Properties = properties;
        }

        internal IReadOnlyList<SpecProperty> Properties { get; }
    }

    internal sealed class SpecArrayNode : SpecNode
    {
        internal SpecArrayNode(int line, int column, IReadOnlyList<SpecNode> items)
            : base(line, column)
        {
            Items = items;
        }

        internal IReadOnlyList<SpecNode> Items { get; }
    }

    internal sealed class SpecStringNode : SpecNode
    {
        internal SpecStringNode(int line, int column, string value)
            : base(line, column)
        {
            Value = value;
        }

        internal string Value { get; }
    }

    internal sealed class SpecNumberNode : SpecNode
    {
        internal SpecNumberNode(int line, int column, bool isInteger, string value)
            : base(line, column)
        {
            IsInteger = isInteger;
            Value = value;
        }

        internal bool IsInteger { get; }

        internal string Value { get; }
    }

    internal sealed class SpecBooleanNode : SpecNode
    {
        internal SpecBooleanNode(int line, int column, bool value)
            : base(line, column)
        {
            Value = value;
        }

        internal bool Value { get; }
    }

    internal sealed class SpecNullNode : SpecNode
    {
        internal SpecNullNode(int line, int column)
            : base(line, column)
        {
        }
    }

    internal sealed class SpecProperty
    {
        internal SpecProperty(string name, int line, int column, SpecNode value)
        {
            Name = name;
            Line = line;
            Column = column;
            Value = value;
        }

        internal string Name { get; }

        internal int Line { get; }

        internal int Column { get; }

        internal SpecNode Value { get; }
    }
}
