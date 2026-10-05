using Microsoft.CodeAnalysis;

using Newtonsoft.Json.Linq;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    public sealed class Phase4ServerAndResponseFieldTests
    {
        [Theory]
        [InlineData("http://[")]
        [InlineData("http:foo")]
        [InlineData("http:/path")]
        [InlineData("https:///path")]
        [InlineData("//[")]
        [InlineData("v1/%ZZ")]
        [InlineData("/v1#bad%ZZ")]
        [InlineData("v1#bad%ZZ")]
        public void MalformedServerUrlReportsPositionedDiagnostic(string url)
        {
            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(Document(url)).RunResult.Diagnostics);

            Assert.Equal("OACG100", diagnostic.Id);
            Assert.Contains("server URL", diagnostic.GetMessage());
            Assert.Contains("logical path '/servers/0/url'", diagnostic.GetMessage());
        }

        [Theory]
        [InlineData("https://{tenant}.example.test")]
        [InlineData("/v1/{tenant}")]
        [InlineData("v1/{tenant}")]
        public void UnresolvedServerVariableReportsPositionedDiagnostic(string url)
        {
            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(Document(url)).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("Server URL variables are not supported", diagnostic.GetMessage());
            Assert.Contains("logical path '/servers/0/url'", diagnostic.GetMessage());
        }

        [Theory]
        [InlineData("https://api.example.test/v1")]
        [InlineData("http://api.example.test/v1?key=1")]
        [InlineData("//api.example.test/v1")]
        [InlineData("/v1?key=1")]
        [InlineData("/v1#fragment")]
        [InlineData("v1?query")]
        [InlineData("v1#fragment")]
        [InlineData("#fragment")]
        [InlineData("v1/values")]
        public void SupportedServerUrlFormsGenerateClient(string url)
        {
            Phase4GeneratorExecution result = Phase4GeneratorTestHarness.GenerateAndCompile(Document(url));

            Assert.Empty(result.RunResult.Diagnostics);
            Assert.Empty(result.CompilationErrors);
        }

        [Theory]
        [InlineData("200", "contents")]
        [InlineData("204", "unexpected")]
        [InlineData("400", "contents")]
        [InlineData("default", "unexpected")]
        public void UnknownResponseFieldReportsPositionedDiagnostic(string status, string field)
        {
            var response = new JObject { ["description"] = "response", [field] = new JObject() };
            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(
                    Document("/v1", status, response)).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("Unsupported Response field: '" + field + "'", diagnostic.GetMessage());
            Assert.Contains("logical path '/paths/~1values/get/responses/" + status + "/" + field + "'",
                diagnostic.GetMessage());
        }

        [Fact]
        public void UnknownFieldInReferencedResponseReportsComponentLocation()
        {
            var target = new JObject { ["description"] = "response", ["contents"] = new JObject() };
            var reference = new JObject { ["$ref"] = "#/components/responses/Target" };
            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(
                    Document("/v1", "204", reference, target)).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("Unsupported Response field: 'contents'", diagnostic.GetMessage());
            Assert.Contains("logical path '/components/responses/Target/contents'", diagnostic.GetMessage());
        }

        [Fact]
        public void ResponseExtensionAndSupportedFieldsRemainAccepted()
        {
            var response = new JObject
            {
                ["description"] = "response",
                ["headers"] = new JObject(),
                ["content"] = new JObject(),
                ["x-note"] = new JObject { ["enabled"] = true }
            };
            Phase4GeneratorExecution result = Phase4GeneratorTestHarness.GenerateAndCompile(
                Document("/v1", "204", response));

            Assert.Empty(result.RunResult.Diagnostics);
            Assert.Empty(result.CompilationErrors);
        }

        private static string Document(
            string url,
            string status = "204",
            JObject? response = null,
            JObject? componentResponse = null)
        {
            response ??= new JObject { ["description"] = "response" };
            var document = new JObject
            {
                ["openapi"] = "3.1.0",
                ["info"] = new JObject { ["title"] = "Test", ["version"] = "1" },
                ["servers"] = new JArray(new JObject { ["url"] = url }),
                ["paths"] = new JObject
                {
                    ["/values"] = new JObject
                    {
                        ["get"] = new JObject
                        {
                            ["operationId"] = "getValue",
                            ["responses"] = new JObject
                            {
                                ["204"] = new JObject { ["description"] = "No content" },
                                [status] = response
                            }
                        }
                    }
                }
            };
            if (componentResponse is not null)
            {
                document["components"] = new JObject
                {
                    ["responses"] = new JObject { ["Target"] = componentResponse }
                };
            }

            return document.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
