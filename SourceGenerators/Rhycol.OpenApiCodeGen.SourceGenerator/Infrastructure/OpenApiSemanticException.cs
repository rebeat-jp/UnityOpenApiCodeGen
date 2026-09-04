using System;
using System.Collections.Generic;

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
            : this(kind, message, OpenApiSourceLocation.FromNode(node), Array.Empty<OpenApiSourceLocation>())
        {
        }

        internal OpenApiSemanticException(
            OpenApiSemanticErrorKind kind,
            string message,
            OpenApiSourceLocation location,
            IReadOnlyList<OpenApiSourceLocation> additionalLocations)
            : base(message)
        {
            if (additionalLocations is null)
            {
                throw new ArgumentNullException(nameof(additionalLocations));
            }

            Kind = kind;
            Location = location;
            AdditionalLocations = additionalLocations;
        }

        internal OpenApiSemanticErrorKind Kind { get; }

        internal OpenApiSourceLocation Location { get; }

        internal IReadOnlyList<OpenApiSourceLocation> AdditionalLocations { get; }
    }
}
