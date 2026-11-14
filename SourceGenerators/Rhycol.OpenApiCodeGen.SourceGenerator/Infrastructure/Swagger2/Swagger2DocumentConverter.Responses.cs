using System;
using System.Collections.Generic;

using F = Rhycol.OpenApiCodeGen.SourceGenerator.Swagger2NodeFactory;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    internal sealed partial class Swagger2DocumentConverter
    {
        private const string BinaryResponseMediaType = "application/octet-stream";

        private SpecNode ConvertResponses(SpecNode responsesNode, string path, IReadOnlyList<string> produces)
        {
            RequireKind(responsesNode, SpecValueKind.Object, "The 'responses' field must be an object.");
            var properties = new List<SpecProperty>();
            foreach (SpecProperty property in responsesNode.EnumerateObject())
            {
                string childPath = F.Child(path, property.Name);
                SpecNode converted = property.Name.StartsWith("x-", StringComparison.Ordinal)
                    ? F.Clone(property.Value, childPath)
                    : ConvertResponse(property.Value, childPath, produces);
                properties.Add(F.Property(property, converted));
            }

            return F.Object(responsesNode, path, properties);
        }

        /// <summary>
        /// A Response Object: a schema goes under every produces media type (application/json without
        /// produces; application/octet-stream for a file), a response without schema has no content, headers
        /// become Header Objects with a schema and examples (media type keyed samples) are dropped.
        /// </summary>
        private SpecNode ConvertResponse(SpecNode node, string path, IReadOnlyList<string> produces)
        {
            RequireKind(node, SpecValueKind.Object, "A Response Object must be an object.");
            var properties = new List<SpecProperty>();
            foreach (SpecProperty property in node.EnumerateObject())
            {
                string childPath = F.Child(path, property.Name);
                SpecNode value = property.Value;
                switch (property.Name)
                {
                    case "$ref":
                        RequireStringReference(value);
                        properties.Add(F.Property(
                            property,
                            ConvertReference(value, childPath, ReferenceKind.Response)));
                        break;
                    case "schema":
                        properties.Add(new SpecProperty(
                            "content",
                            property.Line,
                            property.Column,
                            ConvertResponseContent(value, F.Child(path, "content"), produces)));
                        break;
                    case "headers":
                        RequireKind(value, SpecValueKind.Object, "The response 'headers' field must be an object.");
                        var headers = new List<SpecProperty>();
                        foreach (SpecProperty header in value.EnumerateObject())
                        {
                            headers.Add(F.Property(
                                header,
                                ConvertHeader(header.Value, F.Child(childPath, header.Name))));
                        }

                        properties.Add(F.Property(property, F.Object(value, childPath, headers)));
                        break;
                    case "examples":
                        // Media type keyed samples: documentation only, nothing to generate.
                        break;
                    default:
                        properties.Add(F.Property(property, F.Clone(value, childPath)));
                        break;
                }
            }

            return F.Object(node, path, properties);
        }

        private SpecNode ConvertResponseContent(SpecNode schema, string path, IReadOnlyList<string> produces)
        {
            bool isFile = schema.ValueKind == SpecValueKind.Object &&
                          schema.TryGetProperty("type", out SpecNode type) &&
                          string.Equals(type.GetString(), "file", StringComparison.Ordinal);
            IReadOnlyList<string> mediaTypes = produces.Count > 0
                ? produces
                : new[] { isFile ? BinaryResponseMediaType : DefaultResponseMediaType };
            var media = new List<SpecProperty>();
            foreach (string mediaType in mediaTypes)
            {
                string mediaPath = F.Child(path, mediaType);
                media.Add(F.Property(
                    mediaType,
                    F.Object(
                        schema,
                        mediaPath,
                        new[]
                        {
                            F.Property("schema", ConvertSchema(schema, F.Child(mediaPath, "schema")), schema)
                        }),
                    schema));
            }

            return F.Object(schema, path, media);
        }

        /// <summary>A response Header Object: the inline type fields become 'schema'.</summary>
        private SpecNode ConvertHeader(SpecNode node, string path)
        {
            RequireKind(node, SpecValueKind.Object, "A Header Object must be an object.");
            if (!node.TryGetProperty("type", out SpecNode typeNode))
            {
                throw Invalid(node, "A response header requires a 'type'.");
            }

            var properties = new List<SpecProperty>();
            foreach (SpecProperty property in node.EnumerateObject())
            {
                if (SimpleSchemaKeywords.Contains(property.Name) || property.Name == "collectionFormat")
                {
                    continue;
                }

                properties.Add(F.Property(property, F.Clone(property.Value, F.Child(path, property.Name))));
            }

            if (string.Equals(typeNode.GetString(), "array", StringComparison.Ordinal))
            {
                MapCollectionFormat(node, "header");
            }

            properties.Add(F.Property(
                "schema",
                BuildSimpleSchema(node, F.Child(path, "schema"), allowFile: false, copyDescription: false),
                node));
            return F.Object(node, path, properties);
        }

        // ---- security ----------------------------------------------------------------------------------

        /// <summary>
        /// securityDefinitions become components.securitySchemes: basic is http basic, apiKey is unchanged
        /// and oauth2 moves to flows (implicit, password, application = clientCredentials, accessCode =
        /// authorizationCode); the scopes are kept.
        /// </summary>
        private SpecNode ConvertSecurityDefinitions(SpecNode definitions, string path)
        {
            RequireKind(definitions, SpecValueKind.Object, "The 'securityDefinitions' field must be an object.");
            var schemes = new List<SpecProperty>();
            foreach (SpecProperty definition in definitions.EnumerateObject())
            {
                string schemePath = F.Child(path, definition.Name);
                schemes.Add(F.Property(definition, ConvertSecurityScheme(definition.Value, schemePath)));
            }

            return F.Object(definitions, path, schemes);
        }

        private SpecNode ConvertSecurityScheme(SpecNode node, string path)
        {
            RequireKind(node, SpecValueKind.Object, "A Security Scheme Object must be an object.");
            SpecNode typeNode = RequireProperty(node, "type");
            string type = RequireString(typeNode, "The security scheme type must be a string.");
            var properties = new List<SpecProperty>();
            foreach (SpecProperty property in node.EnumerateObject())
            {
                string childPath = F.Child(path, property.Name);
                switch (type)
                {
                    case "basic":
                        if (property.Name == "type")
                        {
                            properties.Add(F.Property(property, F.String(typeNode, childPath, "http")));
                            properties.Add(F.Property("scheme", F.String(typeNode, F.Child(path, "scheme"), "basic"), typeNode));
                        }
                        else
                        {
                            properties.Add(F.Property(property, F.Clone(property.Value, childPath)));
                        }

                        break;
                    case "apiKey":
                        properties.Add(F.Property(property, F.Clone(property.Value, childPath)));
                        break;
                    case "oauth2":
                        if (property.Name == "type" || property.Name == "description" ||
                            property.Name.StartsWith("x-", StringComparison.Ordinal))
                        {
                            properties.Add(F.Property(property, F.Clone(property.Value, childPath)));
                        }

                        // flow, authorizationUrl, tokenUrl and scopes move into 'flows' below.
                        break;
                    default:
                        throw Unsupported(typeNode, "The security scheme type '" + type + "' is not a Swagger 2.0 type.");
                }
            }

            if (type == "oauth2")
            {
                properties.Add(F.Property("flows", ConvertOAuth2Flows(node, F.Child(path, "flows")), node));
            }

            return F.Object(node, path, properties);
        }

        private SpecNode ConvertOAuth2Flows(SpecNode scheme, string path)
        {
            SpecNode flowNode = RequireProperty(scheme, "flow");
            string flow = RequireString(flowNode, "The oauth2 flow must be a string.");
            string targetFlow;
            switch (flow)
            {
                case "implicit":
                    targetFlow = "implicit";
                    break;
                case "password":
                    targetFlow = "password";
                    break;
                case "application":
                    targetFlow = "clientCredentials";
                    break;
                case "accessCode":
                    targetFlow = "authorizationCode";
                    break;
                default:
                    throw Invalid(flowNode, "Unknown oauth2 flow '" + flow + "'.");
            }

            string flowPath = F.Child(path, targetFlow);
            var properties = new List<SpecProperty>();
            foreach (SpecProperty property in scheme.EnumerateObject())
            {
                if (property.Name == "authorizationUrl" || property.Name == "tokenUrl")
                {
                    properties.Add(F.Property(property, F.Clone(property.Value, F.Child(flowPath, property.Name))));
                }
            }

            SpecProperty? scopes = null;
            foreach (SpecProperty property in scheme.EnumerateObject())
            {
                if (property.Name == "scopes")
                {
                    scopes = property;
                }
            }

            properties.Add(scopes is not null
                ? F.Property(scopes, F.Clone(scopes.Value, F.Child(flowPath, "scopes")))
                : F.Property(
                    "scopes",
                    F.Object(scheme, F.Child(flowPath, "scopes"), Array.Empty<SpecProperty>()),
                    scheme));
            return F.Object(
                flowNode,
                path,
                new[] { F.Property(targetFlow, F.Object(flowNode, flowPath, properties), flowNode) });
        }
    }
}
