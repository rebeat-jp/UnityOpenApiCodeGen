using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// P8 (G33): OpenAPI 3.2. A 3.2 document uses the JSON Schema 2020-12 dialect and the same schema rules as
    /// 3.1 (_minorVersion is 1 for it); this file holds the 3.2 additions:
    /// the QUERY method and additionalOperations on a Path Item, in: querystring parameters, tags
    /// (parent / kind / summary, documentation only), $self, the sequential media type itemSchema
    /// (not interpreted), components.mediaTypes, Response summary, and Encoding / Media Type fields for
    /// sequential and nested multipart bodies (ignored with OACG108).
    /// </summary>
    internal sealed partial class OpenApiSemanticParser
    {
        private const string OpenApi32DialectPrefix = "https://spec.openapis.org/oas/3.2/dialect/";

        // Fixed Path Item fields of OpenAPI 3.2 (lower case, alphabetical like HttpMethods).
        private static readonly string[] HttpMethods32 =
        {
            "delete", "get", "head", "options", "patch", "post", "put", "query", "trace"
        };

        private static readonly HashSet<string> FixedFieldMethodNames =
            new HashSet<string>(HttpMethods32, StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> SequentialMediaTypes =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "application/jsonl", "application/x-ndjson", "application/ndjson",
                "application/json-seq", "application/geo+json-seq"
            };

        private bool IsOpenApi32 => _declaredMinorVersion >= 2;

        // The Path Item fields that hold an operation in the document's OpenAPI version.
        private string[] PathItemMethods => IsOpenApi32 ? HttpMethods32 : HttpMethods;

        private static bool IsSupportedDialect(int declaredMinorVersion, string dialect)
        {
            if (declaredMinorVersion == 1)
            {
                return string.Equals(dialect, OpenApi31BaseDialect, StringComparison.Ordinal);
            }

            return declaredMinorVersion == 2 &&
                   (string.Equals(dialect, OpenApi31BaseDialect, StringComparison.Ordinal) ||
                    dialect.StartsWith(OpenApi32DialectPrefix, StringComparison.Ordinal));
        }

        // ---- root ($self, tags) ------------------------------------------------------------------------

        private void ValidateOpenApi32Root()
        {
            if (!IsOpenApi32)
            {
                return;
            }

            SpecNode? selfNode = GetProperty(_root, "$self");
            if (selfNode is not null)
            {
                string self = RequireString(selfNode, "The '$self' field must be a string.");
                if (self.IndexOf('#') >= 0)
                {
                    throw Invalid(selfNode, "The '$self' field must not contain a fragment.");
                }

                if (_bundle is not null && _bundle.Documents.Count > 1)
                {
                    // Internal references never depend on the base URI; only relative references to other
                    // documents would, and those are resolved against the file location.
                    AddIgnoredWarning(
                        selfNode,
                        "The '$self' base URI is not used: references to other documents are resolved " +
                        "against the location of the document file.");
                }
            }

            ValidateTags32(GetProperty(_root, "tags"));
        }

        // Tag Object additions of 3.2 (summary, parent, kind) are documentation only: they are validated
        // for shape, and parent must name a declared tag without forming a cycle.
        private static void ValidateTags32(SpecNode? tagsNode)
        {
            if (tagsNode is null)
            {
                return;
            }

            RequireKind(tagsNode, SpecValueKind.Array, "The 'tags' field must be an array.");
            var parents = new Dictionary<string, (string Parent, SpecNode Node)>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (SpecNode tag in tagsNode.EnumerateArray())
            {
                RequireKind(tag, SpecValueKind.Object, "Each tag must be an object.");
                string name = RequireString(RequireProperty(tag, "name"), "The tag name must be a string.");
                if (!names.Add(name))
                {
                    throw Invalid(tag, "The tag '" + name + "' is declared more than once.");
                }

                foreach (string field in new[] { "summary", "description", "kind" })
                {
                    SpecNode? fieldNode = GetProperty(tag, field);
                    if (fieldNode is not null)
                    {
                        RequireString(fieldNode, "The tag '" + field + "' must be a string.");
                    }
                }

                SpecNode? parentNode = GetProperty(tag, "parent");
                if (parentNode is not null)
                {
                    parents[name] = (RequireString(parentNode, "The tag 'parent' must be a string."), parentNode);
                }
            }

            foreach (KeyValuePair<string, (string Parent, SpecNode Node)> entry in parents)
            {
                if (!names.Contains(entry.Value.Parent))
                {
                    throw Invalid(
                        entry.Value.Node,
                        "The tag parent '" + entry.Value.Parent + "' is not a declared tag.");
                }
            }

            foreach (string start in parents.Keys)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal) { start };
                string current = start;
                while (parents.TryGetValue(current, out (string Parent, SpecNode Node) link))
                {
                    if (!seen.Add(link.Parent))
                    {
                        throw Invalid(link.Node, "The tag parent chain of '" + start + "' is cyclic.");
                    }

                    current = link.Parent;
                }
            }
        }

        // ---- additionalOperations ----------------------------------------------------------------------

        /// <summary>
        /// Path Item additionalOperations: a map from an HTTP method token to an Operation. The token is
        /// sent exactly as written. A token that is not an HTTP token, or that repeats a fixed Path Item
        /// method (compared case-insensitively), is OACG100 at the operation.
        /// </summary>
        private void ParseAdditionalOperations(
            string path,
            SpecNode pathItem,
            IReadOnlyList<OpenApiSemanticParameter> pathParameters,
            IReadOnlyList<OpenApiSemanticServer> pathServers,
            Dictionary<string, SpecNode> operationIds,
            List<OpenApiSemanticOperation> operations)
        {
            SpecNode? additional = GetProperty(pathItem, "additionalOperations");
            if (additional is null)
            {
                return;
            }

            RequireKind(additional, SpecValueKind.Object, "The 'additionalOperations' field must be an object.");
            foreach (SpecProperty entry in additional.EnumerateObject()
                         .OrderBy(static value => value.Name, StringComparer.Ordinal))
            {
                if (!IsHttpFieldName(entry.Name))
                {
                    throw Invalid(
                        entry.Value,
                        "The additionalOperations key '" + entry.Name + "' is not a valid HTTP method token.");
                }

                if (FixedFieldMethodNames.Contains(entry.Name))
                {
                    throw Invalid(
                        entry.Value,
                        "The additionalOperations key '" + entry.Name +
                        "' repeats a method that has its own Path Item field; use that field instead.");
                }

                OpenApiSemanticOperation operation = ParseOperation(
                    path,
                    entry.Name,
                    entry.Value,
                    pathParameters,
                    pathServers,
                    entry.Name);
                if (operationIds.ContainsKey(operation.OperationId))
                {
                    throw CreateException(
                        OpenApiSemanticErrorKind.InvalidIdentifier,
                        "The operationId '" + operation.OperationId + "' is declared more than once.",
                        entry.Value);
                }

                operationIds.Add(operation.OperationId, entry.Value);
                operations.Add(operation);
            }
        }

        private void CollectAdditionalOperationIds(SpecNode pathItem)
        {
            if (!IsOpenApi32 ||
                GetProperty(pathItem, "additionalOperations") is not SpecNode additional ||
                additional.ValueKind != SpecValueKind.Object)
            {
                return;
            }

            foreach (SpecProperty entry in additional.EnumerateObject())
            {
                if (entry.Value.ValueKind == SpecValueKind.Object &&
                    GetProperty(entry.Value, "operationId") is SpecNode idNode &&
                    idNode.ValueKind == SpecValueKind.String &&
                    !string.IsNullOrWhiteSpace(idNode.GetString()))
                {
                    _explicitOperationIds.Add(idNode.GetString()!);
                }
            }
        }

        /// <summary>
        /// The method word of a synthesized operationId: letters and digits of the method, first letter
        /// upper case and the rest lower case (get becomes Get, a custom PURGE becomes Purge). A token with no
        /// letter or digit becomes Custom.
        /// </summary>
        internal static string FormatMethodWord(string method)
        {
            var builder = new StringBuilder();
            foreach (char character in method)
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(builder.Length == 0
                        ? char.ToUpperInvariant(character)
                        : char.ToLowerInvariant(character));
                }
            }

            return builder.Length == 0 ? "Custom" : builder.ToString();
        }

        // ---- in: querystring ---------------------------------------------------------------------------

        private readonly struct QuerystringContent
        {
            internal QuerystringContent(
                OpenApiSemanticSchema schema,
                string mediaType,
                IReadOnlyDictionary<string, OpenApiSemanticEncoding>? encodings)
            {
                Schema = schema;
                MediaType = mediaType;
                Encodings = encodings;
            }

            internal OpenApiSemanticSchema Schema { get; }

            internal string MediaType { get; }

            internal IReadOnlyDictionary<string, OpenApiSemanticEncoding>? Encodings { get; }
        }

        /// <summary>
        /// The content of an in: querystring parameter: exactly one media type, either
        /// application/x-www-form-urlencoded (an object schema; each property is a form field, serialized
        /// like a form-urlencoded request body) or a concrete JSON media type (the percent-encoded JSON text
        /// is the whole query string). Any other media type is OACG101.
        /// </summary>
        private QuerystringContent ParseQuerystringContent(SpecNode contentNode, string name)
        {
            RequireKind(contentNode, SpecValueKind.Object, "The content field must be an object.");
            SpecProperty[] entries = contentNode.EnumerateObject().ToArray();
            if (entries.Length != 1)
            {
                throw Invalid(contentNode, "A querystring parameter 'content' map must contain exactly one media type.");
            }

            return WithResolvedMediaType(entries[0], entry =>
            {
                ParsedOpenApiMediaType mediaType = ParseMediaType(entry);
                RequireKind(entry.Value, SpecValueKind.Object, "The parameter media type entry must be an object.");
                if (string.Equals(mediaType.Type, "application", StringComparison.Ordinal) &&
                    string.Equals(mediaType.Subtype, "x-www-form-urlencoded", StringComparison.Ordinal))
                {
                    OpenApiSemanticMediaContent content = ParseMediaContent(
                        entry,
                        mediaType.ToNormalizedString(),
                        OpenApiSemanticContentKind.FormUrlEncoded,
                        false,
                        name + "Parameter");
                    return new QuerystringContent(content.Schema, content.MediaType, content.Encodings);
                }

                if (mediaType.IsJson && mediaType.Subtype.IndexOf('*') < 0)
                {
                    foreach (SpecProperty property in entry.Value.EnumerateObject())
                    {
                        if (property.Name == "schema" || property.Name == "example" ||
                            property.Name == "examples" || property.Name.StartsWith("x-", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        throw Unsupported(
                            property.Value, "Unsupported parameter media type field: '" + property.Name + "'.");
                    }

                    OpenApiSemanticSchema schema = ParseSchema(
                        RequireProperty(entry.Value, "schema"),
                        name + "Parameter",
                        new HashSet<NormalizedSpecNodeIdentity>());
                    return new QuerystringContent(schema, mediaType.ToNormalizedString(), null);
                }

                throw Unsupported(
                    entry.Value,
                    "The querystring parameter media type '" + entry.Name +
                    "' is not supported: use application/x-www-form-urlencoded or a concrete JSON media type.");
            });
        }

        // An operation has at most one in: querystring parameter and it excludes in: query parameters
        // (OACG105). Path-level and operation-level parameters are checked together, after the merge.
        private static void ValidateQuerystringParameters(IReadOnlyList<OpenApiSemanticParameter> parameters)
        {
            OpenApiSemanticParameter[] querystrings = parameters
                .Where(static value => value.LocationName == "querystring")
                .ToArray();
            if (querystrings.Length == 0)
            {
                return;
            }

            if (querystrings.Length > 1)
            {
                throw new OpenApiSemanticException(
                    OpenApiSemanticErrorKind.InconsistentResponse,
                    "An operation can declare at most one in: querystring parameter; found '" +
                    querystrings[0].Name + "' and '" + querystrings[1].Name + "'.",
                    querystrings[1].Location,
                    new[] { querystrings[0].Location });
            }

            OpenApiSemanticParameter? query = parameters.FirstOrDefault(
                static value => value.LocationName == "query");
            if (query is not null)
            {
                throw new OpenApiSemanticException(
                    OpenApiSemanticErrorKind.InconsistentResponse,
                    "The in: querystring parameter '" + querystrings[0].Name +
                    "' cannot be combined with the in: query parameter '" + query.Name + "'.",
                    querystrings[0].Location,
                    new[] { query.Location });
            }
        }

        // ---- components.mediaTypes ---------------------------------------------------------------------

        /// <summary>
        /// Runs <paramref name="process"/> on a media type entry of a content map. In OpenAPI 3.2 the entry may
        /// be a Reference Object to components.mediaTypes; it is then resolved first and processed inside
        /// the referenced document's scope, so schema references inside it resolve there.
        /// </summary>
        private T WithResolvedMediaType<T>(SpecProperty entry, Func<SpecProperty, T> process)
        {
            if (!IsOpenApi32 ||
                entry.Value.ValueKind != SpecValueKind.Object ||
                !TryGetReference(entry.Value, out string reference, out SpecNode referenceNode))
            {
                return process(entry);
            }

            ValidateReferenceObjectSiblings(entry.Value);
            _resolver.GetComponentName(_currentDocumentId, reference, "mediaTypes", referenceNode);
            return ParseReferenced(
                reference,
                referenceNode,
                new HashSet<NormalizedSpecNodeIdentity>(),
                target => process(new SpecProperty(entry.Name, entry.Line, entry.Column, target)));
        }

        // components.mediaTypes: every entry is a Media Type Object. It is validated for shape and its
        // schema is parsed; what it means (request or response, which media type) comes from the
        // content entry that references it.
        private void ValidateMediaTypeComponents()
        {
            SpecNode? components = GetProperty(_root, "components");
            SpecNode? mediaTypes = components is null ? null : GetProperty(components, "mediaTypes");
            if (!IsOpenApi32 || mediaTypes is null)
            {
                return;
            }

            RequireKind(mediaTypes, SpecValueKind.Object, "The components.mediaTypes field must be an object.");
            foreach (SpecProperty entry in mediaTypes.EnumerateObject()
                         .OrderBy(static value => value.Name, StringComparer.Ordinal))
            {
                if (entry.Name.StartsWith("x-", StringComparison.Ordinal))
                {
                    continue;
                }

                if (TryGetReference(entry.Value, out string reference, out SpecNode referenceNode))
                {
                    ValidateReferenceObjectSiblings(entry.Value);
                    _resolver.GetComponentName(_currentDocumentId, reference, "mediaTypes", referenceNode);
                    continue;
                }

                RequireKind(entry.Value, SpecValueKind.Object, "Each media type component must be an object.");
                SpecNode? schemaNode = GetProperty(entry.Value, "schema");
                if (schemaNode is not null)
                {
                    ParseSchema(
                        schemaNode,
                        entry.Name + "MediaType",
                        new HashSet<NormalizedSpecNodeIdentity>());
                }
            }
        }

        // ---- media type fields -------------------------------------------------------------------------

        /// <summary>
        /// itemSchema, prefixEncoding and itemEncoding of a 3.2 Media Type Object. Streaming is out of scope:
        /// text/event-stream with itemSchema is OACG101; the other sequential media types (JSON Lines and
        /// friends) are exchanged as byte[] and itemSchema is ignored (OACG108), which also keeps the
        /// body readable without a per-item contract. itemSchema on any other media type is OACG101.
        /// </summary>
        private void AuditMediaTypeFields32(
            SpecProperty mediaProperty,
            string normalizedMediaType,
            OpenApiSemanticContentKind kind)
        {
            if (!IsOpenApi32)
            {
                return;
            }

            SpecNode mediaNode = mediaProperty.Value;
            SpecNode? encodingNode = GetProperty(mediaNode, "encoding");
            if (encodingNode is not null &&
                (GetProperty(mediaNode, "prefixEncoding") is not null || GetProperty(mediaNode, "itemEncoding") is not null))
            {
                throw Invalid(
                    encodingNode,
                    "The Media Type Object field 'encoding' must not be present together with 'prefixEncoding' or 'itemEncoding'.");
            }

            SpecNode? itemSchemaNode = GetProperty(mediaNode, "itemSchema");
            if (itemSchemaNode is not null)
            {
                string essence = GetMediaTypeEssence(normalizedMediaType);
                if (string.Equals(essence, "text/event-stream", StringComparison.Ordinal))
                {
                    throw Unsupported(
                        itemSchemaNode,
                        "The media type 'text/event-stream' with itemSchema describes a stream of events; " +
                        "streaming (Server-Sent Events) is not supported by the Source Generator.");
                }

                if (!SequentialMediaTypes.Contains(essence))
                {
                    throw Unsupported(
                        itemSchemaNode,
                        "itemSchema is defined for sequential media types only (for example application/jsonl); '" +
                        mediaProperty.Name + "' is not one.");
                }

                AddIgnoredWarning(
                    itemSchemaNode,
                    "The itemSchema of the sequential media type '" + essence +
                    "' is not interpreted: streaming is not supported and the body is exchanged as byte[] " +
                    "without per-item (de)serialization.");
            }

            foreach (string field in new[] { "prefixEncoding", "itemEncoding" })
            {
                SpecNode? fieldNode = GetProperty(mediaNode, field);
                if (fieldNode is not null)
                {
                    AddIgnoredWarning(
                        fieldNode,
                        "The Media Type Object field '" + field +
                        "' configures sequential or multipart/mixed content and was ignored.");
                }
            }
        }

        private static string GetMediaTypeEssence(string normalizedMediaType)
        {
            int separator = normalizedMediaType.IndexOf(';');
            return (separator < 0 ? normalizedMediaType : normalizedMediaType.Substring(0, separator))
                .Trim()
                .ToLowerInvariant();
        }

        // ---- discriminator defaultMapping --------------------------------------------------------------

        /// <summary>
        /// Resolves the OpenAPI 3.2 discriminator defaultMapping of a union to the index of a member
        /// (-1 when the field is absent or the document is not 3.2). The value is a schema name or a
        /// reference and has to name a member, like a mapping value.
        /// </summary>
        private int ResolveDefaultMapping(
            SpecNode discriminatorNode,
            IReadOnlyList<SpecNode> memberNodes,
            IReadOnlyList<OpenApiSemanticSchema> members,
            string keyword)
        {
            SpecNode? defaultNode = IsOpenApi32 ? GetProperty(discriminatorNode, "defaultMapping") : null;
            if (defaultNode is null)
            {
                return -1;
            }

            string target = RequireString(defaultNode, "The discriminator defaultMapping must be a string.");
            int index = ResolveMappingMember(target, defaultNode, memberNodes, members);
            if (index < 0)
            {
                throw Unsupported(
                    defaultNode,
                    "The discriminator defaultMapping value '" + target + "' does not refer to a member of the " +
                    keyword + ".");
            }

            return index;
        }
    }
}
