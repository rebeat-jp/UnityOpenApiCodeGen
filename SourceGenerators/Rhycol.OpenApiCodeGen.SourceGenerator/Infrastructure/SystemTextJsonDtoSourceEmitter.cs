using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// System.Text.Json 向けの DTO と enum を生成する（リフレクションベース。JsonSerializerContext は使わない）。
    /// 生成される型の公開 API は Newtonsoft 版と同じ形で、属性と converter だけが異なる。
    /// Specified と readOnly の ShouldSerialize はクライアント側の DefaultJsonTypeInfoResolver.Modifiers が持つ
    /// （HttpClientSourceEmitter.SystemTextJson.cs）。
    /// Emits the DTOs and enums for System.Text.Json (reflection based, no JsonSerializerContext).
    /// The public API matches the Newtonsoft output; only attributes and converters differ. The
    /// "Specified" and readOnly ShouldSerialize rules live in the client's
    /// DefaultJsonTypeInfoResolver.Modifiers.
    /// </summary>
    internal sealed class SystemTextJsonDtoSourceEmitter
    {
        private const string Serialization = "global::System.Text.Json.Serialization";
        private const string JsonException = "global::System.Text.Json.JsonException";

        private static readonly JsonSerializerProfile Json = JsonSerializerProfile.SystemTextJson;

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
            bool usePreserve = NewtonsoftDtoSourceEmitter.NeedsPreserve(model) &&
                               !usedTypeNames.Contains(NewtonsoftDtoSourceEmitter.PreserveAttributeName);
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

            // The extension data member is found by reflection; Unity's managed stripping (IL2CPP)
            // keeps members that carry a PreserveAttribute (recognized by name).
            if (usePreserve)
            {
                files.Add(NewtonsoftDtoSourceEmitter.EmitPreserveAttribute(model.GeneratedNamespace));
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
            NewtonsoftDtoSourceEmitter.AppendHeader(source, generatedNamespace, suppressObsoleteWarnings);
            GeneratedDocumentationEmitter.AppendSummary(source, "    ", model.Annotations, includeDefaultAndDirection: true);
            GeneratedDocumentationEmitter.AppendObsolete(source, "    ", model.Annotations.Deprecated);
            source.Append("    [").Append(Serialization).Append(".JsonConverter(typeof(")
                .Append(converterName).AppendLine("))]");
            source.Append("    public enum ").Append(model.Name);
            if (isInteger && model.Members.Any(static member => !NewtonsoftDtoSourceEmitter.IsInt32(member.WireValue)))
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
                .Append(" : ").Append(Serialization).Append(".JsonConverter<").Append(model.Name).AppendLine(">");
            source.AppendLine("    {");
            source.Append("        public override ").Append(model.Name)
                .AppendLine(" Read(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options)");
            source.AppendLine("        {");
            if (isInteger)
            {
                source.AppendLine("            if (reader.TokenType != global::System.Text.Json.JsonTokenType.Number)");
                source.AppendLine("            {");
                source.Append("                throw new ").Append(JsonException)
                    .AppendLine("(\"A declared integer enum wire value is required.\");");
                source.AppendLine("            }");
                source.AppendLine("            if (!reader.TryGetInt64(out long wireValue))");
                source.AppendLine("            {");
                source.Append("                throw new ").Append(JsonException)
                    .AppendLine("(\"The integer enum wire value is out of range.\");");
                source.AppendLine("            }");
                foreach (GeneratedEnumMemberModel member in model.Members)
                {
                    source.Append("            if (wireValue == ").Append(member.WireValue).Append("L) return ")
                        .Append(model.Name).Append('.').Append(member.Name).AppendLine(";");
                }

                source.Append("            throw new ").Append(JsonException)
                    .AppendLine("(\"The integer enum wire value is not declared.\");");
            }
            else
            {
                source.AppendLine("            if (reader.TokenType != global::System.Text.Json.JsonTokenType.String)");
                source.AppendLine("            {");
                source.Append("                throw new ").Append(JsonException)
                    .AppendLine("(\"A declared string enum wire value is required.\");");
                source.AppendLine("            }");
                source.AppendLine("            string wireValue = reader.GetString()!;");
                foreach (GeneratedEnumMemberModel member in model.Members)
                {
                    source.Append("            if (global::System.String.Equals(wireValue, ")
                        .Append(GeneratedSourceEmitter.StringLiteral(member.WireValue))
                        .Append(", global::System.StringComparison.Ordinal)) return ")
                        .Append(model.Name).Append('.').Append(member.Name).AppendLine(";");
                }

                source.Append("            throw new ").Append(JsonException)
                    .AppendLine("(\"The string enum wire value is not declared.\");");
            }

            source.AppendLine("        }");
            source.AppendLine();
            source.Append("        public override void Write(global::System.Text.Json.Utf8JsonWriter writer, ")
                .Append(model.Name)
                .AppendLine(" value, global::System.Text.Json.JsonSerializerOptions options)");
            source.AppendLine("        {");
            source.AppendLine("            switch (value)");
            source.AppendLine("            {");
            foreach (GeneratedEnumMemberModel member in model.Members)
            {
                source.Append("                case ").Append(model.Name).Append('.')
                    .Append(member.Name).AppendLine(":");
                if (isInteger)
                {
                    source.Append("                    writer.WriteNumberValue(").Append(member.WireValue).AppendLine("L);");
                }
                else
                {
                    source.Append("                    writer.WriteStringValue(")
                        .Append(GeneratedSourceEmitter.StringLiteral(member.WireValue)).AppendLine(");");
                }

                source.AppendLine("                    return;");
            }

            source.AppendLine("                default:");
            source.Append("                    throw new ").Append(JsonException).Append("(\"The ")
                .Append(wireKind).AppendLine(" enum value is not declared.\");");
            source.AppendLine("            }");
            source.AppendLine("        }");
            source.AppendLine("    }");
            source.AppendLine("}");
            return GeneratedSourceEmitter.Create(model.Name + ".g.cs", source.ToString());
        }

        private static GeneratedFile EmitDto(
            string generatedNamespace,
            GeneratedDtoModel model,
            bool suppressObsoleteWarnings,
            bool usePreserve)
        {
            var source = new StringBuilder();
            NewtonsoftDtoSourceEmitter.AppendHeader(source, generatedNamespace, suppressObsoleteWarnings);
            GeneratedDocumentationEmitter.AppendSummary(
                source, "    ", model.Annotations, includeDefaultAndDirection: false, model.ConstraintSets);
            GeneratedDocumentationEmitter.AppendObsolete(source, "    ", model.Annotations.Deprecated);

            // A readOnly property is never sent, so its required-ness only applies to responses.
            GeneratedDtoPropertyModel[] requiredReferenceProperties = model.Properties
                .Where(static property => property.Required &&
                                          !property.Annotations.ReadOnly &&
                                          !property.Type.Nullable &&
                                          !property.Type.IsValueType)
                .ToArray();
            source.Append("    public sealed class ").Append(model.Name);
            if (requiredReferenceProperties.Length > 0)
            {
                source.Append(" : ").Append(Serialization).Append(".IJsonOnSerializing");
            }

            source.AppendLine();
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

            foreach (GeneratedDtoPropertyModel property in model.Properties)
            {
                string typeName = Json.TypeName(property.Type);
                string? backingField = null;
                if (property.UseSpecified)
                {
                    backingField = "_" + property.Name + "Value";
                    int suffix = 2;
                    while (!usedMemberNames.Add(backingField))
                    {
                        backingField = "_" + property.Name + "Value" + suffix++;
                    }

                    source.Append("        private ").Append(typeName).Append(' ').Append(backingField).AppendLine(";");
                }

                GeneratedDocumentationEmitter.AppendSummary(
                    source, "        ", property.Annotations, includeDefaultAndDirection: true, property.Type.ConstraintSets);
                GeneratedDocumentationEmitter.AppendObsolete(source, "        ", property.Annotations.Deprecated);
                source.Append("        [").Append(Serialization).Append(".JsonPropertyName(")
                    .Append(GeneratedSourceEmitter.StringLiteral(property.WireName)).AppendLine(")]");
                if (property.Required && !property.Annotations.ReadOnly && !property.Annotations.WriteOnly)
                {
                    // Missing members of a body that is deserialized without the response contract
                    // (declared error bodies) are rejected, as Required.Always / AllowNull do for Json.NET.
                    source.Append("        [").Append(Serialization).AppendLine(".JsonRequired]");
                }

                if (!property.Required && !property.UseSpecified)
                {
                    source.Append("        [").Append(Serialization)
                        .Append(".JsonIgnore(Condition = ").Append(Serialization)
                        .AppendLine(".JsonIgnoreCondition.WhenWritingNull)]");
                }

                if (property.UseSpecified)
                {
                    source.Append("        public ").Append(typeName).Append(' ').Append(property.Name)
                        .Append(" { get => ").Append(backingField)
                        .Append("; set { ").Append(backingField).Append(" = value; ")
                        .Append(property.Name).AppendLine("Specified = true; } }");
                    source.Append("        [").Append(Serialization).AppendLine(".JsonIgnore]");
                    source.Append("        public bool ").Append(property.Name).AppendLine("Specified { get; set; }");
                }
                else
                {
                    source.Append("        public ").Append(typeName).Append(' ').Append(property.Name)
                        .Append(" { get; set; }");
                    string? constInitializer = NewtonsoftDtoSourceEmitter.GetConstInitializer(
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

                source.AppendLine();
            }

            if (model.ExtensionDataName is not null)
            {
                string valueType = Json.ExtensionDataValueTypeName;
                source.AppendLine("        /// <summary>Members that are not declared by the schema, kept as raw JSON.</summary>");
                source.Append("        [").Append(Serialization).AppendLine(".JsonExtensionData]");
                if (usePreserve)
                {
                    source.AppendLine("        [global::" + generatedNamespace + ".Preserve]");
                }

                source.Append("        public global::System.Collections.Generic.IDictionary<string, ").Append(valueType).Append("> ")
                    .Append(model.ExtensionDataName)
                    .Append(" { get; set; } = new global::System.Collections.Generic.Dictionary<string, ")
                    .Append(valueType).AppendLine(">();");
                source.AppendLine();
            }

            if (requiredReferenceProperties.Length > 0)
            {
                source.Append("        void ").Append(Serialization).AppendLine(".IJsonOnSerializing.OnSerializing()");
                source.AppendLine("        {");
                foreach (GeneratedDtoPropertyModel property in requiredReferenceProperties)
                {
                    source.Append("            if (").Append(property.Name).AppendLine(" == null)");
                    source.AppendLine("            {");
                    source.Append("                throw new ").Append(JsonException).Append('(')
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
    }
}
