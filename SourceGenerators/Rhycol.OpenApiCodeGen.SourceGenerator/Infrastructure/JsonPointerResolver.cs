using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    internal sealed class JsonPointerResolver
    {
        private readonly SpecNode _root;

        internal JsonPointerResolver(SpecNode root)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
        }

        internal SpecNode Resolve(string reference, SpecNode referenceNode)
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

            if (reference.Length == 1)
            {
                return _root;
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

            SpecNode current = _root;
            string[] encodedTokens = fragment.Substring(1).Split(new[] { '/' }, StringSplitOptions.None);
            foreach (string encodedToken in encodedTokens)
            {
                string token = DecodePointerToken(encodedToken, referenceNode);
                if (current.ValueKind == SpecValueKind.Object)
                {
                    if (!current.TryGetProperty(token, out SpecNode child))
                    {
                        throw CreateUnresolved(reference, referenceNode);
                    }

                    current = child;
                    continue;
                }

                if (current.ValueKind == SpecValueKind.Array)
                {
                    if (!int.TryParse(
                            token,
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out int index) ||
                        index < 0)
                    {
                        throw CreateUnresolved(reference, referenceNode);
                    }

                    IReadOnlyList<SpecNode> items = current.EnumerateArray().ToArray();
                    if (index >= items.Count)
                    {
                        throw CreateUnresolved(reference, referenceNode);
                    }

                    current = items[index];
                    continue;
                }

                throw CreateUnresolved(reference, referenceNode);
            }

            return current;
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
            string prefix = "/components/" + componentSection + "/";
            if (!fragment.StartsWith(prefix, StringComparison.Ordinal))
            {
                throw new OpenApiSemanticException(
                    OpenApiSemanticErrorKind.UnsupportedElement,
                    "The Phase 4 MVP only supports $ref values targeting '#" + prefix + "...'.",
                    referenceNode);
            }

            string encodedName = fragment.Substring(prefix.Length);
            if (encodedName.Length == 0 || encodedName.IndexOf('/') >= 0)
            {
                throw new OpenApiSemanticException(
                    OpenApiSemanticErrorKind.UnsupportedElement,
                    "The Phase 4 MVP requires $ref values to target a named component directly: '" +
                    reference + "'.",
                    referenceNode);
            }

            return DecodePointerToken(encodedName, referenceNode);
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
                    throw new OpenApiSemanticException(
                        OpenApiSemanticErrorKind.UnresolvedReference,
                        "The JSON Pointer contains an invalid '~' escape.",
                        referenceNode);
                }

                index++;
                characters.Add(encodedToken[index] == '0' ? '~' : '/');
            }

            return new string(characters.ToArray());
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
