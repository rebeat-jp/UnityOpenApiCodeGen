namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    internal sealed class ResolvedSpecReference
    {
        internal ResolvedSpecReference(
            string documentId,
            string pointer,
            NormalizedSpecDocument? document,
            SpecNode node)
        {
            DocumentId = documentId;
            Pointer = pointer;
            Document = document;
            Node = node;
        }

        internal string DocumentId { get; }

        internal string Pointer { get; }

        internal NormalizedSpecDocument? Document { get; }

        internal SpecNode Node { get; }

        internal NormalizedSpecNodeIdentity Identity =>
            new NormalizedSpecNodeIdentity(DocumentId, Pointer);
    }
}
