using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    public sealed class Phase6CrossDocumentGeneratorTests
    {
        private const string ExternalDocumentId = "Assets/Specs/pet.json";
        private const string SecondExternalDocumentId = "Assets/Specs/b.json";
        private const string SecondSpecId = "fedcba9876543210fedcba9876543210";
        private const string RootReferencePointer =
            "/paths/~1pets/get/responses/200/content/application~1json/schema/$ref";

        [Fact]
        public void ExternalComponentSchemaReferenceGeneratesCompilableClient()
        {
            string root = CreateRootWithReference("pet.json#/components/schemas/Pet");
            string external = CreateOpenApiDocument(
                "Pet document",
                "\"Pet\":{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}}}");
            string bundle = TestBundleFactory.CreateV2(
                root,
                new[]
                {
                    new TestBundleFactory.V2DocumentSpec(
                        ExternalDocumentId,
                        "Assets/Specs/pet.json",
                        "json",
                        external),
                },
                new[]
                {
                    new TestBundleFactory.V2ReferenceSpec(
                        "root",
                        RootReferencePointer,
                        ExternalDocumentId,
                        "/components/schemas/Pet"),
                });

            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                root,
                bundle: bundle);

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assert.Contains("public sealed class Pet", execution.GeneratedSource);
            Assert.Contains("Task<Pet> getPet", execution.GeneratedSource);
        }

        [Fact]
        public void MissingTargetPointerInExistingExternalDocumentReportsOacg102()
        {
            string root = CreateRootWithReference("pet.json#/components/schemas/Missing");
            string external = CreateOpenApiDocument(
                "Pet document",
                "\"Pet\":{\"type\":\"object\"}");
            string bundle = TestBundleFactory.CreateV2(
                root,
                new[]
                {
                    new TestBundleFactory.V2DocumentSpec(
                        ExternalDocumentId,
                        "Assets/Specs/pet.json",
                        "json",
                        external),
                },
                new[]
                {
                    new TestBundleFactory.V2ReferenceSpec(
                        "root",
                        RootReferencePointer,
                        ExternalDocumentId,
                        "/components/schemas/Missing"),
                });

            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                root,
                bundle: bundle);
            Diagnostic diagnostic = Assert.Single(execution.RunResult.Diagnostics);

            Assert.Equal("OACG102", diagnostic.Id);
            Assert.Contains("target pointer '/components/schemas/Missing' could not be resolved", diagnostic.GetMessage());
            Assert.Empty(execution.RunResult.GeneratedTrees);
        }

        [Fact]
        public void ExternalBareSchemaRootUsesDocumentFileStemAsTypeName()
        {
            string root = CreateRootWithReference("pet.yaml");
            string external = "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}}}";
            string bundle = TestBundleFactory.CreateV2(
                root,
                new[]
                {
                    new TestBundleFactory.V2DocumentSpec(
                        "Assets/Specs/pet.yaml",
                        "Assets/Specs/pet.yaml",
                        "yaml",
                        external),
                },
                new[]
                {
                    new TestBundleFactory.V2ReferenceSpec(
                        "root",
                        RootReferencePointer,
                        "Assets/Specs/pet.yaml",
                        string.Empty),
                });

            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                root,
                bundle: bundle);

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assert.Contains("public sealed class Pet", execution.GeneratedSource);
            Assert.Contains("Task<Pet> getPet", execution.GeneratedSource);
        }

        [Theory]
        [InlineData("$id")]
        [InlineData("$anchor")]
        [InlineData("$dynamicAnchor")]
        [InlineData("$dynamicRef")]
        public void ExternalBareSchemaRootIdentityKeywordsReportOacg101(string keyword)
        {
            string root = CreateRootWithReference("pet.yaml");
            string external = "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}}," +
                              System.Text.Json.JsonSerializer.Serialize(keyword) + ":\"value\"}";
            string bundle = TestBundleFactory.CreateV2(
                root,
                new[]
                {
                    new TestBundleFactory.V2DocumentSpec(
                        "Assets/Specs/pet.yaml",
                        "Assets/Specs/pet.yaml",
                        "yaml",
                        external),
                },
                new[]
                {
                    new TestBundleFactory.V2ReferenceSpec(
                        "root",
                        RootReferencePointer,
                        "Assets/Specs/pet.yaml",
                        string.Empty),
                });

            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                root,
                bundle: bundle);

            Diagnostic diagnostic = Assert.Single(execution.RunResult.Diagnostics);
            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Empty(execution.RunResult.GeneratedTrees);
        }

        [Fact]
        public void RootSchemaKeepsNameBeforeExternalSchemaWithSameSuggestedName()
        {
            string root = CreateRootWithTwoReferences(
                "#/components/schemas/Pet",
                "pet.json#/components/schemas/Pet");
            string external = CreateOpenApiDocument(
                "External Pet document",
                "\"Pet\":{\"type\":\"object\",\"properties\":{\"externalName\":{\"type\":\"string\"}}}");
            string bundle = TestBundleFactory.CreateV2(
                root,
                new[]
                {
                    new TestBundleFactory.V2DocumentSpec(
                        ExternalDocumentId,
                        "Assets/Specs/pet.json",
                        "json",
                        external),
                },
                new[]
                {
                    new TestBundleFactory.V2ReferenceSpec(
                        "root",
                        "/paths/~1external/get/responses/200/content/application~1json/schema/$ref",
                        ExternalDocumentId,
                        "/components/schemas/Pet"),
                    new TestBundleFactory.V2ReferenceSpec(
                        "root",
                        "/paths/~1root/get/responses/200/content/application~1json/schema/$ref",
                        "root",
                        "/components/schemas/Pet"),
                });

            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                root,
                bundle: bundle);

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assert.Contains("public sealed class Pet", execution.GeneratedSource);
            Assert.Contains("public sealed class Pet2", execution.GeneratedSource);
            Assert.Contains("Task<Pet> getRoot", execution.GeneratedSource);
            Assert.Contains("Task<Pet2> getExternal", execution.GeneratedSource);
        }

        [Fact]
        public void CrossDocumentSchemaCycleReportsPrimaryAndAdditionalLocations()
        {
            const string CycleDocumentA = "Assets/Specs/a.json";
            const string CycleDocumentB = "Assets/Specs/b.json";
            string root = CreateRootWithReference("a.json#/components/schemas/A");
            string documentA = CreateOpenApiDocument(
                "A document",
                "\"A\":{\"$ref\":\"b.json#/components/schemas/B\"}");
            string documentB = CreateOpenApiDocument(
                "B document",
                "\"B\":{\"$ref\":\"a.json#/components/schemas/A\"}");
            string bundle = TestBundleFactory.CreateV2(
                root,
                new[]
                {
                    new TestBundleFactory.V2DocumentSpec(
                        CycleDocumentA,
                        "Assets/Specs/a.json",
                        "json",
                        documentA),
                    new TestBundleFactory.V2DocumentSpec(
                        SecondExternalDocumentId,
                        "Assets/Specs/b.json",
                        "json",
                        documentB),
                },
                new[]
                {
                    new TestBundleFactory.V2ReferenceSpec(
                        CycleDocumentA,
                        "/components/schemas/A/$ref",
                        CycleDocumentB,
                        "/components/schemas/B"),
                    new TestBundleFactory.V2ReferenceSpec(
                        CycleDocumentB,
                        "/components/schemas/B/$ref",
                        CycleDocumentA,
                        "/components/schemas/A"),
                    new TestBundleFactory.V2ReferenceSpec(
                        "root",
                        RootReferencePointer,
                        CycleDocumentA,
                        "/components/schemas/A"),
                });

            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                root,
                bundle: bundle);
            Diagnostic diagnostic = Assert.Single(execution.RunResult.Diagnostics);

            Assert.True(diagnostic.Id == "OACG103", diagnostic.GetMessage());
            Assert.Contains("cyclic", diagnostic.GetMessage(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Assets/Specs/b.json", diagnostic.Location.GetLineSpan().Path);
            Assert.NotEmpty(diagnostic.AdditionalLocations);
            Assert.Contains(
                diagnostic.AdditionalLocations,
                location => location.GetLineSpan().Path == "Assets/Specs/a.json");
            Assert.Empty(execution.RunResult.GeneratedTrees);
        }

        [Fact]
        public void ChangingExternalDocumentInvalidatesOnlyTheV2BundleAndGeneratedClient()
        {
            string root = CreateRootWithReference("pet.json#/components/schemas/Pet");
            string originalExternal = CreateOpenApiDocument(
                "External Pet document",
                "\"Pet\":{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}}}");
            string changedExternal = CreateOpenApiDocument(
                "External Pet document",
                "\"Pet\":{\"type\":\"object\",\"properties\":{\"nickname\":{\"type\":\"string\"}}}");
            InMemoryAdditionalText original = Phase4GeneratorTestHarness.CreateBundleText(
                TestBundleFactory.SpecId,
                CreateExternalBundle(root, originalExternal));
            InMemoryAdditionalText changed = Phase4GeneratorTestHarness.CreateBundleText(
                TestBundleFactory.SpecId,
                CreateExternalBundle(root, changedExternal));
            Microsoft.CodeAnalysis.CSharp.CSharpCompilation compilation =
                Phase4GeneratorTestHarness.CreateCompilation();
            GeneratorDriver driver = Phase4GeneratorTestHarness.CreateDriver(
                compilation,
                ImmutableArray.Create<AdditionalText>(original));

            driver = driver.RunGenerators(compilation);
            string originalSource = GetGeneratedSource(driver.GetRunResult());
            driver = driver.RunGenerators(compilation);
            GeneratorDriverRunResult unchanged = driver.GetRunResult();
            driver = driver.ReplaceAdditionalText(original, changed).RunGenerators(compilation);
            GeneratorDriverRunResult modified = driver.GetRunResult();

            Assert.Contains(
                IncrementalStepRunReason.Cached,
                Phase4GeneratorTestHarness.GetReasons(unchanged, "OpenApiParseNormalizedBundle"));
            Assert.Contains(
                IncrementalStepRunReason.Modified,
                Phase4GeneratorTestHarness.GetReasons(modified, "OpenApiParseNormalizedBundle"));
            Assert.NotEqual(originalSource, GetGeneratedSource(modified));
            Assert.Contains("Nickname", GetGeneratedSource(modified));
            Assert.DoesNotContain("Name { get;", GetGeneratedSource(modified));
        }

        [Fact]
        public void ChangingOneExternalDocumentKeepsTheOtherClientCachedAndByteIdentical()
        {
            string firstRoot = CreateRootWithReference("pet.json#/components/schemas/Pet");
            string secondRoot = CreateRootWithReference("order.json#/components/schemas/Order")
                .Replace("getPet", "getOrder", StringComparison.Ordinal);
            string firstExternal = CreateOpenApiDocument(
                "First external",
                "\"Pet\":{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}}}");
            string secondExternal = CreateOpenApiDocument(
                "Second external",
                "\"Order\":{\"type\":\"object\",\"properties\":{\"number\":{\"type\":\"string\"}}}");
            string changedSecondExternal = CreateOpenApiDocument(
                "Second external",
                "\"Order\":{\"type\":\"object\",\"properties\":{\"code\":{\"type\":\"string\"}}}");

            InMemoryAdditionalText firstBundle = Phase4GeneratorTestHarness.CreateBundleText(
                TestBundleFactory.SpecId,
                CreateExternalBundle(firstRoot, firstExternal));
            InMemoryAdditionalText secondBundle = Phase4GeneratorTestHarness.CreateBundleText(
                SecondSpecId,
                CreateExternalBundle(
                    secondRoot,
                    secondExternal,
                    SecondSpecId,
                    "Assets/Specs/order.json",
                    "Assets/Specs/order.json",
                    "/components/schemas/Order"));
            InMemoryAdditionalText changedSecondBundle = Phase4GeneratorTestHarness.CreateBundleText(
                SecondSpecId,
                CreateExternalBundle(
                    secondRoot,
                    changedSecondExternal,
                    SecondSpecId,
                    "Assets/Specs/order.json",
                    "Assets/Specs/order.json",
                    "/components/schemas/Order"));

            CSharpCompilation compilation = Phase4GeneratorTestHarness.CreateCompilation(
                apiName: "FirstApi",
                generatedNamespace: "Generated.Phase6.First");
            compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
                "namespace Generated.Phase6.Second { " +
                "[global::Rhycol.OpenApiCodeGen.SourceGenerator.OpenApiClientDefinitionAttribute(\"" +
                SecondSpecId +
                "\", \"SecondApi\", \"Generated.Phase6.Second\", " +
                "global::Rhycol.OpenApiCodeGen.SourceGenerator.OpenApiDocumentFormat.Json)] " +
                "public partial class SecondApi { } }",
                (CSharpParseOptions)compilation.SyntaxTrees.First().Options));

            GeneratorDriver driver = Phase4GeneratorTestHarness.CreateDriver(
                compilation,
                ImmutableArray.Create<AdditionalText>(firstBundle, secondBundle));
            driver = driver.RunGenerators(compilation);
            GeneratorDriverRunResult firstResult = driver.GetRunResult();
            string firstClientBefore = GetGeneratedSourceForSpec(firstResult, TestBundleFactory.SpecId);
            string secondClientBefore = GetGeneratedSourceForSpec(firstResult, SecondSpecId);

            driver = driver.RunGenerators(compilation);
            driver = driver.ReplaceAdditionalText(secondBundle, changedSecondBundle)
                .RunGenerators(compilation);
            GeneratorDriverRunResult changedResult = driver.GetRunResult();
            IReadOnlyList<IncrementalStepRunReason> parseReasons = Phase4GeneratorTestHarness.GetReasons(
                changedResult,
                "OpenApiParseNormalizedBundle");
            IReadOnlyList<IncrementalStepRunReason> matchReasons = Phase4GeneratorTestHarness.GetReasons(
                changedResult,
                "OpenApiMatchDefinitionToBundle");

            Assert.Contains(IncrementalStepRunReason.Cached, parseReasons);
            Assert.Contains(IncrementalStepRunReason.Modified, parseReasons);
            Assert.Contains(IncrementalStepRunReason.Unchanged, matchReasons);
            Assert.Contains(IncrementalStepRunReason.Modified, matchReasons);
            Assert.Equal(
                firstClientBefore,
                GetGeneratedSourceForSpec(changedResult, TestBundleFactory.SpecId));
            Assert.NotEqual(
                secondClientBefore,
                GetGeneratedSourceForSpec(changedResult, SecondSpecId));
            Assert.Contains("Code", GetGeneratedSourceForSpec(changedResult, SecondSpecId));
            Assert.DoesNotContain("Number { get;", GetGeneratedSourceForSpec(changedResult, SecondSpecId));
        }

        private static string CreateExternalBundle(
            string root,
            string external,
            string specId = TestBundleFactory.SpecId,
            string externalDocumentId = ExternalDocumentId,
            string externalSourcePath = "Assets/Specs/pet.json",
            string targetPointer = "/components/schemas/Pet")
        {
            return TestBundleFactory.CreateV2(
                root,
                new[]
                {
                    new TestBundleFactory.V2DocumentSpec(
                        externalDocumentId,
                        externalSourcePath,
                        "json",
                        external),
                },
                new[]
                {
                    new TestBundleFactory.V2ReferenceSpec(
                        "root",
                        RootReferencePointer,
                        externalDocumentId,
                        targetPointer),
                },
                specId: specId);
        }

        private static string GetGeneratedSource(GeneratorDriverRunResult result)
        {
            return string.Join(
                "\n",
                result.Results.Single().GeneratedSources
                    .OrderBy(source => source.HintName, StringComparer.Ordinal)
                .Select(source => source.SourceText.ToString()));
        }

        private static string GetGeneratedSourceForSpec(
            GeneratorDriverRunResult result,
            string specId)
        {
            return string.Join(
                "\n",
                result.Results.Single().GeneratedSources
                    .Where(source => source.HintName.Contains(specId, StringComparison.Ordinal))
                    .OrderBy(source => source.HintName, StringComparer.Ordinal)
                    .Select(source => source.SourceText.ToString()));
        }

        private static string CreateRootWithReference(string reference)
        {
            return "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Sample\",\"version\":\"1\"}," +
                   "\"paths\":{\"/pets\":{\"get\":{\"operationId\":\"getPet\",\"responses\":{" +
                   "\"200\":{\"description\":\"OK\",\"content\":{\"application/json\":{" +
                   "\"schema\":{\"$ref\":" + System.Text.Json.JsonSerializer.Serialize(reference) +
                   "}}}}}}}}}";
        }

        private static string CreateRootWithTwoReferences(string rootReference, string externalReference)
        {
            return "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Sample\",\"version\":\"1\"}," +
                   "\"paths\":{" +
                   "\"/external\":" + CreateOperation("getExternal", externalReference) + "}," +
                   "\"/root\":" + CreateOperation("getRoot", rootReference) +
                   "}},\"components\":{\"schemas\":{\"Pet\":{\"type\":\"object\",\"properties\":{" +
                   "\"rootName\":{\"type\":\"string\"}}}}}}";
        }

        private static string CreateOperation(string operationId, string reference)
        {
            return "{\"get\":{\"operationId\":" +
                   System.Text.Json.JsonSerializer.Serialize(operationId) +
                   ",\"responses\":{\"200\":{\"description\":\"OK\",\"content\":{" +
                   "\"application/json\":{\"schema\":{\"$ref\":" +
                   System.Text.Json.JsonSerializer.Serialize(reference) + "}}}}}}";
        }

        private static string CreateOpenApiDocument(string title, string schemaEntry)
        {
            return "{\"openapi\":\"3.1.0\",\"info\":{\"title\":" +
                   System.Text.Json.JsonSerializer.Serialize(title) +
                   ",\"version\":\"1\"},\"paths\":{},\"components\":{\"schemas\":{" +
                   schemaEntry + "}}}";
        }
    }
}
