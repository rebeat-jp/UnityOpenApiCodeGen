using System;
using System.Collections.Generic;
using System.Linq;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// Normalizes Swagger 2.0 documents into equivalent OpenAPI 3.0 SpecNode trees before semantic parsing,
    /// so the whole Phase 4 pipeline (parser, builder, emitters, diagnostics, run-time validation) applies to
    /// Swagger 2.0 unchanged. The synthesized nodes keep the line, column and logical path of the Swagger
    /// nodes they came from (see <see cref="Swagger2NodeFactory"/>), so diagnostics report the original
    /// document locations.
    /// </summary>
    internal static class Swagger2Normalizer
    {
        /// <summary>
        /// A Swagger 2.0 document declares 'swagger' and no 'openapi'. A document that declares both stays
        /// an OpenAPI document and is rejected by the parser.
        /// </summary>
        internal static bool IsSwagger(SpecNode root)
        {
            return root.ValueKind == SpecValueKind.Object &&
                   root.TryGetProperty("swagger", out _) &&
                   !root.TryGetProperty("openapi", out _);
        }

        /// <summary>Normalizes a single document (a bundle without reference edges).</summary>
        internal static SpecNode Normalize(
            SpecNode root,
            List<(SpecNode Node, string Message)>? warnings = null)
        {
            if (root is null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            if (!IsSwagger(root))
            {
                return root;
            }

            var context = new Swagger2Context(root, warnings);
            return new Swagger2DocumentConverter(context, "root", root, isRoot: true).Convert();
        }

        /// <summary>
        /// Normalizes every Swagger 2.0 document of a multi-document bundle and rewrites the reference edges
        /// that start in them. A bundle without a Swagger 2.0 root is returned as is, after checking that no
        /// document references a Swagger 2.0 document (mixing versions is not supported).
        /// </summary>
        internal static NormalizedSpecBundle NormalizeBundle(
            NormalizedSpecBundle bundle,
            List<(SpecNode Node, string Message)>? warnings = null)
        {
            if (bundle is null)
            {
                throw new ArgumentNullException(nameof(bundle));
            }

            if (!bundle.Documents.Any(static document => IsSwagger(document.Root)))
            {
                return bundle;
            }

            var context = new Swagger2Context(bundle, warnings);
            if (!context.IsSwaggerDocument("root"))
            {
                context.RejectReferencesToSwaggerDocuments();
                return bundle;
            }

            var documents = new List<NormalizedSpecDocument>();
            foreach (NormalizedSpecDocument document in bundle.Documents)
            {
                if (!context.IsSwaggerDocument(document.DocumentId))
                {
                    documents.Add(document);
                    continue;
                }

                SpecNode root = new Swagger2DocumentConverter(
                    context,
                    document.DocumentId,
                    document.Root,
                    isRoot: string.Equals(document.DocumentId, "root", StringComparison.Ordinal)).Convert();
                documents.Add(new NormalizedSpecDocument(
                    document.DocumentId,
                    document.SourcePath,
                    document.Format,
                    document.RawSha256,
                    root));
            }

            // Edges of the documents that are not Swagger 2.0 (they cannot reference a Swagger document)
            // stay as they are; the edges of a Swagger document were rebuilt from its synthesized tree.
            var edges = new List<NormalizedSpecReferenceEdge>(context.OutputEdges);
            foreach (NormalizedSpecReferenceEdge edge in bundle.ReferenceEdges)
            {
                if (!context.IsSwaggerDocument(edge.SourceDocumentId))
                {
                    edges.Add(edge);
                }
            }

            edges.Sort(static (left, right) =>
                NormalizedSpecNodeIdentity.Compare(left.SourceIdentity, right.SourceIdentity));
            return new NormalizedSpecBundle(
                bundle.FormatVersion,
                bundle.SpecId,
                bundle.RawSha256,
                documents,
                edges);
        }
    }

    /// <summary>The documents, reference edges and output edges shared by the converters of one bundle.</summary>
    internal sealed class Swagger2Context
    {
        private readonly Dictionary<string, NormalizedSpecDocument> _documents =
            new Dictionary<string, NormalizedSpecDocument>(StringComparer.Ordinal);
        private readonly HashSet<string> _swaggerDocuments = new HashSet<string>(StringComparer.Ordinal);

        internal Swagger2Context(SpecNode root, List<(SpecNode Node, string Message)>? warnings)
        {
            Warnings = warnings ?? new List<(SpecNode Node, string Message)>();
            Resolver = new JsonPointerResolver(root);
            if (Swagger2Normalizer.IsSwagger(root))
            {
                _swaggerDocuments.Add("root");
            }
        }

        internal Swagger2Context(NormalizedSpecBundle bundle, List<(SpecNode Node, string Message)>? warnings)
        {
            Warnings = warnings ?? new List<(SpecNode Node, string Message)>();
            Bundle = bundle;
            Resolver = new JsonPointerResolver(bundle);
            foreach (NormalizedSpecDocument document in bundle.Documents)
            {
                _documents.Add(document.DocumentId, document);
                if (Swagger2Normalizer.IsSwagger(document.Root))
                {
                    _swaggerDocuments.Add(document.DocumentId);
                }
            }
        }

        /// <summary>Ignored-element warnings (OACG108) the parser reports at the given original nodes.</summary>
        internal List<(SpecNode Node, string Message)> Warnings { get; }

        internal NormalizedSpecBundle? Bundle { get; }

        /// <summary>Resolves the references of the original (not yet normalized) documents.</summary>
        internal JsonPointerResolver Resolver { get; }

        internal List<NormalizedSpecReferenceEdge> OutputEdges { get; } = new List<NormalizedSpecReferenceEdge>();

        internal bool IsSwaggerDocument(string documentId)
        {
            return _swaggerDocuments.Contains(documentId);
        }

        internal OpenApiSourceLocation CreateLocation(SpecNode node, string documentId)
        {
            if (_documents.TryGetValue(documentId, out NormalizedSpecDocument? document))
            {
                return OpenApiSourceLocation.FromNode(node, document.SourcePath, documentId);
            }

            return OpenApiSourceLocation.FromNode(node);
        }

        /// <summary>
        /// A document that is neither Swagger 2.0 (root) cannot reference a Swagger 2.0 document: the two
        /// schema dialects differ and the reference target would not exist in the original tree.
        /// </summary>
        internal void RejectReferencesToSwaggerDocuments()
        {
            if (Bundle is null || _swaggerDocuments.Count == 0)
            {
                return;
            }

            foreach (NormalizedSpecReferenceEdge edge in Bundle.ReferenceEdges)
            {
                if (!_swaggerDocuments.Contains(edge.TargetDocumentId) ||
                    _swaggerDocuments.Contains(edge.SourceDocumentId))
                {
                    continue;
                }

                NormalizedSpecDocument source = _documents[edge.SourceDocumentId];
                SpecNode? node = FindPointer(source.Root, edge.SourcePointer);
                SpecNode located = node ?? source.Root;
                throw new OpenApiSemanticException(
                    OpenApiSemanticErrorKind.UnsupportedElement,
                    "An OpenAPI 3.x document cannot reference a Swagger 2.0 document: '" +
                    edge.TargetDocumentId + "'.",
                    OpenApiSourceLocation.FromNode(located, source.SourcePath, source.DocumentId),
                    Array.Empty<OpenApiSourceLocation>());
            }
        }

        private static SpecNode? FindPointer(SpecNode root, string pointer)
        {
            SpecNode current = root;
            if (pointer.Length == 0)
            {
                return current;
            }

            foreach (string encoded in pointer.Substring(1).Split('/'))
            {
                string token = encoded.Replace("~1", "/").Replace("~0", "~");
                if (current.ValueKind == SpecValueKind.Object)
                {
                    if (!current.TryGetProperty(token, out SpecNode child))
                    {
                        return null;
                    }

                    current = child;
                }
                else if (current.ValueKind == SpecValueKind.Array &&
                         int.TryParse(token, out int index) &&
                         index >= 0 &&
                         index < current.EnumerateArray().Count())
                {
                    current = current.EnumerateArray().ElementAt(index);
                }
                else
                {
                    return null;
                }
            }

            return current;
        }
    }
}
