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
        /// P5 surface: the credential properties of every security scheme kind (apiKey, bearer with
        /// its async provider, basic, other http schemes, oauth2, openIdConnect; mutualTLS adds none).
        /// </summary>
        [Fact]
        public void SecuritySpecKeepsPublicSurface()
        {
            AssertBaseline(
                "security",
                "BaselineSecurityApi",
                File.ReadAllText(SpecPath("security.json")));
        }

        /// <summary>
        /// P8 surface (OpenAPI 3.2): the QUERY method, additionalOperations (explicit and synthesized
        /// names), an in: querystring form parameter, a media type component, a discriminator with
        /// defaultMapping and the device authorization flow.
        /// </summary>
        [Fact]
        public void OpenApi32SpecKeepsPublicSurface()
        {
            AssertBaseline(
                "openapi32",
                "BaselineOpenApi32Api",
                File.ReadAllText(SpecPath("openapi32.json")));
        }

        /// <summary>
        /// P9 surface (Swagger 2.0): body, query, path, header and formData parameters with a file, the
        /// collectionFormat variants, a discriminated union, all security definitions, produces / consumes,
        /// response headers and host / basePath / schemes servers.
        /// </summary>
        [Fact]
        public void Swagger2SpecKeepsPublicSurface()
        {
            AssertBaseline(
                "swagger2",
                "BaselineSwagger2Api",
                File.ReadAllText(SpecPath("swagger2.json")));
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

        /// <summary>
        /// The System.Text.Json clients (HttpClient + SystemTextJson) have their own baselines
        /// (ApiSurfaceBaselines/*.stj.txt), composition / openapi32 / swagger2 included.
        /// </summary>
        [Theory]
        [InlineData("representative", "BaselineRepresentativeApi", "representative.json")]
        [InlineData("coverage", "BaselineCoverageApi", "coverage.json")]
        [InlineData("operations", "BaselineOperationsApi", "operations.json")]
        [InlineData("bodies", "BaselineBodiesApi", "bodies.json")]
        [InlineData("security", "BaselineSecurityApi", "security.json")]
        [InlineData("serializer-core", "BaselineSerializerCoreApi", "serializer-core.json")]
        [InlineData("composition", "BaselineCompositionApi", "composition.json")]
        [InlineData("openapi32", "BaselineOpenApi32Api", "openapi32.json")]
        [InlineData("swagger2", "BaselineSwagger2Api", "swagger2.json")]
        public void SystemTextJsonSpecsKeepPublicSurface(string name, string apiName, string specFile)
        {
            AssertBaseline(name, apiName, File.ReadAllText(SpecPath(specFile)), systemTextJson: true);
        }

        /// <summary>
        /// The UnityWebRequest clients (ApiSurfaceBaselines/*.uwr.txt with Newtonsoft.Json and *.uwr.stj.txt with
        /// System.Text.Json): the constructors take an absolute base address instead of an HttpClient, and the client
        /// has the RequestTimeout and ConfigureUnityWebRequest settings. Everything else is the HttpClient surface.
        /// </summary>
        [Theory]
        [InlineData("representative", "BaselineRepresentativeApi", "representative.json", false)]
        [InlineData("coverage", "BaselineCoverageApi", "coverage.json", false)]
        [InlineData("operations", "BaselineOperationsApi", "operations.json", false)]
        [InlineData("bodies", "BaselineBodiesApi", "bodies.json", false)]
        [InlineData("security", "BaselineSecurityApi", "security.json", false)]
        [InlineData("composition", "BaselineCompositionApi", "composition.json", false)]
        [InlineData("openapi32", "BaselineOpenApi32Api", "openapi32.json", false)]
        [InlineData("swagger2", "BaselineSwagger2Api", "swagger2.json", false)]
        [InlineData("representative", "BaselineRepresentativeApi", "representative.json", true)]
        [InlineData("coverage", "BaselineCoverageApi", "coverage.json", true)]
        [InlineData("operations", "BaselineOperationsApi", "operations.json", true)]
        [InlineData("bodies", "BaselineBodiesApi", "bodies.json", true)]
        [InlineData("security", "BaselineSecurityApi", "security.json", true)]
        [InlineData("serializer-core", "BaselineSerializerCoreApi", "serializer-core.json", true)]
        [InlineData("composition", "BaselineCompositionApi", "composition.json", true)]
        [InlineData("openapi32", "BaselineOpenApi32Api", "openapi32.json", true)]
        [InlineData("swagger2", "BaselineSwagger2Api", "swagger2.json", true)]
        public void UnityWebRequestSpecsKeepPublicSurface(
            string name,
            string apiName,
            string specFile,
            bool systemTextJson)
        {
            AssertBaseline(
                name,
                apiName,
                File.ReadAllText(SpecPath(specFile)),
                systemTextJson,
                unityWebRequest: true);
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

        /// <summary>
        /// Compares the public surface with the baseline. A System.Text.Json client uses the baseline
        /// "<name>.stj.txt" (its attributes and the Any / FreeObject types differ from the Newtonsoft one).
        /// </summary>
        private static void AssertBaseline(
            string name,
            string apiName,
            string specJson,
            bool systemTextJson = false,
            bool unityWebRequest = false)
        {
            string ns = "Generated.Baseline." + apiName;
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                specJson,
                apiName: apiName,
                generatedNamespace: ns,
                options: systemTextJson && !unityWebRequest
                    ? new Phase4CompilationOptions { JsonSerializer = "SystemTextJson" }
                    : unityWebRequest
                        ? new Phase4CompilationOptions
                        {
                            UnityWebRequestStub = true,
                            HttpTransport = "UnityWebRequest",
                            JsonSerializer = systemTextJson ? "SystemTextJson" : null
                        }
                        : null);
            string baselineName = name + (unityWebRequest ? ".uwr" : string.Empty) + (systemTextJson ? ".stj" : string.Empty);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);

            IReadOnlyList<string> actual = PublicApiSurfaceDescriber.Describe(execution.OutputCompilation, ns);
            Assert.NotEmpty(actual);

            if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
            {
                string target = Path.Combine(GetProjectDirectory(), "ApiSurfaceBaselines", baselineName + ".txt");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, string.Join("\n", actual) + "\n", new UTF8Encoding(false));
                return;
            }

            string path = Path.Combine(AppContext.BaseDirectory, "ApiSurfaceBaselines", baselineName + ".txt");
            Assert.True(
                File.Exists(path),
                "Missing baseline '" + baselineName + "'. Run with " + UpdateVariable + "=1 to create it.");
            string[] baseline = File.ReadAllLines(path)
                .Select(static line => line.TrimEnd('\r'))
                .Where(static line => line.Length > 0)
                .ToArray();
            IReadOnlyList<string> missing = FindMissing(baseline, actual);
            Assert.True(
                missing.Count == 0,
                "Public API signatures from the baseline '" + baselineName + "' were removed or changed:" +
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
