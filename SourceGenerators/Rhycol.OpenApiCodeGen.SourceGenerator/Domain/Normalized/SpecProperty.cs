namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
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
