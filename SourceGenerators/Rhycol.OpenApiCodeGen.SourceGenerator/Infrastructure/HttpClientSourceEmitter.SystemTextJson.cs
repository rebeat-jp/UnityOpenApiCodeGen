using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// The System.Text.Json parts of the HttpClient emitter: the shared options (converters, the rules of
    /// the Specified / readOnly members), the request serialization, the declared error bodies and the
    /// response contract. The contract works on a JsonDocument (a strict RFC 8259 parser), so unlike
    /// the Newtonsoft path it needs neither a hand written syntax scanner nor line information.
    /// </summary>
    internal sealed partial class HttpClientSourceEmitter
    {
        private const string StjJson = "global::System.Text.Json";
        private const string StjException = "global::System.Text.Json.JsonException";
        private const string StjElement = "global::System.Text.Json.JsonElement";
        private const string StjKind = "global::System.Text.Json.JsonValueKind";
        private const string Invariant = "global::System.Globalization.CultureInfo.InvariantCulture";

        private static string DateConverterName(BodyHelperNames helpers) => helpers.Prefix + "DateConverter";

        private static string DateTimeOffsetConverterName(BodyHelperNames helpers) => helpers.Prefix + "DateTimeOffsetConverter";

        private static string Int32ConverterName(BodyHelperNames helpers) => helpers.Prefix + "Int32Converter";

        private static string Int64ConverterName(BodyHelperNames helpers) => helpers.Prefix + "Int64Converter";

        private static string TypeInfoModifierName(BodyHelperNames helpers) => helpers.Prefix + "ConfigureTypeInfo";

        private static string CreateOptionsName(BodyHelperNames helpers) => helpers.Prefix + "CreateJsonOptions";

        private static void AppendSystemTextJsonSupport(
            StringBuilder source,
            OpenApiGenerationModel model,
            ResponseValidatorPlan validators,
            BodyHelperNames helpers)
        {
            ISet<GeneratedTypeKind> kinds = SystemTextJsonFeatureGate.CollectKinds(model);
            GeneratedDtoModel[] rulesDtos = model.Dtos
                .Where(static dto => dto.Properties.Any(static property =>
                    property.UseSpecified || property.Annotations.ReadOnly))
                .OrderBy(static dto => dto.Name, StringComparer.Ordinal)
                .ToArray();
            string options = helpers.JsonOptions;

            source.AppendLine("        /// <summary>The Specified / readOnly rules and the date and integer converters live in these options of the generated client; serializing a DTO with your own JsonSerializerOptions does not apply them.</summary>");
            source.AppendLine("        /// <remarks>UnsafeRelaxedJsonEscaping is meant for HTTP bodies only; do not embed this output in HTML.</remarks>");
            source.Append("        private static readonly ").Append(StjJson).Append(".JsonSerializerOptions ")
                .Append(options).Append(" = ").Append(CreateOptionsName(helpers)).AppendLine("();");
            source.AppendLine();
            source.Append("        private static ").Append(StjJson).Append(".JsonSerializerOptions ")
                .Append(CreateOptionsName(helpers)).AppendLine("()");
            source.AppendLine("        {");
            source.Append("            var options = new ").Append(StjJson).AppendLine(".JsonSerializerOptions");
            source.AppendLine("            {");
            source.AppendLine("                Encoder = global::System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,");
            source.AppendLine("                MaxDepth = 64");
            source.AppendLine("            };");
            if (rulesDtos.Length > 0)
            {
                source.Append("            options.TypeInfoResolver = new ").Append(StjJson)
                    .Append(".Serialization.Metadata.DefaultJsonTypeInfoResolver { Modifiers = { ")
                    .Append(TypeInfoModifierName(helpers)).AppendLine(" } };");
            }

            if (kinds.Contains(GeneratedTypeKind.DateTime))
            {
                source.Append("            options.Converters.Add(new ").Append(DateConverterName(helpers)).AppendLine("());");
            }

            if (kinds.Contains(GeneratedTypeKind.DateTimeOffset))
            {
                source.Append("            options.Converters.Add(new ").Append(DateTimeOffsetConverterName(helpers)).AppendLine("());");
            }

            if (kinds.Contains(GeneratedTypeKind.Int32))
            {
                source.Append("            options.Converters.Add(new ").Append(Int32ConverterName(helpers)).AppendLine("());");
            }

            if (kinds.Contains(GeneratedTypeKind.Int64))
            {
                source.Append("            options.Converters.Add(new ").Append(Int64ConverterName(helpers)).AppendLine("());");
            }

            source.AppendLine("            return options;");
            source.AppendLine("        }");

            if (rulesDtos.Length > 0)
            {
                AppendTypeInfoModifier(source, model, rulesDtos, helpers);
            }

            // The request body, a JSON parameter or a JSON form field: the runtime type decides the shape.
            source.AppendLine();
            source.Append("        private static string ").Append(validators.RequestJsonSerializerName).AppendLine("(object? value)");
            source.AppendLine("        {");
            source.AppendLine("            if (value == null)");
            source.AppendLine("            {");
            source.AppendLine("                return \"null\";");
            source.AppendLine("            }");
            source.Append("            return ").Append(StjJson).Append(".JsonSerializer.Serialize(value, value.GetType(), ")
                .Append(options).AppendLine(");");
            source.AppendLine("        }");

            if (kinds.Contains(GeneratedTypeKind.DateTime))
            {
                AppendDateConverter(source, helpers);
            }

            if (kinds.Contains(GeneratedTypeKind.DateTimeOffset))
            {
                AppendDateTimeOffsetConverter(source, helpers);
            }

            if (kinds.Contains(GeneratedTypeKind.Int32))
            {
                AppendIntegerConverter(source, Int32ConverterName(helpers), "int", "Int32", "global::System.Int32");
            }

            if (kinds.Contains(GeneratedTypeKind.Int64))
            {
                AppendIntegerConverter(source, Int64ConverterName(helpers), "long", "Int64", "global::System.Int64");
            }
        }

        // The Specified members and the readOnly rule are applied by the client, not by attributes on the
        // DTO (System.Text.Json has no attribute for a conditional ShouldSerialize).
        private static void AppendTypeInfoModifier(
            StringBuilder source,
            OpenApiGenerationModel model,
            GeneratedDtoModel[] dtos,
            BodyHelperNames helpers)
        {
            source.AppendLine();
            source.Append("        private static void ").Append(TypeInfoModifierName(helpers))
                .Append('(').Append(StjJson).AppendLine(".Serialization.Metadata.JsonTypeInfo typeInfo)");
            source.AppendLine("        {");
            foreach (GeneratedDtoModel dto in dtos)
            {
                string dtoType = "global::" + model.GeneratedNamespace + "." + dto.Name;
                source.Append("            if (typeInfo.Type == typeof(").Append(dtoType).AppendLine("))");
                source.AppendLine("            {");
                source.Append("                foreach (").Append(StjJson)
                    .AppendLine(".Serialization.Metadata.JsonPropertyInfo property in typeInfo.Properties)");
                source.AppendLine("                {");
                source.AppendLine("                    switch (property.Name)");
                source.AppendLine("                    {");
                foreach (GeneratedDtoPropertyModel property in dto.Properties)
                {
                    if (!property.UseSpecified && !property.Annotations.ReadOnly)
                    {
                        continue;
                    }

                    source.Append("                        case ")
                        .Append(GeneratedSourceEmitter.StringLiteral(property.WireName)).AppendLine(":");
                    if (property.Annotations.ReadOnly)
                    {
                        // A readOnly property is never sent.
                        source.AppendLine("                            property.ShouldSerialize = (instance, value) => false;");
                    }
                    else
                    {
                        source.Append("                            property.ShouldSerialize = (instance, value) => ((")
                            .Append(dtoType).Append(")instance).").Append(property.Name).AppendLine("Specified;");
                    }

                    source.AppendLine("                            break;");
                }

                source.AppendLine("                    }");
                source.AppendLine("                }");
                source.AppendLine("            }");
            }

            source.AppendLine("        }");
        }

        // A full-date: the wire form is yyyy-MM-dd, in both directions.
        private static void AppendDateConverter(StringBuilder source, BodyHelperNames helpers)
        {
            source.AppendLine();
            source.Append("        private sealed class ").Append(DateConverterName(helpers)).Append(" : ").Append(StjJson)
                .AppendLine(".Serialization.JsonConverter<global::System.DateTime>");
            source.AppendLine("        {");
            source.Append("            public override global::System.DateTime Read(ref ").Append(StjJson)
                .Append(".Utf8JsonReader reader, global::System.Type typeToConvert, ").Append(StjJson)
                .AppendLine(".JsonSerializerOptions options)");
            source.AppendLine("            {");
            source.Append("                if (reader.TokenType != ").Append(StjJson).AppendLine(".JsonTokenType.String)");
            source.AppendLine("                {");
            source.Append("                    throw new ").Append(StjException).AppendLine("(\"A full-date string is required.\");");
            source.AppendLine("                }");
            source.Append("                if (!global::System.DateTime.TryParseExact(reader.GetString(), \"yyyy-MM-dd\", ")
                .Append(Invariant).AppendLine(", global::System.Globalization.DateTimeStyles.None, out global::System.DateTime value))");
            source.AppendLine("                {");
            source.Append("                    throw new ").Append(StjException).AppendLine("(\"The value is not a valid full-date.\");");
            source.AppendLine("                }");
            source.AppendLine("                return value;");
            source.AppendLine("            }");
            source.AppendLine();
            source.Append("            public override void Write(").Append(StjJson)
                .Append(".Utf8JsonWriter writer, global::System.DateTime value, ").Append(StjJson)
                .AppendLine(".JsonSerializerOptions options)");
            source.AppendLine("            {");
            source.Append("                writer.WriteStringValue(value.ToString(\"yyyy-MM-dd\", ").Append(Invariant).AppendLine("));");
            source.AppendLine("            }");
            source.AppendLine("        }");
        }

        // RFC 3339 allows a lower case 't' and 'z', which the built-in reader does not read (the response
        // contract accepts them, as Json.NET does). The wire form written is the built-in one.
        private static void AppendDateTimeOffsetConverter(StringBuilder source, BodyHelperNames helpers)
        {
            source.AppendLine();
            source.Append("        private sealed class ").Append(DateTimeOffsetConverterName(helpers)).Append(" : ").Append(StjJson)
                .AppendLine(".Serialization.JsonConverter<global::System.DateTimeOffset>");
            source.AppendLine("        {");
            source.Append("            public override global::System.DateTimeOffset Read(ref ").Append(StjJson)
                .Append(".Utf8JsonReader reader, global::System.Type typeToConvert, ").Append(StjJson)
                .AppendLine(".JsonSerializerOptions options)");
            source.AppendLine("            {");
            source.Append("                if (reader.TokenType != ").Append(StjJson).AppendLine(".JsonTokenType.String)");
            source.AppendLine("                {");
            source.Append("                    throw new ").Append(StjException).AppendLine("(\"A date-time string is required.\");");
            source.AppendLine("                }");
            source.AppendLine("                if (reader.TryGetDateTimeOffset(out global::System.DateTimeOffset value))");
            source.AppendLine("                {");
            source.AppendLine("                    return value;");
            source.AppendLine("                }");
            source.Append("                if (!global::System.DateTimeOffset.TryParse(reader.GetString(), ").Append(Invariant)
                .AppendLine(", global::System.Globalization.DateTimeStyles.RoundtripKind, out value))");
            source.AppendLine("                {");
            source.Append("                    throw new ").Append(StjException).AppendLine("(\"The value is not a valid date-time.\");");
            source.AppendLine("                }");
            source.AppendLine("                return value;");
            source.AppendLine("            }");
            source.AppendLine();
            source.Append("            public override void Write(").Append(StjJson)
                .Append(".Utf8JsonWriter writer, global::System.DateTimeOffset value, ").Append(StjJson)
                .AppendLine(".JsonSerializerOptions options)");
            source.AppendLine("            {");
            source.AppendLine("                writer.WriteStringValue(value);");
            source.AppendLine("            }");
            source.AppendLine("        }");
        }

        // JSON Schema integers include 1.0 and 1e2; System.Text.Json reads only the plain lexeme, so
        // the other forms of an integral value are accepted here (as Json.NET does).
        private static void AppendIntegerConverter(
            StringBuilder source,
            string className,
            string keyword,
            string suffix,
            string clrType)
        {
            source.AppendLine();
            source.Append("        private sealed class ").Append(className).Append(" : ").Append(StjJson)
                .Append(".Serialization.JsonConverter<").Append(keyword).AppendLine(">");
            source.AppendLine("        {");
            source.Append("            public override ").Append(keyword).Append(" Read(ref ").Append(StjJson)
                .Append(".Utf8JsonReader reader, global::System.Type typeToConvert, ").Append(StjJson)
                .AppendLine(".JsonSerializerOptions options)");
            source.AppendLine("            {");
            source.Append("                if (reader.TokenType != ").Append(StjJson).AppendLine(".JsonTokenType.Number)");
            source.AppendLine("                {");
            source.Append("                    throw new ").Append(StjException).AppendLine("(\"An integer number is required.\");");
            source.AppendLine("                }");
            source.Append("                if (reader.TryGet").Append(suffix).Append("(out ").Append(keyword).AppendLine(" value))");
            source.AppendLine("                {");
            source.AppendLine("                    return value;");
            source.AppendLine("                }");
            source.AppendLine("                decimal number;");
            source.AppendLine("                try");
            source.AppendLine("                {");
            source.AppendLine("                    number = reader.GetDecimal();");
            source.AppendLine("                }");
            source.AppendLine("                catch (global::System.FormatException exception)");
            source.AppendLine("                {");
            source.Append("                    throw new ").Append(StjException).AppendLine("(\"The integer number is out of range.\", exception);");
            source.AppendLine("                }");
            source.Append("                if (number != global::System.Decimal.Truncate(number) || number < ").Append(clrType)
                .Append(".MinValue || number > ").Append(clrType).AppendLine(".MaxValue)");
            source.AppendLine("                {");
            source.Append("                    throw new ").Append(StjException).AppendLine("(\"The number is not an integer in range.\");");
            source.AppendLine("                }");
            source.Append("                return (").Append(keyword).AppendLine(")number;");
            source.AppendLine("            }");
            source.AppendLine();
            source.Append("            public override void Write(").Append(StjJson).Append(".Utf8JsonWriter writer, ")
                .Append(keyword).Append(" value, ").Append(StjJson).AppendLine(".JsonSerializerOptions options)");
            source.AppendLine("            {");
            source.AppendLine("                writer.WriteNumberValue(value);");
            source.AppendLine("            }");
            source.AppendLine("        }");
        }

        // An enum as text (query, path, header, form): its JSON wire value without the quotes.
        private static void AppendSystemTextJsonEnumToString(StringBuilder source, BodyHelperNames helpers)
        {
            source.Append("                string enumJson = ").Append(StjJson)
                .Append(".JsonSerializer.Serialize(value, value.GetType(), ").Append(helpers.JsonOptions).AppendLine(");");
            source.Append("                using (").Append(StjJson).AppendLine(".JsonDocument enumDocument = global::System.Text.Json.JsonDocument.Parse(enumJson))");
            source.AppendLine("                {");
            source.Append("                    return enumDocument.RootElement.ValueKind == ").Append(StjKind).AppendLine(".String");
            source.AppendLine("                        ? enumDocument.RootElement.GetString() ?? string.Empty");
            source.AppendLine("                        : enumDocument.RootElement.GetRawText();");
            source.AppendLine("                }");
        }

        // A declared error body goes through the same contract as a success body (null, repeated names,
        // integer lexemes, enums, required and undeclared members) and is then read. Any failure of that
        // check or of the read leaves Error null and is kept as ErrorDeserializationException. Only the
        // exceptions that malformed data can cause are caught (JsonException from the parser, the contract and the
        // converters; FormatException and OverflowException from value conversions); anything else is a defect
        // of the generated code or of the caller and propagates.
        private static void AppendSystemTextJsonErrorHelper(
            StringBuilder source, BodyHelperNames names, ResponseJsonContractPlan contracts)
        {
            source.AppendLine(@"
        private static object? __DESERIALIZE__<T>(string json, global::System.Action<global::System.Text.Json.JsonElement, string, bool> validate, out global::System.Exception? failure)
        {
            failure = null;
            if (global::System.String.IsNullOrWhiteSpace(json) || global::System.String.Equals(json.Trim(), ""null"", global::System.StringComparison.Ordinal))
            {
                return null;
            }
            try
            {
                __PARSE__(json, validate, false, 64);
                return global::System.Text.Json.JsonSerializer.Deserialize<T>(json, __OPTIONS__);
            }
            catch (global::System.Exception exception) when (exception is global::System.Text.Json.JsonException ||
                exception is global::System.FormatException || exception is global::System.OverflowException)
            {
                failure = exception;
                return null;
            }
        }
".Replace("__DESERIALIZE__", names.DeserializeError).Replace("__PARSE__", contracts.ParseMethodName).Replace("__OPTIONS__", names.JsonOptions));
        }

        private sealed partial class ResponseJsonContractPlan
        {
            internal bool IsSystemTextJson => _json.IsSystemTextJson;

            /// <summary>
            /// Checks the success body against the contract (JsonDocument), then deserializes it. Ends with
            /// <c>return deserializedResponse!;</c> or with an assignment to <paramref name="resultVariable"/>.
            /// </summary>
            internal void AppendSystemTextJsonDecode(
                StringBuilder source,
                GeneratedTypeModel type,
                string indent,
                ResponseValidatorPlan responseValidators,
                string? resultVariable)
            {
                source.Append(indent).AppendLine("if (global::System.String.IsNullOrWhiteSpace(responseBody))");
                source.Append(indent).AppendLine("{");
                source.Append(indent).Append("    throw new ").Append(StjException).AppendLine("(\"The successful response requires a JSON body.\");");
                source.Append(indent).AppendLine("}");
                if (!type.Nullable)
                {
                    source.Append(indent).AppendLine("if (global::System.String.Equals(responseBody.Trim(), \"null\", global::System.StringComparison.Ordinal))");
                    source.Append(indent).AppendLine("{");
                    source.Append(indent).Append("    throw new ").Append(StjException).AppendLine("(\"The successful response requires a non-null JSON body.\");");
                    source.Append(indent).AppendLine("}");
                }

                source.Append(indent).Append(ParseMethodName).Append("(responseBody, ").Append(MethodName(type))
                    .Append(", ").Append(type.Nullable ? "true" : "false").AppendLine(", 64);");
                source.Append(indent).Append("var deserializedResponse = ").Append(StjJson)
                    .Append(".JsonSerializer.Deserialize<").Append(GeneratedSourceEmitter.TypeName(type, _json))
                    .Append(">(responseBody, ").Append(_jsonOptions).AppendLine(");");
                if (!type.Nullable && !type.IsValueType)
                {
                    source.Append(indent).AppendLine("if (deserializedResponse is null)");
                    source.Append(indent).AppendLine("{");
                    source.Append(indent).Append("    throw new ").Append(StjException).AppendLine("(\"The successful response requires a non-null JSON body.\");");
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

            private void AppendSystemTextJsonMethods(StringBuilder source)
            {
                string actionType = "global::System.Action<" + StjElement + ", string, bool>";
                source.AppendLine();
                source.Append("        private static void ").Append(ParseMethodName)
                    .Append("(string json, ").Append(actionType).AppendLine(" validate, bool nullable, int maxDepth)");
                source.AppendLine("        {");
                source.Append("            using (").Append(StjJson)
                    .Append(".JsonDocument document = ").Append(StjJson)
                    .Append(".JsonDocument.Parse(json, new ").Append(StjJson).AppendLine(".JsonDocumentOptions { MaxDepth = maxDepth }))");
                source.AppendLine("            {");
                source.Append("                ").Append(_prefix).AppendLine("CheckDuplicates(document.RootElement);");
                source.AppendLine("                validate(document.RootElement, \"$\", nullable);");
                source.AppendLine("            }");
                source.AppendLine("        }");
                foreach (GeneratedTypeModel type in _types)
                {
                    AppendSystemTextJsonMethod(source, type);
                }

                if (_types.Any(static type =>
                        type.Kind == GeneratedTypeKind.Int32 || type.Kind == GeneratedTypeKind.Int64))
                {
                    AppendSystemTextJsonIntegerHelper(source);
                }

                if (_types.Any(static type => type.Kind == GeneratedTypeKind.DateTime ||
                    type.Kind == GeneratedTypeKind.DateTimeOffset || type.Kind == GeneratedTypeKind.Guid))
                {
                    AppendStringFormatHelpers(source);
                }

                if (_types.Any(static type => type.Kind == GeneratedTypeKind.Binary))
                {
                    AppendBase64Helper(source);
                }

                if (_types.Any(static type => type.ConstValue.HasValue &&
                                              (type.ConstValue.Value.Kind == OpenApiSemanticLiteralKind.Integer ||
                                               type.ConstValue.Value.Kind == OpenApiSemanticLiteralKind.Number)))
                {
                    AppendSystemTextJsonNumberEqualsHelper(source);
                }

                if (_types.Any(IsIntegerEnum))
                {
                    AppendSystemTextJsonIntegerLexemeHelper(source);
                }

                AppendSystemTextJsonDuplicateHelper(source);
            }

            private bool IsIntegerEnum(GeneratedTypeModel type)
            {
                return type.Kind == GeneratedTypeKind.NamedEnum &&
                       _enums.TryGetValue(type.Name, out GeneratedEnumModel? enumModel) && enumModel.IsInteger;
            }

            // Json.NET rejects a repeated member name (DuplicatePropertyNameHandling.Error); JsonDocument keeps
            // both, and the deserializer would silently take the last one.
            private void AppendSystemTextJsonDuplicateHelper(StringBuilder source)
            {
                source.AppendLine(@"
        private static void __PREFIX__CheckDuplicates(global::System.Text.Json.JsonElement token)
        {
            if (token.ValueKind == global::System.Text.Json.JsonValueKind.Object)
            {
                var names = new global::System.Collections.Generic.HashSet<string>(global::System.StringComparer.Ordinal);
                foreach (var property in token.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                        throw new global::System.Text.Json.JsonException(""The response repeats the property name '"" + property.Name + ""'."");
                    __PREFIX__CheckDuplicates(property.Value);
                }
            }
            else if (token.ValueKind == global::System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in token.EnumerateArray())
                {
                    __PREFIX__CheckDuplicates(item);
                }
            }
        }
".Replace("__PREFIX__", _prefix));
            }

            private void AppendSystemTextJsonNumberEqualsHelper(StringBuilder source)
            {
                source.AppendLine(@"
        private static bool __PREFIX__NumberEquals(global::System.Text.Json.JsonElement token, string expected)
        {
            return global::System.Decimal.TryParse(token.GetRawText(), global::System.Globalization.NumberStyles.Float, global::System.Globalization.CultureInfo.InvariantCulture, out var left) &&
                   global::System.Decimal.TryParse(expected, global::System.Globalization.NumberStyles.Float, global::System.Globalization.CultureInfo.InvariantCulture, out var right) &&
                   left == right;
        }
".Replace("__PREFIX__", _prefix));
            }

            // An integer enum wire value is written without fraction and exponent.
            private void AppendSystemTextJsonIntegerLexemeHelper(StringBuilder source)
            {
                source.AppendLine(@"
        private static bool __PREFIX__IsIntegerLexeme(global::System.Text.Json.JsonElement token)
        {
            return token.ValueKind == global::System.Text.Json.JsonValueKind.Number &&
                   token.GetRawText().IndexOfAny(new[] { '.', 'e', 'E' }) < 0;
        }
".Replace("__PREFIX__", _prefix));
            }

            // Same decision as the Newtonsoft contract (an integral value written as 1.0 or 1e2 is an
            // integer), made on the number text. The converter of the type reads the value.
            private void AppendSystemTextJsonIntegerHelper(StringBuilder source)
            {
                source.AppendLine(@"
        private static bool __PREFIX__IsIntegralNumber(global::System.Text.Json.JsonElement token, bool int32)
        {
            if (token.ValueKind != global::System.Text.Json.JsonValueKind.Number) return false;
            string number = token.GetRawText();
            if (number.IndexOfAny(new[] { '.', 'e', 'E' }) < 0) return true;
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
            return true;
        }
".Replace("__PREFIX__", _prefix));
            }

            private void AppendSystemTextJsonMethod(StringBuilder source, GeneratedTypeModel type)
            {
                string nullFlag(GeneratedTypeModel value) => value.Nullable ? "true" : "false";
                source.AppendLine();
                source.Append("        private static void ").Append(MethodName(type))
                    .Append('(').Append(StjElement).AppendLine(" token, string path, bool nullable)");
                source.AppendLine("        {");
                source.Append("            if (token.ValueKind == ").Append(StjKind).AppendLine(".Null)");
                source.AppendLine("            {");
                source.AppendLine("                if (nullable) return;");
                source.Append("                throw new ").Append(StjException).AppendLine("(\"Non-nullable response value was null at \" + path + \".\");");
                source.AppendLine("            }");

                if (type.Kind == GeneratedTypeKind.Array)
                {
                    AppendKindCheck(source, "Array", "Expected a response array at \" + path + \".");
                    source.AppendLine("            int index = 0;");
                    source.AppendLine("            foreach (var item in token.EnumerateArray())");
                    source.AppendLine("            {");
                    source.Append("                ").Append(MethodName(type.ItemType!))
                        .Append("(item, path + \"[\" + index.ToString(").Append(Invariant).Append(") + \"]\", ")
                        .Append(nullFlag(type.ItemType!)).AppendLine(");");
                    source.AppendLine("                index++;");
                    source.AppendLine("            }");
                }
                else if (type.Kind == GeneratedTypeKind.Any)
                {
                    // Any JSON value (including null) is valid; there is nothing to check.
                }
                else if (type.Kind == GeneratedTypeKind.FreeObject)
                {
                    AppendKindCheck(source, "Object", "Expected a response object at \" + path + \".");
                }
                else if (type.Kind == GeneratedTypeKind.Map)
                {
                    AppendKindCheck(source, "Object", "Expected a response object at \" + path + \".");
                    source.AppendLine("            foreach (var property in token.EnumerateObject())");
                    source.AppendLine("            {");
                    source.Append("                ").Append(MethodName(type.ValueType!))
                        .Append("(property.Value, path + \".\" + property.Name, ")
                        .Append(nullFlag(type.ValueType!)).AppendLine(");");
                    source.AppendLine("            }");
                }
                else if (type.Kind == GeneratedTypeKind.Binary)
                {
                    AppendKindCheck(source, "String", "Unexpected response JSON token at \" + path + \".");
                    source.Append("            if (!").Append(_prefix).AppendLine("IsBase64(token.GetString()!))");
                    source.Append("                throw new ").Append(StjException).AppendLine("(\"Invalid base64 response string at \" + path + \".\");");
                }
                else if (type.Kind == GeneratedTypeKind.Named && _dtos.TryGetValue(type.Name, out GeneratedDtoModel? dto))
                {
                    AppendKindCheck(source, "Object", "Expected a response object at \" + path + \".");
                    source.AppendLine("            foreach (var property in token.EnumerateObject())");
                    source.AppendLine("            {");
                    source.AppendLine("                switch (property.Name)");
                    source.AppendLine("                {");
                    foreach (GeneratedDtoPropertyModel property in dto.Properties)
                    {
                        source.Append("                    case ")
                            .Append(GeneratedSourceEmitter.StringLiteral(property.WireName)).AppendLine(":");
                        if (!property.Required && !property.UseSpecified)
                        {
                            // An optional member whose schema does not allow null is sent as absent, never as
                            // null (Json.NET: Required.DisallowNull); its C# type may still be nullable.
                            source.Append("                        if (property.Value.ValueKind == ").Append(StjKind).AppendLine(".Null)");
                            source.Append("                            throw new ").Append(StjException).Append("(\"Non-nullable response value was null at \" + path + \".\" + ")
                                .Append(GeneratedSourceEmitter.StringLiteral(property.WireName)).AppendLine(" + \".\");");
                        }

                        source.Append("                        ").Append(MethodName(property.Type))
                            .Append("(property.Value, path + \".\" + ")
                            .Append(GeneratedSourceEmitter.StringLiteral(property.WireName)).Append(", ")
                            .Append(nullFlag(property.Type)).AppendLine(");");
                        source.AppendLine("                        break;");
                    }

                    source.AppendLine("                    default:");
                    if (!dto.IsOpen)
                    {
                        source.Append("                        throw new ").Append(StjException).AppendLine("(\"Undeclared response property at \" + path + \".\" + property.Name + \".\");");
                    }
                    else
                    {
                        // An open DTO keeps undeclared members in its extension data. When
                        // additionalProperties declares a value schema, those values are validated.
                        if (dto.ExtensionDataValueType is not null)
                        {
                            source.Append("                        ").Append(MethodName(dto.ExtensionDataValueType))
                                .Append("(property.Value, path + \".\" + property.Name, ")
                                .Append(nullFlag(dto.ExtensionDataValueType)).AppendLine(");");
                        }

                        source.AppendLine("                        break;");
                    }

                    source.AppendLine("                }");
                    source.AppendLine("            }");
                    foreach (GeneratedDtoPropertyModel requiredProperty in dto.Properties.Where(
                                 static value => value.Required && !value.Annotations.WriteOnly))
                    {
                        source.Append("            if (!token.TryGetProperty(")
                            .Append(GeneratedSourceEmitter.StringLiteral(requiredProperty.WireName))
                            .AppendLine(", out _))");
                        source.Append("                throw new ").Append(StjException).Append("(\"Missing required response property at \" + path + \".\" + ")
                            .Append(GeneratedSourceEmitter.StringLiteral(requiredProperty.WireName))
                            .AppendLine(" + \".\");");
                    }
                }
                else
                {
                    bool integerEnum = IsIntegerEnum(type);
                    if (type.Kind == GeneratedTypeKind.Int32 || type.Kind == GeneratedTypeKind.Int64)
                    {
                        source.Append("            if (!").Append(_prefix).Append("IsIntegralNumber(token, ")
                            .Append(type.Kind == GeneratedTypeKind.Int32 ? "true" : "false").AppendLine("))");
                    }
                    else if (type.Kind == GeneratedTypeKind.Single || type.Kind == GeneratedTypeKind.Double ||
                        type.Kind == GeneratedTypeKind.Decimal)
                    {
                        source.Append("            if (token.ValueKind != ").Append(StjKind).AppendLine(".Number)");
                    }
                    else if (integerEnum)
                    {
                        source.Append("            if (!").Append(_prefix).AppendLine("IsIntegerLexeme(token))");
                    }
                    else if (type.Kind == GeneratedTypeKind.Boolean)
                    {
                        source.Append("            if (token.ValueKind != ").Append(StjKind).Append(".True && token.ValueKind != ")
                            .Append(StjKind).AppendLine(".False)");
                    }
                    else
                    {
                        source.Append("            if (token.ValueKind != ").Append(StjKind).AppendLine(".String)");
                    }

                    source.Append("                throw new ").Append(StjException).AppendLine("(\"Unexpected response JSON token at \" + path + \".\");");
                    if (type.Kind == GeneratedTypeKind.DateTime ||
                        type.Kind == GeneratedTypeKind.DateTimeOffset || type.Kind == GeneratedTypeKind.Guid)
                    {
                        string validator = type.Kind == GeneratedTypeKind.DateTime
                            ? "IsFullDate"
                            : type.Kind == GeneratedTypeKind.DateTimeOffset ? "IsDateTime" : "IsUuid";
                        source.Append("            if (!").Append(_prefix).Append(validator).AppendLine("(token.GetString()!))");
                        source.Append("                throw new ").Append(StjException).AppendLine("(\"Invalid response string format at \" + path + \".\");");
                    }

                    if (type.Kind == GeneratedTypeKind.NamedEnum &&
                        _enums.TryGetValue(type.Name, out GeneratedEnumModel? enumMembers))
                    {
                        AppendSystemTextJsonEnumMembershipCheck(source, enumMembers);
                    }

                    AppendSystemTextJsonConstCheck(source, type);
                }

                source.AppendLine("        }");
            }

            private static void AppendKindCheck(StringBuilder source, string kind, string message)
            {
                source.Append("            if (token.ValueKind != ").Append(StjKind).Append('.').Append(kind).AppendLine(")");
                source.Append("                throw new ").Append(StjException).Append("(\"").Append(message).AppendLine("\");");
            }

            // The enum converter rejects undeclared values while deserializing; the contract checks them as
            // well so that the error names the path.
            private static void AppendSystemTextJsonEnumMembershipCheck(StringBuilder source, GeneratedEnumModel enumModel)
            {
                source.AppendLine(enumModel.IsInteger
                    ? "            switch (token.GetRawText())"
                    : "            switch (token.GetString()!)");
                source.AppendLine("            {");
                foreach (GeneratedEnumMemberModel member in enumModel.Members)
                {
                    source.Append("                case ")
                        .Append(GeneratedSourceEmitter.StringLiteral(member.WireValue)).AppendLine(":");
                }

                source.AppendLine("                    break;");
                source.AppendLine("                default:");
                source.Append("                    throw new ").Append(StjException).AppendLine("(\"The response value is not a declared enum value at \" + path + \".\");");
                source.AppendLine("            }");
            }

            private void AppendSystemTextJsonConstCheck(StringBuilder source, GeneratedTypeModel type)
            {
                if (!type.ConstValue.HasValue)
                {
                    return;
                }

                OpenApiSemanticLiteral literal = type.ConstValue.Value;
                string failure =
                    "                throw new " + StjException + "(\"The response value does not equal the declared const at \" + path + \".\");";
                switch (literal.Kind)
                {
                    case OpenApiSemanticLiteralKind.String:
                        source.Append("            if (!global::System.String.Equals(token.GetString()!, ")
                            .Append(GeneratedSourceEmitter.StringLiteral(literal.RawText))
                            .AppendLine(", global::System.StringComparison.Ordinal))");
                        source.AppendLine(failure);
                        break;
                    case OpenApiSemanticLiteralKind.Boolean:
                        source.Append("            if (token.GetBoolean() != ")
                            .Append(literal.RawText).AppendLine(")");
                        source.AppendLine(failure);
                        break;
                    case OpenApiSemanticLiteralKind.Integer:
                    case OpenApiSemanticLiteralKind.Number:
                        source.Append("            if (!").Append(_prefix).Append("NumberEquals(token, ")
                            .Append(GeneratedSourceEmitter.StringLiteral(literal.RawText)).AppendLine("))");
                        source.AppendLine(failure);
                        break;
                }
            }
        }
    }
}
