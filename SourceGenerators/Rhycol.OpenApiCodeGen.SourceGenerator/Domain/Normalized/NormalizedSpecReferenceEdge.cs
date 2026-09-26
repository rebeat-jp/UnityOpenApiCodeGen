using System;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    internal sealed class NormalizedSpecReferenceEdge
    {
        internal NormalizedSpecReferenceEdge(
            string sourceDocumentId,
            string sourcePointer,
            string targetDocumentId,
            string targetPointer)
        {
            SourceDocumentId = sourceDocumentId ?? throw new ArgumentNullException(nameof(sourceDocumentId));
            SourcePointer = sourcePointer ?? throw new ArgumentNullException(nameof(sourcePointer));
            TargetDocumentId = targetDocumentId ?? throw new ArgumentNullException(nameof(targetDocumentId));
            TargetPointer = targetPointer ?? throw new ArgumentNullException(nameof(targetPointer));
        }

        internal string SourceDocumentId { get; }

        internal string SourcePointer { get; }

        internal string TargetDocumentId { get; }

        internal string TargetPointer { get; }

        internal NormalizedSpecNodeIdentity SourceIdentity =>
            new NormalizedSpecNodeIdentity(SourceDocumentId, SourcePointer);

        internal NormalizedSpecNodeIdentity TargetIdentity =>
            new NormalizedSpecNodeIdentity(TargetDocumentId, TargetPointer);
    }
}
