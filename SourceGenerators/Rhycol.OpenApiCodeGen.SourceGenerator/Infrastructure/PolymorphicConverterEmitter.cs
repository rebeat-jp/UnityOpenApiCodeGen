using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// Emits the oneOf / anyOf wrapper classes and their JsonConverters.
    ///
    /// A union is a sealed wrapper class. The member DTOs stay plain sealed DTOs and do not derive
    /// from the wrapper, so the converter that is attached to the wrapper is never picked up when
    /// a concrete DTO is written (a converter on a base type is inherited by the subtypes and would
    /// recurse forever).
    ///
    /// With a discriminator the wrapper holds the concrete DTO and the converter dispatches on the
    /// discriminator value. Without one it holds the raw JToken of a response, or the candidate
    /// value that the caller supplied.
    /// </summary>
    internal sealed class PolymorphicConverterEmitter
    {
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
                // Json.NET creates the converter by reflection; keep it under Unity managed stripping.
                source.AppendLine("    [" + preserveAttribute + "]");
            }

            source.Append("    internal sealed class ").Append(converterName)
                .AppendLine(" : global::Newtonsoft.Json.JsonConverter");
            source.AppendLine("    {");
            source.AppendLine("        public override bool CanConvert(global::System.Type objectType)");
            source.AppendLine("        {");
            source.Append("            return objectType == typeof(").Append(union.Name).AppendLine(");");
            source.AppendLine("        }");
            source.AppendLine();
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
            string value = Unique("Value", union.Name);
            string discriminatorValue = Unique("DiscriminatorValue", union.Name);
            string propertyNameConstant = Unique("DiscriminatorPropertyName", union.Name);
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
                string typeName = GeneratedSourceEmitter.TypeName(member.Type.WithNullable(false));
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
                AppendTypedAccessors(source, member, index, typeName);
            }

            source.AppendLine("    }");
        }

        private static void AppendDiscriminatedConverterBody(StringBuilder source, GeneratedUnionModel union)
        {
            string value = Unique("Value", union.Name);
            string discriminatorValue = Unique("DiscriminatorValue", union.Name);
            string name = union.Name;
            string propertyLiteral = GeneratedSourceEmitter.StringLiteral(union.DiscriminatorPropertyName!);
            source.AppendLine("        public override object? ReadJson(global::Newtonsoft.Json.JsonReader reader, global::System.Type objectType, object? existingValue, global::Newtonsoft.Json.JsonSerializer serializer)");
            source.AppendLine("        {");
            source.AppendLine("            if (reader.TokenType == global::Newtonsoft.Json.JsonToken.Null)");
            source.AppendLine("            {");
            source.AppendLine("                return null;");
            source.AppendLine("            }");
            source.AppendLine("            var token = global::Newtonsoft.Json.Linq.JToken.ReadFrom(reader);");
            source.AppendLine("            if (!(token is global::Newtonsoft.Json.Linq.JObject objectToken))");
            source.AppendLine("            {");
            source.Append("                throw new global::Newtonsoft.Json.JsonSerializationException(")
                .Append(GeneratedSourceEmitter.StringLiteral("Expected a JSON object for " + name + "."))
                .AppendLine(");");
            source.AppendLine("            }");
            source.Append("            var discriminator = objectToken[").Append(propertyLiteral).AppendLine("];");
            if (union.DefaultMemberIndex >= 0)
            {
                // OpenAPI 3.2 defaultMapping: a missing, non-string or unmapped value selects the default member.
                source.AppendLine("            string? discriminatorText = discriminator != null && discriminator.Type == global::Newtonsoft.Json.Linq.JTokenType.String ? (string?)discriminator : null;");
            }
            else
            {
                source.AppendLine("            if (discriminator == null || discriminator.Type != global::Newtonsoft.Json.Linq.JTokenType.String)");
                source.AppendLine("            {");
                source.Append("                throw new global::Newtonsoft.Json.JsonSerializationException(")
                    .Append(GeneratedSourceEmitter.StringLiteral(
                        "The discriminator property '" + union.DiscriminatorPropertyName + "' of " + name +
                        " is missing or is not a string."))
                    .AppendLine(");");
                source.AppendLine("            }");
                source.AppendLine("            string discriminatorText = (string)discriminator!;");
            }

            source.AppendLine("            switch (discriminatorText)");
            source.AppendLine("            {");
            for (int index = 0; index < union.Members.Count; index++)
            {
                GeneratedUnionMemberModel member = union.Members[index];
                string typeName = GeneratedSourceEmitter.TypeName(member.Type.WithNullable(false));
                foreach (string wireValue in member.DiscriminatorValues)
                {
                    source.Append("                case ").Append(GeneratedSourceEmitter.StringLiteral(wireValue)).AppendLine(":");
                }

                source.Append("                    return ").Append(name).Append(".From").Append(member.AccessorName)
                    .Append("(objectToken.ToObject<").Append(typeName).AppendLine(">(serializer)!);");
            }

            source.AppendLine("                default:");
            if (union.DefaultMemberIndex >= 0)
            {
                GeneratedUnionMemberModel defaultMember = union.Members[union.DefaultMemberIndex];
                string defaultTypeName = GeneratedSourceEmitter.TypeName(defaultMember.Type.WithNullable(false));
                source.Append("                    return ").Append(name).Append(".From").Append(defaultMember.AccessorName)
                    .Append("(objectToken.ToObject<").Append(defaultTypeName).AppendLine(">(serializer)!);");
            }
            else
            {
                source.Append("                    throw new global::Newtonsoft.Json.JsonSerializationException(")
                    .Append(GeneratedSourceEmitter.StringLiteral(
                        "The discriminator value '"))
                    .Append(" + discriminatorText + ")
                    .Append(GeneratedSourceEmitter.StringLiteral(
                        "' of property '" + union.DiscriminatorPropertyName + "' does not select a schema of " + name + "."))
                    .AppendLine(");");
            }

            source.AppendLine("            }");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        public override void WriteJson(global::Newtonsoft.Json.JsonWriter writer, object? value, global::Newtonsoft.Json.JsonSerializer serializer)");
            source.AppendLine("        {");
            source.AppendLine("            if (value == null)");
            source.AppendLine("            {");
            source.AppendLine("                writer.WriteNull();");
            source.AppendLine("                return;");
            source.AppendLine("            }");
            source.Append("            var union = (").Append(name).AppendLine(")value;");
            source.AppendLine("            // The member is a plain DTO without a converter, so serializing it here cannot reach this converter again.");
            source.Append("            var token = global::Newtonsoft.Json.Linq.JObject.FromObject(union.").Append(value)
                .AppendLine(", serializer);");
            source.Append("            token[").Append(propertyLiteral).Append("] = union.").Append(discriminatorValue)
                .AppendLine(";");
            source.AppendLine("            token.WriteTo(writer);");
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
            source.AppendLine("        private readonly global::Newtonsoft.Json.Linq.JToken? _token;");
            source.AppendLine("        private readonly object? _value;");
            source.AppendLine("        private readonly int _index;");
            source.AppendLine();
            source.Append("        private ").Append(union.Name)
                .AppendLine("(global::Newtonsoft.Json.Linq.JToken? token, object? value, int index)");
            source.AppendLine("        {");
            source.AppendLine("            _token = token;");
            source.AppendLine("            _value = value;");
            source.AppendLine("            _index = index;");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        /// <summary>Wraps raw JSON; the TryGet / As members read it as the candidate types.</summary>");
            source.Append("        public static ").Append(union.Name)
                .AppendLine(" FromJToken(global::Newtonsoft.Json.Linq.JToken token)");
            source.AppendLine("        {");
            source.AppendLine("            if (token == null)");
            source.AppendLine("            {");
            source.AppendLine("                throw new global::System.ArgumentNullException(nameof(token));");
            source.AppendLine("            }");
            source.Append("            return new ").Append(union.Name).AppendLine("(token, null, -1);");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        /// <summary>The JSON form: the response JSON as received, or the supplied value converted with default settings.</summary>");
            source.AppendLine("        public global::Newtonsoft.Json.Linq.JToken ToJToken()");
            source.AppendLine("        {");
            source.AppendLine("            return _token ?? global::Newtonsoft.Json.Linq.JToken.FromObject(_value!);");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        internal object? TypedValue => _value;");
            source.AppendLine();
            source.AppendLine("        internal global::Newtonsoft.Json.Linq.JToken? RawToken => _token;");
            var implicitTypes = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < union.Members.Count; index++)
            {
                GeneratedUnionMemberModel member = union.Members[index];
                GeneratedTypeModel type = member.Type.WithNullable(false);
                string typeName = GeneratedSourceEmitter.TypeName(type);
                source.AppendLine();
                source.Append("        public static ").Append(union.Name).Append(" From").Append(member.AccessorName)
                    .Append('(').Append(typeName).AppendLine(" value)");
                source.AppendLine("        {");
                if (!type.IsValueType)
                {
                    source.AppendLine("            if (value == null)");
                    source.AppendLine("            {");
                    source.AppendLine("                throw new global::System.ArgumentNullException(nameof(value));");
                    source.AppendLine("            }");
                }

                source.Append("            return new ").Append(union.Name).Append("(null, value, ")
                    .Append(index).AppendLine(");");
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

                AppendTypedAccessors(source, member, index, typeName, tokenReader: true, oneOfSelection: union.IsOneOf);
                source.AppendLine();
                source.Append("        private static bool TryRead").Append(index).Append("(global::Newtonsoft.Json.Linq.JToken token, out ")
                    .Append(typeName).AppendLine(" value)");
                source.AppendLine("        {");
                source.AppendLine("            value = default!;");
                source.Append("            if (!(").Append(GetTokenPrecheck(type, model)).Append(GetClosedCheck(type, model)).AppendLine("))");
                source.AppendLine("            {");
                source.AppendLine("                return false;");
                source.AppendLine("            }");
                source.AppendLine("            try");
                source.AppendLine("            {");
                source.Append("                object? parsed = token.ToObject(typeof(").Append(typeName)
                    .AppendLine("), CreateStrictSerializer());");
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
            source.AppendLine("        private static bool AllDeclared(global::Newtonsoft.Json.Linq.JToken token, string[] names)");
            source.AppendLine("        {");
            source.AppendLine("            if (!(token is global::Newtonsoft.Json.Linq.JObject objectToken)) return true;");
            source.AppendLine("            foreach (var property in objectToken.Properties())");
            source.AppendLine("            {");
            source.AppendLine("                if (global::System.Array.IndexOf(names, property.Name) < 0) return false;");
            source.AppendLine("            }");
            source.AppendLine("            return true;");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        private static global::Newtonsoft.Json.JsonSerializer CreateStrictSerializer()");
            source.AppendLine("        {");
            source.AppendLine("            var serializer = global::Newtonsoft.Json.JsonSerializer.Create();");
            source.AppendLine("            serializer.DateParseHandling = global::Newtonsoft.Json.DateParseHandling.None;");
            source.AppendLine("            return serializer;");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        private static bool IsConversionFailure(global::System.Exception exception)");
            source.AppendLine("        {");
            source.AppendLine("            return exception is global::Newtonsoft.Json.JsonException ||");
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
                    .AppendLine("(_token!, out _);");
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
                    source.Append("                case ").Append(index).Append(": return AllDeclared(_token!, new string[] { ")
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
            source.AppendLine("        public override object? ReadJson(global::Newtonsoft.Json.JsonReader reader, global::System.Type objectType, object? existingValue, global::Newtonsoft.Json.JsonSerializer serializer)");
            source.AppendLine("        {");
            source.AppendLine("            if (reader.TokenType == global::Newtonsoft.Json.JsonToken.Null)");
            source.AppendLine("            {");
            source.AppendLine("                return null;");
            source.AppendLine("            }");
            source.Append("            return ").Append(union.Name)
                .AppendLine(".FromJToken(global::Newtonsoft.Json.Linq.JToken.ReadFrom(reader));");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        public override void WriteJson(global::Newtonsoft.Json.JsonWriter writer, object? value, global::Newtonsoft.Json.JsonSerializer serializer)");
            source.AppendLine("        {");
            source.AppendLine("            if (value == null)");
            source.AppendLine("            {");
            source.AppendLine("                writer.WriteNull();");
            source.AppendLine("                return;");
            source.AppendLine("            }");
            source.Append("            var union = (").Append(union.Name).AppendLine(")value;");
            source.AppendLine("            if (union.TypedValue != null)");
            source.AppendLine("            {");
            source.AppendLine("                serializer.Serialize(writer, union.TypedValue);");
            source.AppendLine("                return;");
            source.AppendLine("            }");
            source.AppendLine("            union.RawToken!.WriteTo(writer);");
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
            source.Append("    [global::Newtonsoft.Json.JsonConverter(typeof(").Append(converterName).AppendLine("))]");
            source.Append("    public sealed class ").Append(union.Name).AppendLine();
        }

        internal static void AppendTypedAccessors(
            StringBuilder source,
            GeneratedUnionMemberModel member,
            int index,
            string typeName,
            bool tokenReader = false,
            bool oneOfSelection = false,
            string tokenCondition = "_token != null",
            string tokenValue = "_token",
            string? readerNote = null)
        {
            source.AppendLine();
            if (readerNote is not null)
            {
                source.Append("        /// <summary>").Append(readerNote).AppendLine("</summary>");
            }

            source.Append("        public bool TryGet").Append(member.AccessorName).Append("(out ").Append(typeName)
                .AppendLine(" value)");
            source.AppendLine("        {");
            source.Append("            if (_index == ").Append(index).AppendLine(")");
            source.AppendLine("            {");
            source.Append("                value = (").Append(typeName).AppendLine(")_value!;");
            source.AppendLine("                return true;");
            source.AppendLine("            }");
            if (tokenReader)
            {
                source.Append("            if (").Append(tokenCondition).Append(" && ").Append(oneOfSelection ? "IsSelected(" + index + ") && " : string.Empty)
                    .Append("TryRead").Append(index).Append("(").Append(tokenValue).AppendLine(", out value))");
                source.AppendLine("            {");
                source.AppendLine("                return true;");
                source.AppendLine("            }");
            }

            source.AppendLine("            value = default!;");
            source.AppendLine("            return false;");
            source.AppendLine("        }");
            source.AppendLine();
            if (readerNote is not null)
            {
                source.Append("        /// <summary>").Append(readerNote).AppendLine("</summary>");
            }

            source.Append("        public ").Append(typeName).Append(" As").Append(member.AccessorName).AppendLine("()");
            source.AppendLine("        {");
            source.Append("            if (TryGet").Append(member.AccessorName).AppendLine("(out var value))");
            source.AppendLine("            {");
            source.AppendLine("                return value;");
            source.AppendLine("            }");
            source.Append("            throw new global::System.InvalidOperationException(")
                .Append(GeneratedSourceEmitter.StringLiteral("The value is not a " + member.AccessorName + "."))
                .AppendLine(");");
            source.AppendLine("        }");
        }

        // A closed DTO candidate does not read a value with undeclared properties (an open one keeps them).
        internal static string GetClosedCheck(GeneratedTypeModel type, OpenApiGenerationModel model)
        {
            GeneratedDtoModel? dto = type.Kind == GeneratedTypeKind.Named
                ? model.Dtos.FirstOrDefault(value => string.Equals(value.Name, type.Name, StringComparison.Ordinal))
                : null;
            return dto is null || dto.IsOpen
                ? string.Empty
                : " && AllDeclared(token, new string[] { " +
                  string.Join(", ", dto.Properties.Select(static property => GeneratedSourceEmitter.StringLiteral(property.WireName))) +
                  " })";
        }

        private static string GetTokenPrecheck(GeneratedTypeModel type, OpenApiGenerationModel model)
        {
            const string TokenType = "token.Type == global::Newtonsoft.Json.Linq.JTokenType.";
            switch (type.Kind)
            {
                case GeneratedTypeKind.String:
                case GeneratedTypeKind.DateTime:
                case GeneratedTypeKind.DateTimeOffset:
                case GeneratedTypeKind.Guid:
                case GeneratedTypeKind.Binary:
                    return TokenType + "String";
                case GeneratedTypeKind.NamedEnum:
                    GeneratedEnumModel? enumModel = model.Enums.FirstOrDefault(
                        value => string.Equals(value.Name, type.Name, StringComparison.Ordinal));
                    return TokenType + (enumModel is not null && enumModel.IsInteger ? "Integer" : "String");
                case GeneratedTypeKind.Int32:
                case GeneratedTypeKind.Int64:
                    return TokenType + "Integer";
                case GeneratedTypeKind.Single:
                case GeneratedTypeKind.Double:
                case GeneratedTypeKind.Decimal:
                    return TokenType + "Integer || " + TokenType + "Float";
                case GeneratedTypeKind.Boolean:
                    return TokenType + "Boolean";
                case GeneratedTypeKind.Named:
                case GeneratedTypeKind.Map:
                case GeneratedTypeKind.FreeObject:
                    return TokenType + "Object";
                case GeneratedTypeKind.Array:
                    return TokenType + "Array";
                default:
                    return "true";
            }
        }

        /// <summary>Avoids a member that has the name of its containing type (CS0542).</summary>
        internal static string Unique(string name, string typeName)
        {
            return string.Equals(name, typeName.TrimStart('@'), StringComparison.Ordinal) ? name + "Member" : name;
        }
    }
}
