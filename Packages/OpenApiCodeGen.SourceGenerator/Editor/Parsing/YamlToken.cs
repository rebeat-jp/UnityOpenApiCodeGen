namespace Rhycol.OpenApiCodeGen.SourceGenerator.Editor
{
    internal sealed class YamlToken
    {
        internal YamlToken(YamlTokenKind kind, string value, YamlSourceSpan span)
        {
            Kind = kind;
            Value = value;
            Span = span;
        }

        internal YamlTokenKind Kind { get; }

        internal string Value { get; }

        internal YamlSourceSpan Span { get; }

        internal int Line => Span.Start.Line;

        internal int Column => Span.Start.Column;

        internal int Offset => Span.Offset;

        internal int Length => Span.Length;

        internal string SourcePath => Span.SourcePath;

        public override string ToString()
        {
            return Kind + " '" + Value + "' @" + Span.Start;
        }
    }
}
