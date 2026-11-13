using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// P3: operation ids, Path Item references, servers, and parameter serialization
    /// (style / explode / allowReserved / content / cookie).
    /// </summary>
    internal sealed partial class OpenApiSemanticParser
    {
        private static readonly HashSet<string> IgnoredHeaderParameterNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Accept", "Content-Type", "Authorization"
            };

        // operationId values written in the document. Synthesized ids avoid them.
        private readonly HashSet<string> _explicitOperationIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _allocatedOperationIds = new HashSet<string>(StringComparer.Ordinal);

        // ---- servers (G20 / G27) ---------------------------------------------------------------------

        /// <summary>
        /// Parses a servers array. At document level an absent or empty array yields the implicit
        /// "/" server; at path / operation level it yields an empty list (inherit).
        /// </summary>
        private IReadOnlyList<OpenApiSemanticServer> ParseServers(SpecNode? serversNode, bool documentLevel)
        {
            if (serversNode is null)
            {
                return documentLevel ? new[] { CreateImplicitServer() } : Array.Empty<OpenApiSemanticServer>();
            }

            RequireKind(serversNode, SpecValueKind.Array, "The 'servers' field must be an array.");
            var result = new List<OpenApiSemanticServer>();
            foreach (SpecNode server in serversNode.EnumerateArray())
            {
                result.Add(ParseServer(server));
            }

            if (result.Count == 0)
            {
                return documentLevel ? new[] { CreateImplicitServer() } : Array.Empty<OpenApiSemanticServer>();
            }

            return result;
        }

        private static OpenApiSemanticServer CreateImplicitServer()
        {
            return new OpenApiSemanticServer("/", "/", Array.Empty<OpenApiSemanticServerVariable>());
        }

        private OpenApiSemanticServer ParseServer(SpecNode server)
        {
            RequireKind(server, SpecValueKind.Object, "Each server entry must be an object.");
            SpecNode urlNode = RequireProperty(server, "url");
            string template = RequireString(urlNode, "The server URL must be a string.");

            var variables = new List<OpenApiSemanticServerVariable>();
            SpecNode? variablesNode = GetProperty(server, "variables");
            if (variablesNode is not null)
            {
                RequireKind(variablesNode, SpecValueKind.Object, "The server 'variables' field must be an object.");
                foreach (SpecProperty variable in variablesNode.EnumerateObject()
                             .OrderBy(static value => value.Name, StringComparer.Ordinal))
                {
                    variables.Add(ParseServerVariable(variable));
                }
            }

            var declared = new HashSet<string>(variables.Select(static value => value.Name), StringComparer.Ordinal);
            int cursor = 0;
            while (cursor < template.Length)
            {
                char character = template[cursor];
                if (character == '}')
                {
                    throw Invalid(urlNode, "The server URL contains an unmatched '}'.");
                }

                if (character != '{')
                {
                    cursor++;
                    continue;
                }

                int close = template.IndexOf('}', cursor + 1);
                if (close < 0)
                {
                    throw Invalid(urlNode, "The server URL contains an unmatched '{'.");
                }

                string name = template.Substring(cursor + 1, close - cursor - 1);
                if (name.Length == 0 || name.IndexOf('{') >= 0)
                {
                    throw Invalid(urlNode, "The server URL contains an invalid variable placeholder.");
                }

                if (!declared.Contains(name))
                {
                    throw Unsupported(
                        urlNode,
                        "The server URL variable '{" + name + "}' is not declared in the server 'variables'.");
                }

                cursor = close + 1;
            }

            // One pass: a default that itself contains a placeholder is not substituted again.
            var defaultBuilder = new StringBuilder();
            int scan = 0;
            while (scan < template.Length)
            {
                int open = template.IndexOf((char)123, scan);
                if (open < 0)
                {
                    defaultBuilder.Append(template, scan, template.Length - scan);
                    break;
                }

                int close = template.IndexOf((char)125, open);
                defaultBuilder.Append(template, scan, open - scan);
                string placeholder = template.Substring(open + 1, close - open - 1);
                defaultBuilder.Append(variables.First(value => value.Name == placeholder).DefaultValue);
                scan = close + 1;
            }

            string defaultUrl = defaultBuilder.ToString();

            ValidateServerUrl(urlNode, defaultUrl);
            return new OpenApiSemanticServer(template, defaultUrl, variables);
        }

        private static OpenApiSemanticServerVariable ParseServerVariable(SpecProperty variable)
        {
            SpecNode node = variable.Value;
            RequireKind(node, SpecValueKind.Object, "Each server variable must be an object.");
            SpecNode defaultNode = RequireProperty(node, "default");
            string defaultValue = RequireString(defaultNode, "The server variable 'default' must be a string.");
            var enumValues = new List<string>();
            SpecNode? enumNode = GetProperty(node, "enum");
            if (enumNode is not null)
            {
                RequireKind(enumNode, SpecValueKind.Array, "The server variable 'enum' must be an array.");
                foreach (SpecNode item in enumNode.EnumerateArray())
                {
                    enumValues.Add(RequireString(item, "Server variable enum values must be strings."));
                }

                if (enumValues.Count == 0)
                {
                    throw Invalid(enumNode, "The server variable 'enum' must not be empty.");
                }

                if (!enumValues.Contains(defaultValue))
                {
                    throw Invalid(defaultNode, "The server variable default must be one of its enum values.");
                }
            }

            return new OpenApiSemanticServerVariable(variable.Name, defaultValue, enumValues);
        }

        // ---- operationId synthesis (G05) -------------------------------------------------------------

        /// <summary>
        /// Builds a deterministic operationId for an operation that has none: the HTTP method
        /// (capitalized) followed by one PascalCase word group per path segment, where a
        /// {parameter} becomes "By" + its name. GET /pets/{petId} becomes GetPetsByPetId and
        /// GET / becomes GetRoot.
        /// </summary>
        internal static string SynthesizeOperationId(string method, string path)
        {
            var builder = new StringBuilder();
            builder.Append(char.ToUpperInvariant(method[0])).Append(method.Substring(1).ToLowerInvariant());
            bool anySegment = false;
            foreach (string segment in path.Split('/'))
            {
                int cursor = 0;
                while (cursor < segment.Length)
                {
                    int open = segment.IndexOf('{', cursor);
                    if (open < 0)
                    {
                        anySegment |= AppendWords(builder, segment.Substring(cursor));
                        break;
                    }

                    anySegment |= AppendWords(builder, segment.Substring(cursor, open - cursor));
                    int close = segment.IndexOf('}', open + 1);
                    if (close < 0)
                    {
                        anySegment |= AppendWords(builder, segment.Substring(open + 1));
                        break;
                    }

                    var parameterWords = new StringBuilder();
                    if (AppendWords(parameterWords, segment.Substring(open + 1, close - open - 1)))
                    {
                        builder.Append("By").Append(parameterWords);
                        anySegment = true;
                    }

                    cursor = close + 1;
                }
            }

            if (!anySegment)
            {
                builder.Append("Root");
            }

            return builder.ToString();
        }

        private static bool AppendWords(StringBuilder builder, string text)
        {
            bool appended = false;
            bool upperNext = true;
            foreach (char character in text)
            {
                if (!char.IsLetterOrDigit(character))
                {
                    upperNext = true;
                    continue;
                }

                builder.Append(upperNext ? char.ToUpperInvariant(character) : character);
                upperNext = false;
                appended = true;
            }

            return appended;
        }

        private string AllocateSynthesizedOperationId(string method, string path)
        {
            string baseId = SynthesizeOperationId(method, path);
            string candidate = baseId;
            int suffix = 2;
            while (_explicitOperationIds.Contains(candidate) || _allocatedOperationIds.Contains(candidate))
            {
                candidate = baseId + suffix.ToString(CultureInfo.InvariantCulture);
                suffix++;
            }

            _allocatedOperationIds.Add(candidate);
            return candidate;
        }

        // ---- Path Item $ref (G20) --------------------------------------------------------------------

        /// <summary>
        /// Collects the explicit operationId values of a Path Item (following $ref) so synthesized
        /// ids never collide with them. Errors are ignored here: the main pass reports them.
        /// </summary>
        private void CollectExplicitOperationIds(
            SpecNode pathItem,
            HashSet<NormalizedSpecNodeIdentity> referenceStack)
        {
            try
            {
                if (pathItem.ValueKind != SpecValueKind.Object)
                {
                    return;
                }

                if (TryGetReference(pathItem, out string reference, out SpecNode referenceNode))
                {
                    ParseReferenced(
                        reference,
                        referenceNode,
                        referenceStack,
                        target =>
                        {
                            CollectExplicitOperationIds(target, referenceStack);
                            return 0;
                        });
                    return;
                }

                foreach (string method in HttpMethods)
                {
                    SpecNode? operationNode = GetProperty(pathItem, method);
                    if (operationNode is not null &&
                        operationNode.ValueKind == SpecValueKind.Object &&
                        GetProperty(operationNode, "operationId") is SpecNode idNode &&
                        idNode.ValueKind == SpecValueKind.String)
                    {
                        string? id = idNode.GetString();
                        if (!string.IsNullOrWhiteSpace(id))
                        {
                            _explicitOperationIds.Add(id!);
                        }
                    }
                }
            }
            catch (OpenApiSemanticException)
            {
                // Reported by the main pass.
            }
        }

        private void ParsePathItem(
            string path,
            SpecNode pathItem,
            HashSet<NormalizedSpecNodeIdentity> referenceStack,
            Dictionary<string, SpecNode> operationIds,
            List<OpenApiSemanticOperation> operations)
        {
            RequireKind(pathItem, SpecValueKind.Object, "Each OpenAPI path item must be an object.");
            if (TryGetReference(pathItem, out string reference, out SpecNode referenceNode))
            {
                ValidatePathItemReferenceSiblings(pathItem);
                ParseReferenced(
                    reference,
                    referenceNode,
                    referenceStack,
                    target =>
                    {
                        ParsePathItem(path, target, referenceStack, operationIds, operations);
                        return 0;
                    });
                return;
            }

            ValidatePathItemProperties(pathItem);
            IReadOnlyList<OpenApiSemanticServer> pathServers =
                ParseServers(GetProperty(pathItem, "servers"), documentLevel: false);
            IReadOnlyList<OpenApiSemanticParameter> pathParameters =
                ParseParameters(GetProperty(pathItem, "parameters"), new HashSet<NormalizedSpecNodeIdentity>());
            foreach (string method in HttpMethods)
            {
                SpecNode? operationNode = GetProperty(pathItem, method);
                if (operationNode is null)
                {
                    continue;
                }

                OpenApiSemanticOperation operation = ParseOperation(
                    path,
                    method,
                    operationNode,
                    pathParameters,
                    pathServers);
                if (operationIds.TryGetValue(operation.OperationId, out _))
                {
                    throw CreateException(
                        OpenApiSemanticErrorKind.InvalidIdentifier,
                        "The operationId '" + operation.OperationId + "' is declared more than once.",
                        operationNode);
                }

                operationIds.Add(operation.OperationId, operationNode);
                operations.Add(operation);
            }
        }

        private void ValidatePathItemReferenceSiblings(SpecNode pathItem)
        {
            foreach (SpecProperty property in pathItem.EnumerateObject())
            {
                if (property.Name == "$ref" ||
                    property.Name == "summary" ||
                    property.Name == "description" ||
                    property.Name.StartsWith("x-", StringComparison.Ordinal))
                {
                    continue;
                }

                throw Unsupported(
                    property.Value,
                    "Path Item field '" + property.Name +
                    "' cannot be combined with $ref: the behavior is undefined by the OpenAPI specification.");
            }
        }

        // ---- parameters (G16-G19) --------------------------------------------------------------------

        private enum ParameterShape
        {
            Scalar,
            PrimitiveArray,
            PrimitiveObject,
            Other
        }

        private ParameterShape ClassifyParameterShape(OpenApiSemanticSchema effective)
        {
            switch (effective.Kind)
            {
                case OpenApiSemanticSchemaKind.String:
                case OpenApiSemanticSchemaKind.Integer:
                case OpenApiSemanticSchemaKind.Number:
                case OpenApiSemanticSchemaKind.Boolean:
                case OpenApiSemanticSchemaKind.Enum:
                case OpenApiSemanticSchemaKind.Binary:
                    return ParameterShape.Scalar;
                case OpenApiSemanticSchemaKind.Array:
                    return effective.ItemSchema is not null && IsScalarSchema(effective.ItemSchema)
                        ? ParameterShape.PrimitiveArray
                        : ParameterShape.Other;
                case OpenApiSemanticSchemaKind.Map:
                    return effective.ValueSchema is not null && IsScalarSchema(effective.ValueSchema)
                        ? ParameterShape.PrimitiveObject
                        : ParameterShape.Other;
                case OpenApiSemanticSchemaKind.Object:
                    foreach (OpenApiSemanticProperty property in effective.Properties)
                    {
                        if (!IsScalarSchema(property.Schema))
                        {
                            return ParameterShape.Other;
                        }
                    }

                    return effective.AdditionalProperties == OpenApiSemanticAdditionalProperties.Typed &&
                           effective.ValueSchema is not null &&
                           !IsScalarSchema(effective.ValueSchema)
                        ? ParameterShape.Other
                        : ParameterShape.PrimitiveObject;
                default:
                    return ParameterShape.Other;
            }
        }

        private bool IsScalarSchema(OpenApiSemanticSchema schema)
        {
            return ClassifyParameterShape(GetEffectiveSchema(schema)) == ParameterShape.Scalar;
        }

        private static bool IsAllowedParameterStyle(string location, string style)
        {
            switch (location)
            {
                case "path":
                    return style == "simple" || style == "label" || style == "matrix";
                case "query":
                    return style == "form" || style == "spaceDelimited" ||
                           style == "pipeDelimited" || style == "deepObject";
                case "header":
                    return style == "simple";
                case "cookie":
                    return style == "form";
                default:
                    return false;
            }
        }

        private struct ParameterSerialization
        {
            internal string Style;
            internal bool Explode;
            internal bool AllowReserved;
        }

        private ParameterSerialization ResolveParameterSerialization(
            SpecNode node,
            string locationName,
            string name,
            ParameterShape shape)
        {
            string defaultStyle = locationName == "query" || locationName == "cookie" ? "form" : "simple";
            SpecNode? styleNode = GetProperty(node, "style");
            string style = styleNode is null
                ? defaultStyle
                : RequireString(styleNode, "The 'style' field must be a string.");
            if (!IsAllowedParameterStyle(locationName, style))
            {
                throw Invalid(
                    styleNode ?? node,
                    "The style '" + style + "' is not valid for " + locationName + " parameters.");
            }

            SpecNode? explodeNode = GetProperty(node, "explode");
            bool explode;
            if (explodeNode is not null)
            {
                explode = RequireBoolean(explodeNode, "The 'explode' field must be a boolean.");
            }
            else
            {
                // OpenAPI: explode defaults to true only for form. deepObject is only defined for
                // explode true, so an omitted value is treated as true for it.
                explode = style == "form" || style == "deepObject";
            }

            bool allowReserved = false;
            SpecNode? allowReservedNode = GetProperty(node, "allowReserved");
            if (allowReservedNode is not null)
            {
                if (locationName != "query")
                {
                    throw Invalid(
                        allowReservedNode,
                        "The allowReserved field is valid only for query parameters.");
                }

                allowReserved = RequireBoolean(allowReservedNode, "The allowReserved field must be a boolean.");
            }

            string where = " (" + locationName + " parameter '" + name + "')";
            if (shape == ParameterShape.Scalar &&
                (style == "spaceDelimited" || style == "pipeDelimited" || style == "deepObject"))
            {
                throw Unsupported(node, "The style '" + style + "' applies only to arrays and objects" + where + ".");
            }

            if (shape == ParameterShape.PrimitiveArray && style == "deepObject")
            {
                throw Unsupported(node, "The style 'deepObject' applies only to objects" + where + ".");
            }

            if ((style == "spaceDelimited" || style == "pipeDelimited") && explode)
            {
                throw Unsupported(
                    node,
                    "The style '" + style + "' with explode true is not defined by the OpenAPI specification" +
                    where + ".");
            }

            if (style == "deepObject" && !explode)
            {
                throw Unsupported(
                    node,
                    "The style 'deepObject' with explode false is not defined by the OpenAPI specification" +
                    where + ".");
            }

            return new ParameterSerialization { Style = style, Explode = explode, AllowReserved = allowReserved };
        }

        private OpenApiSemanticParameter? ParseParameterCore(SpecNode node)
        {
            var supportedFields = new HashSet<string>(StringComparer.Ordinal)
            {
                "name", "in", "description", "required", "deprecated", "allowEmptyValue",
                "style", "explode", "allowReserved", "schema", "example", "examples", "content"
            };
            foreach (SpecProperty property in node.EnumerateObject())
            {
                if (supportedFields.Contains(property.Name) ||
                    property.Name.StartsWith("x-", StringComparison.Ordinal))
                {
                    continue;
                }

                throw Unsupported(property.Value, "Unsupported Parameter field: '" + property.Name + "'.");
            }

            SpecNode nameNode = RequireProperty(node, "name");
            string name = RequireString(nameNode, "The parameter name must be a string.");
            string locationName = RequireString(RequireProperty(node, "in"), "The parameter 'in' value must be a string.");
            if (locationName != "path" && locationName != "query" &&
                locationName != "header" && locationName != "cookie")
            {
                throw Unsupported(node, "Only path, query, header, and cookie parameters are supported.");
            }

            if (locationName == "header" && !IsHttpFieldName(name))
            {
                throw Invalid(nameNode, "Header parameter names must be nonempty ASCII HTTP field-name tokens.");
            }

            if (locationName == "cookie" && !IsHttpFieldName(name))
            {
                throw Invalid(nameNode, "Cookie parameter names must be nonempty RFC 6265 cookie-name tokens.");
            }

            if (locationName == "header" && ContentOnlyRequestHeaders.Contains(name))
            {
                throw Unsupported(nameNode, "The content-only header parameter '" + name +
                    "' cannot be sent through request headers by the Phase 4 MVP.");
            }

            if (locationName == "header" && IgnoredHeaderParameterNames.Contains(name))
            {
                AddIgnoredWarning(
                    nameNode,
                    "The header parameter '" + name +
                    "' is ignored: the OpenAPI specification says header parameters named Accept, " +
                    "Content-Type and Authorization are ignored. It is not a method argument.");
                return null;
            }

            bool required = GetOptionalBoolean(node, "required") ?? false;
            if (locationName == "path" && !required)
            {
                throw Invalid(node, "Path parameters must set required to true.");
            }

            string description = GetOptionalString(node, "description") ?? string.Empty;
            bool deprecated = GetOptionalBoolean(node, "deprecated") ?? false;
            SpecNode? schemaNode = GetProperty(node, "schema");
            SpecNode? contentNode = GetProperty(node, "content");
            if (schemaNode is not null && contentNode is not null)
            {
                throw Invalid(node, "A parameter must define either 'schema' or 'content', not both.");
            }

            string contentMediaType = string.Empty;
            if (contentNode is not null)
            {
                foreach (string field in new[] { "style", "explode", "allowReserved" })
                {
                    SpecNode? fieldNode = GetProperty(node, field);
                    if (fieldNode is not null)
                    {
                        throw Invalid(fieldNode, "The '" + field + "' field cannot be used together with 'content'.");
                    }
                }

                schemaNode = ParseParameterContent(contentNode, out contentMediaType);
            }
            else if (schemaNode is null)
            {
                throw Invalid(node, "Required field 'schema' is missing.");
            }

            OpenApiSemanticSchema schema = ParseSchema(
                schemaNode,
                name + "Parameter",
                new HashSet<NormalizedSpecNodeIdentity>());
            OpenApiSemanticSchema effectiveSchema = GetEffectiveSchema(schema);
            ParameterShape shape = ClassifyParameterShape(effectiveSchema);
            var serialization = new ParameterSerialization();
            if (contentNode is null)
            {
                serialization = ResolveParameterSerialization(node, locationName, name, shape);
                if (shape == ParameterShape.Other)
                {
                    throw Unsupported(
                        node,
                        "Parameter '" + name + "' is not a primitive, an array of primitives, or an object whose " +
                        "properties are all primitives. Nested or untyped parameter values have no defined " +
                        "serialization; describe them with 'content' instead.");
                }
            }

            if (required && IsSchemaNullable(schema))
            {
                throw Unsupported(
                    schemaNode,
                    "Required " + locationName + " parameter '" + name +
                    "' cannot use a nullable schema in the Phase 4 MVP.");
            }

            return new OpenApiSemanticParameter(
                name,
                locationName,
                required,
                schema,
                CreateLocation(node),
                contentNode is null ? serialization.Style : string.Empty,
                serialization.Explode,
                serialization.AllowReserved,
                contentMediaType,
                description,
                deprecated);
        }

        private SpecNode ParseParameterContent(SpecNode contentNode, out string mediaTypeText)
        {
            RequireKind(contentNode, SpecValueKind.Object, "The content field must be an object.");
            SpecProperty[] entries = contentNode.EnumerateObject().ToArray();
            if (entries.Length != 1)
            {
                throw Invalid(contentNode, "A parameter 'content' map must contain exactly one media type.");
            }

            SpecProperty entry = entries[0];
            ParsedOpenApiMediaType mediaType = ParseMediaType(entry);
            if (!mediaType.IsJson || mediaType.Subtype.IndexOf('*') >= 0)
            {
                throw Unsupported(
                    entry.Value,
                    "Only application/json and application/*+json (concrete) parameter content is supported: '" +
                    entry.Name + "'.");
            }

            RequireKind(entry.Value, SpecValueKind.Object, "The parameter media type entry must be an object.");
            foreach (SpecProperty property in entry.Value.EnumerateObject())
            {
                if (property.Name == "schema" || property.Name == "example" ||
                    property.Name == "examples" || property.Name.StartsWith("x-", StringComparison.Ordinal))
                {
                    continue;
                }

                throw Unsupported(property.Value, "Unsupported parameter media type field: '" + property.Name + "'.");
            }

            mediaTypeText = mediaType.ToNormalizedString();
            return RequireProperty(entry.Value, "schema");
        }

        private static void ValidateCookieHeaderConflict(
            IReadOnlyList<OpenApiSemanticParameter> parameters,
            SpecNode operationNode)
        {
            if (parameters.Any(static value => value.LocationName == "cookie") &&
                parameters.Any(static value => value.LocationName == "header" &&
                                               string.Equals(value.Name, "Cookie", StringComparison.OrdinalIgnoreCase)))
            {
                throw Unsupported(
                    operationNode,
                    "An operation cannot combine cookie parameters with a header parameter named 'Cookie'.");
            }
        }
    }
}
