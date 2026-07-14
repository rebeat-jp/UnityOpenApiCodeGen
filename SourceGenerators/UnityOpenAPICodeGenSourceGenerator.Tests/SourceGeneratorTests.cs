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

namespace ReBeat.OpenApiCodeGen.SourceGenerator.Tests
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
                new InMemoryAdditionalText("openapi.json", openApiJson));

            var optionsProvider = new InMemoryAnalyzerConfigOptionsProvider(
                new Dictionary<string, string>
                {
                    ["build_property.OpenApiApiName"] = "TestApi",
                    ["build_property.OpenApiNamespace"] = "ReBeat.OpenApiCodeGen.Generated"
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
