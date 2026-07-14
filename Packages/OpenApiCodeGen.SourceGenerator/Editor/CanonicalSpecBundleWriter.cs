using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Editor
{
    internal static class CanonicalSpecBundleWriter
    {
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal static byte[] Write(
            string specId,
            string rawSha256,
            string sourcePath,
            SpecNode root)
        {
            var builder = new StringBuilder();
            builder.Append("{\n");
            AppendIndent(builder, 1);
            builder.Append("\"formatVersion\": 1,\n");
            AppendIndent(builder, 1);
            builder.Append("\"specId\": ");
            AppendString(builder, specId);
            builder.Append(",\n");
            AppendIndent(builder, 1);
            builder.Append("\"rawSha256\": ");
            AppendString(builder, rawSha256);
            builder.Append(",\n");
            AppendIndent(builder, 1);
            builder.Append("\"rootDocumentId\": \"root\",\n");
            AppendIndent(builder, 1);
            builder.Append("\"documents\": [\n");
            AppendIndent(builder, 2);
            builder.Append("{\n");
            AppendIndent(builder, 3);
            builder.Append("\"documentId\": \"root\",\n");
            AppendIndent(builder, 3);
            builder.Append("\"sourcePath\": ");
            AppendString(builder, sourcePath);
            builder.Append(",\n");
            AppendIndent(builder, 3);
            builder.Append("\"root\": ");
            AppendNode(builder, root, 3);
            builder.Append('\n');
            AppendIndent(builder, 2);
            builder.Append("}\n");
            AppendIndent(builder, 1);
            builder.Append("]\n");
            builder.Append("}\n");
            return StrictUtf8.GetBytes(builder.ToString());
        }

        private static void AppendNode(StringBuilder builder, SpecNode node, int indent)
        {
            var objectNode = node as SpecObjectNode;
            if (objectNode != null)
            {
                AppendObject(builder, objectNode, indent);
                return;
            }

            var arrayNode = node as SpecArrayNode;
            if (arrayNode != null)
            {
                AppendArray(builder, arrayNode, indent);
                return;
            }

            var stringNode = node as SpecStringNode;
            if (stringNode != null)
            {
                AppendScalarStart(builder, "string", stringNode, indent);
                builder.Append(",\n");
                AppendIndent(builder, indent + 1);
                builder.Append("\"value\": ");
                AppendString(builder, stringNode.Value);
                AppendScalarEnd(builder, indent);
                return;
            }

            var numberNode = node as SpecNumberNode;
            if (numberNode != null)
            {
                AppendScalarStart(builder, "number", numberNode, indent);
                builder.Append(",\n");
                AppendIndent(builder, indent + 1);
                builder.Append("\"numberKind\": ");
                AppendString(builder, numberNode.IsInteger ? "integer" : "real");
                builder.Append(",\n");
                AppendIndent(builder, indent + 1);
                builder.Append("\"value\": ");
                AppendString(builder, numberNode.Value);
                AppendScalarEnd(builder, indent);
                return;
            }

            var booleanNode = node as SpecBooleanNode;
            if (booleanNode != null)
            {
                AppendScalarStart(builder, "boolean", booleanNode, indent);
                builder.Append(",\n");
                AppendIndent(builder, indent + 1);
                builder.Append(booleanNode.Value ? "\"value\": true" : "\"value\": false");
                AppendScalarEnd(builder, indent);
                return;
            }

            if (node is SpecNullNode)
            {
                AppendScalarStart(builder, "null", node, indent);
                AppendScalarEnd(builder, indent);
                return;
            }

            throw new ArgumentOutOfRangeException(nameof(node), "Unknown SpecNode implementation.");
        }

        private static void AppendObject(StringBuilder builder, SpecObjectNode node, int indent)
        {
            builder.Append("{\n");
            AppendCommonFields(builder, "object", node, indent);
            builder.Append(",\n");
            AppendIndent(builder, indent + 1);
            builder.Append("\"properties\": [");

            if (node.Properties.Count == 0)
            {
                builder.Append("]\n");
            }
            else
            {
                builder.Append('\n');
                for (int index = 0; index < node.Properties.Count; index++)
                {
                    AppendProperty(builder, node.Properties[index], indent + 2);
                    builder.Append(index + 1 == node.Properties.Count ? "\n" : ",\n");
                }

                AppendIndent(builder, indent + 1);
                builder.Append("]\n");
            }

            AppendIndent(builder, indent);
            builder.Append('}');
        }

        private static void AppendProperty(StringBuilder builder, SpecProperty property, int indent)
        {
            AppendIndent(builder, indent);
            builder.Append("{\n");
            AppendIndent(builder, indent + 1);
            builder.Append("\"name\": ");
            AppendString(builder, property.Name);
            builder.Append(",\n");
            AppendIndent(builder, indent + 1);
            builder.Append("\"line\": ");
            AppendInvariantInteger(builder, property.Line);
            builder.Append(",\n");
            AppendIndent(builder, indent + 1);
            builder.Append("\"column\": ");
            AppendInvariantInteger(builder, property.Column);
            builder.Append(",\n");
            AppendIndent(builder, indent + 1);
            builder.Append("\"value\": ");
            AppendNode(builder, property.Value, indent + 1);
            builder.Append('\n');
            AppendIndent(builder, indent);
            builder.Append('}');
        }

        private static void AppendArray(StringBuilder builder, SpecArrayNode node, int indent)
        {
            builder.Append("{\n");
            AppendCommonFields(builder, "array", node, indent);
            builder.Append(",\n");
            AppendIndent(builder, indent + 1);
            builder.Append("\"items\": [");

            if (node.Items.Count == 0)
            {
                builder.Append("]\n");
            }
            else
            {
                builder.Append('\n');
                for (int index = 0; index < node.Items.Count; index++)
                {
                    AppendIndent(builder, indent + 2);
                    AppendNode(builder, node.Items[index], indent + 2);
                    builder.Append(index + 1 == node.Items.Count ? "\n" : ",\n");
                }

                AppendIndent(builder, indent + 1);
                builder.Append("]\n");
            }

            AppendIndent(builder, indent);
            builder.Append('}');
        }

        private static void AppendScalarStart(StringBuilder builder, string kind, SpecNode node, int indent)
        {
            builder.Append("{\n");
            AppendCommonFields(builder, kind, node, indent);
        }

        private static void AppendScalarEnd(StringBuilder builder, int indent)
        {
            builder.Append('\n');
            AppendIndent(builder, indent);
            builder.Append('}');
        }

        private static void AppendCommonFields(StringBuilder builder, string kind, SpecNode node, int indent)
        {
            AppendIndent(builder, indent + 1);
            builder.Append("\"kind\": ");
            AppendString(builder, kind);
            builder.Append(",\n");
            AppendIndent(builder, indent + 1);
            builder.Append("\"line\": ");
            AppendInvariantInteger(builder, node.Line);
            builder.Append(",\n");
            AppendIndent(builder, indent + 1);
            builder.Append("\"column\": ");
            AppendInvariantInteger(builder, node.Column);
        }

        private static void AppendString(StringBuilder builder, string value)
        {
            builder.Append('"');
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                switch (character)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (character < 0x20 ||
                            (char.IsSurrogate(character) &&
                             !IsValidSurrogatePair(value, index)))
                        {
                            AppendUnicodeEscape(builder, character);
                        }
                        else
                        {
                            builder.Append(character);
                            if (char.IsHighSurrogate(character))
                            {
                                builder.Append(value[++index]);
                            }
                        }

                        break;
                }
            }

            builder.Append('"');
        }

        private static bool IsValidSurrogatePair(string value, int index)
        {
            return char.IsHighSurrogate(value[index]) &&
                   index + 1 < value.Length &&
                   char.IsLowSurrogate(value[index + 1]);
        }

        private static void AppendUnicodeEscape(StringBuilder builder, char character)
        {
            const string Hex = "0123456789abcdef";
            builder.Append("\\u");
            builder.Append(Hex[(character >> 12) & 0xF]);
            builder.Append(Hex[(character >> 8) & 0xF]);
            builder.Append(Hex[(character >> 4) & 0xF]);
            builder.Append(Hex[character & 0xF]);
        }

        private static void AppendIndent(StringBuilder builder, int indent)
        {
            builder.Append(' ', indent * 2);
        }

        private static void AppendInvariantInteger(StringBuilder builder, int value)
        {
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }
    }
}
