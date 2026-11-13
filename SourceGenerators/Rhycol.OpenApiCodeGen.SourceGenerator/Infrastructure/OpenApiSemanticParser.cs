using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    internal sealed partial class OpenApiSemanticParser
    {
        private const string OpenApi31BaseDialect = "https://spec.openapis.org/oas/3.1/dialect/base";

        private static readonly string[] HttpMethods =
        {
            "delete", "get", "head", "options", "patch", "post", "put", "trace"
        };

        private static readonly HashSet<string> HttpMethodSet =
            new HashSet<string>(HttpMethods, StringComparer.Ordinal);

        private static readonly HashSet<string> ContentOnlyRequestHeaders =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Allow", "Content-Disposition", "Content-Encoding", "Content-Language",
                "Content-Length", "Content-Location", "Content-MD5", "Content-Range",
                "Expires", "Last-Modified"
            };

        private static readonly HashSet<string> AllowedReferenceSchemaSiblings =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "$comment",
                "default",
                "deprecated",
                "description",
                "example",
                "examples",
                "externalDocs",
                "readOnly",
                "summary",
                "title",
                "writeOnly",
                "xml"
            };

        private static readonly HashSet<string> SupportedSchemaKeywords =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "$comment", "$ref", "$schema", "additionalProperties", "default", "deprecated",
                "description", "enum", "example", "examples", "externalDocs", "format", "items",
                "nullable", "properties", "required", "summary", "title", "type", "xml",
                "contentEncoding", "contentMediaType", "readOnly", "writeOnly",
                "allOf", "anyOf", "oneOf", "not", "discriminator"
            };

        // Keywords that exist only in the OpenAPI 3.1 (JSON Schema 2020-12) dialect.
        private static readonly HashSet<string> OpenApi31SchemaKeywords =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "const", "$defs"
            };

        private readonly SpecNode _root;
        private readonly JsonPointerResolver _resolver;
        private readonly int _minorVersion;
        private readonly NormalizedSpecBundle? _bundle;
        private readonly Dictionary<string, NormalizedSpecDocument> _documents =
            new Dictionary<string, NormalizedSpecDocument>(StringComparer.Ordinal);
        private readonly Dictionary<NormalizedSpecNodeIdentity, OpenApiSemanticSchema> _schemas =
            new Dictionary<NormalizedSpecNodeIdentity, OpenApiSemanticSchema>();
        private readonly HashSet<NormalizedSpecNodeIdentity> _validatedResponseSchemas =
            new HashSet<NormalizedSpecNodeIdentity>();
        private readonly HashSet<string> _validatedExternalDocuments =
            new HashSet<string>(StringComparer.Ordinal);
        private string _currentDocumentId = "root";

        private OpenApiSemanticParser(SpecNode root, int minorVersion)
            : this(root, minorVersion, null)
        {
        }

        private OpenApiSemanticParser(
            SpecNode root,
            int minorVersion,
            NormalizedSpecBundle? bundle)
        {
            _root = root;
            _bundle = bundle;
            _resolver = bundle is null
                ? new JsonPointerResolver(root)
                : new JsonPointerResolver(bundle);
            _minorVersion = minorVersion;
            if (bundle is not null)
            {
                foreach (NormalizedSpecDocument document in bundle.Documents)
                {
                    _documents.Add(document.DocumentId, document);
                }
            }
        }

        internal static OpenApiSemanticDocument Parse(
            SpecNode root,
            Action<OpenApiSemanticParser, SpecNode>? injectWarnings = null)
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
            parser._injectWarnings = injectWarnings;
            return parser.ParseDocument();
        }

        internal static OpenApiSemanticDocument Parse(
            NormalizedSpecBundle bundle,
            Action<OpenApiSemanticParser, SpecNode>? injectWarnings = null)
        {
            if (bundle is null)
            {
                throw new ArgumentNullException(nameof(bundle));
            }

            int minorVersion;
            try
            {
                RequireKind(bundle.Root, SpecValueKind.Object, "The OpenAPI document root must be an object.");
                SpecNode versionNode = RequireProperty(bundle.Root, "openapi");
                string version = RequireString(versionNode, "The 'openapi' field must be a string.");
                minorVersion = ParseSupportedVersion(version, versionNode);
            }
            catch (OpenApiSemanticException exception) when (string.IsNullOrEmpty(exception.Location.SourcePath))
            {
                OpenApiSourceLocation location = new OpenApiSourceLocation(
                    bundle.SourcePath,
                    "root",
                    exception.Location.Line,
                    exception.Location.Column,
                    exception.Location.LogicalPath);
                throw new OpenApiSemanticException(
                    exception.Kind,
                    exception.Message,
                    location,
                    exception.AdditionalLocations);
            }

            var parser = new OpenApiSemanticParser(bundle.Root, minorVersion, bundle);
            parser._injectWarnings = injectWarnings;
            return parser.ParseDocument();
        }

        private OpenApiSemanticDocument ParseDocument()
        {
            try
            {
                _injectWarnings?.Invoke(this, _root);
                ValidateRootFeatures();
                ParseInfo(RequireProperty(_root, "info"));
                IReadOnlyList<OpenApiSemanticServer> servers = ParseServers(GetProperty(_root, "servers"), documentLevel: true);
                string baseUrl = servers[0].DefaultUrl;
                ParseComponentSchemas();
                _componentsParsed = true;
                ValidateRecursionIfObserved();
                IReadOnlyDictionary<NormalizedSpecNodeIdentity, OpenApiSemanticSchema> schemas = _schemas;
                ValidateNonSchemaComponents();
                IReadOnlyList<OpenApiSemanticOperation> operations = ParsePaths(RequireProperty(_root, "paths"));
                ValidateDiscriminatedUnionMembers();
                ValidateRecursionIfObserved();
                ValidateNoBinaryInJson(operations);

                return new OpenApiSemanticDocument(
                    _minorVersion,
                    baseUrl,
                    schemas,
                    operations,
                    CreateLocation(_root),
                    _warnings.ToList(),
                    servers);
            }
            catch (OpenApiSemanticException exception) when (_bundle is not null &&
                                                              string.IsNullOrEmpty(exception.Location.SourcePath))
            {
                throw RebaseException(exception, _currentDocumentId);
            }
        }

        private void ValidateRootFeatures()
        {
            ValidateDocumentDialect(_root, _minorVersion);

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
                    property.Name == "headers" ||
                    (_minorVersion >= 1 && property.Name == "pathItems") ||
                    property.Name.StartsWith("x-", StringComparison.Ordinal))
                {
                    continue;
                }

                throw Unsupported(
                    property.Value,
                    "The components section '" + property.Name + "' is not supported by the Phase 4 MVP.");
            }
        }

        private static void ValidateDocumentDialect(SpecNode documentRoot, int minorVersion)
        {
            SpecNode? dialectNode = GetProperty(documentRoot, "jsonSchemaDialect");
            if (dialectNode is not null)
            {
                string dialect = RequireAbsoluteDialectUri(dialectNode, "jsonSchemaDialect");
                if (minorVersion != 1 || !string.Equals(dialect, OpenApi31BaseDialect, StringComparison.Ordinal))
                {
                    throw Unsupported(dialectNode, "The jsonSchemaDialect '" + dialect + "' is not supported by the Phase 4 MVP.");
                }
            }
        }

        private static void ParseInfo(SpecNode infoNode)
        {
            RequireKind(infoNode, SpecValueKind.Object, "The 'info' field must be an object.");
            RequireString(RequireProperty(infoNode, "title"), "The info title must be a string.");
            RequireString(RequireProperty(infoNode, "version"), "The info version must be a string.");
        }

        private void ValidateServerUrl(SpecNode urlNode, string url)
        {
            if (url.StartsWith("//", StringComparison.Ordinal))
            {
                if (!Uri.TryCreate("https:" + url, UriKind.Absolute, out Uri? networkUrl) ||
                    !networkUrl.IsWellFormedOriginalString() ||
                    string.IsNullOrEmpty(networkUrl.Host))
                {
                    throw Invalid(urlNode, "The network-path server URL is not a valid URI reference.");
                }
            }
            else if (url.StartsWith("/", StringComparison.Ordinal))
            {
                if (!IsWellFormedRelativeServerUrl(url))
                {
                    throw Invalid(urlNode, "The root-relative server URL is not a valid URI reference.");
                }
            }
            else if (Uri.TryCreate(url, UriKind.Absolute, out Uri? absoluteUrl))
            {
                if (!absoluteUrl.IsWellFormedOriginalString())
                {
                    throw Invalid(urlNode, "The absolute server URL is not a valid URI.");
                }

                if (absoluteUrl.Scheme != Uri.UriSchemeHttp &&
                    absoluteUrl.Scheme != Uri.UriSchemeHttps)
                {
                    throw Unsupported(urlNode, "Only HTTP and HTTPS absolute server URLs are supported.");
                }
            }
            else if (!IsWellFormedRelativeServerUrl(url))
            {
                throw Invalid(urlNode, "The server URL is not a valid relative URI reference.");
            }
        }

        private static bool IsWellFormedRelativeServerUrl(string url)
        {
            if (Uri.IsWellFormedUriString(url, UriKind.Relative))
            {
                return true;
            }

            int fragmentIndex = url.IndexOf('#');
            if (fragmentIndex < 0 ||
                !Uri.IsWellFormedUriString(url.Substring(0, fragmentIndex), UriKind.Relative))
            {
                return false;
            }

            // UriKind.Relative rejects fragment-bearing references on some target runtimes.
            return Uri.IsWellFormedUriString(
                "https://example.test/" + url.TrimStart('/'), UriKind.Absolute);
        }

        private void ParseComponentSchemas()
        {
            SpecNode? components = GetProperty(_root, "components");
            SpecNode? schemas = components is null ? null : GetProperty(components, "schemas");
            if (schemas is null)
            {
                return;
            }

            RequireKind(schemas, SpecValueKind.Object, "The components.schemas field must be an object.");
            foreach (SpecProperty property in schemas.EnumerateObject()
                         .OrderBy(static value => value.Name, StringComparer.Ordinal))
            {
                var identity = new NormalizedSpecNodeIdentity(_currentDocumentId, property.Value.LogicalPath);
                if (_schemas.ContainsKey(identity))
                {
                    continue;
                }

                var stack = new HashSet<NormalizedSpecNodeIdentity> { identity };
                _schemasInProgress.Add(identity);
                try
                {
                    OpenApiSemanticSchema schema = ParseSchema(property.Value, property.Name, stack);
                    AddSchema(identity, schema);
                }
                finally
                {
                    _schemasInProgress.Remove(identity);
                }
            }
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
                (node, name) => ParseParameter(node, new HashSet<NormalizedSpecNodeIdentity>()));
            ValidateComponentMap(
                GetProperty(components, "requestBodies"),
                (node, name) => ParseRequestBody(
                    node,
                    new HashSet<NormalizedSpecNodeIdentity>(),
                    name + "Request"));
            ValidateComponentMap(
                GetProperty(components, "responses"),
                (node, name) => ParseResponseObject(
                    node,
                    new HashSet<NormalizedSpecNodeIdentity>(),
                    name + "Response",
                    ResponseParseMode.ValidateOnly,
                    forbidBodyContent: false));
            ValidateComponentMap(
                GetProperty(components, "headers"),
                (node, name) => ParseHeaderObject(node, name, new HashSet<NormalizedSpecNodeIdentity>()));
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

            foreach (SpecProperty pathProperty in pathsNode.EnumerateObject())
            {
                if (!pathProperty.Name.StartsWith("x-", StringComparison.Ordinal))
                {
                    CollectExplicitOperationIds(pathProperty.Value, new HashSet<NormalizedSpecNodeIdentity>());
                }
            }

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

                if (pathProperty.Name.IndexOf('?') >= 0 || pathProperty.Name.IndexOf('#') >= 0)
                {
                    OpenApiSourceLocation valueLocation = CreateLocation(pathProperty.Value);
                    throw new OpenApiSemanticException(
                        OpenApiSemanticErrorKind.InvalidDocument,
                        "OpenAPI path keys cannot contain raw '?' or '#' characters; percent-encode them in the path.",
                        new OpenApiSourceLocation(
                            valueLocation.SourcePath,
                            valueLocation.DocumentId,
                            pathProperty.Line,
                            pathProperty.Column,
                            valueLocation.LogicalPath),
                        Array.Empty<OpenApiSourceLocation>());
                }

                ParsePathItem(
                    pathProperty.Name,
                    pathProperty.Value,
                    new HashSet<NormalizedSpecNodeIdentity>(),
                    operationIds,
                    operations);
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
            IReadOnlyList<OpenApiSemanticParameter> pathParameters,
            IReadOnlyList<OpenApiSemanticServer> pathServers)
        {
            RequireKind(operationNode, SpecValueKind.Object, "Each OpenAPI operation must be an object.");
            ThrowIfPresent(operationNode, "callbacks", "OpenAPI callbacks are not supported by the Phase 4 MVP.");
            ThrowIfPresent(operationNode, "security", "Operation security requirements are not supported by the Phase 4 MVP.");
            ValidateOperationProperties(operationNode);

            string? explicitOperationId = GetOptionalString(operationNode, "operationId");
            if (explicitOperationId is not null && string.IsNullOrWhiteSpace(explicitOperationId))
            {
                throw Invalid(operationNode, "The operationId must not be empty.");
            }

            string operationId = explicitOperationId ?? AllocateSynthesizedOperationId(method, path);
            IReadOnlyList<OpenApiSemanticServer> operationServers =
                ParseServers(GetProperty(operationNode, "servers"), documentLevel: false);
            IReadOnlyList<OpenApiSemanticServer> effectiveServers =
                operationServers.Count > 0 ? operationServers : pathServers;
            string summary = GetOptionalString(operationNode, "summary") ?? string.Empty;
            string description = GetOptionalString(operationNode, "description") ?? string.Empty;
            bool deprecated = GetOptionalBoolean(operationNode, "deprecated") ?? false;
            IReadOnlyList<OpenApiSemanticParameter> operationParameters = ParseParameters(
                GetProperty(operationNode, "parameters"),
                new HashSet<NormalizedSpecNodeIdentity>());
            IReadOnlyList<OpenApiSemanticParameter> mergedParameters = MergeParameters(
                pathParameters,
                operationParameters);
            ValidatePathParameters(path, mergedParameters, operationNode);
            ValidateCookieHeaderConflict(mergedParameters, operationNode);

            SpecNode? requestBodyNode = GetProperty(operationNode, "requestBody");
            if (method == "trace" && requestBodyNode is not null)
            {
                throw Unsupported(requestBodyNode, "TRACE operations cannot declare a requestBody.");
            }

            OpenApiSemanticRequestBody? requestBody = ParseRequestBody(
                requestBodyNode,
                new HashSet<NormalizedSpecNodeIdentity>(),
                operationId + "Request");
            SpecNode? responsesNode = _minorVersion >= 1
                ? GetProperty(operationNode, "responses")
                : RequireProperty(operationNode, "responses");
            ParsedResponses responses = ParseResponses(
                responsesNode,
                operationId + "Response",
                operationId + "Error",
                method,
                operationNode);

            return new OpenApiSemanticOperation(
                operationId,
                summary,
                method.ToUpperInvariant(),
                path,
                mergedParameters,
                requestBody,
                responses.Schema,
                responses.StatusCodes,
                responses.MediaTypes,
                CreateLocation(operationNode),
                description,
                deprecated,
                effectiveServers,
                responses.Responses);
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
            HashSet<NormalizedSpecNodeIdentity> referenceStack)
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
                OpenApiSemanticParameter? parameter = ParseParameter(node, referenceStack);
                if (parameter is null)
                {
                    continue;
                }

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

        private OpenApiSemanticParameter? ParseParameter(
            SpecNode node,
            HashSet<NormalizedSpecNodeIdentity> referenceStack)
        {
            RequireKind(node, SpecValueKind.Object, "Each parameter must be an object or an internal $ref.");
            if (TryGetReference(node, out string reference, out SpecNode referenceNode))
            {
                ValidateReferenceObjectSiblings(node);
                _resolver.GetComponentName(_currentDocumentId, reference, "parameters", referenceNode);
                return ParseReferenced(
                    reference,
                    referenceNode,
                    referenceStack,
                    target => ParseParameter(target, referenceStack));
            }

            return ParseParameterCore(node);
        }

        private static bool IsHttpFieldName(string name)
        {
            if (name.Length == 0)
            {
                return false;
            }

            foreach (char character in name)
            {
                if ((character >= '0' && character <= '9') ||
                    (character >= 'A' && character <= 'Z') ||
                    (character >= 'a' && character <= 'z') ||
                    "!#$%&'*+-.^_`|~".IndexOf(character) >= 0)
                {
                    continue;
                }

                return false;
            }

            return true;
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
                int prematureClose = path.IndexOf('}', cursor);
                if (prematureClose >= 0 && (open < 0 || prematureClose < open))
                {
                    throw Invalid(operationNode, "The path template contains an unmatched '}'.");
                }

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

        private void ValidateResponseMetadataProperties(
            OpenApiSemanticSchema schema,
            HashSet<NormalizedSpecNodeIdentity> visited)
        {
            if (schema.Kind == OpenApiSemanticSchemaKind.Reference)
            {
                if (visited.Add(schema.ReferenceIdentity) &&
                    _schemas.TryGetValue(schema.ReferenceIdentity, out OpenApiSemanticSchema? target))
                {
                    ValidateResponseMetadataProperties(target, visited);
                }

                return;
            }

            if (schema.Kind == OpenApiSemanticSchemaKind.Array)
            {
                if (schema.ItemSchema is not null)
                {
                    ValidateResponseMetadataProperties(schema.ItemSchema, visited);
                }

                return;
            }

            if (schema.Kind == OpenApiSemanticSchemaKind.Map && schema.ValueSchema is not null)
            {
                ValidateResponseMetadataProperties(schema.ValueSchema, visited);
                return;
            }

            if (schema.Kind == OpenApiSemanticSchemaKind.Union)
            {
                foreach (OpenApiSemanticSchema member in schema.UnionMembers)
                {
                    ValidateResponseMetadataProperties(member, visited);
                }

                return;
            }

            if (schema.Kind != OpenApiSemanticSchemaKind.Object)
            {
                return;
            }

            if (schema.ValueSchema is not null)
            {
                ValidateResponseMetadataProperties(schema.ValueSchema, visited);
            }

            foreach (OpenApiSemanticProperty property in schema.Properties)
            {
                if (property.WireName == "$id" || property.WireName == "$ref" ||
                    property.WireName == "$values" || property.WireName == "$type")
                {
                    throw new OpenApiSemanticException(
                        OpenApiSemanticErrorKind.InvalidDocument,
                        "Response DTO property '" + property.WireName +
                        "' conflicts with Json.NET reference metadata.",
                        property.Location,
                        Array.Empty<OpenApiSourceLocation>());
                }

                ValidateResponseMetadataProperties(property.Schema, visited);
            }
        }

        private static void ValidateResponseProperties(SpecNode node)
        {
            foreach (SpecProperty property in node.EnumerateObject())
            {
                if (property.Name == "description" ||
                    property.Name == "headers" ||
                    property.Name == "content" ||
                    property.Name == "links" ||
                    property.Name.StartsWith("x-", StringComparison.Ordinal))
                {
                    continue;
                }

                throw Unsupported(property.Value, "Unsupported Response field: '" + property.Name + "'.");
            }
        }

        private void ValidateResponseContent(SpecNode contentNode)
        {
            RequireKind(contentNode, SpecValueKind.Object, "The content field must be an object.");
            foreach (SpecProperty mediaProperty in contentNode.EnumerateObject()
                         .OrderBy(static value => value.Name, StringComparer.Ordinal))
            {
                ParseMediaType(mediaProperty, allowWildcard: true);
                RequireKind(mediaProperty.Value, SpecValueKind.Object, "Each media type entry must be an object.");
                SpecNode? schemaNode = GetProperty(mediaProperty.Value, "schema");
                if (schemaNode is not null)
                {
                    ValidateResponseSchemaReferences(
                        schemaNode,
                        new HashSet<NormalizedSpecNodeIdentity>());
                }
            }
        }

        private void ValidateResponseSchemaReferences(
            SpecNode node,
            HashSet<NormalizedSpecNodeIdentity> referenceStack)
        {
            if (node.ValueKind == SpecValueKind.True || node.ValueKind == SpecValueKind.False)
            {
                return;
            }

            RequireKind(node, SpecValueKind.Object, "A response schema must be an object or boolean schema.");
            ValidateSchemaDialect(node);
            ValidateResponseSchemaKeywordShapes(node);
            if (TryGetReference(node, out string reference, out SpecNode referenceNode))
            {
                ResolvedSpecReference resolved = ResolveSchemaReference(reference, referenceNode, out _);
                if (referenceStack.Contains(resolved.Identity))
                {
                    throw CreateException(
                        OpenApiSemanticErrorKind.CyclicReference,
                        "A cyclic internal $ref was detected at '" + reference + "'.",
                        referenceNode,
                        new[] { CreateLocation(resolved.Node, resolved.DocumentId) });
                }

                // Registered before descending so that recursion through a container (property,
                // items, additionalProperties) terminates; the alias stack above still rejects
                // pure $ref cycles.
                if (_validatedResponseSchemas.Add(resolved.Identity))
                {
                    ParseResolvedReferenced(
                        reference,
                        referenceNode,
                        referenceStack,
                        resolved,
                        target =>
                        {
                            ValidateResponseSchemaReferences(target, referenceStack);
                            return true;
                        });
                }
            }

            // Containers start a fresh alias stack (see ParseSchema); other applicators keep the
            // current one so that a schema composed of itself is still rejected.
            ValidateResponseSchemaProperty(node, "items", new HashSet<NormalizedSpecNodeIdentity>());
            ValidateResponseSchemaProperty(node, "not", referenceStack);
            ValidateResponseSchemaProperty(node, "contains", referenceStack);
            ValidateResponseSchemaProperty(node, "propertyNames", referenceStack);
            ValidateResponseSchemaProperty(node, "unevaluatedProperties", referenceStack);
            ValidateResponseSchemaProperty(node, "unevaluatedItems", referenceStack);
            ValidateResponseSchemaProperty(node, "if", referenceStack);
            ValidateResponseSchemaProperty(node, "then", referenceStack);
            ValidateResponseSchemaProperty(node, "else", referenceStack);
            ValidateResponseSchemaMap(node, "properties", new HashSet<NormalizedSpecNodeIdentity>());
            ValidateResponseSchemaMap(node, "patternProperties", referenceStack);
            ValidateResponseSchemaMap(node, "dependentSchemas", referenceStack);
            ValidateResponseSchemaArray(node, "allOf", referenceStack);
            ValidateResponseSchemaArray(node, "anyOf", referenceStack);
            ValidateResponseSchemaArray(node, "oneOf", referenceStack);
            ValidateResponseSchemaArray(node, "prefixItems", referenceStack);

            SpecNode? additionalProperties = GetProperty(node, "additionalProperties");
            if (additionalProperties is not null &&
                additionalProperties.ValueKind != SpecValueKind.True &&
                additionalProperties.ValueKind != SpecValueKind.False)
            {
                ValidateResponseSchemaReferences(additionalProperties, new HashSet<NormalizedSpecNodeIdentity>());
            }
        }

        private static void ValidateResponseSchemaKeywordShapes(SpecNode node)
        {
            SpecNode? type = GetProperty(node, "type");
            if (type is not null)
            {
                if (type.ValueKind == SpecValueKind.String)
                {
                    RequireString(type, "The schema type must be a string or an array of strings.");
                }
                else if (type.ValueKind == SpecValueKind.Array)
                {
                    foreach (SpecNode item in type.EnumerateArray())
                    {
                        RequireString(item, "Schema type array entries must be strings.");
                    }
                }
                else
                {
                    throw Invalid(type, "The schema type must be a string or an array of strings.");
                }
            }

            SpecNode? required = GetProperty(node, "required");
            if (required is not null)
            {
                RequireKind(required, SpecValueKind.Array, "The schema required field must be an array of strings.");
                foreach (SpecNode item in required.EnumerateArray())
                {
                    RequireString(item, "Schema required entries must be strings.");
                }
            }

            SpecNode? enumNode = GetProperty(node, "enum");
            if (enumNode is not null)
            {
                RequireKind(enumNode, SpecValueKind.Array, "The schema enum field must be an array.");
            }

            ValidateOptionalStringKeyword(node, "format");
            ValidateOptionalStringKeyword(node, "title");
            ValidateOptionalStringKeyword(node, "description");
            ValidateOptionalStringKeyword(node, "pattern");
            ValidateOptionalStringKeyword(node, "contentEncoding");
            ValidateOptionalStringKeyword(node, "contentMediaType");
            ValidateOptionalBooleanKeyword(node, "nullable");
            ValidateOptionalBooleanKeyword(node, "deprecated");
            ValidateOptionalBooleanKeyword(node, "readOnly");
            ValidateOptionalBooleanKeyword(node, "writeOnly");
            ValidateOptionalBooleanKeyword(node, "uniqueItems");
        }

        private static void ValidateOptionalStringKeyword(SpecNode node, string keyword)
        {
            SpecNode? value = GetProperty(node, keyword);
            if (value is not null)
            {
                RequireString(value, "The schema '" + keyword + "' field must be a string.");
            }
        }

        private static void ValidateOptionalBooleanKeyword(SpecNode node, string keyword)
        {
            SpecNode? value = GetProperty(node, keyword);
            if (value is not null)
            {
                RequireBoolean(value, "The schema '" + keyword + "' field must be a boolean.");
            }
        }

        private void ValidateResponseSchemaProperty(
            SpecNode owner,
            string propertyName,
            HashSet<NormalizedSpecNodeIdentity> referenceStack)
        {
            SpecNode? schema = GetProperty(owner, propertyName);
            if (schema is not null)
            {
                ValidateResponseSchemaReferences(schema, referenceStack);
            }
        }

        private void ValidateResponseSchemaMap(
            SpecNode owner,
            string propertyName,
            HashSet<NormalizedSpecNodeIdentity> referenceStack)
        {
            SpecNode? map = GetProperty(owner, propertyName);
            if (map is null)
            {
                return;
            }

            RequireKind(map, SpecValueKind.Object, "The schema '" + propertyName + "' field must be an object.");
            foreach (SpecProperty property in map.EnumerateObject())
            {
                ValidateResponseSchemaReferences(property.Value, referenceStack);
            }
        }

        private void ValidateResponseSchemaArray(
            SpecNode owner,
            string propertyName,
            HashSet<NormalizedSpecNodeIdentity> referenceStack)
        {
            SpecNode? array = GetProperty(owner, propertyName);
            if (array is null)
            {
                return;
            }

            RequireKind(array, SpecValueKind.Array, "The schema '" + propertyName + "' field must be an array.");
            foreach (SpecNode schema in array.EnumerateArray())
            {
                ValidateResponseSchemaReferences(schema, referenceStack);
            }
        }

        private static ParsedOpenApiMediaType ParseMediaType(SpecProperty mediaProperty, bool allowWildcard = false)
        {
            if (!OpenApiMediaTypeParser.TryParse(
                    mediaProperty.Name,
                    out ParsedOpenApiMediaType mediaType,
                    out string error))
            {
                throw Invalid(
                    mediaProperty.Value,
                    "Invalid media type '" + mediaProperty.Name + "': " + error);
            }

            bool hasWildcard = mediaType.Type.IndexOf('*') >= 0 || mediaType.Subtype.IndexOf('*') >= 0;
            bool supportedWildcard =
                (string.Equals(mediaType.Type, "application", StringComparison.Ordinal) &&
                 string.Equals(mediaType.Subtype, "*+json", StringComparison.Ordinal)) ||
                (allowWildcard &&
                 string.Equals(mediaType.Subtype, "*", StringComparison.Ordinal) &&
                 (mediaType.Type.IndexOf('*') < 0 || string.Equals(mediaType.Type, "*", StringComparison.Ordinal)));
            if (hasWildcard && !supportedWildcard)
            {
                throw Unsupported(
                    mediaProperty.Value,
                    "Only application/*+json is supported as a wildcard media subtype" +
                    (allowWildcard ? " (body media types also accept */* and type/*)" : string.Empty) + ": '" +
                    mediaProperty.Name + "'.");
            }

            return mediaType;
        }

        private static string? GetSupportedRequestCharset(
            ParsedOpenApiMediaType mediaType,
            SpecNode mediaNode)
        {
            string? charset = mediaType.Charset;
            if (charset is null)
            {
                return null;
            }

            if (string.Equals(charset, "utf-8", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(charset, "utf8", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(charset, "unicode-1-1-utf-8", StringComparison.OrdinalIgnoreCase))
            {
                return "utf-8";
            }

            if (string.Equals(charset, "utf-16", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(charset, "utf-16le", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(charset, "unicode", StringComparison.OrdinalIgnoreCase))
            {
                return "utf-16";
            }

            if (string.Equals(charset, "utf-16be", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(charset, "bigendianunicode", StringComparison.OrdinalIgnoreCase))
            {
                return "utf-16BE";
            }

            throw Unsupported(
                mediaNode,
                "Request media type charset '" + charset +
                "' is not supported. Use UTF-8, UTF-16, or UTF-16BE.");
        }

        private void ValidateUnsupportedSchemaKeywords(SpecNode node)
        {
            ValidateSchemaDialect(node);
            foreach (SpecProperty property in node.EnumerateObject())
            {
                if (!SupportedSchemaKeywords.Contains(property.Name) &&
                    !(_minorVersion == 1 && OpenApi31SchemaKeywords.Contains(property.Name)) &&
                    !property.Name.StartsWith("x-", StringComparison.Ordinal))
                {
                    throw Unsupported(
                        property.Value,
                        "Schema keyword '" + property.Name + "' is not supported by the Phase 4 MVP.");
                }
            }
        }

        private void ValidateSchemaDialect(SpecNode node)
        {
            SpecNode? dialectNode = GetProperty(node, "$schema");
            if (dialectNode is not null)
            {
                string dialect = RequireAbsoluteDialectUri(dialectNode, "$schema");
                if (_minorVersion != 1 || !string.Equals(dialect, OpenApi31BaseDialect, StringComparison.Ordinal))
                {
                    throw Unsupported(dialectNode, "Schema $schema dialect '" + dialect + "' is not supported by the Phase 4 MVP.");
                }
            }
        }

        private static string RequireAbsoluteDialectUri(SpecNode node, string keyword)
        {
            string value = RequireString(node, "The '" + keyword + "' field must be an absolute URI.");
            if (!Uri.TryCreate(value, UriKind.Absolute, out _))
            {
                throw Invalid(node, "The '" + keyword + "' field must be an absolute URI.");
            }

            return value;
        }

        private void ValidateReferenceSchemaSiblings(SpecNode node)
        {
            foreach (SpecProperty property in node.EnumerateObject())
            {
                if (property.Name == "$ref" ||
                    property.Name.StartsWith("x-", StringComparison.Ordinal) ||
                    AllowedReferenceSchemaSiblings.Contains(property.Name) ||
                    (_minorVersion == 0 && property.Name == "nullable") ||
                    (_minorVersion == 1 && (property.Name == "not" || property.Name == "type")))
                {
                    continue;
                }

                throw Unsupported(
                    property.Value,
                    "Schema keyword '" + property.Name +
                    "' cannot be combined with $ref by the Phase 4 MVP.");
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

        private T ParseReferenced<T>(
            string reference,
            SpecNode referenceNode,
            HashSet<NormalizedSpecNodeIdentity> referenceStack,
            Func<SpecNode, T> parser)
        {
            ResolvedSpecReference resolved = ResolveReference(reference, referenceNode);
            return ParseResolvedReferenced(
                reference,
                referenceNode,
                referenceStack,
                resolved,
                parser);
        }

        private T ParseResolvedReferenced<T>(
            string reference,
            SpecNode referenceNode,
            HashSet<NormalizedSpecNodeIdentity> referenceStack,
            ResolvedSpecReference resolved,
            Func<SpecNode, T> parser)
        {
            if (!referenceStack.Add(resolved.Identity))
            {
                throw CreateException(
                    OpenApiSemanticErrorKind.CyclicReference,
                    "A cyclic internal $ref was detected at '" + reference + "'.",
                    referenceNode,
                    new[] { CreateLocation(resolved.Node, resolved.DocumentId) });
            }

            string previousDocumentId = _currentDocumentId;
            OpenApiSourceLocation sourceLocation = CreateLocation(referenceNode);
            try
            {
                _currentDocumentId = resolved.DocumentId;
                return parser(resolved.Node);
            }
            catch (OpenApiSemanticException exception) when (_bundle is not null)
            {
                if (string.IsNullOrEmpty(exception.Location.SourcePath))
                {
                    throw RebaseException(exception, resolved.DocumentId, sourceLocation);
                }

                throw WithAdditionalLocation(exception, sourceLocation);
            }
            finally
            {
                _currentDocumentId = previousDocumentId;
                referenceStack.Remove(resolved.Identity);
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

        private OpenApiSemanticSchema GetEffectiveSchema(OpenApiSemanticSchema schema)
        {
            var visited = new HashSet<NormalizedSpecNodeIdentity>();
            OpenApiSemanticSchema current = schema;
            while (current.Kind == OpenApiSemanticSchemaKind.Reference)
            {
                NormalizedSpecNodeIdentity identity = current.ReferenceIdentity;
                if (identity.IsEmpty ||
                    !visited.Add(identity) ||
                    !_schemas.TryGetValue(identity, out OpenApiSemanticSchema? referenced))
                {
                    return current;
                }

                current = referenced;
            }

            return current;
        }

        private bool IsSchemaNullable(OpenApiSemanticSchema schema)
        {
            var visited = new HashSet<NormalizedSpecNodeIdentity>();
            OpenApiSemanticSchema current = schema;
            while (true)
            {
                if (current.Nullable)
                {
                    return true;
                }

                if (current.Kind != OpenApiSemanticSchemaKind.Reference)
                {
                    return false;
                }

                NormalizedSpecNodeIdentity identity = current.ReferenceIdentity;
                if (identity.IsEmpty ||
                    !visited.Add(identity) ||
                    !_schemas.TryGetValue(identity, out OpenApiSemanticSchema? referenced))
                {
                    return false;
                }

                current = referenced;
            }
        }

        private static void ValidateResponseStatusCode(SpecProperty property)
        {
            string value = property.Name;
            if (string.Equals(value, "default", StringComparison.Ordinal) ||
                (value.Length == 3 &&
                 value[0] >= '1' && value[0] <= '5' &&
                 ((IsAsciiDigit(value[1]) && IsAsciiDigit(value[2])) ||
                  ((value[1] == 'X' || value[1] == 'x') &&
                   (value[2] == 'X' || value[2] == 'x')))))
            {
                return;
            }

            throw Invalid(
                property.Value,
                "Response status code '" + value +
                "' must be 'default', an ASCII HTTP status code, or a range such as '2XX'.");
        }

        private static bool IsSuccessStatusCode(string value)
        {
            if (string.Equals(value, "2XX", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return value.Length == 3 &&
                   value[0] == '2' &&
                   IsAsciiDigit(value[1]) &&
                   IsAsciiDigit(value[2]);
        }

        private static bool IsAsciiDigit(char value)
        {
            return value >= '0' && value <= '9';
        }

        private string GetSchemaSignature(OpenApiSemanticSchema? schema)
        {
            return GetSchemaSignature(schema, new HashSet<NormalizedSpecNodeIdentity>());
        }

        // active holds the referenced schemas whose signature is being built: a union (or object) can
        // lead back to an alias such as an array, and that cycle ends in a reference signature.
        private string GetSchemaSignature(
            OpenApiSemanticSchema? schema,
            HashSet<NormalizedSpecNodeIdentity> active)
        {
            if (schema is null)
            {
                return "void";
            }

            bool nullable = schema.Nullable;
            NormalizedSpecNodeIdentity terminalIdentity = default;
            var visited = new HashSet<NormalizedSpecNodeIdentity>();
            while (schema.Kind == OpenApiSemanticSchemaKind.Reference)
            {
                NormalizedSpecNodeIdentity identity = schema.ReferenceIdentity;
                if (identity.IsEmpty || !visited.Add(identity) ||
                    !_schemas.TryGetValue(identity, out OpenApiSemanticSchema? referenced))
                {
                    return BuildSchemaSignature(
                        "ref",
                        nullable,
                        identity.IsEmpty ? schema.ReferenceName : identity.ToString());
                }

                terminalIdentity = identity;
                schema = referenced;
                nullable |= schema.Nullable;
            }

            if (!terminalIdentity.IsEmpty &&
                (schema.Kind == OpenApiSemanticSchemaKind.Object ||
                 schema.Kind == OpenApiSemanticSchemaKind.Enum ||
                 schema.Kind == OpenApiSemanticSchemaKind.Union))
            {
                return BuildSchemaSignature("ref", nullable, terminalIdentity.ToString());
            }

            if (!terminalIdentity.IsEmpty && !active.Add(terminalIdentity))
            {
                return BuildSchemaSignature("ref", nullable, terminalIdentity.ToString());
            }

            try
            {
                return GetShapeSignature(schema, nullable, active);
            }
            finally
            {
                if (!terminalIdentity.IsEmpty)
                {
                    active.Remove(terminalIdentity);
                }
            }
        }

        private string GetShapeSignature(
            OpenApiSemanticSchema schema,
            bool nullable,
            HashSet<NormalizedSpecNodeIdentity> active)
        {
            switch (schema.Kind)
            {
                case OpenApiSemanticSchemaKind.Union:
                    // Members are compared as a set (order does not change the accepted values);
                    // the discriminator and oneOf / anyOf mode do.
                    return BuildSchemaSignature(
                        "union",
                        nullable,
                        new[]
                            {
                                schema.UnionIsOneOf ? "oneOf" : "anyOf",
                                schema.Discriminator is null
                                    ? string.Empty
                                    : schema.Discriminator.PropertyName
                            }
                            .Concat(schema.UnionMembers
                                .Select(member => GetSchemaSignature(member, active))
                                .OrderBy(static value => value, StringComparer.Ordinal))
                            .ToArray());
                case OpenApiSemanticSchemaKind.Any:
                    return BuildSchemaSignature("any", true);
                case OpenApiSemanticSchemaKind.FreeObject:
                    return BuildSchemaSignature("freeObject", nullable);
                case OpenApiSemanticSchemaKind.Array:
                    return BuildSchemaSignature("array", nullable, GetSchemaSignature(schema.ItemSchema, active));
                case OpenApiSemanticSchemaKind.Object:
                    return BuildSchemaSignature(
                        "object",
                        nullable,
                        schema.Properties.Select(property => BuildSchemaSignature(
                            "property",
                            false,
                            property.WireName,
                            property.Required ? "1" : "0",
                            GetSchemaSignature(property.Schema, active)))
                            .Concat(new[]
                            {
                                BuildSchemaSignature(
                                    "additional",
                                    false,
                                    schema.AdditionalProperties.ToString(),
                                    schema.ValueSchema is null ? string.Empty : GetSchemaSignature(schema.ValueSchema, active))
                            }).ToArray());
                case OpenApiSemanticSchemaKind.Map:
                    return BuildSchemaSignature(
                        "map",
                        nullable,
                        schema.ValueSchema is null ? string.Empty : GetSchemaSignature(schema.ValueSchema, active));
                case OpenApiSemanticSchemaKind.Enum:
                    return BuildSchemaSignature(
                        "enum",
                        nullable,
                        schema.TypedEnumValues
                            .Select(static value => value.Kind == OpenApiSemanticLiteralKind.String
                                ? value.RawText
                                : value.Kind + ":" + value.RawText)
                            .OrderBy(static value => value, StringComparer.Ordinal)
                            .ToArray());
                default:
                    return BuildSchemaSignature(schema.Kind.ToString(), nullable, schema.Format);
            }
        }

        private static string BuildSchemaSignature(string kind, bool nullable, params string[] parts)
        {
            return kind + ":" + (nullable ? "1" : "0") + ":" +
                   parts.Length.ToString(CultureInfo.InvariantCulture) + ":" +
                   string.Concat(parts.Select(part =>
                       part.Length.ToString(CultureInfo.InvariantCulture) + ":" + part));
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

        private void ValidateReferenceObjectSiblings(SpecNode node)
        {
            foreach (SpecProperty property in node.EnumerateObject())
            {
                if (property.Name == "$ref")
                {
                    continue;
                }

                if (_minorVersion == 1 &&
                    (property.Name == "summary" || property.Name == "description"))
                {
                    RequireString(property.Value, "Reference Object '" + property.Name + "' must be a string.");
                    continue;
                }

                throw Unsupported(
                    property.Value,
                    "Reference Object field '" + property.Name +
                    "' is not supported alongside $ref in OpenAPI 3." + _minorVersion + ".");
            }
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

        private ResolvedSpecReference ResolveReference(string reference, SpecNode referenceNode)
        {
            try
            {
                ResolvedSpecReference resolved = _resolver.ResolveReference(
                    _currentDocumentId, reference, referenceNode);
                ValidateExternalDocument(resolved, referenceNode);
                return resolved;
            }
            catch (OpenApiSemanticException exception)
            {
                if (_bundle is null ||
                    !string.IsNullOrEmpty(exception.Location.SourcePath))
                {
                    throw;
                }

                throw CreateException(
                    exception.Kind,
                    exception.Message,
                    referenceNode,
                    exception.AdditionalLocations);
            }
        }

        private void ValidateExternalDocument(
            ResolvedSpecReference resolved,
            SpecNode referenceNode)
        {
            if (resolved.Document is null ||
                string.Equals(resolved.DocumentId, "root", StringComparison.Ordinal) ||
                _validatedExternalDocuments.Contains(resolved.DocumentId) ||
                !resolved.Document.Root.TryGetProperty("openapi", out SpecNode versionNode))
            {
                return;
            }

            try
            {
                string version = RequireString(versionNode, "The 'openapi' field must be a string.");
                int minorVersion = ParseSupportedVersion(version, versionNode);
                if (minorVersion != _minorVersion)
                {
                    throw Unsupported(
                        versionNode,
                        "Referenced OpenAPI documents must use the same major.minor version as the entry document.");
                }

                ValidateDocumentDialect(resolved.Document.Root, minorVersion);
            }
            catch (OpenApiSemanticException exception)
            {
                throw RebaseException(
                    exception,
                    resolved.DocumentId,
                    CreateLocation(referenceNode));
            }

            _validatedExternalDocuments.Add(resolved.DocumentId);
        }

        private OpenApiSourceLocation CreateLocation(SpecNode node)
        {
            if (_documents.TryGetValue(_currentDocumentId, out NormalizedSpecDocument? document))
            {
                return OpenApiSourceLocation.FromNode(node, document.SourcePath, _currentDocumentId);
            }

            return OpenApiSourceLocation.FromNode(node);
        }

        private OpenApiSourceLocation CreateLocation(SpecNode node, string documentId)
        {
            if (_documents.TryGetValue(documentId, out NormalizedSpecDocument? document))
            {
                return OpenApiSourceLocation.FromNode(node, document.SourcePath, documentId);
            }

            return OpenApiSourceLocation.FromNode(node);
        }

        private OpenApiSemanticException CreateException(
            OpenApiSemanticErrorKind kind,
            string message,
            SpecNode node,
            IReadOnlyList<OpenApiSourceLocation>? additionalLocations = null)
        {
            return new OpenApiSemanticException(
                kind,
                message,
                CreateLocation(node),
                additionalLocations ?? Array.Empty<OpenApiSourceLocation>());
        }

        private OpenApiSemanticException RebaseException(
            OpenApiSemanticException exception,
            string documentId,
            OpenApiSourceLocation? additionalLocation = null)
        {
            if (!_documents.TryGetValue(documentId, out NormalizedSpecDocument? document))
            {
                return exception;
            }

            OpenApiSourceLocation location = new OpenApiSourceLocation(
                document.SourcePath,
                documentId,
                exception.Location.Line,
                exception.Location.Column,
                exception.Location.LogicalPath);
            IReadOnlyList<OpenApiSourceLocation> additionalLocations = exception.AdditionalLocations;
            if (additionalLocation.HasValue)
            {
                additionalLocations = exception.AdditionalLocations
                    .Concat(new[] { additionalLocation.Value })
                    .ToArray();
            }

            return new OpenApiSemanticException(
                exception.Kind,
                exception.Message,
                location,
                additionalLocations);
        }

        private static OpenApiSemanticException WithAdditionalLocation(
            OpenApiSemanticException exception,
            OpenApiSourceLocation additionalLocation)
        {
            return new OpenApiSemanticException(
                exception.Kind,
                exception.Message,
                exception.Location,
                exception.AdditionalLocations
                    .Concat(new[] { additionalLocation })
                    .ToArray());
        }

        private void AddSchema(NormalizedSpecNodeIdentity identity, OpenApiSemanticSchema schema)
        {
            if (_schemas.TryGetValue(identity, out OpenApiSemanticSchema? existing))
            {
                if (!ReferenceEquals(existing, schema))
                {
                    _schemas[identity] = schema;
                }
            }
            else
            {
                _schemas.Add(identity, schema);
            }

            if (_componentsParsed)
            {
                ValidateRecursionIfObserved();
            }
        }

        private string GetBareSchemaName(ResolvedSpecReference resolved)
        {
            string sourcePath = resolved.Document?.SourcePath ?? string.Empty;
            int separator = Math.Max(sourcePath.LastIndexOf('/'), sourcePath.LastIndexOf('\\'));
            string fileName = separator >= 0 ? sourcePath.Substring(separator + 1) : sourcePath;
            int extension = fileName.LastIndexOf('.');
            if (extension > 0)
            {
                fileName = fileName.Substring(0, extension);
            }

            return ToPascalFragment(string.IsNullOrWhiteSpace(fileName) ? "Value" : fileName);
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

        private readonly struct ParsedResponses
        {
            internal ParsedResponses(OpenApiSemanticSchema? schema, IReadOnlyList<string> statusCodes,
                IReadOnlyDictionary<string, IReadOnlyList<string>> mediaTypes,
                IReadOnlyList<OpenApiSemanticResponse> responses)
            {
                Responses = responses;
                Schema = schema;
                StatusCodes = statusCodes;
                MediaTypes = mediaTypes;
            }

            internal IReadOnlyList<OpenApiSemanticResponse> Responses { get; }

            internal OpenApiSemanticSchema? Schema { get; }

            internal IReadOnlyList<string> StatusCodes { get; }

            internal IReadOnlyDictionary<string, IReadOnlyList<string>> MediaTypes { get; }
        }
    }
}
