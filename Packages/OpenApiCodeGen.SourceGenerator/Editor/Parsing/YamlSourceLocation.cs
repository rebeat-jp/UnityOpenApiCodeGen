namespace Rhycol.OpenApiCodeGen.SourceGenerator.Editor
{
    internal struct YamlSourceLocation
    {
        internal YamlSourceLocation(int line, int column, int offset)
        {
            Line = line;
            Column = column;
            Offset = offset;
        }

        internal int Line { get; }

        internal int Column { get; }

        internal int Offset { get; }

        public override string ToString()
        {
            return Line + ":" + Column;
        }
    }
}
