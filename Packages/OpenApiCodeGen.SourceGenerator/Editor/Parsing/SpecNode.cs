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
}
