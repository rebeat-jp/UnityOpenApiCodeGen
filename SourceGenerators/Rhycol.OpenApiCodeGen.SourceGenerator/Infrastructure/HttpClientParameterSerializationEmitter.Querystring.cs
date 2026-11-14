using System;
using System.Linq;
using System.Text;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// P8 (G33): the request side of an OpenAPI 3.2 <c>in: querystring</c> parameter. Its content defines the
    /// whole query string: an application/x-www-form-urlencoded content is serialized field by field with
    /// the form-urlencoded body rules (style / explode / allowReserved of the Encoding Objects); a JSON
    /// content becomes the percent-encoded JSON text.
    /// </summary>
    internal sealed partial class HttpClientParameterSerializationEmitter
    {
        internal void AppendQuerystringParameter(StringBuilder source, GeneratedOperationModel operation)
        {
            GeneratedParameterModel? parameter = operation.Parameters.FirstOrDefault(
                static value => value.LocationName == "querystring");
            if (parameter is null)
            {
                return;
            }

            const string Join =
                "relativePath += (relativePath.IndexOf('?') >= 0 ? \"&\" : \"?\") + ";
            AppendGuarded(source, parameter, 12, indent =>
            {
                if (parameter.FormFields is null)
                {
                    source.Append(' ', indent).Append("string ").Append(JsonVar).Append(" = ")
                        .Append(_jsonSerializerName).Append('(')
                        .Append(HttpClientSourceEmitter.GetValueExpression(parameter)).AppendLine(");");
                    source.Append(' ', indent).Append(Join).Append(EncodeName).Append('(').Append(JsonVar)
                        .AppendLine(", false);");
                    return;
                }

                if (parameter.Required)
                {
                    source.Append(' ', indent).Append("if (").Append(parameter.Name).AppendLine(" == null)");
                    source.Append(' ', indent).AppendLine("{");
                    source.Append(' ', indent + 4).Append("throw new global::System.ArgumentNullException(nameof(")
                        .Append(parameter.Name).AppendLine("));");
                    source.Append(' ', indent).AppendLine("}");
                }

                source.Append(' ', indent)
                    .AppendLine("var oacgItems = new global::System.Collections.Generic.List<string>();");
                foreach (GeneratedFormFieldModel field in parameter.FormFields)
                {
                    if (field.Role == GeneratedFormFieldRole.Text && field.StyleParameter is not null)
                    {
                        AppendFormField(source, field.StyleParameter, indent, "oacgItems");
                    }
                    else if (field.Role == GeneratedFormFieldRole.Json)
                    {
                        AppendFormJsonField(
                            source, field, parameter.Name + "." + field.MemberName, indent, "oacgItems");
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            "The querystring field '" + field.WireName + "' is a file and has no query string form.");
                    }
                }

                source.Append(' ', indent).AppendLine("if (oacgItems.Count > 0)");
                source.Append(' ', indent).AppendLine("{");
                source.Append(' ', indent + 4).Append(Join).AppendLine("string.Join(\"&\", oacgItems);");
                source.Append(' ', indent).AppendLine("}");
            });
        }
    }
}
