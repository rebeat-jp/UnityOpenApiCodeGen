using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Newtonsoft.Json;
using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    public sealed class Phase4WireContractReviewTests
    {
        [Theory]
        [InlineData("Allow")]
        [InlineData("content-disposition")]
        [InlineData("Content-Encoding")]
        [InlineData("Content-Language")]
        [InlineData("Content-Length")]
        [InlineData("Content-Location")]
        [InlineData("Content-MD5")]
        [InlineData("Content-Range")]
        [InlineData("CONTENT-TYPE")]
        [InlineData("Expires")]
        [InlineData("Last-Modified")]
        public void ContentOnlyHeaderParameterReportsSourceLocation(string name)
        {
            Diagnostic diagnostic = Assert.Single(Phase4GeneratorTestHarness.GenerateAndCompile(
                HeaderDocument(name)).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("content-only header parameter", diagnostic.GetMessage());
            Assert.Contains("/paths/~1value/get/parameters/0/name", diagnostic.GetMessage());
            Assert.Equal(TestBundleFactory.SourcePath, diagnostic.Location.GetLineSpan().Path);
        }

        [Fact]
        public async Task CustomContentPrefixedHeaderIsSentWithoutRequestBody()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                HeaderDocument("Content-Trace"));
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();
            var handler = new HeaderHandler();
            using var httpClient = new HttpClient(handler);
            object client = Activator.CreateInstance(
                assembly.GetType("Generated.Phase4.Phase4Api")!, httpClient)!;
            var task = (Task)client.GetType().GetMethod("readValue")!
                .Invoke(client, new object[] { "trace-123", CancellationToken.None })!;

            await task;

            Assert.Equal("trace-123", handler.Value);
            Assert.False(handler.HadContent);
        }

        [Fact]
        public void GeneratedStringEnumRoundTripsNamesAndRejectsNumbers()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                EnumDocument("{\"type\":\"string\",\"enum\":[\"ready\",\"done\"]}"));
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Type enumType = execution.EmitAssembly().GetType("Generated.Phase4.State", true)!;

            object value = JsonConvert.DeserializeObject("\"ready\"", enumType)!;
            Assert.Equal("\"ready\"", JsonConvert.SerializeObject(value));
            Assert.Throws<JsonSerializationException>(() => JsonConvert.DeserializeObject("0", enumType));
            Assert.Throws<JsonSerializationException>(() => JsonConvert.SerializeObject(Enum.ToObject(enumType, 99)));
        }

        [Fact]
        public void DifferentEnumValueBoundariesCannotShareSuccessResponseContract()
        {
            string document = TwoResponseDocument(
                "{\"type\":\"string\",\"enum\":[\"a,b\",\"c\"]}",
                "{\"type\":\"string\",\"enum\":[\"a\",\"b,c\"]}");

            Diagnostic diagnostic = Assert.Single(Phase4GeneratorTestHarness.GenerateAndCompile(document)
                .RunResult.Diagnostics);

            Assert.Contains("same JSON body contract", diagnostic.GetMessage());
            Assert.Contains("/responses/201", diagnostic.GetMessage());
        }

        [Fact]
        public void PropertyNamesCannotImpersonateObjectSignatureDelimiters()
        {
            string document = TwoResponseDocument(
                "{\"type\":\"object\",\"properties\":{\"a:False:String:,b\":{\"type\":\"string\"}}}",
                "{\"type\":\"object\",\"properties\":{\"a\":{\"type\":\"string\"}," +
                "\"b\":{\"type\":\"string\"}}}");

            Diagnostic diagnostic = Assert.Single(Phase4GeneratorTestHarness.GenerateAndCompile(document)
                .RunResult.Diagnostics);

            Assert.Contains("same JSON body contract", diagnostic.GetMessage());
            Assert.Contains("/responses/201", diagnostic.GetMessage());
        }

        private static string TwoResponseDocument(string firstSchema, string secondSchema)
        {
            return "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Responses\",\"version\":\"1\"}," +
                "\"paths\":{\"/value\":{\"get\":{\"operationId\":\"readValue\",\"responses\":{" +
                "\"200\":{\"description\":\"OK\",\"content\":{\"application/json\":{\"schema\":" +
                firstSchema + "}}}," +
                "\"201\":{\"description\":\"Created\",\"content\":{\"application/json\":{\"schema\":" +
                secondSchema + "}}}}}}}}";
        }

        private static string HeaderDocument(string name)
        {
            return "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Headers\",\"version\":\"1\"}," +
                "\"paths\":{\"/value\":{\"get\":{\"operationId\":\"readValue\"," +
                "\"parameters\":[{\"name\":\"" + name + "\",\"in\":\"header\",\"required\":true," +
                "\"schema\":{\"type\":\"string\"}}]," +
                "\"responses\":{\"204\":{\"description\":\"OK\"}}}}}}";
        }

        private static string EnumDocument(string schema)
        {
            return "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Enums\",\"version\":\"1\"}," +
                "\"paths\":{\"/value\":{\"get\":{\"operationId\":\"readValue\"," +
                "\"responses\":{\"200\":{\"description\":\"OK\",\"content\":{\"application/json\":{" +
                "\"schema\":{\"$ref\":\"#/components/schemas/State\"}}}}}}}}," +
                "\"components\":{\"schemas\":{\"State\":" + schema + "}}}";
        }

        private sealed class HeaderHandler : HttpMessageHandler
        {
            internal string? Value { get; private set; }
            internal bool HadContent { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                Value = request.Headers.Contains("Content-Trace")
                    ? string.Join(",", request.Headers.GetValues("Content-Trace"))
                    : null;
                HadContent = request.Content is not null;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
        }
    }
}
