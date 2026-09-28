using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    internal sealed class HttpClientSourceEmitter
    {
        internal GeneratedFile Emit(OpenApiGenerationModel model)
        {
            if (model is null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            var source = new StringBuilder();
            source.AppendLine("#nullable enable");
            source.Append("namespace ").Append(model.GeneratedNamespace).AppendLine();
            source.AppendLine("{");
            source.Append("    public partial class ").Append(model.ApiName).AppendLine();
            source.AppendLine("    {");
            source.AppendLine("        private readonly global::System.Net.Http.HttpClient _httpClient;");
            source.AppendLine("        private readonly string _baseUrl;");
            source.AppendLine();
            AppendConstructors(source, model);
            var responseValidators = new ResponseValidatorPlan(model);
            var responseJsonContracts = new ResponseJsonContractPlan(model);

            foreach (GeneratedOperationModel operation in model.Operations)
            {
                source.AppendLine();
                AppendOperation(source, model, operation, responseValidators, responseJsonContracts);
            }

            source.AppendLine();
            AppendHelpers(source, responseValidators);
            responseValidators.AppendMethods(source);
            responseJsonContracts.AppendMethods(source);
            source.AppendLine("    }");
            source.AppendLine();
            AppendException(source, model.ApiName);
            source.AppendLine("}");
            return GeneratedSourceEmitter.Create(model.ApiName + ".g.cs", source.ToString());
        }

        private static void AppendConstructors(StringBuilder source, OpenApiGenerationModel model)
        {
            source.Append("        public ").Append(model.ApiName)
                .Append("(global::System.Net.Http.HttpClient httpClient) : this(httpClient, ")
                .Append(GeneratedSourceEmitter.StringLiteral(model.BaseUrl))
                .AppendLine(")");
            source.AppendLine("        {");
            source.AppendLine("        }");
            source.AppendLine();
            source.Append("        public ").Append(model.ApiName)
                .AppendLine("(global::System.Net.Http.HttpClient httpClient, string baseUrl)");
            source.AppendLine("        {");
            source.AppendLine("            _httpClient = httpClient ?? throw new global::System.ArgumentNullException(nameof(httpClient));");
            source.AppendLine("            _baseUrl = baseUrl ?? throw new global::System.ArgumentNullException(nameof(baseUrl));");
            source.AppendLine("        }");
        }

        private static void AppendOperation(
            StringBuilder source,
            OpenApiGenerationModel model,
            GeneratedOperationModel operation,
            ResponseValidatorPlan responseValidators,
            ResponseJsonContractPlan responseJsonContracts)
        {
            string summary = string.IsNullOrWhiteSpace(operation.Summary)
                ? operation.Name
                : operation.Summary;
            source.Append("        /// <summary>")
                .Append(EscapeXml(summary))
                .AppendLine("</summary>");
            foreach (GeneratedParameterModel parameter in operation.Parameters)
            {
                source.Append("        /// <param name=\"")
                    .Append(parameter.Name)
                    .Append("\">")
                    .Append(EscapeXml(parameter.WireName))
                    .AppendLine("</param>");
            }

            if (operation.RequestBody is not null)
            {
                source.Append("        /// <param name=\"")
                    .Append(operation.RequestBody.ParameterName)
                    .AppendLine("\">JSON request body.</param>");
            }

            source.AppendLine("        /// <param name=\"cancellationToken\">Cancellation token.</param>");
            if (operation.RequestBody?.SpecifiedParameterName is string specifiedParameterName)
            {
                source.Append("        /// <param name=\"").Append(specifiedParameterName)
                    .AppendLine("\">Send the JSON body even when its value is null.</param>");
            }
            string taskType = operation.ResponseType is null
                ? "global::System.Threading.Tasks.Task"
                : "global::System.Threading.Tasks.Task<" +
                  GeneratedSourceEmitter.TypeName(operation.ResponseType) + ">";
            source.Append("        public async ").Append(taskType).Append(' ').Append(operation.Name).Append('(');
            bool first = true;
            foreach (GeneratedParameterModel parameter in operation.Parameters)
            {
                AppendParameterSeparator(source, ref first);
                source.Append(GeneratedSourceEmitter.TypeName(parameter.Type))
                    .Append(' ')
                    .Append(parameter.Name);
            }

            if (operation.RequestBody is not null)
            {
                AppendParameterSeparator(source, ref first);
                source.Append(GeneratedSourceEmitter.TypeName(operation.RequestBody.Type))
                    .Append(' ')
                    .Append(operation.RequestBody.ParameterName);
            }

            AppendParameterSeparator(source, ref first);
            source.Append("global::System.Threading.CancellationToken cancellationToken = default");
            if (operation.RequestBody?.SpecifiedParameterName is string specifiedParameter)
            {
                source.Append(", bool ").Append(specifiedParameter).Append(" = false");
            }
            source.AppendLine(")");
            source.AppendLine("        {");
            AppendNumericParameterValidation(source, operation);
            AppendRequestBodyValidation(source, operation.RequestBody);
            source.Append("            string relativePath = ")
                .Append(GeneratedSourceEmitter.StringLiteral(operation.Path))
                .AppendLine(";");
            AppendPathParameters(source, operation);
            AppendQueryParameters(source, operation);
            source.AppendLine("            ValidatePathSegments(relativePath);");
            source.AppendLine("            var requestUri = CreateRequestUri(relativePath);");
            source.Append("            using (var request = new global::System.Net.Http.HttpRequestMessage(")
                .Append(GetHttpMethod(operation.HttpMethod))
                .AppendLine(", requestUri))");
            source.AppendLine("            {");
            AppendHeaderParameters(source, operation, responseValidators);
            AppendRequestBody(source, operation.RequestBody, responseValidators);
            source.AppendLine("                using (var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))");
            source.AppendLine("                {");
            source.AppendLine("                    string responseBody = response.Content == null");
            source.AppendLine("                        ? string.Empty");
            source.AppendLine("                        : await response.Content.ReadAsStringAsync().ConfigureAwait(false);");
            source.Append("                    if (!(")
                .Append(CreateSuccessExpression(operation.SuccessStatusCodes))
                .AppendLine("))");
            source.AppendLine("                    {");
            source.Append("                        throw new ").Append(model.ApiName)
                .AppendLine("Exception(response.StatusCode, responseBody);");
            source.AppendLine("                    }");

            AppendResponseContentTypeValidation(source, operation, responseValidators);

            if (operation.ResponseType is null)
            {
                source.AppendLine("                    return;");
            }
            else
            {
                source.AppendLine("                    if (global::System.String.IsNullOrWhiteSpace(responseBody))");
                source.AppendLine("                    {");
                source.AppendLine("                        throw new global::Newtonsoft.Json.JsonSerializationException(\"The successful response requires a JSON body.\");");
                source.AppendLine("                    }");
                if (!operation.ResponseType.Nullable)
                {
                    source.AppendLine("                    if (global::System.String.Equals(responseBody.Trim(), \"null\", global::System.StringComparison.Ordinal))");
                    source.AppendLine("                    {");
                    source.AppendLine("                        throw new global::Newtonsoft.Json.JsonSerializationException(\"The successful response requires a non-null JSON body.\");");
                    source.AppendLine("                    }");
                }
                source.AppendLine("                    var defaultResponseSettings = global::Newtonsoft.Json.JsonConvert.DefaultSettings?.Invoke();");
                source.AppendLine("                    var responseSerializer = global::Newtonsoft.Json.JsonSerializer.Create();");
                source.AppendLine("                    responseSerializer.DateParseHandling = global::Newtonsoft.Json.DateParseHandling.None;");
                source.AppendLine("                    responseSerializer.MaxDepth = defaultResponseSettings?.MaxDepth > 0 ? defaultResponseSettings.MaxDepth : 64;");
                source.Append("                    string validatedResponseBody = ").Append(responseJsonContracts.ParseMethodName)
                    .Append("(responseBody, ").Append(responseJsonContracts.MethodName(operation.ResponseType))
                    .Append(", ").Append(operation.ResponseType.Nullable ? "true" : "false")
                    .AppendLine(", responseSerializer.MaxDepth);");
                source.Append("                    var deserializedResponse = ").Append(responseJsonContracts.DeserializeMethodName).Append('<')
                    .Append(GeneratedSourceEmitter.TypeName(operation.ResponseType))
                    .AppendLine(">(validatedResponseBody, responseSerializer);");
                if (!operation.ResponseType.Nullable && !operation.ResponseType.IsValueType)
                {
                    source.AppendLine("                    if (deserializedResponse is null)");
                    source.AppendLine("                    {");
                    source.AppendLine("                        throw new global::Newtonsoft.Json.JsonSerializationException(\"The successful response requires a non-null JSON body.\");");
                    source.AppendLine("                    }");
                }
                if (responseValidators.TryGetMethodName(operation.ResponseType, out string? validatorName))
                {
                    source.Append("                    ").Append(validatorName)
                        .Append("(deserializedResponse, \"$\", \"response\", new global::System.Collections.Generic.Dictionary<object, global::System.Collections.Generic.HashSet<int>>(new ")
                        .Append(responseValidators.ComparerName).AppendLine("()));");
                }
                source.AppendLine("                    return deserializedResponse!;");
            }

            source.AppendLine("                }");
            source.AppendLine("            }");
            source.AppendLine("        }");
        }

        private static void AppendPathParameters(StringBuilder source, GeneratedOperationModel operation)
        {
            foreach (GeneratedParameterModel parameter in operation.Parameters.Where(
                         static value => value.LocationName == "path"))
            {
                source.Append("            relativePath = relativePath.Replace(")
                    .Append(GeneratedSourceEmitter.StringLiteral("{" + parameter.WireName + "}"))
                    .Append(", global::System.Uri.EscapeDataString(ConvertToString(")
                    .Append(GetValueExpression(parameter))
                    .AppendLine(")));");
            }
        }

        private static void AppendQueryParameters(StringBuilder source, GeneratedOperationModel operation)
        {
            GeneratedParameterModel[] queryParameters = operation.Parameters
                .Where(static value => value.LocationName == "query")
                .ToArray();
            if (queryParameters.Length == 0)
            {
                return;
            }

            source.AppendLine("            var queryParts = new global::System.Collections.Generic.List<string>();");
            foreach (GeneratedParameterModel parameter in queryParameters)
            {
                string statement = "queryParts.Add(global::System.Uri.EscapeDataString(" +
                                   GeneratedSourceEmitter.StringLiteral(parameter.WireName) +
                                   ") + \"=\" + global::System.Uri.EscapeDataString(ConvertToString(" +
                                   GetValueExpression(parameter) + ")));";
                AppendConditionalStatement(source, parameter, statement, 12);
            }

            source.AppendLine("            if (queryParts.Count > 0)");
            source.AppendLine("            {");
            source.AppendLine("                relativePath += (relativePath.IndexOf('?') >= 0 ? \"&\" : \"?\") + string.Join(\"&\", queryParts);");
            source.AppendLine("            }");
        }

        private static void AppendHeaderParameters(
            StringBuilder source,
            GeneratedOperationModel operation,
            ResponseValidatorPlan validators)
        {
            foreach (GeneratedParameterModel parameter in operation.Parameters.Where(
                         static value => value.LocationName == "header"))
            {
                string statement = "if (!request.Headers.TryAddWithoutValidation(" +
                                   GeneratedSourceEmitter.StringLiteral(parameter.WireName) +
                                   ", " + validators.HeaderValueValidatorName + "(ConvertToString(" +
                                   GetValueExpression(parameter) + "), " +
                                   GeneratedSourceEmitter.StringLiteral(parameter.WireName) + "))) " +
                                   "throw new global::System.InvalidOperationException(" +
                                   GeneratedSourceEmitter.StringLiteral(
                                       "Unable to add request header '" + parameter.WireName + "'.") + ");";
                AppendConditionalStatement(source, parameter, statement, 16);
            }
        }

        private static void AppendRequestBodyValidation(
            StringBuilder source,
            GeneratedRequestBodyModel? body)
        {
            if (body is null || !body.Required || body.Type.Nullable || body.Type.IsValueType)
            {
                return;
            }

            source.Append("            if (").Append(body.ParameterName).AppendLine(" is null)");
            source.AppendLine("            {");
            source.Append("                throw new global::System.ArgumentNullException(nameof(")
                .Append(body.ParameterName).AppendLine("));");
            source.AppendLine("            }");
        }

        private static void AppendNumericParameterValidation(
            StringBuilder source,
            GeneratedOperationModel operation)
        {
            foreach (GeneratedParameterModel parameter in operation.Parameters)
            {
                if (parameter.Type.Kind != GeneratedTypeKind.Single &&
                    parameter.Type.Kind != GeneratedTypeKind.Double)
                {
                    continue;
                }

                string numberType = parameter.Type.Kind == GeneratedTypeKind.Single ? "Single" : "Double";
                string value = GetValueExpression(parameter);
                source.Append("            if (");
                if (!parameter.Required)
                {
                    source.Append(parameter.Name).Append(".HasValue && ");
                }

                source.Append("(global::System.").Append(numberType).Append(".IsNaN(").Append(value)
                    .Append(") || global::System.").Append(numberType).Append(".IsInfinity(")
                    .Append(value).AppendLine(")))");
                source.AppendLine("            {");
                source.Append("                throw new global::System.ArgumentOutOfRangeException(nameof(")
                    .Append(parameter.Name)
                    .AppendLine("), \"A finite number is required.\");");
                source.AppendLine("            }");
            }
        }

        private static void AppendRequestBody(
            StringBuilder source,
            GeneratedRequestBodyModel? body,
            ResponseValidatorPlan validators)
        {
            if (body is null)
            {
                return;
            }

            if (!body.Required)
            {
                source.Append("                if (").Append(body.ParameterName).Append(" != null");
                if (body.SpecifiedParameterName is not null)
                {
                    source.Append(" || ").Append(body.SpecifiedParameterName);
                }
                source.AppendLine(")");
                source.AppendLine("                {");
            }

            string indent = body.Required ? "                " : "                    ";
            if (validators.TryGetMethodName(body.Type, out string? validatorName))
            {
                source.Append(indent).Append(validatorName).Append('(')
                    .Append(body.ParameterName)
                    .Append(", \"$\", \"request\", new global::System.Collections.Generic.Dictionary<object, global::System.Collections.Generic.HashSet<int>>(new ")
                    .Append(validators.ComparerName).AppendLine("()));");
            }
            source.Append(indent).Append("string requestJson = ")
                .Append(validators.RequestJsonSerializerName).Append('(')
                .Append(body.ParameterName)
                .AppendLine(");");
            source.Append(indent).Append("request.Content = CreateJsonContent(requestJson, ")
                .Append(GeneratedSourceEmitter.StringLiteral(body.MediaType))
                .AppendLine(");");
            if (!body.Required)
            {
                source.AppendLine("                }");
            }
        }

        private static void AppendConditionalStatement(
            StringBuilder source,
            GeneratedParameterModel parameter,
            string statement,
            int indentation)
        {
            string indent = new string(' ', indentation);
            string? condition = GetPresenceCondition(parameter);
            if (condition is null)
            {
                source.Append(indent).AppendLine(statement);
                return;
            }

            source.Append(indent).Append("if (").Append(condition).AppendLine(")");
            source.Append(indent).AppendLine("{");
            source.Append(indent).Append("    ").AppendLine(statement);
            source.Append(indent).AppendLine("}");
        }

        private static string? GetPresenceCondition(GeneratedParameterModel parameter)
        {
            if (parameter.Required)
            {
                return null;
            }

            return parameter.Type.IsValueType
                ? parameter.Name + ".HasValue"
                : parameter.Name + " != null";
        }

        private static string GetValueExpression(GeneratedParameterModel parameter)
        {
            return !parameter.Required && parameter.Type.IsValueType
                ? parameter.Name + ".Value"
                : parameter.Name;
        }

        private static string CreateSuccessExpression(System.Collections.Generic.IReadOnlyList<string> statusCodes)
        {
            return string.Join(
                " || ",
                statusCodes.Select(static code =>
                    code == "2XX"
                        ? "((int)response.StatusCode >= 200 && (int)response.StatusCode <= 299)"
                        : "((int)response.StatusCode == " + code + ")"));
        }

        private static void AppendResponseContentTypeValidation(
            StringBuilder source, GeneratedOperationModel operation, ResponseValidatorPlan validators)
        {
            source.Append("                    string[] ").Append(validators.MediaTypesLocalName).AppendLine(";");
            source.AppendLine("                    switch ((int)response.StatusCode)");
            source.AppendLine("                    {");
            foreach (var entry in operation.ResponseMediaTypes.OrderBy(static item => item.Key, StringComparer.Ordinal))
            {
                if (entry.Key == "2XX")
                {
                    continue;
                }

                source.Append("                        case ").Append(entry.Key).AppendLine(":");
                AppendDeclaredResponseMediaTypes(source, validators.MediaTypesLocalName, entry.Value);
            }

            source.AppendLine("                        default:");
            operation.ResponseMediaTypes.TryGetValue("2XX", out IReadOnlyList<string>? rangeMediaTypes);
            AppendDeclaredResponseMediaTypes(source, validators.MediaTypesLocalName, rangeMediaTypes ?? Array.Empty<string>());
            source.AppendLine("                    }");
            source.Append("                    ").Append(validators.MediaTypeValidatorName)
                .Append("(response, ").Append(validators.MediaTypesLocalName).AppendLine(");");
        }

        private static void AppendDeclaredResponseMediaTypes(
            StringBuilder source, string localName, IReadOnlyList<string> mediaTypes)
        {
            source.Append("                            ").Append(localName).Append(" = new string[] { ");
            source.Append(string.Join(", ", mediaTypes.Select(GeneratedSourceEmitter.StringLiteral)));
            source.AppendLine(" }; ");
            source.AppendLine("                            break;");
        }

        private static string GetHttpMethod(string method)
        {
            switch (method)
            {
                case "GET":
                    return "global::System.Net.Http.HttpMethod.Get";
                case "POST":
                    return "global::System.Net.Http.HttpMethod.Post";
                case "PUT":
                    return "global::System.Net.Http.HttpMethod.Put";
                case "DELETE":
                    return "global::System.Net.Http.HttpMethod.Delete";
                case "HEAD":
                    return "global::System.Net.Http.HttpMethod.Head";
                case "OPTIONS":
                    return "global::System.Net.Http.HttpMethod.Options";
                default:
                    return "new global::System.Net.Http.HttpMethod(" +
                           GeneratedSourceEmitter.StringLiteral(method) + ")";
            }
        }

        private static void AppendHelpers(StringBuilder source, ResponseValidatorPlan validators)
        {
            source.AppendLine("        private static readonly global::Newtonsoft.Json.JsonConverter DateOnlyJsonConverterInstance = new DateOnlyJsonConverter();");
            AppendRequestJsonSerializationHelper(source, validators.RequestJsonSerializerName);
            AppendResponseContentTypeHelper(source, validators.MediaTypeValidatorName);
            source.AppendLine();
            source.Append("        private static string ").Append(validators.HeaderValueValidatorName)
                .AppendLine("(string value, string name)");
            source.AppendLine("        {");
            source.AppendLine("            foreach (char character in value)");
            source.AppendLine("            {");
            source.AppendLine("                if ((character < (char)32 && character != (char)9) || character > (char)126)");
            source.AppendLine("                    throw new global::System.ArgumentException(\"Request header '\" + name + \"' contains a character outside the supported ASCII HTTP field value range.\", nameof(value));");
            source.AppendLine("            }");
            source.AppendLine("            return value;");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        private global::System.Uri CreateRequestUri(string relativePath)");
            source.AppendLine("        {");
            source.AppendLine("            SplitPathAndQuery(relativePath, out string operationPath, out string operationQuery);");
            source.AppendLine("            int baseFragment = _baseUrl.IndexOf('#');");
            source.AppendLine("            string baseUrl = baseFragment < 0 ? _baseUrl : _baseUrl.Substring(0, baseFragment);");
            source.AppendLine("            if (baseUrl.StartsWith(\"//\", global::System.StringComparison.Ordinal))");
            source.AppendLine("            {");
            source.AppendLine("                if (_httpClient.BaseAddress is null)");
            source.AppendLine("                {");
            source.AppendLine("                    throw new global::System.InvalidOperationException(\"A HttpClient.BaseAddress is required for a network-path server URL.\");");
            source.AppendLine("                }");
            source.AppendLine("                var networkBaseUri = new global::System.Uri(_httpClient.BaseAddress, baseUrl);");
            source.AppendLine("                return CombineAbsoluteUri(networkBaseUri, operationPath, operationQuery);");
            source.AppendLine("            }");
            source.AppendLine();
            source.AppendLine("            if (baseUrl.StartsWith(\"/\", global::System.StringComparison.Ordinal))");
            source.AppendLine("            {");
            source.AppendLine("                if (_httpClient.BaseAddress is null)");
            source.AppendLine("                {");
            source.AppendLine("                    throw new global::System.InvalidOperationException(\"A HttpClient.BaseAddress is required for a root-relative server URL.\");");
            source.AppendLine("                }");
            source.AppendLine("                var rootBaseUri = new global::System.Uri(_httpClient.BaseAddress, baseUrl);");
            source.AppendLine("                return CombineAbsoluteUri(rootBaseUri, operationPath, operationQuery);");
            source.AppendLine("            }");
            source.AppendLine();
            source.AppendLine("            if (global::System.Uri.TryCreate(baseUrl, global::System.UriKind.Absolute, out var absoluteBaseUri) &&");
            source.AppendLine("                (absoluteBaseUri.Scheme == global::System.Uri.UriSchemeHttp ||");
            source.AppendLine("                 absoluteBaseUri.Scheme == global::System.Uri.UriSchemeHttps))");
            source.AppendLine("            {");
            source.AppendLine("                return CombineAbsoluteUri(absoluteBaseUri, operationPath, operationQuery);");
            source.AppendLine("            }");
            source.AppendLine();
            source.AppendLine("            SplitPathAndQuery(baseUrl, out string basePath, out string baseQuery);");
            source.AppendLine("            string combinedPath = CombinePaths(basePath, operationPath);");
            source.AppendLine("            string combinedQuery = CombineQueries(baseQuery, operationQuery);");
            source.AppendLine("            if (_httpClient.BaseAddress != null)");
            source.AppendLine("            {");
            source.AppendLine("                var resolvedUri = new global::System.Uri(_httpClient.BaseAddress, combinedPath.TrimStart('/')); ");
            source.AppendLine("                string resolvedQuery = CombineQueries(_httpClient.BaseAddress.Query.TrimStart('?'), combinedQuery);");
            source.AppendLine("                var resolvedBuilder = new global::System.UriBuilder(resolvedUri) { Query = resolvedQuery };");
            source.AppendLine("                return resolvedBuilder.Uri;");
            source.AppendLine("            }");
            source.AppendLine();
            source.AppendLine("            string combined = AppendQuery(combinedPath, combinedQuery);");
            source.AppendLine("            if (global::System.Uri.TryCreate(combined, global::System.UriKind.Absolute, out var absoluteUri))");
            source.AppendLine("            {");
            source.AppendLine("                return absoluteUri;");
            source.AppendLine("            }");
            source.AppendLine();
            source.AppendLine("            return new global::System.Uri(combined, global::System.UriKind.Relative);");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        private static global::System.Uri CombineAbsoluteUri(global::System.Uri baseUri, string operationPath, string operationQuery)");
            source.AppendLine("        {");
            source.AppendLine("            var builder = new global::System.UriBuilder(baseUri)");
            source.AppendLine("            {");
            source.AppendLine("                Path = CombinePaths(baseUri.AbsolutePath, operationPath),");
            source.AppendLine("                Query = CombineQueries(baseUri.Query.TrimStart('?'), operationQuery)");
            source.AppendLine("            };");
            source.AppendLine("            return builder.Uri;");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        private static string CombinePaths(string basePath, string operationPath)");
            source.AppendLine("        {");
            source.AppendLine("            if (string.IsNullOrEmpty(basePath))");
            source.AppendLine("            {");
            source.AppendLine("                return operationPath;");
            source.AppendLine("            }");
            source.AppendLine();
            source.AppendLine("            if (string.IsNullOrEmpty(operationPath))");
            source.AppendLine("            {");
            source.AppendLine("                return basePath;");
            source.AppendLine("            }");
            source.AppendLine();
            source.AppendLine("            return basePath.TrimEnd('/') + \"/\" + operationPath.TrimStart('/');");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        private static string CombineQueries(string baseQuery, string operationQuery)");
            source.AppendLine("        {");
            source.AppendLine("            if (string.IsNullOrEmpty(baseQuery))");
            source.AppendLine("            {");
            source.AppendLine("                return operationQuery;");
            source.AppendLine("            }");
            source.AppendLine();
            source.AppendLine("            if (string.IsNullOrEmpty(operationQuery))");
            source.AppendLine("            {");
            source.AppendLine("                return baseQuery;");
            source.AppendLine("            }");
            source.AppendLine();
            source.AppendLine("            return baseQuery.TrimEnd('&') + \"&\" + operationQuery.TrimStart('&');");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        private static string AppendQuery(string path, string query)");
            source.AppendLine("        {");
            source.AppendLine("            return string.IsNullOrEmpty(query) ? path : path + \"?\" + query;");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        private static void SplitPathAndQuery(string value, out string path, out string query)");
            source.AppendLine("        {");
            source.AppendLine("            int separator = value.IndexOf('?');");
            source.AppendLine("            if (separator < 0)");
            source.AppendLine("            {");
            source.AppendLine("                path = value;");
            source.AppendLine("                query = string.Empty;");
            source.AppendLine("                return;");
            source.AppendLine("            }");
            source.AppendLine();
            source.AppendLine("            path = value.Substring(0, separator);");
            source.AppendLine("            query = value.Substring(separator + 1);");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        private static void ValidatePathSegments(string relativePath)");
            source.AppendLine("        {");
            source.AppendLine("            SplitPathAndQuery(relativePath, out string path, out _);");
            source.AppendLine("            string[] segments = path.Split('/');");
            source.AppendLine("            foreach (string segment in segments)");
            source.AppendLine("            {");
            source.AppendLine("                string decodedSegment = global::System.Uri.UnescapeDataString(segment);");
            source.AppendLine("                if (decodedSegment == \".\" || decodedSegment == \"..\")");
            source.AppendLine("                {");
            source.AppendLine("                    throw new global::System.ArgumentException(");
            source.AppendLine("                        \"Path parameter values must not produce '.' or '..' path segments.\",");
            source.AppendLine("                        nameof(relativePath));");
            source.AppendLine("                }");
            source.AppendLine("            }");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        private static global::System.Net.Http.HttpContent CreateJsonContent(string requestJson, string mediaType)");
            source.AppendLine("        {");
            source.AppendLine("            var contentType = global::System.Net.Http.Headers.MediaTypeHeaderValue.Parse(mediaType);");
            source.AppendLine("            global::System.Text.Encoding encoding = global::System.Text.Encoding.UTF8;");
            source.AppendLine("            if (string.IsNullOrWhiteSpace(contentType.CharSet))");
            source.AppendLine("            {");
            source.AppendLine("                contentType.CharSet = encoding.WebName;");
            source.AppendLine("            }");
            source.AppendLine("            else");
            source.AppendLine("            {");
            source.AppendLine("                string charset = contentType.CharSet.Trim().Trim('\"');");
            source.AppendLine("                encoding = global::System.Text.Encoding.GetEncoding(charset);");
            source.AppendLine("            }");
            source.AppendLine();
            source.AppendLine("            var content = new global::System.Net.Http.StringContent(");
            source.AppendLine("                requestJson,");
            source.AppendLine("                encoding,");
            source.AppendLine("                contentType.MediaType ?? \"application/json\");");
            source.AppendLine("            content.Headers.ContentType = contentType;");
            source.AppendLine("            return content;");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        private static string ConvertToString(object value)");
            source.AppendLine("        {");
            source.AppendLine("            if (value == null)");
            source.AppendLine("            {");
            source.AppendLine("                throw new global::System.ArgumentNullException(nameof(value));");
            source.AppendLine("            }");
            source.AppendLine();
            source.AppendLine("            if (value is string text)");
            source.AppendLine("            {");
            source.AppendLine("                return text;");
            source.AppendLine("            }");
            source.AppendLine();
            source.AppendLine("            if (value is bool boolean)");
            source.AppendLine("            {");
            source.AppendLine("                return boolean ? \"true\" : \"false\";");
            source.AppendLine("            }");
            source.AppendLine();
            source.AppendLine("            if (value is global::System.DateTime date)");
            source.AppendLine("            {");
            source.AppendLine("                return date.ToString(\"yyyy-MM-dd\", global::System.Globalization.CultureInfo.InvariantCulture);");
            source.AppendLine("            }");
            source.AppendLine();
            source.AppendLine("            if (value is global::System.DateTimeOffset dateTime)");
            source.AppendLine("            {");
            source.AppendLine("                return dateTime.ToString(\"O\", global::System.Globalization.CultureInfo.InvariantCulture);");
            source.AppendLine("            }");
            source.AppendLine();
            source.AppendLine("            if (value is global::System.Guid guid)");
            source.AppendLine("            {");
            source.AppendLine("                return guid.ToString(\"D\");");
            source.AppendLine("            }");
            source.AppendLine();
            source.AppendLine("            if (value is global::System.Enum)");
            source.AppendLine("            {");
            source.AppendLine("                string json = global::Newtonsoft.Json.JsonConvert.SerializeObject(value);");
            source.AppendLine("                return global::Newtonsoft.Json.JsonConvert.DeserializeObject<string>(json, new global::Newtonsoft.Json.JsonSerializerSettings { DateParseHandling = global::Newtonsoft.Json.DateParseHandling.None }) ?? string.Empty;");
            source.AppendLine("            }");
            source.AppendLine();
            source.AppendLine("            return value is global::System.IFormattable formattable");
            source.AppendLine("                ? formattable.ToString(null, global::System.Globalization.CultureInfo.InvariantCulture)");
            source.AppendLine("                : value.ToString() ?? string.Empty;");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        private sealed class DateOnlyJsonConverter : global::Newtonsoft.Json.JsonConverter");
            source.AppendLine("        {");
            source.AppendLine("            public override bool CanRead => false;");
            source.AppendLine();
            source.AppendLine("            public override bool CanConvert(global::System.Type objectType)");
            source.AppendLine("            {");
            source.AppendLine("                return objectType == typeof(global::System.DateTime) ||");
            source.AppendLine("                       objectType == typeof(global::System.DateTime?);");
            source.AppendLine("            }");
            source.AppendLine();
            source.AppendLine("            public override void WriteJson(global::Newtonsoft.Json.JsonWriter writer, object? value, global::Newtonsoft.Json.JsonSerializer serializer)");
            source.AppendLine("            {");
            source.AppendLine("                if (value == null)");
            source.AppendLine("                {");
            source.AppendLine("                    writer.WriteNull();");
            source.AppendLine("                    return;");
            source.AppendLine("                }");
            source.AppendLine();
            source.AppendLine("                var date = (global::System.DateTime)value;");
            source.AppendLine("                writer.WriteValue(date.ToString(\"yyyy-MM-dd\", global::System.Globalization.CultureInfo.InvariantCulture));");
            source.AppendLine("            }");
            source.AppendLine();
            source.AppendLine("            public override object? ReadJson(global::Newtonsoft.Json.JsonReader reader, global::System.Type objectType, object? existingValue, global::Newtonsoft.Json.JsonSerializer serializer)");
            source.AppendLine("            {");
            source.AppendLine("                throw new global::System.NotSupportedException();");
            source.AppendLine("            }");
            source.AppendLine("        }");
        }

        private static void AppendResponseContentTypeHelper(StringBuilder source, string methodName)
        {
            string decodeMethodName = methodName + "DecodeParameter";
            string validHeadMethodName = methodName + "HasValidHead";
            string tokenMethodName = methodName + "IsTokenCharacter";
            source.AppendLine();
            source.Append("        private static void ").Append(methodName)
                .AppendLine("(global::System.Net.Http.HttpResponseMessage response, string[] declaredMediaTypes)");
            source.AppendLine("        {");
            source.AppendLine("            if (declaredMediaTypes.Length == 0) return;");
            source.AppendLine("            if (response.Content == null || !response.Content.Headers.TryGetValues(\"Content-Type\", out var headerValues)) return;");
            source.AppendLine("            string? header = null;");
            source.AppendLine("            foreach (string value in headerValues)");
            source.AppendLine("            {");
            source.AppendLine("                if (header != null) throw new global::Newtonsoft.Json.JsonSerializationException(\"The response contains multiple Content-Type values.\");");
            source.AppendLine("                header = value;");
            source.AppendLine("            }");
            source.Append("            if (string.IsNullOrWhiteSpace(header) || !")
                .Append(validHeadMethodName).AppendLine("(header) ||");
            source.AppendLine("                !global::System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(header, out var actual) ||");
            source.AppendLine("                actual.MediaType == null || actual.MediaType.IndexOf('*') >= 0)");
            source.AppendLine("                throw new global::Newtonsoft.Json.JsonSerializationException(\"The response Content-Type is malformed.\");");
            source.AppendLine("            var actualParameters = new global::System.Collections.Generic.Dictionary<string, string>(global::System.StringComparer.OrdinalIgnoreCase);");
            source.AppendLine("            foreach (var parameter in actual.Parameters)");
            source.AppendLine("            {");
            source.Append("                if (parameter.Value == null || actualParameters.ContainsKey(parameter.Name) || !")
                .Append(decodeMethodName).AppendLine("(parameter.Value, out string decodedValue))");
            source.AppendLine("                    throw new global::Newtonsoft.Json.JsonSerializationException(\"The response Content-Type is malformed.\");");
            source.AppendLine("                actualParameters.Add(parameter.Name, decodedValue);");
            source.AppendLine("            }");
            source.AppendLine("            foreach (string declaredValue in declaredMediaTypes)");
            source.AppendLine("            {");
            source.AppendLine("                if (!global::System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(declaredValue, out var declared) || declared.MediaType == null) continue;");
            source.AppendLine("                int separator = declared.MediaType.IndexOf('/');");
            source.AppendLine("                int actualSeparator = actual.MediaType.IndexOf('/');");
            source.AppendLine("                if (separator <= 0 || actualSeparator <= 0 ||");
            source.AppendLine("                    !global::System.String.Equals(declared.MediaType.Substring(0, separator), actual.MediaType.Substring(0, actualSeparator), global::System.StringComparison.OrdinalIgnoreCase)) continue;");
            source.AppendLine("                string subtype = declared.MediaType.Substring(separator + 1);");
            source.AppendLine("                string actualSubtype = actual.MediaType.Substring(actualSeparator + 1);");
            source.AppendLine("                bool subtypeMatches = global::System.String.Equals(subtype, actualSubtype, global::System.StringComparison.OrdinalIgnoreCase) ||");
            source.AppendLine("                    (global::System.String.Equals(subtype, \"*+json\", global::System.StringComparison.OrdinalIgnoreCase) &&");
            source.AppendLine("                     actualSubtype.Length > 5 && actualSubtype.EndsWith(\"+json\", global::System.StringComparison.OrdinalIgnoreCase));");
            source.AppendLine("                if (!subtypeMatches) continue;");
            source.AppendLine("                bool parametersMatch = true;");
            source.AppendLine("                foreach (var parameter in declared.Parameters)");
            source.AppendLine("                {");
            source.Append("                    if (parameter.Value == null || !").Append(decodeMethodName)
                .AppendLine("(parameter.Value, out string expected)) { parametersMatch = false; break; }");
            source.AppendLine("                    if (!actualParameters.TryGetValue(parameter.Name, out string? actualValue) ||");
            source.AppendLine("                        !global::System.String.Equals(expected, actualValue, global::System.String.Equals(parameter.Name, \"charset\", global::System.StringComparison.OrdinalIgnoreCase)");
            source.AppendLine("                            ? global::System.StringComparison.OrdinalIgnoreCase : global::System.StringComparison.Ordinal))");
            source.AppendLine("                    { parametersMatch = false; break; }");
            source.AppendLine("                }");
            source.AppendLine("                if (parametersMatch) return;");
            source.AppendLine("            }");
            source.AppendLine("            throw new global::Newtonsoft.Json.JsonSerializationException(\"The response Content-Type '" + "\" + header + \"' does not match the declared media type.\");");
            source.AppendLine("        }");
            source.AppendLine();
            source.Append("        private static bool ").Append(decodeMethodName)
                .AppendLine("(string value, out string decoded)");
            source.AppendLine("        {");
            source.AppendLine("            decoded = string.Empty;");
            source.AppendLine("            if (value.Length > 0 && (value[0] == (char)34 || value[value.Length - 1] == (char)34))");
            source.AppendLine("            {");
            source.AppendLine("                if (value.Length < 2 || value[0] != (char)34 || value[value.Length - 1] != (char)34) return false;");
            source.AppendLine("                var builder = new global::System.Text.StringBuilder(value.Length - 2);");
            source.AppendLine("                for (int index = 1; index < value.Length - 1; index++)");
            source.AppendLine("                {");
            source.AppendLine("                    char character = value[index];");
            source.AppendLine("                    if (character == (char)92)");
            source.AppendLine("                    {");
            source.AppendLine("                        if (++index >= value.Length - 1) return false;");
            source.AppendLine("                        character = value[index];");
            source.AppendLine("                        if (!(character == (char)9 || (character >= (char)32 && character <= (char)126) ||");
            source.AppendLine("                              (character >= (char)128 && character <= (char)255))) return false;");
            source.AppendLine("                    }");
            source.AppendLine("                    else if (!(character == (char)9 || character == (char)32 || character == (char)33 ||");
            source.AppendLine("                               (character >= (char)35 && character <= (char)91) ||");
            source.AppendLine("                               (character >= (char)93 && character <= (char)126) ||");
            source.AppendLine("                               (character >= (char)128 && character <= (char)255))) return false;");
            source.AppendLine("                    builder.Append(character);");
            source.AppendLine("                }");
            source.AppendLine("                decoded = builder.ToString();");
            source.AppendLine("                return true;");
            source.AppendLine("            }");
            source.AppendLine("            if (value.IndexOf((char)34) >= 0 || value.IndexOf((char)92) >= 0) return false;");
            source.AppendLine("            decoded = value;");
            source.AppendLine("            return true;");
            source.AppendLine("        }");
            source.AppendLine();
            source.Append("        private static bool ").Append(validHeadMethodName)
                .AppendLine("(string value)");
            source.AppendLine("        {");
            source.AppendLine("            int index = 0;");
            source.AppendLine("            while (index < value.Length && (value[index] == (char)32 || value[index] == (char)9)) index++;");
            source.AppendLine("            int start = index;");
            source.Append("            while (index < value.Length && ").Append(tokenMethodName)
                .AppendLine("(value[index])) index++;");
            source.AppendLine("            if (index == start || index >= value.Length || value[index] != (char)47) return false;");
            source.AppendLine("            index++;");
            source.AppendLine("            start = index;");
            source.Append("            while (index < value.Length && ").Append(tokenMethodName)
                .AppendLine("(value[index])) index++;");
            source.AppendLine("            return index > start && (index == value.Length || value[index] == (char)59 ||");
            source.AppendLine("                value[index] == (char)32 || value[index] == (char)9);");
            source.AppendLine("        }");
            source.AppendLine();
            source.Append("        private static bool ").Append(tokenMethodName)
                .AppendLine("(char character)");
            source.AppendLine("        {");
            source.AppendLine("            return (character >= (char)48 && character <= (char)57) ||");
            source.AppendLine("                   (character >= (char)65 && character <= (char)90) ||");
            source.AppendLine("                   (character >= (char)97 && character <= (char)122) ||");
            source.AppendLine("                   character == (char)33 || character == (char)35 || character == (char)36 ||");
            source.AppendLine("                   character == (char)37 || character == (char)38 || character == (char)39 ||");
            source.AppendLine("                   character == (char)42 || character == (char)43 || character == (char)45 ||");
            source.AppendLine("                   character == (char)46 || character == (char)94 || character == (char)95 ||");
            source.AppendLine("                   character == (char)96 || character == (char)124 || character == (char)126;");
            source.AppendLine("        }");
        }

        private static void AppendRequestJsonSerializationHelper(StringBuilder source, string methodName)
        {
            source.AppendLine();
            source.Append("        private static string ").Append(methodName).AppendLine("(object? value)");
            source.AppendLine("        {");
            source.AppendLine("            var serializer = global::Newtonsoft.Json.JsonSerializer.Create();");
            source.AppendLine("            serializer.Converters.Add(DateOnlyJsonConverterInstance);");
            source.AppendLine("            using (var textWriter = new global::System.IO.StringWriter(global::System.Globalization.CultureInfo.InvariantCulture))");
            source.AppendLine("            using (var jsonWriter = new global::Newtonsoft.Json.JsonTextWriter(textWriter))");
            source.AppendLine("            {");
            source.AppendLine("                serializer.Serialize(jsonWriter, value);");
            source.AppendLine("                jsonWriter.Flush();");
            source.AppendLine("                return textWriter.ToString();");
            source.AppendLine("            }");
            source.AppendLine("        }");
        }

        private static void AppendException(StringBuilder source, string apiName)
        {
            source.Append("    public sealed class ").Append(apiName)
                .AppendLine("Exception : global::System.Exception");
            source.AppendLine("    {");
            source.Append("        public ").Append(apiName)
                .AppendLine("Exception(global::System.Net.HttpStatusCode statusCode, string responseBody)");
            source.AppendLine("            : base(\"The API returned HTTP status \" + ((int)statusCode).ToString(global::System.Globalization.CultureInfo.InvariantCulture) + \" (\" + statusCode + \").\")");
            source.AppendLine("        {");
            source.AppendLine("            StatusCode = statusCode;");
            source.AppendLine("            ResponseBody = responseBody ?? string.Empty;");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        public global::System.Net.HttpStatusCode StatusCode { get; }");
            source.AppendLine();
            source.AppendLine("        public string ResponseBody { get; }");
            source.AppendLine("    }");
        }

        private sealed class ResponseJsonContractPlan
        {
            private readonly IReadOnlyDictionary<string, GeneratedDtoModel> _dtos;
            private readonly List<GeneratedTypeModel> _types = new List<GeneratedTypeModel>();
            private readonly Dictionary<GeneratedTypeModel, int> _indices =
                new Dictionary<GeneratedTypeModel, int>();
            private readonly string _prefix;

            internal ResponseJsonContractPlan(OpenApiGenerationModel model)
            {
                _dtos = model.Dtos.ToDictionary(static dto => dto.Name, StringComparer.Ordinal);
                var occupied = new HashSet<string>(StringComparer.Ordinal) { model.ApiName };
                occupied.UnionWith(model.Dtos.Select(static dto => dto.Name));
                occupied.UnionWith(model.Enums.Select(static value => value.Name));
                foreach (GeneratedOperationModel operation in model.Operations)
                {
                    occupied.Add(operation.Name);
                    foreach (GeneratedParameterModel parameter in operation.Parameters)
                    {
                        occupied.Add(parameter.Name);
                    }

                    if (operation.RequestBody is GeneratedRequestBodyModel requestBody)
                    {
                        occupied.Add(requestBody.ParameterName);
                        if (requestBody.SpecifiedParameterName is string specifiedParameterName)
                        {
                            occupied.Add(specifiedParameterName);
                        }
                    }
                }

                string prefix = "__OacgJsonContract_";
                while (occupied.Any(name => name.StartsWith(prefix, StringComparison.Ordinal)))
                {
                    prefix += "_";
                }

                _prefix = prefix;
                foreach (GeneratedOperationModel operation in model.Operations)
                {
                    if (operation.ResponseType is GeneratedTypeModel responseType)
                    {
                        Register(responseType);
                    }
                }
            }

            internal string ParseMethodName => _prefix + "Parse";

            internal string DeserializeMethodName => _prefix + "Deserialize";

            internal string MethodName(GeneratedTypeModel type) =>
                _prefix + _indices[type.WithNullable(false)];

            private void Register(GeneratedTypeModel type)
            {
                type = type.WithNullable(false);
                if (_indices.ContainsKey(type))
                {
                    return;
                }

                _indices.Add(type, _types.Count);
                _types.Add(type);
                if (type.Kind == GeneratedTypeKind.Array)
                {
                    Register(type.ItemType!);
                }
                else if (type.Kind == GeneratedTypeKind.Named &&
                         _dtos.TryGetValue(type.Name, out GeneratedDtoModel? dto))
                {
                    foreach (GeneratedDtoPropertyModel property in dto.Properties)
                    {
                        Register(property.Type);
                    }
                }
            }

            internal void AppendMethods(StringBuilder source)
            {
                if (_types.Count == 0)
                {
                    return;
                }

                source.AppendLine();
                source.Append("        private static string ").Append(ParseMethodName)
                    .AppendLine("(string json, global::System.Action<global::Newtonsoft.Json.Linq.JToken, string, bool, string, global::System.Collections.Generic.List<int>, global::System.Collections.Generic.List<global::System.Tuple<int, int, string>>> validate, bool nullable, int? maxDepth)");
                source.AppendLine("        {");
                source.Append("            ").Append(_prefix).AppendLine("ValidateSyntax(json, maxDepth);");
                source.AppendLine("            var lineStarts = new global::System.Collections.Generic.List<int> { 0 };");
                source.AppendLine("            for (int index = 0; index < json.Length; index++)");
                source.AppendLine("            {");
                source.AppendLine("                if (json[index] != '\\r' && json[index] != '\\n') continue;");
                source.AppendLine("                if (json[index] == '\\r' && index + 1 < json.Length && json[index + 1] == '\\n') index++;");
                source.AppendLine("                lineStarts.Add(index + 1);");
                source.AppendLine("            }");
                source.AppendLine("            var replacements = new global::System.Collections.Generic.List<global::System.Tuple<int, int, string>>();");
                source.AppendLine("            using (var textReader = new global::System.IO.StringReader(json))");
                source.AppendLine("            using (var reader = new global::Newtonsoft.Json.JsonTextReader(textReader) { DateParseHandling = global::Newtonsoft.Json.DateParseHandling.None, MaxDepth = maxDepth })");
                source.AppendLine("            {");
                source.AppendLine("                var token = global::Newtonsoft.Json.Linq.JToken.ReadFrom(reader, new global::Newtonsoft.Json.Linq.JsonLoadSettings");
                source.AppendLine("                {");
                source.AppendLine("                    DuplicatePropertyNameHandling = global::Newtonsoft.Json.Linq.DuplicatePropertyNameHandling.Error");
                source.AppendLine("                });");
                source.AppendLine("                if (reader.Read())");
                source.AppendLine("                {");
                source.AppendLine("                    throw new global::Newtonsoft.Json.JsonSerializationException(\"The successful response has trailing JSON content.\");");
                source.AppendLine("                }");
                source.AppendLine("                validate(token, \"$\", nullable, json, lineStarts, replacements);");
                source.AppendLine("            }");
                source.AppendLine("            if (replacements.Count == 0) return json;");
                source.AppendLine("            replacements.Sort((left, right) => left.Item1.CompareTo(right.Item1));");
                source.AppendLine("            var normalized = new global::System.Text.StringBuilder(json.Length);");
                source.AppendLine("            int copiedTo = 0;");
                source.AppendLine("            foreach (var replacement in replacements)");
                source.AppendLine("            {");
                source.AppendLine("                normalized.Append(json, copiedTo, replacement.Item1 - copiedTo);");
                source.AppendLine("                normalized.Append(replacement.Item3);");
                source.AppendLine("                copiedTo = replacement.Item2;");
                source.AppendLine("            }");
                source.AppendLine("            normalized.Append(json, copiedTo, json.Length - copiedTo);");
                source.AppendLine("            return normalized.ToString();");
                source.AppendLine("        }");
                source.AppendLine();
                source.Append("        private static T? ").Append(DeserializeMethodName)
                    .AppendLine("<T>(string json, global::Newtonsoft.Json.JsonSerializer serializer)");
                source.AppendLine("        {");
                source.AppendLine("            using (var textReader = new global::System.IO.StringReader(json))");
                source.AppendLine("            using (var reader = new global::Newtonsoft.Json.JsonTextReader(textReader) { DateParseHandling = global::Newtonsoft.Json.DateParseHandling.None, MaxDepth = serializer.MaxDepth })");
                source.AppendLine("            {");
                source.AppendLine("                return serializer.Deserialize<T>(reader);");
                source.AppendLine("            }");
                source.AppendLine("        }");
                foreach (GeneratedTypeModel type in _types)
                {
                    AppendMethod(source, type);
                }

                if (_types.Any(static type =>
                        type.Kind == GeneratedTypeKind.Int32 || type.Kind == GeneratedTypeKind.Int64))
                {
                    AppendIntegerLexemeHelper(source);
                }

                if (_types.Any(static type => type.Kind == GeneratedTypeKind.DateTime ||
                    type.Kind == GeneratedTypeKind.DateTimeOffset || type.Kind == GeneratedTypeKind.Guid))
                {
                    AppendStringFormatHelpers(source);
                }

                AppendSyntaxHelpers(source);
            }

            private void AppendSyntaxHelpers(StringBuilder source)
            {
                source.AppendLine(@"
        private static void __PREFIX__ValidateSyntax(string json, int? maxDepth)
        {
            int index = 0;
            __PREFIX__SkipWhitespace(json, ref index);
            __PREFIX__ReadValue(json, ref index, 0, maxDepth);
            __PREFIX__SkipWhitespace(json, ref index);
            if (index != json.Length) __PREFIX__InvalidSyntax();
        }

        private static void __PREFIX__ReadValue(string json, ref int index, int depth, int? maxDepth)
        {
            if (index >= json.Length || (maxDepth.HasValue && depth > maxDepth.Value)) __PREFIX__InvalidSyntax();
            char current = json[index];
            if (current == '{')
            {
                __PREFIX__ReadObject(json, ref index, depth + 1, maxDepth);
                return;
            }
            if (current == '[')
            {
                __PREFIX__ReadArray(json, ref index, depth + 1, maxDepth);
                return;
            }
            if (current == '""')
            {
                __PREFIX__ReadString(json, ref index);
                return;
            }
            if (current == 't' && __PREFIX__ReadLiteral(json, ref index, ""true"")) return;
            if (current == 'f' && __PREFIX__ReadLiteral(json, ref index, ""false"")) return;
            if (current == 'n' && __PREFIX__ReadLiteral(json, ref index, ""null"")) return;
            if (current == '-' || (current >= '0' && current <= '9'))
            {
                __PREFIX__ReadNumber(json, ref index);
                return;
            }
            __PREFIX__InvalidSyntax();
        }

        private static void __PREFIX__ReadObject(string json, ref int index, int depth, int? maxDepth)
        {
            index++;
            __PREFIX__SkipWhitespace(json, ref index);
            if (index < json.Length && json[index] == '}') { index++; return; }
            while (true)
            {
                if (index >= json.Length || json[index] != '""') __PREFIX__InvalidSyntax();
                __PREFIX__ReadString(json, ref index);
                __PREFIX__SkipWhitespace(json, ref index);
                if (index >= json.Length || json[index++] != ':') __PREFIX__InvalidSyntax();
                __PREFIX__SkipWhitespace(json, ref index);
                __PREFIX__ReadValue(json, ref index, depth, maxDepth);
                __PREFIX__SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == '}') { index++; return; }
                if (index >= json.Length || json[index++] != ',') __PREFIX__InvalidSyntax();
                __PREFIX__SkipWhitespace(json, ref index);
            }
        }

        private static void __PREFIX__ReadArray(string json, ref int index, int depth, int? maxDepth)
        {
            index++;
            __PREFIX__SkipWhitespace(json, ref index);
            if (index < json.Length && json[index] == ']') { index++; return; }
            while (true)
            {
                __PREFIX__ReadValue(json, ref index, depth, maxDepth);
                __PREFIX__SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ']') { index++; return; }
                if (index >= json.Length || json[index++] != ',') __PREFIX__InvalidSyntax();
                __PREFIX__SkipWhitespace(json, ref index);
            }
        }

        private static void __PREFIX__ReadString(string json, ref int index)
        {
            index++;
            while (index < json.Length)
            {
                char current = json[index++];
                if (current == '""') return;
                if (current < 0x20) __PREFIX__InvalidSyntax();
                if (current != '\\') continue;
                if (index >= json.Length) __PREFIX__InvalidSyntax();
                char escaped = json[index++];
                if (escaped == 'u')
                {
                    for (int digit = 0; digit < 4; digit++)
                    {
                        if (index >= json.Length) __PREFIX__InvalidSyntax();
                        char hex = json[index++];
                        if (!((hex >= '0' && hex <= '9') || (hex >= 'a' && hex <= 'f') ||
                              (hex >= 'A' && hex <= 'F'))) __PREFIX__InvalidSyntax();
                    }
                }
                else if (escaped != '""' && escaped != '\\' && escaped != '/' &&
                         escaped != 'b' && escaped != 'f' && escaped != 'n' &&
                         escaped != 'r' && escaped != 't') __PREFIX__InvalidSyntax();
            }
            __PREFIX__InvalidSyntax();
        }

        private static void __PREFIX__ReadNumber(string json, ref int index)
        {
            if (json[index] == '-') index++;
            if (index >= json.Length) __PREFIX__InvalidSyntax();
            if (json[index] == '0') index++;
            else
            {
                if (json[index] < '1' || json[index] > '9') __PREFIX__InvalidSyntax();
                do { index++; }
                while (index < json.Length && json[index] >= '0' && json[index] <= '9');
            }
            if (index < json.Length && json[index] == '.')
            {
                index++;
                if (index >= json.Length || json[index] < '0' || json[index] > '9') __PREFIX__InvalidSyntax();
                do { index++; }
                while (index < json.Length && json[index] >= '0' && json[index] <= '9');
            }
            if (index < json.Length && (json[index] == 'e' || json[index] == 'E'))
            {
                index++;
                if (index < json.Length && (json[index] == '+' || json[index] == '-')) index++;
                if (index >= json.Length || json[index] < '0' || json[index] > '9') __PREFIX__InvalidSyntax();
                do { index++; }
                while (index < json.Length && json[index] >= '0' && json[index] <= '9');
            }
        }

        private static bool __PREFIX__ReadLiteral(string json, ref int index, string literal)
        {
            if (index + literal.Length > json.Length ||
                global::System.String.CompareOrdinal(json, index, literal, 0, literal.Length) != 0) return false;
            index += literal.Length;
            return true;
        }

        private static void __PREFIX__SkipWhitespace(string json, ref int index)
        {
            while (index < json.Length && (json[index] == ' ' || json[index] == '\t' ||
                   json[index] == '\r' || json[index] == '\n')) index++;
        }

        private static void __PREFIX__InvalidSyntax()
        {
            throw new global::Newtonsoft.Json.JsonSerializationException(""The successful response is not valid JSON."");
        }
".Replace("__PREFIX__", _prefix));
            }

            private void AppendMethod(StringBuilder source, GeneratedTypeModel type)
            {
                source.AppendLine();
                source.Append("        private static void ").Append(MethodName(type))
                    .AppendLine("(global::Newtonsoft.Json.Linq.JToken token, string path, bool nullable, string json, global::System.Collections.Generic.List<int> lineStarts, global::System.Collections.Generic.List<global::System.Tuple<int, int, string>> replacements)");
                source.AppendLine("        {");
                source.AppendLine("            if (token.Type == global::Newtonsoft.Json.Linq.JTokenType.Null)");
                source.AppendLine("            {");
                source.AppendLine("                if (nullable) return;");
                source.AppendLine("                throw new global::Newtonsoft.Json.JsonSerializationException(\"Non-nullable response value was null at \" + path + \".\");");
                source.AppendLine("            }");

                if (type.Kind == GeneratedTypeKind.Array)
                {
                    source.AppendLine("            if (token is global::Newtonsoft.Json.Linq.JObject wrapper)");
                    source.AppendLine("            {");
                    source.AppendLine("                if (wrapper.Property(\"$ref\") is not null && wrapper.Count == 1) return;");
                    source.AppendLine("                foreach (var property in wrapper.Properties())");
                    source.AppendLine("                {");
                    source.AppendLine("                    if (property.Name != \"$id\" && property.Name != \"$values\")");
                    source.AppendLine("                        throw new global::Newtonsoft.Json.JsonSerializationException(\"Undeclared response property at \" + path + \".\" + property.Name + \".\");");
                    source.AppendLine("                }");
                    source.AppendLine("                token = wrapper[\"$values\"] ?? token;");
                    source.AppendLine("            }");
                    source.AppendLine("            if (!(token is global::Newtonsoft.Json.Linq.JArray array))");
                    source.AppendLine("                throw new global::Newtonsoft.Json.JsonSerializationException(\"Expected a response array at \" + path + \".\");");
                    source.AppendLine("            for (int index = 0; index < array.Count; index++)");
                    source.AppendLine("            {");
                    source.Append("                ").Append(MethodName(type.ItemType!))
                        .Append("(array[index], path + \"[\" + index.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + \"]\", ")
                        .Append(type.ItemType!.Nullable ? "true" : "false").AppendLine(", json, lineStarts, replacements);");
                    source.AppendLine("            }");
                }
                else if (type.Kind == GeneratedTypeKind.Named && _dtos.TryGetValue(type.Name, out GeneratedDtoModel? dto))
                {
                    source.AppendLine("            if (!(token is global::Newtonsoft.Json.Linq.JObject objectToken))");
                    source.AppendLine("                throw new global::Newtonsoft.Json.JsonSerializationException(\"Expected a response object at \" + path + \".\");");
                    if (!dto.Properties.Any(static property => property.WireName == "$ref"))
                    {
                        source.AppendLine("            if (objectToken.Property(\"$ref\") is not null && objectToken.Count == 1) return;");
                    }
                    source.AppendLine("            foreach (var property in objectToken.Properties())");
                    source.AppendLine("            {");
                    source.AppendLine("                switch (property.Name)");
                    source.AppendLine("                {");
                    foreach (GeneratedDtoPropertyModel property in dto.Properties)
                    {
                        source.Append("                    case ")
                            .Append(GeneratedSourceEmitter.StringLiteral(property.WireName)).AppendLine(":");
                        source.Append("                        ").Append(MethodName(property.Type))
                            .Append("(property.Value, path + \".\" + ")
                            .Append(GeneratedSourceEmitter.StringLiteral(property.WireName)).Append(", ")
                            .Append(property.Type.Nullable ? "true" : "false").AppendLine(", json, lineStarts, replacements);");
                        source.AppendLine("                        break;");
                    }
                    source.AppendLine("                    default:");
                    source.AppendLine("                        if (property.Name == \"$id\") break;");
                    source.AppendLine("                        throw new global::Newtonsoft.Json.JsonSerializationException(\"Undeclared response property at \" + path + \".\" + property.Name + \".\");");
                    source.AppendLine("                }");
                    source.AppendLine("            }");
                }
                else
                {
                    if (type.Kind == GeneratedTypeKind.Int32 || type.Kind == GeneratedTypeKind.Int64)
                    {
                        source.Append("            if (!").Append(_prefix)
                            .Append("IsIntegralNumber(token, json, lineStarts, ")
                            .Append(type.Kind == GeneratedTypeKind.Int32 ? "true" : "false")
                            .AppendLine(", replacements))");
                    }
                    else if (type.Kind == GeneratedTypeKind.Single || type.Kind == GeneratedTypeKind.Double ||
                        type.Kind == GeneratedTypeKind.Decimal)
                    {
                        source.AppendLine("            if (token.Type != global::Newtonsoft.Json.Linq.JTokenType.Integer &&");
                        source.AppendLine("                token.Type != global::Newtonsoft.Json.Linq.JTokenType.Float)");
                    }
                    else
                    {
                        string expected = type.Kind == GeneratedTypeKind.Int32 || type.Kind == GeneratedTypeKind.Int64
                            ? "Integer"
                            : type.Kind == GeneratedTypeKind.Boolean ? "Boolean" : "String";
                        source.Append("            if (token.Type != global::Newtonsoft.Json.Linq.JTokenType.")
                            .Append(expected).AppendLine(")");
                    }
                    source.AppendLine("                throw new global::Newtonsoft.Json.JsonSerializationException(\"Unexpected response JSON token at \" + path + \".\");");
                    if (type.Kind == GeneratedTypeKind.DateTime ||
                        type.Kind == GeneratedTypeKind.DateTimeOffset || type.Kind == GeneratedTypeKind.Guid)
                    {
                        string validator = type.Kind == GeneratedTypeKind.DateTime
                            ? "IsFullDate"
                            : type.Kind == GeneratedTypeKind.DateTimeOffset ? "IsDateTime" : "IsUuid";
                        source.Append("            if (!").Append(_prefix).Append(validator)
                            .AppendLine("((string)((global::Newtonsoft.Json.Linq.JValue)token).Value!))");
                        source.AppendLine("                throw new global::Newtonsoft.Json.JsonSerializationException(\"Invalid response string format at \" + path + \".\");");
                    }
                }

                source.AppendLine("        }");
            }

            private void AppendStringFormatHelpers(StringBuilder source)
            {
                source.AppendLine(@"
        private static bool __PREFIX__IsAsciiDigit(char value) => value >= '0' && value <= '9';

        private static bool __PREFIX__IsTwoDigits(string value, int start) =>
            __PREFIX__IsAsciiDigit(value[start]) && __PREFIX__IsAsciiDigit(value[start + 1]);

        private static int __PREFIX__TwoDigits(string value, int start) =>
            (value[start] - '0') * 10 + value[start + 1] - '0';

        private static bool __PREFIX__IsFullDate(string value)
        {
            if (value.Length != 10 || value[4] != '-' || value[7] != '-' ||
                !__PREFIX__IsTwoDigits(value, 0) || !__PREFIX__IsTwoDigits(value, 2) ||
                !__PREFIX__IsTwoDigits(value, 5) || !__PREFIX__IsTwoDigits(value, 8)) return false;
            return global::System.DateTime.TryParseExact(value, ""yyyy-MM-dd"",
                global::System.Globalization.CultureInfo.InvariantCulture,
                global::System.Globalization.DateTimeStyles.None, out _);
        }

        private static bool __PREFIX__IsDateTime(string value)
        {
            if (value.Length < 20 || !__PREFIX__IsFullDate(value.Substring(0, 10)) ||
                (value[10] != 'T' && value[10] != 't') ||
                value[13] != ':' || value[16] != ':' ||
                !__PREFIX__IsTwoDigits(value, 11) || !__PREFIX__IsTwoDigits(value, 14) ||
                !__PREFIX__IsTwoDigits(value, 17) ||
                __PREFIX__TwoDigits(value, 11) > 23 ||
                __PREFIX__TwoDigits(value, 14) > 59 ||
                __PREFIX__TwoDigits(value, 17) > 60) return false;
            int index = 19;
            if (value[index] == '.')
            {
                index++;
                int fractionalStart = index;
                while (index < value.Length && __PREFIX__IsAsciiDigit(value[index])) index++;
                if (index == fractionalStart) return false;
            }
            if (index == value.Length - 1 && (value[index] == 'Z' || value[index] == 'z')) return true;
            return index == value.Length - 6 && (value[index] == '+' || value[index] == '-') &&
                   value[index + 3] == ':' && __PREFIX__IsTwoDigits(value, index + 1) &&
                   __PREFIX__IsTwoDigits(value, index + 4) &&
                   __PREFIX__TwoDigits(value, index + 1) <= 23 &&
                   __PREFIX__TwoDigits(value, index + 4) <= 59;
        }

        private static bool __PREFIX__IsUuid(string value)
        {
            if (value.Length != 36) return false;
            for (int index = 0; index < value.Length; index++)
            {
                if (index == 8 || index == 13 || index == 18 || index == 23)
                {
                    if (value[index] != '-') return false;
                }
                else if (!((value[index] >= '0' && value[index] <= '9') ||
                           (value[index] >= 'a' && value[index] <= 'f') ||
                           (value[index] >= 'A' && value[index] <= 'F'))) return false;
            }
            return true;
        }
".Replace("__PREFIX__", _prefix));
            }

            private void AppendIntegerLexemeHelper(StringBuilder source)
            {
                source.AppendLine();
                source.Append("        private static bool ").Append(_prefix)
                    .AppendLine("IsIntegralNumber(global::Newtonsoft.Json.Linq.JToken token, string json, global::System.Collections.Generic.List<int> lineStarts, bool int32, global::System.Collections.Generic.List<global::System.Tuple<int, int, string>> replacements)");
                source.AppendLine(@"        {
            if (token.Type == global::Newtonsoft.Json.Linq.JTokenType.Integer) return true;
            if (token.Type != global::Newtonsoft.Json.Linq.JTokenType.Float) return false;
            var location = (global::Newtonsoft.Json.IJsonLineInfo)token;
            if (!location.HasLineInfo()) return false;
            if (location.LineNumber < 1 || location.LineNumber > lineStarts.Count) return false;
            int lineStart = lineStarts[location.LineNumber - 1];
            int end = lineStart + location.LinePosition;
            if (end > json.Length || end <= lineStart) return false;
            int start = end;
            while (start > lineStart)
            {
                char character = json[start - 1];
                if ((character >= '0' && character <= '9') || character == '-' ||
                    character == '+' || character == '.' || character == 'e' || character == 'E')
                    start--;
                else
                    break;
            }
            if (start == end) return false;
            string number = json.Substring(start, end - start);
            int exponentMarker = number.IndexOfAny(new[] { 'e', 'E' });
            int mantissaEnd = exponentMarker < 0 ? number.Length : exponentMarker;
            int decimalPoint = number.IndexOf('.');
            int fractionalDigits = decimalPoint < 0 ? 0 : mantissaEnd - decimalPoint - 1;
            int trailingZeros = 0;
            bool hasNonzeroDigit = false;
            bool hasDigit = false;
            for (int index = mantissaEnd - 1; index >= 0; index--)
            {
                char character = number[index];
                if (character < '0' || character > '9') continue;
                hasDigit = true;
                if (character == '0' && !hasNonzeroDigit)
                    trailingZeros++;
                else if (character != '0')
                    hasNonzeroDigit = true;
            }
            if (!hasDigit) return false;
            if (hasNonzeroDigit)
            {
            int exponent = 0;
            if (exponentMarker >= 0 &&
                !global::System.Int32.TryParse(number.Substring(exponentMarker + 1),
                    global::System.Globalization.NumberStyles.AllowLeadingSign,
                    global::System.Globalization.CultureInfo.InvariantCulture, out exponent))
            {
                if (number[exponentMarker + 1] == '-') return false;
            }
            else if (exponent < fractionalDigits - trailingZeros) return false;
            }
            if (!global::System.Decimal.TryParse(number,
                global::System.Globalization.NumberStyles.Float,
                global::System.Globalization.CultureInfo.InvariantCulture, out var value)) return false;
            if (int32 && (value < global::System.Int32.MinValue || value > global::System.Int32.MaxValue)) return false;
            if (value < global::System.Int64.MinValue || value > global::System.Int64.MaxValue) return false;
            replacements.Add(global::System.Tuple.Create(start, end, value.ToString(""0"", global::System.Globalization.CultureInfo.InvariantCulture)));
            return true;
        }");
            }
        }

        private sealed class ResponseValidatorPlan
        {
            private readonly IReadOnlyDictionary<string, GeneratedDtoModel> _dtos;
            private readonly List<GeneratedTypeModel> _types = new List<GeneratedTypeModel>();
            private readonly Dictionary<GeneratedTypeModel, int> _indices =
                new Dictionary<GeneratedTypeModel, int>();
            private readonly string _methodPrefix;
            private readonly string _mediaTypePrefix;

            internal ResponseValidatorPlan(OpenApiGenerationModel model)
            {
                _dtos = model.Dtos.ToDictionary(static dto => dto.Name, StringComparer.Ordinal);
                var occupiedNames = new HashSet<string>(StringComparer.Ordinal) { model.ApiName };
                foreach (GeneratedOperationModel operation in model.Operations)
                {
                    occupiedNames.Add(operation.Name);
                    foreach (GeneratedParameterModel parameter in operation.Parameters)
                    {
                        occupiedNames.Add(parameter.Name);
                    }

                    if (operation.RequestBody is GeneratedRequestBodyModel requestBody)
                    {
                        occupiedNames.Add(requestBody.ParameterName);
                        if (requestBody.SpecifiedParameterName is string specifiedParameterName)
                        {
                            occupiedNames.Add(specifiedParameterName);
                        }
                    }
                }

                foreach (GeneratedDtoModel dto in model.Dtos)
                {
                    occupiedNames.Add(dto.Name);
                }

                foreach (GeneratedEnumModel generatedEnum in model.Enums)
                {
                    occupiedNames.Add(generatedEnum.Name);
                }

                string prefix = "__OacgResponseValidator_";
                while (occupiedNames.Any(name => name.StartsWith(prefix, StringComparison.Ordinal)))
                {
                    prefix += "_";
                }

                _methodPrefix = prefix;
                prefix = "__OacgContentType_";
                while (occupiedNames.Any(name => name.StartsWith(prefix, StringComparison.Ordinal)))
                {
                    prefix += "_";
                }

                _mediaTypePrefix = prefix;
                foreach (GeneratedOperationModel operation in model.Operations)
                {
                    if (operation.ResponseType is GeneratedTypeModel responseType &&
                        RequiresValidation(responseType, new HashSet<string>(StringComparer.Ordinal)))
                    {
                        Register(responseType);
                    }

                    if (operation.RequestBody is GeneratedRequestBodyModel requestBody &&
                        RequiresValidation(requestBody.Type, new HashSet<string>(StringComparer.Ordinal)))
                    {
                        Register(requestBody.Type);
                    }
                }
            }

            internal string ComparerName => _methodPrefix + "Comparer";

            internal string MediaTypeValidatorName => _mediaTypePrefix + "Validate";

            internal string HeaderValueValidatorName => _mediaTypePrefix + "ValidateHeaderValue";

            internal string RequestJsonSerializerName => _mediaTypePrefix + "SerializeRequestJson";

            internal string MediaTypesLocalName => _mediaTypePrefix + "DeclaredMediaTypes";

            internal bool TryGetMethodName(GeneratedTypeModel type, out string? methodName)
            {
                if (_indices.TryGetValue(type.WithNullable(false), out int index))
                {
                    methodName = _methodPrefix + index;
                    return true;
                }

                methodName = null;
                return false;
            }

            internal void AppendMethods(StringBuilder source)
            {
                if (_types.Count == 0)
                {
                    return;
                }

                foreach (GeneratedTypeModel type in _types)
                {
                    source.AppendLine();
                    AppendMethod(source, type);
                }

                source.AppendLine();
                source.Append("        private sealed class ").Append(ComparerName)
                    .AppendLine(" : global::System.Collections.Generic.IEqualityComparer<object>");
                source.AppendLine("        {");
                source.AppendLine("            public bool Equals(object? left, object? right) => global::System.Object.ReferenceEquals(left, right);");
                source.AppendLine("            public int GetHashCode(object value) => global::System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);");
                source.AppendLine("        }");
            }

            private bool RequiresValidation(GeneratedTypeModel type, HashSet<string> visited)
            {
                if (type.Kind == GeneratedTypeKind.Single || type.Kind == GeneratedTypeKind.Double)
                {
                    return true;
                }

                if (type.Kind == GeneratedTypeKind.Array)
                {
                    GeneratedTypeModel itemType = type.ItemType!;
                    return (!itemType.Nullable && !itemType.IsValueType) ||
                           RequiresValidation(itemType, visited);
                }

                if (type.Kind != GeneratedTypeKind.Named ||
                    !_dtos.TryGetValue(type.Name, out GeneratedDtoModel? dto) ||
                    !visited.Add(type.Name))
                {
                    return false;
                }

                // A shared DTO only needs to be searched once during this reachability check.
                return dto.Properties.Any(property => RequiresValidation(property.Type, visited));
            }

            private void Register(GeneratedTypeModel type)
            {
                if (!RequiresValidation(type, new HashSet<string>(StringComparer.Ordinal)))
                {
                    return;
                }

                type = type.WithNullable(false);
                if (_indices.ContainsKey(type))
                {
                    return;
                }

                _indices.Add(type, _types.Count);
                _types.Add(type);
                if (type.Kind == GeneratedTypeKind.Array)
                {
                    Register(type.ItemType!);
                }
                else if (type.Kind == GeneratedTypeKind.Named &&
                         _dtos.TryGetValue(type.Name, out GeneratedDtoModel? dto))
                {
                    foreach (GeneratedDtoPropertyModel property in dto.Properties)
                    {
                        Register(property.Type);
                    }
                }
            }

            private void AppendMethod(StringBuilder source, GeneratedTypeModel type)
            {
                TryGetMethodName(type, out string? methodName);
                int validatorIndex = _indices[type.WithNullable(false)];
                source.Append("        private static void ").Append(methodName).Append('(')
                    .Append(GeneratedSourceEmitter.TypeName(type.WithNullable(true)))
                    .AppendLine(" value, string path, string contract, global::System.Collections.Generic.Dictionary<object, global::System.Collections.Generic.HashSet<int>> visited)");
                source.AppendLine("        {");
                source.AppendLine("            if (value is null)");
                source.AppendLine("            {");
                source.AppendLine("                return;");
                source.AppendLine("            }");
                if (type.Kind == GeneratedTypeKind.Single || type.Kind == GeneratedTypeKind.Double)
                {
                    string numberType = type.Kind == GeneratedTypeKind.Single ? "Single" : "Double";
                    source.Append("            if (value.HasValue && (global::System.")
                        .Append(numberType).Append(".IsNaN(value.Value) || global::System.")
                        .Append(numberType).AppendLine(".IsInfinity(value.Value)))");
                    source.AppendLine("            {");
                    source.AppendLine("                throw new global::Newtonsoft.Json.JsonSerializationException(\"Non-finite \" + contract + \" number at \" + path + \".\");");
                    source.AppendLine("            }");
                    source.AppendLine("            return;");
                    source.AppendLine("        }");
                    return;
                }

                source.AppendLine("            if (!visited.TryGetValue(value, out var validatedTypes))");
                source.AppendLine("            {");
                source.AppendLine("                validatedTypes = new global::System.Collections.Generic.HashSet<int>();");
                source.AppendLine("                visited.Add(value, validatedTypes);");
                source.AppendLine("            }");
                source.Append("            if (!validatedTypes.Add(")
                    .Append(validatorIndex).AppendLine("))");
                source.AppendLine("            {");
                source.AppendLine("                return;");
                source.AppendLine("            }");

                if (type.Kind == GeneratedTypeKind.Array)
                {
                    GeneratedTypeModel itemType = type.ItemType!;
                    source.AppendLine("            for (int index = 0; index < value.Count; index++)");
                    source.AppendLine("            {");
                    source.AppendLine("                var item = value[index];");
                    source.AppendLine("                string itemPath = path + \"[\" + index.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + \"]\";");
                    if (!itemType.Nullable && !itemType.IsValueType)
                    {
                        source.AppendLine("                if (item is null)");
                        source.AppendLine("                {");
                        source.AppendLine("                    throw new global::Newtonsoft.Json.JsonSerializationException(\"Non-nullable \" + contract + \" array element was null at \" + itemPath + \".\");");
                        source.AppendLine("                }");
                    }

                    if (TryGetMethodName(itemType, out string? itemMethodName))
                    {
                        source.Append("                ").Append(itemMethodName)
                            .AppendLine("(item, itemPath, contract, visited);");
                    }

                    source.AppendLine("            }");
                }
                else if (type.Kind == GeneratedTypeKind.Named)
                {
                    foreach (GeneratedDtoPropertyModel property in _dtos[type.Name].Properties)
                    {
                        if (TryGetMethodName(property.Type, out string? propertyMethodName))
                        {
                            if (property.UseSpecified)
                            {
                                source.Append("            if (contract != \"request\" || value.")
                                    .Append(property.Name).AppendLine("Specified)");
                                source.AppendLine("            {");
                            }

                            string indent = property.UseSpecified ? "                " : "            ";
                            source.Append(indent).Append(propertyMethodName)
                                .Append("(value.").Append(property.Name).Append(", path + \".\" + ")
                                .Append(GeneratedSourceEmitter.StringLiteral(property.WireName))
                                .AppendLine(", contract, visited);");
                            if (property.UseSpecified)
                            {
                                source.AppendLine("            }");
                            }
                        }
                    }
                }

                source.AppendLine("        }");
            }
        }

        private static void AppendParameterSeparator(StringBuilder source, ref bool first)
        {
            if (!first)
            {
                source.Append(", ");
            }

            first = false;
        }

        private static string EscapeXml(string value)
        {
            var normalized = new StringBuilder(value.Length);
            foreach (char character in value)
            {
                normalized.Append(char.IsControl(character) ||
                                  character == '\u2028' ||
                                  character == '\u2029'
                    ? ' '
                    : character);
            }

            return normalized.ToString().Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }
    }
}
