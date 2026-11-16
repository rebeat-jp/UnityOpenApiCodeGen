using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Newtonsoft.Json;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    /// <summary>
    /// Emits the assertions of a set of schemas with <see cref="AssertionEmitter"/>, compiles them with Roslyn
    /// (C# 9, nullable enabled, against Newtonsoft.Json or System.Text.Json) and runs the generated methods.
    /// </summary>
    internal sealed class AssertionProgram
    {
        private const string TypeName = "AssertionTests.Probe";
        private readonly Type _type;

        private AssertionProgram(Type type, GeneratedAssertionGraph graph, string source, IReadOnlyList<string> warnings)
        {
            _type = type;
            Graph = graph;
            Source = source;
            Warnings = warnings;
        }

        internal GeneratedAssertionGraph Graph { get; }

        internal string Source { get; }

        internal IReadOnlyList<string> Warnings { get; }

        /// <summary>Null when the instance is valid, otherwise "keyword@path" of the recorded failure.</summary>
        internal string? Evaluate(string root, string json, bool request)
        {
            return (string?)Method("Run_" + root).Invoke(null, new object[] { json, request });
        }

        /// <summary>Calls the entry method; null when it returns, otherwise "exception type|message|parameter name".</summary>
        internal string? Enter(string root, string json, string contract)
        {
            return (string?)Method("Enter_" + root).Invoke(null, new object[] { json, contract });
        }

        private MethodInfo Method(string name)
        {
            return _type.GetMethod(name, BindingFlags.Public | BindingFlags.Static)
                   ?? throw new InvalidOperationException("The probe has no method " + name + ".");
        }

        internal static AssertionProgram Build(
            IReadOnlyList<KeyValuePair<string, OpenApiSemanticAssertion>> roots,
            bool systemTextJson)
        {
            var builder = new GeneratedAssertionGraphBuilder();
            foreach (KeyValuePair<string, OpenApiSemanticAssertion> root in roots)
            {
                builder.AddRoot(root.Key, root.Value);
            }

            GeneratedAssertionGraph graph = builder.Build();
            var model = new OpenApiGenerationModel(
                "Api",
                "Generated.Assertions",
                "https://example.test",
                Array.Empty<GeneratedDtoModel>(),
                Array.Empty<GeneratedEnumModel>(),
                Array.Empty<GeneratedOperationModel>(),
                Array.Empty<GeneratedTypeNameCollision>());
            JsonSerializerProfile profile = systemTextJson ? JsonSerializerProfile.SystemTextJson : JsonSerializerProfile.Newtonsoft;
            var constraints = new ConstraintValidationEmitter(model, profile) { JsonOptionsName = "ProbeOptions" };
            var emitter = new AssertionEmitter(constraints, profile, graph);

            var source = new StringBuilder();
            source.AppendLine("#nullable enable");
            source.AppendLine("namespace AssertionTests");
            source.AppendLine("{");
            source.AppendLine("    public static class Probe");
            source.AppendLine("    {");
            if (systemTextJson)
            {
                source.AppendLine("        private static readonly global::System.Text.Json.JsonSerializerOptions ProbeOptions = new global::System.Text.Json.JsonSerializerOptions();");
            }

            emitter.AppendMembers(source);
            foreach (GeneratedAssertionRoot root in graph.Roots)
            {
                string parse = constraints.HelperPrefix + "AParse";
                string token = profile.AnyTypeName;
                source.AppendLine();
                source.AppendLine("        public static string? Run_" + root.Name + "(string json, bool request)");
                source.AppendLine("        {");
                source.AppendLine("            " + token + " token = " + parse + "(json);");
                source.AppendLine("            " + emitter.FailureTypeName + " failure = default;");
                source.AppendLine("            if (" + emitter.MatchMethodName(root.Name) + "(token, request, ref failure))");
                source.AppendLine("            {");
                source.AppendLine("                return null;");
                source.AppendLine("            }");
                source.AppendLine("            return failure.Keyword + \"@\" + failure.Path;");
                source.AppendLine("        }");
                source.AppendLine();
                source.AppendLine("        public static string? Enter_" + root.Name + "(string json, string contract)");
                source.AppendLine("        {");
                source.AppendLine("            " + token + " token = " + parse + "(json);");
                source.AppendLine("            try");
                source.AppendLine("            {");
                source.AppendLine("                " + emitter.AssertMethodName(root.Name) + "(token, \"$.body\", contract, \"argument\");");
                source.AppendLine("                return null;");
                source.AppendLine("            }");
                source.AppendLine("            catch (global::System.Exception exception)");
                source.AppendLine("            {");
                source.AppendLine("                return exception.GetType().FullName + \"|\" + exception.Message + \"|\" + (exception as global::System.ArgumentException)?.ParamName;");
                source.AppendLine("            }");
                source.AppendLine("        }");
            }

            constraints.AppendHelpers(source);
            source.AppendLine("    }");
            if (!systemTextJson)
            {
                // The class that the Newtonsoft helpers of the generated client name (never used by the assertions).
                source.AppendLine("    public sealed class DateOnlyJsonConverter : global::Newtonsoft.Json.JsonConverter");
                source.AppendLine("    {");
                source.AppendLine("        public override bool CanConvert(global::System.Type objectType) => false;");
                source.AppendLine("        public override object? ReadJson(global::Newtonsoft.Json.JsonReader reader, global::System.Type objectType, object? existingValue, global::Newtonsoft.Json.JsonSerializer serializer) => null;");
                source.AppendLine("        public override void WriteJson(global::Newtonsoft.Json.JsonWriter writer, object? value, global::Newtonsoft.Json.JsonSerializer serializer) { }");
                source.AppendLine("    }");
            }

            source.AppendLine("}");

            string text = source.ToString();
            string? dump = Environment.GetEnvironmentVariable("OACG_ASSERTION_DUMP");
            if (!string.IsNullOrEmpty(dump))
            {
                File.WriteAllText(Path.Combine(dump, systemTextJson ? "stj.cs" : "newtonsoft.cs"), text);
            }

            CSharpCompilation compilation = CSharpCompilation.Create(
                "AssertionProbe_" + Guid.NewGuid().ToString("N"),
                new[] { CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.CSharp9)) },
                References(),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable));
            using var stream = new MemoryStream();
            var result = compilation.Emit(stream);
            if (!result.Success)
            {
                throw new InvalidOperationException(
                    "The generated assertions do not compile:" + Environment.NewLine +
                    string.Join(Environment.NewLine, result.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).Select(static d => d.ToString())));
            }

            Assembly assembly = Assembly.Load(stream.ToArray());
            string[] warnings = result.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Warning && d.Id != "CS1701" && d.Id != "CS1702")
                .Select(static d => d.ToString()).ToArray();
            return new AssertionProgram(
                assembly.GetType(TypeName) ?? throw new InvalidOperationException("The probe type is missing."),
                graph,
                text,
                warnings);
        }

        private static IEnumerable<MetadataReference> References()
        {
            string trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
            var paths = new HashSet<string>(trusted.Split(Path.PathSeparator), StringComparer.OrdinalIgnoreCase)
            {
                typeof(JsonConvert).Assembly.Location
            };
            return paths.Where(static path => !string.IsNullOrWhiteSpace(path))
                .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
                .ToArray();
        }
    }
}
