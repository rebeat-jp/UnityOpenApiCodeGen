using System;
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

            foreach (GeneratedOperationModel operation in model.Operations)
            {
                source.AppendLine();
                AppendOperation(source, model, operation);
            }

            source.AppendLine();
            AppendHelpers(source);
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
            GeneratedOperationModel operation)
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
            source.AppendLine("global::System.Threading.CancellationToken cancellationToken = default)");
            source.AppendLine("        {");
            source.Append("            string relativePath = ")
                .Append(GeneratedSourceEmitter.StringLiteral(operation.Path))
                .AppendLine(";");
            AppendPathParameters(source, operation);
            AppendQueryParameters(source, operation);
            source.AppendLine("            var requestUri = CreateRequestUri(relativePath);");
            source.Append("            using (var request = new global::System.Net.Http.HttpRequestMessage(")
                .Append(GetHttpMethod(operation.HttpMethod))
                .AppendLine(", requestUri))");
            source.AppendLine("            {");
            AppendHeaderParameters(source, operation);
            AppendRequestBody(source, operation.RequestBody);
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
                source.Append("                    return global::Newtonsoft.Json.JsonConvert.DeserializeObject<")
                    .Append(GeneratedSourceEmitter.TypeName(operation.ResponseType))
                    .AppendLine(">(responseBody)!;");
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
                string statement = "request.Headers.TryAddWithoutValidation(" +
                                   GeneratedSourceEmitter.StringLiteral(parameter.WireName) +
                                   ", ConvertToString(" + GetValueExpression(parameter) + "));";
                AppendConditionalStatement(source, parameter, statement, 16);
            }
        }

        private static void AppendRequestBody(StringBuilder source, GeneratedRequestBodyModel? body)
        {
            if (body is null)
            {
                return;
            }

            if (!body.Required)
            {
                source.Append("                if (").Append(body.ParameterName).AppendLine(" != null)");
                source.AppendLine("                {");
            }

            string indent = body.Required ? "                " : "                    ";
            source.Append(indent).Append("string requestJson = global::Newtonsoft.Json.JsonConvert.SerializeObject(")
                .Append(body.ParameterName)
                .AppendLine(");");
            source.Append(indent).Append("request.Content = new global::System.Net.Http.StringContent(requestJson, global::System.Text.Encoding.UTF8, ")
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
            source.AppendLine("        private global::System.Uri CreateRequestUri(string relativePath)");
            source.AppendLine("        {");
            source.AppendLine("            string combined = string.IsNullOrEmpty(_baseUrl)");
            source.AppendLine("                ? relativePath");
            source.AppendLine("                : _baseUrl.TrimEnd('/') + \"/\" + relativePath.TrimStart('/');");
            source.AppendLine("            if (global::System.Uri.TryCreate(combined, global::System.UriKind.Absolute, out var absoluteUri))");
            source.AppendLine("            {");
            source.AppendLine("                return absoluteUri;");
            source.AppendLine("            }");
            source.AppendLine();
            source.AppendLine("            if (_httpClient.BaseAddress != null)");
            source.AppendLine("            {");
            source.AppendLine("                return new global::System.Uri(_httpClient.BaseAddress, combined.TrimStart('/')); ");
            source.AppendLine("            }");
            source.AppendLine();
            source.AppendLine("            return new global::System.Uri(combined, global::System.UriKind.Relative);");
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
            source.AppendLine("                return global::Newtonsoft.Json.JsonConvert.DeserializeObject<string>(json) ?? string.Empty;");
            source.AppendLine("            }");
            source.AppendLine();
            source.AppendLine("            return value is global::System.IFormattable formattable");
            source.AppendLine("                ? formattable.ToString(null, global::System.Globalization.CultureInfo.InvariantCulture)");
            source.AppendLine("                : value.ToString() ?? string.Empty;");
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
            return value.Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }
    }
}
