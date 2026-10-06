using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    internal static class TestBundleFactory
    {
        internal const string SpecId = "0123456789abcdef0123456789abcdef";
        internal const string SourcePath = "Assets/Specs/openapi.json";

        internal sealed class V2DocumentSpec
        {
            internal V2DocumentSpec(
                string documentId,
                string sourcePath,
                string format,
                string rootJson,
                string? rawSha256 = null)
            {
                DocumentId = documentId;
                SourcePath = sourcePath;
                Format = format;
                RootJson = rootJson;
                RawSha256 = rawSha256 ?? ComputeSha256(rootJson);
            }

            internal string DocumentId { get; }

            internal string SourcePath { get; }

            internal string Format { get; }

            internal string RootJson { get; }

            internal string RawSha256 { get; }
        }

        internal sealed class V2ReferenceSpec
        {
            internal V2ReferenceSpec(
                string sourceDocumentId,
                string sourcePointer,
                string targetDocumentId,
                string targetPointer)
            {
                SourceDocumentId = sourceDocumentId;
                SourcePointer = sourcePointer;
                TargetDocumentId = targetDocumentId;
                TargetPointer = targetPointer;
            }

            internal string SourceDocumentId { get; }

            internal string SourcePointer { get; }

            internal string TargetDocumentId { get; }

            internal string TargetPointer { get; }
        }

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

        internal static string CreateV2(
            string rootJson,
            IReadOnlyList<V2DocumentSpec> externalDocuments,
            IReadOnlyList<V2ReferenceSpec> referenceEdges,
            string sourcePath = SourcePath,
            string specId = SpecId,
            string? topRawSha256 = null)
        {
            using JsonDocument rootDocument = JsonDocument.Parse(rootJson);
            var builder = new StringBuilder();
            builder.Append("{\n");
            builder.Append("  \"formatVersion\": 2,\n");
            builder.Append("  \"specId\": ");
            AppendString(builder, specId);
            builder.Append(",\n  \"rawSha256\": ");
            AppendString(builder, topRawSha256 ?? ComputeSha256(rootJson));
            builder.Append(",\n  \"rootDocumentId\": \"root\",\n");
            builder.Append("  \"documents\": [\n");
            AppendV2Document(
                builder,
                "root",
                sourcePath,
                "json",
                ComputeSha256(rootJson),
                rootDocument.RootElement,
                2);

            for (int index = 0; index < externalDocuments.Count; index++)
            {
                builder.Append(",\n");
                V2DocumentSpec document = externalDocuments[index];
                using JsonDocument externalRoot = JsonDocument.Parse(document.RootJson);
                AppendV2Document(
                    builder,
                    document.DocumentId,
                    document.SourcePath,
                    document.Format,
                    document.RawSha256,
                    externalRoot.RootElement,
                    2);
            }

            builder.Append("\n  ],\n  \"referenceEdges\": [");
            if (referenceEdges.Count == 0)
            {
                builder.Append("]\n");
            }
            else
            {
                builder.Append('\n');
                for (int index = 0; index < referenceEdges.Count; index++)
                {
                    V2ReferenceSpec edge = referenceEdges[index];
                    builder.Append("    {\n");
                    builder.Append("      \"sourceDocumentId\": ");
                    AppendString(builder, edge.SourceDocumentId);
                    builder.Append(",\n      \"sourcePointer\": ");
                    AppendString(builder, edge.SourcePointer);
                    builder.Append(",\n      \"targetDocumentId\": ");
                    AppendString(builder, edge.TargetDocumentId);
                    builder.Append(",\n      \"targetPointer\": ");
                    AppendString(builder, edge.TargetPointer);
                    builder.Append("\n    }");
                    builder.Append(index + 1 == referenceEdges.Count ? "\n" : ",\n");
                }

                builder.Append("  ]\n");
            }

            builder.Append("}\n");
            return builder.ToString();
        }

        private static void AppendV2Document(
            StringBuilder builder,
            string documentId,
            string sourcePath,
            string format,
            string rawSha256,
            JsonElement root,
            int indent)
        {
            builder.Append(new string(' ', indent * 2));
            builder.Append("{\n");
            builder.Append(new string(' ', (indent + 1) * 2));
            builder.Append("\"documentId\": ");
            AppendString(builder, documentId);
            builder.Append(",\n");
            builder.Append(new string(' ', (indent + 1) * 2));
            builder.Append("\"sourcePath\": ");
            AppendString(builder, sourcePath);
            builder.Append(",\n");
            builder.Append(new string(' ', (indent + 1) * 2));
            builder.Append("\"format\": ");
            AppendString(builder, format);
            builder.Append(",\n");
            builder.Append(new string(' ', (indent + 1) * 2));
            builder.Append("\"rawSha256\": ");
            AppendString(builder, rawSha256);
            builder.Append(",\n");
            builder.Append(new string(' ', (indent + 1) * 2));
            builder.Append("\"root\": ");
            AppendNode(builder, root);
            builder.Append('\n');
            builder.Append(new string(' ', indent * 2));
            builder.Append('}');
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

        private static string ComputeSha256(string value)
        {
            using SHA256 sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
            var builder = new StringBuilder(hash.Length * 2);
            foreach (byte valueByte in hash)
            {
                builder.Append(valueByte.ToString("x2"));
            }

            return builder.ToString();
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
