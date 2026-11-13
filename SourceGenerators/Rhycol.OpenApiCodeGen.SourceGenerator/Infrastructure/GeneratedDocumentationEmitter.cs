using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// XML documentation and [Obsolete] output shared by the DTO and enum emitters.
    /// description / title / default / readOnly / writeOnly only affect documentation; deprecated
    /// additionally produces [Obsolete] (the generated files disable CS0612 / CS0618 so the
    /// generated code that refers to a deprecated type or member stays warning free).
    /// </summary>
    internal static class GeneratedDocumentationEmitter
    {
        internal const string PragmaLine = "#pragma warning disable CS0612, CS0618";

        /// <summary>True when any generated declaration is marked [Obsolete].</summary>
        internal static bool AnyDeprecated(OpenApiGenerationModel model)
        {
            return model.Dtos.Any(static dto =>
                       dto.Annotations.Deprecated ||
                       dto.Properties.Any(static property => property.Annotations.Deprecated)) ||
                   model.Enums.Any(static value => value.Annotations.Deprecated) ||
                   model.Unions.Any(static value => value.Annotations.Deprecated);
        }

        internal static void AppendObsolete(StringBuilder source, string indent, bool deprecated)
        {
            if (deprecated)
            {
                source.Append(indent).AppendLine("[global::System.Obsolete]");
            }
        }

        /// <summary>
        /// Writes a &lt;summary&gt; from the description (one line per source line) plus
        /// default / direction notes. Nothing is written when there is nothing to say.
        /// </summary>
        internal static void AppendSummary(
            StringBuilder source,
            string indent,
            OpenApiSemanticAnnotations annotations,
            bool includeDefaultAndDirection)
        {
            var lines = new List<string>();
            foreach (string line in annotations.Description
                         .Replace("\r\n", "\n")
                         .Replace('\r', '\n')
                         .Split('\n'))
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    lines.Add(Escape(line.Trim()));
                }
            }

            if (includeDefaultAndDirection)
            {
                if (annotations.Default.HasValue &&
                    annotations.Default.Value.Kind != OpenApiSemanticLiteralKind.Null)
                {
                    lines.Add("Default: <c>" + Escape(FormatLiteral(annotations.Default.Value)) + "</c>.");
                }

                if (annotations.ReadOnly)
                {
                    lines.Add("Read-only: it is not sent in requests.");
                }

                if (annotations.WriteOnly)
                {
                    lines.Add("Write-only: it may be absent from responses.");
                }
            }

            AppendLines(source, indent, lines);
        }

        internal static void AppendSummary(StringBuilder source, string indent, string description)
        {
            var lines = new List<string>();
            foreach (string line in (description ?? string.Empty)
                         .Replace("\r\n", "\n")
                         .Replace('\r', '\n')
                         .Split('\n'))
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    lines.Add(Escape(line.Trim()));
                }
            }

            AppendLines(source, indent, lines);
        }

        private static void AppendLines(StringBuilder source, string indent, List<string> lines)
        {
            if (lines.Count == 0)
            {
                return;
            }

            source.Append(indent).AppendLine("/// <summary>");
            foreach (string line in lines)
            {
                source.Append(indent).Append("/// ").AppendLine(line);
            }

            source.Append(indent).AppendLine("/// </summary>");
        }

        private static string FormatLiteral(OpenApiSemanticLiteral literal)
        {
            return literal.Kind == OpenApiSemanticLiteralKind.String
                ? "\"" + literal.RawText + "\""
                : literal.RawText;
        }

        internal static string Escape(string value)
        {
            var normalized = new StringBuilder(value.Length);
            foreach (char character in value)
            {
                normalized.Append(char.IsControl(character) ||
                                  character == (char)0x2028 ||
                                  character == (char)0x2029
                    ? ' '
                    : character);
            }

            return normalized.ToString().Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }
    }
}
