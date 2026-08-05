using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    public class SourceGeneratorTests
    {
        private const string SecondSpecId = "fedcba9876543210fedcba9876543210";
        private const string AttributeContract = @"
namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    public enum OpenApiDocumentFormat
    {
        Json = 0,
        Yaml = 1
    }

    [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class OpenApiClientDefinitionAttribute : System.Attribute
    {
        public OpenApiClientDefinitionAttribute(
            string specId,
            string apiName,
            string generatedNamespace,
            OpenApiDocumentFormat documentFormat)
        {
        }
    }
}
";

        [Fact]
        public void GeneratesClientFromMatchingDefinitionAndAdditionalFile()
        {
            const string OpenApiJson = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Sample"", ""version"": ""1.0.0"" },
  ""paths"": {
    ""/items"": {
      ""get"": {
        ""operationId"": ""getItems"",
        ""summary"": ""Get items"",
        ""responses"": {
          ""200"": {
            ""description"": ""OK"",
            ""content"": {
              ""application/json"": {
                ""schema"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } }
              }
            }
          }
        }
      }
    }
  },
  ""components"": { ""schemas"": {} }
}";
            CSharpCompilation compilation = CreateCompilation(
                CreateDefinition(TestBundleFactory.SpecId, "TestApi", "Generated.Clients", "TestApi"));
            var additionalTexts = ImmutableArray.Create<AdditionalText>(CreateBundleText(
                TestBundleFactory.SpecId,
                TestBundleFactory.Create(OpenApiJson)));

            GeneratorDriverRunResult result = Run(compilation, additionalTexts);
            string generated = result.GeneratedTrees.Single().ToString();

            Assert.Empty(result.Diagnostics);
            Assert.Contains("namespace Generated.Clients", generated);
            Assert.Contains("public partial class TestApi", generated);
            Assert.Contains("/// <summary>Get items</summary>", generated);
            Assert.Contains(
                "public async global::System.Threading.Tasks.Task<global::System.Collections.Generic.List<string>> getItems(global::System.Threading.CancellationToken cancellationToken = default)",
                generated);
            Assert.Contains("public TestApi(global::System.Net.Http.HttpClient httpClient)", generated);
        }

        [Fact]
        public void BundleWithoutDefinitionProducesNoSources()
        {
            CSharpCompilation compilation = CreateCompilation("public class Dummy { }");
            var additionalTexts = ImmutableArray.Create<AdditionalText>(CreateBundleText(
                TestBundleFactory.SpecId,
                TestBundleFactory.Create("{\"openapi\":\"3.1.0\",\"paths\":{}}")));

            GeneratorDriverRunResult result = Run(compilation, additionalTexts);

            Assert.Empty(result.GeneratedTrees);
            Assert.Empty(result.Diagnostics);
        }

        [Fact]
        public void DefinitionWithoutBundleReportsOacg006Error()
        {
            CSharpCompilation compilation = CreateCompilation(
                CreateDefinition(TestBundleFactory.SpecId, "Api", "Generated", "Api"));

            GeneratorDriverRunResult result = Run(compilation, ImmutableArray<AdditionalText>.Empty);
            Diagnostic diagnostic = Assert.Single(result.Diagnostics);

            Assert.Equal("OACG006", diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Empty(result.GeneratedTrees);
        }

        [Fact]
        public void CommittedUnityFixtureGeneratesDefinedClient()
        {
            string bundle = TestAssetLoader.LoadAsset("unity-minimal.normalized-v1.json");
            CSharpCompilation compilation = CreateCompilation(
                CreateDefinition(TestBundleFactory.SpecId, "Api", "Rhycol.OpenApiCodeGen.Generated", "Api"));

            GeneratorDriverRunResult result = RunSingleBundle(compilation, bundle);
            string generated = result.GeneratedTrees.Single().ToString();

            Assert.Empty(result.Diagnostics);
            Assert.Contains("namespace Rhycol.OpenApiCodeGen.Generated", generated);
            Assert.Contains("public partial class Api", generated);
        }

        [Fact]
        public void CaseSensitiveSuffixNearMissIsIgnored()
        {
            CSharpCompilation compilation = CreateCompilation(
                CreateDefinition(TestBundleFactory.SpecId, "Api", "Generated", "Api"));
            var additionalTexts = ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText(
                    TestBundleFactory.SpecId + ".Rhycol.OpenApiCodeGen.SourceGenerator.ADDITIONALFILE",
                    TestBundleFactory.Create("{\"openapi\":\"3.1.0\",\"paths\":{}}")));

            GeneratorDriverRunResult result = Run(compilation, additionalTexts);
            Diagnostic diagnostic = Assert.Single(result.Diagnostics);

            Assert.Equal("OACG006", diagnostic.Id);
            Assert.Empty(result.GeneratedTrees);
        }

        [Fact]
        public void MalformedFieldOrderReportsOacg001ErrorWithoutCascadingMissingError()
        {
            string bundle = TestBundleFactory.Create("{}").Replace(
                "\"specId\":",
                "\"unexpected\":0,\"specId\":");
            CSharpCompilation compilation = CreateCompilation(
                CreateDefinition(TestBundleFactory.SpecId, "Api", "Generated", "Api"));

            Diagnostic diagnostic = Assert.Single(RunSingleBundle(compilation, bundle).Diagnostics);

            Assert.Equal("OACG001", diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        }

        [Fact]
        public void UnsupportedBundleVersionReportsOacg002ErrorWithoutCascadingMissingError()
        {
            CSharpCompilation compilation = CreateCompilation(
                CreateDefinition(TestBundleFactory.SpecId, "Api", "Generated", "Api"));

            Diagnostic diagnostic = Assert.Single(RunSingleBundle(
                compilation,
                TestBundleFactory.Create("{}", formatVersion: 2)).Diagnostics);

            Assert.Equal("OACG002", diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        }

        [Fact]
        public void InvalidDocumentReportsOacg100AtRawSourceLocation()
        {
            const string Root = "{\"kind\":\"array\",\"line\":7,\"column\":9,\"items\":[]}";
            const string SourcePath = "Assets/Specs/not-openapi.json";
            string bundle = TestBundleFactory.CreateWithEncodedRoot(Root, sourcePath: SourcePath);
            CSharpCompilation compilation = CreateCompilation(
                CreateDefinition(TestBundleFactory.SpecId, "Api", "Generated", "Api"));

            Diagnostic diagnostic = Assert.Single(RunSingleBundle(compilation, bundle).Diagnostics);
            FileLinePositionSpan lineSpan = diagnostic.Location.GetLineSpan();

            Assert.Equal("OACG100", diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Equal(SourcePath, lineSpan.Path);
            Assert.Equal(new LinePosition(6, 8), lineSpan.StartLinePosition);
            Assert.Contains("logical path ''", diagnostic.GetMessage());
        }

        [Fact]
        public void DuplicateBundleSpecIdReportsOacg004Error()
        {
            string bundle = TestBundleFactory.Create("{\"openapi\":\"3.1.0\",\"paths\":{}}");
            var additionalTexts = ImmutableArray.Create<AdditionalText>(
                CreateBundleText(TestBundleFactory.SpecId, bundle, "first"),
                CreateBundleText(TestBundleFactory.SpecId, bundle, "second"));
            CSharpCompilation compilation = CreateCompilation(
                CreateDefinition(TestBundleFactory.SpecId, "Api", "Generated", "Api"));

            GeneratorDriverRunResult result = Run(compilation, additionalTexts);
            Diagnostic diagnostic = Assert.Single(result.Diagnostics);

            Assert.Equal("OACG004", diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Empty(result.GeneratedTrees);
        }

        [Fact]
        public void InvalidAttributeArgumentsReportOacg005Error()
        {
            CSharpCompilation compilation = CreateCompilation(
                CreateDefinition(string.Empty, "Api", "Generated", "Api"));

            GeneratorDriverRunResult result = Run(compilation, ImmutableArray<AdditionalText>.Empty);
            Diagnostic diagnostic = Assert.Single(result.Diagnostics);

            Assert.Equal("OACG005", diagnostic.Id);
            Assert.Contains("specId", diagnostic.GetMessage());
            Assert.Empty(result.GeneratedTrees);
        }

        [Theory]
        [InlineData("WrongApi", null, true, "apiName")]
        [InlineData("Api", "Other.Namespace", true, "generatedNamespace")]
        [InlineData("Api", null, false, "partial class")]
        public void InvalidDefinitionTargetReportsOacg005Error(
            string className,
            string? targetNamespace,
            bool isPartial,
            string expectedMessage)
        {
            CSharpCompilation compilation = CreateCompilation(CreateDefinition(
                TestBundleFactory.SpecId,
                "Api",
                "Generated",
                className,
                targetNamespace: targetNamespace,
                isPartial: isPartial));

            GeneratorDriverRunResult result = Run(compilation, ImmutableArray<AdditionalText>.Empty);
            Diagnostic diagnostic = Assert.Single(result.Diagnostics);

            Assert.Equal("OACG005", diagnostic.Id);
            Assert.Contains(expectedMessage, diagnostic.GetMessage());
            Assert.Empty(result.GeneratedTrees);
        }

        [Fact]
        public void MalformedBundleSuppressesMissingDiagnosticOnlyForItsFileSpecId()
        {
            CSharpCompilation compilation = CreateCompilation(
                CreateDefinition(TestBundleFactory.SpecId, "FirstApi", "Generated.First", "FirstApi") +
                CreateDefinition(SecondSpecId, "SecondApi", "Generated.Second", "SecondApi"));
            string malformedBundle = TestBundleFactory.Create("{}").Replace(
                "\"specId\":",
                "\"unexpected\":0,\"specId\":");
            var additionalTexts = ImmutableArray.Create<AdditionalText>(CreateBundleText(
                TestBundleFactory.SpecId,
                malformedBundle));

            GeneratorDriverRunResult result = Run(compilation, additionalTexts);

            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "OACG001");
            Diagnostic missingDiagnostic = Assert.Single(
                result.Diagnostics,
                diagnostic => diagnostic.Id == "OACG006");
            Assert.Contains(SecondSpecId, missingDiagnostic.GetMessage());
            Assert.Empty(result.GeneratedTrees);
        }

        [Fact]
        public void DefinitionAndBundleSpecIdMismatchReportsMissingBundle()
        {
            CSharpCompilation compilation = CreateCompilation(
                CreateDefinition(TestBundleFactory.SpecId, "Api", "Generated", "Api"));
            var additionalTexts = ImmutableArray.Create<AdditionalText>(CreateBundleText(
                SecondSpecId,
                TestBundleFactory.Create(
                    "{\"openapi\":\"3.1.0\",\"paths\":{}}",
                    specId: SecondSpecId)));

            GeneratorDriverRunResult result = Run(compilation, additionalTexts);
            Diagnostic diagnostic = Assert.Single(result.Diagnostics);

            Assert.Equal("OACG006", diagnostic.Id);
            Assert.Empty(result.GeneratedTrees);
        }

        [Fact]
        public void DuplicateDefinitionSpecIdReportsOacg007AtEachDefinition()
        {
            CSharpCompilation compilation = CreateCompilation(
                CreateDefinition(TestBundleFactory.SpecId, "FirstApi", "Generated.First", "FirstApi") +
                CreateDefinition(TestBundleFactory.SpecId, "SecondApi", "Generated.Second", "SecondApi"));
            var additionalTexts = ImmutableArray.Create<AdditionalText>(CreateBundleText(
                TestBundleFactory.SpecId,
                TestBundleFactory.Create("{\"openapi\":\"3.1.0\",\"paths\":{}}")));

            GeneratorDriverRunResult result = Run(compilation, additionalTexts);

            Assert.Equal(2, result.Diagnostics.Length);
            Assert.All(result.Diagnostics, diagnostic => Assert.Equal("OACG007", diagnostic.Id));
            Assert.All(result.Diagnostics, diagnostic => Assert.True(diagnostic.Location.IsInSource));
            Assert.Empty(result.GeneratedTrees);
        }

        [Fact]
        public void UnsupportedDocumentFormatReportsOacg008Error()
        {
            CSharpCompilation compilation = CreateCompilation(
                @"
namespace Generated
{
    [global::Rhycol.OpenApiCodeGen.SourceGenerator.OpenApiClientDefinitionAttribute(
        ""0123456789abcdef0123456789abcdef"",
        ""Api"",
        ""Generated"",
        (global::Rhycol.OpenApiCodeGen.SourceGenerator.OpenApiDocumentFormat)2)]
    public partial class Api { }
}
");

            GeneratorDriverRunResult result = Run(compilation, ImmutableArray<AdditionalText>.Empty);
            Diagnostic diagnostic = Assert.Single(result.Diagnostics);

            Assert.Equal("OACG008", diagnostic.Id);
            Assert.Contains("Json (0) and Yaml (1)", diagnostic.GetMessage());
            Assert.Empty(result.GeneratedTrees);
        }

        [Fact]
        public void YamlDocumentFormatGeneratesFromMatchingBundle()
        {
            CSharpCompilation compilation = CreateCompilation(
                CreateDefinition(
                    TestBundleFactory.SpecId,
                    "Api",
                    "Generated",
                    "Api",
                    "Yaml"));
            var additionalTexts = ImmutableArray.Create<AdditionalText>(CreateBundleText(
                TestBundleFactory.SpecId,
                TestBundleFactory.Create(
                    "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Sample\",\"version\":\"1.0.0\"},\"paths\":{}}")));

            GeneratorDriverRunResult result = Run(compilation, additionalTexts);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);
            Assert.Contains("public partial class Api", result.GeneratedTrees.Single().ToString());
        }

        [Fact]
        public void JsonAndYamlDocumentFormatsProduceIdenticalGeneratedSource()
        {
            const string OpenApi =
                "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Sample\",\"version\":\"1.0.0\"},\"paths\":{}}";
            var additionalTexts = ImmutableArray.Create<AdditionalText>(CreateBundleText(
                TestBundleFactory.SpecId,
                TestBundleFactory.Create(OpenApi)));
            GeneratorDriverRunResult jsonResult = Run(
                CreateCompilation(CreateDefinition(
                    TestBundleFactory.SpecId,
                    "Api",
                    "Generated",
                    "Api",
                    "Json")),
                additionalTexts);
            GeneratorDriverRunResult yamlResult = Run(
                CreateCompilation(CreateDefinition(
                    TestBundleFactory.SpecId,
                    "Api",
                    "Generated",
                    "Api",
                    "Yaml")),
                additionalTexts);

            Assert.Empty(jsonResult.Diagnostics);
            Assert.Empty(yamlResult.Diagnostics);
            Assert.Equal(
                jsonResult.Results.Single().GeneratedSources.Select(source => source.HintName),
                yamlResult.Results.Single().GeneratedSources.Select(source => source.HintName));
            Assert.Equal(
                jsonResult.GeneratedTrees.Select(tree => tree.ToString()),
                yamlResult.GeneratedTrees.Select(tree => tree.ToString()));
        }

        [Fact]
        public void RawJsonAndYamlNormalizeToParityBundlesAndIdenticalGeneratedSource()
        {
            const string JsonSourcePath = "Assets/Specs/parity.json";
            const string YamlSourcePath = "Assets/Specs/parity.yaml";
            const string RawJson = @"{
  ""openapi"": ""3.0.3"",
  ""info"": { ""title"": ""Parity Sample"", ""version"": ""1.0.0"" },
  ""servers"": [ { ""url"": ""https://api.example.test/v1"" } ],
  ""paths"": {
    ""/pets"": {
      ""get"": {
        ""operationId"": ""listPets"",
        ""summary"": ""List pets"",
        ""responses"": {
          ""200"": {
            ""description"": ""OK"",
            ""content"": {
              ""application/json"": {
                ""schema"": { ""$ref"": ""#/components/schemas/Pet"" }
              }
            }
          }
        }
      }
    }
  },
  ""components"": {
    ""schemas"": {
      ""Pet"": {
        ""type"": ""object"",
        ""required"": [ ""id"" ],
        ""properties"": {
          ""id"": { ""type"": ""integer"", ""format"": ""int64"" },
          ""name"": { ""type"": ""string"" }
        }
      }
    }
  }
}";
            const string RawYaml = @"openapi: 3.0.3
info:
  title: Parity Sample
  version: 1.0.0
servers:
  - url: ""https://api.example.test/v1""
paths:
  /pets:
    get:
      operationId: listPets
      summary: List pets
      responses:
        ""200"":
          description: OK
          content:
            application/json:
              schema:
                $ref: ""#/components/schemas/Pet""
components:
  schemas:
    Pet:
      type: object
      required:
        - id
      properties:
        id:
          type: integer
          format: int64
        name:
          type: string
";

            Rhycol.OpenApiCodeGen.SourceGenerator.Editor.NormalizedSpecBundle jsonBundle =
                new Rhycol.OpenApiCodeGen.SourceGenerator.Editor.RawJsonNormalizer().Normalize(
                    Encoding.UTF8.GetBytes(RawJson),
                    TestBundleFactory.SpecId,
                    JsonSourcePath);
            Rhycol.OpenApiCodeGen.SourceGenerator.Editor.NormalizedSpecBundle yamlBundle =
                new Rhycol.OpenApiCodeGen.SourceGenerator.Editor.RawYamlNormalizer().Normalize(
                    Encoding.UTF8.GetBytes(RawYaml),
                    TestBundleFactory.SpecId,
                    YamlSourcePath);

            Assert.Equal(TestBundleFactory.SpecId, jsonBundle.SpecId);
            Assert.Equal(TestBundleFactory.SpecId, yamlBundle.SpecId);
            Assert.NotEqual(jsonBundle.RawSha256, yamlBundle.RawSha256);
            Assert.NotEqual(jsonBundle.SourcePath, yamlBundle.SourcePath);
            Assert.False(jsonBundle.Bytes.SequenceEqual(yamlBundle.Bytes));

            string jsonBundleText = Encoding.UTF8.GetString(jsonBundle.Bytes);
            string yamlBundleText = Encoding.UTF8.GetString(yamlBundle.Bytes);
            NormalizedSpecBundle jsonAnalyzerBundle = NormalizedSpecBundleReader.Read(jsonBundleText);
            NormalizedSpecBundle yamlAnalyzerBundle = NormalizedSpecBundleReader.Read(yamlBundleText);
            Assert.Equal(jsonAnalyzerBundle.Root.GetRawJson(), yamlAnalyzerBundle.Root.GetRawJson());

            CSharpCompilation jsonCompilation = CreateParityCompilation(CreateDefinition(
                TestBundleFactory.SpecId,
                "ParityApi",
                "Generated.Parity",
                "ParityApi",
                "Json"));
            CSharpCompilation yamlCompilation = CreateParityCompilation(CreateDefinition(
                TestBundleFactory.SpecId,
                "ParityApi",
                "Generated.Parity",
                "ParityApi",
                "Yaml"));
            ImmutableArray<AdditionalText> jsonAdditionalTexts = ImmutableArray.Create<AdditionalText>(
                CreateBundleText(TestBundleFactory.SpecId, jsonBundleText));
            ImmutableArray<AdditionalText> yamlAdditionalTexts = ImmutableArray.Create<AdditionalText>(
                CreateBundleText(TestBundleFactory.SpecId, yamlBundleText));

            GeneratorDriver jsonDriver = CreateDriver(jsonCompilation, jsonAdditionalTexts);
            jsonDriver = jsonDriver.RunGeneratorsAndUpdateCompilation(
                jsonCompilation,
                out Compilation jsonOutputCompilation,
                out ImmutableArray<Diagnostic> jsonGeneratorDiagnostics);
            GeneratorDriverRunResult jsonResult = jsonDriver.GetRunResult();
            GeneratorDriver yamlDriver = CreateDriver(yamlCompilation, yamlAdditionalTexts);
            yamlDriver = yamlDriver.RunGeneratorsAndUpdateCompilation(
                yamlCompilation,
                out Compilation yamlOutputCompilation,
                out ImmutableArray<Diagnostic> yamlGeneratorDiagnostics);
            GeneratorDriverRunResult yamlResult = yamlDriver.GetRunResult();

            Assert.Empty(jsonResult.Diagnostics);
            Assert.Empty(yamlResult.Diagnostics);
            Assert.Empty(jsonGeneratorDiagnostics);
            Assert.Empty(yamlGeneratorDiagnostics);
            Assert.Empty(jsonOutputCompilation.GetDiagnostics().Where(
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
            Assert.Empty(yamlOutputCompilation.GetDiagnostics().Where(
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

            string[] jsonHintNames = jsonResult.Results.Single().GeneratedSources
                .OrderBy(source => source.HintName, StringComparer.Ordinal)
                .Select(source => source.HintName)
                .ToArray();
            string[] yamlHintNames = yamlResult.Results.Single().GeneratedSources
                .OrderBy(source => source.HintName, StringComparer.Ordinal)
                .Select(source => source.HintName)
                .ToArray();
            string[] jsonGeneratedSources = jsonResult.Results.Single().GeneratedSources
                .OrderBy(source => source.HintName, StringComparer.Ordinal)
                .Select(source => source.SourceText.ToString())
                .ToArray();
            string[] yamlGeneratedSources = yamlResult.Results.Single().GeneratedSources
                .OrderBy(source => source.HintName, StringComparer.Ordinal)
                .Select(source => source.SourceText.ToString())
                .ToArray();

            Assert.NotEmpty(jsonHintNames);
            Assert.Equal(jsonHintNames, yamlHintNames);
            Assert.Equal(jsonGeneratedSources, yamlGeneratedSources);
        }

        [Fact]
        public void ChangingDefinitionFormatInvalidatesDefinitionMatchButReusesBundleParse()
        {
            const string OpenApi =
                "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Sample\",\"version\":\"1.0.0\"},\"paths\":{}}";
            var additionalTexts = ImmutableArray.Create<AdditionalText>(CreateBundleText(
                TestBundleFactory.SpecId,
                TestBundleFactory.Create(OpenApi)));
            CSharpCompilation jsonCompilation = CreateCompilation(CreateDefinition(
                TestBundleFactory.SpecId,
                "Api",
                "Generated",
                "Api",
                "Json"));
            CSharpCompilation yamlCompilation = CreateCompilation(CreateDefinition(
                TestBundleFactory.SpecId,
                "Api",
                "Generated",
                "Api",
                "Yaml"));
            GeneratorDriver driver = CreateDriver(jsonCompilation, additionalTexts)
                .RunGenerators(jsonCompilation);
            GeneratorDriverRunResult first = driver.GetRunResult();

            driver = driver.RunGenerators(yamlCompilation);
            GeneratorDriverRunResult changed = driver.GetRunResult();

            Assert.Empty(first.Diagnostics);
            Assert.Empty(changed.Diagnostics);
            Assert.Equal(
                first.GeneratedTrees.Select(tree => tree.ToString()),
                changed.GeneratedTrees.Select(tree => tree.ToString()));
            IReadOnlyList<IncrementalStepRunReason> definitionReasons = GetReasons(
                changed,
                "OpenApiClientDefinitions");
            IReadOnlyList<IncrementalStepRunReason> parseReasons = GetReasons(
                changed,
                "OpenApiParseNormalizedBundle");
            IReadOnlyList<IncrementalStepRunReason> matchReasons = GetReasons(
                changed,
                "OpenApiMatchDefinitionToBundle");
            Assert.Contains(IncrementalStepRunReason.Modified, definitionReasons);
            Assert.Contains(IncrementalStepRunReason.Cached, parseReasons);
            Assert.Contains(IncrementalStepRunReason.Modified, matchReasons);
            Assert.DoesNotContain(IncrementalStepRunReason.Cached, matchReasons);
        }

        [Fact]
        public void FileNameAndBundleSpecIdMismatchReportsOacg009Error()
        {
            CSharpCompilation compilation = CreateCompilation(
                CreateDefinition(TestBundleFactory.SpecId, "Api", "Generated", "Api"));
            var additionalTexts = ImmutableArray.Create<AdditionalText>(CreateBundleText(
                SecondSpecId,
                TestBundleFactory.Create("{\"openapi\":\"3.1.0\",\"paths\":{}}")));

            GeneratorDriverRunResult result = Run(compilation, additionalTexts);
            Diagnostic diagnostic = Assert.Single(result.Diagnostics);

            Assert.Equal("OACG009", diagnostic.Id);
            Assert.Contains(TestBundleFactory.SpecId, diagnostic.GetMessage());
            Assert.Contains(SecondSpecId, diagnostic.GetMessage());
            Assert.Empty(result.GeneratedTrees);
        }

        [Fact]
        public void BundleEnvelopeMismatchForDefinitionFileReportsOnlyOacg009Error()
        {
            CSharpCompilation compilation = CreateCompilation(
                CreateDefinition(TestBundleFactory.SpecId, "Api", "Generated", "Api"));
            var additionalTexts = ImmutableArray.Create<AdditionalText>(CreateBundleText(
                TestBundleFactory.SpecId,
                TestBundleFactory.Create(
                    "{\"openapi\":\"3.1.0\",\"paths\":{}}",
                    specId: SecondSpecId)));

            GeneratorDriverRunResult result = Run(compilation, additionalTexts);
            Diagnostic diagnostic = Assert.Single(result.Diagnostics);

            Assert.Equal("OACG009", diagnostic.Id);
            Assert.Contains(TestBundleFactory.SpecId, diagnostic.GetMessage());
            Assert.Contains(SecondSpecId, diagnostic.GetMessage());
            Assert.Empty(result.GeneratedTrees);
        }

        [Fact]
        public void MultipleDefinitionsMatchOnlyTheirOwnSpecsAndUseStableUniqueHintNames()
        {
            const string JsonA =
                "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"A\",\"version\":\"1\"},\"paths\":{\"/a\":{\"get\":{\"operationId\":\"getA\",\"responses\":{\"204\":{\"description\":\"No Content\"}}}}}}";
            const string JsonB =
                "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"B\",\"version\":\"1\"},\"paths\":{\"/b\":{\"get\":{\"operationId\":\"getB\",\"responses\":{\"204\":{\"description\":\"No Content\"}}}}}}";
            CSharpCompilation compilation = CreateCompilation(
                CreateDefinition(TestBundleFactory.SpecId, "FirstApi", "Generated.First", "FirstApi") +
                CreateDefinition(SecondSpecId, "SecondApi", "Generated.Second", "SecondApi"));
            var additionalTexts = ImmutableArray.Create<AdditionalText>(
                CreateBundleText(TestBundleFactory.SpecId, TestBundleFactory.Create(JsonA)),
                CreateBundleText(
                    SecondSpecId,
                    TestBundleFactory.Create(JsonB, specId: SecondSpecId)));

            GeneratorDriverRunResult result = Run(compilation, additionalTexts);
            GeneratorRunResult generatorResult = Assert.Single(result.Results);
            string firstOutput = Assert.Single(
                result.GeneratedTrees,
                tree => tree.ToString().Contains("class FirstApi")).ToString();
            string secondOutput = Assert.Single(
                result.GeneratedTrees,
                tree => tree.ToString().Contains("class SecondApi")).ToString();
            string[] hintNames = generatorResult.GeneratedSources.Select(source => source.HintName).ToArray();

            Assert.Empty(result.Diagnostics);
            Assert.Equal(2, result.GeneratedTrees.Length);
            Assert.Contains("getA", firstOutput);
            Assert.DoesNotContain("getB", firstOutput);
            Assert.Contains("getB", secondOutput);
            Assert.DoesNotContain("getA", secondOutput);
            Assert.Equal(2, hintNames.Distinct(StringComparer.Ordinal).Count());
            Assert.Contains(hintNames, hintName => hintName.Contains(TestBundleFactory.SpecId));
            Assert.Contains(hintNames, hintName => hintName.Contains(SecondSpecId));
        }

        [Fact]
        public void SeparateCompilationsGenerateOnlyTheDefinitionInEachAssembly()
        {
            const string JsonA =
                "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"A\",\"version\":\"1\"},\"paths\":{\"/a\":{\"get\":{\"operationId\":\"getA\",\"responses\":{\"204\":{\"description\":\"No Content\"}}}}}}";
            const string JsonB =
                "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"B\",\"version\":\"1\"},\"paths\":{\"/b\":{\"get\":{\"operationId\":\"getB\",\"responses\":{\"204\":{\"description\":\"No Content\"}}}}}}";
            var additionalTexts = ImmutableArray.Create<AdditionalText>(
                CreateBundleText(TestBundleFactory.SpecId, TestBundleFactory.Create(JsonA)),
                CreateBundleText(
                    SecondSpecId,
                    TestBundleFactory.Create(JsonB, specId: SecondSpecId)));
            CSharpCompilation firstAssembly = CreateCompilation(
                CreateDefinition(TestBundleFactory.SpecId, "FirstApi", "Generated.First", "FirstApi"),
                "FirstAssembly");
            CSharpCompilation secondAssembly = CreateCompilation(
                CreateDefinition(SecondSpecId, "SecondApi", "Generated.Second", "SecondApi"),
                "SecondAssembly");

            GeneratorDriverRunResult firstResult = Run(firstAssembly, additionalTexts);
            GeneratorDriverRunResult secondResult = Run(secondAssembly, additionalTexts);

            string firstOutput = Assert.Single(firstResult.GeneratedTrees).ToString();
            string secondOutput = Assert.Single(secondResult.GeneratedTrees).ToString();
            Assert.Contains("class FirstApi", firstOutput);
            Assert.Contains("getA", firstOutput);
            Assert.DoesNotContain("getB", firstOutput);
            Assert.Contains("class SecondApi", secondOutput);
            Assert.Contains("getB", secondOutput);
            Assert.DoesNotContain("getA", secondOutput);
            Assert.Empty(firstResult.Diagnostics);
            Assert.Empty(secondResult.Diagnostics);
        }

        [Fact]
        public void ReusedDriverKeepsUnrelatedClientInputUnchangedWhenOneBundleChanges()
        {
            const string JsonA =
                "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"A\",\"version\":\"1\"},\"paths\":{\"/a\":{\"get\":{\"operationId\":\"getA\",\"responses\":{\"204\":{\"description\":\"No Content\"}}}}}}";
            const string JsonB =
                "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"B\",\"version\":\"1\"},\"paths\":{\"/b\":{\"get\":{\"operationId\":\"getB\",\"responses\":{\"204\":{\"description\":\"No Content\"}}}}}}";
            const string JsonBChanged =
                "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"B\",\"version\":\"1\"},\"paths\":{\"/b2\":{\"get\":{\"operationId\":\"getBChanged\",\"responses\":{\"204\":{\"description\":\"No Content\"}}}}}}";
            CSharpCompilation compilation = CreateCompilation(
                CreateDefinition(TestBundleFactory.SpecId, "FirstApi", "Generated.First", "FirstApi") +
                CreateDefinition(SecondSpecId, "SecondApi", "Generated.Second", "SecondApi"));
            var firstBundle = CreateBundleText(
                TestBundleFactory.SpecId,
                TestBundleFactory.Create(JsonA));
            var originalSecondBundle = CreateBundleText(
                SecondSpecId,
                TestBundleFactory.Create(JsonB, specId: SecondSpecId));
            var changedSecondBundle = CreateBundleText(
                SecondSpecId,
                TestBundleFactory.Create(JsonBChanged, specId: SecondSpecId));
            GeneratorDriver driver = CreateDriver(
                compilation,
                ImmutableArray.Create<AdditionalText>(firstBundle, originalSecondBundle));

            driver = driver.RunGenerators(compilation);
            GeneratorDriverRunResult firstResult = driver.GetRunResult();
            string firstClientBefore = FindGeneratedClient(firstResult, "FirstApi");
            driver = driver.RunGenerators(compilation);
            Assert.All(
                GetReasons(driver.GetRunResult(), "OpenApiMatchDefinitionToBundle"),
                reason => Assert.Equal(IncrementalStepRunReason.Cached, reason));

            driver = driver.ReplaceAdditionalText(originalSecondBundle, changedSecondBundle)
                .RunGenerators(compilation);
            GeneratorDriverRunResult changedResult = driver.GetRunResult();
            IReadOnlyList<IncrementalStepRunReason> parseReasons = GetReasons(
                changedResult,
                "OpenApiParseNormalizedBundle");
            IReadOnlyList<IncrementalStepRunReason> matchReasons = GetReasons(
                changedResult,
                "OpenApiMatchDefinitionToBundle");

            Assert.Contains(IncrementalStepRunReason.Cached, parseReasons);
            Assert.Contains(IncrementalStepRunReason.Modified, parseReasons);
            Assert.Contains(IncrementalStepRunReason.Unchanged, matchReasons);
            Assert.Contains(IncrementalStepRunReason.Modified, matchReasons);
            Assert.Equal(firstClientBefore, FindGeneratedClient(changedResult, "FirstApi"));
            Assert.Contains("getBChanged", FindGeneratedClient(changedResult, "SecondApi"));
            Assert.DoesNotContain("getB\"", FindGeneratedClient(changedResult, "SecondApi"));
        }

        private static GeneratorDriverRunResult RunSingleBundle(
            CSharpCompilation compilation,
            string bundle)
        {
            var texts = ImmutableArray.Create<AdditionalText>(CreateBundleText(
                TestBundleFactory.SpecId,
                bundle));
            return Run(compilation, texts);
        }

        private static GeneratorDriverRunResult Run(
            CSharpCompilation compilation,
            ImmutableArray<AdditionalText> additionalTexts)
        {
            return CreateDriver(compilation, additionalTexts)
                .RunGenerators(compilation)
                .GetRunResult();
        }

        private static GeneratorDriver CreateDriver(
            CSharpCompilation compilation,
            ImmutableArray<AdditionalText> additionalTexts)
        {
            return CSharpGeneratorDriver.Create(
                generators: new[] { new ApiClientCodeGenerator().AsSourceGenerator() },
                additionalTexts: additionalTexts,
                parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options,
                driverOptions: new GeneratorDriverOptions(
                    disabledOutputs: IncrementalGeneratorOutputKind.None,
                    trackIncrementalGeneratorSteps: true));
        }

        private static CSharpCompilation CreateCompilation(
            string definitions,
            string assemblyName = "GeneratorTests")
        {
            return CSharpCompilation.Create(
                assemblyName,
                new[] { CSharpSyntaxTree.ParseText(AttributeContract + definitions) },
                new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }

        private static CSharpCompilation CreateParityCompilation(string definitions)
        {
            string trustedAssemblies =
                (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
            IEnumerable<MetadataReference> references = trustedAssemblies
                .Split(Path.PathSeparator)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => MetadataReference.CreateFromFile(path));
            return CSharpCompilation.Create(
                "GeneratorParityTests",
                new[] { CSharpSyntaxTree.ParseText(AttributeContract + definitions) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }

        private static string CreateDefinition(
            string specId,
            string apiName,
            string generatedNamespace,
            string className,
            string documentFormat = "Json",
            string? targetNamespace = null,
            bool isPartial = true)
        {
            string declarationNamespace = targetNamespace ?? generatedNamespace;
            return "\nnamespace " + declarationNamespace + @"
{
[global::Rhycol.OpenApiCodeGen.SourceGenerator.OpenApiClientDefinitionAttribute(" +
                   ToCSharpString(specId) + ", " +
                   ToCSharpString(apiName) + ", " +
                   ToCSharpString(generatedNamespace) +
                   ", global::Rhycol.OpenApiCodeGen.SourceGenerator.OpenApiDocumentFormat." +
                   documentFormat + @")]
public " + (isPartial ? "partial " : string.Empty) + "class " + className + @" { }
}
";
        }

        private static string ToCSharpString(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static InMemoryAdditionalText CreateBundleText(
            string fileSpecId,
            string content,
            string? directory = null)
        {
            string path = fileSpecId + ApiClientCodeGenerator.AdditionalFileSuffix;
            if (!string.IsNullOrEmpty(directory))
            {
                path = directory + "/" + path;
            }

            return new InMemoryAdditionalText(path, content);
        }

        private static string FindGeneratedClient(GeneratorDriverRunResult result, string apiName)
        {
            return Assert.Single(
                result.GeneratedTrees,
                tree => tree.ToString().Contains("class " + apiName)).ToString();
        }

        private static IReadOnlyList<IncrementalStepRunReason> GetReasons(
            GeneratorDriverRunResult result,
            string trackingName)
        {
            ImmutableArray<IncrementalGeneratorRunStep> steps =
                result.Results.Single().TrackedSteps[trackingName];
            return steps.SelectMany(static step => step.Outputs)
                .Select(static output => output.Reason)
                .ToArray();
        }
    }

    internal sealed class InMemoryAdditionalText : AdditionalText
    {
        private readonly SourceText _text;

        public InMemoryAdditionalText(string path, string content)
        {
            Path = path;
            _text = SourceText.From(content);
        }

        public override string Path { get; }

        public override SourceText GetText(CancellationToken cancellationToken = default)
        {
            return _text;
        }
    }
}
