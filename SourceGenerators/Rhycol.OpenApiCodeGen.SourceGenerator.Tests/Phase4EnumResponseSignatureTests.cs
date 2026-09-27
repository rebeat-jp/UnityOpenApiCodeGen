using Microsoft.CodeAnalysis;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    public sealed class Phase4EnumResponseSignatureTests
    {
        private const string FirstEnum = "{\"type\":\"string\",\"enum\":[\"z\",\"a\",\"m\"]}";
        private const string ReorderedEnum = "{\"type\":\"string\",\"enum\":[\"m\",\"z\",\"a\"]}";

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ReorderedInlineEnumValuesShareResponseContract(bool multipleMediaTypes)
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                CreateDocument(multipleMediaTypes, FirstEnum, ReorderedEnum));

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
        }

        [Theory]
        [InlineData(false, "{\"type\":\"string\",\"enum\":[\"a\",\"b\"]}",
            "{\"type\":\"string\",\"enum\":[\"a\",\"c\"]}")]
        [InlineData(false, "{\"type\":\"string\",\"enum\":[\"a\",\"a\",\"b\"]}",
            "{\"type\":\"string\",\"enum\":[\"a\",\"b\"]}")]
        [InlineData(true, "{\"type\":\"string\",\"enum\":[\"a\",\"b\"]}",
            "{\"type\":\"string\",\"enum\":[\"a\",\"c\"]}")]
        [InlineData(true, "{\"type\":\"string\",\"enum\":[\"a\",\"a\",\"b\"]}",
            "{\"type\":\"string\",\"enum\":[\"a\",\"b\"]}")]
        public void DifferentValuesOrDuplicateCountsRemainIncompatible(
            bool multipleMediaTypes,
            string firstSchema,
            string secondSchema)
        {
            AssertInconsistent(CreateDocument(multipleMediaTypes, firstSchema, secondSchema), multipleMediaTypes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void NullableDifferenceRemainsIncompatible(bool multipleMediaTypes)
        {
            const string First = "{\"type\":\"string\",\"enum\":[\"a\",\"b\"]}";
            const string Second = "{\"type\":\"string\",\"nullable\":true,\"enum\":[\"b\",\"a\"]}";

            AssertInconsistent(
                CreateDocument(multipleMediaTypes, First, Second, version: "3.0.3"),
                multipleMediaTypes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DistinctNamedReferencesRemainIncompatible(bool multipleMediaTypes)
        {
            const string Components = "{\"schemas\":{\"First\":" + FirstEnum +
                                      ",\"Second\":" + ReorderedEnum + "}}";
            const string FirstReference = "{\"$ref\":\"#/components/schemas/First\"}";
            const string SecondReference = "{\"$ref\":\"#/components/schemas/Second\"}";

            AssertInconsistent(
                CreateDocument(multipleMediaTypes, FirstReference, SecondReference, Components),
                multipleMediaTypes);
        }

        private static void AssertInconsistent(string document, bool multipleMediaTypes)
        {
            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(document).RunResult.Diagnostics);

            Assert.Equal("OACG105", diagnostic.Id);
            Assert.Contains(
                multipleMediaTypes
                    ? "All JSON media types must use the same schema"
                    : "same JSON body contract",
                diagnostic.GetMessage());
        }

        private static string CreateDocument(
            bool multipleMediaTypes,
            string firstSchema,
            string secondSchema,
            string components = "",
            string version = "3.1.0")
        {
            string responses = multipleMediaTypes
                ? "\"200\":{\"description\":\"OK\",\"content\":{" +
                  "\"application/json\":{\"schema\":" + firstSchema + "}," +
                  "\"application/problem+json\":{\"schema\":" + secondSchema + "}}}"
                : "\"200\":{\"description\":\"OK\",\"content\":{" +
                  "\"application/json\":{\"schema\":" + firstSchema + "}}}," +
                  "\"201\":{\"description\":\"Created\",\"content\":{" +
                  "\"application/json\":{\"schema\":" + secondSchema + "}}}";

            return "{\"openapi\":\"" + version + "\",\"info\":{\"title\":\"Enums\",\"version\":\"1\"}," +
                   "\"paths\":{\"/value\":{\"get\":{\"operationId\":\"readValue\",\"responses\":{" +
                   responses + "}}}}" +
                   (components.Length == 0 ? string.Empty : ",\"components\":" + components) + "}";
        }
    }
}
