using System;
using System.Collections.Generic;
using System.Linq;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>P5 (G28): the public credential members of the generated client.</summary>
    internal sealed partial class OpenApiGenerationModelBuilder
    {
        // Member names already taken on the generated client (set up by BuildOperations).
        private HashSet<string> _clientMemberNames = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Allocates the credential properties after the operation methods, so adding security to a
        /// document never renames an operation.
        /// </summary>
        private IReadOnlyList<GeneratedSecuritySchemeModel> BuildSecuritySchemes()
        {
            var result = new List<GeneratedSecuritySchemeModel>();
            foreach (OpenApiSemanticSecurityScheme scheme in _document.SecuritySchemes)
            {
                string baseName = ToIdentifier(scheme.Key, pascalCase: true).TrimStart('@');
                switch (scheme.Type)
                {
                    case OpenApiSemanticSecuritySchemeType.ApiKey:
                        result.Add(new GeneratedSecuritySchemeModel(
                            scheme,
                            GeneratedSecurityKind.ApiKey,
                            AllocateUniqueName(baseName + "ApiKey", _clientMemberNames),
                            string.Empty,
                            string.Empty));
                        break;
                    case OpenApiSemanticSecuritySchemeType.Http:
                        if (string.Equals(scheme.HttpScheme, "bearer", StringComparison.OrdinalIgnoreCase))
                        {
                            result.Add(CreateBearer(scheme, baseName));
                        }
                        else if (string.Equals(scheme.HttpScheme, "basic", StringComparison.OrdinalIgnoreCase))
                        {
                            string userName = AllocateUniqueName(baseName + "Username", _clientMemberNames);
                            string password = AllocateUniqueName(baseName + "Password", _clientMemberNames);
                            result.Add(new GeneratedSecuritySchemeModel(
                                scheme, GeneratedSecurityKind.Basic, userName, password, string.Empty));
                        }
                        else
                        {
                            result.Add(new GeneratedSecuritySchemeModel(
                                scheme,
                                GeneratedSecurityKind.HttpCredentials,
                                AllocateUniqueName(baseName + "Credentials", _clientMemberNames),
                                string.Empty,
                                string.Empty));
                        }

                        break;
                    case OpenApiSemanticSecuritySchemeType.OAuth2:
                    case OpenApiSemanticSecuritySchemeType.OpenIdConnect:
                        result.Add(CreateBearer(scheme, baseName));
                        break;
                    default:
                        result.Add(new GeneratedSecuritySchemeModel(
                            scheme, GeneratedSecurityKind.MutualTls, string.Empty, string.Empty, string.Empty));
                        break;
                }
            }

            return result;
        }

        private GeneratedSecuritySchemeModel CreateBearer(OpenApiSemanticSecurityScheme scheme, string baseName)
        {
            string token = AllocateUniqueName(baseName + "AccessToken", _clientMemberNames);
            string provider = AllocateUniqueName(token + "Provider", _clientMemberNames);
            return new GeneratedSecuritySchemeModel(
                scheme, GeneratedSecurityKind.BearerToken, token, string.Empty, provider);
        }
    }
}
