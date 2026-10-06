using Microsoft.CodeAnalysis;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    public sealed class Phase4NewSemanticBoundaryTests
    {
        private const string BodyResponse =
            "{\"description\":\"OK\",\"content\":{\"application/json\":{\"schema\":{\"type\":\"string\"}}}}";

        [Theory]
        [InlineData("head", "200", false)]
        [InlineData("get", "204", false)]
        [InlineData("get", "205", false)]
        [InlineData("get", "2XX", false)]
        [InlineData("head", "200", true)]
        [InlineData("get", "204", true)]
        public void BodylessSuccessCannotDeclareJsonContent(string method, string status, bool referenced)
        {
            string response = referenced
                ? "{\"$ref\":\"#/components/responses/Body\"}"
                : BodyResponse;
            string components = referenced
                ? ",\"components\":{\"responses\":{\"Body\":" + BodyResponse + "}}"
                : string.Empty;
            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(
                    Document("\"/values\"", method, "\"" + status + "\":" + response, components))
                .RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("cannot declare body content", diagnostic.GetMessage());
            Assert.Contains("/content", diagnostic.GetMessage());
        }

        [Theory]
        [InlineData("head", "200")]
        [InlineData("get", "204")]
        [InlineData("get", "205")]
        [InlineData("get", "2XX")]
        public void BodylessSuccessStillGeneratesTask(string method, string status)
        {
            Phase4GeneratorExecution result = Phase4GeneratorTestHarness.GenerateAndCompile(
                Document("\"/values\"", method,
                    "\"" + status + "\":{\"description\":\"OK\",\"content\":{}}"));

            Assert.Empty(result.RunResult.Diagnostics);
            Assert.Empty(result.CompilationErrors);
            Assert.Contains("Task getValue", result.GeneratedSource);
        }

        [Fact]
        public void HeadErrorResponseCannotDeclareBodyContent()
        {
            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(
                    Document("\"/values\"", "head",
                        "\"200\":{\"description\":\"OK\"},\"400\":" + BodyResponse))
                .RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("/responses/400/content", diagnostic.GetMessage());
        }

        [Theory]
        [InlineData("/values?x=1")]
        [InlineData("/values#fragment")]
        public void RawQueryOrFragmentInPathKeyReportsKeyLocation(string path)
        {
            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(
                    Document("\"" + path + "\"", "get", "\"204\":{\"description\":\"OK\"}"))
                .RunResult.Diagnostics);

            Assert.Equal("OACG100", diagnostic.Id);
            Assert.Contains("cannot contain raw", diagnostic.GetMessage());
            Assert.Contains("/paths/~1" + path.Substring(1), diagnostic.GetMessage());
        }

        [Fact]
        public void PercentEncodedPathKeyIsAccepted()
        {
            Phase4GeneratorExecution result = Phase4GeneratorTestHarness.GenerateAndCompile(
                Document("\"/values%3Fx%23fragment\"", "get", "\"204\":{\"description\":\"OK\"}"));
            Assert.Empty(result.RunResult.Diagnostics);
            Assert.Empty(result.CompilationErrors);
        }

        [Theory]
        [InlineData("null")]
        [InlineData("[]")]
        [InlineData("\"bad\"")]
        public void ResponseHeadersMustBeObject(string headers)
        {
            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(
                    Document("\"/values\"", "get",
                        "\"204\":{\"description\":\"OK\",\"headers\":" + headers + "}"))
                .RunResult.Diagnostics);

            Assert.Equal("OACG100", diagnostic.Id);
            Assert.Contains("response headers field must be an object", diagnostic.GetMessage());
            Assert.Contains("/headers", diagnostic.GetMessage());
        }

        [Fact]
        public void NonEmptyResponseHeadersRemainUnsupported()
        {
            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(
                    Document("\"/values\"", "get",
                        "\"204\":{\"description\":\"OK\",\"headers\":{\"X-Test\":{\"schema\":{\"type\":\"string\"}}}}"))
                .RunResult.Diagnostics);
            Assert.Equal("OACG101", diagnostic.Id);
        }

        [Fact]
        public void EmptyResponseHeadersRemainAccepted()
        {
            Phase4GeneratorExecution result = Phase4GeneratorTestHarness.GenerateAndCompile(
                Document("\"/values\"", "get",
                    "\"204\":{\"description\":\"OK\",\"headers\":{}}"));
            Assert.Empty(result.RunResult.Diagnostics);
            Assert.Empty(result.CompilationErrors);
        }

        [Theory]
        [InlineData("3.0.4", "parameter", "summary")]
        [InlineData("3.1.0", "parameter", "required")]
        [InlineData("3.0.4", "requestBody", "description")]
        [InlineData("3.1.0", "requestBody", "required")]
        [InlineData("3.0.4", "response", "summary")]
        [InlineData("3.1.0", "response", "content")]
        public void UnsupportedReferenceSiblingsArePositioned(
            string version, string kind, string sibling)
        {
            string value = sibling == "required" ? "true" :
                sibling == "content" ? "{\"application/json\":{\"schema\":{\"type\":\"string\"}}}" :
                "\"annotation\"";
            string reference = "{\"$ref\":\"#/components/" + ComponentName(kind) + "/Target\",\"" +
                               sibling + "\":" + value + "}";
            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(
                    ReferenceDocument(version, kind, reference)).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("alongside $ref", diagnostic.GetMessage());
            Assert.Contains("/" + sibling, diagnostic.GetMessage());
        }

        [Theory]
        [InlineData("parameter")]
        [InlineData("requestBody")]
        [InlineData("response")]
        public void OpenApi31ReferenceAnnotationsAreAccepted(string kind)
        {
            string reference = "{\"$ref\":\"#/components/" + ComponentName(kind) +
                               "/Target\",\"summary\":\"note\",\"description\":\"note\"}";
            Phase4GeneratorExecution result = Phase4GeneratorTestHarness.GenerateAndCompile(
                ReferenceDocument("3.1.0", kind, reference));

            Assert.Empty(result.RunResult.Diagnostics);
            Assert.Empty(result.CompilationErrors);
        }

        private static string ComponentName(string kind)
        {
            return kind == "parameter" ? "parameters" : kind == "requestBody" ? "requestBodies" : "responses";
        }

        private static string ReferenceDocument(string version, string kind, string reference)
        {
            string parameter = "{\"name\":\"q\",\"in\":\"query\",\"schema\":{\"type\":\"string\"}}";
            string requestBody = "{\"content\":{\"application/json\":{\"schema\":{\"type\":\"string\"}}}}";
            string response = "{\"description\":\"OK\"}";
            string operation = "\"operationId\":\"getValue\"," +
                               (kind == "parameter" ? "\"parameters\":[" + reference + "]," : string.Empty) +
                               (kind == "requestBody" ? "\"requestBody\":" + reference + "," : string.Empty) +
                               "\"responses\":{\"200\":" + (kind == "response" ? reference : response) + "}";
            string components = "\"components\":{\"" + ComponentName(kind) + "\":{\"Target\":" +
                                (kind == "parameter" ? parameter : kind == "requestBody" ? requestBody : response) + "}}";
            return "{\"openapi\":\"" + version + "\",\"info\":{\"title\":\"Test\",\"version\":\"1\"}," +
                   "\"paths\":{\"/values\":{\"post\":{" + operation + "}}}," + components + "}";
        }

        private static string Document(string path, string method, string responses, string components = "")
        {
            return "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Test\",\"version\":\"1\"}," +
                   "\"paths\":{" + path + ":{\"" + method + "\":{\"operationId\":\"getValue\"," +
                   "\"responses\":{" + responses + "}}}}" + components + "}";
        }
    }
}
