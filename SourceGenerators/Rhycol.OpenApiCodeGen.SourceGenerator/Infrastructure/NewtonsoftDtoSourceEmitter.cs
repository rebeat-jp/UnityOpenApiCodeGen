using System;
using System.Collections.Generic;
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
            var usedTypeNames = new HashSet<string>(model.Enums.Select(static value => value.Name),
                StringComparer.Ordinal);
            usedTypeNames.UnionWith(model.Dtos.Select(static value => value.Name));
            usedTypeNames.Add(model.ApiName);
            usedTypeNames.Add(model.ApiName + "Exception");
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

                files.Add(EmitEnum(model.GeneratedNamespace, enumModel, converterName));
            }

            foreach (GeneratedDtoModel dto in model.Dtos.OrderBy(
                         static value => value.Name,
                         StringComparer.Ordinal))
            {
                files.Add(EmitDto(model.GeneratedNamespace, dto));
            }

            return files;
        }

        private static GeneratedFile EmitEnum(
            string generatedNamespace,
            GeneratedEnumModel model,
            string converterName)
        {
            var source = new StringBuilder();
            AppendHeader(source, generatedNamespace);
            source.Append("    [global::Newtonsoft.Json.JsonConverter(typeof(")
                .Append(converterName).AppendLine("))]");
            source.Append("    public enum ").Append(model.Name).AppendLine();
            source.AppendLine("    {");
            for (int index = 0; index < model.Members.Count; index++)
            {
                GeneratedEnumMemberModel member = model.Members[index];
                source.Append("        [global::System.Runtime.Serialization.EnumMember(Value = ")
                    .Append(GeneratedSourceEmitter.StringLiteral(member.WireValue))
                    .AppendLine(")]");
                source.Append("        ").Append(member.Name);
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
            source.AppendLine("                throw new global::Newtonsoft.Json.JsonSerializationException(\"A non-null string enum value is required.\");");
            source.AppendLine("            }");
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
            source.AppendLine("                throw new global::Newtonsoft.Json.JsonSerializationException(\"A string enum value is required.\");");
            source.AppendLine("            }");
            source.AppendLine("            switch (enumValue)");
            source.AppendLine("            {");
            foreach (GeneratedEnumMemberModel member in model.Members)
            {
                source.Append("                case ").Append(model.Name).Append('.')
                    .Append(member.Name).AppendLine(":");
                source.Append("                    writer.WriteValue(")
                    .Append(GeneratedSourceEmitter.StringLiteral(member.WireValue)).AppendLine(");");
                source.AppendLine("                    return;");
            }

            source.AppendLine("                default:");
            source.AppendLine("                    throw new global::Newtonsoft.Json.JsonSerializationException(\"The string enum value is not declared.\");");
            source.AppendLine("            }");
            source.AppendLine("        }");
            source.AppendLine("    }");
            source.AppendLine("}");
            return GeneratedSourceEmitter.Create(model.Name + ".g.cs", source.ToString());
        }

        private static GeneratedFile EmitDto(string generatedNamespace, GeneratedDtoModel model)
        {
            var source = new StringBuilder();
            AppendHeader(source, generatedNamespace);
            source.AppendLine("    [global::Newtonsoft.Json.JsonObject(global::Newtonsoft.Json.MemberSerialization.OptIn)]");
            source.Append("    public sealed class ").Append(model.Name).AppendLine();
            source.AppendLine("    {");
            var usedMemberNames = new HashSet<string>(model.Properties.Select(static value => value.Name),
                StringComparer.Ordinal);
            usedMemberNames.Add(model.Name);
            foreach (GeneratedDtoPropertyModel property in model.Properties.Where(static value => value.UseSpecified))
            {
                usedMemberNames.Add(property.Name + "Specified");
            }

            GeneratedDtoPropertyModel[] requiredReferenceProperties = model.Properties
                .Where(static property => property.Required && !property.Type.Nullable && !property.Type.IsValueType)
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
                    if (property.Required && !property.Type.Nullable && !property.Type.IsValueType)
                    {
                        source.Append(" = null!;");
                    }

                    source.AppendLine();
                }
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

        private static void AppendHeader(StringBuilder source, string generatedNamespace)
        {
            source.AppendLine("#nullable enable");
            source.Append("namespace ").Append(generatedNamespace).AppendLine();
            source.AppendLine("{");
        }
    }
}
