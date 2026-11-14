using System;
using System.Collections.Generic;
using System.Linq;

using F = Rhycol.OpenApiCodeGen.SourceGenerator.Swagger2NodeFactory;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// Converts one Swagger 2.0 document into an OpenAPI 3.0 SpecNode tree (see
    /// <see cref="Swagger2Normalizer"/>). The document level is handled here; schemas, parameters,
    /// responses and security schemes are in the other parts of the class.
    /// </summary>
    internal sealed partial class Swagger2DocumentConverter
    {
        private const string OpenApiVersion = "3.0.3";
        private const string DefaultScheme = "https";
        private const string DefaultRequestMediaType = "application/json";
        private const string DefaultResponseMediaType = "application/json";

        private static readonly string[] HttpMethods =
        {
            "delete", "get", "head", "options", "patch", "post", "put", "trace"
        };

        private readonly Swagger2Context _context;
        private readonly string _documentId;
        private readonly SpecNode _root;
        private readonly bool _isRoot;
        private IReadOnlyList<string> _globalConsumes = Array.Empty<string>();
        private IReadOnlyList<string> _globalProduces = Array.Empty<string>();

        internal Swagger2DocumentConverter(
            Swagger2Context context,
            string documentId,
            SpecNode root,
            bool isRoot)
        {
            _context = context;
            _documentId = documentId;
            _root = root;
            _isRoot = isRoot;
        }

        internal SpecNode Convert()
        {
            SpecNode versionNode = RequireProperty(_root, "swagger");
            if (!string.Equals(versionNode.GetString(), "2.0", StringComparison.Ordinal))
            {
                throw Unsupported(
                    versionNode,
                    "Only Swagger 2.0 documents are supported; the 'swagger' field must be the string \"2.0\".");
            }

            _globalConsumes = ReadMediaTypes(_root, "consumes");
            _globalProduces = ReadMediaTypes(_root, "produces");

            var properties = new List<SpecProperty>();
            bool serversEmitted = false;
            foreach (SpecProperty property in _root.EnumerateObject())
            {
                string path = F.Child(string.Empty, property.Name);
                switch (property.Name)
                {
                    case "swagger":
                        properties.Add(new SpecProperty(
                            "openapi",
                            property.Line,
                            property.Column,
                            F.String(property.Value, "/openapi", OpenApiVersion)));
                        break;
                    case "host":
                    case "basePath":
                    case "schemes":
                        if (!serversEmitted)
                        {
                            serversEmitted = true;
                            if (_isRoot)
                            {
                                properties.Add(F.Property(
                                    "servers",
                                    ConvertServers(
                                        GetProperty(_root, "host"),
                                        GetProperty(_root, "basePath"),
                                        GetProperty(_root, "schemes"),
                                        "/servers",
                                        property.Value),
                                    property.Value));
                            }
                        }

                        break;
                    case "consumes":
                    case "produces":
                    case "definitions":
                    case "parameters":
                    case "responses":
                    case "securityDefinitions":
                        // Moved to components below.
                        break;
                    case "paths":
                        properties.Add(F.Property(
                            property,
                            _isRoot
                                ? ConvertPaths(property.Value, "/paths")
                                : F.Object(property.Value, "/paths", Array.Empty<SpecProperty>())));
                        break;
                    case "security":
                    case "tags":
                        if (_isRoot)
                        {
                            properties.Add(F.Property(property, F.Clone(property.Value, path)));
                        }

                        break;
                    default:
                        // info, externalDocs, x-* extensions and unknown fields are copied as written. Fields
                        // with no OpenAPI 3.0 meaning are silently carried along as extensions; the parser
                        // ignores 'x-' fields and rejects unknown fields where it knows the object.
                        properties.Add(F.Property(property, F.Clone(property.Value, path)));
                        break;
                }
            }

            SpecNode? components = ConvertComponents();
            if (components is not null)
            {
                properties.Add(F.Property("components", components, _root));
            }

            return F.Object(_root, string.Empty, properties);
        }

        // ---- servers -----------------------------------------------------------------------------------

        /// <summary>
        /// host + basePath + schemes become servers. Without a host the server is the relative basePath (or
        /// "/"); a host without schemes uses https (the Swagger default, "the scheme used to access the
        /// definition", is unknown to a generator).
        /// </summary>
        private SpecNode ConvertServers(
            SpecNode? hostNode,
            SpecNode? basePathNode,
            SpecNode? schemesNode,
            string path,
            SpecNode origin)
        {
            string? host = null;
            if (hostNode is not null)
            {
                host = RequireString(hostNode, "The 'host' field must be a string.");
            }

            string basePath = string.Empty;
            if (basePathNode is not null)
            {
                basePath = RequireString(basePathNode, "The 'basePath' field must be a string.");
                if (basePath.Length > 0 && !basePath.StartsWith("/", StringComparison.Ordinal))
                {
                    throw Invalid(basePathNode, "The 'basePath' field must start with '/'.");
                }

                basePath = basePath.TrimEnd('/');
            }

            var urls = new List<(string Url, SpecNode Origin)>();
            if (string.IsNullOrEmpty(host))
            {
                urls.Add((basePath.Length == 0 ? "/" : basePath, basePathNode ?? origin));
            }
            else
            {
                var schemes = new List<(string Scheme, SpecNode Origin)>();
                if (schemesNode is not null)
                {
                    RequireKind(schemesNode, SpecValueKind.Array, "The 'schemes' field must be an array.");
                    foreach (SpecNode schemeNode in schemesNode.EnumerateArray())
                    {
                        schemes.Add((RequireString(schemeNode, "A scheme must be a string."), schemeNode));
                    }
                }

                if (schemes.Count == 0)
                {
                    schemes.Add((DefaultScheme, hostNode ?? origin));
                }

                foreach ((string scheme, SpecNode schemeOrigin) in schemes)
                {
                    urls.Add((scheme + "://" + host + basePath, schemeOrigin));
                }
            }

            return CreateServerArray(urls, path, hostNode ?? basePathNode ?? origin);
        }

        private static SpecNode CreateServerArray(
            IReadOnlyList<(string Url, SpecNode Origin)> urls,
            string path,
            SpecNode origin)
        {
            var servers = new List<SpecNode>();
            for (int index = 0; index < urls.Count; index++)
            {
                string serverPath = F.Index(path, index);
                SpecNode url = F.String(urls[index].Origin, serverPath + "/url", urls[index].Url);
                servers.Add(F.Object(
                    urls[index].Origin,
                    serverPath,
                    new[] { F.Property("url", url, urls[index].Origin) }));
            }

            return F.Array(origin, path, servers);
        }

        // ---- paths -------------------------------------------------------------------------------------

        private SpecNode ConvertPaths(SpecNode pathsNode, string path)
        {
            RequireKind(pathsNode, SpecValueKind.Object, "The 'paths' field must be an object.");
            var properties = new List<SpecProperty>();
            foreach (SpecProperty property in pathsNode.EnumerateObject())
            {
                string itemPath = F.Child(path, property.Name);
                SpecNode converted = property.Name.StartsWith("x-", StringComparison.Ordinal)
                    ? F.Clone(property.Value, itemPath)
                    : ConvertPathItem(property.Value, itemPath);
                properties.Add(F.Property(property, converted));
            }

            return F.Object(pathsNode, path, properties);
        }

        private SpecNode ConvertPathItem(SpecNode itemNode, string path)
        {
            RequireKind(itemNode, SpecValueKind.Object, "A Path Item must be an object.");
            if (itemNode.TryGetProperty("$ref", out SpecNode refNode))
            {
                throw Unsupported(refNode, "A Path Item $ref is not supported in Swagger 2.0 documents.");
            }

            IReadOnlyList<ResolvedParameter> pathParameters =
                ResolveParameters(GetProperty(itemNode, "parameters"));
            var properties = new List<SpecProperty>();
            foreach (SpecProperty property in itemNode.EnumerateObject())
            {
                string childPath = F.Child(path, property.Name);
                if (property.Name == "parameters")
                {
                    SpecNode? regular = ConvertRegularParameters(pathParameters, property.Value, childPath);
                    if (regular is not null)
                    {
                        properties.Add(F.Property(property, regular));
                    }
                }
                else if (Array.IndexOf(HttpMethods, property.Name) >= 0)
                {
                    properties.Add(F.Property(
                        property,
                        ConvertOperation(property.Value, childPath, pathParameters)));
                }
                else
                {
                    properties.Add(F.Property(property, F.Clone(property.Value, childPath)));
                }
            }

            return F.Object(itemNode, path, properties);
        }

        private SpecNode ConvertOperation(
            SpecNode operationNode,
            string path,
            IReadOnlyList<ResolvedParameter> pathParameters)
        {
            RequireKind(operationNode, SpecValueKind.Object, "An Operation must be an object.");
            IReadOnlyList<string> consumes = operationNode.TryGetProperty("consumes", out _)
                ? ReadMediaTypes(operationNode, "consumes")
                : _globalConsumes;
            IReadOnlyList<string> produces = operationNode.TryGetProperty("produces", out _)
                ? ReadMediaTypes(operationNode, "produces")
                : _globalProduces;
            IReadOnlyList<ResolvedParameter> operationParameters =
                ResolveParameters(GetProperty(operationNode, "parameters"));

            var properties = new List<SpecProperty>();
            foreach (SpecProperty property in operationNode.EnumerateObject())
            {
                string childPath = F.Child(path, property.Name);
                switch (property.Name)
                {
                    case "consumes":
                    case "produces":
                    case "schemes":
                        break;
                    case "parameters":
                        SpecNode? regular = ConvertRegularParameters(operationParameters, property.Value, childPath);
                        if (regular is not null)
                        {
                            properties.Add(F.Property(property, regular));
                        }

                        break;
                    case "responses":
                        properties.Add(F.Property(property, ConvertResponses(property.Value, childPath, produces)));
                        break;
                    default:
                        properties.Add(F.Property(property, F.Clone(property.Value, childPath)));
                        break;
                }
            }

            SpecNode? requestBody = ConvertRequestBody(
                pathParameters,
                operationParameters,
                consumes,
                F.Child(path, "requestBody"));
            if (requestBody is not null)
            {
                properties.Add(F.Property("requestBody", requestBody, requestBody));
            }

            // Operation-level schemes replace the document's: they become operation-level servers.
            if (operationNode.TryGetProperty("schemes", out SpecNode schemesNode))
            {
                SpecNode? host = GetProperty(_root, "host");
                if (host is not null)
                {
                    properties.Add(F.Property(
                        "servers",
                        ConvertServers(
                            host,
                            GetProperty(_root, "basePath"),
                            schemesNode,
                            F.Child(path, "servers"),
                            schemesNode),
                        schemesNode));
                }
                else
                {
                    _context.Warnings.Add((
                        schemesNode,
                        "The operation schemes are ignored: without a document-level host the server is relative, so a scheme cannot be applied."));
                }
            }

            return F.Object(operationNode, path, properties);
        }

        // ---- shared helpers ----------------------------------------------------------------------------

        private IReadOnlyList<string> ReadMediaTypes(SpecNode owner, string name)
        {
            SpecNode? node = GetProperty(owner, name);
            if (node is null)
            {
                return Array.Empty<string>();
            }

            RequireKind(node, SpecValueKind.Array, "The '" + name + "' field must be an array of media types.");
            var result = new List<string>();
            foreach (SpecNode item in node.EnumerateArray())
            {
                string value = RequireString(item, "A media type in '" + name + "' must be a string.");
                if (!result.Contains(value))
                {
                    result.Add(value);
                }
            }

            return result;
        }

        private static SpecNode? GetProperty(SpecNode owner, string name)
        {
            return owner.ValueKind == SpecValueKind.Object && owner.TryGetProperty(name, out SpecNode value)
                ? value
                : null;
        }

        private SpecNode RequireProperty(SpecNode owner, string name)
        {
            RequireKind(owner, SpecValueKind.Object, "A Swagger 2.0 object is expected here.");
            if (!owner.TryGetProperty(name, out SpecNode value))
            {
                throw Invalid(owner, "The required field '" + name + "' is missing.");
            }

            return value;
        }

        private void RequireStringReference(SpecNode referenceNode)
        {
            if (referenceNode.ValueKind != SpecValueKind.String)
            {
                throw Invalid(referenceNode, "The $ref value must be a string.");
            }
        }

        private string RequireString(SpecNode node, string message)
        {
            string? value = node.GetString();
            if (value is null)
            {
                throw Invalid(node, message);
            }

            return value;
        }

        private void RequireKind(SpecNode node, SpecValueKind kind, string message)
        {
            if (node.ValueKind != kind)
            {
                throw Invalid(node, message);
            }
        }

        private OpenApiSemanticException Invalid(SpecNode node, string message)
        {
            return Fail(OpenApiSemanticErrorKind.InvalidDocument, message, node);
        }

        private OpenApiSemanticException Unsupported(SpecNode node, string message)
        {
            return Fail(OpenApiSemanticErrorKind.UnsupportedElement, message, node);
        }

        private OpenApiSemanticException Fail(OpenApiSemanticErrorKind kind, string message, SpecNode node)
        {
            return new OpenApiSemanticException(
                kind,
                message,
                _context.CreateLocation(node, _documentId),
                Array.Empty<OpenApiSourceLocation>());
        }

        private void AddEdge(string sourcePointer, string targetDocumentId, string targetPointer)
        {
            if (_context.Bundle is not null)
            {
                _context.OutputEdges.Add(new NormalizedSpecReferenceEdge(
                    _documentId,
                    sourcePointer,
                    targetDocumentId,
                    targetPointer));
            }
        }
    }
}
