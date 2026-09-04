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
            foreach (GeneratedEnumModel enumModel in model.Enums.OrderBy(
                         static value => value.Name,
                         StringComparer.Ordinal))
            {
                files.Add(EmitEnum(model.GeneratedNamespace, enumModel));
            }

            foreach (GeneratedDtoModel dto in model.Dtos.OrderBy(
                         static value => value.Name,
                         StringComparer.Ordinal))
            {
                files.Add(EmitDto(model.GeneratedNamespace, dto));
            }

            return files;
        }

        private static GeneratedFile EmitEnum(string generatedNamespace, GeneratedEnumModel model)
        {
            var source = new StringBuilder();
            AppendHeader(source, generatedNamespace);
            source.AppendLine("    [global::Newtonsoft.Json.JsonConverter(typeof(global::Newtonsoft.Json.Converters.StringEnumConverter))]");
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
            foreach (GeneratedDtoPropertyModel property in model.Properties)
            {
                string required = property.Required
                    ? property.Type.Nullable
                        ? "global::Newtonsoft.Json.Required.AllowNull"
                        : "global::Newtonsoft.Json.Required.Always"
                    : "global::Newtonsoft.Json.Required.Default";
                source.Append("        [global::Newtonsoft.Json.JsonProperty(")
                    .Append(GeneratedSourceEmitter.StringLiteral(property.WireName))
                    .Append(", Required = ")
                    .Append(required);
                if (!property.Required)
                {
                    source.Append(", NullValueHandling = global::Newtonsoft.Json.NullValueHandling.Ignore");
                }

                source.AppendLine(")]");
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
