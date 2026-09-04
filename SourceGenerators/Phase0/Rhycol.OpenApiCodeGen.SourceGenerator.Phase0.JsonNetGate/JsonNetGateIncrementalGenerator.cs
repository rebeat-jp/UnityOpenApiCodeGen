using System;
using System.IO;
using System.Reflection;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

using Newtonsoft.Json.Linq;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Phase0.JsonNetGate
{
    [Generator(LanguageNames.CSharp)]
    public sealed class JsonNetGateIncrementalGenerator : IIncrementalGenerator
    {
        private const string AnalyzerName = "Rhycol.OpenApiCodeGen.SourceGenerator.Phase0.JsonNetGate";
        private const string AdditionalFileSuffix = "." + AnalyzerName + ".additionalfile";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            IncrementalValuesProvider<string> messages = context.AdditionalTextsProvider
                .Where(static text => Path.GetFileName(text.Path).EndsWith(AdditionalFileSuffix, StringComparison.Ordinal))
                .Select(static (text, cancellationToken) => ParseMessage(text, cancellationToken));

            context.RegisterSourceOutput(messages, static (output, message) =>
            {
                Assembly jsonAssembly = typeof(JObject).Assembly;
                string informationalVersion = jsonAssembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                    .InformationalVersion ?? string.Empty;
                string source = "namespace Rhycol.OpenApiCodeGen.SourceGenerator.Phase0 { "
                    + "internal static class Phase0JsonNetGate { "
                    + "internal const string Message = \"" + Escape(message) + "\"; "
                    + "internal const string AssemblyFullName = \"" + Escape(jsonAssembly.FullName ?? string.Empty) + "\"; "
                    + "internal const string AssemblyLocation = \"" + Escape(jsonAssembly.Location) + "\"; "
                    + "internal const string InformationalVersion = \"" + Escape(informationalVersion) + "\"; "
                    + "internal const string ModuleVersionId = \"" + jsonAssembly.ManifestModule.ModuleVersionId.ToString("D") + "\"; "
                    + "} }";
                output.AddSource("Phase0JsonNetGate.g.cs", SourceText.From(source, Encoding.UTF8));
            });
        }

        private static string ParseMessage(
            AdditionalText text,
            System.Threading.CancellationToken cancellationToken)
        {
            string json = text.GetText(cancellationToken)?.ToString() ?? "{}";
            return JObject.Parse(json).Value<string>("message") ?? string.Empty;
        }

        private static string Escape(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
