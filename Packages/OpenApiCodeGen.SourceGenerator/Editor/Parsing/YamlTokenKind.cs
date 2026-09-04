namespace Rhycol.OpenApiCodeGen.SourceGenerator.Editor
{
    internal enum YamlTokenKind
    {
        Indent,
        NewLine,
        DocumentStart,
        DocumentEnd,
        Dash,
        Colon,
        Comma,
        FlowMappingStart,
        FlowMappingEnd,
        FlowSequenceStart,
        FlowSequenceEnd,
        Scalar,
        BlockScalarHeader,
        BlockScalarText,
        Anchor,
        Alias,
        Comment,
        EndOfInput
    }
}
