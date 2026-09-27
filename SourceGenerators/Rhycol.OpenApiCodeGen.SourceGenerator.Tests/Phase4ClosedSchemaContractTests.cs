using Microsoft.CodeAnalysis;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    public sealed class Phase4ClosedSchemaContractTests
    {
        private const string Properties = "\"properties\":{\"name\":{\"type\":\"string\"}}";

        [Theory]
        [InlineData("3.0.3", "", "/components/schemas/Payload")]
        [InlineData("3.1.0", "", "/components/schemas/Payload")]
        [InlineData("3.0.3", "\"additionalProperties\":true,", "/components/schemas/Payload/additionalProperties")]
        [InlineData("3.1.0", "\"additionalProperties\":true,", "/components/schemas/Payload/additionalProperties")]
        [InlineData("3.0.3", "\"additionalProperties\":{\"type\":\"string\"},", "/components/schemas/Payload/additionalProperties")]
        [InlineData("3.1.0", "\"additionalProperties\":{\"type\":\"string\"},", "/components/schemas/Payload/additionalProperties")]
        public void OpenNamedObjectsAreRejectedAtTheirSourceLocation(
            string version,
            string additionalProperties,
            string expectedPath)
        {
            Diagnostic diagnostic = GetOnlyDiagnostic(DocumentWithComponent(
                "{\"type\":\"object\"," + additionalProperties + Properties + "}",
                version));

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Equal(TestBundleFactory.SourcePath, diagnostic.Location.GetLineSpan().Path);
            Assert.Contains("logical path '" + expectedPath + "'", diagnostic.GetMessage());
        }

        [Theory]
        [InlineData("3.0.3")]
        [InlineData("3.1.0")]
        public void ExplicitlyClosedNamedObjectGeneratesAndCompiles(string version)
        {
            AssertGenerates(DocumentWithComponent(
                "{\"type\":\"object\",\"additionalProperties\":false," + Properties + "}",
                version));
        }

        [Fact]
        public void NestedObjectAndArrayItemAssertionsAreCheckedAtTheirOwnLocations()
        {
            const string NestedObject = "{\"type\":\"object\",\"additionalProperties\":false," +
                                        "\"properties\":{\"child\":{\"type\":\"object\"," +
                                        "\"properties\":{\"name\":{\"type\":\"string\"}}}}}";
            Diagnostic openChild = GetOnlyDiagnostic(DocumentWithComponent(NestedObject));
            Assert.Equal("OACG101", openChild.Id);
            Assert.Contains("/components/schemas/Payload/properties/child'", openChild.GetMessage());

            const string ArrayItem = "{\"type\":\"array\",\"items\":{\"type\":\"string\"," +
                                     "\"minLength\":2}}";
            Diagnostic constrainedItem = GetOnlyDiagnostic(DocumentWithComponent(ArrayItem));
            Assert.Equal("OACG101", constrainedItem.Id);
            Assert.Contains("/components/schemas/Payload/items/minLength'", constrainedItem.GetMessage());
        }

        [Theory]
        [InlineData("3.0.3", "multipleOf", "2")]
        [InlineData("3.0.3", "maximum", "10")]
        [InlineData("3.0.3", "minimum", "1")]
        [InlineData("3.0.3", "exclusiveMaximum", "true")]
        [InlineData("3.0.3", "exclusiveMinimum", "true")]
        [InlineData("3.0.3", "maxLength", "8")]
        [InlineData("3.0.3", "minLength", "2")]
        [InlineData("3.0.3", "pattern", "\"^[a-z]+$\"")]
        [InlineData("3.0.3", "maxItems", "4")]
        [InlineData("3.0.3", "minItems", "1")]
        [InlineData("3.0.3", "uniqueItems", "true")]
        [InlineData("3.0.3", "maxProperties", "3")]
        [InlineData("3.0.3", "minProperties", "1")]
        [InlineData("3.1.0", "const", "\"fixed\"")]
        [InlineData("3.1.0", "exclusiveMaximum", "10")]
        [InlineData("3.1.0", "minContains", "1")]
        [InlineData("3.1.0", "maxContains", "2")]
        [InlineData("3.1.0", "propertyNames", "{\"pattern\":\"^[a-z]+$\"}")]
        [InlineData("3.1.0", "contentSchema", "{\"type\":\"string\"}")]
        [InlineData("3.1.0", "$defs", "{\"Part\":{\"type\":\"string\"}}")]
        [InlineData("3.1.0", "dependentSchemas", "{\"name\":{\"type\":\"string\"}}")]
        [InlineData("3.1.0", "unevaluatedProperties", "false")]
        [InlineData("3.1.0", "customAssertion", "true")]
        public void UnmodeledAssertionsAreRejectedAtTheirKeyword(
            string version,
            string keyword,
            string value)
        {
            Diagnostic diagnostic = GetOnlyDiagnostic(DocumentWithComponent(
                "{\"type\":\"string\",\"" + keyword + "\":" + value + "}",
                version));

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("Schema keyword '" + keyword + "' is not supported", diagnostic.GetMessage());
            Assert.Contains("/components/schemas/Payload/" + keyword + "'", diagnostic.GetMessage());
        }

        [Theory]
        [InlineData("3.0.3")]
        [InlineData("3.1.0")]
        public void AnnotationsAndExtensionsRemainAccepted(string version)
        {
            AssertGenerates(DocumentWithComponent(
                "{\"type\":\"object\",\"additionalProperties\":false," + Properties +
                ",\"title\":\"Named\",\"description\":\"A value\",\"default\":{}," +
                "\"example\":{\"name\":\"sample\"},\"deprecated\":true," +
                "\"x-vendor\":{\"const\":\"literal extension data\"}}",
                version));
        }

        [Theory]
        [InlineData("400")]
        [InlineData("default")]
        public void ErrorResponseInlineSchemaStillAllowsUnmodeledAssertions(string status)
        {
            string document = "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Errors\",\"version\":\"1\"}," +
                              "\"paths\":{\"/value\":{\"get\":{\"operationId\":\"readValue\"," +
                              "\"responses\":{\"204\":{\"description\":\"OK\"},\"" + status +
                              "\":{\"description\":\"Error\",\"content\":{\"application/json\":{" +
                              "\"schema\":{\"type\":\"object\",\"properties\":{\"name\":{" +
                              "\"type\":\"string\",\"pattern\":\"^[a-z]+$\"}},\"const\":{}," +
                              "\"additionalProperties\":true}}}}}}}}}";

            AssertGenerates(document);
        }

        [Fact]
        public void DuplicateStringEnumWireValueReportsSecondValueLocation()
        {
            Diagnostic diagnostic = GetOnlyDiagnostic(DocumentWithComponent(
                "{\"type\":\"string\",\"enum\":[\"ready\",\"ready\"]}"));

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("enum values must be unique", diagnostic.GetMessage());
            Assert.Contains("/components/schemas/Payload/enum/1'", diagnostic.GetMessage());
        }

        [Theory]
        [InlineData("{\"type\":\"object\",\"additionalProperties\":false}", "Free-form object schemas")]
        [InlineData("{\"type\":\"object\",\"additionalProperties\":true}", "additionalProperties maps")]
        [InlineData("{\"type\":\"object\",\"additionalProperties\":{\"type\":\"string\"}}", "additionalProperties maps")]
        public void FreeFormAndMapObjectsRemainUnsupported(string schema, string expectedMessage)
        {
            Diagnostic diagnostic = GetOnlyDiagnostic(DocumentWithComponent(schema));

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains(expectedMessage, diagnostic.GetMessage());
        }

        private static string DocumentWithComponent(string schema, string version = "3.1.0")
        {
            return "{\"openapi\":\"" + version + "\",\"info\":{\"title\":\"Schema\",\"version\":\"1\"}," +
                   "\"paths\":{},\"components\":{\"schemas\":{\"Payload\":" + schema + "}}}";
        }

        private static Diagnostic GetOnlyDiagnostic(string document)
        {
            return Assert.Single(Phase4GeneratorTestHarness.GenerateAndCompile(document).RunResult.Diagnostics);
        }

        private static void AssertGenerates(string document)
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(document);

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
        }
    }
}
