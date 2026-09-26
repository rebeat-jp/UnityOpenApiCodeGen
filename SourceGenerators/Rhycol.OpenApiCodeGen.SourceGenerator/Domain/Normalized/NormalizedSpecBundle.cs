using System;
using System.Collections.Generic;
using System.Linq;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    internal sealed class NormalizedSpecBundle
    {
        internal NormalizedSpecBundle(
            string specId,
            string rawSha256,
            string sourcePath,
            SpecNode root)
            : this(
                1,
                specId,
                rawSha256,
                new[]
                {
                    new NormalizedSpecDocument("root", sourcePath, "json", rawSha256, root)
                },
                Array.Empty<NormalizedSpecReferenceEdge>())
        {
        }

        internal NormalizedSpecBundle(
            int formatVersion,
            string specId,
            string rawSha256,
            IReadOnlyList<NormalizedSpecDocument> documents,
            IReadOnlyList<NormalizedSpecReferenceEdge> referenceEdges)
        {
            if (documents is null)
            {
                throw new ArgumentNullException(nameof(documents));
            }

            SpecId = specId;
            RawSha256 = rawSha256;
            FormatVersion = formatVersion;
            Documents = documents;
            ReferenceEdges = referenceEdges ?? throw new ArgumentNullException(nameof(referenceEdges));

            NormalizedSpecDocument? rootDocument = documents.FirstOrDefault(
                static document => string.Equals(document.DocumentId, "root", StringComparison.Ordinal));
            if (rootDocument is null)
            {
                throw new ArgumentException("A normalized spec bundle must contain a root document.", nameof(documents));
            }

            SourcePath = rootDocument.SourcePath;
            Root = rootDocument.Root;
        }

        internal int FormatVersion { get; }

        internal string SpecId { get; }

        internal string RawSha256 { get; }

        internal string SourcePath { get; }

        internal SpecNode Root { get; }

        internal IReadOnlyList<NormalizedSpecDocument> Documents { get; }

        internal IReadOnlyList<NormalizedSpecReferenceEdge> ReferenceEdges { get; }

        internal bool IsMultiDocument => FormatVersion >= 2;

        internal bool TryGetDocument(string documentId, out NormalizedSpecDocument? document)
        {
            foreach (NormalizedSpecDocument candidate in Documents)
            {
                if (string.Equals(candidate.DocumentId, documentId, StringComparison.Ordinal))
                {
                    document = candidate;
                    return true;
                }
            }

            document = null;
            return false;
        }
    }
}
