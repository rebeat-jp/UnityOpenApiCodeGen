using System;
using System.Collections.Generic;
using System.Linq;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// P5 (G28): components.securitySchemes and security requirements (document and operation level).
    /// Malformed schemes and references to undefined schemes are OACG100 at the offending node.
    /// </summary>
    internal sealed partial class OpenApiSemanticParser
    {
        private readonly Dictionary<string, OpenApiSemanticSecurityScheme> _securitySchemes =
            new Dictionary<string, OpenApiSemanticSecurityScheme>(StringComparer.Ordinal);

        private readonly List<OpenApiSemanticSecurityScheme> _securitySchemeList =
            new List<OpenApiSemanticSecurityScheme>();

        private IReadOnlyList<OpenApiSemanticSecurityRequirement> _rootSecurity =
            Array.Empty<OpenApiSemanticSecurityRequirement>();

        private void ParseSecuritySchemes()
        {
            SpecNode? components = GetProperty(_root, "components");
            SpecNode? schemes = components is null ? null : GetProperty(components, "securitySchemes");
            if (schemes is null)
            {
                return;
            }

            RequireKind(schemes, SpecValueKind.Object, "The components.securitySchemes field must be an object.");
            foreach (SpecProperty entry in schemes.EnumerateObject())
            {
                if (entry.Name.StartsWith("x-", StringComparison.Ordinal))
                {
                    continue;
                }

                if (entry.Name.Length == 0)
                {
                    throw Invalid(entry.Value, "A security scheme name must not be empty.");
                }

                OpenApiSemanticSecurityScheme scheme = ParseSecurityScheme(
                    entry.Value, entry.Name, new HashSet<NormalizedSpecNodeIdentity>());
                _securitySchemes[entry.Name] = scheme;
                _securitySchemeList.Add(scheme);
            }
        }

        private OpenApiSemanticSecurityScheme ParseSecurityScheme(
            SpecNode node,
            string key,
            HashSet<NormalizedSpecNodeIdentity> referenceStack)
        {
            RequireKind(node, SpecValueKind.Object, "Each security scheme must be an object or an internal $ref.");
            if (TryGetReference(node, out string reference, out SpecNode referenceNode))
            {
                ValidateReferenceObjectSiblings(node);
                _resolver.GetComponentName(_currentDocumentId, reference, "securitySchemes", referenceNode);
                return ParseReferenced(
                    reference,
                    referenceNode,
                    referenceStack,
                    target => ParseSecurityScheme(target, key, referenceStack));
            }

            SpecNode typeNode = RequireProperty(node, "type");
            string type = RequireString(typeNode, "The security scheme type must be a string.");
            string description = GetOptionalString(node, "description") ?? string.Empty;
            OpenApiSourceLocation location = CreateLocation(node);
            if (IsOpenApi32 && GetProperty(node, "deprecated") is SpecNode deprecatedNode)
            {
                RequireBoolean(deprecatedNode, "The security scheme deprecated field must be a boolean.");
            }

            switch (type)
            {
                case "apiKey":
                {
                    SpecNode nameNode = RequireProperty(node, "name");
                    string name = RequireString(nameNode, "The apiKey name must be a string.");
                    SpecNode inNode = RequireProperty(node, "in");
                    string where = RequireString(inNode, "The apiKey 'in' field must be a string.");
                    if (where != "header" && where != "query" && where != "cookie")
                    {
                        throw Invalid(inNode, "The apiKey 'in' field must be 'header', 'query' or 'cookie'.");
                    }

                    if (name.Length == 0)
                    {
                        throw Invalid(nameNode, "The apiKey name must not be empty.");
                    }

                    if (where == "header" &&
                        (string.Equals(name, "Cookie", StringComparison.OrdinalIgnoreCase) ||
                         name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase)))
                    {
                        throw Invalid(
                            nameNode,
                            "The apiKey header name must not be Cookie or a Content-* header; use an apiKey in a cookie or a header that is not managed by the HTTP stack.");
                    }

                    if (where != "query" && !IsHttpFieldName(name))
                    {
                        throw Invalid(
                            nameNode,
                            "The apiKey name must be an HTTP token when it is a header or a cookie name.");
                    }

                    return new OpenApiSemanticSecurityScheme(
                        key, OpenApiSemanticSecuritySchemeType.ApiKey, description, location,
                        apiKeyLocation: where, apiKeyName: name);
                }

                case "http":
                {
                    SpecNode schemeNode = RequireProperty(node, "scheme");
                    string scheme = RequireString(schemeNode, "The http security scheme must be a string.");
                    if (!IsHttpFieldName(scheme))
                    {
                        throw Invalid(schemeNode, "The http authentication scheme must be a non-empty HTTP token.");
                    }

                    return new OpenApiSemanticSecurityScheme(
                        key, OpenApiSemanticSecuritySchemeType.Http, description, location,
                        httpScheme: scheme,
                        bearerFormat: GetOptionalString(node, "bearerFormat") ?? string.Empty);
                }

                case "oauth2":
                {
                    string oauth2MetadataUrl = string.Empty;
                    if (IsOpenApi32)
                    {
                        // OpenAPI 3.2: the RFC 8414 metadata URL is documentation only (never contacted).
                        SpecNode? metadataNode = GetProperty(node, "oauth2MetadataUrl");
                        if (metadataNode is not null)
                        {
                            oauth2MetadataUrl = RequireString(metadataNode, "The oauth2MetadataUrl must be a string.");
                        }
                    }

                    return new OpenApiSemanticSecurityScheme(
                        key, OpenApiSemanticSecuritySchemeType.OAuth2, description, location,
                        flows: ParseOAuthFlows(RequireProperty(node, "flows")),
                        oauth2MetadataUrl: oauth2MetadataUrl);
                }

                case "openIdConnect":
                {
                    SpecNode urlNode = RequireProperty(node, "openIdConnectUrl");
                    return new OpenApiSemanticSecurityScheme(
                        key, OpenApiSemanticSecuritySchemeType.OpenIdConnect, description, location,
                        openIdConnectUrl: RequireString(urlNode, "The openIdConnectUrl must be a string."));
                }

                case "mutualTLS":
                    if (_minorVersion < 1)
                    {
                        throw Invalid(typeNode, "The mutualTLS security scheme requires OpenAPI 3.1 or later.");
                    }

                    return new OpenApiSemanticSecurityScheme(
                        key, OpenApiSemanticSecuritySchemeType.MutualTls, description, location);

                default:
                    throw Invalid(
                        typeNode,
                        "The security scheme type must be apiKey, http, oauth2, openIdConnect or mutualTLS.");
            }
        }

        private IReadOnlyList<OpenApiSemanticOAuthFlow> ParseOAuthFlows(SpecNode flowsNode)
        {
            RequireKind(flowsNode, SpecValueKind.Object, "The oauth2 flows field must be an object.");
            var result = new List<OpenApiSemanticOAuthFlow>();
            foreach (SpecProperty flow in flowsNode.EnumerateObject())
            {
                if (flow.Name.StartsWith("x-", StringComparison.Ordinal))
                {
                    continue;
                }

                bool needsAuthorization;
                bool needsToken;
                bool needsDevice = false;
                switch (flow.Name)
                {
                    case "deviceAuthorization" when IsOpenApi32:
                        needsAuthorization = false;
                        needsToken = true;
                        needsDevice = true;
                        break;
                    case "implicit":
                        needsAuthorization = true;
                        needsToken = false;
                        break;
                    case "password":
                    case "clientCredentials":
                        needsAuthorization = false;
                        needsToken = true;
                        break;
                    case "authorizationCode":
                        needsAuthorization = true;
                        needsToken = true;
                        break;
                    default:
                        throw Invalid(
                            flow.Value,
                            "Unknown oauth2 flow '" + flow.Name +
                            (IsOpenApi32
                                ? "'; expected implicit, password, clientCredentials, authorizationCode or deviceAuthorization."
                                : "'; expected implicit, password, clientCredentials or authorizationCode."));
                }

                RequireKind(flow.Value, SpecValueKind.Object, "Each oauth2 flow must be an object.");
                string authorizationUrl = needsAuthorization
                    ? RequireString(
                        RequireProperty(flow.Value, "authorizationUrl"),
                        "The oauth2 authorizationUrl must be a string.")
                    : string.Empty;
                string tokenUrl = needsToken
                    ? RequireString(
                        RequireProperty(flow.Value, "tokenUrl"),
                        "The oauth2 tokenUrl must be a string.")
                    : string.Empty;
                string deviceAuthorizationUrl = needsDevice
                    ? RequireString(
                        RequireProperty(flow.Value, "deviceAuthorizationUrl"),
                        "The oauth2 deviceAuthorizationUrl must be a string.")
                    : string.Empty;
                SpecNode scopesNode = RequireProperty(flow.Value, "scopes");
                RequireKind(scopesNode, SpecValueKind.Object, "The oauth2 scopes field must be an object.");
                var scopes = new List<KeyValuePair<string, string>>();
                foreach (SpecProperty scope in scopesNode.EnumerateObject())
                {
                    scopes.Add(new KeyValuePair<string, string>(
                        scope.Name,
                        RequireString(scope.Value, "An oauth2 scope description must be a string.")));
                }

                result.Add(new OpenApiSemanticOAuthFlow(flow.Name, authorizationUrl, tokenUrl, scopes, deviceAuthorizationUrl));
            }

            return result;
        }

        /// <summary>
        /// Parses a security array. Entries stay in document order because the first requirement the
        /// configured credentials can satisfy wins.
        /// </summary>
        private IReadOnlyList<OpenApiSemanticSecurityRequirement> ParseSecurityRequirements(SpecNode? securityNode)
        {
            if (securityNode is null)
            {
                return Array.Empty<OpenApiSemanticSecurityRequirement>();
            }

            RequireKind(securityNode, SpecValueKind.Array, "The 'security' field must be an array.");
            var result = new List<OpenApiSemanticSecurityRequirement>();
            foreach (SpecNode requirementNode in securityNode.EnumerateArray())
            {
                RequireKind(requirementNode, SpecValueKind.Object, "Each security requirement must be an object.");
                var entries = new List<OpenApiSemanticSecurityRequirementEntry>();
                foreach (SpecProperty entry in requirementNode.EnumerateObject())
                {
                    if (!_securitySchemes.ContainsKey(entry.Name))
                    {
                        throw Invalid(
                            entry.Value,
                            "The security requirement refers to the undefined security scheme '" + entry.Name + "'.");
                    }

                    RequireKind(entry.Value, SpecValueKind.Array, "The scopes of a security requirement must be an array.");
                    var scopes = new List<string>();
                    foreach (SpecNode scope in entry.Value.EnumerateArray())
                    {
                        scopes.Add(RequireString(scope, "A security requirement scope must be a string."));
                    }

                    entries.Add(new OpenApiSemanticSecurityRequirementEntry(entry.Name, scopes));
                }

                result.Add(new OpenApiSemanticSecurityRequirement(entries));
            }

            return result;
        }
    }
}
