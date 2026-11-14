using System;
using System.Collections.Generic;
using System.Linq;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// P4 (G21-G26): request bodies in every supported media type, success / error responses,
    /// response headers and format: binary.
    /// </summary>
    internal sealed partial class OpenApiSemanticParser
    {
        private static readonly IReadOnlyDictionary<string, OpenApiSemanticEncoding> NoEncodings =
            new Dictionary<string, OpenApiSemanticEncoding>(StringComparer.Ordinal);

        private enum ResponseParseMode
        {
            /// <summary>A success response: JSON, text and binary bodies are parsed.</summary>
            Success,
            /// <summary>An error response: JSON bodies are parsed leniently (a failure only produces a warning).</summary>
            Error,
            /// <summary>Validated for shape only (components.responses, redirects, informational).</summary>
            ValidateOnly
        }

        private readonly struct ParsedResponseObject
        {
            internal ParsedResponseObject(
                IReadOnlyList<OpenApiSemanticMediaContent> contents,
                IReadOnlyList<OpenApiSemanticHeader> headers)
            {
                Contents = contents;
                Headers = headers;
            }

            internal IReadOnlyList<OpenApiSemanticMediaContent> Contents { get; }

            internal IReadOnlyList<OpenApiSemanticHeader> Headers { get; }
        }

        private readonly struct ParsedHeader
        {
            internal ParsedHeader(OpenApiSemanticHeader header, string? staticValue)
            {
                Header = header;
                StaticValue = staticValue;
            }

            internal OpenApiSemanticHeader Header { get; }

            /// <summary>The const, default or example of the header schema; null when none is declared.</summary>
            internal string? StaticValue { get; }
        }

        // ---- media types ---------------------------------------------------------------------------

        private static OpenApiSemanticContentKind ClassifyMedia(ParsedOpenApiMediaType mediaType)
        {
            if (mediaType.IsJson)
            {
                return OpenApiSemanticContentKind.Json;
            }

            if (string.Equals(mediaType.Type, "multipart", StringComparison.Ordinal))
            {
                return OpenApiSemanticContentKind.Multipart;
            }

            if (string.Equals(mediaType.Type, "application", StringComparison.Ordinal) &&
                string.Equals(mediaType.Subtype, "x-www-form-urlencoded", StringComparison.Ordinal))
            {
                return OpenApiSemanticContentKind.FormUrlEncoded;
            }

            if (string.Equals(mediaType.Type, "text", StringComparison.Ordinal) ||
                string.Equals(mediaType.Subtype, "xml", StringComparison.Ordinal) ||
                mediaType.Subtype.EndsWith("+xml", StringComparison.Ordinal) ||
                (string.Equals(mediaType.Type, "application", StringComparison.Ordinal) &&
                 (string.Equals(mediaType.Subtype, "yaml", StringComparison.Ordinal) ||
                  string.Equals(mediaType.Subtype, "x-yaml", StringComparison.Ordinal))))
            {
                return OpenApiSemanticContentKind.Text;
            }

            return OpenApiSemanticContentKind.Binary;
        }

        /// <summary>
        /// True when a string schema's contentMediaType (without contentEncoding) names opaque
        /// data: the OpenAPI 3.1 spelling of format: binary.
        /// </summary>
        private static bool IsBinaryContentMediaType(string? contentMediaType)
        {
            if (string.IsNullOrWhiteSpace(contentMediaType) ||
                !OpenApiMediaTypeParser.TryParse(contentMediaType!, out ParsedOpenApiMediaType parsed, out _))
            {
                return false;
            }

            return ClassifyMedia(parsed) == OpenApiSemanticContentKind.Binary;
        }

        // ---- request body --------------------------------------------------------------------------

        private OpenApiSemanticRequestBody? ParseRequestBody(
            SpecNode? node,
            HashSet<NormalizedSpecNodeIdentity> referenceStack,
            string suggestedName)
        {
            if (node is null)
            {
                return null;
            }

            RequireKind(node, SpecValueKind.Object, "The requestBody field must be an object or an internal $ref.");
            if (TryGetReference(node, out string reference, out SpecNode referenceNode))
            {
                ValidateReferenceObjectSiblings(node);
                _resolver.GetComponentName(_currentDocumentId, reference, "requestBodies", referenceNode);
                return ParseReferenced(
                    reference,
                    referenceNode,
                    referenceStack,
                    target => ParseRequestBody(target, referenceStack, suggestedName)!);
            }

            bool required = GetOptionalBoolean(node, "required") ?? false;
            IReadOnlyList<OpenApiSemanticMediaContent> contents = ParseBodyContents(
                RequireProperty(node, "content"),
                suggestedName,
                isRequest: true);
            if (contents.Count == 0)
            {
                throw Invalid(node, "A request body must declare at least one media type.");
            }

            OpenApiSemanticMediaContent primary = contents[0];
            return new OpenApiSemanticRequestBody(
                required,
                primary.MediaType,
                primary.Schema,
                CreateLocation(node),
                contents);
        }

        /// <summary>
        /// Parses a content map into its media types, ordered by priority (JSON, form-urlencoded,
        /// multipart, text, binary; then by name). For a response, media types whose body type
        /// differs from the primary one are dropped with a warning.
        /// </summary>
        private IReadOnlyList<OpenApiSemanticMediaContent> ParseBodyContents(
            SpecNode contentNode,
            string suggestedName,
            bool isRequest)
        {
            RequireKind(contentNode, SpecValueKind.Object, "The content field must be an object.");
            var parsed = new List<(OpenApiSemanticMediaContent Content, SpecNode Node)>();
            SpecNode? skippedWildcardJson = null;
            bool hasNonXmlMediaType = contentNode.EnumerateObject().Any(static entry =>
                entry.Name.IndexOf("xml", StringComparison.OrdinalIgnoreCase) < 0);
            foreach (SpecProperty mediaEntry in contentNode.EnumerateObject()
                         .OrderBy(static value => value.Name, StringComparer.Ordinal))
            {
                (OpenApiSemanticMediaContent Content, SpecNode Node)? item =
                    WithResolvedMediaType<(OpenApiSemanticMediaContent Content, SpecNode Node)?>(mediaEntry, mediaProperty =>
                {
                    ParsedOpenApiMediaType mediaType = ParseMediaType(mediaProperty, allowWildcard: true);
                    RequireKind(mediaProperty.Value, SpecValueKind.Object, "Each media type entry must be an object.");
                    OpenApiSemanticContentKind kind = ClassifyMedia(mediaType);
                    bool wildcard = mediaType.Type.IndexOf('*') >= 0 || mediaType.Subtype.IndexOf('*') >= 0;
                    if (isRequest && kind == OpenApiSemanticContentKind.Json && wildcard)
                    {
                        skippedWildcardJson ??= mediaProperty.Value;
                        return null;
                    }

                    if (!isRequest &&
                        (kind == OpenApiSemanticContentKind.FormUrlEncoded || kind == OpenApiSemanticContentKind.Multipart))
                    {
                        throw Unsupported(
                            mediaProperty.Value,
                            "The response media type '" + mediaProperty.Name +
                            "' is not supported: multipart and form-urlencoded bodies are request-only.");
                    }

                    if (isRequest && kind == OpenApiSemanticContentKind.Multipart &&
                        !string.Equals(mediaType.Subtype, "form-data", StringComparison.Ordinal))
                    {
                        throw Unsupported(
                            mediaProperty.Value,
                            "Only multipart/form-data request bodies are supported: '" + mediaProperty.Name + "'.");
                    }

                    if (kind == OpenApiSemanticContentKind.Text && hasNonXmlMediaType &&
                        IsXmlMediaType(mediaType) &&
                        IsNonScalarBodySchema(GetProperty(mediaProperty.Value, "schema"), suggestedName))
                    {
                        // An XML representation of an object or array cannot be generated, but the same
                        // content map offers another media type that can: skip only the XML entry.
                        AddIgnoredWarning(
                            mediaProperty.Value,
                            "The media type '" + mediaProperty.Name +
                            "' with an object or array schema is not supported (XML bodies cannot be generated) and was " +
                            "ignored; the other media types of this content map are used.");
                        return null;
                    }

                    string? normalizedCharset = isRequest &&
                                                (kind == OpenApiSemanticContentKind.Json ||
                                                 kind == OpenApiSemanticContentKind.Text)
                        ? GetSupportedRequestCharset(mediaType, mediaProperty.Value)
                        : null;
                    string normalized = mediaType.ToNormalizedString(normalizedCharset);
                    OpenApiSemanticMediaContent content = ParseMediaContent(
                        mediaProperty,
                        normalized,
                        kind,
                        wildcard,
                        suggestedName);
                    return (content, mediaProperty.Value);
                });
                if (item.HasValue)
                {
                    parsed.Add(item.Value);
                }
            }

            if (parsed.Count == 0)
            {
                if (skippedWildcardJson is not null)
                {
                    throw Unsupported(
                        skippedWildcardJson,
                        "A request body must declare a concrete JSON media type; application/*+json is a response range only.");
                }

                return Array.Empty<OpenApiSemanticMediaContent>();
            }

            var jsonContents = parsed.Where(static item => item.Content.Kind == OpenApiSemanticContentKind.Json).ToArray();
            if (jsonContents.Length > 1)
            {
                string signature = GetSchemaSignature(jsonContents[0].Content.Schema);
                for (int index = 1; index < jsonContents.Length; index++)
                {
                    if (!string.Equals(signature, GetSchemaSignature(jsonContents[index].Content.Schema), StringComparison.Ordinal))
                    {
                        throw new OpenApiSemanticException(
                            OpenApiSemanticErrorKind.InconsistentResponse,
                            "All JSON media types must use the same schema in the Phase 4 MVP.",
                            CreateLocation(contentNode),
                            Array.Empty<OpenApiSourceLocation>());
                    }
                }
            }

            var ordered = parsed
                .OrderBy(static item => (int)item.Content.Kind)
                .ThenBy(static item => item.Content.MediaType, StringComparer.Ordinal)
                .ToList();
            if (isRequest)
            {
                bool jsonSelected = false;
                foreach (var item in ordered)
                {
                    if (item.Content.Kind == OpenApiSemanticContentKind.Json && jsonSelected)
                    {
                        continue;
                    }

                    jsonSelected |= item.Content.Kind == OpenApiSemanticContentKind.Json;
                    if (item.Content.MediaType.Any(static character => character > (char)127))
                    {
                        throw Invalid(
                            item.Node,
                            "A request media type must contain ASCII characters only to be sent as Content-Type.");
                    }
                }

                return ordered.Select(static item => item.Content).ToArray();
            }

            // A response is decoded into one C# type: media types that carry another type are not handled.
            string primaryKey = GetBodyTypeKey(ordered[0].Content);
            var handled = new List<OpenApiSemanticMediaContent> { ordered[0].Content };
            for (int index = 1; index < ordered.Count; index++)
            {
                if (string.Equals(primaryKey, GetBodyTypeKey(ordered[index].Content), StringComparison.Ordinal))
                {
                    handled.Add(ordered[index].Content);
                    continue;
                }

                AddIgnoredWarning(
                    ordered[index].Node,
                    "The response media type '" + ordered[index].Content.MediaType +
                    "' carries a different body type than the primary media type '" +
                    ordered[0].Content.MediaType +
                    "'. It is not requested in Accept and a response with that Content-Type is rejected.");
            }

            return handled;
        }

        private static bool IsXmlMediaType(ParsedOpenApiMediaType mediaType)
        {
            return string.Equals(mediaType.Subtype, "xml", StringComparison.Ordinal) ||
                   mediaType.Subtype.EndsWith("+xml", StringComparison.Ordinal);
        }

        /// <summary>
        /// True when the schema parses to something other than a scalar, enum or binary payload (an object,
        /// array, union, ...): the shapes a text media type such as XML cannot carry.
        /// </summary>
        private bool IsNonScalarBodySchema(SpecNode? schemaNode, string suggestedName)
        {
            if (schemaNode is null)
            {
                return false;
            }

            OpenApiSemanticSchema effective = GetEffectiveSchema(
                ParseSchema(schemaNode, suggestedName, new HashSet<NormalizedSpecNodeIdentity>()));
            switch (effective.Kind)
            {
                case OpenApiSemanticSchemaKind.String:
                case OpenApiSemanticSchemaKind.Integer:
                case OpenApiSemanticSchemaKind.Number:
                case OpenApiSemanticSchemaKind.Boolean:
                case OpenApiSemanticSchemaKind.Enum:
                case OpenApiSemanticSchemaKind.Binary:
                    return false;
                default:
                    return true;
            }
        }

        private string GetBodyTypeKey(OpenApiSemanticMediaContent content)
        {
            return content.Kind == OpenApiSemanticContentKind.Binary
                ? "binary"
                : GetSchemaSignature(content.Schema);
        }

        private OpenApiSemanticMediaContent ParseMediaContent(
            SpecProperty mediaProperty,
            string normalizedMediaType,
            OpenApiSemanticContentKind kind,
            bool wildcard,
            string suggestedName)
        {
            SpecNode mediaNode = mediaProperty.Value;
            AuditMediaTypeFields32(mediaProperty, normalizedMediaType, kind);
            SpecNode? schemaNode = GetProperty(mediaNode, "schema");
            OpenApiSemanticSchema schema;
            IReadOnlyDictionary<string, OpenApiSemanticEncoding> encodings = NoEncodings;
            switch (kind)
            {
                case OpenApiSemanticContentKind.Json:
                    schema = ParseSchema(
                        RequireProperty(mediaNode, "schema"),
                        suggestedName,
                        new HashSet<NormalizedSpecNodeIdentity>());
                    break;
                case OpenApiSemanticContentKind.FormUrlEncoded:
                case OpenApiSemanticContentKind.Multipart:
                    SpecNode formSchemaNode = RequireProperty(mediaNode, "schema");
                    schema = ParseSchema(formSchemaNode, suggestedName, new HashSet<NormalizedSpecNodeIdentity>());
                    OpenApiSemanticSchema effective = GetEffectiveSchema(schema);
                    if (effective.Kind != OpenApiSemanticSchemaKind.Object)
                    {
                        throw Unsupported(
                            formSchemaNode,
                            "The schema of " + normalizedMediaType +
                            " must be an object whose properties are the fields or parts.");
                    }

                    encodings = ParseEncodings(mediaNode, effective, kind);
                    break;
                default:
                    if (schemaNode is null)
                    {
                        schema = new OpenApiSemanticSchema(
                            kind == OpenApiSemanticContentKind.Binary
                                ? OpenApiSemanticSchemaKind.Binary
                                : OpenApiSemanticSchemaKind.String,
                            suggestedName,
                            kind == OpenApiSemanticContentKind.Binary ? "binary" : string.Empty,
                            false,
                            string.Empty,
                            Array.Empty<OpenApiSemanticProperty>(),
                            null,
                            Array.Empty<string>(),
                            CreateLocation(mediaNode));
                        break;
                    }

                    schema = ParseSchema(schemaNode, suggestedName, new HashSet<NormalizedSpecNodeIdentity>());
                    OpenApiSemanticSchema effectiveScalar = GetEffectiveSchema(schema);
                    if (effectiveScalar.Kind == OpenApiSemanticSchemaKind.Binary)
                    {
                        kind = OpenApiSemanticContentKind.Binary;
                    }
                    else if (kind == OpenApiSemanticContentKind.Text)
                    {
                        if (effectiveScalar.Kind != OpenApiSemanticSchemaKind.String &&
                            effectiveScalar.Kind != OpenApiSemanticSchemaKind.Integer &&
                            effectiveScalar.Kind != OpenApiSemanticSchemaKind.Number &&
                            effectiveScalar.Kind != OpenApiSemanticSchemaKind.Boolean &&
                            effectiveScalar.Kind != OpenApiSemanticSchemaKind.Enum)
                        {
                            throw Unsupported(
                                schemaNode,
                                "The media type '" + normalizedMediaType +
                                "' supports a string (or other scalar) schema only; object and array bodies are supported as JSON, form-urlencoded or multipart.");
                        }
                    }
                    else if (effectiveScalar.Kind != OpenApiSemanticSchemaKind.String)
                    {
                        throw Unsupported(
                            schemaNode,
                            "The media type '" + normalizedMediaType +
                            "' is a binary payload: its schema must be a string (format: binary) or absent.");
                    }

                    break;
            }

            return new OpenApiSemanticMediaContent(
                normalizedMediaType,
                kind,
                schema,
                encodings,
                wildcard,
                CreateLocation(mediaNode));
        }

        // ---- encoding ------------------------------------------------------------------------------

        private IReadOnlyDictionary<string, OpenApiSemanticEncoding> ParseEncodings(
            SpecNode mediaNode,
            OpenApiSemanticSchema objectSchema,
            OpenApiSemanticContentKind kind)
        {
            SpecNode? encodingNode = GetProperty(mediaNode, "encoding");
            if (encodingNode is null)
            {
                return NoEncodings;
            }

            RequireKind(encodingNode, SpecValueKind.Object, "The encoding field must be an object.");
            var result = new Dictionary<string, OpenApiSemanticEncoding>(StringComparer.Ordinal);
            foreach (SpecProperty entry in encodingNode.EnumerateObject())
            {
                RequireKind(entry.Value, SpecValueKind.Object, "Each encoding entry must be an object.");
                OpenApiSemanticProperty? property = objectSchema.Properties
                    .FirstOrDefault(value => string.Equals(value.WireName, entry.Name, StringComparison.Ordinal));
                if (property is null)
                {
                    AddIgnoredWarning(
                        entry.Value,
                        "The encoding for '" + entry.Name + "' matches no declared property and was ignored.");
                    continue;
                }

                foreach (SpecProperty field in entry.Value.EnumerateObject())
                {
                    if (field.Name == "contentType" || field.Name == "headers" || field.Name == "style" ||
                        field.Name == "explode" || field.Name == "allowReserved" ||
                        field.Name.StartsWith("x-", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (IsOpenApi32 &&
                        (field.Name == "encoding" || field.Name == "prefixEncoding" || field.Name == "itemEncoding"))
                    {
                        AddIgnoredWarning(
                            field.Value,
                            "The Encoding Object field '" + field.Name +
                            "' configures nested or sequential content and was ignored.");
                        continue;
                    }

                    throw Unsupported(field.Value, "Unsupported Encoding field: '" + field.Name + "'.");
                }

                string contentType = ParseEncodingContentType(entry.Value);
                IReadOnlyList<KeyValuePair<string, string>> headers = ParseEncodingHeaders(GetProperty(entry.Value, "headers"));
                string style = "form";
                bool explode = true;
                bool allowReserved = false;
                bool serializationDeclared = GetProperty(entry.Value, "style") is not null ||
                                             GetProperty(entry.Value, "explode") is not null ||
                                             GetProperty(entry.Value, "allowReserved") is not null;
                if (kind == OpenApiSemanticContentKind.Multipart)
                {
                    if (serializationDeclared)
                    {
                        AddIgnoredWarning(
                            entry.Value,
                            "style, explode and allowReserved of '" + entry.Name +
                            "' are ignored for multipart/form-data: an array is sent as one part per item.");
                    }
                }
                else if (serializationDeclared)
                {
                    ParameterShape shape = ClassifyParameterShape(GetEffectiveSchema(property.Schema));
                    ParameterSerialization serialization = ResolveParameterSerialization(
                        entry.Value,
                        "query",
                        entry.Name,
                        shape == ParameterShape.Other ? ParameterShape.Scalar : shape);
                    style = serialization.Style;
                    explode = serialization.Explode;
                    allowReserved = serialization.AllowReserved;
                }

                result.Add(entry.Name, new OpenApiSemanticEncoding(contentType, style, explode, allowReserved, headers));
            }

            return result;
        }

        private string ParseEncodingContentType(SpecNode encodingNode)
        {
            SpecNode? contentTypeNode = GetProperty(encodingNode, "contentType");
            if (contentTypeNode is null)
            {
                return string.Empty;
            }

            string value = RequireString(contentTypeNode, "The encoding contentType must be a string.");
            string[] candidates = value.Split(',')
                .Select(static part => part.Trim())
                .Where(static part => part.Length > 0)
                .ToArray();
            if (candidates.Length == 0)
            {
                return string.Empty;
            }

            foreach (string candidate in candidates)
            {
                if (!OpenApiMediaTypeParser.TryParse(candidate, out _, out string error))
                {
                    throw Invalid(contentTypeNode, "Invalid encoding contentType '" + candidate + "': " + error);
                }
            }

            if (candidates.Length > 1)
            {
                AddIgnoredWarning(
                    contentTypeNode,
                    "Only the first contentType ('" + candidates[0] + "') of the list is used for the part.");
            }

            return candidates[0];
        }

        private IReadOnlyList<KeyValuePair<string, string>> ParseEncodingHeaders(SpecNode? headersNode)
        {
            if (headersNode is null)
            {
                return Array.Empty<KeyValuePair<string, string>>();
            }

            RequireKind(headersNode, SpecValueKind.Object, "The encoding headers field must be an object.");
            var result = new List<KeyValuePair<string, string>>();
            foreach (SpecProperty entry in headersNode.EnumerateObject()
                         .OrderBy(static value => value.Name, StringComparer.Ordinal))
            {
                if (!IsHttpFieldName(entry.Name))
                {
                    throw Invalid(entry.Value, "Encoding header names must be nonempty ASCII HTTP field-name tokens.");
                }

                if (string.Equals(entry.Name, "Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    AddIgnoredWarning(
                        entry.Value,
                        "The encoding header 'Content-Type' is ignored: use the contentType field.");
                    continue;
                }

                ParsedHeader header = ParseHeaderWithValue(entry.Value, entry.Name, new HashSet<NormalizedSpecNodeIdentity>());
                if (header.StaticValue is null)
                {
                    AddIgnoredWarning(
                        entry.Value,
                        "The encoding header '" + entry.Name +
                        "' has no static value (const, default or example) and is not sent.");
                    continue;
                }

                result.Add(new KeyValuePair<string, string>(entry.Name, header.StaticValue));
            }

            return result;
        }

        // ---- headers -------------------------------------------------------------------------------

        private OpenApiSemanticHeader ParseHeaderObject(
            SpecNode node,
            string name,
            HashSet<NormalizedSpecNodeIdentity> referenceStack)
        {
            return ParseHeaderWithValue(node, name, referenceStack).Header;
        }

        private ParsedHeader ParseHeaderWithValue(
            SpecNode node,
            string name,
            HashSet<NormalizedSpecNodeIdentity> referenceStack)
        {
            RequireKind(node, SpecValueKind.Object, "Each header must be an object or an internal $ref.");
            if (TryGetReference(node, out string reference, out SpecNode referenceNode))
            {
                ValidateReferenceObjectSiblings(node);
                _resolver.GetComponentName(_currentDocumentId, reference, "headers", referenceNode);
                return ParseReferenced(
                    reference,
                    referenceNode,
                    referenceStack,
                    target => ParseHeaderWithValue(target, name, referenceStack));
            }

            foreach (SpecProperty property in node.EnumerateObject())
            {
                if (property.Name == "description" || property.Name == "required" || property.Name == "deprecated" ||
                    property.Name == "allowEmptyValue" || property.Name == "style" || property.Name == "explode" ||
                    property.Name == "schema" || property.Name == "example" || property.Name == "examples" ||
                    property.Name.StartsWith("x-", StringComparison.Ordinal))
                {
                    continue;
                }

                throw Unsupported(property.Value, "Unsupported Header field: '" + property.Name + "'.");
            }

            SpecNode? styleNode = GetProperty(node, "style");
            if (styleNode is not null &&
                !string.Equals(RequireString(styleNode, "The header style must be a string."), "simple", StringComparison.Ordinal))
            {
                throw Unsupported(styleNode, "Only the 'simple' style is defined for headers.");
            }

            SpecNode? explodeNode = GetProperty(node, "explode");
            if (explodeNode is not null)
            {
                RequireBoolean(explodeNode, "The header explode field must be a boolean.");
            }

            SpecNode schemaNode = RequireProperty(node, "schema");
            OpenApiSemanticSchema schema = ParseSchema(
                schemaNode,
                ToPascalFragment(name) + "Header",
                new HashSet<NormalizedSpecNodeIdentity>());
            ParameterShape shape = ClassifyParameterShape(GetEffectiveSchema(schema));
            if (shape != ParameterShape.Scalar && shape != ParameterShape.PrimitiveArray)
            {
                throw Unsupported(
                    node,
                    "The header '" + name + "' must use a primitive or an array of primitives; objects have no supported header form.");
            }

            bool required = GetOptionalBoolean(node, "required") ?? false;
            string description = GetOptionalString(node, "description") ?? string.Empty;
            bool deprecated = GetOptionalBoolean(node, "deprecated") ?? false;
            string? staticValue = schema.Annotations.Const?.RawText ?? schema.Annotations.Default?.RawText;
            if (staticValue is null && GetProperty(node, "example") is SpecNode exampleNode)
            {
                staticValue = exampleNode.ValueKind == SpecValueKind.String
                    ? exampleNode.GetString()
                    : exampleNode.ValueKind == SpecValueKind.Number ||
                      exampleNode.ValueKind == SpecValueKind.True ||
                      exampleNode.ValueKind == SpecValueKind.False
                        ? exampleNode.GetRawJson()
                        : null;
            }

            return new ParsedHeader(
                new OpenApiSemanticHeader(name, required, schema, description, deprecated, CreateLocation(node)),
                staticValue);
        }

        private IReadOnlyList<OpenApiSemanticHeader> ParseResponseHeaders(SpecNode? headersNode)
        {
            if (headersNode is null)
            {
                return Array.Empty<OpenApiSemanticHeader>();
            }

            RequireKind(headersNode, SpecValueKind.Object, "The response headers field must be an object.");
            var result = new List<OpenApiSemanticHeader>();
            foreach (SpecProperty entry in headersNode.EnumerateObject()
                         .OrderBy(static value => value.Name, StringComparer.Ordinal))
            {
                if (!IsHttpFieldName(entry.Name))
                {
                    throw Invalid(entry.Value, "Response header names must be nonempty ASCII HTTP field-name tokens.");
                }

                if (string.Equals(entry.Name, "Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    AddIgnoredWarning(
                        entry.Value,
                        "The response header 'Content-Type' is ignored: the OpenAPI specification says it SHALL be ignored.");
                    continue;
                }

                result.Add(ParseHeaderObject(entry.Value, entry.Name, new HashSet<NormalizedSpecNodeIdentity>()));
            }

            return result;
        }

        // ---- responses -----------------------------------------------------------------------------

        private ParsedResponses ParseResponses(
            SpecNode? responsesNode,
            string successName,
            string errorName,
            string method,
            SpecNode operationNode)
        {
            SpecProperty[] entries = Array.Empty<SpecProperty>();
            if (responsesNode is not null)
            {
                RequireKind(responsesNode, SpecValueKind.Object, "The responses field must be an object.");
                entries = responsesNode.EnumerateObject()
                    .Where(static value => !value.Name.StartsWith("x-", StringComparison.Ordinal))
                    .OrderBy(static value => value.Name, StringComparer.Ordinal)
                    .ToArray();
                if (entries.Length == 0 && _minorVersion == 0)
                {
                    throw Invalid(responsesNode, "The responses object must contain at least one response.");
                }
            }

            bool hasDeclaredSuccess = entries.Any(static value => IsSuccessStatusCode(value.Name));
            // The last success response keeps the plain suggested name (the original contract used its
            // schema); earlier ones are qualified by status so that different contracts get distinct types.
            string? lastSuccessKey = entries
                .Select(value => value.Name.ToUpperInvariant())
                .LastOrDefault(value => ClassifyResponse(value, hasDeclaredSuccess) == OpenApiSemanticResponseClass.Success);
            var responses = new List<OpenApiSemanticResponse>();
            foreach (SpecProperty responseProperty in entries)
            {
                ValidateResponseStatusCode(responseProperty);
                string key = responseProperty.Name.ToUpperInvariant();
                OpenApiSemanticResponseClass responseClass = ClassifyResponse(key, hasDeclaredSuccess);
                bool bodyless = method == "head" ||
                                (responseClass == OpenApiSemanticResponseClass.Success &&
                                 (key == "204" || key == "205"));
                ResponseParseMode mode = responseClass == OpenApiSemanticResponseClass.Success
                    ? ResponseParseMode.Success
                    : responseClass == OpenApiSemanticResponseClass.Error
                        ? ResponseParseMode.Error
                        : ResponseParseMode.ValidateOnly;
                ParsedResponseObject parsed = ParseResponseObject(
                    responseProperty.Value,
                    new HashSet<NormalizedSpecNodeIdentity>(),
                    responseClass == OpenApiSemanticResponseClass.Success
                        ? (string.Equals(key, lastSuccessKey, StringComparison.Ordinal)
                            ? successName
                            : successName + ToPascalFragment(key))
                        : errorName + ToPascalFragment(key),
                    mode,
                    bodyless);
                string contractKey = string.Empty;
                if (responseClass == OpenApiSemanticResponseClass.Success)
                {
                    foreach (OpenApiSemanticMediaContent content in parsed.Contents)
                    {
                        if (content.Kind == OpenApiSemanticContentKind.Json)
                        {
                            ValidateResponseMetadataProperties(content.Schema, new HashSet<NormalizedSpecNodeIdentity>());
                        }
                    }

                    contractKey = parsed.Contents.Count == 0 ? "void" : GetBodyTypeKey(parsed.Contents[0]);
                }

                responses.Add(new OpenApiSemanticResponse(
                    key,
                    responseClass,
                    parsed.Contents,
                    parsed.Headers,
                    CreateLocation(responseProperty.Value),
                    contractKey));
            }

            OpenApiSemanticResponse[] success = responses
                .Where(static value => value.ResponseClass == OpenApiSemanticResponseClass.Success)
                .ToArray();
            var successCodes = new List<string>();
            var responseMediaTypes = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            OpenApiSemanticSchema? successSchema = null;
            foreach (OpenApiSemanticResponse response in success)
            {
                successCodes.Add(response.StatusKey);
                responseMediaTypes[response.StatusKey] = response.Contents
                    .Where(static value => value.Kind == OpenApiSemanticContentKind.Json)
                    .Select(static value => value.MediaType)
                    .ToArray();
                OpenApiSemanticMediaContent? json = response.Contents
                    .FirstOrDefault(static value => value.Kind == OpenApiSemanticContentKind.Json);
                if (json is not null)
                {
                    successSchema = json.Schema;
                }
            }

            return new ParsedResponses(
                successSchema,
                successCodes.OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
                responseMediaTypes,
                responses);
        }

        private static OpenApiSemanticResponseClass ClassifyResponse(string key, bool hasDeclaredSuccess)
        {
            if (string.Equals(key, "DEFAULT", StringComparison.Ordinal))
            {
                // default is the success contract only when no 2xx is declared (G24).
                return hasDeclaredSuccess
                    ? OpenApiSemanticResponseClass.Error
                    : OpenApiSemanticResponseClass.Success;
            }

            switch (key[0])
            {
                case '1':
                    return OpenApiSemanticResponseClass.Informational;
                case '2':
                    return OpenApiSemanticResponseClass.Success;
                case '3':
                    return OpenApiSemanticResponseClass.Redirect;
                default:
                    return OpenApiSemanticResponseClass.Error;
            }
        }

        private ParsedResponseObject ParseResponseObject(
            SpecNode node,
            HashSet<NormalizedSpecNodeIdentity> referenceStack,
            string suggestedName,
            ResponseParseMode mode,
            bool forbidBodyContent)
        {
            RequireKind(node, SpecValueKind.Object, "Each response must be an object or an internal $ref.");
            if (TryGetReference(node, out string reference, out SpecNode referenceNode))
            {
                ValidateReferenceObjectSiblings(node);
                _resolver.GetComponentName(_currentDocumentId, reference, "responses", referenceNode);
                return ParseReferenced(
                    reference,
                    referenceNode,
                    referenceStack,
                    target => ParseResponseObject(target, referenceStack, suggestedName, mode, forbidBodyContent));
            }

            ValidateResponseProperties(node);
            if (IsOpenApi32)
            {
                // OpenAPI 3.2 made the response description optional and added summary.
                foreach (string field in new[] { "description", "summary" })
                {
                    SpecNode? textNode = GetProperty(node, field);
                    if (textNode is not null)
                    {
                        RequireString(textNode, "The response " + field + " must be a string.");
                    }
                }
            }
            else
            {
                RequireString(RequireProperty(node, "description"), "The response description must be a string.");
            }

            IReadOnlyList<OpenApiSemanticHeader> headers = ParseResponseHeaders(GetProperty(node, "headers"));
            WarnIgnoredLinks(node);
            SpecNode? contentNode = GetProperty(node, "content");
            if (contentNode is null)
            {
                return new ParsedResponseObject(Array.Empty<OpenApiSemanticMediaContent>(), headers);
            }

            if (forbidBodyContent)
            {
                RequireKind(contentNode, SpecValueKind.Object, "The response content field must be an object.");
                if (contentNode.EnumerateObject().Any())
                {
                    throw Unsupported(contentNode, "HEAD, 204, and 205 success responses cannot declare body content in the Phase 4 client contract.");
                }

                return new ParsedResponseObject(Array.Empty<OpenApiSemanticMediaContent>(), headers);
            }

            switch (mode)
            {
                case ResponseParseMode.Success:
                    return new ParsedResponseObject(
                        ParseBodyContents(contentNode, suggestedName, isRequest: false),
                        headers);
                case ResponseParseMode.Error:
                    ValidateResponseContent(contentNode);
                    return new ParsedResponseObject(ParseErrorContents(contentNode, suggestedName), headers);
                default:
                    ValidateResponseContent(contentNode);
                    return new ParsedResponseObject(Array.Empty<OpenApiSemanticMediaContent>(), headers);
            }
        }

        /// <summary>
        /// The JSON body of an error response, parsed leniently: error responses were only
        /// validated before, so a schema that cannot be generated keeps the body as a string.
        /// </summary>
        private IReadOnlyList<OpenApiSemanticMediaContent> ParseErrorContents(SpecNode contentNode, string suggestedName)
        {
            foreach (SpecProperty mediaEntry in contentNode.EnumerateObject()
                         .OrderBy(static value => value.Name, StringComparer.Ordinal))
            {
                OpenApiSemanticMediaContent? errorContent = WithResolvedMediaType<OpenApiSemanticMediaContent?>(
                    mediaEntry,
                    mediaProperty => ParseErrorMediaContent(mediaProperty, suggestedName));
                if (errorContent is not null)
                {
                    return new[] { errorContent };
                }
            }

            return Array.Empty<OpenApiSemanticMediaContent>();
        }

        private OpenApiSemanticMediaContent? ParseErrorMediaContent(SpecProperty mediaProperty, string suggestedName)
        {
            ParsedOpenApiMediaType mediaType = ParseMediaType(mediaProperty, allowWildcard: true);
            if (!mediaType.IsJson || GetProperty(mediaProperty.Value, "schema") is not SpecNode schemaNode)
            {
                return null;
            }

            OpenApiSemanticSchema schema;
            try
            {
                schema = ParseSchema(schemaNode, suggestedName, new HashSet<NormalizedSpecNodeIdentity>());
            }
            catch (OpenApiSemanticException exception)
            {
                AddIgnoredWarning(
                    schemaNode,
                    "The error response schema cannot be generated (" + exception.Message +
                    "); the error body is available as text only (ResponseBody).");
                return null;
            }

            if (FindBinarySchema(schema, new HashSet<NormalizedSpecNodeIdentity>()) is not null)
            {
                AddIgnoredWarning(
                    schemaNode,
                    "The error response schema contains a binary (format: binary) value; the error body is available as a string only.");
                return null;
            }

            return new OpenApiSemanticMediaContent(
                mediaType.ToNormalizedString(),
                OpenApiSemanticContentKind.Json,
                schema,
                NoEncodings,
                mediaType.Subtype.IndexOf('*') >= 0,
                CreateLocation(mediaProperty.Value));
        }

        // ---- binary --------------------------------------------------------------------------------

        private OpenApiSemanticSchema? FindBinarySchema(
            OpenApiSemanticSchema schema,
            HashSet<NormalizedSpecNodeIdentity> visited)
        {
            switch (schema.Kind)
            {
                case OpenApiSemanticSchemaKind.Binary:
                    return schema;
                case OpenApiSemanticSchemaKind.Reference:
                    return !schema.ReferenceIdentity.IsEmpty &&
                           visited.Add(schema.ReferenceIdentity) &&
                           _schemas.TryGetValue(schema.ReferenceIdentity, out OpenApiSemanticSchema? target)
                        ? FindBinarySchema(target, visited)
                        : null;
                case OpenApiSemanticSchemaKind.Array:
                    return schema.ItemSchema is null ? null : FindBinarySchema(schema.ItemSchema, visited);
                case OpenApiSemanticSchemaKind.Map:
                    return schema.ValueSchema is null ? null : FindBinarySchema(schema.ValueSchema, visited);
                case OpenApiSemanticSchemaKind.Union:
                    foreach (OpenApiSemanticSchema member in schema.UnionMembers)
                    {
                        OpenApiSemanticSchema? found = FindBinarySchema(member, visited);
                        if (found is not null)
                        {
                            return found;
                        }
                    }

                    return null;
                case OpenApiSemanticSchemaKind.Object:
                    foreach (OpenApiSemanticProperty property in schema.Properties)
                    {
                        OpenApiSemanticSchema? found = FindBinarySchema(property.Schema, visited);
                        if (found is not null)
                        {
                            return found;
                        }
                    }

                    return schema.ValueSchema is null ? null : FindBinarySchema(schema.ValueSchema, visited);
                default:
                    return null;
            }
        }

        /// <summary>
        /// format: binary has no JSON form: it is accepted only as a multipart part, as the schema of
        /// a binary body and as a binary response. Using it in a JSON body, parameter, header or
        /// form-urlencoded field is rejected here, at the binary schema.
        /// </summary>
        private void ValidateNoBinaryInJson(IReadOnlyList<OpenApiSemanticOperation> operations)
        {
            foreach (OpenApiSemanticOperation operation in operations)
            {
                foreach (OpenApiSemanticParameter parameter in operation.Parameters)
                {
                    RejectBinary(parameter.Schema, "parameters");
                }

                if (operation.RequestBody is not null)
                {
                    foreach (OpenApiSemanticMediaContent content in operation.RequestBody.Contents)
                    {
                        if (content.Kind == OpenApiSemanticContentKind.Json ||
                            content.Kind == OpenApiSemanticContentKind.FormUrlEncoded)
                        {
                            RejectBinary(content.Schema, content.Kind == OpenApiSemanticContentKind.Json
                                ? "JSON bodies"
                                : "form-urlencoded bodies");
                        }
                    }
                }

                foreach (OpenApiSemanticResponse response in operation.Responses)
                {
                    foreach (OpenApiSemanticMediaContent content in response.Contents)
                    {
                        if (content.Kind == OpenApiSemanticContentKind.Json)
                        {
                            RejectBinary(content.Schema, "JSON bodies");
                        }
                    }

                    foreach (OpenApiSemanticHeader header in response.Headers)
                    {
                        RejectBinary(header.Schema, "headers");
                    }
                }
            }
        }

        private void RejectBinary(OpenApiSemanticSchema schema, string context)
        {
            OpenApiSemanticSchema? binary = FindBinarySchema(schema, new HashSet<NormalizedSpecNodeIdentity>());
            if (binary is not null)
            {
                throw new OpenApiSemanticException(
                    OpenApiSemanticErrorKind.UnsupportedElement,
                    "Binary schemas (format: binary) cannot be used in " + context +
                    ": they are supported as multipart/form-data parts, as binary request bodies and as binary responses.",
                    binary.Location,
                    Array.Empty<OpenApiSourceLocation>());
            }
        }
    }
}
