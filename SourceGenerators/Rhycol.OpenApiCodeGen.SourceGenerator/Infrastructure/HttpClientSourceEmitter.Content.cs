using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// Request content (json, form-urlencoded, multipart, text, binary), the Accept header and the
    /// generated helpers for typed errors, content negotiation and response headers (P4: G21-G26).
    /// </summary>
    internal sealed partial class HttpClientSourceEmitter
    {
        /// <summary>
        /// Names of the generated private helpers. They share one prefix that no operation, parameter or
        /// generated type starts with.
        /// </summary>
        private sealed class BodyHelperNames
        {
            internal BodyHelperNames(OpenApiGenerationModel model)
            {
                Model = model;
                Prefix = HttpClientParameterSerializationEmitter.CreatePrefix(model, "__OacgBody_");
                UsesErrors = model.Operations.Any(static operation => operation.ErrorResponses.Count > 0);
                UsesPlans = model.Operations.Any(static operation => operation.ResponsePlan is not null);
                UsesHeaders = model.Operations.Any(static operation => operation.ResponsePlan?.HttpInfo is not null);
                UsesParse = UsesHeaders || model.Operations.Any(static operation =>
                    operation.ResponsePlan is not null &&
                    operation.ResponsePlan.Successes.Any(static member =>
                        member.Contents.Any(static content => content.Kind == GeneratedResponseBodyKind.Text) &&
                        member.Type is not null &&
                        member.Type.Kind != GeneratedTypeKind.String));
            }

            internal bool UsesMultipart => Model.Operations.Any(static operation => operation.RequestBody is not null &&
                operation.RequestBody.Variants.Any(static variant => variant.Kind == GeneratedBodyKind.Multipart));

            internal string Disposition => Prefix + "Disposition";

            internal OpenApiGenerationModel Model { get; }

            internal string Prefix { get; }

            internal bool UsesErrors { get; }

            internal bool UsesPlans { get; }

            internal bool UsesHeaders { get; }

            internal bool UsesParse { get; }

            internal string DeserializeError => Prefix + "DeserializeError";

            internal string ContentType => Prefix + "ContentType";

            internal string Matches => Prefix + "Matches";

            internal string CollectHeaders => Prefix + "CollectHeaders";

            internal string GetHeader => Prefix + "GetHeader";

            internal string SplitHeader => Prefix + "SplitHeader";

            internal string Parse => Prefix + "Parse";

            internal string ParseBool => Prefix + "ParseBool";
        }

        // ---- request content -------------------------------------------------------------------------

        private static void AppendRequestBody(
            StringBuilder source,
            GeneratedRequestBodyModel body,
            GeneratedBodyVariantModel variant,
            ResponseValidatorPlan validators,
            HttpClientParameterSerializationEmitter parameters,
            BodyHelperNames helpers)
        {
            if (!body.Required)
            {
                source.Append("                if (").Append(body.ParameterName).Append(" != null");
                if (variant.SpecifiedParameterName is not null)
                {
                    source.Append(" || ").Append(variant.SpecifiedParameterName);
                }

                source.AppendLine(")");
                source.AppendLine("                {");
            }

            string indent = body.Required ? "                " : "                    ";
            switch (variant.Kind)
            {
                case GeneratedBodyKind.Json:
                    AppendJsonRequestBody(source, body, variant, validators, indent);
                    break;
                case GeneratedBodyKind.Text:
                    source.Append(indent).Append("request.Content = CreateJsonContent(ConvertToString(")
                        .Append(body.ParameterName).Append("), ")
                        .Append(GeneratedSourceEmitter.StringLiteral(variant.MediaType)).AppendLine(");");
                    break;
                case GeneratedBodyKind.Binary:
                    AppendBinaryRequestBody(source, body, variant, indent);
                    break;
                case GeneratedBodyKind.FormUrlEncoded:
                    AppendFormRequestBody(source, body, variant, parameters, indent);
                    break;
                default:
                    AppendMultipartRequestBody(source, body, variant, validators, helpers, indent);
                    break;
            }

            if (!body.Required)
            {
                source.AppendLine("                }");
            }
        }

        private static void AppendJsonRequestBody(
            StringBuilder source,
            GeneratedRequestBodyModel body,
            GeneratedBodyVariantModel variant,
            ResponseValidatorPlan validators,
            string indent)
        {
            if (validators.TryGetMethodName(variant.Type, out string? validatorName))
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
                .Append(GeneratedSourceEmitter.StringLiteral(variant.MediaType))
                .AppendLine(");");
        }

        private static void AppendBinaryRequestBody(
            StringBuilder source,
            GeneratedRequestBodyModel body,
            GeneratedBodyVariantModel variant,
            string indent)
        {
            string contentType = variant.ContentTypeParameterName is string parameter
                ? "(" + parameter + " ?? " + GeneratedSourceEmitter.StringLiteral(variant.MediaType) + ")"
                : GeneratedSourceEmitter.StringLiteral(variant.MediaType);
            source.Append(indent).AppendLine("{");
            source.Append(indent).Append("    var oacgContent = new global::System.Net.Http.ByteArrayContent(")
                .Append(body.ParameterName).AppendLine(");");
            source.Append(indent).Append("    oacgContent.Headers.ContentType = global::System.Net.Http.Headers.MediaTypeHeaderValue.Parse(")
                .Append(contentType).AppendLine(");");
            source.Append(indent).AppendLine("    request.Content = oacgContent;");
            source.Append(indent).AppendLine("}");
        }

        // application/x-www-form-urlencoded: every field is serialized with the query form style rules (the
        // style / explode / allowReserved of its Encoding Object) and the fields are joined with '&'.
        // Characters are percent-encoded (a space is %20, not '+').
        private static void AppendFormRequestBody(
            StringBuilder source,
            GeneratedRequestBodyModel body,
            GeneratedBodyVariantModel variant,
            HttpClientParameterSerializationEmitter parameters,
            string indent)
        {
            source.Append(indent).AppendLine("{");
            source.Append(indent).AppendLine("    var oacgItems = new global::System.Collections.Generic.List<string>();");
            foreach (GeneratedFormFieldModel field in variant.Fields)
            {
                if (field.Role == GeneratedFormFieldRole.Text && field.StyleParameter is not null)
                {
                    parameters.AppendFormField(source, field.StyleParameter, indent.Length + 4, "oacgItems");
                }
                else if (field.Role == GeneratedFormFieldRole.Json)
                {
                    parameters.AppendFormJsonField(
                        source, field, body.ParameterName + "." + field.MemberName, indent.Length + 4, "oacgItems");
                }
                else
                {
                    throw new InvalidOperationException(
                        "The form field '" + field.WireName + "' is a file; files require multipart/form-data.");
                }
            }

            source.Append(indent).AppendLine("    var oacgContent = new global::System.Net.Http.StringContent(string.Join(\"&\", oacgItems), global::System.Text.Encoding.UTF8);");
            source.Append(indent).AppendLine("    oacgContent.Headers.ContentType = new global::System.Net.Http.Headers.MediaTypeHeaderValue(\"application/x-www-form-urlencoded\");");
            source.Append(indent).AppendLine("    request.Content = oacgContent;");
            source.Append(indent).AppendLine("}");
        }

        private static void AppendMultipartRequestBody(
            StringBuilder source,
            GeneratedRequestBodyModel body,
            GeneratedBodyVariantModel variant,
            ResponseValidatorPlan validators,
            BodyHelperNames helpers,
            string indent)
        {
            source.Append(indent).AppendLine("{");
            source.Append(indent).AppendLine("    var oacgMultipart = new global::System.Net.Http.MultipartFormDataContent();");
            foreach (GeneratedFormFieldModel field in variant.Fields)
            {
                AppendMultipartField(source, body, field, validators, helpers, indent + "    ");
            }

            source.Append(indent).AppendLine("    request.Content = oacgMultipart;");
            source.Append(indent).AppendLine("}");
        }

        private static void AppendMultipartField(
            StringBuilder source,
            GeneratedRequestBodyModel body,
            GeneratedFormFieldModel field,
            ResponseValidatorPlan validators,
            BodyHelperNames helpers,
            string indent)
        {
            string member = body.ParameterName + "." + field.MemberName;
            string wire = GeneratedSourceEmitter.StringLiteral(field.WireName);
            bool isOptional = !field.Required;
            string? presence = !isOptional
                ? null
                : field.Type.IsValueType ? member + ".HasValue" : member + " != null";
            source.Append(indent).AppendLine("{");
            string inner = indent + "    ";
            if (presence is not null)
            {
                source.Append(inner).Append("if (").Append(presence).AppendLine(")");
                source.Append(inner).AppendLine("{");
                inner += "    ";
            }
            else if (!field.Type.IsValueType)
            {
                source.Append(inner).Append("if (").Append(member).AppendLine(" == null)");
                source.Append(inner).AppendLine("{");
                source.Append(inner).Append("    throw new global::System.ArgumentException(")
                    .Append(GeneratedSourceEmitter.StringLiteral("The required part '" + field.WireName + "' must not be null."))
                    .Append(", nameof(").Append(body.ParameterName).AppendLine("));");
                source.Append(inner).AppendLine("}");
            }

            switch (field.Role)
            {
                case GeneratedFormFieldRole.Text:
                    if (field.Type.Kind == GeneratedTypeKind.Array)
                    {
                        source.Append(inner).Append("foreach (var oacgItem in ").Append(member).AppendLine(")");
                        source.Append(inner).AppendLine("{");
                        if (!field.Type.ItemType!.IsValueType || field.Type.ItemType.Nullable)
                        {
                            source.Append(inner).AppendLine("    if (oacgItem == null)");
                            source.Append(inner).AppendLine("    {");
                            source.Append(inner).Append("        throw new global::System.ArgumentException(")
                                .Append(GeneratedSourceEmitter.StringLiteral("An array element of the part '" + field.WireName + "' must not be null."))
                                .Append(", nameof(").Append(body.ParameterName).AppendLine("));");
                            source.Append(inner).AppendLine("    }");
                        }

                        AppendStringPart(source, field, "ConvertToString(oacgItem)", wire, inner + "    ", helpers.Disposition);
                        source.Append(inner).AppendLine("}");
                    }
                    else
                    {
                        AppendStringPart(source, field, "ConvertToString(" + member + ")", wire, inner, helpers.Disposition);
                    }

                    break;
                case GeneratedFormFieldRole.Json:
                    AppendStringPart(
                        source, field, validators.RequestJsonSerializerName + "(" + member + ")", wire, inner, helpers.Disposition);
                    break;
                case GeneratedFormFieldRole.File:
                    AppendFilePart(source, field, member, wire, inner, helpers.Disposition);
                    break;
                default:
                    source.Append(inner).Append("foreach (var oacgItem in ").Append(member).AppendLine(")");
                    source.Append(inner).AppendLine("{");
                    source.Append(inner).AppendLine("    if (oacgItem == null)");
                    source.Append(inner).AppendLine("    {");
                    source.Append(inner).Append("        throw new global::System.ArgumentException(")
                        .Append(GeneratedSourceEmitter.StringLiteral("A file of the part '" + field.WireName + "' must not be null."))
                        .Append(", nameof(").Append(body.ParameterName).AppendLine("));");
                    source.Append(inner).AppendLine("    }");
                    AppendFilePart(source, field, "oacgItem", wire, inner + "    ", helpers.Disposition);
                    source.Append(inner).AppendLine("}");
                    break;
            }

            if (presence is not null)
            {
                source.Append(indent).AppendLine("    }");
            }

            source.Append(indent).AppendLine("}");
        }

        private static void AppendPartHeaders(StringBuilder source, GeneratedFormFieldModel field, string indent)
        {
            if (field.ContentType.Length > 0)
            {
                source.Append(indent).Append("oacgPart.Headers.ContentType = global::System.Net.Http.Headers.MediaTypeHeaderValue.Parse(")
                    .Append(GeneratedSourceEmitter.StringLiteral(field.ContentType)).AppendLine(");");
            }

            foreach (KeyValuePair<string, string> header in field.Headers)
            {
                source.Append(indent).Append("oacgPart.Headers.TryAddWithoutValidation(")
                    .Append(GeneratedSourceEmitter.StringLiteral(header.Key)).Append(", ")
                    .Append(GeneratedSourceEmitter.StringLiteral(header.Value)).AppendLine(");");
            }
        }

        private static void AppendStringPart(
            StringBuilder source,
            GeneratedFormFieldModel field,
            string valueExpression,
            string wire,
            string indent,
            string disposition)
        {
            source.Append(indent).Append("var oacgPart = new global::System.Net.Http.StringContent(")
                .Append(valueExpression).AppendLine(", global::System.Text.Encoding.UTF8);");
            AppendPartHeaders(source, field, indent);
            source.Append(indent).Append("oacgPart.Headers.TryAddWithoutValidation(\"Content-Disposition\", ").Append(disposition)
                .Append("(").Append(wire).AppendLine(", null));");
            source.Append(indent).AppendLine("oacgMultipart.Add(oacgPart);");
        }

        private static void AppendFilePart(
            StringBuilder source,
            GeneratedFormFieldModel field,
            string fileExpression,
            string wire,
            string indent,
            string disposition)
        {
            // The part content type is the caller's (FileParameter.ContentType), else the declared / default one.
            source.Append(indent).Append("var oacgPart = ").Append(fileExpression).Append(".CreateContent(")
                .Append(GeneratedSourceEmitter.StringLiteral(
                    field.ContentType.Length > 0 ? field.ContentType : "application/octet-stream"))
                .AppendLine(");");
            foreach (KeyValuePair<string, string> header in field.Headers)
            {
                source.Append(indent).Append("oacgPart.Headers.TryAddWithoutValidation(")
                    .Append(GeneratedSourceEmitter.StringLiteral(header.Key)).Append(", ")
                    .Append(GeneratedSourceEmitter.StringLiteral(header.Value)).AppendLine(");");
            }

            source.Append(indent).Append("oacgPart.Headers.TryAddWithoutValidation(\"Content-Disposition\", ").Append(disposition)
                .Append("(").Append(wire).Append(", ").Append(fileExpression).AppendLine(".FileName));");
            source.Append(indent).AppendLine("oacgMultipart.Add(oacgPart);");
        }

        // ---- Accept ----------------------------------------------------------------------------------

        // The primary media type is requested first; the other declared media types follow with a
        // decreasing quality value.
        private static void AppendAcceptHeader(StringBuilder source, GeneratedResponsePlanModel plan)
        {
            if (plan.AcceptMediaTypes.Count == 0)
            {
                return;
            }

            var values = new List<string>();
            for (int index = 0; index < plan.AcceptMediaTypes.Count; index++)
            {
                string mediaType = plan.AcceptMediaTypes[index];
                int parameters = mediaType.IndexOf(';');
                if (parameters >= 0)
                {
                    mediaType = mediaType.Substring(0, parameters);
                }

                values.Add(index == 0
                    ? mediaType
                    : mediaType + ";q=" + FormatQuality(index));
            }

            source.Append("                if (!request.Headers.TryAddWithoutValidation(\"Accept\", ")
                .Append(GeneratedSourceEmitter.StringLiteral(string.Join(", ", values))).AppendLine("))");
            source.AppendLine("                {");
            source.AppendLine("                    throw new global::System.InvalidOperationException(\"Unable to add the request header 'Accept'.\");");
            source.AppendLine("                }");
        }

        // Quality values: the first media type has the implicit q=1.0; the n-th (0-based) gets 1.0 - 0.1 * n,
        // never below 0.1, written with the invariant culture ("0.9", "0.8", ... "0.1").
        private static string FormatQuality(int index)
        {
            int tenths = Math.Max(1, 10 - index);
            return (tenths / 10.0).ToString("0.0", CultureInfo.InvariantCulture);
        }

        // ---- generated helpers -----------------------------------------------------------------------

        private static void AppendBodyHelpers(StringBuilder source, OpenApiGenerationModel model, BodyHelperNames names)
        {
            if (names.UsesErrors)
            {
                source.AppendLine(@"
        private static object? __DESERIALIZE__<T>(string json, out global::System.Exception? failure)
        {
            failure = null;
            if (global::System.String.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            try
            {
                var defaultSettings = global::Newtonsoft.Json.JsonConvert.DefaultSettings?.Invoke();
                var serializer = global::Newtonsoft.Json.JsonSerializer.Create();
                serializer.DateParseHandling = global::Newtonsoft.Json.DateParseHandling.None;
                serializer.MaxDepth = defaultSettings?.MaxDepth > 0 ? defaultSettings.MaxDepth : 64;
                using (var textReader = new global::System.IO.StringReader(json))
                using (var reader = new global::Newtonsoft.Json.JsonTextReader(textReader) { DateParseHandling = global::Newtonsoft.Json.DateParseHandling.None, MaxDepth = serializer.MaxDepth })
                {
                    return serializer.Deserialize<T>(reader);
                }
            }
            catch (global::System.Exception exception) when (exception is global::Newtonsoft.Json.JsonException ||
                exception is global::System.FormatException || exception is global::System.InvalidCastException ||
                exception is global::System.OverflowException || exception is global::System.ArgumentException)
            {
                failure = exception;
                return null;
            }
        }
".Replace("__DESERIALIZE__", names.DeserializeError));
            }

            if (names.UsesPlans)
            {
                source.AppendLine(@"
        private static string? __CONTENTTYPE__(global::System.Net.Http.HttpResponseMessage response)
        {
            string? mediaType = response.Content?.Headers.ContentType?.MediaType;
            return mediaType == null ? null : mediaType.Trim().ToLowerInvariant();
        }

        private static bool __MATCHES__(string actual, string declared)
        {
            int separator = declared.IndexOf(';');
            if (separator >= 0)
            {
                declared = declared.Substring(0, separator);
            }
            declared = declared.Trim().ToLowerInvariant();
            int slash = declared.IndexOf('/');
            int actualSlash = actual.IndexOf('/');
            if (slash <= 0 || actualSlash <= 0)
            {
                return false;
            }
            string type = declared.Substring(0, slash);
            string subtype = declared.Substring(slash + 1);
            if (type != ""*"" && !global::System.String.Equals(type, actual.Substring(0, actualSlash), global::System.StringComparison.Ordinal))
            {
                return false;
            }
            string actualSubtype = actual.Substring(actualSlash + 1);
            if (subtype == ""*"")
            {
                return true;
            }
            if (subtype.StartsWith(""*+"", global::System.StringComparison.Ordinal))
            {
                return actualSubtype.EndsWith(subtype.Substring(1), global::System.StringComparison.Ordinal);
            }
            return global::System.String.Equals(subtype, actualSubtype, global::System.StringComparison.Ordinal);
        }
".Replace("__CONTENTTYPE__", names.ContentType).Replace("__MATCHES__", names.Matches));
            }

            if (names.UsesParse)
            {
                source.AppendLine(@"
        private static T __PARSE__<T>(string description, string raw, global::System.Func<string, T> parse)
        {
            try
            {
                return parse(raw);
            }
            catch (global::System.Exception exception) when (exception is global::Newtonsoft.Json.JsonException ||
                exception is global::System.FormatException || exception is global::System.InvalidCastException ||
                exception is global::System.OverflowException || exception is global::System.ArgumentException)
            {
                throw new global::Newtonsoft.Json.JsonSerializationException(""The "" + description + "" has an invalid value."", exception);
            }
        }

        private static bool __PARSEBOOL__(string raw)
        {
            if (global::System.String.Equals(raw, ""true"", global::System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (global::System.String.Equals(raw, ""false"", global::System.StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            throw new global::System.FormatException(""A boolean must be true or false."");
        }
".Replace("__PARSE__", names.Parse).Replace("__PARSEBOOL__", names.ParseBool));
            }

            if (names.UsesMultipart)
            {
                // RFC 7578 Content-Disposition built here instead of by MultipartFormDataContent: name and filename are
                // quoted with '"' and '\\' escaped, CR / LF are rejected, and a non-ASCII name is percent-encoded UTF-8 while a
                // non-ASCII filename gets an ASCII fallback plus filename*=UTF-8''<percent-encoded> (RFC 8187).
                source.AppendLine(@"
        private static string __DISP__(string name, string? fileName)
        {
            if (name.IndexOf('\r') >= 0 || name.IndexOf('\n') >= 0 || (fileName != null && (fileName.IndexOf('\r') >= 0 || fileName.IndexOf('\n') >= 0)))
            {
                throw new global::System.ArgumentException(""A multipart part name or file name must not contain CR or LF."");
            }
            var builder = new global::System.Text.StringBuilder(""form-data; name=\"""");
            for (int index = 0; index < name.Length; index++)
            {
                char character = name[index];
                if (character > (char)126 || character < (char)32)
                {
                    // A surrogate pair is encoded as one code point (4 UTF-8 octets), not as two replacement characters.
                    int length = char.IsHighSurrogate(character) && index + 1 < name.Length && char.IsLowSurrogate(name[index + 1]) ? 2 : 1;
                    string scalar = name.Substring(index, length);
                    index += length - 1;
                    foreach (byte octet in global::System.Text.Encoding.UTF8.GetBytes(scalar))
                    {
                        builder.Append('%').Append(octet.ToString(""X2"", global::System.Globalization.CultureInfo.InvariantCulture));
                    }
                }
                else
                {
                    if (character == '""' || character == '\\') builder.Append('\\');
                    builder.Append(character);
                }
            }
            builder.Append('""');
            if (fileName != null)
            {
                bool ascii = true;
                builder.Append(""; filename=\"""");
                for (int index = 0; index < fileName.Length; index++)
                {
                    char character = fileName[index];
                    if (character > (char)126 || character < (char)32)
                    {
                        // One '_' per code point: skip the low half of a surrogate pair.
                        if (char.IsHighSurrogate(character) && index + 1 < fileName.Length && char.IsLowSurrogate(fileName[index + 1])) index++;
                        ascii = false;
                        builder.Append('_');
                        continue;
                    }
                    if (character == '""' || character == '\\') builder.Append('\\');
                    builder.Append(character);
                }
                builder.Append('""');
                if (!ascii)
                {
                    builder.Append(""; filename*=UTF-8''"");
                    foreach (byte octet in global::System.Text.Encoding.UTF8.GetBytes(fileName))
                    {
                        char value = (char)octet;
                        if ((value >= 'a' && value <= 'z') || (value >= 'A' && value <= 'Z') || (value >= '0' && value <= '9') || ""!#$&+-.^_`|~"".IndexOf(value) >= 0)
                        {
                            builder.Append(value);
                        }
                        else
                        {
                            builder.Append('%').Append(octet.ToString(""X2"", global::System.Globalization.CultureInfo.InvariantCulture));
                        }
                    }
                }
            }
            return builder.ToString();
        }
".Replace("__DISP__", names.Disposition));
            }

            if (names.UsesHeaders)
            {
                source.AppendLine(@"
        private static global::System.Collections.Generic.IReadOnlyDictionary<string, string[]> __COLLECT__(global::System.Net.Http.HttpResponseMessage response)
        {
            var headers = new global::System.Collections.Generic.Dictionary<string, string[]>(global::System.StringComparer.OrdinalIgnoreCase);
            foreach (var header in response.Headers)
            {
                headers[header.Key] = global::System.Linq.Enumerable.ToArray(header.Value);
            }
            if (response.Content != null)
            {
                foreach (var header in response.Content.Headers)
                {
                    headers[header.Key] = global::System.Linq.Enumerable.ToArray(header.Value);
                }
            }
            return headers;
        }

        private static string[]? __GETHEADER__(global::System.Collections.Generic.IReadOnlyDictionary<string, string[]> headers, string name)
        {
            return headers.TryGetValue(name, out string[]? values) && values.Length > 0 ? values : null;
        }

        private static global::System.Collections.Generic.List<string> __SPLIT__(string[] values)
        {
            var items = new global::System.Collections.Generic.List<string>();
            foreach (string value in values)
            {
                foreach (string part in value.Split(','))
                {
                    string trimmed = part.Trim();
                    if (trimmed.Length > 0)
                    {
                        items.Add(trimmed);
                    }
                }
            }
            return items;
        }
".Replace("__COLLECT__", names.CollectHeaders).Replace("__GETHEADER__", names.GetHeader).Replace("__SPLIT__", names.SplitHeader));
            }
        }
    }
}
