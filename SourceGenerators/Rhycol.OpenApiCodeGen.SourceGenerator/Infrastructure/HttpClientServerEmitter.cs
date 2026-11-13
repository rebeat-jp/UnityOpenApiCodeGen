using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// Server catalog of the generated client: document servers (Servers, BuildServerUrl),
    /// per-operation servers (GetOperationServers, BuildOperationServerUrl, OperationNames) and
    /// the OperationServerUrls override table.
    /// </summary>
    internal sealed partial class HttpClientSourceEmitter
    {
        /// <summary>The key of an operation in OperationServerUrls: its generated method name.</summary>
        private static string OperationKey(GeneratedOperationModel operation)
        {
            return operation.Name.TrimStart('@');
        }

        private static void AppendServers(StringBuilder source, OpenApiGenerationModel model)
        {
            IReadOnlyList<OpenApiSemanticServer> servers = model.Servers.Count > 0
                ? model.Servers
                : new[] { new OpenApiSemanticServer(model.BaseUrl, model.BaseUrl, Array.Empty<OpenApiSemanticServerVariable>()) };
            source.AppendLine();
            source.AppendLine("        /// <summary>The server URL templates of the OpenAPI document (servers[].url), in document order. The constructor without a base URL uses the first one with each variable replaced by its default.</summary>");
            AppendServerList(source, "public static global::System.Collections.Generic.IReadOnlyList<string> Servers { get; } = ", servers, 8);
            source.AppendLine();
            source.AppendLine("        /// <summary>Expands the server URL template at <paramref name=\"index\"/>. Variables not supplied use their default; a supplied value must be one of the declared enum values.</summary>");
            source.AppendLine("        public static string BuildServerUrl(int index, global::System.Collections.Generic.IReadOnlyDictionary<string, string>? variables = null)");
            source.AppendLine("        {");
            AppendIndexSwitch(source, servers, 12);
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        private static string ExpandServerUrl(string template, string[][] declared, global::System.Collections.Generic.IReadOnlyDictionary<string, string>? variables)");
            source.AppendLine("        {");
            source.AppendLine("            if (variables != null)");
            source.AppendLine("            {");
            source.AppendLine("                foreach (var supplied in variables)");
            source.AppendLine("                {");
            source.AppendLine("                    bool known = false;");
            source.AppendLine("                    foreach (string[] variable in declared)");
            source.AppendLine("                    {");
            source.AppendLine("                        if (string.Equals(variable[0], supplied.Key, global::System.StringComparison.Ordinal)) { known = true; break; }");
            source.AppendLine("                    }");
            source.AppendLine("                    if (!known)");
            source.AppendLine("                    {");
            source.AppendLine("                        throw new global::System.ArgumentException(\"The server variable '\" + supplied.Key + \"' is not declared.\", nameof(variables));");
            source.AppendLine("                    }");
            source.AppendLine("                }");
            source.AppendLine("            }");
            source.AppendLine("            var values = new global::System.Collections.Generic.Dictionary<string, string>(global::System.StringComparer.Ordinal);");
            source.AppendLine("            foreach (string[] variable in declared)");
            source.AppendLine("            {");
            source.AppendLine("                string value = variable[1];");
            source.AppendLine("                if (variables != null && variables.TryGetValue(variable[0], out string? suppliedValue))");
            source.AppendLine("                {");
            source.AppendLine("                    value = suppliedValue ?? throw new global::System.ArgumentException(\"The server variable '\" + variable[0] + \"' must not be null.\", nameof(variables));");
            source.AppendLine("                }");
            source.AppendLine("                if (variable.Length > 2)");
            source.AppendLine("                {");
            source.AppendLine("                    bool allowed = false;");
            source.AppendLine("                    for (int index = 2; index < variable.Length; index++)");
            source.AppendLine("                    {");
            source.AppendLine("                        if (string.Equals(variable[index], value, global::System.StringComparison.Ordinal)) { allowed = true; break; }");
            source.AppendLine("                    }");
            source.AppendLine("                    if (!allowed)");
            source.AppendLine("                    {");
            source.AppendLine("                        throw new global::System.ArgumentException(\"The value '\" + value + \"' is not allowed for the server variable '\" + variable[0] + \"'.\", nameof(variables));");
            source.AppendLine("                    }");
            source.AppendLine("                }");
            source.AppendLine("                values[variable[0]] = value;");
            source.AppendLine("            }");
            source.AppendLine("            // One pass: substituted values are not scanned again for placeholders.");
            source.AppendLine("            var url = new global::System.Text.StringBuilder(template.Length);");
            source.AppendLine("            int cursor = 0;");
            source.AppendLine("            while (cursor < template.Length)");
            source.AppendLine("            {");
            source.AppendLine("                int open = template.IndexOf((char)123, cursor);");
            source.AppendLine("                int close = open < 0 ? -1 : template.IndexOf((char)125, open + 1);");
            source.AppendLine("                if (open < 0 || close < 0)");
            source.AppendLine("                {");
            source.AppendLine("                    url.Append(template, cursor, template.Length - cursor);");
            source.AppendLine("                    break;");
            source.AppendLine("                }");
            source.AppendLine("                url.Append(template, cursor, open - cursor);");
            source.AppendLine("                string name = template.Substring(open + 1, close - open - 1);");
            source.AppendLine("                if (values.TryGetValue(name, out string? replacement)) { url.Append(replacement); }");
            source.AppendLine("                else { url.Append(template, open, close - open + 1); }");
            source.AppendLine("                cursor = close + 1;");
            source.AppendLine("            }");
            source.AppendLine("            return url.ToString();");
            source.AppendLine("        }");

            GeneratedOperationModel[] withServers = model.Operations
                .Where(static operation => operation.Servers.Count > 0)
                .ToArray();
            if (withServers.Length == 0)
            {
                return;
            }

            source.AppendLine();
            source.AppendLine("        /// <summary>Server URL overrides for operations whose servers are declared on the operation or its Path Item. The key is the generated method name (see <see cref=\"OperationNames\"/>; a number may be appended to the name when it collides with another member), the value an expanded server URL that replaces the one from the document. An unknown key has no effect. Set entries at configuration time, before the first request: the dictionary is not thread-safe. The base URL passed to the constructor does not affect these operations.</summary>");
            source.AppendLine("        public global::System.Collections.Generic.IDictionary<string, string> OperationServerUrls { get; } = new global::System.Collections.Generic.Dictionary<string, string>(global::System.StringComparer.Ordinal);");
            source.AppendLine();
            source.AppendLine("        private string ResolveOperationServerUrl(string operationName, string specifiedUrl)");
            source.AppendLine("        {");
            source.AppendLine("            return OperationServerUrls.TryGetValue(operationName, out string? overrideUrl) ? overrideUrl : specifiedUrl;");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        /// <summary>The generated method names of the operations that declare their own servers, usable as <see cref=\"OperationServerUrls\"/> keys and as the operation name of <c>GetOperationServers</c> and <c>BuildOperationServerUrl</c>.</summary>");
            source.AppendLine("        public static class OperationNames");
            source.AppendLine("        {");
            foreach (GeneratedOperationModel operation in withServers)
            {
                source.Append("            public const string ").Append(operation.Name).Append(" = ")
                    .Append(GeneratedSourceEmitter.StringLiteral(OperationKey(operation))).AppendLine(";");
            }

            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        /// <summary>The server URL templates (servers[].url) that apply to an operation with its own servers, in document order; the first one is used.</summary>");
            source.AppendLine("        public static global::System.Collections.Generic.IReadOnlyList<string> GetOperationServers(string operationName)");
            source.AppendLine("        {");
            source.AppendLine("            switch (operationName)");
            source.AppendLine("            {");
            foreach (GeneratedOperationModel operation in withServers)
            {
                source.Append("                case ").Append(GeneratedSourceEmitter.StringLiteral(OperationKey(operation))).AppendLine(":");
                AppendServerList(source, "return ", operation.Servers, 20);
            }

            source.AppendLine("                default:");
            source.AppendLine("                    throw new global::System.ArgumentException(\"The operation '\" + operationName + \"' does not declare its own servers.\", nameof(operationName));");
            source.AppendLine("            }");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        /// <summary>Expands the server URL template at <paramref name=\"index\"/> of an operation with its own servers, like <c>BuildServerUrl</c>.</summary>");
            source.AppendLine("        public static string BuildOperationServerUrl(string operationName, int index, global::System.Collections.Generic.IReadOnlyDictionary<string, string>? variables = null)");
            source.AppendLine("        {");
            source.AppendLine("            switch (operationName)");
            source.AppendLine("            {");
            foreach (GeneratedOperationModel operation in withServers)
            {
                source.Append("                case ").Append(GeneratedSourceEmitter.StringLiteral(OperationKey(operation))).AppendLine(":");
                source.AppendLine("                {");
                AppendIndexSwitch(source, operation.Servers, 20, "GetOperationServers(" + GeneratedSourceEmitter.StringLiteral(OperationKey(operation)) + ")");
                source.AppendLine("                }");
            }

            source.AppendLine("                default:");
            source.AppendLine("                    throw new global::System.ArgumentException(\"The operation '\" + operationName + \"' does not declare its own servers.\", nameof(operationName));");
            source.AppendLine("            }");
            source.AppendLine("        }");
        }

        private static void AppendServerList(
            StringBuilder source,
            string prefix,
            IReadOnlyList<OpenApiSemanticServer> servers,
            int indent)
        {
            string pad = new string(' ', indent);
            source.Append(pad).Append(prefix).AppendLine("global::System.Array.AsReadOnly(new string[]");
            source.Append(pad).AppendLine("{");
            foreach (OpenApiSemanticServer server in servers)
            {
                source.Append(pad).Append("    ").Append(GeneratedSourceEmitter.StringLiteral(server.UrlTemplate)).AppendLine(",");
            }

            source.Append(pad).AppendLine("});");
        }

        private static void AppendIndexSwitch(
            StringBuilder source,
            IReadOnlyList<OpenApiSemanticServer> servers,
            int indent,
            string templates = "Servers")
        {
            string pad = new string(' ', indent);
            source.Append(pad).AppendLine("switch (index)");
            source.Append(pad).AppendLine("{");
            for (int index = 0; index < servers.Count; index++)
            {
                source.Append(pad).Append("    case ").Append(index).AppendLine(":");
                source.Append(pad).Append("        return ExpandServerUrl(").Append(templates).Append('[').Append(index).Append("], ");
                if (servers[index].Variables.Count == 0)
                {
                    source.Append("global::System.Array.Empty<string[]>()");
                }
                else
                {
                    source.Append("new string[][] { ");
                    source.Append(string.Join(", ", servers[index].Variables.Select(static variable =>
                        "new string[] { " + string.Join(", ", new[] { variable.Name, variable.DefaultValue }
                            .Concat(variable.EnumValues)
                            .Select(GeneratedSourceEmitter.StringLiteral)) + " }")));
                    source.Append(" }");
                }

                source.AppendLine(", variables);");
            }

            source.Append(pad).AppendLine("    default:");
            source.Append(pad).AppendLine("        throw new global::System.ArgumentOutOfRangeException(nameof(index));");
            source.Append(pad).AppendLine("}");
        }
    }
}
