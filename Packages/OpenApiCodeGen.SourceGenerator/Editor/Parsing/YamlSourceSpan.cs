namespace Rhycol.OpenApiCodeGen.SourceGenerator.Editor
{
    internal struct YamlSourceSpan
    {
        internal YamlSourceSpan(YamlSourceLocation start, int length, string sourcePath = "")
        {
            Start = start;
            Length = length;
            SourcePath = sourcePath ?? string.Empty;
        }

        internal YamlSourceLocation Start { get; }

        internal int Length { get; }

        internal int Offset => Start.Offset;

        internal string SourcePath { get; }
    }
}
