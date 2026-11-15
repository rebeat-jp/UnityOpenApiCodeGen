using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Rhycol.OpenApiCodeGen.SourceGenerator.Editor;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    public sealed class ExternalSpecGraphLoaderReaderParityTests
    {
        private const string SpecId = "0123456789abcdef0123456789abcdef";

        [Fact]
        public async Task JsonLoaderBundleIsAcceptedByAnalyzerReaderWithSemanticReferencesOnly()
        {
            string projectRoot = CreateProjectRoot();
            try
            {
                WriteText(
                    projectRoot,
                    "Assets/Specs/root.json",
                    @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Root"", ""version"": ""1"" },
  ""paths"": {
    ""/items"": {
      ""get"": {
        ""responses"": {
          ""204"": { ""description"": ""No content"" },
          ""x-meta"": { ""$ref"": ""missing-operation-extension.json?token=literal"" }
        }
      }
    }
  },
  ""components"": {
    ""schemas"": {
      ""PetAlias"": { ""$ref"": ""bare.yaml"" }
    },
    ""responses"": {
      ""x-shared"": { ""$ref"": ""response.json"" }
    },
    ""examples"": {
      ""Payload"": {
        ""value"": { ""$ref"": ""missing-example.json?token=literal"" }
      }
    }
  }
}");
                WriteText(
                    projectRoot,
                    "Assets/Specs/bare.yaml",
                    "type: object\n" +
                    "properties:\n" +
                    "  name:\n" +
                    "    type: string\n" +
                    "example:\n" +
                    "  $ref: 'missing-bare-example.yaml?token=literal'\n");
                WriteText(
                    projectRoot,
                    "Assets/Specs/response.json",
                    "{\"description\":\"Shared\"}");

                (NormalizedSpecGraph graph, string bundleText, NormalizedSpecBundle bundle) =
                    await LoadWriteAndReadAsync(projectRoot, "Assets/Specs/root.json");

                Assert.Equal(3, graph.Documents.Count);
                Assert.Equal(2, bundle.ReferenceEdges.Count);
                Assert.Contains(
                    bundle.ReferenceEdges,
                    edge => edge.SourcePointer == "/components/schemas/PetAlias/$ref" &&
                            edge.TargetPointer == string.Empty);
                Assert.Contains(
                    bundle.ReferenceEdges,
                    edge => edge.SourcePointer == "/components/responses/x-shared/$ref" &&
                            edge.TargetPointer == string.Empty);
                Assert.DoesNotContain(
                    bundle.ReferenceEdges,
                    edge => edge.SourcePointer.Contains("/responses/x-meta/", StringComparison.Ordinal));
                Assert.Contains("missing-operation-extension.json?token=literal", bundleText);
                Assert.Contains("missing-example.json?token=literal", bundleText);
                Assert.Contains("missing-bare-example.yaml?token=literal", bundleText);
            }
            finally
            {
                Directory.Delete(projectRoot, true);
            }
        }

        [Fact]
        public async Task YamlAliasClonesKeepSemanticAndLiteralReferenceContextsForAnalyzerReader()
        {
            string projectRoot = CreateProjectRoot();
            try
            {
                WriteText(
                    projectRoot,
                    "Assets/Specs/root.yaml",
                    "openapi: 3.1.0\n" +
                    "info:\n" +
                    "  title: Root\n" +
                    "  version: '1'\n" +
                    "paths:\n" +
                    "  /items:\n" +
                    "    get:\n" +
                    "      responses:\n" +
                    "        '204':\n" +
                    "          description: No content\n" +
                    "        x-meta:\n" +
                    "          $ref: 'missing-operation-extension.yaml?token=literal'\n" +
                    "components:\n" +
                    "  schemas:\n" +
                    "    Shared: &semantic\n" +
                    "      $ref: 'bare.json?token=secret'\n" +
                    "    SharedAlias: *semantic\n" +
                    "  examples:\n" +
                    "    Literal:\n" +
                    "      value: &literal\n" +
                    "        $ref: 'missing-example.yaml?token=literal'\n" +
                    "    LiteralAlias:\n" +
                    "      value: *literal\n");
                WriteText(
                    projectRoot,
                    "Assets/Specs/bare.json",
                    "{\"type\":\"object\",\"properties\":{\"id\":{\"type\":\"integer\"}}}");

                (_, string bundleText, NormalizedSpecBundle bundle) =
                    await LoadWriteAndReadAsync(projectRoot, "Assets/Specs/root.yaml");

                Assert.Equal(2, bundle.ReferenceEdges.Count);
                Assert.Contains(
                    bundle.ReferenceEdges,
                    edge => edge.SourcePointer == "/components/schemas/Shared/$ref");
                Assert.Contains(
                    bundle.ReferenceEdges,
                    edge => edge.SourcePointer == "/components/schemas/SharedAlias/$ref");
                Assert.DoesNotContain("token=secret", bundleText);
                Assert.Equal(2, CountOccurrences(bundleText, "missing-example.yaml?token=literal"));
                Assert.Contains("missing-operation-extension.yaml?token=literal", bundleText);
            }
            finally
            {
                Directory.Delete(projectRoot, true);
            }
        }

        private const string PathItemRoot =
            "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Root\",\"version\":\"1\"}," +
            "\"paths\":{\"/pets\":{\"$ref\":\"item.json\"}},\"components\":{}}";

        [Theory]
        [InlineData(
            "{\"parameters\":[{\"name\":\"l\",\"in\":\"query\",\"schema\":{\"$ref\":\"s.json\"}}]," +
            "\"get\":{\"responses\":{\"200\":{\"description\":\"OK\",\"content\":{\"application/json\":{\"schema\":{\"$ref\":\"s.json\"}}}}}}," +
            "\"post\":{\"requestBody\":{\"content\":{\"application/json\":{\"schema\":{\"$ref\":\"s.json\"}}}},\"responses\":{\"204\":{\"description\":\"n\"}}}}",
            new[]
            {
                "/parameters/0/schema/$ref",
                "/get/responses/200/content/application~1json/schema/$ref",
                "/post/requestBody/content/application~1json/schema/$ref"
            })]
        [InlineData(
            "{\"summary\":\"s\",\"parameters\":[{\"name\":\"l\",\"in\":\"query\",\"schema\":{\"$ref\":\"s.json\"}}]}",
            new[] { "/parameters/0/schema/$ref" })]
        [InlineData(
            "{\"servers\":[{\"url\":\"https://x\"}],\"parameters\":[{\"name\":\"l\",\"in\":\"query\",\"schema\":{\"$ref\":\"s.json\"}}]}",
            new[] { "/parameters/0/schema/$ref" })]
        [InlineData("{\"$ref\":\"s.json\",\"summary\":\"chain\"}", new[] { "/$ref" })]
        public async Task BarePathItemFileReferencesAreCollected(string item, string[] expectedPointers)
        {
            string projectRoot = CreateProjectRoot();
            try
            {
                WriteText(projectRoot, "Assets/Specs/root.json", PathItemRoot);
                WriteText(projectRoot, "Assets/Specs/item.json", item);
                WriteText(projectRoot, "Assets/Specs/s.json", "{\"type\":\"integer\"}");

                (_, _, NormalizedSpecBundle bundle) =
                    await LoadWriteAndReadAsync(projectRoot, "Assets/Specs/root.json");

                foreach (string pointer in expectedPointers)
                {
                    Assert.Contains(bundle.ReferenceEdges, edge => edge.SourcePointer == pointer);
                }
            }
            finally
            {
                Directory.Delete(projectRoot, true);
            }
        }

        [Fact]
        public async Task BarePathItemChainIsFollowedAcrossFiles()
        {
            string projectRoot = CreateProjectRoot();
            try
            {
                WriteText(projectRoot, "Assets/Specs/root.json", PathItemRoot);
                WriteText(projectRoot, "Assets/Specs/item.json", "{\"$ref\":\"second.json\"}");
                WriteText(
                    projectRoot,
                    "Assets/Specs/second.json",
                    "{\"parameters\":[{\"name\":\"l\",\"in\":\"query\",\"schema\":{\"$ref\":\"s.json\"}}]}");
                WriteText(projectRoot, "Assets/Specs/s.json", "{\"type\":\"integer\"}");

                (_, string text, NormalizedSpecBundle bundle) =
                    await LoadWriteAndReadAsync(projectRoot, "Assets/Specs/root.json");

                Assert.Contains("Assets/Specs/second.json", text);
                Assert.Contains("Assets/Specs/s.json", text);
                Assert.Equal(3, bundle.ReferenceEdges.Count);
            }
            finally
            {
                Directory.Delete(projectRoot, true);
            }
        }

        [Fact]
        public async Task FileWithSchemaKeywordsStaysABareSchemaEvenWithDescription()
        {
            string projectRoot = CreateProjectRoot();
            try
            {
                WriteText(projectRoot, "Assets/Specs/root.json", PathItemRoot);
                WriteText(
                    projectRoot,
                    "Assets/Specs/item.json",
                    "{\"description\":\"d\",\"type\":\"object\",\"properties\":{\"x\":{\"$ref\":\"s.json\"}}}");
                WriteText(projectRoot, "Assets/Specs/s.json", "{\"type\":\"integer\"}");

                (_, _, NormalizedSpecBundle bundle) =
                    await LoadWriteAndReadAsync(projectRoot, "Assets/Specs/root.json");

                Assert.Contains(bundle.ReferenceEdges, edge => edge.SourcePointer == "/properties/x/$ref");
            }
            finally
            {
                Directory.Delete(projectRoot, true);
            }
        }

        private static async Task<(NormalizedSpecGraph Graph, string Text, NormalizedSpecBundle Bundle)>
            LoadWriteAndReadAsync(string projectRoot, string source)
        {
            var loader = new ExternalSpecGraphLoader(
                projectRoot,
                new RawJsonNormalizer(),
                new RawYamlNormalizer());
            NormalizedSpecGraph graph = await loader.LoadAsync(
                source,
                SpecId,
                CancellationToken.None);
            byte[] bytes = CanonicalSpecBundleWriter.Write(
                graph.SpecId,
                graph.RawSha256,
                graph.Documents,
                graph.Edges);
            string text = Encoding.UTF8.GetString(bytes);
            return (graph, text, NormalizedSpecBundleReader.Read(text));
        }

        private static string CreateProjectRoot()
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "OpenApiCodeGenGraphReaderParity",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(path, "Assets", "Specs"));
            return path;
        }

        private static void WriteText(string projectRoot, string relativePath, string content)
        {
            string path = Path.Combine(
                projectRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        private static int CountOccurrences(string value, string fragment)
        {
            int count = 0;
            int index = 0;
            while ((index = value.IndexOf(fragment, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += fragment.Length;
            }

            return count;
        }
    }
}
