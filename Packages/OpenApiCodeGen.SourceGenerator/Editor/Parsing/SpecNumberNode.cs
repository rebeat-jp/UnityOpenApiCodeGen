namespace Rhycol.OpenApiCodeGen.SourceGenerator.Editor
{
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
}
