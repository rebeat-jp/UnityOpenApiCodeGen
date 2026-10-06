namespace Rhycol.OpenApiCodeGen.SourceGenerator.Editor
{
    internal enum YamlDiagnosticCode
    {
        LexicalError,
        InvalidIndentation,
        InvalidDocument,
        InvalidMapping,
        InvalidSequence,
        InvalidScalar,
        DuplicateKey,
        UnsupportedTag,
        UnsupportedDirective,
        MultipleDocuments,
        ComplexKey,
        UndefinedAlias,
        AliasCycle,
        AnchorRedefinition,
        LimitExceeded
    }
}
