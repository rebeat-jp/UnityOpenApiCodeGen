using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// Emits the request-side serialization of operation parameters: path / query / header / cookie
    /// parameters in every OpenAPI style and explode combination, allowReserved, and
    /// content (JSON) parameters.
    ///
    /// Plain scalar parameters in the default style keep the original inline code (they are a
    /// large share of real specs and their output must not change). Everything else goes through
    /// one generated static helper, so the generated client stays small.
    /// </summary>
    internal sealed partial class HttpClientParameterSerializationEmitter
    {
        private const int KindScalar = 0;
        private const int KindArray = 1;
        private const int KindObject = 2;

        private readonly OpenApiGenerationModel _model;
        private readonly IReadOnlyDictionary<string, GeneratedDtoModel> _dtos;
        private readonly string _prefix;
        private readonly string _jsonSerializerName;
        private readonly string _headerValidatorName;

        internal HttpClientParameterSerializationEmitter(
            OpenApiGenerationModel model,
            string jsonSerializerName,
            string headerValidatorName,
            ConstraintValidationEmitter constraints)
        {
            Constraints = constraints;
            _model = model;
            _jsonSerializerName = jsonSerializerName;
            _headerValidatorName = headerValidatorName;
            _dtos = model.Dtos.ToDictionary(static dto => dto.Name, StringComparer.Ordinal);
            _prefix = CreatePrefix(model, "__OacgParam_");
            Security = new SecurityEmitter(model, headerValidatorName);
        }

        /// <summary>The security (authentication) emitter of the same client.</summary>
        internal SecurityEmitter Security { get; }

        /// <summary>The validation keyword (minLength, pattern, ...) emitter of the same client.</summary>
        internal ConstraintValidationEmitter Constraints { get; }

        private string SerializeName => _prefix + "Serialize";

        private string EncodeName => _prefix + "Encode";

        private string ItemName => _prefix + "Item";

        private string ExtraName => _prefix + "Extra";

        // Local variable names carry the prefix so they never collide with operation parameters.
        private string ResultVar => _prefix + "result";

        private string JsonVar => _prefix + "json";

        private string ItemsVar => _prefix + "items";

        private string ItemVar => _prefix + "item";

        private string PairsVar => _prefix + "pairs";

        private string PairVar => _prefix + "pair";

        /// <summary>True when any parameter is serialized through the generated helper.</summary>
        internal bool UsesHelpers =>
            _model.Operations.Any(operation => operation.Parameters.Any(UsesHelper)) ||
            _model.Operations.Any(static operation => operation.RequestBody is not null &&
                operation.RequestBody.Variants.Any(static variant => variant.Kind == GeneratedBodyKind.FormUrlEncoded));

        internal bool HasCookieParameters(GeneratedOperationModel operation)
        {
            return operation.Parameters.Any(static value => value.LocationName == "cookie");
        }

        internal static string CreatePrefix(OpenApiGenerationModel model, string seed)
        {
            var occupied = new HashSet<string>(StringComparer.Ordinal) { model.ApiName };
            foreach (GeneratedOperationModel operation in model.Operations)
            {
                occupied.Add(operation.Name);
                foreach (GeneratedParameterModel parameter in operation.Parameters)
                {
                    occupied.Add(parameter.Name);
                }

                if (operation.RequestBody is GeneratedRequestBodyModel body)
                {
                    occupied.Add(body.ParameterName);
                    if (body.SpecifiedParameterName is string specified)
                    {
                        occupied.Add(specified);
                    }
                }
            }

            foreach (GeneratedDtoModel dto in model.Dtos)
            {
                occupied.Add(dto.Name);
            }

            foreach (GeneratedEnumModel generatedEnum in model.Enums)
            {
                occupied.Add(generatedEnum.Name);
            }

            foreach (GeneratedUnionModel union in model.Unions)
            {
                occupied.Add(union.Name);
            }

            string prefix = seed;
            while (occupied.Any(name => name.StartsWith(prefix, StringComparison.Ordinal)))
            {
                prefix += "_";
            }

            return prefix;
        }

        // ---- classification --------------------------------------------------------------------------

        private static bool IsScalarKind(GeneratedTypeModel type)
        {
            switch (type.Kind)
            {
                case GeneratedTypeKind.Array:
                case GeneratedTypeKind.Map:
                case GeneratedTypeKind.Named:
                case GeneratedTypeKind.Any:
                case GeneratedTypeKind.FreeObject:
                case GeneratedTypeKind.Union:
                    return false;
                default:
                    return true;
            }
        }

        private static int GetKind(GeneratedTypeModel type)
        {
            switch (type.Kind)
            {
                case GeneratedTypeKind.Array:
                    return KindArray;
                case GeneratedTypeKind.Map:
                case GeneratedTypeKind.Named:
                    return KindObject;
                default:
                    return KindScalar;
            }
        }

        private static bool IsDefaultInline(GeneratedParameterModel parameter)
        {
            if (parameter.IsContent || parameter.AllowReserved || !IsScalarKind(parameter.Type))
            {
                return false;
            }

            switch (parameter.LocationName)
            {
                case "path":
                case "header":
                    return parameter.Style == "simple";
                case "query":
                    return parameter.Style == "form";
                default:
                    return false;
            }
        }

        private static bool UsesHelper(GeneratedParameterModel parameter)
        {
            return !IsDefaultInline(parameter);
        }

        // ---- statements ------------------------------------------------------------------------------

        internal void AppendPathParameters(StringBuilder source, GeneratedOperationModel operation)
        {
            foreach (GeneratedParameterModel parameter in operation.Parameters.Where(
                         static value => value.LocationName == "path"))
            {
                string placeholder = GeneratedSourceEmitter.StringLiteral("{" + parameter.WireName + "}");
                if (IsDefaultInline(parameter))
                {
                    source.Append("            relativePath = relativePath.Replace(")
                        .Append(placeholder)
                        .Append(", global::System.Uri.EscapeDataString(ConvertToString(")
                        .Append(HttpClientSourceEmitter.GetValueExpression(parameter))
                        .AppendLine(")));");
                    continue;
                }

                source.AppendLine("            {");
                AppendSerializedValue(source, parameter, 16, ResultVar);
                source.Append("                relativePath = relativePath.Replace(")
                    .Append(placeholder)
                    .Append(", ").Append(ResultVar).AppendLine(" ?? string.Empty);");
                source.AppendLine("            }");
            }
        }

        internal void AppendQueryParameters(StringBuilder source, GeneratedOperationModel operation)
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
                if (IsDefaultInline(parameter))
                {
                    string statement = "queryParts.Add(global::System.Uri.EscapeDataString(" +
                                       GeneratedSourceEmitter.StringLiteral(parameter.WireName) +
                                       ") + \"=\" + global::System.Uri.EscapeDataString(ConvertToString(" +
                                       HttpClientSourceEmitter.GetValueExpression(parameter) + ")));";
                    HttpClientSourceEmitter.AppendConditionalStatement(source, parameter, statement, 12);
                    continue;
                }

                AppendGuarded(source, parameter, 12, (indent) =>
                {
                    AppendSerializedValue(source, parameter, indent, ResultVar);
                    source.Append(' ', indent).AppendLine("if (" + ResultVar + " != null && " + ResultVar + ".Length > 0)");
                    source.Append(' ', indent).AppendLine("{");
                    source.Append(' ', indent + 4).AppendLine("queryParts.Add(" + ResultVar + ");");
                    source.Append(' ', indent).AppendLine("}");
                });
            }

            source.AppendLine("            if (queryParts.Count > 0)");
            source.AppendLine("            {");
            source.AppendLine("                relativePath += (relativePath.IndexOf('?') >= 0 ? \"&\" : \"?\") + string.Join(\"&\", queryParts);");
            source.AppendLine("            }");
        }

        internal void AppendHeaderAndCookieParameters(StringBuilder source, GeneratedOperationModel operation)
        {
            foreach (GeneratedParameterModel parameter in operation.Parameters.Where(
                         static value => value.LocationName == "header"))
            {
                string wire = GeneratedSourceEmitter.StringLiteral(parameter.WireName);
                string failure = "throw new global::System.InvalidOperationException(" +
                                 GeneratedSourceEmitter.StringLiteral(
                                     "Unable to add request header '" + parameter.WireName + "'.") + ");";
                if (IsDefaultInline(parameter))
                {
                    string statement = "if (!request.Headers.TryAddWithoutValidation(" + wire +
                                       ", " + _headerValidatorName + "(ConvertToString(" +
                                       HttpClientSourceEmitter.GetValueExpression(parameter) + "), " + wire +
                                       "))) " + failure;
                    HttpClientSourceEmitter.AppendConditionalStatement(source, parameter, statement, 16);
                    continue;
                }

                AppendGuarded(source, parameter, 16, (indent) =>
                {
                    AppendSerializedValue(source, parameter, indent, ResultVar);
                    source.Append(' ', indent).AppendLine("if (" + ResultVar + " != null)");
                    source.Append(' ', indent).AppendLine("{");
                    source.Append(' ', indent + 4)
                        .Append("if (!request.Headers.TryAddWithoutValidation(").Append(wire).Append(", ")
                        .Append(_headerValidatorName).Append('(').Append(ResultVar).Append(", ").Append(wire).Append("))) ")
                        .AppendLine(failure);
                    source.Append(' ', indent).AppendLine("}");
                });
            }

            GeneratedParameterModel[] cookieParameters = operation.Parameters
                .Where(static value => value.LocationName == "cookie")
                .ToArray();
            if (cookieParameters.Length == 0 && !Security.UsesCookie(operation))
            {
                return;
            }

            source.AppendLine("                var cookieParts = new global::System.Collections.Generic.List<string>();");
            foreach (GeneratedParameterModel parameter in cookieParameters)
            {
                AppendGuarded(source, parameter, 16, (indent) =>
                {
                    AppendSerializedValue(source, parameter, indent, ResultVar);
                    source.Append(' ', indent).AppendLine("if (" + ResultVar + " != null)");
                    source.Append(' ', indent).AppendLine("{");
                    source.Append(' ', indent + 4).AppendLine("cookieParts.Add(" + ResultVar + ");");
                    source.Append(' ', indent).AppendLine("}");
                });
            }

            if (Security.UsesCookie(operation))
            {
                source.AppendLine("                foreach (var oacgItem in " + SecurityEmitter.LocalName + ".Cookies)");
                source.AppendLine("                {");
                source.AppendLine("                    cookieParts.RemoveAll(oacgPart => oacgPart.StartsWith(oacgItem.Substring(0, oacgItem.IndexOf('=') + 1), global::System.StringComparison.Ordinal));");
                source.AppendLine("                    cookieParts.Add(oacgItem);");
                source.AppendLine("                }");
            }

            source.AppendLine("                if (cookieParts.Count > 0 && !request.Headers.TryAddWithoutValidation(\"Cookie\", string.Join(\"; \", cookieParts)))");
            source.AppendLine("                {");
            source.AppendLine("                    throw new global::System.InvalidOperationException(\"Unable to add the request header 'Cookie'.\");");
            source.AppendLine("                }");
        }

        /// <summary>Wraps a body in the presence check of an optional parameter, inside its own scope.</summary>
        private static void AppendGuarded(
            StringBuilder source,
            GeneratedParameterModel parameter,
            int indentation,
            Action<int> appendBody)
        {
            string? condition = HttpClientSourceEmitter.GetPresenceCondition(parameter);
            if (condition is not null)
            {
                source.Append(' ', indentation).Append("if (").Append(condition).AppendLine(")");
            }

            source.Append(' ', indentation).AppendLine("{");
            appendBody(indentation + 4);
            source.Append(' ', indentation).AppendLine("}");
        }

        /// <summary>
        /// Declares <c>string? resultName</c> holding the serialized parameter (null = send nothing).
        /// </summary>
        private void AppendSerializedValue(
            StringBuilder source,
            GeneratedParameterModel parameter,
            int indent,
            string resultName)
        {
            string value = HttpClientSourceEmitter.GetValueExpression(parameter);
            string wire = GeneratedSourceEmitter.StringLiteral(parameter.WireName);
            string location = _prefix + "L_" + parameter.LocationName;

            if (parameter.IsContent)
            {
                source.Append(' ', indent).Append("string ").Append(JsonVar).Append(" = ").Append(_jsonSerializerName)
                    .Append('(').Append(value).AppendLine(");");
                switch (parameter.LocationName)
                {
                    case "header":
                        source.Append(' ', indent).Append("string? ").Append(resultName).Append(" = ").Append(JsonVar).AppendLine(";");
                        break;
                    case "path":
                        source.Append(' ', indent).Append("string? ").Append(resultName).Append(" = ")
                            .Append(EncodeName).Append("(").Append(JsonVar).AppendLine(", false);");
                        break;
                    case "query":
                        source.Append(' ', indent).Append("string? ").Append(resultName).Append(" = ")
                            .Append(EncodeName).Append('(').Append(wire).Append(", false) + \"=\" + ")
                            .Append(EncodeName).Append("(").Append(JsonVar).Append(", ")
                            .Append(parameter.AllowReserved ? "true" : "false").AppendLine(");");
                        break;
                    default:
                        source.Append(' ', indent).Append("string? ").Append(resultName).Append(" = ")
                            .Append(wire).Append(" + \"=\" + ").Append(EncodeName).Append("(").Append(JsonVar).AppendLine(", false);");
                        break;
                }

                return;
            }

            int kind = GetKind(parameter.Type);
            string style = _prefix + "S_" + parameter.Style;
            string explode = parameter.Explode ? "true" : "false";
            string allowReserved = parameter.AllowReserved ? "true" : "false";
            string common = location + ", " + style + ", " + explode + ", " + allowReserved + ", " + wire;

            if (kind == KindScalar)
            {
                source.Append(' ', indent).Append("string? ").Append(resultName).Append(" = ").Append(SerializeName)
                    .Append('(').Append(common).Append(", ").Append(KindConst(kind)).Append(", ConvertToString(")
                    .Append(value).AppendLine("), null, null);");
                return;
            }

            if (parameter.Required)
            {
                // A required List / Dictionary / DTO parameter must be non-null.
                source.Append(' ', indent).Append("if (").Append(value).AppendLine(" == null)");
                source.Append(' ', indent).AppendLine("{");
                source.Append(' ', indent + 4).Append("throw new global::System.ArgumentNullException(nameof(")
                    .Append(parameter.Name).AppendLine("));");
                source.Append(' ', indent).AppendLine("}");
            }

            if (kind == KindArray)
            {
                ThrowIfNotScalar(parameter.Type.ItemType!, parameter);
                source.Append(' ', indent).Append("var ").Append(ItemsVar)
                    .AppendLine(" = new global::System.Collections.Generic.List<string>();");
                source.Append(' ', indent).Append("foreach (var ").Append(ItemVar).Append(" in ").Append(value).AppendLine(")");
                source.Append(' ', indent).AppendLine("{");
                source.Append(' ', indent + 4).Append(ItemsVar).Append(".Add(").Append(ItemName).Append('(')
                    .Append(ItemVar).Append(", ")
                    .Append(GeneratedSourceEmitter.StringLiteral(parameter.Name)).AppendLine("));");
                source.Append(' ', indent).AppendLine("}");
                source.Append(' ', indent).Append("string? ").Append(resultName).Append(" = ").Append(SerializeName)
                    .Append('(').Append(common).Append(", ").Append(KindConst(kind))
                    .Append(", null, ").Append(ItemsVar).AppendLine(", null);");
                return;
            }

            source.Append(' ', indent).Append("var ").Append(PairsVar)
                .AppendLine(" = new global::System.Collections.Generic.List<string>();");
            if (parameter.Type.Kind == GeneratedTypeKind.Map)
            {
                GeneratedTypeModel valueType = parameter.Type.ValueType!;
                ThrowIfNotScalar(valueType, parameter);
                source.Append(' ', indent).Append("foreach (var ").Append(PairVar).Append(" in ").Append(value).AppendLine(")");
                source.Append(' ', indent).AppendLine("{");
                AppendPairIfPresent(source, indent + 4, valueType, PairVar + ".Value", PairVar + ".Key");
                source.Append(' ', indent).AppendLine("}");
            }
            else
            {
                if (!_dtos.TryGetValue(parameter.Type.Name, out GeneratedDtoModel? dto))
                {
                    throw new InvalidOperationException(
                        "Parameter '" + parameter.WireName + "' refers to the unknown type '" +
                        parameter.Type.Name + "'.");
                }

                foreach (GeneratedDtoPropertyModel property in dto.Properties)
                {
                    ThrowIfNotScalar(property.Type, parameter);
                    AppendPairIfPresent(
                        source,
                        indent,
                        property.Type,
                        value + "." + property.Name,
                        GeneratedSourceEmitter.StringLiteral(property.WireName));
                }

                if (dto.ExtensionDataName is string extensionName &&
                    dto.ExtensionDataValueType is GeneratedTypeModel extensionType)
                {
                    // additionalProperties with a primitive schema: the extra members follow the
                    // declared ones, in ordinal key order.
                    ThrowIfNotScalar(extensionType, parameter);
                    string extra = value + "." + extensionName;
                    source.Append(' ', indent).Append("if (").Append(extra).AppendLine(" != null)");
                    source.Append(' ', indent).AppendLine("{");
                    source.Append(' ', indent + 4).Append("var ").Append(ItemsVar)
                        .Append(" = new global::System.Collections.Generic.List<string>(").Append(extra).AppendLine(".Keys);");
                    source.Append(' ', indent + 4).Append(ItemsVar).AppendLine(".Sort(global::System.StringComparer.Ordinal);");
                    source.Append(' ', indent + 4).Append("foreach (var ").Append(ItemVar).Append(" in ").Append(ItemsVar).AppendLine(")");
                    source.Append(' ', indent + 4).AppendLine("{");
                    source.Append(' ', indent + 8).Append("string? ").Append(PairVar).Append(" = ").Append(ExtraName).Append('(')
                        .Append(extra).Append('[').Append(ItemVar).Append("], ")
                        .Append(GeneratedSourceEmitter.StringLiteral(parameter.Name)).AppendLine(");");
                    source.Append(' ', indent + 8).Append("if (").Append(PairVar).AppendLine(" != null)");
                    source.Append(' ', indent + 8).AppendLine("{");
                    source.Append(' ', indent + 12).Append(PairsVar).Append(".Add(").Append(ItemVar).AppendLine(");");
                    source.Append(' ', indent + 12).Append(PairsVar).Append(".Add(").Append(PairVar).AppendLine(");");
                    source.Append(' ', indent + 8).AppendLine("}");
                    source.Append(' ', indent + 4).AppendLine("}");
                    source.Append(' ', indent).AppendLine("}");
                }
            }

            source.Append(' ', indent).Append("string? ").Append(resultName).Append(" = ").Append(SerializeName)
                .Append('(').Append(common).Append(", ").Append(KindConst(kind))
                .Append(", null, null, ").Append(PairsVar).AppendLine(");");
        }

        // ---- form-urlencoded fields (P4) --------------------------------------------------------------

        /// <summary>
        /// Appends one application/x-www-form-urlencoded field of a body DTO to <paramref name="collection"/>
        /// (a List of string): the member is serialized with the form style rules of its Encoding Object
        /// (query style: style / explode / allowReserved), so an array becomes name=a&amp;name=b or name=a,b.
        /// </summary>
        internal void AppendFormField(
            StringBuilder source,
            GeneratedParameterModel parameter,
            int indentation,
            string collection)
        {
            AppendGuarded(source, parameter, indentation, (indent) =>
            {
                AppendSerializedValue(source, parameter, indent, ResultVar);
                source.Append(' ', indent).AppendLine("if (" + ResultVar + " != null && " + ResultVar + ".Length > 0)");
                source.Append(' ', indent).AppendLine("{");
                source.Append(' ', indent + 4).AppendLine(collection + ".Add(" + ResultVar + ");");
                source.Append(' ', indent).AppendLine("}");
            });
        }

        /// <summary>A nested value of a urlencoded body: its JSON text becomes the (percent-encoded) field value.</summary>
        internal void AppendFormJsonField(
            StringBuilder source,
            GeneratedFormFieldModel field,
            string valueExpression,
            int indentation,
            string collection)
        {
            string? condition = field.Required
                ? null
                : field.Type.IsValueType ? valueExpression + ".HasValue" : valueExpression + " != null";
            if (condition is not null)
            {
                source.Append(' ', indentation).Append("if (").Append(condition).AppendLine(")");
            }

            source.Append(' ', indentation).AppendLine("{");
            source.Append(' ', indentation + 4).Append(collection).Append(".Add(")
                .Append(EncodeName).Append('(').Append(GeneratedSourceEmitter.StringLiteral(field.WireName)).Append(", false) + \"=\" + ")
                .Append(EncodeName).Append('(').Append(_jsonSerializerName).Append('(').Append(valueExpression).Append("), ")
                .Append(field.AllowReserved ? "true" : "false").AppendLine("));");
            source.Append(' ', indentation).AppendLine("}");
        }

        private static void ThrowIfNotScalar(GeneratedTypeModel type, GeneratedParameterModel parameter)
        {
            if (!IsScalarKind(type))
            {
                throw new InvalidOperationException(
                    "Parameter '" + parameter.WireName + "' contains a nested value that has no defined serialization.");
            }
        }

        private void AppendPairIfPresent(
            StringBuilder source,
            int indent,
            GeneratedTypeModel type,
            string valueExpression,
            string nameExpression)
        {
            string? condition = null;
            string converted = valueExpression;
            if (type.IsValueType)
            {
                if (type.Nullable)
                {
                    condition = valueExpression + ".HasValue";
                    converted = valueExpression + ".Value";
                }
            }
            else
            {
                condition = valueExpression + " != null";
            }

            string body = PairsVar + ".Add(" + nameExpression + "); " + PairsVar + ".Add(ConvertToString(" + converted + "));";
            if (condition is null)
            {
                source.Append(' ', indent).AppendLine(body);
            }
            else
            {
                source.Append(' ', indent).Append("if (").Append(condition).Append(") { ").Append(body).AppendLine(" }");
            }
        }

        // ---- generated helpers -----------------------------------------------------------------------

        internal void AppendHelpers(StringBuilder source)
        {
            if (!UsesHelpers)
            {
                return;
            }

            source.AppendLine();
            source.Append("        private static string ").Append(ItemName)
                .AppendLine("(object? item, string parameterName)");
            source.AppendLine("        {");
            source.AppendLine("            if (item == null)");
            source.AppendLine("            {");
            source.AppendLine("                throw new global::System.ArgumentException(\"An array element must not be null.\", parameterName);");
            source.AppendLine("            }");
            source.AppendLine("            return ConvertToString(item);");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine();
            source.Append("        private static string? ").Append(ExtraName)
                .AppendLine("(global::Newtonsoft.Json.Linq.JToken? token, string parameterName)");
            source.AppendLine("        {");
            source.AppendLine("            if (token == null || token.Type == global::Newtonsoft.Json.Linq.JTokenType.Null)");
            source.AppendLine("            {");
            source.AppendLine("                return null;");
            source.AppendLine("            }");
            source.AppendLine("            if (token is global::Newtonsoft.Json.Linq.JValue jsonValue && jsonValue.Value != null)");
            source.AppendLine("            {");
            source.AppendLine("                return ConvertToString(jsonValue.Value);");
            source.AppendLine("            }");
            source.AppendLine("            throw new global::System.ArgumentException(\"An additional member must be a primitive value.\", parameterName);");
            source.AppendLine("        }");
            AppendConstants(source);
            source.AppendLine();
            AppendEncodeHelper(source);
            source.AppendLine();
            AppendSerializeHelper(source);
        }

        private static readonly string[] Styles =
            { "matrix", "label", "simple", "form", "spaceDelimited", "pipeDelimited", "deepObject" };

        private static readonly string[] Locations = { "path", "query", "header", "cookie" };

        private string KindConst(int kind)
        {
            return _prefix + "K_" + (kind == KindScalar ? "scalar" : kind == KindArray ? "array" : "object");
        }

        // The generated helper compares against private constants instead of bare literals; they
        // stay private, so the public API does not grow.
        private void AppendConstants(StringBuilder source)
        {
            source.Append("        private const int ").Append(KindConst(KindScalar)).AppendLine(" = 0;");
            source.Append("        private const int ").Append(KindConst(KindArray)).AppendLine(" = 1;");
            source.Append("        private const int ").Append(KindConst(KindObject)).AppendLine(" = 2;");
            foreach (string style in Styles)
            {
                source.Append("        private const string ").Append(_prefix).Append("S_").Append(style)
                    .Append(" = \"").Append(style).AppendLine("\";");
            }

            foreach (string location in Locations)
            {
                source.Append("        private const string ").Append(_prefix).Append("L_").Append(location)
                    .Append(" = \"").Append(location).AppendLine("\";");
            }
        }

        private void AppendEncodeHelper(StringBuilder source)
        {
            // allowReserved keeps RFC 3986 reserved characters (except '#', which would start a
            // fragment and be dropped by the HTTP stack) and existing percent-encoded triplets.
            source.Append("        private static string ").Append(EncodeName)
                .AppendLine("(string value, bool allowReserved)");
            source.AppendLine("        {");
            source.AppendLine("            if (!allowReserved)");
            source.AppendLine("            {");
            source.AppendLine("                return global::System.Uri.EscapeDataString(value);");
            source.AppendLine("            }");
            source.AppendLine("            var builder = new global::System.Text.StringBuilder(value.Length);");
            source.AppendLine("            for (int index = 0; index < value.Length; index++)");
            source.AppendLine("            {");
            source.AppendLine("                char character = value[index];");
            source.AppendLine("                if ((character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z') ||");
            source.AppendLine("                    (character >= '0' && character <= '9') || \"-._~:/?[]@!$&'()*+,;=\".IndexOf(character) >= 0)");
            source.AppendLine("                {");
            source.AppendLine("                    builder.Append(character);");
            source.AppendLine("                    continue;");
            source.AppendLine("                }");
            source.AppendLine("                if (character == '%' && index + 2 < value.Length && global::System.Uri.IsHexDigit(value[index + 1]) && global::System.Uri.IsHexDigit(value[index + 2]))");
            source.AppendLine("                {");
            source.AppendLine("                    builder.Append(character);");
            source.AppendLine("                    continue;");
            source.AppendLine("                }");
            source.AppendLine("                int length = char.IsHighSurrogate(character) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]) ? 2 : 1;");
            source.AppendLine("                foreach (byte octet in global::System.Text.Encoding.UTF8.GetBytes(value.Substring(index, length)))");
            source.AppendLine("                {");
            source.AppendLine("                    builder.Append('%').Append(octet.ToString(\"X2\", global::System.Globalization.CultureInfo.InvariantCulture));");
            source.AppendLine("                }");
            source.AppendLine("                index += length - 1;");
            source.AppendLine("            }");
            source.AppendLine("            return builder.ToString();");
            source.AppendLine("        }");
        }

        private void AppendSerializeHelper(StringBuilder source)
        {
            string[] lines =
            {
                "private static string? @SERIALIZE@(string location, string style, bool explode, bool allowReserved, string name, int kind, string? scalar, global::System.Collections.Generic.List<string>? items, global::System.Collections.Generic.List<string>? pairs)",
                "{",
                "    bool isHeader = location == \"header\";",
                "    bool isCookie = location == \"cookie\";",
                "    bool reserved = allowReserved && location == \"query\";",
                "    string N(string text) => isHeader ? text : @ENCODE@(text, false);",
                "    string V(string text) => isHeader ? text : @ENCODE@(text, reserved);",
                "    string entry(string key, string text) => N(key) + \"=\" + V(text);",
                "    string formKey = isCookie ? name : N(name);",
                "    string separator = isCookie ? \"; \" : \"&\";",
                "    if (kind == @KIND0@)",
                "    {",
                "        string text = scalar ?? string.Empty;",
                "        switch (style)",
                "        {",
                "            case \"matrix\":",
                "                return \";\" + N(name) + (text.Length == 0 ? string.Empty : \"=\" + V(text));",
                "            case \"label\":",
                "                return \".\" + V(text);",
                "            case \"simple\":",
                "                return V(text);",
                "            default:",
                "                return formKey + \"=\" + V(text);",
                "        }",
                "    }",
                "    var parts = new global::System.Collections.Generic.List<string>();",
                "    if (kind == @KIND1@)",
                "    {",
                "        if (items == null || items.Count == 0)",
                "        {",
                "            return location == \"path\" ? string.Empty : null;",
                "        }",
                "        switch (style)",
                "        {",
                "            case \"matrix\":",
                "                if (explode)",
                "                {",
                "                    foreach (string item in items)",
                "                    {",
                "                        parts.Add(\";\" + N(name) + (item.Length == 0 ? string.Empty : \"=\" + V(item)));",
                "                    }",
                "                    return string.Concat(parts);",
                "                }",
                "                foreach (string item in items) parts.Add(V(item));",
                "                return \";\" + N(name) + \"=\" + string.Join(\",\", parts);",
                "            case \"label\":",
                "                foreach (string item in items) parts.Add(V(item));",
                "                return \".\" + string.Join(\".\", parts);",
                "            case \"simple\":",
                "                foreach (string item in items) parts.Add(V(item));",
                "                return string.Join(\",\", parts);",
                "            case \"spaceDelimited\":",
                "                foreach (string item in items) parts.Add(V(item));",
                "                return N(name) + \"=\" + string.Join(\"%20\", parts);",
                "            case \"pipeDelimited\":",
                "                foreach (string item in items) parts.Add(V(item));",
                "                return N(name) + \"=\" + string.Join(\"|\", parts);",
                "            default:",
                "                if (explode)",
                "                {",
                "                    foreach (string item in items) parts.Add(formKey + \"=\" + V(item));",
                "                    return string.Join(separator, parts);",
                "                }",
                "                foreach (string item in items) parts.Add(V(item));",
                "                return formKey + \"=\" + string.Join(\",\", parts);",
                "        }",
                "    }",
                "    if (pairs == null || pairs.Count == 0)",
                "    {",
                "        return location == \"path\" ? string.Empty : null;",
                "    }",
                "    var flat = new global::System.Collections.Generic.List<string>();",
                "    for (int index = 0; index + 1 < pairs.Count; index += 2)",
                "    {",
                "        flat.Add(N(pairs[index]));",
                "        flat.Add(V(pairs[index + 1]));",
                "    }",
                "    switch (style)",
                "    {",
                "        case \"matrix\":",
                "            if (explode)",
                "            {",
                "                for (int index = 0; index + 1 < pairs.Count; index += 2)",
                "                {",
                "                    parts.Add(\";\" + N(pairs[index]) + (pairs[index + 1].Length == 0 ? string.Empty : \"=\" + V(pairs[index + 1])));",
                "                }",
                "                return string.Concat(parts);",
                "            }",
                "            return \";\" + N(name) + \"=\" + string.Join(\",\", flat);",
                "        case \"label\":",
                "            if (explode)",
                "            {",
                "                for (int index = 0; index + 1 < pairs.Count; index += 2) parts.Add(entry(pairs[index], pairs[index + 1]));",
                "                return \".\" + string.Join(\".\", parts);",
                "            }",
                "            return \".\" + string.Join(\".\", flat);",
                "        case \"simple\":",
                "            if (explode)",
                "            {",
                "                for (int index = 0; index + 1 < pairs.Count; index += 2) parts.Add(entry(pairs[index], pairs[index + 1]));",
                "                return string.Join(\",\", parts);",
                "            }",
                "            return string.Join(\",\", flat);",
                "        case \"spaceDelimited\":",
                "            return N(name) + \"=\" + string.Join(\"%20\", flat);",
                "        case \"pipeDelimited\":",
                "            return N(name) + \"=\" + string.Join(\"|\", flat);",
                "        case \"deepObject\":",
                "            for (int index = 0; index + 1 < pairs.Count; index += 2) parts.Add(N(name) + \"[\" + N(pairs[index]) + \"]=\" + V(pairs[index + 1]));",
                "            return string.Join(\"&\", parts);",
                "        default:",
                "            if (explode)",
                "            {",
                "                for (int index = 0; index + 1 < pairs.Count; index += 2) parts.Add(entry(pairs[index], pairs[index + 1]));",
                "                return string.Join(separator, parts);",
                "            }",
                "            return formKey + \"=\" + string.Join(\",\", flat);",
                "    }",
                "}"
            };
            foreach (string line in lines)
            {
                string text = line
                    .Replace("@SERIALIZE@", SerializeName)
                    .Replace("@ENCODE@", EncodeName)
                    .Replace("@KIND0@", KindConst(KindScalar))
                    .Replace("@KIND1@", KindConst(KindArray));
                foreach (string style in Styles)
                {
                    text = text.Replace("\"" + style + "\"", _prefix + "S_" + style);
                }

                foreach (string location in Locations)
                {
                    text = text.Replace("\"" + location + "\"", _prefix + "L_" + location);
                }

                source.Append("        ").AppendLine(text);
            }
        }
    }
}
