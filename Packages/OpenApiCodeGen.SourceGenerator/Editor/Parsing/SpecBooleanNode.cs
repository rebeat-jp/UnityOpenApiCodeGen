namespace Rhycol.OpenApiCodeGen.SourceGenerator.Editor
{
    internal sealed class SpecBooleanNode : SpecNode
    {
        internal SpecBooleanNode(int line, int column, bool value)
            : base(line, column)
        {
            Value = value;
        }

        internal bool Value { get; }
    }
}
