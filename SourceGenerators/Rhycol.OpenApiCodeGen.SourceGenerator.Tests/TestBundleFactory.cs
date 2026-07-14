using System;
using System.Text;
using System.Text.Json;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    internal static class TestBundleFactory
    {
        internal const string SpecId = "0123456789abcdef0123456789abcdef";
        internal const string SourcePath = "Assets/Specs/openapi.json";

        internal static SpecNode ReadRoot(string rawJson)
        {
            return NormalizedSpecBundleReader.Read(Create(rawJson)).Root;
        }

        internal static string Create(
            string rawJson,
            string sourcePath = SourcePath,
            string specId = SpecId,
            int formatVersion = 1)
        {
            using JsonDocument document = JsonDocument.Parse(rawJson);
            var rootBuilder = new StringBuilder();
            AppendNode(rootBuilder, document.RootElement);
            return CreateWithEncodedRoot(rootBuilder.ToString(), sourcePath, specId, formatVersion);
        }

        internal static string CreateWithEncodedRoot(
            string encodedRoot,
            string sourcePath = SourcePath,
            string specId = SpecId,
            int formatVersion = 1)
        {
            var builder = new StringBuilder();
            builder.Append('{');
            builder.Append("\"formatVersion\":");
            builder.Append(formatVersion);
            builder.Append(",\"specId\":");
            AppendString(builder, specId);
            builder.Append(",\"rawSha256\":\"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\"");
            builder.Append(",\"rootDocumentId\":\"root\"");
            builder.Append(",\"documents\":[{\"documentId\":\"root\",\"sourcePath\":");
            AppendString(builder, sourcePath);
            builder.Append(",\"root\":");
            builder.Append(encodedRoot);
            builder.Append("}]}");
            return builder.ToString();
        }

        private static void AppendNode(StringBuilder builder, JsonElement element)
        {
            builder.Append('{');
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    AppendCommon(builder, "object");
                    builder.Append(",\"properties\":[");
                    bool firstProperty = true;
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        if (!firstProperty)
                        {
                            builder.Append(',');
                        }

                        firstProperty = false;
                        builder.Append("{\"name\":");
                        AppendString(builder, property.Name);
                        builder.Append(",\"line\":1,\"column\":1,\"value\":");
                        AppendNode(builder, property.Value);
                        builder.Append('}');
                    }

                    builder.Append(']');
                    break;
                case JsonValueKind.Array:
                    AppendCommon(builder, "array");
                    builder.Append(",\"items\":[");
                    bool firstItem = true;
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        if (!firstItem)
                        {
                            builder.Append(',');
                        }

                        firstItem = false;
                        AppendNode(builder, item);
                    }

                    builder.Append(']');
                    break;
                case JsonValueKind.String:
                    AppendCommon(builder, "string");
                    builder.Append(",\"value\":");
                    AppendString(builder, element.GetString() ?? string.Empty);
                    break;
                case JsonValueKind.Number:
                    string number = element.GetRawText();
                    AppendCommon(builder, "number");
                    builder.Append(",\"numberKind\":\"");
                    builder.Append(IsInteger(number) ? "integer" : "real");
                    builder.Append("\",\"value\":");
                    AppendString(builder, number);
                    break;
                case JsonValueKind.True:
                    AppendCommon(builder, "boolean");
                    builder.Append(",\"value\":true");
                    break;
                case JsonValueKind.False:
                    AppendCommon(builder, "boolean");
                    builder.Append(",\"value\":false");
                    break;
                case JsonValueKind.Null:
                    AppendCommon(builder, "null");
                    break;
                default:
                    throw new InvalidOperationException("Unsupported test JSON value.");
            }

            builder.Append('}');
        }

        private static void AppendCommon(StringBuilder builder, string kind)
        {
            builder.Append("\"kind\":\"");
            builder.Append(kind);
            builder.Append("\",\"line\":1,\"column\":1");
        }

        private static void AppendString(StringBuilder builder, string value)
        {
            builder.Append(JsonSerializer.Serialize(value));
        }

        private static bool IsInteger(string value)
        {
            return value.IndexOf('.') < 0 &&
                   value.IndexOf('e') < 0 &&
                   value.IndexOf('E') < 0;
        }
    }
}
