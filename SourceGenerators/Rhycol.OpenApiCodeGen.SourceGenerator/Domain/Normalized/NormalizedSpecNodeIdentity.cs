using System;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    internal readonly struct NormalizedSpecNodeIdentity : IEquatable<NormalizedSpecNodeIdentity>
    {
        internal NormalizedSpecNodeIdentity(string documentId, string pointer)
        {
            DocumentId = documentId ?? throw new ArgumentNullException(nameof(documentId));
            Pointer = pointer ?? throw new ArgumentNullException(nameof(pointer));
        }

        internal string DocumentId { get; }

        internal string Pointer { get; }

        internal bool IsEmpty => string.IsNullOrEmpty(DocumentId) && string.IsNullOrEmpty(Pointer);

        internal static int Compare(
            NormalizedSpecNodeIdentity left,
            NormalizedSpecNodeIdentity right)
        {
            int documentComparison = StringComparer.Ordinal.Compare(left.DocumentId, right.DocumentId);
            return documentComparison != 0
                ? documentComparison
                : StringComparer.Ordinal.Compare(left.Pointer, right.Pointer);
        }

        public bool Equals(NormalizedSpecNodeIdentity other)
        {
            return string.Equals(DocumentId, other.DocumentId, StringComparison.Ordinal) &&
                   string.Equals(Pointer, other.Pointer, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is NormalizedSpecNodeIdentity other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (StringComparer.Ordinal.GetHashCode(DocumentId ?? string.Empty) * 397) ^
                       StringComparer.Ordinal.GetHashCode(Pointer ?? string.Empty);
            }
        }

        public override string ToString()
        {
            return (DocumentId ?? string.Empty) + "\0" + (Pointer ?? string.Empty);
        }
    }
}
