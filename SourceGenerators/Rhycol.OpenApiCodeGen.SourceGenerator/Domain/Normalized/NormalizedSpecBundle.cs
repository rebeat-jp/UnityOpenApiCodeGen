namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    internal sealed class NormalizedSpecBundle
    {
        internal NormalizedSpecBundle(
            string specId,
            string rawSha256,
            string sourcePath,
            SpecNode root)
        {
            SpecId = specId;
            RawSha256 = rawSha256;
            SourcePath = sourcePath;
            Root = root;
        }

        internal string SpecId { get; }

        internal string RawSha256 { get; }

        internal string SourcePath { get; }

        internal SpecNode Root { get; }
    }
}
