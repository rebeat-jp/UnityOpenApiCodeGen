using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// Emits the public types that belong to request and response content (P4): the file part type
    /// (<c>&lt;Api&gt;FileParameter</c>), the status-keyed result wrapper of an operation whose success
    /// responses carry different contracts, and the WithHttpInfo result (status code, headers, data).
    /// </summary>
    internal sealed class HttpContentEmitter
    {
        private readonly JsonSerializerProfile _json;

        internal HttpContentEmitter()
            : this(JsonSerializerProfile.Newtonsoft)
        {
        }

        internal HttpContentEmitter(JsonSerializerProfile json)
        {
            _json = json ?? throw new ArgumentNullException(nameof(json));
        }

        internal IReadOnlyList<GeneratedFile> Emit(OpenApiGenerationModel model)
        {
            if (model is null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            var files = new List<GeneratedFile>();
            if (model.FileParameterTypeName is string fileParameterName)
            {
                files.Add(EmitFileParameter(model, fileParameterName));
            }

            foreach (GeneratedOperationModel operation in model.Operations)
            {
                GeneratedResponsePlanModel? plan = operation.ResponsePlan;
                if (plan is null)
                {
                    continue;
                }

                if (plan.Wrapper is not null)
                {
                    files.Add(EmitWrapper(model, plan.Wrapper));
                }

                if (plan.HttpInfo is not null)
                {
                    files.Add(EmitHttpInfo(model, plan));
                }
            }

            return files;
        }

        private static void AppendHeader(StringBuilder source, OpenApiGenerationModel model)
        {
            source.AppendLine("#nullable enable");
            if (GeneratedDocumentationEmitter.AnyDeprecated(model))
            {
                source.AppendLine(GeneratedDocumentationEmitter.PragmaLine);
            }

            source.Append("namespace ").Append(model.GeneratedNamespace).AppendLine();
            source.AppendLine("{");
        }

        private GeneratedFile EmitFileParameter(OpenApiGenerationModel model, string name)
        {
            var source = new StringBuilder();
            AppendHeader(source, model);
            source.AppendLine("    /// <summary>");
            source.AppendLine("    /// A file part of a multipart/form-data request body: the content (a byte array or a stream) with an");
            source.AppendLine("    /// optional file name and Content-Type.");
            source.AppendLine("    /// </summary>");
            source.AppendLine("    /// <remarks>");
            source.AppendLine("    /// Stream ownership: a stream is handed to the request. It is read while the request is sent and is");
            source.AppendLine("    /// disposed together with the request (StreamContent semantics), so pass a stream per call and do not use");
            source.AppendLine("    /// it afterwards. A method that throws before the request is sent does not dispose it.");
            source.AppendLine("    /// A stream cannot be re-read, so it is not reusable for a retry or for a 307/308 redirect that resends the body.");
            source.AppendLine("    /// </remarks>");
            source.Append("    public sealed class ").Append(name).AppendLine();
            source.AppendLine("    {");
            source.Append("        public ").Append(name)
                .AppendLine("(byte[] data, string? fileName = null, string? contentType = null)");
            source.AppendLine("        {");
            source.AppendLine("            Data = data ?? throw new global::System.ArgumentNullException(nameof(data));");
            source.AppendLine("            FileName = fileName;");
            source.AppendLine("            ContentType = contentType;");
            source.AppendLine("        }");
            source.AppendLine();
            source.Append("        public ").Append(name)
                .AppendLine("(global::System.IO.Stream stream, string? fileName = null, string? contentType = null)");
            source.AppendLine("        {");
            source.AppendLine("            DataStream = stream ?? throw new global::System.ArgumentNullException(nameof(stream));");
            source.AppendLine("            FileName = fileName;");
            source.AppendLine("            ContentType = contentType;");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        /// <summary>The content when the part was created from a byte array.</summary>");
            source.AppendLine("        public byte[]? Data { get; }");
            source.AppendLine();
            source.AppendLine("        /// <summary>The content when the part was created from a stream.</summary>");
            source.AppendLine("        public global::System.IO.Stream? DataStream { get; }");
            source.AppendLine();
            source.AppendLine("        /// <summary>The file name of the Content-Disposition header; none is sent when null.</summary>");
            source.AppendLine("        public string? FileName { get; }");
            source.AppendLine();
            source.AppendLine("        /// <summary>The Content-Type of the part; the declared one (application/octet-stream by default) when null.</summary>");
            source.AppendLine("        public string? ContentType { get; }");
            source.AppendLine();
            source.AppendLine("        internal global::System.Net.Http.HttpContent CreateContent(string defaultContentType)");
            source.AppendLine("        {");
            source.AppendLine("            global::System.Net.Http.HttpContent content = Data != null");
            source.AppendLine("                ? new global::System.Net.Http.ByteArrayContent(Data)");
            source.AppendLine("                : new global::System.Net.Http.StreamContent(DataStream!);");
            source.AppendLine("            content.Headers.ContentType = global::System.Net.Http.Headers.MediaTypeHeaderValue.Parse(ContentType ?? defaultContentType);");
            source.AppendLine("            return content;");
            source.AppendLine("        }");
            source.AppendLine("    }");
            source.AppendLine("}");
            return GeneratedSourceEmitter.Create(name + ".g.cs", source.ToString());
        }

        // A non-nullable reference Data (the wrapper or a non-nullable body type) needs "= default!" to satisfy
        // nullable analysis; value types and nullable types do not.
        private static bool DataNeedsInitializer(GeneratedResponsePlanModel plan)
        {
            return plan.Wrapper is not null ||
                   (plan.DataType is not null && !plan.DataType.Nullable && !plan.DataType.IsValueType);
        }

        private static string Escape(string value)
        {
            return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        private GeneratedFile EmitWrapper(OpenApiGenerationModel model, GeneratedResponseWrapperModel wrapper)
        {
            var source = new StringBuilder();
            AppendHeader(source, model);
            source.Append("    /// <summary>").Append(Escape(wrapper.Summary.Replace('\r', ' ').Replace('\n', ' '))).AppendLine("</summary>");
            source.Append("    public sealed class ").Append(wrapper.TypeName).AppendLine();
            source.AppendLine("    {");
            var used = new HashSet<string>(StringComparer.Ordinal) { wrapper.TypeName, "StatusCode" };
            source.Append("        internal ").Append(wrapper.TypeName)
                .AppendLine("(global::System.Net.HttpStatusCode statusCode, string statusKey, object? body)");
            source.AppendLine("        {");
            source.AppendLine("            StatusCode = statusCode;");
            foreach (GeneratedSuccessResponseModel member in wrapper.Members)
            {
                string key = GeneratedSourceEmitter.StringLiteral(member.StatusKey);
                source.Append("            Is").Append(member.AccessorName)
                    .Append(" = global::System.String.Equals(statusKey, ").Append(key)
                    .AppendLine(", global::System.StringComparison.Ordinal);");
                if (member.Type is not null)
                {
                    string typeName = _json.TypeName(member.Type.WithNullable(true));
                    source.Append("            ").Append(member.AccessorName).Append(" = Is").Append(member.AccessorName)
                        .Append(" ? (").Append(typeName).AppendLine(")body : default;");
                }
            }

            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        /// <summary>The HTTP status code of the response.</summary>");
            source.AppendLine("        public global::System.Net.HttpStatusCode StatusCode { get; }");
            foreach (GeneratedSuccessResponseModel member in wrapper.Members)
            {
                string label = member.StatusKey == "DEFAULT"
                    ? "the default response"
                    : member.StatusKey == "ANY" ? "any 2xx response" : "the " + member.StatusKey + " response";
                source.AppendLine();
                source.Append("        /// <summary>True when the response is ").Append(label).AppendLine(".</summary>");
                source.Append("        public bool Is").Append(member.AccessorName).AppendLine(" { get; }");
                if (member.Type is not null)
                {
                    source.AppendLine();
                    source.Append("        /// <summary>The body of ").Append(label).AppendLine("; null for another response.</summary>");
                    source.Append("        public ").Append(_json.TypeName(member.Type.WithNullable(true)))
                        .Append(' ').Append(member.AccessorName).AppendLine(" { get; }");
                }
            }

            source.AppendLine("    }");
            source.AppendLine("}");
            return GeneratedSourceEmitter.Create(wrapper.TypeName + ".g.cs", source.ToString());
        }

        private GeneratedFile EmitHttpInfo(OpenApiGenerationModel model, GeneratedResponsePlanModel plan)
        {
            GeneratedHttpInfoModel info = plan.HttpInfo!;
            var source = new StringBuilder();
            AppendHeader(source, model);
            source.Append("    /// <summary>").Append(Escape(info.Summary.Replace('\r', ' ').Replace('\n', ' '))).AppendLine("</summary>");
            source.Append("    public sealed class ").Append(info.TypeName).AppendLine();
            source.AppendLine("    {");
            source.AppendLine("        /// <summary>The HTTP status code of the response.</summary>");
            source.AppendLine("        public global::System.Net.HttpStatusCode StatusCode { get; internal set; }");
            source.AppendLine();
            source.AppendLine("        /// <summary>Every response header (and content header), by name, case-insensitive.</summary>");
            source.AppendLine("        public global::System.Collections.Generic.IReadOnlyDictionary<string, string[]> Headers { get; internal set; } = new global::System.Collections.Generic.Dictionary<string, string[]>();");
            if (plan.ReturnTypeName is string dataType)
            {
                source.AppendLine();
                source.AppendLine("        /// <summary>The data the method without HttpInfo returns.</summary>");
                source.Append("        public ").Append(dataType).Append(" Data { get; internal set; }")
                    .Append(DataNeedsInitializer(plan) ? " = default!;" : string.Empty)
                    .AppendLine();
            }

            foreach (GeneratedResponseHeaderModel header in info.Headers)
            {
                source.AppendLine();
                source.Append("        /// <summary>The response header '").Append(Escape(header.WireName)).Append("'");
                if (!string.IsNullOrWhiteSpace(header.Description))
                {
                    source.Append(": ").Append(Escape(string.Join(" ", header.Description
                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(static line => line.Trim()))));
                }

                source.AppendLine(". Null when the response does not carry it.</summary>");
                GeneratedDocumentationEmitter.AppendObsolete(source, "        ", header.Deprecated);
                source.Append("        public ").Append(_json.TypeName(header.Type))
                    .Append(' ').Append(header.PropertyName).Append(" { get; internal set; }")
                    .Append(!header.Type.Nullable && !header.Type.IsValueType ? " = default!;" : string.Empty)
                    .AppendLine();
            }

            source.AppendLine("    }");
            source.AppendLine("}");
            return GeneratedSourceEmitter.Create(info.TypeName + ".g.cs", source.ToString());
        }
    }
}
