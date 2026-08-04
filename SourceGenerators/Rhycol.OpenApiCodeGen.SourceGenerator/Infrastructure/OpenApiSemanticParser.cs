using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    internal sealed class OpenApiSemanticParser
    {
        private static readonly string[] HttpMethods =
        {
            "delete", "get", "head", "options", "patch", "post", "put", "trace"
        };

        private static readonly HashSet<string> HttpMethodSet =
            new HashSet<string>(HttpMethods, StringComparer.Ordinal);

        private readonly SpecNode _root;
        private readonly JsonPointerResolver _resolver;
        private readonly int _minorVersion;

        private OpenApiSemanticParser(SpecNode root, int minorVersion)
        {
            _root = root;
            _resolver = new JsonPointerResolver(root);
            _minorVersion = minorVersion;
        }

        internal static OpenApiSemanticDocument Parse(SpecNode root)
        {
            if (root is null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            RequireKind(root, SpecValueKind.Object, "The OpenAPI document root must be an object.");
            SpecNode versionNode = RequireProperty(root, "openapi");
            string version = RequireString(versionNode, "The 'openapi' field must be a string.");
            int minorVersion = ParseSupportedVersion(version, versionNode);
            var parser = new OpenApiSemanticParser(root, minorVersion);
            return parser.ParseDocument();
        }

        private OpenApiSemanticDocument ParseDocument()
        {
            ValidateRootFeatures();
            ParseInfo(RequireProperty(_root, "info"));
            string baseUrl = ParseBaseUrl(GetProperty(_root, "servers"));
            IReadOnlyDictionary<string, OpenApiSemanticSchema> schemas = ParseComponentSchemas();
            ValidateNonSchemaComponents();
            IReadOnlyList<OpenApiSemanticOperation> operations = ParsePaths(RequireProperty(_root, "paths"));

            return new OpenApiSemanticDocument(
                _minorVersion,
                baseUrl,
                schemas,
                operations,
                OpenApiSourceLocation.FromNode(_root));
        }

        private void ValidateRootFeatures()
        {
            ThrowIfPresent(_root, "swagger", "Swagger 2.0 documents are not supported by the Phase 4 Source Generator.");
            ThrowIfPresent(_root, "security", "OpenAPI security requirements are not supported by the Phase 4 MVP.");
            ThrowIfPresent(_root, "webhooks", "OpenAPI webhooks are not supported by the Phase 4 MVP.");

            SpecNode? components = GetProperty(_root, "components");
            if (components is null)
            {
                return;
            }

            RequireKind(components, SpecValueKind.Object, "The 'components' field must be an object.");
            foreach (SpecProperty property in components.EnumerateObject())
            {
                if (property.Name == "schemas" ||
                    property.Name == "parameters" ||
                    property.Name == "requestBodies" ||
                    property.Name == "responses" ||
                    property.Name.StartsWith("x-", StringComparison.Ordinal))
                {
                    continue;
                }

                throw Unsupported(
                    property.Value,
                    "The components section '" + property.Name + "' is not supported by the Phase 4 MVP.");
            }
        }

        private static void ParseInfo(SpecNode infoNode)
        {
            RequireKind(infoNode, SpecValueKind.Object, "The 'info' field must be an object.");
            RequireString(RequireProperty(infoNode, "title"), "The info title must be a string.");
            RequireString(RequireProperty(infoNode, "version"), "The info version must be a string.");
        }

        private string ParseBaseUrl(SpecNode? serversNode)
        {
            if (serversNode is null)
            {
                return string.Empty;
            }

            RequireKind(serversNode, SpecValueKind.Array, "The 'servers' field must be an array.");
            IReadOnlyList<SpecNode> servers = serversNode.EnumerateArray().ToArray();
            if (servers.Count == 0)
            {
                return string.Empty;
            }

            if (servers.Count > 1)
            {
                throw Unsupported(
                    servers[1],
                    "The Phase 4 MVP supports exactly one server URL. Supply a base URL override at runtime for other servers.");
            }

            SpecNode server = servers[0];
            RequireKind(server, SpecValueKind.Object, "Each server entry must be an object.");
            ThrowIfPresent(server, "variables", "Server URL variables are not supported by the Phase 4 MVP.");
            return RequireString(RequireProperty(server, "url"), "The server URL must be a string.");
        }

        private IReadOnlyDictionary<string, OpenApiSemanticSchema> ParseComponentSchemas()
        {
            var result = new Dictionary<string, OpenApiSemanticSchema>(StringComparer.Ordinal);
            SpecNode? components = GetProperty(_root, "components");
            SpecNode? schemas = components is null ? null : GetProperty(components, "schemas");
            if (schemas is null)
            {
                return result;
            }

            RequireKind(schemas, SpecValueKind.Object, "The components.schemas field must be an object.");
            foreach (SpecProperty property in schemas.EnumerateObject()
                         .OrderBy(static value => value.Name, StringComparer.Ordinal))
            {
                string pointer = "#/components/schemas/" + EncodePointerToken(property.Name);
                var stack = new HashSet<string>(StringComparer.Ordinal) { pointer };
                result.Add(property.Name, ParseSchema(property.Value, property.Name, stack));
            }

            return result;
        }

        private void ValidateNonSchemaComponents()
        {
            SpecNode? components = GetProperty(_root, "components");
            if (components is null)
            {
                return;
            }

            ValidateComponentMap(
                GetProperty(components, "parameters"),
                (node, name) => ParseParameter(node, new HashSet<string>(StringComparer.Ordinal)));
            ValidateComponentMap(
                GetProperty(components, "requestBodies"),
                (node, name) => ParseRequestBody(
                    node,
                    new HashSet<string>(StringComparer.Ordinal),
                    name + "Request"));
            ValidateComponentMap(
                GetProperty(components, "responses"),
                (node, name) => ParseResponse(
                    node,
                    new HashSet<string>(StringComparer.Ordinal),
                    name + "Response"));
        }

        private static void ValidateComponentMap(
            SpecNode? mapNode,
            Func<SpecNode, string, object?> validator)
        {
            if (mapNode is null)
            {
                return;
            }

            RequireKind(mapNode, SpecValueKind.Object, "An OpenAPI components map must be an object.");
            foreach (SpecProperty property in mapNode.EnumerateObject()
                         .OrderBy(static value => value.Name, StringComparer.Ordinal))
            {
                validator(property.Value, property.Name);
            }
        }

        private IReadOnlyList<OpenApiSemanticOperation> ParsePaths(SpecNode pathsNode)
        {
            RequireKind(pathsNode, SpecValueKind.Object, "The 'paths' field must be an object.");
            var operations = new List<OpenApiSemanticOperation>();
            var operationIds = new Dictionary<string, SpecNode>(StringComparer.Ordinal);

            foreach (SpecProperty pathProperty in pathsNode.EnumerateObject()
                         .OrderBy(static value => value.Name, StringComparer.Ordinal))
            {
                if (pathProperty.Name.StartsWith("x-", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!pathProperty.Name.StartsWith("/", StringComparison.Ordinal))
                {
                    throw Invalid(pathProperty.Value, "OpenAPI path keys must begin with '/'.");
                }

                SpecNode pathItem = pathProperty.Value;
                RequireKind(pathItem, SpecValueKind.Object, "Each OpenAPI path item must be an object.");
                ThrowIfPresent(pathItem, "$ref", "Path Item $ref values are not supported by the Phase 4 MVP.");
                ThrowIfPresent(pathItem, "servers", "Path-level server overrides are not supported by the Phase 4 MVP.");
                IReadOnlyList<OpenApiSemanticParameter> pathParameters =
                    ParseParameters(GetProperty(pathItem, "parameters"), new HashSet<string>(StringComparer.Ordinal));

                ValidatePathItemProperties(pathItem);
                foreach (string method in HttpMethods)
                {
                    SpecNode? operationNode = GetProperty(pathItem, method);
                    if (operationNode is null)
                    {
                        continue;
                    }

                    OpenApiSemanticOperation operation = ParseOperation(
                        pathProperty.Name,
                        method,
                        operationNode,
                        pathParameters);
                    if (operationIds.TryGetValue(operation.OperationId, out _))
                    {
                        throw new OpenApiSemanticException(
                            OpenApiSemanticErrorKind.InvalidIdentifier,
                            "The operationId '" + operation.OperationId + "' is declared more than once.",
                            operationNode);
                    }

                    operationIds.Add(operation.OperationId, operationNode);
                    operations.Add(operation);
                }
            }

            return operations
                .OrderBy(static value => value.Path, StringComparer.Ordinal)
                .ThenBy(static value => value.HttpMethod, StringComparer.Ordinal)
                .ToArray();
        }

        private static void ValidatePathItemProperties(SpecNode pathItem)
        {
            foreach (SpecProperty property in pathItem.EnumerateObject())
            {
                if (HttpMethodSet.Contains(property.Name) ||
                    property.Name == "parameters" ||
                    property.Name == "summary" ||
                    property.Name == "description" ||
                    property.Name.StartsWith("x-", StringComparison.Ordinal))
                {
                    continue;
                }

                if (property.Name == "$ref" || property.Name == "servers")
                {
                    continue;
                }

                throw Unsupported(property.Value, "Unsupported Path Item field: '" + property.Name + "'.");
            }
        }

        private OpenApiSemanticOperation ParseOperation(
            string path,
            string method,
            SpecNode operationNode,
            IReadOnlyList<OpenApiSemanticParameter> pathParameters)
        {
            RequireKind(operationNode, SpecValueKind.Object, "Each OpenAPI operation must be an object.");
            ThrowIfPresent(operationNode, "callbacks", "OpenAPI callbacks are not supported by the Phase 4 MVP.");
            ThrowIfPresent(operationNode, "security", "Operation security requirements are not supported by the Phase 4 MVP.");
            ThrowIfPresent(operationNode, "servers", "Operation-level server overrides are not supported by the Phase 4 MVP.");
            ValidateOperationProperties(operationNode);

            string operationId = RequireString(
                RequireProperty(operationNode, "operationId"),
                "Each operation must define a string operationId.");
            if (string.IsNullOrWhiteSpace(operationId))
            {
                throw Invalid(operationNode, "The operationId must not be empty.");
            }

            string summary = GetOptionalString(operationNode, "summary") ?? string.Empty;
            IReadOnlyList<OpenApiSemanticParameter> operationParameters = ParseParameters(
                GetProperty(operationNode, "parameters"),
                new HashSet<string>(StringComparer.Ordinal));
            IReadOnlyList<OpenApiSemanticParameter> mergedParameters = MergeParameters(
                pathParameters,
                operationParameters);
            ValidatePathParameters(path, mergedParameters, operationNode);

            OpenApiSemanticRequestBody? requestBody = ParseRequestBody(
                GetProperty(operationNode, "requestBody"),
                new HashSet<string>(StringComparer.Ordinal),
                operationId + "Request");
            ParsedResponses responses = ParseResponses(
                RequireProperty(operationNode, "responses"),
                operationId + "Response");

            return new OpenApiSemanticOperation(
                operationId,
                summary,
                method.ToUpperInvariant(),
                path,
                mergedParameters,
                requestBody,
                responses.Schema,
                responses.StatusCodes,
                OpenApiSourceLocation.FromNode(operationNode));
        }

        private static void ValidateOperationProperties(SpecNode operationNode)
        {
            var supported = new HashSet<string>(StringComparer.Ordinal)
            {
                "operationId", "summary", "description", "tags", "deprecated",
                "externalDocs", "parameters", "requestBody", "responses",
                "callbacks", "security", "servers"
            };
            foreach (SpecProperty property in operationNode.EnumerateObject())
            {
                if (supported.Contains(property.Name) || property.Name.StartsWith("x-", StringComparison.Ordinal))
                {
                    continue;
                }

                throw Unsupported(property.Value, "Unsupported Operation field: '" + property.Name + "'.");
            }
        }

        private IReadOnlyList<OpenApiSemanticParameter> ParseParameters(
            SpecNode? parametersNode,
            HashSet<string> referenceStack)
        {
            if (parametersNode is null)
            {
                return Array.Empty<OpenApiSemanticParameter>();
            }

            RequireKind(parametersNode, SpecValueKind.Array, "The parameters field must be an array.");
            var result = new List<OpenApiSemanticParameter>();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (SpecNode node in parametersNode.EnumerateArray())
            {
                OpenApiSemanticParameter parameter = ParseParameter(node, referenceStack);
                if (!identities.Add(parameter.Identity))
                {
                    throw Invalid(
                        node,
                        "The parameters array contains more than one parameter named '" +
                        parameter.Name + "' in '" + parameter.LocationName + "'.");
                }

                result.Add(parameter);
            }

            return result.OrderBy(static value => value.Identity, StringComparer.Ordinal).ToArray();
        }

        private OpenApiSemanticParameter ParseParameter(SpecNode node, HashSet<string> referenceStack)
        {
            RequireKind(node, SpecValueKind.Object, "Each parameter must be an object or an internal $ref.");
            if (TryGetReference(node, out string reference, out SpecNode referenceNode))
            {
                JsonPointerResolver.GetComponentName(reference, "parameters", referenceNode);
                return ParseReferenced(
                    reference,
                    referenceNode,
                    referenceStack,
                    target => ParseParameter(target, referenceStack));
            }

            ThrowIfPresent(node, "content", "Parameter content is not supported by the Phase 4 MVP.");
            string name = RequireString(RequireProperty(node, "name"), "The parameter name must be a string.");
            string locationName = RequireString(RequireProperty(node, "in"), "The parameter 'in' value must be a string.");
            if (locationName != "path" && locationName != "query" && locationName != "header")
            {
                throw Unsupported(node, "Only path, query, and header parameters are supported.");
            }

            bool required = GetOptionalBoolean(node, "required") ?? false;
            if (locationName == "path" && !required)
            {
                throw Invalid(node, "Path parameters must set required to true.");
            }

            ValidateParameterSerialization(node, locationName);
            OpenApiSemanticSchema schema = ParseSchema(
                RequireProperty(node, "schema"),
                name + "Parameter",
                new HashSet<string>(StringComparer.Ordinal));
            if (schema.Kind == OpenApiSemanticSchemaKind.Array ||
                schema.Kind == OpenApiSemanticSchemaKind.Object)
            {
                throw Unsupported(node, "Array and object parameters require style/explode support and are outside the Phase 4 MVP.");
            }

            return new OpenApiSemanticParameter(
                name,
                locationName,
                required,
                schema,
                OpenApiSourceLocation.FromNode(node));
        }

        private static void ValidateParameterSerialization(SpecNode node, string locationName)
        {
            string? style = GetOptionalString(node, "style");
            bool? explode = GetOptionalBoolean(node, "explode");
            SpecNode? allowReservedNode = GetProperty(node, "allowReserved");
            if (allowReservedNode is not null)
            {
                if (locationName != "query")
                {
                    throw Invalid(
                        allowReservedNode,
                        "The allowReserved field is valid only for query parameters.");
                }

                if (RequireBoolean(allowReservedNode, "The allowReserved field must be a boolean."))
                {
                    throw Unsupported(
                        allowReservedNode,
                        "Query parameters with allowReserved set to true are not supported by the Phase 4 MVP.");
                }
            }

            string defaultStyle = locationName == "query" ? "form" : "simple";
            bool defaultExplode = locationName == "query";
            if (style is not null && style != defaultStyle)
            {
                throw Unsupported(node, "Only the default '" + defaultStyle + "' parameter style is supported for " + locationName + ".");
            }

            if (explode.HasValue && explode.Value != defaultExplode)
            {
                throw Unsupported(node, "Only the default explode value is supported for " + locationName + " parameters.");
            }
        }

        private static IReadOnlyList<OpenApiSemanticParameter> MergeParameters(
            IReadOnlyList<OpenApiSemanticParameter> pathParameters,
            IReadOnlyList<OpenApiSemanticParameter> operationParameters)
        {
            var merged = new Dictionary<string, OpenApiSemanticParameter>(StringComparer.Ordinal);
            foreach (OpenApiSemanticParameter parameter in pathParameters)
            {
                merged[parameter.Identity] = parameter;
            }

            foreach (OpenApiSemanticParameter parameter in operationParameters)
            {
                merged[parameter.Identity] = parameter;
            }

            return merged.Values.OrderBy(static value => value.Identity, StringComparer.Ordinal).ToArray();
        }

        private static void ValidatePathParameters(
            string path,
            IReadOnlyList<OpenApiSemanticParameter> parameters,
            SpecNode operationNode)
        {
            var declared = new HashSet<string>(
                parameters.Where(static value => value.LocationName == "path")
                    .Select(static value => value.Name),
                StringComparer.Ordinal);
            var used = new HashSet<string>(StringComparer.Ordinal);
            int cursor = 0;
            while (cursor < path.Length)
            {
                int open = path.IndexOf('{', cursor);
                if (open < 0)
                {
                    break;
                }

                int close = path.IndexOf('}', open + 1);
                if (close < 0)
                {
                    throw Invalid(operationNode, "The path template contains an unmatched '{'.");
                }

                string name = path.Substring(open + 1, close - open - 1);
                if (!declared.Contains(name))
                {
                    throw Invalid(operationNode, "The path template parameter '" + name + "' has no matching path parameter.");
                }

                used.Add(name);

                cursor = close + 1;
            }

            declared.ExceptWith(used);
            if (declared.Count > 0)
            {
                throw Invalid(
                    operationNode,
                    "Path parameter '" + declared.OrderBy(static value => value, StringComparer.Ordinal).First() +
                    "' is not present in the path template.");
            }
        }

        private OpenApiSemanticRequestBody? ParseRequestBody(
            SpecNode? node,
            HashSet<string> referenceStack,
            string suggestedName)
        {
            if (node is null)
            {
                return null;
            }

            RequireKind(node, SpecValueKind.Object, "The requestBody field must be an object or an internal $ref.");
            if (TryGetReference(node, out string reference, out SpecNode referenceNode))
            {
                JsonPointerResolver.GetComponentName(reference, "requestBodies", referenceNode);
                return ParseReferenced(
                    reference,
                    referenceNode,
                    referenceStack,
                    target => ParseRequestBody(target, referenceStack, suggestedName)!);
            }

            bool required = GetOptionalBoolean(node, "required") ?? false;
            ParsedContent content = ParseContent(RequireProperty(node, "content"), suggestedName);
            if (content.Schema is null)
            {
                throw Invalid(node, "A request body must define a JSON schema.");
            }

            return new OpenApiSemanticRequestBody(
                required,
                content.MediaType,
                content.Schema,
                OpenApiSourceLocation.FromNode(node));
        }

        private ParsedResponses ParseResponses(SpecNode responsesNode, string suggestedName)
        {
            RequireKind(responsesNode, SpecValueKind.Object, "The responses field must be an object.");
            var successCodes = new List<string>();
            OpenApiSemanticSchema? successSchema = null;
            string? successSignature = null;

            foreach (SpecProperty responseProperty in responsesNode.EnumerateObject()
                         .OrderBy(static value => value.Name, StringComparer.Ordinal))
            {
                bool isSuccess = IsSuccessStatusCode(responseProperty.Name);
                OpenApiSemanticSchema? schema = ParseResponse(
                    responseProperty.Value,
                    new HashSet<string>(StringComparer.Ordinal),
                    suggestedName);
                if (!isSuccess)
                {
                    continue;
                }

                string signature = GetSchemaSignature(schema);
                if (successSignature is not null && !string.Equals(successSignature, signature, StringComparison.Ordinal))
                {
                    throw new OpenApiSemanticException(
                        OpenApiSemanticErrorKind.InconsistentResponse,
                        "All successful responses for an operation must use the same JSON body contract.",
                        responseProperty.Value);
                }

                successSignature = signature;
                successSchema = schema;
                successCodes.Add(responseProperty.Name.ToUpperInvariant());
            }

            if (successCodes.Count == 0)
            {
                throw Invalid(responsesNode, "At least one 2xx response is required by the Phase 4 MVP.");
            }

            return new ParsedResponses(successSchema, successCodes.OrderBy(static value => value, StringComparer.Ordinal).ToArray());
        }

        private OpenApiSemanticSchema? ParseResponse(
            SpecNode node,
            HashSet<string> referenceStack,
            string suggestedName)
        {
            RequireKind(node, SpecValueKind.Object, "Each response must be an object or an internal $ref.");
            if (TryGetReference(node, out string reference, out SpecNode referenceNode))
            {
                JsonPointerResolver.GetComponentName(reference, "responses", referenceNode);
                return ParseReferenced(
                    reference,
                    referenceNode,
                    referenceStack,
                    target => ParseResponse(target, referenceStack, suggestedName));
            }

            RequireString(RequireProperty(node, "description"), "The response description must be a string.");
            SpecNode? headers = GetProperty(node, "headers");
            if (headers is not null && headers.ValueKind == SpecValueKind.Object && headers.EnumerateObject().Any())
            {
                throw Unsupported(headers, "Response headers are not exposed by the Phase 4 client contract.");
            }

            ThrowIfPresent(node, "links", "OpenAPI response links are not supported by the Phase 4 MVP.");
            SpecNode? contentNode = GetProperty(node, "content");
            return contentNode is null ? null : ParseContent(contentNode, suggestedName).Schema;
        }

        private ParsedContent ParseContent(SpecNode contentNode, string suggestedName)
        {
            RequireKind(contentNode, SpecValueKind.Object, "The content field must be an object.");
            var supported = new List<(string MediaType, OpenApiSemanticSchema Schema)>();
            foreach (SpecProperty mediaProperty in contentNode.EnumerateObject()
                         .OrderBy(static value => value.Name, StringComparer.Ordinal))
            {
                if (!IsJsonMediaType(mediaProperty.Name))
                {
                    throw Unsupported(
                        mediaProperty.Value,
                        "Only application/json and application/*+json media types are supported: '" +
                        mediaProperty.Name + "'.");
                }

                RequireKind(mediaProperty.Value, SpecValueKind.Object, "Each media type entry must be an object.");
                OpenApiSemanticSchema schema = ParseSchema(
                    RequireProperty(mediaProperty.Value, "schema"),
                    suggestedName,
                    new HashSet<string>(StringComparer.Ordinal));
                supported.Add((mediaProperty.Name, schema));
            }

            if (supported.Count == 0)
            {
                return new ParsedContent(string.Empty, null);
            }

            string signature = GetSchemaSignature(supported[0].Schema);
            for (int index = 1; index < supported.Count; index++)
            {
                if (!string.Equals(signature, GetSchemaSignature(supported[index].Schema), StringComparison.Ordinal))
                {
                    throw new OpenApiSemanticException(
                        OpenApiSemanticErrorKind.InconsistentResponse,
                        "All JSON media types must use the same schema in the Phase 4 MVP.",
                        contentNode);
                }
            }

            return new ParsedContent(supported[0].MediaType, supported[0].Schema);
        }

        private OpenApiSemanticSchema ParseSchema(
            SpecNode node,
            string suggestedName,
            HashSet<string> referenceStack)
        {
            RequireKind(node, SpecValueKind.Object, "A schema must be an object in the Phase 4 MVP.");
            ValidateUnsupportedSchemaKeywords(node);

            if (TryGetReference(node, out string reference, out SpecNode referenceNode))
            {
                string referenceName = JsonPointerResolver.GetComponentName(reference, "schemas", referenceNode);
                ParseReferenced(
                    reference,
                    referenceNode,
                    referenceStack,
                    target => ParseSchema(target, referenceName, referenceStack));
                return new OpenApiSemanticSchema(
                    OpenApiSemanticSchemaKind.Reference,
                    suggestedName,
                    string.Empty,
                    ParseNullable(node),
                    referenceName,
                    Array.Empty<OpenApiSemanticProperty>(),
                    null,
                    Array.Empty<string>(),
                    OpenApiSourceLocation.FromNode(node));
            }

            bool nullable = ParseNullable(node);
            string type = ParseSchemaType(node, ref nullable);
            string format = GetOptionalString(node, "format") ?? string.Empty;
            if (type == "string" && string.Equals(format, "binary", StringComparison.OrdinalIgnoreCase))
            {
                throw Unsupported(node, "Binary schemas are outside the JSON-only Phase 4 MVP.");
            }

            SpecNode? enumNode = GetProperty(node, "enum");
            if (enumNode is not null)
            {
                if (type != "string")
                {
                    throw Unsupported(enumNode, "Only string enums are supported by the Phase 4 MVP.");
                }

                IReadOnlyList<string> enumValues = ParseStringArray(enumNode, "Enum values must be strings.");
                if (enumValues.Count == 0)
                {
                    throw Invalid(enumNode, "An enum must contain at least one value.");
                }

                return new OpenApiSemanticSchema(
                    OpenApiSemanticSchemaKind.Enum,
                    suggestedName,
                    format,
                    nullable,
                    string.Empty,
                    Array.Empty<OpenApiSemanticProperty>(),
                    null,
                    enumValues,
                    OpenApiSourceLocation.FromNode(node));
            }

            switch (type)
            {
                case "string":
                    return CreateSimpleSchema(OpenApiSemanticSchemaKind.String, node, suggestedName, format, nullable);
                case "integer":
                    return CreateSimpleSchema(OpenApiSemanticSchemaKind.Integer, node, suggestedName, format, nullable);
                case "number":
                    return CreateSimpleSchema(OpenApiSemanticSchemaKind.Number, node, suggestedName, format, nullable);
                case "boolean":
                    return CreateSimpleSchema(OpenApiSemanticSchemaKind.Boolean, node, suggestedName, format, nullable);
                case "array":
                    OpenApiSemanticSchema itemSchema = ParseSchema(
                        RequireProperty(node, "items"),
                        suggestedName + "Item",
                        referenceStack);
                    return new OpenApiSemanticSchema(
                        OpenApiSemanticSchemaKind.Array,
                        suggestedName,
                        format,
                        nullable,
                        string.Empty,
                        Array.Empty<OpenApiSemanticProperty>(),
                        itemSchema,
                        Array.Empty<string>(),
                        OpenApiSourceLocation.FromNode(node));
                case "object":
                    return ParseObjectSchema(node, suggestedName, format, nullable, referenceStack);
                default:
                    throw Unsupported(node, "Unsupported schema type: '" + type + "'.");
            }
        }

        private OpenApiSemanticSchema ParseObjectSchema(
            SpecNode node,
            string suggestedName,
            string format,
            bool nullable,
            HashSet<string> referenceStack)
        {
            SpecNode? additionalProperties = GetProperty(node, "additionalProperties");
            if (additionalProperties is not null && additionalProperties.ValueKind != SpecValueKind.False)
            {
                throw Unsupported(additionalProperties, "additionalProperties maps are not supported by the Phase 4 MVP.");
            }

            SpecNode? propertiesNode = GetProperty(node, "properties");
            if (propertiesNode is null)
            {
                throw Unsupported(node, "Free-form object schemas are not supported by the Phase 4 MVP.");
            }

            RequireKind(propertiesNode, SpecValueKind.Object, "The schema properties field must be an object.");
            var required = new HashSet<string>(StringComparer.Ordinal);
            var requiredLocations = new Dictionary<string, SpecNode>(StringComparer.Ordinal);
            SpecNode? requiredNode = GetProperty(node, "required");
            if (requiredNode is not null)
            {
                RequireKind(requiredNode, SpecValueKind.Array, "Required property names must be strings.");
                foreach (SpecNode requiredItem in requiredNode.EnumerateArray())
                {
                    string value = RequireString(requiredItem, "Required property names must be strings.");
                    if (!required.Add(value))
                    {
                        throw Invalid(
                            requiredItem,
                            "The required array contains the property name '" + value + "' more than once.");
                    }

                    requiredLocations.Add(value, requiredItem);
                }
            }

            var properties = new List<OpenApiSemanticProperty>();
            var propertyNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (SpecProperty property in propertiesNode.EnumerateObject()
                         .OrderBy(static value => value.Name, StringComparer.Ordinal))
            {
                propertyNames.Add(property.Name);
                OpenApiSemanticSchema propertySchema = ParseSchema(
                    property.Value,
                    suggestedName + ToPascalFragment(property.Name),
                    referenceStack);
                properties.Add(new OpenApiSemanticProperty(
                    property.Name,
                    required.Contains(property.Name),
                    propertySchema,
                    OpenApiSourceLocation.FromNode(property.Value)));
            }

            string[] missingRequiredProperties = required
                .Where(value => !propertyNames.Contains(value))
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray();
            if (missingRequiredProperties.Length > 0)
            {
                string missingProperty = missingRequiredProperties[0];
                throw Invalid(
                    requiredLocations[missingProperty],
                    "Required property '" + missingProperty + "' is not declared in schema properties.");
            }

            return new OpenApiSemanticSchema(
                OpenApiSemanticSchemaKind.Object,
                suggestedName,
                format,
                nullable,
                string.Empty,
                properties,
                null,
                Array.Empty<string>(),
                OpenApiSourceLocation.FromNode(node));
        }

        private static void ValidateUnsupportedSchemaKeywords(SpecNode node)
        {
            string[] unsupported =
            {
                "allOf", "anyOf", "oneOf", "not", "discriminator", "patternProperties",
                "unevaluatedProperties", "unevaluatedItems", "contains", "prefixItems",
                "dependentSchemas", "dependentRequired", "if", "then", "else", "$dynamicRef",
                "readOnly", "writeOnly"
            };
            foreach (string name in unsupported)
            {
                ThrowIfPresent(node, name, "Schema keyword '" + name + "' is not supported by the Phase 4 MVP.");
            }
        }

        private bool ParseNullable(SpecNode node)
        {
            SpecNode? nullableNode = GetProperty(node, "nullable");
            if (nullableNode is null)
            {
                return false;
            }

            if (_minorVersion == 1)
            {
                throw Unsupported(nullableNode, "OpenAPI 3.1 nullable schemas must use a type union containing 'null'.");
            }

            return RequireBoolean(nullableNode, "The nullable field must be a boolean.");
        }

        private string ParseSchemaType(SpecNode node, ref bool nullable)
        {
            SpecNode typeNode = RequireProperty(node, "type");
            if (typeNode.ValueKind == SpecValueKind.String)
            {
                return typeNode.GetString() ?? string.Empty;
            }

            if (_minorVersion != 1 || typeNode.ValueKind != SpecValueKind.Array)
            {
                throw Invalid(typeNode, "The schema type must be a string, or a nullable two-item type array in OpenAPI 3.1.");
            }

            IReadOnlyList<string> types = ParseStringArray(typeNode, "OpenAPI 3.1 type union entries must be strings.");
            string[] nonNullTypes = types.Where(static value => value != "null").Distinct(StringComparer.Ordinal).ToArray();
            if (types.Count != 2 || nonNullTypes.Length != 1 || !types.Contains("null"))
            {
                throw Unsupported(typeNode, "Only a single schema type combined with 'null' is supported for OpenAPI 3.1.");
            }

            nullable = true;
            return nonNullTypes[0];
        }

        private T ParseReferenced<T>(
            string reference,
            SpecNode referenceNode,
            HashSet<string> referenceStack,
            Func<SpecNode, T> parser)
        {
            if (!referenceStack.Add(reference))
            {
                throw new OpenApiSemanticException(
                    OpenApiSemanticErrorKind.CyclicReference,
                    "A cyclic internal $ref was detected at '" + reference + "'.",
                    referenceNode);
            }

            try
            {
                return parser(_resolver.Resolve(reference, referenceNode));
            }
            finally
            {
                referenceStack.Remove(reference);
            }
        }

        private static int ParseSupportedVersion(string version, SpecNode node)
        {
            string[] segments = version.Split('.');
            if (segments.Length != 3 ||
                !int.TryParse(segments[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major) ||
                !int.TryParse(segments[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor) ||
                !int.TryParse(segments[2], NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                throw Invalid(node, "The openapi field must use a numeric major.minor.patch version.");
            }

            if (major != 3 || (minor != 0 && minor != 1))
            {
                throw Unsupported(
                    node,
                    "The Phase 4 MVP supports OpenAPI 3.0.* and 3.1.*. Found '" + version + "'.");
            }

            return minor;
        }

        private static bool IsJsonMediaType(string value)
        {
            if (string.Equals(value, "application/json", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            int separator = value.IndexOf(';');
            string mediaType = separator < 0 ? value : value.Substring(0, separator);
            return mediaType.StartsWith("application/", StringComparison.OrdinalIgnoreCase) &&
                   mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSuccessStatusCode(string value)
        {
            if (string.Equals(value, "2XX", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return value.Length == 3 &&
                   value[0] == '2' &&
                   char.IsDigit(value[1]) &&
                   char.IsDigit(value[2]);
        }

        private static string GetSchemaSignature(OpenApiSemanticSchema? schema)
        {
            if (schema is null)
            {
                return "void";
            }

            switch (schema.Kind)
            {
                case OpenApiSemanticSchemaKind.Reference:
                    return "ref:" + schema.ReferenceName + (schema.Nullable ? "?" : string.Empty);
                case OpenApiSemanticSchemaKind.Array:
                    return "array:" + GetSchemaSignature(schema.ItemSchema) + (schema.Nullable ? "?" : string.Empty);
                case OpenApiSemanticSchemaKind.Object:
                    return "object:" + string.Join(
                        ",",
                        schema.Properties.Select(static property =>
                            property.WireName + ":" + property.Required + ":" + GetSchemaSignature(property.Schema))) +
                           (schema.Nullable ? "?" : string.Empty);
                case OpenApiSemanticSchemaKind.Enum:
                    return "enum:" + string.Join(",", schema.EnumValues) + (schema.Nullable ? "?" : string.Empty);
                default:
                    return schema.Kind + ":" + schema.Format + (schema.Nullable ? "?" : string.Empty);
            }
        }

        private static OpenApiSemanticSchema CreateSimpleSchema(
            OpenApiSemanticSchemaKind kind,
            SpecNode node,
            string suggestedName,
            string format,
            bool nullable)
        {
            return new OpenApiSemanticSchema(
                kind,
                suggestedName,
                format,
                nullable,
                string.Empty,
                Array.Empty<OpenApiSemanticProperty>(),
                null,
                Array.Empty<string>(),
                OpenApiSourceLocation.FromNode(node));
        }

        private static IReadOnlyList<string> ParseStringArray(SpecNode node, string errorMessage)
        {
            RequireKind(node, SpecValueKind.Array, errorMessage);
            var values = new List<string>();
            foreach (SpecNode item in node.EnumerateArray())
            {
                values.Add(RequireString(item, errorMessage));
            }

            return values;
        }

        private static bool TryGetReference(SpecNode node, out string reference, out SpecNode referenceNode)
        {
            if (node.TryGetProperty("$ref", out referenceNode))
            {
                reference = RequireString(referenceNode, "The $ref value must be a string.");
                return true;
            }

            reference = string.Empty;
            referenceNode = node;
            return false;
        }

        private static SpecNode RequireProperty(SpecNode node, string name)
        {
            if (!node.TryGetProperty(name, out SpecNode value))
            {
                throw Invalid(node, "Required field '" + name + "' is missing.");
            }

            return value;
        }

        private static SpecNode? GetProperty(SpecNode node, string name)
        {
            return node.TryGetProperty(name, out SpecNode value) ? value : null;
        }

        private static string? GetOptionalString(SpecNode node, string name)
        {
            SpecNode? value = GetProperty(node, name);
            return value is null ? null : RequireString(value, "The '" + name + "' field must be a string.");
        }

        private static bool? GetOptionalBoolean(SpecNode node, string name)
        {
            SpecNode? value = GetProperty(node, name);
            return value is null ? null : RequireBoolean(value, "The '" + name + "' field must be a boolean.");
        }

        private static string RequireString(SpecNode node, string message)
        {
            if (node.ValueKind != SpecValueKind.String)
            {
                throw Invalid(node, message);
            }

            return node.GetString() ?? string.Empty;
        }

        private static bool RequireBoolean(SpecNode node, string message)
        {
            if (node.ValueKind == SpecValueKind.True)
            {
                return true;
            }

            if (node.ValueKind == SpecValueKind.False)
            {
                return false;
            }

            throw Invalid(node, message);
        }

        private static void RequireKind(SpecNode node, SpecValueKind kind, string message)
        {
            if (node.ValueKind != kind)
            {
                throw Invalid(node, message);
            }
        }

        private static void ThrowIfPresent(SpecNode node, string name, string message)
        {
            if (node.TryGetProperty(name, out SpecNode value))
            {
                throw Unsupported(value, message);
            }
        }

        private static OpenApiSemanticException Invalid(SpecNode node, string message)
        {
            return new OpenApiSemanticException(OpenApiSemanticErrorKind.InvalidDocument, message, node);
        }

        private static OpenApiSemanticException Unsupported(SpecNode node, string message)
        {
            return new OpenApiSemanticException(OpenApiSemanticErrorKind.UnsupportedElement, message, node);
        }

        private static string EncodePointerToken(string value)
        {
            return value.Replace("~", "~0").Replace("/", "~1");
        }

        private static string ToPascalFragment(string value)
        {
            var characters = new List<char>(value.Length);
            bool uppercaseNext = true;
            foreach (char character in value)
            {
                if (!char.IsLetterOrDigit(character))
                {
                    uppercaseNext = true;
                    continue;
                }

                characters.Add(uppercaseNext ? char.ToUpperInvariant(character) : character);
                uppercaseNext = false;
            }

            return characters.Count == 0 ? "Value" : new string(characters.ToArray());
        }

        private readonly struct ParsedContent
        {
            internal ParsedContent(string mediaType, OpenApiSemanticSchema? schema)
            {
                MediaType = mediaType;
                Schema = schema;
            }

            internal string MediaType { get; }

            internal OpenApiSemanticSchema? Schema { get; }
        }

        private readonly struct ParsedResponses
        {
            internal ParsedResponses(OpenApiSemanticSchema? schema, IReadOnlyList<string> statusCodes)
            {
                Schema = schema;
                StatusCodes = statusCodes;
            }

            internal OpenApiSemanticSchema? Schema { get; }

            internal IReadOnlyList<string> StatusCodes { get; }
        }
    }
}
