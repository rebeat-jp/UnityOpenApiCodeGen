using System;
using System.Collections.Generic;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>How a security scheme obtains and sends its credential.</summary>
    internal enum GeneratedSecurityKind
    {
        /// <summary>apiKey: a string credential sent in a header, the query or a cookie.</summary>
        ApiKey,
        /// <summary>http bearer, oauth2 and openIdConnect: an access token sent as a Bearer credential.</summary>
        BearerToken,
        /// <summary>http basic: user name and password.</summary>
        Basic,
        /// <summary>Any other http scheme: the credentials are sent as <c>Authorization: Scheme credentials</c>.</summary>
        HttpCredentials,
        /// <summary>mutualTLS: nothing to send; the client certificate belongs to the HttpClientHandler.</summary>
        MutualTls
    }

    /// <summary>A security scheme of the client together with its generated public member names.</summary>
    internal sealed class GeneratedSecuritySchemeModel
    {
        internal GeneratedSecuritySchemeModel(
            OpenApiSemanticSecurityScheme scheme,
            GeneratedSecurityKind kind,
            string valuePropertyName,
            string secondaryPropertyName,
            string providerPropertyName)
        {
            Scheme = scheme ?? throw new ArgumentNullException(nameof(scheme));
            Kind = kind;
            ValuePropertyName = valuePropertyName;
            SecondaryPropertyName = secondaryPropertyName;
            ProviderPropertyName = providerPropertyName;
        }

        internal OpenApiSemanticSecurityScheme Scheme { get; }

        internal string Key => Scheme.Key;

        internal GeneratedSecurityKind Kind { get; }

        /// <summary>
        /// ApiKey: the key. BearerToken: the access token. Basic: the user name.
        /// HttpCredentials: the credentials. Empty for MutualTls.
        /// </summary>
        internal string ValuePropertyName { get; }

        /// <summary>Basic: the password property. Empty otherwise.</summary>
        internal string SecondaryPropertyName { get; }

        /// <summary>BearerToken: the async token provider property. Empty otherwise.</summary>
        internal string ProviderPropertyName { get; }
    }
}
