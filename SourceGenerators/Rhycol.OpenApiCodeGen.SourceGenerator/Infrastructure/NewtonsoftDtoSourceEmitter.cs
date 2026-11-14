using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    internal sealed class NewtonsoftDtoSourceEmitter
    {
        internal IReadOnlyList<GeneratedFile> Emit(OpenApiGenerationModel model)
        {
            if (model is null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            var files = new List<GeneratedFile>();
            bool suppressObsoleteWarnings = GeneratedDocumentationEmitter.AnyDeprecated(model);
            var usedTypeNames = new HashSet<string>(model.Enums.Select(static value => value.Name),
                StringComparer.Ordinal);
            usedTypeNames.UnionWith(model.Dtos.Select(static value => value.Name));
            usedTypeNames.UnionWith(model.Unions.Select(static value => value.Name));
            usedTypeNames.Add(model.ApiName);
            usedTypeNames.Add(model.ApiName + "Exception");
            bool usePreserve = NeedsPreserve(model) && !usedTypeNames.Contains(PreserveAttributeName);
            foreach (GeneratedEnumModel enumModel in model.Enums.OrderBy(
                         static value => value.Name,
                         StringComparer.Ordinal))
            {
                string converterName = enumModel.Name + "WireConverter";
                int suffix = 2;
                while (!usedTypeNames.Add(converterName))
                {
                    converterName = enumModel.Name + "WireConverter" + suffix++;
                }

                files.Add(EmitEnum(model.GeneratedNamespace, enumModel, converterName, suppressObsoleteWarnings));
            }

            foreach (GeneratedDtoModel dto in model.Dtos.OrderBy(
                         static value => value.Name,
                         StringComparer.Ordinal))
            {
                files.Add(EmitDto(model.GeneratedNamespace, dto, suppressObsoleteWarnings, usePreserve));
            }

            files.AddRange(new PolymorphicConverterEmitter().Emit(
                model,
                usedTypeNames,
                usePreserve ? PreserveAttributeName : null,
                suppressObsoleteWarnings));

            // Unity's managed stripping (IL2CPP) removes members that are only reached through
            // reflection. Json.NET finds ShouldSerialize<Name>() and the extension data member by
            // reflection, so they carry a PreserveAttribute (the linker recognizes it by name).
            // The attribute is internal; it is skipped if a type with that name already exists.
            if (usePreserve)
            {
                files.Add(EmitPreserveAttribute(model.GeneratedNamespace));
            }

            return files;
        }

        private static GeneratedFile EmitEnum(
            string generatedNamespace,
            GeneratedEnumModel model,
            string converterName,
            bool suppressObsoleteWarnings)
        {
            bool isInteger = model.IsInteger;
            string wireKind = isInteger ? "integer" : "string";
            var source = new StringBuilder();
            AppendHeader(source, generatedNamespace, suppressObsoleteWarnings);
            GeneratedDocumentationEmitter.AppendSummary(source, "    ", model.Annotations, includeDefaultAndDirection: true);
            GeneratedDocumentationEmitter.AppendObsolete(source, "    ", model.Annotations.Deprecated);
            source.Append("    [global::Newtonsoft.Json.JsonConverter(typeof(")
                .Append(converterName).AppendLine("))]");
            source.Append("    public enum ").Append(model.Name);
            if (isInteger && model.Members.Any(static member => !IsInt32(member.WireValue)))
            {
                source.Append(" : long");
            }

            source.AppendLine();
            source.AppendLine("    {");
            for (int index = 0; index < model.Members.Count; index++)
            {
                GeneratedEnumMemberModel member = model.Members[index];
                GeneratedDocumentationEmitter.AppendSummary(source, "        ", member.Description);
                if (!isInteger)
                {
                    source.Append("        [global::System.Runtime.Serialization.EnumMember(Value = ")
                        .Append(GeneratedSourceEmitter.StringLiteral(member.WireValue))
                        .AppendLine(")]");
                }

                source.Append("        ").Append(member.Name);
                if (isInteger)
                {
                    source.Append(" = ").Append(member.WireValue);
                }

                source.AppendLine(index + 1 == model.Members.Count ? string.Empty : ",");
            }

            source.AppendLine("    }");
            source.AppendLine();
            source.Append("    internal sealed class ").Append(converterName)
                .AppendLine(" : global::Newtonsoft.Json.JsonConverter");
            source.AppendLine("    {");
            source.AppendLine("        public override bool CanConvert(global::System.Type objectType)");
            source.AppendLine("        {");
            source.Append("            return objectType == typeof(").Append(model.Name)
                .Append(") || objectType == typeof(").Append(model.Name).AppendLine("?);");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        public override object? ReadJson(global::Newtonsoft.Json.JsonReader reader, global::System.Type objectType, object? existingValue, global::Newtonsoft.Json.JsonSerializer serializer)");
            source.AppendLine("        {");
            source.AppendLine("            if (reader.TokenType == global::Newtonsoft.Json.JsonToken.Null)");
            source.AppendLine("            {");
            source.Append("                if (objectType == typeof(").Append(model.Name).AppendLine("?)) return null;");
            source.Append("                throw new global::Newtonsoft.Json.JsonSerializationException(\"A non-null ")
                .Append(wireKind).AppendLine(" enum value is required.\");");
            source.AppendLine("            }");
            if (isInteger)
            {
                source.AppendLine("            if (reader.TokenType != global::Newtonsoft.Json.JsonToken.Integer)");
                source.AppendLine("            {");
                source.AppendLine("                throw new global::Newtonsoft.Json.JsonSerializationException(\"A declared integer enum wire value is required.\");");
                source.AppendLine("            }");
                source.AppendLine("            object? rawWireValue = reader.Value;");
                source.AppendLine("            if (!(rawWireValue is long || rawWireValue is int || rawWireValue is short || rawWireValue is sbyte || rawWireValue is byte || rawWireValue is ushort || rawWireValue is uint))");
                source.AppendLine("            {");
                source.AppendLine("                throw new global::Newtonsoft.Json.JsonSerializationException(\"The integer enum wire value is out of range.\");");
                source.AppendLine("            }");
                source.AppendLine("            long wireValue = global::System.Convert.ToInt64(rawWireValue, global::System.Globalization.CultureInfo.InvariantCulture);");
                foreach (GeneratedEnumMemberModel member in model.Members)
                {
                    source.Append("            if (wireValue == ").Append(member.WireValue).Append("L) return ")
                        .Append(model.Name).Append('.').Append(member.Name).AppendLine(";");
                }

                source.AppendLine("            throw new global::Newtonsoft.Json.JsonSerializationException(\"The integer enum wire value is not declared.\");");
            }
            else
            {
                source.AppendLine("            if (reader.TokenType != global::Newtonsoft.Json.JsonToken.String)");
                source.AppendLine("            {");
                source.AppendLine("                throw new global::Newtonsoft.Json.JsonSerializationException(\"A declared string enum wire value is required.\");");
                source.AppendLine("            }");
                source.AppendLine("            string wireValue = (string)reader.Value!;");
                foreach (GeneratedEnumMemberModel member in model.Members)
                {
                    source.Append("            if (global::System.String.Equals(wireValue, ")
                        .Append(GeneratedSourceEmitter.StringLiteral(member.WireValue))
                        .Append(", global::System.StringComparison.Ordinal)) return ")
                        .Append(model.Name).Append('.').Append(member.Name).AppendLine(";");
                }

                source.AppendLine("            throw new global::Newtonsoft.Json.JsonSerializationException(\"The string enum wire value is not declared.\");");
            }

            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        public override void WriteJson(global::Newtonsoft.Json.JsonWriter writer, object? value, global::Newtonsoft.Json.JsonSerializer serializer)");
            source.AppendLine("        {");
            source.AppendLine("            if (value is null)");
            source.AppendLine("            {");
            source.AppendLine("                writer.WriteNull();");
            source.AppendLine("                return;");
            source.AppendLine("            }");
            source.Append("            if (!(value is ").Append(model.Name).AppendLine(" enumValue))");
            source.AppendLine("            {");
            source.Append("                throw new global::Newtonsoft.Json.JsonSerializationException(\"A ")
                .Append(wireKind).AppendLine(" enum value is required.\");");
            source.AppendLine("            }");
            source.AppendLine("            switch (enumValue)");
            source.AppendLine("            {");
            foreach (GeneratedEnumMemberModel member in model.Members)
            {
                source.Append("                case ").Append(model.Name).Append('.')
                    .Append(member.Name).AppendLine(":");
                if (isInteger)
                {
                    source.Append("                    writer.WriteValue(").Append(member.WireValue).AppendLine("L);");
                }
                else
                {
                    source.Append("                    writer.WriteValue(")
                        .Append(GeneratedSourceEmitter.StringLiteral(member.WireValue)).AppendLine(");");
                }

                source.AppendLine("                    return;");
            }

            source.AppendLine("                default:");
            source.Append("                    throw new global::Newtonsoft.Json.JsonSerializationException(\"The ")
                .Append(wireKind).AppendLine(" enum value is not declared.\");");
            source.AppendLine("            }");
            source.AppendLine("        }");
            source.AppendLine("    }");
            source.AppendLine("}");
            return GeneratedSourceEmitter.Create(model.Name + ".g.cs", source.ToString());
        }

        private const string PreserveAttributeName = "PreserveAttribute";

        private static bool NeedsPreserve(OpenApiGenerationModel model)
        {
            return model.Unions.Count > 0 ||
                   model.Dtos.Any(static dto =>
                       dto.IsOpen || dto.Properties.Any(static property => property.Annotations.ReadOnly));
        }

        private static GeneratedFile EmitPreserveAttribute(string generatedNamespace)
        {
            var source = new StringBuilder();
            AppendHeader(source, generatedNamespace, false);
            source.AppendLine("    /// <summary>Marks members that are only used through reflection so Unity managed stripping keeps them.</summary>");
            source.AppendLine("    [global::System.AttributeUsage(global::System.AttributeTargets.All)]");
            source.AppendLine("    internal sealed class PreserveAttribute : global::System.Attribute");
            source.AppendLine("    {");
            source.AppendLine("    }");
            source.AppendLine("}");
            return GeneratedSourceEmitter.Create(PreserveAttributeName + ".g.cs", source.ToString());
        }

        private static bool IsInt32(string raw)
        {
            return int.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);
        }

        private static GeneratedFile EmitDto(
            string generatedNamespace,
            GeneratedDtoModel model,
            bool suppressObsoleteWarnings,
            bool usePreserve)
        {
            var source = new StringBuilder();
            AppendHeader(source, generatedNamespace, suppressObsoleteWarnings);
            GeneratedDocumentationEmitter.AppendSummary(
                source, "    ", model.Annotations, includeDefaultAndDirection: false, model.ConstraintSets);
            GeneratedDocumentationEmitter.AppendObsolete(source, "    ", model.Annotations.Deprecated);
            source.AppendLine("    [global::Newtonsoft.Json.JsonObject(global::Newtonsoft.Json.MemberSerialization.OptIn)]");
            source.Append("    public sealed class ").Append(model.Name).AppendLine();
            source.AppendLine("    {");
            var usedMemberNames = new HashSet<string>(model.Properties.Select(static value => value.Name),
                StringComparer.Ordinal);
            usedMemberNames.Add(model.Name);
            if (model.ExtensionDataName is not null)
            {
                usedMemberNames.Add(model.ExtensionDataName);
            }

            foreach (GeneratedDtoPropertyModel property in model.Properties.Where(static value => value.UseSpecified))
            {
                usedMemberNames.Add(property.Name + "Specified");
            }

            foreach (GeneratedDtoPropertyModel property in model.Properties.Where(static value => value.Annotations.ReadOnly))
            {
                usedMemberNames.Add("ShouldSerialize" + property.Name);
            }

            // A readOnly property is never sent, so its required-ness only applies to responses.
            GeneratedDtoPropertyModel[] requiredReferenceProperties = model.Properties
                .Where(static property => property.Required &&
                                          !property.Annotations.ReadOnly &&
                                          !property.Type.Nullable &&
                                          !property.Type.IsValueType)
                .ToArray();

            foreach (GeneratedDtoPropertyModel property in model.Properties)
            {
                string? backingField = null;
                if (property.UseSpecified)
                {
                    backingField = "_" + property.Name + "Value";
                    int suffix = 2;
                    while (!usedMemberNames.Add(backingField))
                    {
                        backingField = "_" + property.Name + "Value" + suffix++;
                    }

                    source.Append("        private ")
                        .Append(GeneratedSourceEmitter.TypeName(property.Type))
                        .Append(' ')
                        .Append(backingField)
                        .AppendLine(";");
                }

                string required = property.Required
                    ? property.Type.Nullable
                        ? "global::Newtonsoft.Json.Required.AllowNull"
                        : "global::Newtonsoft.Json.Required.Always"
                    : property.UseSpecified
                        ? "global::Newtonsoft.Json.Required.Default"
                        : "global::Newtonsoft.Json.Required.DisallowNull";
                if (property.Required && property.Annotations.WriteOnly)
                {
                    // A writeOnly property may be missing from a response.
                    required = property.Type.Nullable
                        ? "global::Newtonsoft.Json.Required.Default"
                        : "global::Newtonsoft.Json.Required.DisallowNull";
                }

                GeneratedDocumentationEmitter.AppendSummary(
                    source, "        ", property.Annotations, includeDefaultAndDirection: true, property.Type.ConstraintSets);
                GeneratedDocumentationEmitter.AppendObsolete(source, "        ", property.Annotations.Deprecated);
                source.Append("        [global::Newtonsoft.Json.JsonProperty(")
                    .Append(GeneratedSourceEmitter.StringLiteral(property.WireName))
                    .Append(", Required = ")
                    .Append(required);
                if (property.Required || property.UseSpecified)
                {
                    source.Append(", NullValueHandling = global::Newtonsoft.Json.NullValueHandling.Include");
                }
                else
                {
                    source.Append(", NullValueHandling = global::Newtonsoft.Json.NullValueHandling.Ignore");
                }

                if (property.Required || property.UseSpecified)
                {
                    source.Append(", DefaultValueHandling = global::Newtonsoft.Json.DefaultValueHandling.Include");
                }

                source.AppendLine(")]");
                if (property.UseSpecified)
                {
                    source.Append("        public ")
                        .Append(GeneratedSourceEmitter.TypeName(property.Type))
                        .Append(' ')
                        .Append(property.Name)
                        .Append(" { get => ")
                        .Append(backingField)
                        .Append("; set { ")
                        .Append(backingField)
                        .Append(" = value; ")
                        .Append(property.Name)
                        .AppendLine("Specified = true; } }");
                    source.Append("        public bool ")
                        .Append(property.Name)
                        .AppendLine("Specified { get; set; }");
                }
                else
                {
                    source.Append("        public ")
                        .Append(GeneratedSourceEmitter.TypeName(property.Type))
                        .Append(' ')
                        .Append(property.Name)
                        .Append(" { get; set; }");
                    string? constInitializer = GetConstInitializer(
                        property.InitialValue.HasValue ? property.Type.WithConst(property.InitialValue) : property.Type);
                    if (constInitializer is not null)
                    {
                        source.Append(" = ").Append(constInitializer).Append(';');
                    }
                    else if (property.Required && !property.Type.Nullable && !property.Type.IsValueType)
                    {
                        source.Append(" = null!;");
                    }

                    source.AppendLine();
                }

                if (property.Annotations.ReadOnly)
                {
                    // Json.NET calls ShouldSerialize<Name>() by convention before writing a member.
                    if (usePreserve)
                    {
                        source.AppendLine("        [global::" + generatedNamespace + ".Preserve]");
                    }

                    source.AppendLine("        [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
                    source.Append("        public bool ShouldSerialize").Append(property.Name)
                        .AppendLine("() => false;");
                }

                source.AppendLine();
            }

            if (model.ExtensionDataName is not null)
            {
                source.AppendLine("        /// <summary>Members that are not declared by the schema, kept as raw JSON.</summary>");
                source.AppendLine("        [global::Newtonsoft.Json.JsonExtensionData]");
                if (usePreserve)
                {
                    source.AppendLine("        [global::" + generatedNamespace + ".Preserve]");
                }
                source.Append("        public global::System.Collections.Generic.IDictionary<string, global::Newtonsoft.Json.Linq.JToken> ")
                    .Append(model.ExtensionDataName)
                    .AppendLine(" { get; set; } = new global::System.Collections.Generic.Dictionary<string, global::Newtonsoft.Json.Linq.JToken>();");
                source.AppendLine();
            }

            if (requiredReferenceProperties.Length > 0)
            {
                const string CallbackBaseName = "ValidateRequiredPropertiesOnSerializing";
                string callbackName = CallbackBaseName;
                int suffix = 2;
                while (!usedMemberNames.Add(callbackName))
                {
                    callbackName = CallbackBaseName + suffix++;
                }

                source.AppendLine("        [global::System.Runtime.Serialization.OnSerializing]");
                source.Append("        private void ").Append(callbackName)
                    .AppendLine("(global::System.Runtime.Serialization.StreamingContext context)");
                source.AppendLine("        {");
                foreach (GeneratedDtoPropertyModel property in requiredReferenceProperties)
                {
                    source.Append("            if (").Append(property.Name).AppendLine(" == null)");
                    source.AppendLine("            {");
                    source.Append("                throw new global::Newtonsoft.Json.JsonSerializationException(")
                        .Append(GeneratedSourceEmitter.StringLiteral(
                            "Required property '" + property.WireName + "' cannot be null during serialization."))
                        .AppendLine(");");
                    source.AppendLine("            }");
                }

                source.AppendLine("        }");
            }

            source.AppendLine("    }");
            source.AppendLine("}");
            return GeneratedSourceEmitter.Create(model.Name + ".g.cs", source.ToString());
        }

        /// <summary>
        /// Initial value for a property whose schema has a const: a request DTO then sends the
        /// constant without the caller setting it. Returns null for types without a literal form.
        /// </summary>
        private static string? GetConstInitializer(GeneratedTypeModel type)
        {
            if (!type.ConstValue.HasValue)
            {
                return null;
            }

            OpenApiSemanticLiteral literal = type.ConstValue.Value;
            switch (type.Kind)
            {
                case GeneratedTypeKind.String:
                    return literal.Kind == OpenApiSemanticLiteralKind.String
                        ? GeneratedSourceEmitter.StringLiteral(literal.RawText)
                        : null;
                case GeneratedTypeKind.Boolean:
                    return literal.Kind == OpenApiSemanticLiteralKind.Boolean ? literal.RawText : null;
                case GeneratedTypeKind.Int32:
                    return literal.Kind == OpenApiSemanticLiteralKind.Integer && IsInt32(literal.RawText)
                        ? literal.RawText
                        : null;
                case GeneratedTypeKind.Int64:
                    return literal.Kind == OpenApiSemanticLiteralKind.Integer ? literal.RawText + "L" : null;
                case GeneratedTypeKind.Double:
                    return IsNumericLiteral(literal) ? literal.RawText + "D" : null;
                case GeneratedTypeKind.Single:
                    return IsNumericLiteral(literal) ? literal.RawText + "F" : null;
                case GeneratedTypeKind.Decimal:
                    return IsNumericLiteral(literal) ? literal.RawText + "M" : null;
                default:
                    return null;
            }
        }

        private static bool IsNumericLiteral(OpenApiSemanticLiteral literal)
        {
            return (literal.Kind == OpenApiSemanticLiteralKind.Integer ||
                    literal.Kind == OpenApiSemanticLiteralKind.Number) &&
                   literal.RawText.All(static character =>
                       (character >= '0' && character <= '9') ||
                       character == '-' || character == '+' || character == '.' ||
                       character == 'e' || character == 'E') &&
                   double.TryParse(
                       literal.RawText,
                       NumberStyles.Float,
                       CultureInfo.InvariantCulture,
                       out double parsed) &&
                   !double.IsInfinity(parsed);
        }

        private static void AppendHeader(
            StringBuilder source,
            string generatedNamespace,
            bool suppressObsoleteWarnings)
        {
            source.AppendLine("#nullable enable");
            if (suppressObsoleteWarnings)
            {
                source.AppendLine(GeneratedDocumentationEmitter.PragmaLine);
            }

            source.Append("namespace ").Append(generatedNamespace).AppendLine();
            source.AppendLine("{");
        }
    }
}
