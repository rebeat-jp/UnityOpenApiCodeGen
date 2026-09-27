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

            foreach (GeneratedOperationModel operation in model.Operations)
            {
                source.AppendLine();
                AppendOperation(source, model, operation, responseValidators);
            }

            source.AppendLine();
            AppendHelpers(source);
            responseValidators.AppendMethods(source);
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
            ResponseValidatorPlan responseValidators)
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
            AppendHeaderParameters(source, operation);
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

            if (operation.ResponseType is null)
            {
                source.AppendLine("                    return;");
            }
            else
            {
                if (!operation.ResponseType.Nullable)
                {
                    source.AppendLine("                    if (global::System.String.IsNullOrWhiteSpace(responseBody) ||");
                    source.AppendLine("                        global::System.String.Equals(responseBody.Trim(), \"null\", global::System.StringComparison.Ordinal))");
                    source.AppendLine("                    {");
                    source.AppendLine("                        throw new global::Newtonsoft.Json.JsonSerializationException(\"The successful response requires a non-null JSON body.\");");
                    source.AppendLine("                    }");
                }
                source.Append("                    var deserializedResponse = global::Newtonsoft.Json.JsonConvert.DeserializeObject<")
                    .Append(GeneratedSourceEmitter.TypeName(operation.ResponseType))
                    .AppendLine(">(responseBody, new global::Newtonsoft.Json.JsonSerializerSettings { DateParseHandling = global::Newtonsoft.Json.DateParseHandling.None });");
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

        private static void AppendHeaderParameters(StringBuilder source, GeneratedOperationModel operation)
        {
            foreach (GeneratedParameterModel parameter in operation.Parameters.Where(
                         static value => value.LocationName == "header"))
            {
                string statement = "if (!request.Headers.TryAddWithoutValidation(" +
                                   GeneratedSourceEmitter.StringLiteral(parameter.WireName) +
                                   ", ConvertToString(" + GetValueExpression(parameter) + "))) " +
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
            source.Append(indent).Append("string requestJson = global::Newtonsoft.Json.JsonConvert.SerializeObject(")
                .Append(body.ParameterName)
                .AppendLine(", DateOnlyJsonConverterInstance);");
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

        private static void AppendHelpers(StringBuilder source)
        {
            source.AppendLine("        private static readonly global::Newtonsoft.Json.JsonConverter DateOnlyJsonConverterInstance = new DateOnlyJsonConverter();");
            source.AppendLine();
            source.AppendLine("        private global::System.Uri CreateRequestUri(string relativePath)");
            source.AppendLine("        {");
            source.AppendLine("            SplitPathAndQuery(relativePath, out string operationPath, out string operationQuery);");
            source.AppendLine("            if (_baseUrl.StartsWith(\"//\", global::System.StringComparison.Ordinal))");
            source.AppendLine("            {");
            source.AppendLine("                if (_httpClient.BaseAddress is null)");
            source.AppendLine("                {");
            source.AppendLine("                    throw new global::System.InvalidOperationException(\"A HttpClient.BaseAddress is required for a network-path server URL.\");");
            source.AppendLine("                }");
            source.AppendLine("                var networkBaseUri = new global::System.Uri(_httpClient.BaseAddress, _baseUrl);");
            source.AppendLine("                return CombineAbsoluteUri(networkBaseUri, operationPath, operationQuery);");
            source.AppendLine("            }");
            source.AppendLine();
            source.AppendLine("            if (_baseUrl.StartsWith(\"/\", global::System.StringComparison.Ordinal))");
            source.AppendLine("            {");
            source.AppendLine("                if (_httpClient.BaseAddress is null)");
            source.AppendLine("                {");
            source.AppendLine("                    throw new global::System.InvalidOperationException(\"A HttpClient.BaseAddress is required for a root-relative server URL.\");");
            source.AppendLine("                }");
            source.AppendLine("                var rootBaseUri = new global::System.Uri(_httpClient.BaseAddress, _baseUrl);");
            source.AppendLine("                return CombineAbsoluteUri(rootBaseUri, operationPath, operationQuery);");
            source.AppendLine("            }");
            source.AppendLine();
            source.AppendLine("            if (global::System.Uri.TryCreate(_baseUrl, global::System.UriKind.Absolute, out var absoluteBaseUri) &&");
            source.AppendLine("                (absoluteBaseUri.Scheme == global::System.Uri.UriSchemeHttp ||");
            source.AppendLine("                 absoluteBaseUri.Scheme == global::System.Uri.UriSchemeHttps))");
            source.AppendLine("            {");
            source.AppendLine("                return CombineAbsoluteUri(absoluteBaseUri, operationPath, operationQuery);");
            source.AppendLine("            }");
            source.AppendLine();
            source.AppendLine("            SplitPathAndQuery(_baseUrl, out string basePath, out string baseQuery);");
            source.AppendLine("            string combinedPath = CombinePaths(basePath, operationPath);");
            source.AppendLine("            string combinedQuery = CombineQueries(baseQuery, operationQuery);");
            source.AppendLine("            if (_httpClient.BaseAddress != null)");
            source.AppendLine("            {");
            source.AppendLine("                var resolvedUri = new global::System.Uri(_httpClient.BaseAddress, combinedPath.TrimStart('/')); ");
            source.AppendLine("                string resolvedQuery = CombineQueries(_httpClient.BaseAddress.Query.TrimStart('?'), combinedQuery);");
            source.AppendLine("                return CombineAbsoluteUri(resolvedUri, string.Empty, resolvedQuery);");
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

        private sealed class ResponseValidatorPlan
        {
            private readonly IReadOnlyDictionary<string, GeneratedDtoModel> _dtos;
            private readonly List<GeneratedTypeModel> _types = new List<GeneratedTypeModel>();
            private readonly Dictionary<GeneratedTypeModel, int> _indices =
                new Dictionary<GeneratedTypeModel, int>();
            private readonly string _methodPrefix;

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
