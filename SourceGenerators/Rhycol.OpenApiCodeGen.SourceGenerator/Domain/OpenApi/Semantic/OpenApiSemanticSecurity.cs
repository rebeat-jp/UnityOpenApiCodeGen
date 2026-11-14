using System;
using System.Collections.Generic;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    internal enum OpenApiSemanticSecuritySchemeType
    {
        ApiKey,
        Http,
        OAuth2,
        OpenIdConnect,
        MutualTls
    }

    /// <summary>One OAuth2 flow, kept for documentation only (the client never obtains tokens).</summary>
    internal sealed class OpenApiSemanticOAuthFlow
    {
        internal OpenApiSemanticOAuthFlow(
            string name,
            string authorizationUrl,
            string tokenUrl,
            IReadOnlyList<KeyValuePair<string, string>> scopes,
            string deviceAuthorizationUrl = "")
        {
            DeviceAuthorizationUrl = deviceAuthorizationUrl ?? string.Empty;
            Name = name;
            AuthorizationUrl = authorizationUrl ?? string.Empty;
            TokenUrl = tokenUrl ?? string.Empty;
            Scopes = scopes ?? Array.Empty<KeyValuePair<string, string>>();
        }

        internal string Name { get; }

        internal string AuthorizationUrl { get; }

        internal string TokenUrl { get; }

        /// <summary>OpenAPI 3.2 deviceAuthorization flow: the device authorization endpoint (documentation only).</summary>
        internal string DeviceAuthorizationUrl { get; }

        internal IReadOnlyList<KeyValuePair<string, string>> Scopes { get; }
    }

    /// <summary>An entry of components.securitySchemes.</summary>
    internal sealed class OpenApiSemanticSecurityScheme
    {
        internal OpenApiSemanticSecurityScheme(
            string key,
            OpenApiSemanticSecuritySchemeType type,
            string description,
            OpenApiSourceLocation location,
            string apiKeyLocation = "",
            string apiKeyName = "",
            string httpScheme = "",
            string bearerFormat = "",
            IReadOnlyList<OpenApiSemanticOAuthFlow>? flows = null,
            string openIdConnectUrl = "",
            string oauth2MetadataUrl = "")
        {
            OAuth2MetadataUrl = oauth2MetadataUrl ?? string.Empty;
            Key = key;
            Type = type;
            Description = description ?? string.Empty;
            Location = location;
            ApiKeyLocation = apiKeyLocation ?? string.Empty;
            ApiKeyName = apiKeyName ?? string.Empty;
            HttpScheme = httpScheme ?? string.Empty;
            BearerFormat = bearerFormat ?? string.Empty;
            Flows = flows ?? Array.Empty<OpenApiSemanticOAuthFlow>();
            OpenIdConnectUrl = openIdConnectUrl ?? string.Empty;
        }

        /// <summary>The name used by security requirements (the key in components.securitySchemes).</summary>
        internal string Key { get; }

        internal OpenApiSemanticSecuritySchemeType Type { get; }

        internal string Description { get; }

        internal OpenApiSourceLocation Location { get; }

        /// <summary>apiKey: header, query or cookie.</summary>
        internal string ApiKeyLocation { get; }

        internal string ApiKeyName { get; }

        /// <summary>http: the scheme as written (bearer, basic, digest, ...).</summary>
        internal string HttpScheme { get; }

        internal string BearerFormat { get; }

        internal IReadOnlyList<OpenApiSemanticOAuthFlow> Flows { get; }

        internal string OpenIdConnectUrl { get; }

        /// <summary>OpenAPI 3.2 oauth2MetadataUrl (RFC 8414 metadata; documentation only, never contacted).</summary>
        internal string OAuth2MetadataUrl { get; }
    }

    /// <summary>One scheme use inside a requirement, with its scopes (documentation only).</summary>
    internal sealed class OpenApiSemanticSecurityRequirementEntry
    {
        internal OpenApiSemanticSecurityRequirementEntry(string schemeKey, IReadOnlyList<string> scopes)
        {
            SchemeKey = schemeKey;
            Scopes = scopes ?? Array.Empty<string>();
        }

        internal string SchemeKey { get; }

        internal IReadOnlyList<string> Scopes { get; }
    }

    /// <summary>
    /// A Security Requirement Object: every entry must be satisfied (AND). An empty requirement
    /// (<c>{}</c>) makes the operation callable anonymously.
    /// </summary>
    internal sealed class OpenApiSemanticSecurityRequirement
    {
        internal OpenApiSemanticSecurityRequirement(IReadOnlyList<OpenApiSemanticSecurityRequirementEntry> entries)
        {
            Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        }

        internal IReadOnlyList<OpenApiSemanticSecurityRequirementEntry> Entries { get; }
    }
}
