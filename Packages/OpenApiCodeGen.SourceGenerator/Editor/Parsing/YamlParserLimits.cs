namespace Rhycol.OpenApiCodeGen.SourceGenerator.Editor
{
    internal static class YamlParserLimits
    {
        internal const int MaximumDepth = 256;
        internal const int MaximumInputCharacters = 4 * 1024 * 1024;
        internal const int MaximumTokens = 1_000_000;
        internal const int MaximumScalarCharacters = 1 * 1024 * 1024;
        internal const int MaximumAliases = 4096;
        internal const int MaximumExpandedNodes = 1_000_000;
        internal const int MaximumAnchors = 4096;
    }
}
