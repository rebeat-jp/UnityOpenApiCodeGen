using System;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    internal sealed class NormalizedSpecDocument
    {
        internal NormalizedSpecDocument(
            string documentId,
            string sourcePath,
            string format,
            string rawSha256,
            SpecNode root)
        {
            DocumentId = documentId ?? throw new ArgumentNullException(nameof(documentId));
            SourcePath = sourcePath ?? throw new ArgumentNullException(nameof(sourcePath));
            Format = format ?? throw new ArgumentNullException(nameof(format));
            RawSha256 = rawSha256 ?? throw new ArgumentNullException(nameof(rawSha256));
            Root = root ?? throw new ArgumentNullException(nameof(root));
        }

        internal string DocumentId { get; }

        internal string SourcePath { get; }

        internal string Format { get; }

        internal string RawSha256 { get; }

        internal SpecNode Root { get; }
    }
}
