using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// oneOf / anyOf のラッパークラスと System.Text.Json の converter を出力する。公開 API は Newtonsoft.Json 版と同じで、
    /// raw JSON 形式の名前だけが FromJsonElement / ToJsonElement になる（Newtonsoft 版は FromJToken / ToJToken）。
    /// ラッパーは sealed クラスでメンバー DTO は素の DTO のまま。ラッパーの converter がメンバーの書き出しで再帰することはない。
    /// discriminator があればラッパーは具体 DTO を保持し、converter が discriminator の値で振り分ける。なければレスポンスの
    /// JsonElement（Clone 済み）または呼び出し側が渡した候補値を保持する。
    /// Emits the oneOf / anyOf wrapper classes and their System.Text.Json converters. The public API of
    /// the wrapper is the one of the Newtonsoft.Json output; only the raw JSON form differs by name:
    /// FromJsonElement / ToJsonElement instead of FromJToken / ToJToken.
    ///
    /// A union is a sealed wrapper class and the member DTOs stay plain DTOs, so the converter attached to
    /// the wrapper is never picked up when a member is written (no recursion).
    ///
    /// With a discriminator the wrapper holds the concrete DTO and the converter dispatches on the
    /// discriminator value. Without one it holds the raw JsonElement of a response, or the candidate
    /// value that the caller supplied.
    /// </summary>
    internal sealed class SystemTextJsonPolymorphicConverterEmitter
    {
        private const string Json = "global::System.Text.Json";
        private const string Element = "global::System.Text.Json.JsonElement";
        private const string Options = "global::System.Text.Json.JsonSerializerOptions";

        private static readonly JsonSerializerProfile Profile = JsonSerializerProfile.SystemTextJson;

        // The note of TryGet / As of a union without a discriminator (documentation of the generated code).
        private const string RawReaderNote =
            "Reads the candidate from the held JSON. A value that was read from a response uses the options of the client " +
            "(its converters apply). A value created by FromJsonElement is read with default JsonSerializerOptions, so the " +
            "converters of the client (full-date, an integer written as 1.0, ...) do not apply to it.";

        internal IReadOnlyList<GeneratedFile> Emit(
            OpenApiGenerationModel model,
            HashSet<string> usedTypeNames,
            string? preserveAttribute,
            bool suppressObsoleteWarnings)
        {
            var files = new List<GeneratedFile>();
            foreach (GeneratedUnionModel union in model.Unions.OrderBy(
                         static value => value.Name,
                         StringComparer.Ordinal))
            {
                string converterName = union.Name + "JsonConverter";
                int suffix = 2;
                while (!usedTypeNames.Add(converterName))
                {
                    converterName = union.Name + "JsonConverter" + suffix++;
                }

                files.Add(Emit(model, union, converterName, preserveAttribute, suppressObsoleteWarnings));
            }

            return files;
        }

        private static string TypeName(GeneratedTypeModel type)
        {
            return GeneratedSourceEmitter.TypeName(type.WithNullable(false), Profile);
        }

        // JsonElement is a struct, so an Any member needs no null check.
        private static bool IsValueType(GeneratedTypeModel type)
        {
            return type.IsValueType || type.Kind == GeneratedTypeKind.Any;
        }

        private static GeneratedFile Emit(
            OpenApiGenerationModel model,
            GeneratedUnionModel union,
            string converterName,
            string? preserveAttribute,
            bool suppressObsoleteWarnings)
        {
            var source = new StringBuilder();
            source.AppendLine("#nullable enable");
            if (suppressObsoleteWarnings)
            {
                source.AppendLine(GeneratedDocumentationEmitter.PragmaLine);
            }

            source.Append("namespace ").Append(model.GeneratedNamespace).AppendLine();
            source.AppendLine("{");
            if (union.DiscriminatorPropertyName is null)
            {
                AppendRawUnion(source, model, union, converterName);
            }
            else
            {
                AppendDiscriminatedUnion(source, union, converterName);
            }

            source.AppendLine();
            if (preserveAttribute is not null)
            {
                // The converter is created by reflection; keep it under Unity managed stripping.
                source.AppendLine("    [global::" + model.GeneratedNamespace + "." + preserveAttribute + "]");
            }

            source.Append("    internal sealed class ").Append(converterName)
                .Append(" : ").Append(Json).Append(".Serialization.JsonConverter<").Append(union.Name).AppendLine(">");
            source.AppendLine("    {");
            if (union.DiscriminatorPropertyName is null)
            {
                AppendRawConverterBody(source, union);
            }
            else
            {
                AppendDiscriminatedConverterBody(source, union);
            }

            source.AppendLine("    }");
            source.AppendLine("}");
            return GeneratedSourceEmitter.Create(union.Name + ".g.cs", source.ToString());
        }

        // ---- discriminated union -------------------------------------------------------------------

        private static void AppendDiscriminatedUnion(
            StringBuilder source,
            GeneratedUnionModel union,
            string converterName)
        {
            string value = PolymorphicConverterEmitter.Unique("Value", union.Name);
            string discriminatorValue = PolymorphicConverterEmitter.Unique("DiscriminatorValue", union.Name);
            string propertyNameConstant = PolymorphicConverterEmitter.Unique("DiscriminatorPropertyName", union.Name);
            AppendClassHeader(source, union, converterName);
            source.AppendLine("    {");
            source.Append("        private static readonly string[] _discriminatorValues = new string[] { ")
                .Append(string.Join(", ", union.Members.Select(static member =>
                    GeneratedSourceEmitter.StringLiteral(member.DiscriminatorValues[0]))))
                .AppendLine(" };");
            source.AppendLine("        private readonly object _value;");
            source.AppendLine("        private readonly int _index;");
            source.AppendLine();
            source.Append("        private ").Append(union.Name).AppendLine("(int index, object value)");
            source.AppendLine("        {");
            source.AppendLine("            _index = index;");
            source.AppendLine("            _value = value;");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        /// <summary>Name of the property whose value selects the member.</summary>");
            source.Append("        public const string ").Append(propertyNameConstant).Append(" = ")
                .Append(GeneratedSourceEmitter.StringLiteral(union.DiscriminatorPropertyName!)).AppendLine(";");
            source.AppendLine();
            source.AppendLine("        /// <summary>The concrete value (one of the member types).</summary>");
            source.Append("        public object ").Append(value).AppendLine(" => _value;");
            source.AppendLine();
            source.AppendLine("        /// <summary>The discriminator value that identifies the held member.</summary>");
            source.Append("        public string ").Append(discriminatorValue).AppendLine(" => _discriminatorValues[_index];");
            for (int index = 0; index < union.Members.Count; index++)
            {
                GeneratedUnionMemberModel member = union.Members[index];
                string typeName = TypeName(member.Type);
                source.AppendLine();
                source.Append("        public static ").Append(union.Name).Append(" From").Append(member.AccessorName)
                    .Append('(').Append(typeName).AppendLine(" value)");
                source.AppendLine("        {");
                source.AppendLine("            if (value == null)");
                source.AppendLine("            {");
                source.AppendLine("                throw new global::System.ArgumentNullException(nameof(value));");
                source.AppendLine("            }");
                source.Append("            return new ").Append(union.Name).Append('(')
                    .Append(index).AppendLine(", value);");
                source.AppendLine("        }");
                source.AppendLine();
                source.Append("        public static implicit operator ").Append(union.Name).Append('(')
                    .Append(typeName).AppendLine(" value)");
                source.AppendLine("        {");
                source.Append("            return From").Append(member.AccessorName).AppendLine("(value);");
                source.AppendLine("        }");
                PolymorphicConverterEmitter.AppendTypedAccessors(source, member, index, typeName);
            }

            source.AppendLine("    }");
        }

        private static void AppendDiscriminatedConverterBody(StringBuilder source, GeneratedUnionModel union)
        {
            string value = PolymorphicConverterEmitter.Unique("Value", union.Name);
            string discriminatorValue = PolymorphicConverterEmitter.Unique("DiscriminatorValue", union.Name);
            string name = union.Name;
            string propertyLiteral = GeneratedSourceEmitter.StringLiteral(union.DiscriminatorPropertyName!);
            source.Append("        public override ").Append(name).Append("? Read(ref ").Append(Json)
                .Append(".Utf8JsonReader reader, global::System.Type typeToConvert, ").Append(Options).AppendLine(" options)");
            source.AppendLine("        {");
            source.Append("            if (reader.TokenType == ").Append(Json).AppendLine(".JsonTokenType.Null)");
            source.AppendLine("            {");
            source.AppendLine("                return null;");
            source.AppendLine("            }");
            source.Append("            using (").Append(Json).AppendLine(".JsonDocument document = global::System.Text.Json.JsonDocument.ParseValue(ref reader))");
            source.AppendLine("            {");
            source.AppendLine("                var token = document.RootElement;");
            source.Append("                if (token.ValueKind != ").Append(Json).AppendLine(".JsonValueKind.Object)");
            source.AppendLine("                {");
            source.Append("                    throw new ").Append(Json).Append(".JsonException(")
                .Append(GeneratedSourceEmitter.StringLiteral("Expected a JSON object for " + name + "."))
                .AppendLine(");");
            source.AppendLine("                }");
            if (union.DefaultMemberIndex >= 0)
            {
                // OpenAPI 3.2 defaultMapping: a missing, non-string or unmapped value selects the default member.
                source.Append("                string? discriminatorText = token.TryGetProperty(").Append(propertyLiteral)
                    .Append(", out var discriminator) && discriminator.ValueKind == ").Append(Json)
                    .AppendLine(".JsonValueKind.String ? discriminator.GetString() : null;");
            }
            else
            {
                source.Append("                if (!token.TryGetProperty(").Append(propertyLiteral)
                    .Append(", out var discriminator) || discriminator.ValueKind != ").Append(Json)
                    .AppendLine(".JsonValueKind.String)");
                source.AppendLine("                {");
                source.Append("                    throw new ").Append(Json).Append(".JsonException(")
                    .Append(GeneratedSourceEmitter.StringLiteral(
                        "The discriminator property '" + union.DiscriminatorPropertyName + "' of " + name +
                        " is missing or is not a string."))
                    .AppendLine(");");
                source.AppendLine("                }");
                source.AppendLine("                string discriminatorText = discriminator.GetString()!;");
            }

            source.AppendLine("                switch (discriminatorText)");
            source.AppendLine("                {");
            for (int index = 0; index < union.Members.Count; index++)
            {
                GeneratedUnionMemberModel member = union.Members[index];
                string typeName = TypeName(member.Type);
                foreach (string wireValue in member.DiscriminatorValues)
                {
                    source.Append("                    case ").Append(GeneratedSourceEmitter.StringLiteral(wireValue)).AppendLine(":");
                }

                source.Append("                        return ").Append(name).Append(".From").Append(member.AccessorName)
                    .Append("(").Append(Json).Append(".JsonSerializer.Deserialize<").Append(typeName)
                    .AppendLine(">(token, options)!);");
            }

            source.AppendLine("                    default:");
            if (union.DefaultMemberIndex >= 0)
            {
                GeneratedUnionMemberModel defaultMember = union.Members[union.DefaultMemberIndex];
                source.Append("                        return ").Append(name).Append(".From").Append(defaultMember.AccessorName)
                    .Append("(").Append(Json).Append(".JsonSerializer.Deserialize<").Append(TypeName(defaultMember.Type))
                    .AppendLine(">(token, options)!);");
            }
            else
            {
                source.Append("                        throw new ").Append(Json).Append(".JsonException(")
                    .Append(GeneratedSourceEmitter.StringLiteral("The discriminator value '"))
                    .Append(" + discriminatorText + ")
                    .Append(GeneratedSourceEmitter.StringLiteral(
                        "' of property '" + union.DiscriminatorPropertyName + "' does not select a schema of " + name + "."))
                    .AppendLine(");");
            }

            source.AppendLine("                }");
            source.AppendLine("            }");
            source.AppendLine("        }");
            source.AppendLine();
            source.Append("        public override void Write(").Append(Json).Append(".Utf8JsonWriter writer, ")
                .Append(name).Append(" value, ").Append(Options).AppendLine(" options)");
            source.AppendLine("        {");
            source.AppendLine("            // The member is a plain DTO without a converter, so serializing it here cannot reach this converter again.");
            source.Append("            ").Append(Element).Append(" member = ").Append(Json)
                .Append(".JsonSerializer.SerializeToElement(value.").Append(value).Append(", value.").Append(value)
                .AppendLine(".GetType(), options);");
            source.AppendLine("            writer.WriteStartObject();");
            source.AppendLine("            bool discriminatorWritten = false;");
            source.Append("            foreach (").Append(Json).AppendLine(".JsonProperty property in member.EnumerateObject())");
            source.AppendLine("            {");
            source.Append("                if (global::System.String.Equals(property.Name, ").Append(propertyLiteral)
                .AppendLine(", global::System.StringComparison.Ordinal))");
            source.AppendLine("                {");
            source.AppendLine("                    // The union wins over a stale value of the member.");
            source.Append("                    writer.WriteString(").Append(propertyLiteral).Append(", value.")
                .Append(discriminatorValue).AppendLine(");");
            source.AppendLine("                    discriminatorWritten = true;");
            source.AppendLine("                }");
            source.AppendLine("                else");
            source.AppendLine("                {");
            source.AppendLine("                    property.WriteTo(writer);");
            source.AppendLine("                }");
            source.AppendLine("            }");
            source.AppendLine("            if (!discriminatorWritten)");
            source.AppendLine("            {");
            source.Append("                writer.WriteString(").Append(propertyLiteral).Append(", value.")
                .Append(discriminatorValue).AppendLine(");");
            source.AppendLine("            }");
            source.AppendLine("            writer.WriteEndObject();");
            source.AppendLine("        }");
        }

        // ---- union without a discriminator ---------------------------------------------------------

        private static void AppendRawUnion(
            StringBuilder source,
            OpenApiGenerationModel model,
            GeneratedUnionModel union,
            string converterName)
        {
            AppendClassHeader(source, union, converterName);
            source.AppendLine("    {");
            source.Append("        private static readonly ").Append(Options).Append(" _defaultOptions = new ")
                .Append(Options).AppendLine("();");
            source.Append("        private readonly ").Append(Element).AppendLine("? _token;");
            source.Append("        private readonly ").Append(Options).AppendLine("? _options;");
            source.AppendLine("        private readonly object? _value;");
            source.AppendLine("        private readonly int _index;");
            source.AppendLine();
            source.Append("        private ").Append(union.Name).Append('(').Append(Element)
                .Append("? token, object? value, int index, ").Append(Options).AppendLine("? options)");
            source.AppendLine("        {");
            source.AppendLine("            _token = token;");
            source.AppendLine("            _value = value;");
            source.AppendLine("            _index = index;");
            source.AppendLine("            _options = options;");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        /// <summary>Wraps raw JSON (a copy is kept); the TryGet / As members read it as the candidate types.</summary>");
            source.Append("        public static ").Append(union.Name).Append(" FromJsonElement(").Append(Element).AppendLine(" element)");
            source.AppendLine("        {");
            source.Append("            if (element.ValueKind == ").Append(Json).AppendLine(".JsonValueKind.Undefined)");
            source.AppendLine("            {");
            source.AppendLine("                throw new global::System.ArgumentException(\"The JSON element is not initialized.\", nameof(element));");
            source.AppendLine("            }");
            source.Append("            return new ").Append(union.Name).AppendLine("(element.Clone(), null, -1, null);");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        // The converter hands over the options it was called with, so that the candidates are read with the converters of the client.");
            source.Append("        internal static ").Append(union.Name).Append(" FromReader(").Append(Element)
                .Append(" element, ").Append(Options).AppendLine(" options)");
            source.AppendLine("        {");
            source.Append("            return new ").Append(union.Name).AppendLine("(element.Clone(), null, -1, options);");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        /// <summary>The JSON form: the response JSON as received, or the supplied value converted with default options.</summary>");
            source.Append("        public ").Append(Element).AppendLine(" ToJsonElement()");
            source.AppendLine("        {");
            source.AppendLine("            return ToJsonElement(null);");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        /// <summary>The JSON form; a supplied value is converted with <paramref name=\"options\"/>.</summary>");
            source.Append("        public ").Append(Element).Append(" ToJsonElement(").Append(Options).AppendLine("? options)");
            source.AppendLine("        {");
            source.AppendLine("            if (_token.HasValue)");
            source.AppendLine("            {");
            source.AppendLine("                return _token.Value;");
            source.AppendLine("            }");
            source.Append("            return ").Append(Json)
                .AppendLine(".JsonSerializer.SerializeToElement(_value!, _value!.GetType(), options);");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        internal object? TypedValue => _value;");
            source.AppendLine();
            source.Append("        internal ").Append(Element).AppendLine("? RawToken => _token;");
            var implicitTypes = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < union.Members.Count; index++)
            {
                GeneratedUnionMemberModel member = union.Members[index];
                GeneratedTypeModel type = member.Type.WithNullable(false);
                string typeName = TypeName(type);
                source.AppendLine();
                source.Append("        public static ").Append(union.Name).Append(" From").Append(member.AccessorName)
                    .Append('(').Append(typeName).AppendLine(" value)");
                source.AppendLine("        {");
                if (!IsValueType(type))
                {
                    source.AppendLine("            if (value == null)");
                    source.AppendLine("            {");
                    source.AppendLine("                throw new global::System.ArgumentNullException(nameof(value));");
                    source.AppendLine("            }");
                }

                source.Append("            return new ").Append(union.Name).Append("(null, value, ")
                    .Append(index).AppendLine(", null);");
                source.AppendLine("        }");
                if (type.Kind != GeneratedTypeKind.Any && implicitTypes.Add(typeName))
                {
                    source.AppendLine();
                    source.Append("        public static implicit operator ").Append(union.Name).Append('(')
                        .Append(typeName).AppendLine(" value)");
                    source.AppendLine("        {");
                    source.Append("            return From").Append(member.AccessorName).AppendLine("(value);");
                    source.AppendLine("        }");
                }

                PolymorphicConverterEmitter.AppendTypedAccessors(
                    source, member, index, typeName,
                    tokenReader: true,
                    oneOfSelection: union.IsOneOf,
                    tokenCondition: "_token.HasValue",
                    tokenValue: "_token.Value",
                    readerNote: RawReaderNote);
                source.AppendLine();
                source.Append("        private bool TryRead").Append(index).Append('(').Append(Element)
                    .Append(" token, out ").Append(typeName).AppendLine(" value)");
                source.AppendLine("        {");
                source.AppendLine("            value = default!;");
                source.Append("            if (!(").Append(GetTokenPrecheck(type, model))
                    .Append(PolymorphicConverterEmitter.GetClosedCheck(type, model)).AppendLine("))");
                source.AppendLine("            {");
                source.AppendLine("                return false;");
                source.AppendLine("            }");
                source.AppendLine("            try");
                source.AppendLine("            {");
                source.Append("                object? parsed = ").Append(Json).Append(".JsonSerializer.Deserialize(token, typeof(")
                    .Append(typeName).AppendLine("), _options ?? _defaultOptions);");
                source.AppendLine("                if (parsed == null)");
                source.AppendLine("                {");
                source.AppendLine("                    return false;");
                source.AppendLine("                }");
                source.Append("                value = (").Append(typeName).AppendLine(")parsed;");
                source.AppendLine("                return true;");
                source.AppendLine("            }");
                source.AppendLine("            catch (global::System.Exception exception) when (IsConversionFailure(exception))");
                source.AppendLine("            {");
                source.AppendLine("                return false;");
                source.AppendLine("            }");
                source.AppendLine("        }");
            }

            if (union.IsOneOf)
            {
                AppendOneOfSelection(source, model, union);
            }

            source.AppendLine();
            source.Append("        private static bool AllDeclared(").Append(Element).AppendLine(" token, string[] names)");
            source.AppendLine("        {");
            source.Append("            if (token.ValueKind != ").Append(Json).AppendLine(".JsonValueKind.Object) return true;");
            source.AppendLine("            foreach (var property in token.EnumerateObject())");
            source.AppendLine("            {");
            source.AppendLine("                if (global::System.Array.IndexOf(names, property.Name) < 0) return false;");
            source.AppendLine("            }");
            source.AppendLine("            return true;");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        // JSON Schema integers are the numbers written without a fraction and exponent here (Json.NET reads 1.0 as a float).");
            source.Append("        private static bool IsIntegerLexeme(").Append(Element).AppendLine(" token)");
            source.AppendLine("        {");
            source.Append("            return token.ValueKind == ").Append(Json)
                .AppendLine(".JsonValueKind.Number && token.GetRawText().IndexOfAny(new[] { '.', 'e', 'E' }) < 0;");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        private static bool IsConversionFailure(global::System.Exception exception)");
            source.AppendLine("        {");
            source.Append("            return exception is ").Append(Json).AppendLine(".JsonException ||");
            source.AppendLine("                   exception is global::System.FormatException ||");
            source.AppendLine("                   exception is global::System.InvalidCastException ||");
            source.AppendLine("                   exception is global::System.OverflowException ||");
            source.AppendLine("                   exception is global::System.ArgumentException;");
            source.AppendLine("        }");
            source.AppendLine("    }");
        }

        // oneOf: when several candidates read the JSON, the ones that declare every property of it win
        // (the tie-break of the response contract); the member is selected only if exactly one is left.
        private static void AppendOneOfSelection(
            StringBuilder source,
            OpenApiGenerationModel model,
            GeneratedUnionModel union)
        {
            source.AppendLine();
            source.AppendLine("        private bool Matches(int index)");
            source.AppendLine("        {");
            source.AppendLine("            switch (index)");
            source.AppendLine("            {");
            for (int index = 0; index < union.Members.Count; index++)
            {
                source.Append("                case ").Append(index).Append(": return TryRead").Append(index)
                    .AppendLine("(_token!.Value, out _);");
            }

            source.AppendLine("                default: return false;");
            source.AppendLine("            }");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        private bool IsStrict(int index)");
            source.AppendLine("        {");
            source.AppendLine("            switch (index)");
            source.AppendLine("            {");
            for (int index = 0; index < union.Members.Count; index++)
            {
                GeneratedTypeModel type = union.Members[index].Type;
                GeneratedDtoModel? dto = type.Kind == GeneratedTypeKind.Named
                    ? model.Dtos.FirstOrDefault(value => string.Equals(value.Name, type.Name, StringComparison.Ordinal))
                    : null;
                if (dto is not null)
                {
                    source.Append("                case ").Append(index).Append(": return AllDeclared(_token!.Value, new string[] { ")
                        .Append(string.Join(", ", dto.Properties.Select(static property =>
                            GeneratedSourceEmitter.StringLiteral(property.WireName))))
                        .AppendLine(" });");
                }
            }

            source.AppendLine("                default: return true;");
            source.AppendLine("            }");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        private bool IsSelected(int index)");
            source.AppendLine("        {");
            source.AppendLine("            if (!Matches(index)) return false;");
            source.AppendLine("            int matches = 0;");
            source.Append("            for (int candidate = 0; candidate < ").Append(union.Members.Count).AppendLine("; candidate++)");
            source.AppendLine("            {");
            source.AppendLine("                if (Matches(candidate)) matches++;");
            source.AppendLine("            }");
            source.AppendLine("            if (matches <= 1) return true;");
            source.AppendLine("            if (!IsStrict(index)) return false;");
            source.AppendLine("            int strict = 0;");
            source.Append("            for (int candidate = 0; candidate < ").Append(union.Members.Count).AppendLine("; candidate++)");
            source.AppendLine("            {");
            source.AppendLine("                if (Matches(candidate) && IsStrict(candidate)) strict++;");
            source.AppendLine("            }");
            source.AppendLine("            return strict == 1;");
            source.AppendLine("        }");
        }

        private static void AppendRawConverterBody(StringBuilder source, GeneratedUnionModel union)
        {
            source.Append("        public override ").Append(union.Name).Append("? Read(ref ").Append(Json)
                .Append(".Utf8JsonReader reader, global::System.Type typeToConvert, ").Append(Options).AppendLine(" options)");
            source.AppendLine("        {");
            source.Append("            if (reader.TokenType == ").Append(Json).AppendLine(".JsonTokenType.Null)");
            source.AppendLine("            {");
            source.AppendLine("                return null;");
            source.AppendLine("            }");
            source.Append("            using (").Append(Json).AppendLine(".JsonDocument document = global::System.Text.Json.JsonDocument.ParseValue(ref reader))");
            source.AppendLine("            {");
            source.Append("                return ").Append(union.Name).AppendLine(".FromReader(document.RootElement, options);");
            source.AppendLine("            }");
            source.AppendLine("        }");
            source.AppendLine();
            source.Append("        public override void Write(").Append(Json).Append(".Utf8JsonWriter writer, ")
                .Append(union.Name).Append(" value, ").Append(Options).AppendLine(" options)");
            source.AppendLine("        {");
            source.AppendLine("            if (value.TypedValue != null)");
            source.AppendLine("            {");
            source.Append("                ").Append(Json)
                .AppendLine(".JsonSerializer.Serialize(writer, value.TypedValue, value.TypedValue.GetType(), options);");
            source.AppendLine("                return;");
            source.AppendLine("            }");
            source.AppendLine("            value.RawToken!.Value.WriteTo(writer);");
            source.AppendLine("        }");
        }

        // ---- shared pieces -------------------------------------------------------------------------

        private static void AppendClassHeader(
            StringBuilder source,
            GeneratedUnionModel union,
            string converterName)
        {
            GeneratedDocumentationEmitter.AppendSummary(source, "    ", union.Annotations, includeDefaultAndDirection: false);
            GeneratedDocumentationEmitter.AppendObsolete(source, "    ", union.Annotations.Deprecated);
            source.Append("    [").Append(Json).Append(".Serialization.JsonConverter(typeof(").Append(converterName).AppendLine("))]");
            source.Append("    public sealed class ").Append(union.Name).AppendLine();
        }

        private static string GetTokenPrecheck(GeneratedTypeModel type, OpenApiGenerationModel model)
        {
            const string Kind = "token.ValueKind == global::System.Text.Json.JsonValueKind.";
            switch (type.Kind)
            {
                case GeneratedTypeKind.String:
                case GeneratedTypeKind.DateTime:
                case GeneratedTypeKind.DateTimeOffset:
                case GeneratedTypeKind.Guid:
                case GeneratedTypeKind.Binary:
                    return Kind + "String";
                case GeneratedTypeKind.NamedEnum:
                    GeneratedEnumModel? enumModel = model.Enums.FirstOrDefault(
                        value => string.Equals(value.Name, type.Name, StringComparison.Ordinal));
                    return enumModel is not null && enumModel.IsInteger ? "IsIntegerLexeme(token)" : Kind + "String";
                case GeneratedTypeKind.Int32:
                case GeneratedTypeKind.Int64:
                    return "IsIntegerLexeme(token)";
                case GeneratedTypeKind.Single:
                case GeneratedTypeKind.Double:
                case GeneratedTypeKind.Decimal:
                    return Kind + "Number";
                case GeneratedTypeKind.Boolean:
                    return Kind + "True || " + Kind + "False";
                case GeneratedTypeKind.Named:
                case GeneratedTypeKind.Map:
                case GeneratedTypeKind.FreeObject:
                    return Kind + "Object";
                case GeneratedTypeKind.Array:
                    return Kind + "Array";
                default:
                    return "true";
            }
        }
    }
}
