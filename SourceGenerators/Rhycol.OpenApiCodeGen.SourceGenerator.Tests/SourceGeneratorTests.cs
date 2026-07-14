using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    public class SourceGeneratorTests
    {
        [Fact]
        public async Task GeneratesClientFromAdditionalFile()
        {
            var openApiJson = @"{
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

            var compilation = CSharpCompilation.Create(
                "GeneratorTests",
                new[] { CSharpSyntaxTree.ParseText("public class Dummy { }") },
                new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var additionalTexts = ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText(
                    TestBundleFactory.SpecId + ApiClientCodeGenerator.AdditionalFileSuffix,
                    TestBundleFactory.Create(openApiJson)));

            var optionsProvider = new InMemoryAnalyzerConfigOptionsProvider(
                new Dictionary<string, string>
                {
                    ["build_property.OpenApiApiName"] = "TestApi",
                    ["build_property.OpenApiNamespace"] = "Rhycol.OpenApiCodeGen.Generated"
                });

            var generator = new ApiClientCodeGenerator().AsSourceGenerator();
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                generators: new[] { generator },
                additionalTexts: additionalTexts,
                parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options,
                optionsProvider: optionsProvider);

            driver = driver.RunGenerators(compilation);

            var result = driver.GetRunResult();
            var generated = result.GeneratedTrees.Select(tree => tree.ToString()).ToArray();

            Assert.Contains(generated, text => text.Contains("class TestApi"));
            Assert.Contains(generated, text => text.Contains("/// <summary>Get items</summary>"));
            Assert.Contains(generated, text => text.Contains("public System.Collections.Generic.IReadOnlyList<string> getItems()"));
            await Task.CompletedTask;
        }

        [Fact]
        public void NoBundleProducesNoSourcesOrDiagnostics()
        {
            GeneratorDriver driver = CreateDriver(ImmutableArray<AdditionalText>.Empty);

            GeneratorDriverRunResult result = driver.RunGenerators(CreateCompilation()).GetRunResult();

            Assert.Empty(result.GeneratedTrees);
            Assert.Empty(result.Diagnostics);
        }

        [Fact]
        public void CommittedUnityFixtureGeneratesDefaultClient()
        {
            string bundle = TestAssetLoader.LoadAsset("unity-minimal.normalized-v1.json");

            GeneratorDriverRunResult result = RunSingleBundle(bundle);
            string generated = result.GeneratedTrees.Single().ToString();

            Assert.Empty(result.Diagnostics);
            Assert.Contains("namespace Rhycol.OpenApiCodeGen.Generated", generated);
            Assert.Contains("public class Api", generated);
        }

        [Fact]
        public void CaseSensitiveSuffixNearMissIsIgnored()
        {
            var additionalTexts = ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText(
                    TestBundleFactory.SpecId + ".Rhycol.OpenApiCodeGen.SourceGenerator.ADDITIONALFILE",
                    TestBundleFactory.Create("{\"openapi\":\"3.1.0\",\"paths\":{}}")));

            GeneratorDriverRunResult result = CreateDriver(additionalTexts)
                .RunGenerators(CreateCompilation())
                .GetRunResult();

            Assert.Empty(result.GeneratedTrees);
            Assert.Empty(result.Diagnostics);
        }

        [Fact]
        public void MalformedFieldOrderReportsOacg001Error()
        {
            string bundle = TestBundleFactory.Create("{}").Replace(
                "\"specId\":",
                "\"unexpected\":0,\"specId\":");

            Diagnostic diagnostic = RunSingleBundle(bundle).Diagnostics.Single();

            Assert.Equal("OACG001", diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        }

        [Fact]
        public void UnsupportedVersionReportsOacg002Error()
        {
            Diagnostic diagnostic = RunSingleBundle(
                TestBundleFactory.Create("{}", formatVersion: 2)).Diagnostics.Single();

            Assert.Equal("OACG002", diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        }

        [Fact]
        public void MappingFailureReportsOacg003AtRawSourceLocation()
        {
            const string Root = "{\"kind\":\"array\",\"line\":7,\"column\":9,\"items\":[]}";
            const string SourcePath = "Assets/Specs/not-openapi.json";
            string bundle = TestBundleFactory.CreateWithEncodedRoot(Root, sourcePath: SourcePath);

            Diagnostic diagnostic = RunSingleBundle(bundle).Diagnostics.Single();
            FileLinePositionSpan lineSpan = diagnostic.Location.GetLineSpan();

            Assert.Equal("OACG003", diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Equal(SourcePath, lineSpan.Path);
            Assert.Equal(new LinePosition(6, 8), lineSpan.StartLinePosition);
            Assert.Contains("logical path ''", diagnostic.GetMessage());
        }

        [Fact]
        public void MultipleBundlesReportOacg004Error()
        {
            string bundle = TestBundleFactory.Create("{}");
            var additionalTexts = ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("first" + ApiClientCodeGenerator.AdditionalFileSuffix, bundle),
                new InMemoryAdditionalText("second" + ApiClientCodeGenerator.AdditionalFileSuffix, bundle));

            GeneratorDriverRunResult result = CreateDriver(additionalTexts)
                .RunGenerators(CreateCompilation())
                .GetRunResult();
            Diagnostic diagnostic = result.Diagnostics.Single();

            Assert.Equal("OACG004", diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Empty(result.GeneratedTrees);
        }

        [Fact]
        public void ReusedDriverKeepsUnchangedOutputAndUpdatesOneChangedBundle()
        {
            const string JsonA = "{\"openapi\":\"3.1.0\",\"paths\":{\"/a\":{\"get\":{\"operationId\":\"getA\",\"responses\":{}}}}}";
            const string JsonB = "{\"openapi\":\"3.1.0\",\"paths\":{\"/b\":{\"get\":{\"operationId\":\"getB\",\"responses\":{}}}}}";
            string path = TestBundleFactory.SpecId + ApiClientCodeGenerator.AdditionalFileSuffix;
            var firstText = new InMemoryAdditionalText(path, TestBundleFactory.Create(JsonA));
            var secondText = new InMemoryAdditionalText(path, TestBundleFactory.Create(JsonB));
            CSharpCompilation compilation = CreateCompilation();
            GeneratorDriver driver = CreateDriver(ImmutableArray.Create<AdditionalText>(firstText));

            driver = driver.RunGenerators(compilation);
            string firstOutput = driver.GetRunResult().GeneratedTrees.Single().ToString();
            Assert.Equal(
                new[] { IncrementalStepRunReason.New },
                GetReasons(driver.GetRunResult(), "OpenApiReadNormalizedBundle"));
            driver = driver.RunGenerators(compilation);
            string unchangedOutput = driver.GetRunResult().GeneratedTrees.Single().ToString();
            Assert.Equal(
                new[] { IncrementalStepRunReason.Cached },
                GetReasons(driver.GetRunResult(), "OpenApiReadNormalizedBundle"));
            driver = driver.ReplaceAdditionalText(firstText, secondText).RunGenerators(compilation);
            string changedOutput = driver.GetRunResult().GeneratedTrees.Single().ToString();
            Assert.Equal(
                new[] { IncrementalStepRunReason.Modified },
                GetReasons(driver.GetRunResult(), "OpenApiReadNormalizedBundle"));
            Assert.Equal(
                new[] { IncrementalStepRunReason.Modified },
                GetReasons(driver.GetRunResult(), "OpenApiCollectNormalizedBundles"));

            Assert.Equal(firstOutput, unchangedOutput);
            Assert.Contains("getA", firstOutput);
            Assert.DoesNotContain("getA", changedOutput);
            Assert.Contains("getB", changedOutput);
        }

        private static GeneratorDriverRunResult RunSingleBundle(string bundle)
        {
            var texts = ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText(
                    TestBundleFactory.SpecId + ApiClientCodeGenerator.AdditionalFileSuffix,
                    bundle));
            return CreateDriver(texts).RunGenerators(CreateCompilation()).GetRunResult();
        }

        private static GeneratorDriver CreateDriver(ImmutableArray<AdditionalText> additionalTexts)
        {
            CSharpCompilation compilation = CreateCompilation();
            return CSharpGeneratorDriver.Create(
                generators: new[] { new ApiClientCodeGenerator().AsSourceGenerator() },
                additionalTexts: additionalTexts,
                parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options,
                optionsProvider: new InMemoryAnalyzerConfigOptionsProvider(
                    new Dictionary<string, string>()),
                driverOptions: new GeneratorDriverOptions(
                    disabledOutputs: IncrementalGeneratorOutputKind.None,
                    trackIncrementalGeneratorSteps: true));
        }

        private static CSharpCompilation CreateCompilation()
        {
            return CSharpCompilation.Create(
                "GeneratorTests",
                new[] { CSharpSyntaxTree.ParseText("public class Dummy { }") },
                new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
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

    internal sealed class InMemoryAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _globalOptions;

        public InMemoryAnalyzerConfigOptionsProvider(IReadOnlyDictionary<string, string> globalOptions)
        {
            _globalOptions = new InMemoryAnalyzerConfigOptions(globalOptions);
        }

        public override AnalyzerConfigOptions GlobalOptions => _globalOptions;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            return _globalOptions;
        }

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            return _globalOptions;
        }
    }

    internal sealed class InMemoryAnalyzerConfigOptions : AnalyzerConfigOptions
    {
        private readonly IReadOnlyDictionary<string, string> _options;

        public InMemoryAnalyzerConfigOptions(IReadOnlyDictionary<string, string> options)
        {
            _options = options;
        }

        public override bool TryGetValue(string key, out string value)
        {
            return _options.TryGetValue(key, out value!);
        }
    }
}
