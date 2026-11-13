using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    /// <summary>
    /// Guards the public surface of generated clients. Every line in a baseline file must still
    /// exist in the symbols generated from the same spec (removal or change of a signature,
    /// attribute, constant, nullable annotation or modifier fails). Lines present only in the
    /// generated output (additions) are allowed.
    ///
    /// Specs and baselines are copied to the output directory by the csproj. To regenerate a
    /// baseline after an intentional change, run the tests with UPDATE_PUBLIC_API_BASELINE=1; the
    /// files are then written back to the source tree (ApiSurfaceBaselines/*.txt) and the git diff
    /// must be reviewed.
    ///
    /// Known intentional changes (user-approved, so the baselines are expected to be updated in
    /// P1): format: byte becoming byte[] and deprecated becoming [Obsolete]. The 'coverage' spec
    /// includes both so those diffs show up there.
    /// </summary>
    public sealed class PublicApiSurfaceBaselineTests
    {
        private const string UpdateVariable = "UPDATE_PUBLIC_API_BASELINE";

        [Fact]
        public void RepresentativeSpecKeepsPublicSurface()
        {
            AssertBaseline(
                "representative",
                "BaselineRepresentativeApi",
                File.ReadAllText(SpecPath("representative.json")));
        }

        [Fact]
        public void CompositionSpecKeepsPublicSurface()
        {
            AssertBaseline(
                "composition",
                "BaselineCompositionApi",
                File.ReadAllText(SpecPath("composition.json")));
        }

        [Fact]
        public void CoverageSpecKeepsPublicSurface()
        {
            AssertBaseline(
                "coverage",
                "BaselineCoverageApi",
                File.ReadAllText(SpecPath("coverage.json")));
        }

        /// <summary>
        /// P3 surface: synthesized operation ids, server catalog (Servers / BuildServerUrl),
        /// operation-level servers (OperationServerUrls), cookie / array / content parameters.
        /// </summary>
        [Fact]
        public void OperationsSpecKeepsPublicSurface()
        {
            AssertBaseline(
                "operations",
                "BaselineOperationsApi",
                File.ReadAllText(SpecPath("operations.json")));
        }

        /// <summary>
        /// P4 surface: request body variants (AsMultipart, AsFormUrlEncoded, AsText, AsBinary), the file
        /// part type, the status-keyed result wrapper, WithHttpInfo and the typed API exception.
        /// </summary>
        [Fact]
        public void BodiesSpecKeepsPublicSurface()
        {
            AssertBaseline(
                "bodies",
                "BaselineBodiesApi",
                File.ReadAllText(SpecPath("bodies.json")));
        }

        /// <summary>
        /// NOT the Unity fixture document itself: the external YAML reference
        /// ("VerificationItem.yaml") is rewritten to an internal component reference and the
        /// component is added, because this test uses a single-document bundle.
        /// </summary>
        [Fact]
        public void UnityVerificationFixtureInlinedSpecKeepsPublicSurface()
        {
            JsonNode root = JsonNode.Parse(File.ReadAllText(SpecPath("unity-fixture-openapi.json")))!;
            root["paths"]!["/items"]!["get"]!["responses"]!["200"]!["content"]!["application/json"]!
                ["schema"]!["items"]!["$ref"] = "#/components/schemas/VerificationItem";
            root["components"] = JsonNode.Parse(@"{
  ""schemas"": {
    ""VerificationItem"": {
      ""type"": ""object"",
      ""additionalProperties"": false,
      ""required"": [""id""],
      ""properties"": { ""id"": { ""type"": ""integer"" }, ""label"": { ""type"": ""string"" } }
    }
  }
}");

            AssertBaseline("unity-fixture-inlined", "BaselineFixtureApi", root.ToJsonString());
        }

        [Fact]
        public void BaselineCheckAllowsAdditionsButNotRemovalsOrChanges()
        {
            string[] baseline = { "A", "B" };
            Assert.Empty(FindMissing(baseline, new[] { "A", "B", "C" }));
            Assert.Equal(new[] { "B" }, FindMissing(baseline, new[] { "A", "B2" }));
        }

        [Fact]
        public void DescriberRecordsAttributesConstantsNullabilityModifiersAndDefaults()
        {
            const string Source = @"
namespace Probe
{
    #nullable enable
    public enum Color { Red = 1, Green = 2 }

    [System.Obsolete(""old"", true)]
    public abstract class Base<T> where T : class, new()
    {
        public const int Answer = 42;
        [System.Runtime.Serialization.DataMember(Name = ""n"")]
        public string? Name { get; set; }
        public string Other { get; set; } = """";
        public abstract void Run(System.Threading.CancellationToken token = default, Color color = Color.Green, double d = 1.5);
        public virtual void Go() { }
    }

    public sealed class Derived : Base<string>
    {
        public override void Run(System.Threading.CancellationToken token = default, Color color = Color.Green, double d = 1.5) { }
        public sealed override void Go() { }
    }
}";
            Microsoft.CodeAnalysis.CSharp.CSharpCompilation compilation =
                Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                    "Probe",
                    new[] { Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(Source) },
                    Phase4GeneratorTestHarness.CreateCompilation().References,
                    new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                        Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));

            string text = string.Join("\n", PublicApiSurfaceDescriber.Describe(compilation, "Probe"));

            Assert.Contains("Probe.Base<T> field public const int Answer = 42", text);
            Assert.Contains("[System.ObsoleteAttribute(\"old\", true)] type public abstract class Probe.Base<T> where T : class, new()", text);
            Assert.Contains("[System.Runtime.Serialization.DataMemberAttribute(Name = \"n\")] Probe.Base<T> property public string? Name { get; set; }", text);
            Assert.Contains("property public string Other", text);
            Assert.Contains("method public abstract void Run(System.Threading.CancellationToken token = default(System.Threading.CancellationToken), Probe.Color color = Probe.Color.Green, double d = 1.5d)", text);
            Assert.Contains("method public virtual void Go()", text);
            Assert.Contains("method public override sealed void Go()", text);
            Assert.Contains("Probe.Color field public const Probe.Color Green = 2", text);
        }

        private static string SpecPath(string fileName)
        {
            return Path.Combine(AppContext.BaseDirectory, "ApiSurfaceSpecs", fileName);
        }

        private static string GetProjectDirectory()
        {
            return typeof(PublicApiSurfaceBaselineTests).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(static attribute => attribute.Key == "ProjectDirectory")
                .Value!;
        }

        private static void AssertBaseline(string name, string apiName, string specJson)
        {
            string ns = "Generated.Baseline." + apiName;
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                specJson,
                apiName: apiName,
                generatedNamespace: ns);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);

            IReadOnlyList<string> actual = PublicApiSurfaceDescriber.Describe(execution.OutputCompilation, ns);
            Assert.NotEmpty(actual);

            if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
            {
                string target = Path.Combine(GetProjectDirectory(), "ApiSurfaceBaselines", name + ".txt");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, string.Join("\n", actual) + "\n", new UTF8Encoding(false));
                return;
            }

            string path = Path.Combine(AppContext.BaseDirectory, "ApiSurfaceBaselines", name + ".txt");
            Assert.True(
                File.Exists(path),
                "Missing baseline '" + name + "'. Run with " + UpdateVariable + "=1 to create it.");
            string[] baseline = File.ReadAllLines(path)
                .Select(static line => line.TrimEnd('\r'))
                .Where(static line => line.Length > 0)
                .ToArray();
            IReadOnlyList<string> missing = FindMissing(baseline, actual);
            Assert.True(
                missing.Count == 0,
                "Public API signatures from the baseline '" + name + "' were removed or changed:" +
                Environment.NewLine + string.Join(Environment.NewLine, missing) +
                Environment.NewLine + "If this is intentional, rerun with " + UpdateVariable + "=1.");
        }

        private static IReadOnlyList<string> FindMissing(
            IEnumerable<string> baseline,
            IEnumerable<string> actual)
        {
            var actualSet = new HashSet<string>(actual, StringComparer.Ordinal);
            return baseline.Where(line => !actualSet.Contains(line)).ToArray();
        }
    }
}
