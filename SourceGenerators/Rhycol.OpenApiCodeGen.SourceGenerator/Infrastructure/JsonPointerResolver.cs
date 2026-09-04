using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    internal sealed class JsonPointerResolver
    {
        private readonly SpecNode _root;
        private readonly NormalizedSpecBundle? _bundle;
        private readonly Dictionary<string, NormalizedSpecDocument> _documents =
            new Dictionary<string, NormalizedSpecDocument>(StringComparer.Ordinal);
        private readonly Dictionary<NormalizedSpecNodeIdentity, NormalizedSpecReferenceEdge> _edges =
            new Dictionary<NormalizedSpecNodeIdentity, NormalizedSpecReferenceEdge>();

        internal JsonPointerResolver(SpecNode root)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
        }

        internal JsonPointerResolver(NormalizedSpecBundle bundle)
        {
            _bundle = bundle ?? throw new ArgumentNullException(nameof(bundle));
            _root = bundle.Root;
            foreach (NormalizedSpecDocument document in bundle.Documents)
            {
                _documents.Add(document.DocumentId, document);
            }

            foreach (NormalizedSpecReferenceEdge edge in bundle.ReferenceEdges)
            {
                _edges.Add(edge.SourceIdentity, edge);
            }
        }

        internal SpecNode Resolve(string reference, SpecNode referenceNode)
        {
            return ResolveReference("root", reference, referenceNode).Node;
        }

        internal ResolvedSpecReference ResolveReference(
            string sourceDocumentId,
            string reference,
            SpecNode referenceNode)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                throw new OpenApiSemanticException(
                    OpenApiSemanticErrorKind.UnresolvedReference,
                    "The $ref value must not be empty.",
                    referenceNode);
            }

            if (_bundle is null)
            {
                return ResolveLegacy(reference, referenceNode);
            }

            var sourceIdentity = new NormalizedSpecNodeIdentity(sourceDocumentId, referenceNode.LogicalPath);
            if (!_edges.TryGetValue(sourceIdentity, out NormalizedSpecReferenceEdge? edge))
            {
                throw new OpenApiSemanticException(
                    OpenApiSemanticErrorKind.UnresolvedReference,
                    "The v2 reference edge is missing for '$ref' at '" + referenceNode.LogicalPath + "'.",
                    referenceNode);
            }

            if (!_documents.TryGetValue(edge.TargetDocumentId, out NormalizedSpecDocument? targetDocument))
            {
                throw new OpenApiSemanticException(
                    OpenApiSemanticErrorKind.UnresolvedReference,
                    "The v2 reference target document '" + edge.TargetDocumentId + "' is not present.",
                    referenceNode);
            }

            if (!TryResolvePointer(targetDocument.Root, edge.TargetPointer, out SpecNode? targetNode) ||
                targetNode is null)
            {
                throw new OpenApiSemanticException(
                    OpenApiSemanticErrorKind.UnresolvedReference,
                    "The v2 reference target pointer '" + edge.TargetPointer + "' could not be resolved.",
                    referenceNode);
            }

            return new ResolvedSpecReference(
                edge.TargetDocumentId,
                edge.TargetPointer,
                targetDocument,
                targetNode);
        }

        internal static string GetComponentName(
            string reference,
            string componentSection,
            SpecNode referenceNode)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                throw new OpenApiSemanticException(
                    OpenApiSemanticErrorKind.UnresolvedReference,
                    "The $ref value must not be empty.",
                    referenceNode);
            }

            if (reference[0] != '#')
            {
                throw new OpenApiSemanticException(
                    OpenApiSemanticErrorKind.ExternalReference,
                    "External $ref values are not supported in the Phase 4 MVP: '" + reference + "'.",
                    referenceNode);
            }

            string fragment = DecodeFragment(reference.Substring(1), referenceNode);
            return ExtractComponentName(
                fragment,
                reference,
                componentSection,
                referenceNode);
        }

        internal string GetComponentName(
            string sourceDocumentId,
            string reference,
            string componentSection,
            SpecNode referenceNode)
        {
            if (_bundle is null)
            {
                return GetComponentName(reference, componentSection, referenceNode);
            }

            ResolvedSpecReference resolved = ResolveReference(sourceDocumentId, reference, referenceNode);
            return ExtractComponentName(resolved.Pointer, reference, componentSection, referenceNode);
        }

        internal ResolvedSpecReference ResolveComponent(
            string sourceDocumentId,
            string reference,
            string componentSection,
            SpecNode referenceNode)
        {
            ResolvedSpecReference resolved = ResolveReference(sourceDocumentId, reference, referenceNode);
            ExtractComponentName(resolved.Pointer, reference, componentSection, referenceNode);
            return resolved;
        }

        private static string ExtractComponentName(
            string fragment,
            string reference,
            string componentSection,
            SpecNode referenceNode)
        {
            string prefix = "/components/" + componentSection + "/";
            if (!fragment.StartsWith(prefix, StringComparison.Ordinal))
            {
                throw new OpenApiSemanticException(
                    OpenApiSemanticErrorKind.UnsupportedElement,
                    "The Source Generator only supports $ref values targeting '#" + prefix + "...'.",
                    referenceNode);
            }

            string encodedName = fragment.Substring(prefix.Length);
            if (encodedName.Length == 0 || encodedName.IndexOf('/') >= 0)
            {
                throw new OpenApiSemanticException(
                    OpenApiSemanticErrorKind.UnsupportedElement,
                    "The Source Generator requires $ref values to target a named component directly: '" +
                    reference + "'.",
                    referenceNode);
            }

            return DecodePointerToken(encodedName, referenceNode);
        }

        private ResolvedSpecReference ResolveLegacy(string reference, SpecNode referenceNode)
        {
            if (reference[0] != '#')
            {
                throw new OpenApiSemanticException(
                    OpenApiSemanticErrorKind.ExternalReference,
                    "External $ref values are not supported in the Phase 4 MVP: '" + reference + "'.",
                    referenceNode);
            }

            if (reference.Length == 1)
            {
                return new ResolvedSpecReference("root", string.Empty, null, _root);
            }

            string fragment = DecodeFragment(reference.Substring(1), referenceNode);
            if (!fragment.StartsWith("/", StringComparison.Ordinal))
            {
                throw new OpenApiSemanticException(
                    OpenApiSemanticErrorKind.UnresolvedReference,
                    "The internal $ref must be a JSON Pointer fragment beginning with '#/': '" +
                    reference + "'.",
                    referenceNode);
            }

            if (!TryResolvePointer(_root, fragment, out SpecNode? targetNode) || targetNode is null)
            {
                throw CreateUnresolved(reference, referenceNode);
            }

            return new ResolvedSpecReference("root", fragment, null, targetNode);
        }

        private static bool TryResolvePointer(SpecNode root, string pointer, out SpecNode? result)
        {
            result = root;
            if (pointer.Length == 0)
            {
                return true;
            }

            if (pointer[0] != '/')
            {
                result = null;
                return false;
            }

            string[] encodedTokens = pointer.Substring(1).Split(new[] { '/' }, StringSplitOptions.None);
            foreach (string encodedToken in encodedTokens)
            {
                if (!TryDecodePointerToken(encodedToken, out string token))
                {
                    result = null;
                    return false;
                }

                if (result is null)
                {
                    return false;
                }

                if (result.ValueKind == SpecValueKind.Object)
                {
                    if (!result.TryGetProperty(token, out SpecNode child))
                    {
                        result = null;
                        return false;
                    }

                    result = child;
                    continue;
                }

                if (result.ValueKind == SpecValueKind.Array &&
                    int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out int index) &&
                    index >= 0)
                {
                    SpecNode[] items = result.EnumerateArray().ToArray();
                    if (index < items.Length)
                    {
                        result = items[index];
                        continue;
                    }
                }

                result = null;
                return false;
            }

            return true;
        }

        private static string DecodeFragment(string encodedFragment, SpecNode referenceNode)
        {
            for (int index = 0; index < encodedFragment.Length; index++)
            {
                if (encodedFragment[index] != '%')
                {
                    continue;
                }

                if (index + 2 >= encodedFragment.Length ||
                    !IsHexDigit(encodedFragment[index + 1]) ||
                    !IsHexDigit(encodedFragment[index + 2]))
                {
                    throw new OpenApiSemanticException(
                        OpenApiSemanticErrorKind.UnresolvedReference,
                        "The JSON Pointer contains invalid percent encoding.",
                        referenceNode);
                }

                index += 2;
            }

            try
            {
                return Uri.UnescapeDataString(encodedFragment);
            }
            catch (UriFormatException)
            {
                throw new OpenApiSemanticException(
                    OpenApiSemanticErrorKind.UnresolvedReference,
                    "The JSON Pointer contains invalid percent encoding.",
                    referenceNode);
            }
        }

        private static string DecodePointerToken(string encodedToken, SpecNode referenceNode)
        {
            if (!TryDecodePointerToken(encodedToken, out string decoded))
            {
                throw new OpenApiSemanticException(
                    OpenApiSemanticErrorKind.UnresolvedReference,
                    "The JSON Pointer contains an invalid '~' escape.",
                    referenceNode);
            }

            return decoded;
        }

        private static bool TryDecodePointerToken(string encodedToken, out string decoded)
        {
            var characters = new List<char>(encodedToken.Length);
            for (int index = 0; index < encodedToken.Length; index++)
            {
                char character = encodedToken[index];
                if (character != '~')
                {
                    characters.Add(character);
                    continue;
                }

                if (index + 1 >= encodedToken.Length ||
                    (encodedToken[index + 1] != '0' && encodedToken[index + 1] != '1'))
                {
                    decoded = string.Empty;
                    return false;
                }

                index++;
                characters.Add(encodedToken[index] == '0' ? '~' : '/');
            }

            decoded = new string(characters.ToArray());
            return true;
        }

        private static bool IsHexDigit(char value)
        {
            return (value >= '0' && value <= '9') ||
                   (value >= 'A' && value <= 'F') ||
                   (value >= 'a' && value <= 'f');
        }

        private static OpenApiSemanticException CreateUnresolved(string reference, SpecNode node)
        {
            return new OpenApiSemanticException(
                OpenApiSemanticErrorKind.UnresolvedReference,
                "The internal $ref could not be resolved: '" + reference + "'.",
                node);
        }
    }
}
