using Microsoft.CodeAnalysis;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    public sealed class Phase4ClosedSchemaContractTests
    {
        private const string Properties = "\"properties\":{\"name\":{\"type\":\"string\"}}";

        [Theory]
        [InlineData("3.0.3", "")]
        [InlineData("3.1.0", "")]
        [InlineData("3.0.3", "\"additionalProperties\":true,")]
        [InlineData("3.1.0", "\"additionalProperties\":true,")]
        [InlineData("3.0.3", "\"additionalProperties\":{\"type\":\"string\"},")]
        [InlineData("3.1.0", "\"additionalProperties\":{\"type\":\"string\"},")]
        public void OpenNamedObjectsGenerateExtensionData(string version, string additionalProperties)
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                DocumentWithComponent("{\"type\":\"object\"," + additionalProperties + Properties + "}", version));

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assert.Contains("JsonExtensionData", execution.GeneratedSource);
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
            // An object without additionalProperties is open now, so it is no longer rejected.
            const string NestedObject = "{\"type\":\"object\",\"additionalProperties\":false," +
                                        "\"properties\":{\"child\":{\"type\":\"object\"," +
                                        "\"properties\":{\"name\":{\"type\":\"string\"}}}}}";
            AssertGenerates(DocumentWithComponent(NestedObject));

            // P6: the validation keywords (minLength, ...) are checked at run time, so they are accepted
            // at every nesting level; a keyword that stays out of scope is still reported at its own location.
            const string NestedObjectWithConstraint = "{\"type\":\"object\",\"additionalProperties\":false," +
                                        "\"properties\":{\"child\":{\"type\":\"object\"," +
                                        "\"properties\":{\"name\":{\"type\":\"string\",\"maxLength\":3}}}}}";
            AssertGenerates(DocumentWithComponent(NestedObjectWithConstraint));

            const string ArrayItem = "{\"type\":\"array\",\"items\":{\"type\":\"string\"," +
                                     "\"minLength\":2}}";
            AssertGenerates(DocumentWithComponent(ArrayItem));

            const string NestedUnsupported = "{\"type\":\"object\",\"additionalProperties\":false," +
                                        "\"properties\":{\"child\":{\"type\":\"object\"," +
                                        "\"properties\":{\"name\":{\"type\":\"string\",\"if\":{}}}}}}";
            Diagnostic unsupportedChild = GetOnlyDiagnostic(DocumentWithComponent(NestedUnsupported));
            Assert.Equal("OACG101", unsupportedChild.Id);
            Assert.Contains(
                "/components/schemas/Payload/properties/child/properties/name/if'",
                unsupportedChild.GetMessage());

            const string UnsupportedArrayItem = "{\"type\":\"array\",\"items\":{\"type\":\"string\"," +
                                                "\"then\":{}}}";
            Diagnostic unsupportedItem = GetOnlyDiagnostic(DocumentWithComponent(UnsupportedArrayItem));
            Assert.Equal("OACG101", unsupportedItem.Id);
            Assert.Contains("/components/schemas/Payload/items/then'", unsupportedItem.GetMessage());
        }

        [Theory]
        // The 3.1 validation keywords do not exist in OpenAPI 3.0.
        [InlineData("3.0.3", "patternProperties", "{\"^a\":{\"type\":\"string\"}}")]
        [InlineData("3.0.3", "propertyNames", "{\"pattern\":\"^[a-z]+$\"}")]
        [InlineData("3.0.3", "contains", "{\"type\":\"string\"}")]
        [InlineData("3.0.3", "minContains", "1")]
        [InlineData("3.0.3", "maxContains", "2")]
        [InlineData("3.0.3", "prefixItems", "[{\"type\":\"string\"}]")]
        [InlineData("3.0.3", "dependentRequired", "{\"a\":[\"b\"]}")]
        // const and $defs are OpenAPI 3.1 keywords (supported there since P1); 3.0 still rejects them.
        [InlineData("3.0.3", "const", "\"fixed\"")]
        [InlineData("3.0.3", "$defs", "{\"Part\":{\"type\":\"string\"}}")]
        // Out of scope in every version.
        [InlineData("3.1.0", "if", "{}")]
        [InlineData("3.1.0", "then", "{}")]
        [InlineData("3.1.0", "else", "{}")]
        [InlineData("3.1.0", "contentSchema", "{\"type\":\"string\"}")]
        [InlineData("3.1.0", "dependentSchemas", "{\"name\":{\"type\":\"string\"}}")]
        [InlineData("3.1.0", "unevaluatedProperties", "false")]
        [InlineData("3.1.0", "unevaluatedItems", "false")]
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

            // P4: the schema (const {}) cannot be generated, so the typed error is skipped with an OACG108
            // warning; the document still generates and the body stays available as ResponseBody.
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(document);
            Assert.All(execution.RunResult.Diagnostics, diagnostic => Assert.Equal("OACG108", diagnostic.Id));
            Assert.Empty(execution.CompilationErrors);
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
        [InlineData("{\"type\":\"object\",\"additionalProperties\":false}")]
        [InlineData("{\"type\":\"object\",\"additionalProperties\":true}")]
        [InlineData("{\"type\":\"object\",\"additionalProperties\":{\"type\":\"string\"}}")]
        public void FreeFormAndMapObjectsGenerate(string schema)
        {
            AssertGenerates(DocumentWithComponent(schema));
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
