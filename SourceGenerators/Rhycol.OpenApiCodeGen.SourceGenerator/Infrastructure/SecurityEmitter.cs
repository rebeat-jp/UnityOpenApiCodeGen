using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// P5 (G28): emits the credential properties of the client and the per-request application of
    /// security requirements.
    ///
    /// A requirement set (the OR list of an operation) becomes one private resolver method. It tries
    /// the requirements in document order and returns the first one whose every scheme (AND) has a
    /// configured credential; when none can be satisfied the result is empty and the request is sent
    /// without authentication. The resolver collects headers, query pairs and cookie pairs in a small
    /// private result object, which the operation method merges into the request it already builds
    /// (one Cookie header, one query string).
    ///
    /// Credentials are never written to logs or exception messages: the only messages generated here
    /// name the header or scheme, never a value.
    /// </summary>
    internal sealed class SecurityEmitter
    {
        /// <summary>The local variable of an operation method that holds the resolved credentials.</summary>
        internal const string LocalName = "oacgSecurity";

        private const string KeyValuePairType =
            "global::System.Collections.Generic.KeyValuePair<string, string>";

        private readonly OpenApiGenerationModel _model;
        private readonly string _headerValidatorName;
        private readonly string _prefix;
        private readonly Dictionary<string, GeneratedSecuritySchemeModel> _schemesByKey;
        private readonly Dictionary<string, int> _schemeIndexByKey;
        private readonly List<List<GeneratedSecuritySchemeModel[]>> _resolverSets =
            new List<List<GeneratedSecuritySchemeModel[]>>();
        private readonly Dictionary<string, int> _resolverBySignature =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<GeneratedOperationModel, int> _resolverByOperation =
            new Dictionary<GeneratedOperationModel, int>();
        private readonly HashSet<string> _usedSchemeKeys = new HashSet<string>(StringComparer.Ordinal);

        internal SecurityEmitter(OpenApiGenerationModel model, string headerValidatorName)
        {
            _model = model;
            _headerValidatorName = headerValidatorName;
            _schemesByKey = new Dictionary<string, GeneratedSecuritySchemeModel>(StringComparer.Ordinal);
            _schemeIndexByKey = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int index = 0; index < model.SecuritySchemes.Count; index++)
            {
                _schemesByKey[model.SecuritySchemes[index].Key] = model.SecuritySchemes[index];
                _schemeIndexByKey[model.SecuritySchemes[index].Key] = index;
            }

            string prefix = HttpClientParameterSerializationEmitter.CreatePrefix(model, "__OacgSecurity_");
            while (model.SecuritySchemes.Any(scheme =>
                       scheme.ValuePropertyName.StartsWith(prefix, StringComparison.Ordinal) ||
                       scheme.SecondaryPropertyName.StartsWith(prefix, StringComparison.Ordinal) ||
                       scheme.ProviderPropertyName.StartsWith(prefix, StringComparison.Ordinal)))
            {
                prefix += "_";
            }

            _prefix = prefix;
            PlanOperations();
        }

        private string ResultType => _prefix + "Result";

        /// <summary>True when the document declares any security scheme (the client then has credential members).</summary>
        internal bool HasSchemes => _model.SecuritySchemes.Count > 0;

        // ---- planning --------------------------------------------------------------------------------

        private void PlanOperations()
        {
            foreach (GeneratedOperationModel operation in _model.Operations)
            {
                var requirements = new List<GeneratedSecuritySchemeModel[]>();
                foreach (OpenApiSemanticSecurityRequirement requirement in operation.SecurityRequirements)
                {
                    // mutualTLS sends nothing (the certificate is a transport setting), so it neither
                    // blocks nor satisfies a requirement; a requirement without other schemes is the
                    // anonymous one.
                    GeneratedSecuritySchemeModel[] schemes = requirement.Entries
                        .Select(entry => _schemesByKey[entry.SchemeKey])
                        .Where(static scheme => scheme.Kind != GeneratedSecurityKind.MutualTls)
                        .OrderBy(static scheme => scheme.Kind == GeneratedSecurityKind.BearerToken ? 1 : 0)
                        .ToArray();
                    if (schemes.Length > 0)
                    {
                        requirements.Add(schemes);
                    }
                }

                if (requirements.Count == 0)
                {
                    continue;
                }

                string signature = string.Join(
                    "|",
                    requirements.Select(static value => string.Join("&", value.Select(static scheme => scheme.Key))));
                if (!_resolverBySignature.TryGetValue(signature, out int index))
                {
                    index = _resolverSets.Count;
                    _resolverBySignature.Add(signature, index);
                    _resolverSets.Add(requirements);
                    foreach (GeneratedSecuritySchemeModel scheme in requirements.SelectMany(static value => value))
                    {
                        _usedSchemeKeys.Add(scheme.Key);
                    }
                }

                _resolverByOperation[operation] = index;
            }
        }

        private IEnumerable<GeneratedSecuritySchemeModel> SchemesOf(GeneratedOperationModel operation)
        {
            return _resolverByOperation.TryGetValue(operation, out int index)
                ? _resolverSets[index].SelectMany(static value => value)
                : Enumerable.Empty<GeneratedSecuritySchemeModel>();
        }

        internal bool AppliesTo(GeneratedOperationModel operation)
        {
            return _resolverByOperation.ContainsKey(operation);
        }

        internal bool UsesQuery(GeneratedOperationModel operation)
        {
            return SchemesOf(operation).Any(static scheme => scheme.Kind == GeneratedSecurityKind.ApiKey &&
                                                           scheme.Scheme.ApiKeyLocation == "query");
        }

        internal bool UsesCookie(GeneratedOperationModel operation)
        {
            return SchemesOf(operation).Any(static scheme => scheme.Kind == GeneratedSecurityKind.ApiKey &&
                                                           scheme.Scheme.ApiKeyLocation == "cookie");
        }

        internal bool UsesHeaders(GeneratedOperationModel operation)
        {
            return SchemesOf(operation).Any(static scheme =>
                scheme.Kind != GeneratedSecurityKind.MutualTls &&
                !(scheme.Kind == GeneratedSecurityKind.ApiKey && scheme.Scheme.ApiKeyLocation != "header"));
        }

        // ---- operation statements --------------------------------------------------------------------

        internal void AppendResolve(StringBuilder source, GeneratedOperationModel operation)
        {
            if (!_resolverByOperation.TryGetValue(operation, out int index))
            {
                return;
            }

            source.Append("            var ").Append(LocalName).Append(" = await ")
                .Append(_prefix).Append("Resolve").Append(index)
                .AppendLine("(cancellationToken).ConfigureAwait(false);");
        }

        internal void AppendQuery(StringBuilder source, GeneratedOperationModel operation)
        {
            if (!UsesQuery(operation))
            {
                return;
            }

            source.Append("            if (").Append(LocalName).AppendLine(".Query.Count > 0)");
            source.AppendLine("            {");
            source.Append("                relativePath += (relativePath.IndexOf('?') >= 0 ? \"&\" : \"?\") + string.Join(\"&\", ")
                .Append(LocalName).AppendLine(".Query);");
            source.AppendLine("            }");
        }

        internal void AppendHeaders(StringBuilder source, GeneratedOperationModel operation)
        {
            if (!UsesHeaders(operation))
            {
                return;
            }

            source.Append("                ").Append(LocalName).AppendLine(".ApplyHeaders(request);");
        }

        // ---- members of the client -------------------------------------------------------------------

        internal void AppendProperties(StringBuilder source)
        {
            foreach (GeneratedSecuritySchemeModel scheme in _model.SecuritySchemes)
            {
                switch (scheme.Kind)
                {
                    case GeneratedSecurityKind.ApiKey:
                        AppendPropertyDoc(source, scheme, DescribeApiKey(scheme.Scheme), "The API key; null or empty means not configured.");
                        AppendProperty(source, "string?", scheme.ValuePropertyName);
                        break;
                    case GeneratedSecurityKind.BearerToken:
                        AppendPropertyDoc(source, scheme, DescribeBearer(scheme.Scheme), "The access token sent as a Bearer credential; null or empty means not configured. The token is obtained elsewhere; the client never fetches or refreshes it.");
                        AppendProperty(source, "string?", scheme.ValuePropertyName);
                        AppendPropertyDoc(
                            source,
                            scheme,
                            "Asynchronous access token source for the '" + scheme.Key + "' security scheme.",
                            "Called for every request that uses this scheme, with the request's cancellation token. When it is set it takes precedence over " +
                            scheme.ValuePropertyName + "; a null or empty result falls back to that property.");
                        AppendProperty(
                            source,
                            "global::System.Func<global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task<string?>>?",
                            scheme.ProviderPropertyName);
                        break;
                    case GeneratedSecurityKind.Basic:
                        AppendPropertyDoc(source, scheme, "HTTP Basic user name for the '" + scheme.Key + "' security scheme.", "The scheme is used only when the user name is not null; it must not contain ':'. User name and password are joined with ':' and encoded as UTF-8 and Base64.");
                        AppendProperty(source, "string?", scheme.ValuePropertyName);
                        AppendPropertyDoc(source, scheme, "HTTP Basic password for the '" + scheme.Key + "' security scheme.", "A null password is sent as an empty password.");
                        AppendProperty(source, "string?", scheme.SecondaryPropertyName);
                        break;
                    case GeneratedSecurityKind.HttpCredentials:
                        AppendPropertyDoc(
                            source,
                            scheme,
                            "Credentials for the '" + scheme.Key + "' security scheme (HTTP authentication scheme '" + scheme.Scheme.HttpScheme + "').",
                            "Sent as 'Authorization: " + scheme.Scheme.HttpScheme + " <credentials>'; null or empty means not configured.");
                        AppendProperty(source, "string?", scheme.ValuePropertyName);
                        break;
                    default:
                        break;
                }
            }
        }

        private static void AppendProperty(StringBuilder source, string type, string name)
        {
            source.Append("        public ").Append(type).Append(' ').Append(name).AppendLine(" { get; set; }");
            source.AppendLine();
        }

        private void AppendPropertyDoc(
            StringBuilder source,
            GeneratedSecuritySchemeModel scheme,
            string summary,
            string detail)
        {
            source.AppendLine("        /// <summary>");
            AppendDocLine(source, summary);
            foreach (string line in SplitLines(scheme.Scheme.Description))
            {
                AppendDocLine(source, line);
            }

            if (scheme.Kind == GeneratedSecurityKind.BearerToken)
            {
                foreach (string line in DescribeBearerDetails(scheme.Scheme))
                {
                    AppendDocLine(source, line);
                }
            }

            source.AppendLine("        /// </summary>");
            source.AppendLine("        /// <remarks>");
            AppendDocLine(source, detail);
            AppendDocLine(source, "Not synchronized: set it before sending requests; changing it while requests are in flight is not thread safe. It is read once per request.");
            AppendDocLine(source, "The credential is never written to logs or exception messages by the generated code.");
            source.AppendLine("        /// </remarks>");
        }

        private static void AppendDocLine(StringBuilder source, string line)
        {
            source.Append("        /// ").AppendLine(GeneratedDocumentationEmitter.Escape(line));
        }

        private static IEnumerable<string> SplitLines(string text)
        {
            return (text ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n')
                .Select(static line => line.Trim())
                .Where(static line => line.Length > 0);
        }

        private static string DescribeApiKey(OpenApiSemanticSecurityScheme scheme)
        {
            string where = scheme.ApiKeyLocation == "query"
                ? "query parameter"
                : scheme.ApiKeyLocation == "cookie" ? "cookie" : "header";
            return "API key for the '" + scheme.Key + "' security scheme (sent in the " + where + " '" +
                   scheme.ApiKeyName + "').";
        }

        private static string DescribeBearer(OpenApiSemanticSecurityScheme scheme)
        {
            switch (scheme.Type)
            {
                case OpenApiSemanticSecuritySchemeType.OAuth2:
                    return "OAuth2 access token for the '" + scheme.Key + "' security scheme.";
                case OpenApiSemanticSecuritySchemeType.OpenIdConnect:
                    return "OpenID Connect access token for the '" + scheme.Key + "' security scheme.";
                default:
                    return "Bearer token for the '" + scheme.Key + "' security scheme.";
            }
        }

        private static IEnumerable<string> DescribeBearerDetails(OpenApiSemanticSecurityScheme scheme)
        {
            if (scheme.BearerFormat.Length > 0)
            {
                yield return "Bearer format: " + scheme.BearerFormat + ".";
            }

            if (scheme.OpenIdConnectUrl.Length > 0)
            {
                yield return "OpenID Connect discovery URL: " + scheme.OpenIdConnectUrl + " (not contacted by the client).";
            }

            foreach (OpenApiSemanticOAuthFlow flow in scheme.Flows)
            {
                string urls = flow.AuthorizationUrl.Length > 0 ? " authorizationUrl " + flow.AuthorizationUrl : string.Empty;
                urls += flow.TokenUrl.Length > 0 ? " tokenUrl " + flow.TokenUrl : string.Empty;
                yield return "OAuth2 flow " + flow.Name + ":" + urls + " (the client does not run flows).";
                foreach (KeyValuePair<string, string> scope in flow.Scopes)
                {
                    yield return "Scope " + scope.Key + ": " + scope.Value;
                }
            }
        }

        // ---- private helpers -------------------------------------------------------------------------

        internal void AppendHelpers(StringBuilder source)
        {
            if (_resolverSets.Count == 0)
            {
                return;
            }

            source.AppendLine();
            source.Append("        private sealed class ").Append(ResultType).AppendLine();
            source.AppendLine("        {");
            source.Append("            internal readonly global::System.Collections.Generic.List<").Append(KeyValuePairType)
                .AppendLine("> Headers = new global::System.Collections.Generic.List<" + KeyValuePairType + ">();");
            source.AppendLine("            internal readonly global::System.Collections.Generic.List<string> Query = new global::System.Collections.Generic.List<string>();");
            source.AppendLine("            internal readonly global::System.Collections.Generic.List<string> Cookies = new global::System.Collections.Generic.List<string>();");
            source.AppendLine();
            source.AppendLine("            internal void ApplyHeaders(global::System.Net.Http.HttpRequestMessage request)");
            source.AppendLine("            {");
            source.AppendLine("                foreach (var header in Headers)");
            source.AppendLine("                {");
            source.AppendLine("                    request.Headers.Remove(header.Key);");
            source.Append("                    if (!request.Headers.TryAddWithoutValidation(header.Key, ")
                .Append(_headerValidatorName).AppendLine("(header.Value, header.Key)))");
            source.AppendLine("                    {");
            source.AppendLine("                        throw new global::System.InvalidOperationException(\"Unable to add the request header '\" + header.Key + \"'.\");");
            source.AppendLine("                    }");
            source.AppendLine("                }");
            source.AppendLine("            }");
            source.AppendLine("        }");

            foreach (GeneratedSecuritySchemeModel scheme in _model.SecuritySchemes)
            {
                if (_usedSchemeKeys.Contains(scheme.Key))
                {
                    source.AppendLine();
                    AppendSchemeMethod(source, scheme);
                }
            }

            for (int index = 0; index < _resolverSets.Count; index++)
            {
                source.AppendLine();
                AppendResolver(source, index, _resolverSets[index]);
            }
        }

        private string SchemeMethodName(GeneratedSecuritySchemeModel scheme)
        {
            return _prefix + "Apply" + _schemeIndexByKey[scheme.Key];
        }

        private static bool IsAsync(GeneratedSecuritySchemeModel scheme)
        {
            return scheme.Kind == GeneratedSecurityKind.BearerToken;
        }

        private void AppendSchemeMethod(StringBuilder source, GeneratedSecuritySchemeModel scheme)
        {
            string name = SchemeMethodName(scheme);
            if (IsAsync(scheme))
            {
                source.Append("        private async global::System.Threading.Tasks.Task<bool> ").Append(name)
                    .Append('(').Append(ResultType).AppendLine(" oacgResult, global::System.Threading.CancellationToken cancellationToken)");
            }
            else
            {
                source.Append("        private bool ").Append(name).Append('(').Append(ResultType).AppendLine(" oacgResult)");
            }

            source.AppendLine("        {");
            switch (scheme.Kind)
            {
                case GeneratedSecurityKind.ApiKey:
                    source.Append("            string? oacgValue = ").Append(scheme.ValuePropertyName).AppendLine(";");
                    source.AppendLine("            if (string.IsNullOrEmpty(oacgValue))");
                    source.AppendLine("            {");
                    source.AppendLine("                return false;");
                    source.AppendLine("            }");
                    source.AppendLine();
                    string wire = GeneratedSourceEmitter.StringLiteral(scheme.Scheme.ApiKeyName);
                    if (scheme.Scheme.ApiKeyLocation == "query")
                    {
                        source.Append("            oacgResult.Query.Add(global::System.Uri.EscapeDataString(").Append(wire)
                            .AppendLine(") + \"=\" + global::System.Uri.EscapeDataString(oacgValue!));");
                    }
                    else if (scheme.Scheme.ApiKeyLocation == "cookie")
                    {
                        source.Append("            oacgResult.Cookies.Add(").Append(wire)
                            .AppendLine(" + \"=\" + global::System.Uri.EscapeDataString(oacgValue!));");
                    }
                    else
                    {
                        source.Append("            oacgResult.Headers.Add(new ").Append(KeyValuePairType).Append('(')
                            .Append(wire).AppendLine(", oacgValue!));");
                    }

                    break;
                case GeneratedSecurityKind.BearerToken:
                    source.AppendLine("            string? oacgValue = null;");
                    source.Append("            var oacgProvider = ").Append(scheme.ProviderPropertyName).AppendLine(";");
                    source.AppendLine("            if (oacgProvider != null)");
                    source.AppendLine("            {");
                    source.AppendLine("                oacgValue = await oacgProvider(cancellationToken).ConfigureAwait(false);");
                    source.AppendLine("            }");
                    source.AppendLine();
                    source.AppendLine("            if (string.IsNullOrEmpty(oacgValue))");
                    source.AppendLine("            {");
                    source.Append("                oacgValue = ").Append(scheme.ValuePropertyName).AppendLine(";");
                    source.AppendLine("            }");
                    source.AppendLine();
                    source.AppendLine("            if (string.IsNullOrEmpty(oacgValue))");
                    source.AppendLine("            {");
                    source.AppendLine("                return false;");
                    source.AppendLine("            }");
                    source.AppendLine();
                    source.Append("            oacgResult.Headers.Add(new ").Append(KeyValuePairType)
                        .AppendLine("(\"Authorization\", \"Bearer \" + oacgValue));");
                    break;
                case GeneratedSecurityKind.Basic:
                    source.Append("            string? oacgUser = ").Append(scheme.ValuePropertyName).AppendLine(";");
                    source.AppendLine("            if (oacgUser == null)");
                    source.AppendLine("            {");
                    source.AppendLine("                return false;");
                    source.AppendLine("            }");
                    source.AppendLine();
                    source.AppendLine("            if (oacgUser.IndexOf(':') >= 0)");
                    source.AppendLine("            {");
                    source.Append("                throw new global::System.InvalidOperationException(")
                        .Append(GeneratedSourceEmitter.StringLiteral(
                            "The HTTP Basic user name for the '" + scheme.Key + "' security scheme must not contain ':'."))
                        .AppendLine(");");
                    source.AppendLine("            }");
                    source.AppendLine();
                    source.Append("            string oacgPassword = ").Append(scheme.SecondaryPropertyName).AppendLine(" ?? string.Empty;");
                    source.Append("            oacgResult.Headers.Add(new ").Append(KeyValuePairType)
                        .AppendLine("(\"Authorization\", \"Basic \" + global::System.Convert.ToBase64String(global::System.Text.Encoding.UTF8.GetBytes(oacgUser + \":\" + oacgPassword))));");
                    break;
                default:
                    source.Append("            string? oacgValue = ").Append(scheme.ValuePropertyName).AppendLine(";");
                    source.AppendLine("            if (string.IsNullOrEmpty(oacgValue))");
                    source.AppendLine("            {");
                    source.AppendLine("                return false;");
                    source.AppendLine("            }");
                    source.AppendLine();
                    source.Append("            oacgResult.Headers.Add(new ").Append(KeyValuePairType).Append("(\"Authorization\", ")
                        .Append(GeneratedSourceEmitter.StringLiteral(scheme.Scheme.HttpScheme + " "))
                        .AppendLine(" + oacgValue));");
                    break;
            }

            source.AppendLine("            return true;");
            source.AppendLine("        }");
        }

        private void AppendResolver(
            StringBuilder source,
            int index,
            List<GeneratedSecuritySchemeModel[]> requirements)
        {
            bool isAsync = requirements.Any(static value => value.Any(IsAsync));
            string name = _prefix + "Resolve" + index;
            source.Append("        private ").Append(isAsync ? "async " : string.Empty)
                .Append("global::System.Threading.Tasks.Task<").Append(ResultType).Append("> ").Append(name)
                .AppendLine("(global::System.Threading.CancellationToken cancellationToken)");
            source.AppendLine("        {");
            string returnPrefix = isAsync ? "return " : "return global::System.Threading.Tasks.Task.FromResult(";
            string returnSuffix = isAsync ? ";" : ");";
            source.Append("            var oacgResult = new ").Append(ResultType).AppendLine("();");
            foreach (GeneratedSecuritySchemeModel[] requirement in requirements)
            {
                var conditions = new List<string>();
                foreach (GeneratedSecuritySchemeModel scheme in requirement)
                {
                    conditions.Add(IsAsync(scheme)
                        ? "await " + SchemeMethodName(scheme) + "(oacgResult, cancellationToken).ConfigureAwait(false)"
                        : SchemeMethodName(scheme) + "(oacgResult)");
                }

                source.Append("            if (").Append(string.Join(" && ", conditions)).AppendLine(")");
                source.AppendLine("            {");
                source.Append("                ").Append(returnPrefix).Append("oacgResult").Append(returnSuffix).AppendLine();
                source.AppendLine("            }");
                source.AppendLine();
                source.Append("            oacgResult = new ").Append(ResultType).AppendLine("();");
            }

            source.Append("            ").Append(returnPrefix).Append("oacgResult").Append(returnSuffix).AppendLine();
            source.AppendLine("        }");
        }
    }
}
