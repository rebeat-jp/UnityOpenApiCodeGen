namespace Rhycol.OpenApiCodeGen.SourceGenerator.Editor
{
    internal sealed class SpecStringNode : SpecNode
    {
        internal SpecStringNode(int line, int column, string value)
            : base(line, column)
        {
            Value = value;
        }

        internal string Value { get; }
    }
}
