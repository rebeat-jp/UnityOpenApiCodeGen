using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;

using Rhycol.OpenApiCodeGen.SourceGenerator.Editor;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    /// <summary>
    /// P7 (G31): webhooks, callbacks, links and the unused components sections are accepted with an
    /// OACG108 warning at the element's own location. Ignored subtrees are never parsed, so a broken
    /// $ref inside them cannot raise an error. Documentation-only elements stay silent.
    /// </summary>
    public sealed class Phase7IgnoredElementTests
    {
        private const string SpecId = "0123456789abcdef0123456789abcdef";

        private static string Spec(string version, string extraRoot, string operationExtra, string responseExtra, string components)
        {
            return "{\"openapi\":\"" + version + "\",\"info\":{\"title\":\"P7\",\"version\":\"1\"}," +
                   "\"servers\":[{\"url\":\"https://api.example.com\"}]," + extraRoot +
                   "\"paths\":{\"/pets\":{\"post\":{\"operationId\":\"createPet\"," + operationExtra +
                   "\"requestBody\":{\"required\":true,\"content\":{\"application/json\":{\"schema\":{\"$ref\":\"#/components/schemas/Pet\"}}}}," +
                   "\"responses\":{\"201\":{\"description\":\"created\"," + responseExtra +
                   "\"content\":{\"application/json\":{\"schema\":{\"$ref\":\"#/components/schemas/Pet\"}}}}}}}}," +
                   "\"components\":{" + components +
                   "\"schemas\":{\"Pet\":{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}}}," +
                   "\"Notification\":{\"type\":\"object\",\"properties\":{\"text\":{\"type\":\"string\"}}}}}}";
        }

        private const string CallbackBlock =
            "\"callbacks\":{\"onEvent\":{\"{$request.body#/url}\":{\"post\":{\"operationId\":\"callbackReceiveNotification\"," +
            "\"requestBody\":{\"content\":{\"application/json\":{\"schema\":{\"$ref\":\"#/components/schemas/Notification\"}}}}," +
            "\"responses\":{\"200\":{\"description\":\"ok\"}}}}}},";

        private const string WebhookBlock =
            "\"webhooks\":{\"newPet\":{\"post\":{\"operationId\":\"webhookNewPet\"," +
            "\"requestBody\":{\"content\":{\"application/json\":{\"schema\":{\"$ref\":\"#/components/schemas/Notification\"}}}}," +
            "\"responses\":{\"200\":{\"description\":\"ok\"}}}}},";

        private const string LinksBlock =
            "\"links\":{\"GetPet\":{\"operationId\":\"getPet\",\"parameters\":{\"id\":\"$response.body#/id\"}}},";

        private static Phase4GeneratorExecution Generate(string spec)
        {
            return Phase4GeneratorTestHarness.GenerateAndCompile(spec);
        }

        private static void AssertOnlyIgnoredWarnings(Phase4GeneratorExecution execution, params string[] logicalPaths)
        {
            Assert.All(execution.RunResult.Diagnostics, static d =>
            {
                Assert.Equal("OACG108", d.Id);
                Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
            });
            Assert.Equal(logicalPaths.Length, execution.RunResult.Diagnostics.Length);
            foreach (string path in logicalPaths)
            {
                Assert.Contains(
                    execution.RunResult.Diagnostics,
                    d => d.GetMessage().Contains("'" + path + "'", StringComparison.Ordinal));
            }

            Assert.Empty(execution.CompilationErrors);
        }

        private static void AssertDtosButNoCallbackMembers(Phase4GeneratorExecution execution)
        {
            Assert.Contains("class Pet", execution.GeneratedSource);
            Assert.Contains("class Notification", execution.GeneratedSource);
            Assert.Contains("createPet", execution.GeneratedSource, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("CallbackReceiveNotification", execution.GeneratedSource, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("WebhookNewPet", execution.GeneratedSource, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("getPet", execution.GeneratedSource, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("onEvent", execution.GeneratedSource, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("newPet", execution.GeneratedSource, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("3.0.3")]
        [InlineData("3.1.0")]
        public void RootWebhooksAreIgnoredWithAWarningAtTheWebhooksNode(string version)
        {
            Phase4GeneratorExecution execution = Generate(Spec(version, WebhookBlock, string.Empty, string.Empty, string.Empty));

            AssertOnlyIgnoredWarnings(execution, "/webhooks");
            AssertDtosButNoCallbackMembers(execution);
        }

        [Theory]
        [InlineData("3.0.3")]
        [InlineData("3.1.0")]
        public void OperationCallbacksAreIgnoredWithAWarningAtTheCallbacksNode(string version)
        {
            Phase4GeneratorExecution execution = Generate(Spec(version, string.Empty, CallbackBlock, string.Empty, string.Empty));

            AssertOnlyIgnoredWarnings(execution, "/paths/~1pets/post/callbacks");
            AssertDtosButNoCallbackMembers(execution);
        }

        [Theory]
        [InlineData("3.0.3")]
        [InlineData("3.1.0")]
        public void ResponseLinksAreIgnoredWithAWarningAtTheLinksNode(string version)
        {
            Phase4GeneratorExecution execution = Generate(Spec(version, string.Empty, string.Empty, LinksBlock, string.Empty));

            AssertOnlyIgnoredWarnings(execution, "/paths/~1pets/post/responses/201/links");
            AssertDtosButNoCallbackMembers(execution);
        }

        [Fact]
        public void ComponentsLinksAndCallbacksWarnAndComponentsExamplesAreSilent()
        {
            string components =
                "\"examples\":{\"PetExample\":{\"value\":{\"name\":\"rex\"}},\"Ref\":{\"$ref\":\"#/components/examples/PetExample\"}}," +
                "\"links\":{\"GetPet\":{\"operationId\":\"getPet\"}}," +
                "\"callbacks\":{\"Notify\":{\"{$request.body#/url}\":{\"post\":{\"responses\":{\"200\":{\"description\":\"ok\"}}}}}},";
            Phase4GeneratorExecution execution = Generate(Spec("3.0.3", string.Empty, string.Empty, string.Empty, components));

            AssertOnlyIgnoredWarnings(execution, "/components/links", "/components/callbacks");
            AssertDtosButNoCallbackMembers(execution);
        }

        [Fact]
        public void EmptyIgnoredMapsAreSilent()
        {
            string components = "\"examples\":{},\"links\":{},\"callbacks\":{},";
            Phase4GeneratorExecution execution = Generate(
                Spec("3.1.0", "\"webhooks\":{},", "\"callbacks\":{},", "\"links\":{},", components));

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
        }

        [Fact]
        public void AllIgnoredElementsTogetherProduceOnlyWarningsAndKeepAllDtos()
        {
            string components =
                "\"links\":{\"L\":{\"operationId\":\"x\"}}," +
                "\"callbacks\":{\"C\":{\"{$request.body#/u}\":{\"$ref\":\"#/components/pathItems/Gone\"}}},";
            Phase4GeneratorExecution execution = Generate(
                Spec("3.1.0", WebhookBlock, CallbackBlock, LinksBlock, components));

            AssertOnlyIgnoredWarnings(
                execution,
                "/webhooks",
                "/paths/~1pets/post/callbacks",
                "/paths/~1pets/post/responses/201/links",
                "/components/links",
                "/components/callbacks");
            AssertDtosButNoCallbackMembers(execution);
        }

        [Fact]
        public void BrokenRefsInsideIgnoredSubtreesAreNeverResolved()
        {
            // Unresolved internal, missing external, and a $ref to an unsupported construct
            // (a Path Item in 3.0, a dynamic reference) all live inside ignored content.
            const string Callback =
                "\"callbacks\":{" +
                "\"a\":{\"$ref\":\"#/components/callbacks/DoesNotExist\"}," +
                "\"b\":{\"{$request.body#/url}\":{\"post\":{" +
                "\"requestBody\":{\"$ref\":\"#/components/requestBodies/Nope\"}," +
                "\"parameters\":[{\"$ref\":\"missing-parameter.json\"}]," +
                "\"responses\":{\"200\":{\"description\":\"ok\",\"content\":{\"application/json\":{\"schema\":{\"$ref\":\"other.json#/does/not/exist\"}}}}}}}}," +
                "\"c\":{\"x\":{\"post\":{\"requestBody\":{\"content\":{\"application/json\":{\"schema\":{\"$dynamicRef\":\"#meta\",\"$ref\":\"#/nowhere\"}}}}}}}},";
            const string Webhook =
                "\"webhooks\":{\"w\":{\"$ref\":\"missing-webhook.json\"},\"v\":{\"post\":{\"responses\":{\"200\":{\"$ref\":\"#/components/responses/Nope\"}}}}},";
            const string Links =
                "\"links\":{\"l\":{\"$ref\":\"#/components/links/Nope\"},\"m\":{\"operationRef\":\"other.json#/paths\"}},";
            const string Components =
                "\"links\":{\"k\":{\"$ref\":\"missing-link.json\"}}," +
                "\"callbacks\":{\"k\":{\"$ref\":\"missing-callback.json\"}}," +
                "\"examples\":{\"e\":{\"$ref\":\"missing-example.json\"}},";
            Phase4GeneratorExecution execution = Generate(Spec("3.1.0", Webhook, Callback, Links, Components));

            AssertOnlyIgnoredWarnings(
                execution,
                "/webhooks",
                "/paths/~1pets/post/callbacks",
                "/paths/~1pets/post/responses/201/links",
                "/components/links",
                "/components/callbacks");
            AssertDtosButNoCallbackMembers(execution);
        }

        [Fact]
        public void IgnoredElementsMustStillBeObjects()
        {
            Phase4GeneratorExecution execution = Generate(
                Spec("3.1.0", "\"webhooks\":[],", string.Empty, string.Empty, string.Empty));

            Diagnostic diagnostic = Assert.Single(execution.RunResult.Diagnostics);
            Assert.NotEqual("OACG108", diagnostic.Id);
            Assert.Contains("/webhooks", diagnostic.GetMessage());
        }

        [Fact]
        public void UnknownComponentsSectionsAndOtherSectionsStayRejected()
        {
            Phase4GeneratorExecution execution = Generate(
                Spec("3.0.3", string.Empty, string.Empty, string.Empty, "\"foo\":{},"));

            Diagnostic diagnostic = Assert.Single(execution.RunResult.Diagnostics);
            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("logical path '/components/foo'", diagnostic.GetMessage());

            // pathItems is an OpenAPI 3.1 section.
            execution = Generate(Spec("3.0.3", string.Empty, string.Empty, string.Empty, "\"pathItems\":{},"));
            diagnostic = Assert.Single(execution.RunResult.Diagnostics);
            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("logical path '/components/pathItems'", diagnostic.GetMessage());
        }

        [Fact]
        public void DocumentationOnlyElementsAreAcceptedSilently()
        {
            const string Document = "{\"openapi\":\"3.1.0\",\"jsonSchemaDialect\":\"https://spec.openapis.org/oas/3.1/dialect/base\"," +
                "\"info\":{\"title\":\"Docs\",\"version\":\"1\"},\"x-root\":{\"anything\":true}," +
                "\"tags\":[{\"name\":\"pets\",\"description\":\"d\",\"externalDocs\":{\"url\":\"https://example.com\"}}]," +
                "\"externalDocs\":{\"url\":\"https://example.com/docs\"}," +
                "\"paths\":{\"/pets/{id}\":{\"get\":{\"operationId\":\"getPet\",\"tags\":[\"pets\"],\"x-internal\":1," +
                "\"externalDocs\":{\"url\":\"https://example.com\"}," +
                "\"parameters\":[{\"name\":\"id\",\"in\":\"path\",\"required\":true,\"example\":\"a\"," +
                "\"examples\":{\"one\":{\"value\":\"a\"},\"two\":{\"$ref\":\"#/components/examples/Ex\"}}," +
                "\"schema\":{\"type\":\"string\",\"xml\":{\"name\":\"id\",\"attribute\":true},\"example\":\"a\",\"externalDocs\":{\"url\":\"https://example.com\"}}}]," +
                "\"responses\":{\"200\":{\"description\":\"ok\",\"x-meta\":1," +
                "\"headers\":{\"X-Rate\":{\"description\":\"r\",\"example\":\"1\",\"examples\":{\"a\":{\"value\":\"1\"}},\"schema\":{\"type\":\"string\"}}}," +
                "\"content\":{\"application/json\":{\"example\":{\"name\":\"rex\"},\"examples\":{\"a\":{\"$ref\":\"#/components/examples/Ex\"}}," +
                "\"schema\":{\"$ref\":\"#/components/schemas/Pet\"}}}}}}}}," +
                "\"components\":{\"x-ext\":{}," +
                "\"examples\":{\"Ex\":{\"summary\":\"s\",\"value\":{\"name\":\"rex\"}},\"Missing\":{\"$ref\":\"missing-example.json\"}}," +
                "\"schemas\":{\"Pet\":{\"type\":\"object\",\"x-extra\":1,\"xml\":{\"name\":\"pet\"},\"properties\":{\"name\":{\"type\":\"string\",\"xml\":{\"attribute\":true}}}}}}}";

            Phase4GeneratorExecution execution = Generate(Document);

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assert.Contains("class Pet", execution.GeneratedSource);
        }

        // ---- Editor bundling -----------------------------------------------------------------------

        [Fact]
        public async Task EditorBundlingIgnoresRefsInsideIgnoredSubtrees()
        {
            string projectRoot = CreateProjectRoot();
            try
            {
                WriteText(
                    projectRoot,
                    "Assets/Specs/root.json",
                    "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"R\",\"version\":\"1\"}," +
                    "\"webhooks\":{\"w\":{\"$ref\":\"missing-webhook.json\"},\"v\":{\"post\":{\"requestBody\":{\"content\":{\"application/json\":{\"schema\":{\"$ref\":\"missing-hook-schema.json\"}}}}}}}," +
                    "\"paths\":{\"/pets\":{\"post\":{\"operationId\":\"createPet\"," +
                    "\"callbacks\":{\"c\":{\"$ref\":\"missing-callback.json\"},\"d\":{\"{$request.body#/u}\":{\"post\":{\"requestBody\":{\"content\":{\"application/json\":{\"schema\":{\"$ref\":\"missing-cb-schema.json\"}}}}}}}}," +
                    "\"requestBody\":{\"content\":{\"application/json\":{\"schema\":{\"$ref\":\"pet.json\"},\"examples\":{\"e\":{\"$ref\":\"missing-media-example.json\"}}}}}," +
                    "\"responses\":{\"201\":{\"description\":\"c\",\"links\":{\"l\":{\"$ref\":\"missing-link.json\"}}}}}}}," +
                    "\"components\":{\"links\":{\"k\":{\"$ref\":\"missing-link.json\"}}," +
                    "\"callbacks\":{\"k\":{\"$ref\":\"missing-callback.json\"}}," +
                    "\"examples\":{\"e\":{\"$ref\":\"missing-example.json\"}}," +
                    "\"schemas\":{\"Pet\":{\"$ref\":\"pet.json\"}}}}");
                WriteText(projectRoot, "Assets/Specs/pet.json", "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}}}");

                var loader = new ExternalSpecGraphLoader(
                    projectRoot,
                    new RawJsonNormalizer(),
                    new RawYamlNormalizer());
                NormalizedSpecGraph graph = await loader.LoadAsync(
                    "Assets/Specs/root.json",
                    SpecId,
                    CancellationToken.None);
                byte[] bytes = CanonicalSpecBundleWriter.Write(
                    graph.SpecId,
                    graph.RawSha256,
                    graph.Documents,
                    graph.Edges);
                NormalizedSpecBundle bundle = NormalizedSpecBundleReader.Read(Encoding.UTF8.GetString(bytes));

                // Only the semantic references (the request body and Pet) are bundled.
                Assert.Equal(2, graph.Documents.Count);
                Assert.Equal(2, bundle.ReferenceEdges.Count);
                Assert.All(bundle.ReferenceEdges, static edge =>
                    Assert.DoesNotContain("callbacks", edge.SourcePointer, StringComparison.Ordinal));
            }
            finally
            {
                Directory.Delete(projectRoot, true);
            }
        }

        [Fact]
        public async Task EditorBundlingStillCollectsSemanticRefsNextToIgnoredSubtrees()
        {
            string projectRoot = CreateProjectRoot();
            try
            {
                WriteText(
                    projectRoot,
                    "Assets/Specs/root.json",
                    "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"R\",\"version\":\"1\"}," +
                    "\"webhooks\":{\"w\":{\"$ref\":\"missing-webhook.json\"}}," +
                    "\"paths\":{\"/pets\":{\"$ref\":\"item.json\"}," +
                    "\"/other\":{\"post\":{\"callbacks\":{\"c\":{\"$ref\":\"missing-callback.json\"}}," +
                    "\"responses\":{\"201\":{\"description\":\"c\",\"links\":{\"l\":{\"$ref\":\"missing-link.json\"}}}}}}}," +
                    "\"components\":{\"links\":{\"k\":{\"$ref\":\"missing-link.json\"}}," +
                    "\"schemas\":{\"S\":{\"$ref\":\"schema.json\"}}," +
                    "\"securitySchemes\":{\"Auth\":{\"$ref\":\"scheme.json\"}}," +
                    "\"parameters\":{\"P\":{\"$ref\":\"parameter.json\"}}," +
                    "\"headers\":{\"H\":{\"$ref\":\"header.json\"}}," +
                    "\"requestBodies\":{\"R\":{\"$ref\":\"body.json\"}}," +
                    "\"responses\":{\"Resp\":{\"$ref\":\"response.json\"}}}}");
                WriteText(projectRoot, "Assets/Specs/schema.json", "{\"type\":\"string\"}");
                WriteText(projectRoot, "Assets/Specs/scheme.json", "{\"type\":\"http\",\"scheme\":\"bearer\"}");
                WriteText(projectRoot, "Assets/Specs/parameter.json", "{\"name\":\"q\",\"in\":\"query\",\"schema\":{\"type\":\"string\"}}");
                WriteText(projectRoot, "Assets/Specs/header.json", "{\"schema\":{\"type\":\"string\"}}");
                WriteText(projectRoot, "Assets/Specs/body.json", "{\"content\":{}}");
                WriteText(projectRoot, "Assets/Specs/response.json", "{\"description\":\"d\"}");
                WriteText(projectRoot, "Assets/Specs/item.json", "{\"get\":{\"responses\":{\"204\":{\"description\":\"n\"}}}}");

                var loader = new ExternalSpecGraphLoader(
                    projectRoot,
                    new RawJsonNormalizer(),
                    new RawYamlNormalizer());
                NormalizedSpecGraph graph = await loader.LoadAsync(
                    "Assets/Specs/root.json",
                    SpecId,
                    CancellationToken.None);
                byte[] bytes = CanonicalSpecBundleWriter.Write(
                    graph.SpecId,
                    graph.RawSha256,
                    graph.Documents,
                    graph.Edges);
                NormalizedSpecBundle bundle = NormalizedSpecBundleReader.Read(Encoding.UTF8.GetString(bytes));

                Assert.Equal(8, graph.Documents.Count);
                string[] expected =
                {
                    "/paths/~1pets/$ref",
                    "/components/schemas/S/$ref",
                    "/components/securitySchemes/Auth/$ref",
                    "/components/parameters/P/$ref",
                    "/components/headers/H/$ref",
                    "/components/requestBodies/R/$ref",
                    "/components/responses/Resp/$ref"
                };
                Assert.Equal(expected.Length, bundle.ReferenceEdges.Count);
                foreach (string pointer in expected)
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
        public void IgnoredWarningsInASharedPathItemAreReportedOncePerNode()
        {
            const string Document =
                "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"S\",\"version\":\"1\"}," +
                "\"paths\":{\"/a\":{\"$ref\":\"#/components/pathItems/Shared\"},\"/b\":{\"$ref\":\"#/components/pathItems/Shared\"}}," +
                "\"components\":{\"pathItems\":{\"Shared\":{\"post\":{" +
                CallbackBlock +
                "\"responses\":{\"201\":{\"description\":\"c\"," + LinksBlock + "\"content\":{}}}}}}}}";

            Phase4GeneratorExecution execution = Generate(Document);

            AssertOnlyIgnoredWarnings(
                execution,
                "/components/pathItems/Shared/post/callbacks",
                "/components/pathItems/Shared/post/responses/201/links");
        }

        private static string CreateProjectRoot()
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "OpenApiCodeGenPhase7",
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
    }
}
