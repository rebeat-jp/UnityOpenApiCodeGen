using System;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    public sealed class Phase4DialectEnumRegressionTests
    {
        private const string BaseDialect = "https://spec.openapis.org/oas/3.1/dialect/base";

        [Theory]
        [InlineData("")]
        [InlineData("\"jsonSchemaDialect\": \"https://spec.openapis.org/oas/3.1/dialect/base\",")]
        public void DefaultAndExplicitOpenApi31BaseDialectCompile(string dialectField)
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                CreateDocument(dialectField, "\"type\": \"string\""));

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
        }

        [Fact]
        public void UnsupportedRootDialectReportsItsLocation()
        {
            Diagnostic diagnostic = GetOnlyDiagnostic(CreateDocument(
                "\"jsonSchemaDialect\": \"https://json-schema.org/draft/2020-12/schema\",",
                "\"type\": \"string\""));

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("logical path '/jsonSchemaDialect'", diagnostic.GetMessage());
            Assert.Equal(TestBundleFactory.SourcePath, diagnostic.Location.GetLineSpan().Path);
        }

        [Theory]
        [InlineData("\"custom/dialect\"")]
        [InlineData("\"https://[invalid\"")]
        [InlineData("42")]
        public void InvalidRootDialectReportsInvalidDocument(string value)
        {
            Diagnostic diagnostic = GetOnlyDiagnostic(CreateDocument(
                "\"jsonSchemaDialect\": " + value + ",",
                "\"type\": \"string\""));

            Assert.Equal("OACG100", diagnostic.Id);
            Assert.Contains("logical path '/jsonSchemaDialect'", diagnostic.GetMessage());
        }

        [Fact]
        public void ExplicitOpenApi31BaseSchemaDialectCompiles()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                CreateDocument(string.Empty,
                    "\"type\": \"string\", \"$schema\": \"" + BaseDialect + "\""));

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
        }

        [Theory]
        [InlineData("3.1.0", "\"https://json-schema.org/draft/2020-12/schema\"", "OACG101")]
        [InlineData("3.1.0", "\"relative/dialect\"", "OACG100")]
        [InlineData("3.0.3", "\"https://spec.openapis.org/oas/3.1/dialect/base\"", "OACG101")]
        public void UnsupportedOrInvalidSchemaDialectReportsItsLocation(
            string version,
            string dialectValue,
            string expectedId)
        {
            Diagnostic diagnostic = GetOnlyDiagnostic(CreateDocument(
                string.Empty,
                "\"type\": \"string\", \"$schema\": " + dialectValue,
                version));

            Assert.Equal(expectedId, diagnostic.Id);
            Assert.Contains("logical path '/components/schemas/Value/$schema'", diagnostic.GetMessage());
            Assert.Equal(TestBundleFactory.SourcePath, diagnostic.Location.GetLineSpan().Path);
        }

        [Fact]
        public void ExternalBareSchemaWithBaseDialectCompiles()
        {
            const string externalPath = "Assets/Specs/value.yaml";
            string root = CreateExternalReferenceDocument();
            string external = "{\"type\":\"string\",\"$schema\":\"" + BaseDialect + "\"}";
            string bundle = TestBundleFactory.CreateV2(
                root,
                new[]
                {
                    new TestBundleFactory.V2DocumentSpec(externalPath, externalPath, "yaml", external),
                },
                new[]
                {
                    new TestBundleFactory.V2ReferenceSpec(
                        "root",
                        "/paths/~1value/get/responses/200/content/application~1json/schema/$ref",
                        externalPath,
                        string.Empty),
                });

            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(root, bundle: bundle);

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
        }

        [Fact]
        public void UnsupportedExternalBareSchemaDialectReportsExternalLocation()
        {
            const string externalPath = "Assets/Specs/value.yaml";
            string root = CreateExternalReferenceDocument();
            const string external = "{\"type\":\"string\",\"$schema\":\"https://json-schema.org/draft/2020-12/schema\"}";
            string bundle = TestBundleFactory.CreateV2(
                root,
                new[]
                {
                    new TestBundleFactory.V2DocumentSpec(externalPath, externalPath, "yaml", external),
                },
                new[]
                {
                    new TestBundleFactory.V2ReferenceSpec(
                        "root",
                        "/paths/~1value/get/responses/200/content/application~1json/schema/$ref",
                        externalPath,
                        string.Empty),
                });

            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(root, bundle: bundle).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("logical path '/$schema'", diagnostic.GetMessage());
            Assert.Equal(externalPath, diagnostic.Location.GetLineSpan().Path);
            Assert.Contains(diagnostic.AdditionalLocations,
                location => location.GetLineSpan().Path == TestBundleFactory.SourcePath);
        }

        [Fact]
        public void StringOnlyEnumUnionIsNotNullableForRequiredParameter()
        {
            const string schema = "{\"type\":[\"string\",\"null\"],\"enum\":[\"ready\",\"done\"]}";
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                CreateRequiredParameterDocument("3.1.0", schema));

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            MethodDeclarationSyntax method = CSharpSyntaxTree.ParseText(execution.GeneratedSource)
                .GetCompilationUnitRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(value => value.Identifier.ValueText == "getValue");
            ParameterSyntax parameter = method.ParameterList.Parameters
                .Single(value => value.Identifier.ValueText != "cancellationToken");
            Assert.DoesNotContain("?", parameter.Type!.ToString());
        }

        [Theory]
        [InlineData("3.1.0", "{\"type\":[\"string\",\"null\"],\"enum\":[\"ready\",null]}")]
        [InlineData("3.0.3", "{\"type\":\"string\",\"nullable\":true,\"enum\":[\"ready\",null]}")]
        public void NullValuedStringEnumIsExplicitlyRejected(string version, string schema)
        {
            Diagnostic diagnostic = GetOnlyDiagnostic(CreateRequiredParameterDocument(version, schema));

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("String enums containing null", diagnostic.GetMessage());
            Assert.Contains("/parameters/0/schema/enum", diagnostic.GetMessage());
        }

        [Fact]
        public void OpenApi30NullableStringEnumRemainsNullable()
        {
            const string schema = "{\"type\":\"string\",\"nullable\":true,\"enum\":[\"ready\",\"done\"]}";
            Diagnostic diagnostic = GetOnlyDiagnostic(CreateRequiredParameterDocument("3.0.3", schema));

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("cannot use a nullable schema", diagnostic.GetMessage());
        }

        private static Diagnostic GetOnlyDiagnostic(string document)
        {
            return Assert.Single(Phase4GeneratorTestHarness.GenerateAndCompile(document).RunResult.Diagnostics);
        }

        private static string CreateDocument(string dialectField, string schemaFields, string version = "3.1.0")
        {
            return "{\"openapi\":\"" + version + "\"," + dialectField +
                   "\"info\":{\"title\":\"Dialect\",\"version\":\"1\"}," +
                   "\"paths\":{\"/value\":{\"get\":{\"operationId\":\"getValue\"," +
                   "\"responses\":{\"200\":{\"description\":\"OK\",\"content\":{\"application/json\":{" +
                   "\"schema\":{\"$ref\":\"#/components/schemas/Value\"}}}}}}}}," +
                   "\"components\":{\"schemas\":{\"Value\":{" + schemaFields + "}}}}";
        }

        private static string CreateExternalReferenceDocument()
        {
            return "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"External\",\"version\":\"1\"}," +
                   "\"paths\":{\"/value\":{\"get\":{\"operationId\":\"getValue\"," +
                   "\"responses\":{\"200\":{\"description\":\"OK\",\"content\":{\"application/json\":{" +
                   "\"schema\":{\"$ref\":\"value.yaml\"}}}}}}}}}";
        }

        private static string CreateRequiredParameterDocument(string version, string schema)
        {
            return "{\"openapi\":\"" + version + "\",\"info\":{\"title\":\"Enum\",\"version\":\"1\"}," +
                   "\"paths\":{\"/value\":{\"get\":{\"operationId\":\"getValue\"," +
                   "\"parameters\":[{\"name\":\"state\",\"in\":\"query\",\"required\":true,\"schema\":" + schema + "}]," +
                   "\"responses\":{\"204\":{\"description\":\"OK\"}}}}}}";
        }
    }
}
