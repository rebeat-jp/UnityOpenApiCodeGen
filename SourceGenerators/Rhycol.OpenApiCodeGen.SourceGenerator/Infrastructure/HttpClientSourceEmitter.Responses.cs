using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// The advanced response path (P4: G22-G26): text and binary bodies, several media types chosen by
    /// the received Content-Type, a different contract per status (result wrapper), default-only or
    /// omitted responses and typed response headers.
    /// </summary>
    internal sealed partial class HttpClientSourceEmitter
    {
        private void AppendPlannedResponse(
            StringBuilder source,
            OpenApiGenerationModel model,
            GeneratedOperationModel operation,
            GeneratedResponsePlanModel plan,
            MethodMode mode,
            ResponseValidatorPlan validators,
            ResponseJsonContractPlan contracts,
            BodyHelperNames helpers)
        {
            const string Indent = "                    ";
            source.Append(Indent).AppendLine("int oacgStatus = (int)response.StatusCode;");
            source.Append(Indent).Append("if (!(")
                .Append(string.Join(" || ", plan.Successes.Select(static member => CreateMatchExpression(member))))
                .AppendLine("))");
            source.Append(Indent).AppendLine("{");
            AppendThrowApiException(
                source,
                model,
                operation,
                "response.Content == null ? string.Empty : await response.Content.ReadAsStringAsync().ConfigureAwait(false)",
                helpers,
                contracts);
            source.Append(Indent).AppendLine("}");
            if (mode == MethodMode.HttpInfo)
            {
                source.Append(Indent).Append("var oacgHeaders = ").Append(helpers.CollectHeaders).AppendLine("(response);");
            }

            bool endsWithFallback = false;
            foreach (GeneratedSuccessResponseModel member in plan.Successes)
            {
                bool fallback = member.StatusKey == "DEFAULT" || member.StatusKey == "ANY";
                if (fallback)
                {
                    source.Append(Indent).AppendLine("{");
                    endsWithFallback = true;
                }
                else
                {
                    source.Append(Indent).Append("if (").Append(CreateMatchExpression(member)).AppendLine(")");
                    source.Append(Indent).AppendLine("{");
                }

                AppendSuccessMember(source, model, plan, member, mode, validators, contracts, helpers, Indent + "    ");
                source.Append(Indent).AppendLine("}");
            }

            if (!endsWithFallback)
            {
                source.Append(Indent).AppendLine("throw new global::System.InvalidOperationException(\"The response status code is not declared.\");");
            }
        }

        private static string CreateMatchExpression(GeneratedSuccessResponseModel member)
        {
            if (member.StatusKey == "DEFAULT" || member.StatusKey == "ANY")
            {
                return "(oacgStatus >= 200 && oacgStatus <= 299)";
            }

            return member.StatusKey.IndexOf('X') >= 0
                ? "(oacgStatus >= " + member.StatusKey[0] + "00 && oacgStatus <= " + member.StatusKey[0] + "99)"
                : "oacgStatus == " + member.StatusKey;
        }

        private void AppendSuccessMember(
            StringBuilder source,
            OpenApiGenerationModel model,
            GeneratedResponsePlanModel plan,
            GeneratedSuccessResponseModel member,
            MethodMode mode,
            ResponseValidatorPlan validators,
            ResponseJsonContractPlan contracts,
            BodyHelperNames helpers,
            string indent)
        {
            if (member.Type is not null)
            {
                source.Append(indent).Append(_json.TypeName(member.Type)).AppendLine(" oacgData = default!;");
                AppendBodyDecode(source, model, member, validators, contracts, helpers, indent);
            }

            var headerLocals = new List<KeyValuePair<GeneratedResponseHeaderModel, string>>();
            if (mode == MethodMode.HttpInfo)
            {
                for (int index = 0; index < member.Headers.Count; index++)
                {
                    GeneratedResponseHeaderModel header = member.Headers[index];
                    GeneratedResponseHeaderModel property = plan.HttpInfo!.Headers
                        .First(value => string.Equals(value.PropertyName, header.PropertyName, StringComparison.Ordinal));
                    string local = "oacgHeader" + index;
                    headerLocals.Add(new KeyValuePair<GeneratedResponseHeaderModel, string>(header, local));
                    AppendHeaderParse(source, model, header, property, local, helpers, indent);
                }
            }

            string data = plan.Wrapper is not null
                ? "new " + plan.Wrapper.TypeName + "(response.StatusCode, " +
                  GeneratedSourceEmitter.StringLiteral(member.StatusKey) + ", " +
                  (member.Type is null ? "null" : "oacgData") + ")"
                : "oacgData";
            if (mode == MethodMode.HttpInfo)
            {
                source.Append(indent).Append("return new ").Append(plan.HttpInfo!.TypeName).AppendLine();
                source.Append(indent).AppendLine("{");
                source.Append(indent).AppendLine("    StatusCode = response.StatusCode,");
                source.Append(indent).AppendLine("    Headers = oacgHeaders,");
                if (plan.ReturnTypeName is not null)
                {
                    source.Append(indent).Append("    Data = ").Append(data).AppendLine(",");
                }

                foreach (KeyValuePair<GeneratedResponseHeaderModel, string> local in headerLocals)
                {
                    source.Append(indent).Append("    ").Append(local.Key.PropertyName).Append(" = ").Append(local.Value).AppendLine(",");
                }

                source.Append(indent).AppendLine("};");
                return;
            }

            if (plan.ReturnTypeName is null)
            {
                source.Append(indent).AppendLine("return;");
            }
            else
            {
                source.Append(indent).Append("return ").Append(data).AppendLine(";");
            }
        }

        /// <summary>Reads and decodes the body into <c>oacgData</c>, choosing the decoder by the received Content-Type.</summary>
        private void AppendBodyDecode(
            StringBuilder source,
            OpenApiGenerationModel model,
            GeneratedSuccessResponseModel member,
            ResponseValidatorPlan validators,
            ResponseJsonContractPlan contracts,
            BodyHelperNames helpers,
            string indent)
        {
            IReadOnlyList<GeneratedResponseContentModel> contents = member.Contents;
            GeneratedTypeModel type = member.Type!;
            if (contents.Count == 1 && contents[0].Kind == GeneratedResponseBodyKind.Json)
            {
                AppendHandler(source, model, contents[0], type, validators, contracts, helpers, indent);
                return;
            }

            source.Append(indent).Append("string? oacgMediaType = ").Append(helpers.ContentType).AppendLine("(response);");
            if (contents.Count == 1)
            {
                source.Append(indent).Append("if (oacgMediaType != null && !").Append(helpers.Matches).Append("(oacgMediaType, ")
                    .Append(GeneratedSourceEmitter.StringLiteral(contents[0].MediaType)).AppendLine("))");
                source.Append(indent).AppendLine("{");
                source.Append(indent).AppendLine("    throw new " + _json.SerializationExceptionName + "(\"The response Content-Type '\" + oacgMediaType + \"' does not match the declared media type.\");");
                source.Append(indent).AppendLine("}");
                source.Append(indent).AppendLine("{");
                AppendHandler(source, model, contents[0], type, validators, contracts, helpers, indent + "    ");
                source.Append(indent).AppendLine("}");
                return;
            }

            for (int index = 0; index < contents.Count; index++)
            {
                GeneratedResponseContentModel content = contents[index];
                // A response without Content-Type (an empty body) is read with the primary decoder.
                string condition = (index == 0 ? "oacgMediaType == null || " : string.Empty) +
                                   helpers.Matches + "(oacgMediaType!, " +
                                   GeneratedSourceEmitter.StringLiteral(content.MediaType) + ")";

                source.Append(indent).Append(index == 0 ? "if (" : "else if (").Append(condition).AppendLine(")");
                source.Append(indent).AppendLine("{");
                AppendHandler(source, model, content, type, validators, contracts, helpers, indent + "    ");
                source.Append(indent).AppendLine("}");
            }

            source.Append(indent).AppendLine("else");
            source.Append(indent).AppendLine("{");
            source.Append(indent).AppendLine("    throw new " + _json.SerializationExceptionName + "(\"The response Content-Type '\" + oacgMediaType + \"' is not supported by this method.\");");
            source.Append(indent).AppendLine("}");
        }

        private void AppendHandler(
            StringBuilder source,
            OpenApiGenerationModel model,
            GeneratedResponseContentModel content,
            GeneratedTypeModel type,
            ResponseValidatorPlan validators,
            ResponseJsonContractPlan contracts,
            BodyHelperNames helpers,
            string indent)
        {
            switch (content.Kind)
            {
                case GeneratedResponseBodyKind.Json:
                    source.Append(indent).Append(validators.MediaTypeValidatorName)
                        .Append("(response, new string[] { ")
                        .Append(GeneratedSourceEmitter.StringLiteral(content.MediaType)).AppendLine(" });");
                    source.Append(indent).AppendLine("string responseBody = response.Content == null");
                    source.Append(indent).AppendLine("    ? string.Empty");
                    source.Append(indent).AppendLine("    : await ReadJsonResponseBodyAsync(response.Content).ConfigureAwait(false);");
                    AppendJsonDecode(source, type, indent, validators, contracts, "oacgData");
                    break;
                case GeneratedResponseBodyKind.Text:
                    source.Append(indent).AppendLine("string oacgBody = response.Content == null");
                    source.Append(indent).AppendLine("    ? string.Empty");
                    source.Append(indent).AppendLine("    : await ReadJsonResponseBodyAsync(response.Content).ConfigureAwait(false);");
                    source.Append(indent).Append("oacgData = ")
                        .Append(CreateParseExpression(model, type.WithNullable(false), "oacgBody", "response body", helpers))
                        .AppendLine(";");
                    helpers.Constraints.AppendTypedCheck(
                        source, indent, type, "oacgData", "\"$\"", "\"response\"", "null");
                    break;
                default:
                    source.Append(indent).AppendLine("oacgData = response.Content == null");
                    source.Append(indent).AppendLine("    ? global::System.Array.Empty<byte>()");
                    source.Append(indent).AppendLine("    : await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);");
                    break;
            }
        }

        /// <summary>
        /// Parses one header (or text body) value into <paramref name="local"/>: a scalar from its first
        /// value, an array from the comma separated values. A missing required header is an error.
        /// </summary>
        private void AppendHeaderParse(
            StringBuilder source,
            OpenApiGenerationModel model,
            GeneratedResponseHeaderModel header,
            GeneratedResponseHeaderModel property,
            string local,
            BodyHelperNames helpers,
            string indent)
        {
            string wire = GeneratedSourceEmitter.StringLiteral(header.WireName);
            string description = "response header '" + header.WireName + "'";
            source.Append(indent).Append(_json.TypeName(property.Type)).Append(' ').Append(local)
                .AppendLine(" = default!;");
            source.Append(indent).AppendLine("{");
            source.Append(indent).Append("    string[]? oacgValues = ").Append(helpers.GetHeader)
                .Append("(oacgHeaders, ").Append(wire).AppendLine(");");
            source.Append(indent).AppendLine("    if (oacgValues != null)");
            source.Append(indent).AppendLine("    {");
            GeneratedTypeModel type = header.Type.WithNullable(false);
            if (type.Kind == GeneratedTypeKind.Array)
            {
                GeneratedTypeModel item = type.ItemType!.WithNullable(false);
                source.Append(indent).Append("        var oacgItems = new global::System.Collections.Generic.List<")
                    .Append(_json.TypeName(item)).AppendLine(">();");
                source.Append(indent).Append("        foreach (string oacgItem in ").Append(helpers.SplitHeader).AppendLine("(oacgValues))");
                source.Append(indent).AppendLine("        {");
                source.Append(indent).Append("            oacgItems.Add(")
                    .Append(CreateParseExpression(model, item, "oacgItem", description, helpers)).AppendLine(");");
                source.Append(indent).AppendLine("        }");
                source.Append(indent).Append("        ").Append(local).AppendLine(" = oacgItems;");
            }
            else
            {
                source.Append(indent).Append("        ").Append(local).Append(" = ")
                    .Append(CreateParseExpression(model, type, "oacgValues[0]", description, helpers)).AppendLine(";");
            }

            // Validation keywords of the header schema (only when the header is present).
            helpers.Constraints.AppendTypedCheck(
                source, indent + "        ", header.Type, local,
                GeneratedSourceEmitter.StringLiteral("header:" + header.WireName), "\"response\"", "null");

            source.Append(indent).AppendLine("    }");
            if (header.Required)
            {
                source.Append(indent).AppendLine("    else");
                source.Append(indent).AppendLine("    {");
                source.Append(indent).Append("        throw new " + _json.SerializationExceptionName + "(")
                    .Append(GeneratedSourceEmitter.StringLiteral("The required response header '" + header.WireName + "' is missing."))
                    .AppendLine(");");
                source.Append(indent).AppendLine("    }");
            }

            source.Append(indent).AppendLine("}");
        }

        /// <summary>
        /// An expression that converts the string <paramref name="raw"/> to the scalar <paramref name="type"/>
        /// (response headers and text bodies). A malformed value raises a JsonSerializationException.
        /// </summary>
        private string CreateParseExpression(
            OpenApiGenerationModel model,
            GeneratedTypeModel type,
            string raw,
            string description,
            BodyHelperNames helpers)
        {
            string wrap(string typeName, string lambda) =>
                helpers.Parse + "<" + typeName + ">(" + GeneratedSourceEmitter.StringLiteral(description) + ", " + raw +
                ", oacgValue => " + lambda + ")";
            const string Invariant = "global::System.Globalization.CultureInfo.InvariantCulture";
            switch (type.Kind)
            {
                case GeneratedTypeKind.String:
                    return raw;
                case GeneratedTypeKind.Int32:
                    return wrap("int", "int.Parse(oacgValue, global::System.Globalization.NumberStyles.Integer, " + Invariant + ")");
                case GeneratedTypeKind.Int64:
                    return wrap("long", "long.Parse(oacgValue, global::System.Globalization.NumberStyles.Integer, " + Invariant + ")");
                case GeneratedTypeKind.Single:
                    return wrap("float", "float.Parse(oacgValue, global::System.Globalization.NumberStyles.Float, " + Invariant + ")");
                case GeneratedTypeKind.Double:
                    return wrap("double", "double.Parse(oacgValue, global::System.Globalization.NumberStyles.Float, " + Invariant + ")");
                case GeneratedTypeKind.Decimal:
                    return wrap("decimal", "decimal.Parse(oacgValue, global::System.Globalization.NumberStyles.Float, " + Invariant + ")");
                case GeneratedTypeKind.Boolean:
                    return wrap("bool", helpers.ParseBool + "(oacgValue)");
                case GeneratedTypeKind.DateTime:
                    return wrap("global::System.DateTime", "global::System.DateTime.ParseExact(oacgValue, \"yyyy-MM-dd\", " + Invariant + ", global::System.Globalization.DateTimeStyles.None)");
                case GeneratedTypeKind.DateTimeOffset:
                    return wrap("global::System.DateTimeOffset", "global::System.DateTimeOffset.Parse(oacgValue, " + Invariant + ", global::System.Globalization.DateTimeStyles.RoundtripKind)");
                case GeneratedTypeKind.Guid:
                    return wrap("global::System.Guid", "global::System.Guid.Parse(oacgValue)");
                case GeneratedTypeKind.Binary:
                    return wrap("byte[]", "global::System.Convert.FromBase64String(oacgValue)");
                case GeneratedTypeKind.NamedEnum:
                    GeneratedEnumModel? enumModel = model.Enums.FirstOrDefault(
                        value => string.Equals(value.Name, type.Name, StringComparison.Ordinal));
                    if (helpers.Json.IsSystemTextJson)
                    {
                        string textJson = enumModel is not null && enumModel.IsInteger
                            ? "oacgValue"
                            : "global::System.Text.Json.JsonSerializer.Serialize(oacgValue)";
                        return wrap(
                            type.Name,
                            "global::System.Text.Json.JsonSerializer.Deserialize<" + type.Name + ">(" +
                            textJson + ", " + helpers.JsonOptions + ")");
                    }

                    string json = enumModel is not null && enumModel.IsInteger
                        ? "oacgValue"
                        : "global::Newtonsoft.Json.JsonConvert.ToString(oacgValue)";
                    return wrap(type.Name, "global::Newtonsoft.Json.JsonConvert.DeserializeObject<" + type.Name + ">(" + json + ")");
                default:
                    throw new InvalidOperationException(
                        "The type '" + type.Kind + "' has no text form for " + description + ".");
            }
        }
    }
}
