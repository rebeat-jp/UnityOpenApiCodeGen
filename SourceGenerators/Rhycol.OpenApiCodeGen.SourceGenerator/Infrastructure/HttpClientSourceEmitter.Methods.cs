using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// The operation methods of the generated client. An operation produces one method per request
    /// media type (the primary one keeps the operation name, the others carry a suffix such as
    /// AsMultipart) and, when its success responses declare headers, a WithHttpInfo method per
    /// variant that returns the status code, the headers and the data.
    /// </summary>
    internal sealed partial class HttpClientSourceEmitter
    {
        private enum MethodMode
        {
            /// <summary>Returns the data (the body of the matched success response).</summary>
            Data,
            /// <summary>Returns the status code, the headers and the data.</summary>
            HttpInfo
        }

        private static void AppendOperationMethods(
            StringBuilder source,
            OpenApiGenerationModel model,
            GeneratedOperationModel operation,
            ResponseValidatorPlan responseValidators,
            ResponseJsonContractPlan responseJsonContracts,
            HttpClientParameterSerializationEmitter parameters,
            BodyHelperNames helpers)
        {
            GeneratedRequestBodyModel? body = operation.RequestBody;
            if (body is null)
            {
                AppendOperationVariant(
                    source, model, operation, null, operation.Name,
                    operation.ResponsePlan?.HttpInfo?.MethodName ?? string.Empty,
                    responseValidators, responseJsonContracts, parameters, helpers);
                return;
            }

            bool first = true;
            foreach (GeneratedBodyVariantModel variant in body.Variants)
            {
                if (!first)
                {
                    source.AppendLine();
                }

                first = false;
                AppendOperationVariant(
                    source, model, operation, variant,
                    variant.MethodName.Length > 0 ? variant.MethodName : operation.Name,
                    variant.HttpInfoMethodName,
                    responseValidators, responseJsonContracts, parameters, helpers);
            }
        }

        private static void AppendOperationVariant(
            StringBuilder source,
            OpenApiGenerationModel model,
            GeneratedOperationModel operation,
            GeneratedBodyVariantModel? variant,
            string methodName,
            string httpInfoMethodName,
            ResponseValidatorPlan responseValidators,
            ResponseJsonContractPlan responseJsonContracts,
            HttpClientParameterSerializationEmitter parameters,
            BodyHelperNames helpers)
        {
            GeneratedHttpInfoModel? info = operation.ResponsePlan?.HttpInfo;
            if (info is null)
            {
                AppendOperationMethod(
                    source, model, operation, variant, methodName, MethodMode.Data,
                    responseValidators, responseJsonContracts, parameters, helpers);
                return;
            }

            string infoMethodName = httpInfoMethodName.Length > 0 ? httpInfoMethodName : methodName + "WithHttpInfo";
            AppendOperationMethod(
                source, model, operation, variant, infoMethodName, MethodMode.HttpInfo,
                responseValidators, responseJsonContracts, parameters, helpers);
            source.AppendLine();

            // The data method forwards to the WithHttpInfo method and returns its Data.
            GeneratedResponsePlanModel plan = operation.ResponsePlan!;
            string? returnType = plan.ReturnTypeName;
            string taskType = returnType is null
                ? "global::System.Threading.Tasks.Task"
                : "global::System.Threading.Tasks.Task<" + returnType + ">";
            AppendMethodHeader(source, operation, variant, methodName, taskType, string.Empty);
            source.Append("            var oacgResult = await ").Append(infoMethodName).Append('(');
            AppendForwardedArguments(source, operation, variant);
            source.AppendLine(").ConfigureAwait(false);");
            source.AppendLine(returnType is null ? "            return;" : "            return oacgResult.Data;");
            source.AppendLine("        }");
        }

        private static void AppendForwardedArguments(
            StringBuilder source,
            GeneratedOperationModel operation,
            GeneratedBodyVariantModel? variant)
        {
            bool first = true;
            foreach (GeneratedParameterModel parameter in operation.Parameters)
            {
                AppendParameterSeparator(source, ref first);
                source.Append(parameter.Name);
            }

            if (operation.RequestBody is not null && variant is not null)
            {
                AppendParameterSeparator(source, ref first);
                source.Append(operation.RequestBody.ParameterName);
            }

            AppendParameterSeparator(source, ref first);
            source.Append("cancellationToken");
            if (variant?.SpecifiedParameterName is string specified)
            {
                source.Append(", ").Append(specified);
            }

            if (variant?.ContentTypeParameterName is string contentType)
            {
                source.Append(", ").Append(contentType);
            }
        }

        private static string GetTaskType(
            GeneratedOperationModel operation,
            GeneratedResponsePlanModel? plan,
            MethodMode mode)
        {
            if (mode == MethodMode.HttpInfo)
            {
                return "global::System.Threading.Tasks.Task<" + plan!.HttpInfo!.TypeName + ">";
            }

            if (plan is not null)
            {
                return plan.ReturnTypeName is null
                    ? "global::System.Threading.Tasks.Task"
                    : "global::System.Threading.Tasks.Task<" + plan.ReturnTypeName + ">";
            }

            return operation.ResponseType is null
                ? "global::System.Threading.Tasks.Task"
                : "global::System.Threading.Tasks.Task<" +
                  GeneratedSourceEmitter.TypeName(operation.ResponseType) + ">";
        }

        /// <summary>Writes the documentation, the signature and the opening brace of a method.</summary>
        private static void AppendMethodHeader(
            StringBuilder source,
            GeneratedOperationModel operation,
            GeneratedBodyVariantModel? variant,
            string methodName,
            string taskType,
            string summarySuffix)
        {
            string summary = string.IsNullOrWhiteSpace(operation.Summary)
                ? operation.Name
                : operation.Summary;
            if (variant is not null && variant.MethodSuffix.Length > 0)
            {
                summarySuffix = " (request body as " + variant.MediaType + ")" + summarySuffix;
            }

            source.Append("        /// <summary>")
                .Append(EscapeXml(summary + summarySuffix))
                .AppendLine("</summary>");
            if (!string.IsNullOrWhiteSpace(operation.Description))
            {
                var remarks = new StringBuilder();
                GeneratedDocumentationEmitter.AppendSummary(remarks, "        ", operation.Description);
                source.Append(remarks.ToString()
                    .Replace("/// <summary>", "/// <remarks>")
                    .Replace("/// </summary>", "/// </remarks>"));
            }

            foreach (GeneratedParameterModel parameter in operation.Parameters)
            {
                source.Append("        /// <param name=\"")
                    .Append(parameter.Name)
                    .Append("\">")
                    .Append(EscapeXml(parameter.WireName));
                if (parameter.Deprecated)
                {
                    source.Append(" (deprecated)");
                }

                if (!string.IsNullOrWhiteSpace(parameter.Description))
                {
                    source.Append(": ").Append(EscapeXml(string.Join(
                        " ",
                        parameter.Description.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(static line => line.Trim())
                            .Where(static line => line.Length > 0))));
                }

                foreach (string sentence in GeneratedConstraintDocumentation.Describe(parameter.Type.ConstraintSets))
                {
                    source.Append(' ').Append(EscapeXml(sentence));
                }

                source.AppendLine("</param>");
            }

            if (operation.RequestBody is not null && variant is not null)
            {
                source.Append("        /// <param name=\"")
                    .Append(operation.RequestBody.ParameterName)
                    .Append("\">")
                    .Append(EscapeXml(DescribeBody(variant)))
                    .AppendLine("</param>");
            }

            source.AppendLine("        /// <param name=\"cancellationToken\">Cancellation token.</param>");
            if (variant?.SpecifiedParameterName is string specifiedParameterName)
            {
                source.Append("        /// <param name=\"").Append(specifiedParameterName)
                    .AppendLine("\">Send the JSON body even when its value is null.</param>");
            }

            if (variant?.ContentTypeParameterName is string contentTypeParameterName)
            {
                source.Append("        /// <param name=\"").Append(contentTypeParameterName)
                    .Append("\">Content-Type of the body; application/octet-stream when null (the declared media type is ")
                    .Append(EscapeXml("a wildcard")).AppendLine(").</param>");
            }

            GeneratedDocumentationEmitter.AppendObsolete(source, "        ", operation.Deprecated);
            source.Append("        public async ").Append(taskType).Append(' ').Append(methodName).Append('(');
            bool first = true;
            foreach (GeneratedParameterModel parameter in operation.Parameters)
            {
                AppendParameterSeparator(source, ref first);
                source.Append(GeneratedSourceEmitter.TypeName(parameter.Type))
                    .Append(' ')
                    .Append(parameter.Name);
            }

            if (operation.RequestBody is not null && variant is not null)
            {
                AppendParameterSeparator(source, ref first);
                source.Append(GeneratedSourceEmitter.TypeName(variant.Type))
                    .Append(' ')
                    .Append(operation.RequestBody.ParameterName);
            }

            AppendParameterSeparator(source, ref first);
            source.Append("global::System.Threading.CancellationToken cancellationToken = default");
            if (variant?.SpecifiedParameterName is string specifiedParameter)
            {
                source.Append(", bool ").Append(specifiedParameter).Append(" = false");
            }

            if (variant?.ContentTypeParameterName is string contentTypeParameter)
            {
                source.Append(", string? ").Append(contentTypeParameter).Append(" = null");
            }

            source.AppendLine(")");
            source.AppendLine("        {");
        }

        private static string DescribeBody(GeneratedBodyVariantModel variant)
        {
            switch (variant.Kind)
            {
                case GeneratedBodyKind.FormUrlEncoded:
                    return "application/x-www-form-urlencoded request body: each property is a form field.";
                case GeneratedBodyKind.Multipart:
                    return "multipart/form-data request body: each property is a part (a file part takes a FileParameter; a stream is disposed with the request).";
                case GeneratedBodyKind.Text:
                    return "Text request body (" + variant.MediaType + ").";
                case GeneratedBodyKind.Binary:
                    return "Binary request body (" + variant.MediaType + ").";
                default:
                    return "JSON request body.";
            }
        }

        private static void AppendOperationMethod(
            StringBuilder source,
            OpenApiGenerationModel model,
            GeneratedOperationModel operation,
            GeneratedBodyVariantModel? variant,
            string methodName,
            MethodMode mode,
            ResponseValidatorPlan responseValidators,
            ResponseJsonContractPlan responseJsonContracts,
            HttpClientParameterSerializationEmitter parameters,
            BodyHelperNames helpers)
        {
            GeneratedResponsePlanModel? plan = operation.ResponsePlan;
            GeneratedRequestBodyModel? body = operation.RequestBody;
            AppendMethodHeader(
                source,
                operation,
                variant,
                methodName,
                GetTaskType(operation, plan, mode),
                mode == MethodMode.HttpInfo ? " (with status code and headers)" : string.Empty);
            AppendNumericParameterValidation(source, operation);
            AppendRequestBodyValidation(source, body, variant);
            parameters.Constraints.AppendParameterChecks(source, operation);
            parameters.Security.AppendResolve(source, operation);
            source.Append("            string relativePath = ")
                .Append(GeneratedSourceEmitter.StringLiteral(operation.Path))
                .AppendLine(";");
            parameters.AppendPathParameters(source, operation);
            parameters.AppendQueryParameters(source, operation);
            parameters.Security.AppendQuery(source, operation);
            source.AppendLine("            ValidatePathSegments(relativePath);");
            if (operation.Servers.Count > 0)
            {
                source.Append("            var requestUri = CreateRequestUri(relativePath, ResolveOperationServerUrl(")
                    .Append(GeneratedSourceEmitter.StringLiteral(OperationKey(operation))).Append(", ")
                    .Append(GeneratedSourceEmitter.StringLiteral(operation.Servers[0].DefaultUrl))
                    .AppendLine("));");
            }
            else
            {
                source.AppendLine("            var requestUri = CreateRequestUri(relativePath);");
            }

            source.Append("            using (var request = new global::System.Net.Http.HttpRequestMessage(")
                .Append(GetHttpMethod(operation.HttpMethod))
                .AppendLine(", requestUri))");
            source.AppendLine("            {");
            parameters.AppendHeaderAndCookieParameters(source, operation);
            parameters.Security.AppendHeaders(source, operation);
            if (body is not null && variant is not null)
            {
                AppendRequestBody(source, body, variant, responseValidators, parameters, helpers);
            }

            if (plan is not null)
            {
                AppendAcceptHeader(source, plan);
            }

            source.AppendLine("                using (var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))");
            source.AppendLine("                {");
            if (plan is null)
            {
                AppendOriginalResponse(source, model, operation, responseValidators, responseJsonContracts, helpers);
            }
            else
            {
                AppendPlannedResponse(
                    source, model, operation, plan, mode, responseValidators, responseJsonContracts, helpers);
            }

            source.AppendLine("                }");
            source.AppendLine("            }");
            source.AppendLine("        }");
        }

        /// <summary>The response handling of operations that fit the original contract: void or one JSON type.</summary>
        private static void AppendOriginalResponse(
            StringBuilder source,
            OpenApiGenerationModel model,
            GeneratedOperationModel operation,
            ResponseValidatorPlan responseValidators,
            ResponseJsonContractPlan responseJsonContracts,
            BodyHelperNames helpers)
        {
            if (operation.ResponseType is null)
            {
                source.AppendLine("                    string responseBody = response.Content == null");
                source.AppendLine("                        ? string.Empty");
                source.AppendLine("                        : await response.Content.ReadAsStringAsync().ConfigureAwait(false);");
                source.Append("                    if (!(")
                    .Append(CreateSuccessExpression(operation.SuccessStatusCodes))
                    .AppendLine("))");
                source.AppendLine("                    {");
                AppendThrowApiException(source, model, operation, "responseBody", helpers);
                source.AppendLine("                    }");
                AppendResponseContentTypeValidation(source, operation, responseValidators);
                source.AppendLine("                    return;");
                return;
            }

            source.Append("                    if (!(")
                .Append(CreateSuccessExpression(operation.SuccessStatusCodes))
                .AppendLine("))");
            source.AppendLine("                    {");
            AppendThrowApiException(
                source,
                model,
                operation,
                "response.Content == null ? string.Empty : await response.Content.ReadAsStringAsync().ConfigureAwait(false)",
                helpers);
            source.AppendLine("                    }");
            AppendResponseContentTypeValidation(source, operation, responseValidators);
            source.AppendLine("                    string responseBody = response.Content == null");
            source.AppendLine("                        ? string.Empty");
            source.AppendLine("                        : await ReadJsonResponseBodyAsync(response.Content).ConfigureAwait(false);");
            AppendJsonDecode(
                source, operation.ResponseType, "                    ", responseValidators, responseJsonContracts,
                resultVariable: null);
        }

        /// <summary>
        /// Writes the JSON contract check and deserialization of the success body that is in
        /// <c>responseBody</c>. Ends with <c>return deserializedResponse!;</c> or, with
        /// <paramref name="resultVariable"/>, with an assignment to it.
        /// </summary>
        private static void AppendJsonDecode(
            StringBuilder source,
            GeneratedTypeModel type,
            string indent,
            ResponseValidatorPlan responseValidators,
            ResponseJsonContractPlan responseJsonContracts,
            string? resultVariable)
        {
            source.Append(indent).AppendLine("if (global::System.String.IsNullOrWhiteSpace(responseBody))");
            source.Append(indent).AppendLine("{");
            source.Append(indent).AppendLine("    throw new global::Newtonsoft.Json.JsonSerializationException(\"The successful response requires a JSON body.\");");
            source.Append(indent).AppendLine("}");
            if (!type.Nullable)
            {
                source.Append(indent).AppendLine("if (global::System.String.Equals(responseBody.Trim(), \"null\", global::System.StringComparison.Ordinal))");
                source.Append(indent).AppendLine("{");
                source.Append(indent).AppendLine("    throw new global::Newtonsoft.Json.JsonSerializationException(\"The successful response requires a non-null JSON body.\");");
                source.Append(indent).AppendLine("}");
            }

            source.Append(indent).AppendLine("var defaultResponseSettings = global::Newtonsoft.Json.JsonConvert.DefaultSettings?.Invoke();");
            source.Append(indent).AppendLine("var responseSerializer = global::Newtonsoft.Json.JsonSerializer.Create();");
            source.Append(indent).AppendLine("responseSerializer.DateParseHandling = global::Newtonsoft.Json.DateParseHandling.None;");
            source.Append(indent).AppendLine("responseSerializer.MaxDepth = defaultResponseSettings?.MaxDepth > 0 ? defaultResponseSettings.MaxDepth : 64;");
            source.Append(indent).Append("string validatedResponseBody = ").Append(responseJsonContracts.ParseMethodName)
                .Append("(responseBody, ").Append(responseJsonContracts.MethodName(type))
                .Append(", ").Append(type.Nullable ? "true" : "false")
                .AppendLine(", responseSerializer.MaxDepth);");
            source.Append(indent).Append("var deserializedResponse = ").Append(responseJsonContracts.DeserializeMethodName).Append('<')
                .Append(GeneratedSourceEmitter.TypeName(type))
                .AppendLine(">(validatedResponseBody, responseSerializer);");
            if (!type.Nullable && !type.IsValueType)
            {
                source.Append(indent).AppendLine("if (deserializedResponse is null)");
                source.Append(indent).AppendLine("{");
                source.Append(indent).AppendLine("    throw new global::Newtonsoft.Json.JsonSerializationException(\"The successful response requires a non-null JSON body.\");");
                source.Append(indent).AppendLine("}");
            }

            if (responseValidators.TryGetMethodName(type, out string? validatorName))
            {
                source.Append(indent).Append(validatorName)
                    .Append("(deserializedResponse, \"$\", \"response\", new global::System.Collections.Generic.Dictionary<object, global::System.Collections.Generic.HashSet<int>>(new ")
                    .Append(responseValidators.ComparerName).AppendLine("()));");
            }

            source.Append(indent).AppendLine(resultVariable is null
                ? "return deserializedResponse!;"
                : resultVariable + " = deserializedResponse!;");
        }

        private static void AppendRequestBodyValidation(
            StringBuilder source,
            GeneratedRequestBodyModel? body,
            GeneratedBodyVariantModel? variant)
        {
            if (body is null || variant is null || !body.Required || variant.Type.Nullable || variant.Type.IsValueType)
            {
                return;
            }

            source.Append("            if (").Append(body.ParameterName).AppendLine(" is null)");
            source.AppendLine("            {");
            source.Append("                throw new global::System.ArgumentNullException(nameof(")
                .Append(body.ParameterName).AppendLine("));");
            source.AppendLine("            }");
        }

        // ---- errors ----------------------------------------------------------------------------------

        /// <summary>
        /// Throws the API exception for a non-success response. When the operation declares JSON error
        /// bodies, the body is deserialized for the matching status (exact code, range, default) and
        /// handed to the exception; a failure to deserialize is kept next to the raw body text.
        /// </summary>
        private static void AppendThrowApiException(
            StringBuilder source,
            OpenApiGenerationModel model,
            GeneratedOperationModel operation,
            string bodyExpression,
            BodyHelperNames helpers)
        {
            string indent = "                        ";
            if (operation.ErrorResponses.All(static value => value.Type is null))
            {
                source.Append(indent).Append("throw new ").Append(model.ApiName)
                    .Append("Exception(response.StatusCode, ").Append(bodyExpression).AppendLine(");");
                return;
            }

            source.Append(indent).Append("string oacgErrorBody = ").Append(bodyExpression).AppendLine(";");
            source.Append(indent).AppendLine("object? oacgError = null;");
            source.Append(indent).AppendLine("global::System.Exception? oacgErrorFailure = null;");
            bool first = true;
            foreach (GeneratedErrorResponseModel error in operation.ErrorResponses)
            {
                string call = error.Type is null
                    ? ";"
                    : "oacgError = " + helpers.DeserializeError + "<" +
                      GeneratedSourceEmitter.TypeName(error.Type.WithNullable(false)) +
                      ">(oacgErrorBody, out oacgErrorFailure);";
                if (error.StatusKey == "DEFAULT")
                {
                    if (first)
                    {
                        source.Append(indent).AppendLine(call);
                    }
                    else
                    {
                        source.Append(indent).AppendLine("else");
                        source.Append(indent).AppendLine("{");
                        source.Append(indent).Append("    ").AppendLine(call);
                        source.Append(indent).AppendLine("}");
                    }

                    first = false;
                    continue;
                }

                string condition = error.StatusKey.IndexOf('X') >= 0
                    ? "(int)response.StatusCode >= " + error.StatusKey[0] + "00 && (int)response.StatusCode <= " + error.StatusKey[0] + "99"
                    : "(int)response.StatusCode == " + error.StatusKey;
                source.Append(indent).Append(first ? "if (" : "else if (").Append(condition).AppendLine(")");
                source.Append(indent).AppendLine("{");
                source.Append(indent).Append("    ").AppendLine(call);
                source.Append(indent).AppendLine("}");
                first = false;
            }

            source.Append(indent).Append("throw new ").Append(model.ApiName)
                .AppendLine("Exception(response.StatusCode, oacgErrorBody, oacgError, oacgErrorFailure);");
        }
    }
}
