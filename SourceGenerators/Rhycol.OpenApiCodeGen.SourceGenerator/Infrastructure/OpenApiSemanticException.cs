using System;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    internal enum OpenApiSemanticErrorKind
    {
        InvalidDocument,
        UnsupportedElement,
        UnresolvedReference,
        CyclicReference,
        ExternalReference,
        InconsistentResponse,
        InvalidIdentifier
    }

    internal sealed class OpenApiSemanticException : Exception
    {
        internal OpenApiSemanticException(
            OpenApiSemanticErrorKind kind,
            string message,
            SpecNode node)
            : base(message)
        {
            if (node is null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            Kind = kind;
            Location = OpenApiSourceLocation.FromNode(node);
        }

        internal OpenApiSemanticErrorKind Kind { get; }

        internal OpenApiSourceLocation Location { get; }
    }
}
