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

        [Theory]
        [InlineData("Accept")]
        [InlineData("aCcEpT")]
        [InlineData("Authorization")]
        [InlineData("AUTHORIZATION")]
        public void IgnoredHeaderParameterReportsSourceLocation(string name)
        {
            Diagnostic diagnostic = Assert.Single(Phase4GeneratorTestHarness.GenerateAndCompile(
                HeaderDocument(name)).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("ignored by OpenAPI", diagnostic.GetMessage());
            Assert.Contains("/paths/~1value/get/parameters/0/name", diagnostic.GetMessage());
            Assert.Equal(TestBundleFactory.SourcePath, diagnostic.Location.GetLineSpan().Path);
        }

        [Theory]
        [InlineData("")]
        [InlineData("Bad Name")]
        [InlineData("Bad:Name")]
        [InlineData("Bad\nName")]
        [InlineData("X-雪")]
        public void InvalidHeaderNameReportsNameLocation(string name)
        {
            Diagnostic diagnostic = Assert.Single(Phase4GeneratorTestHarness.GenerateAndCompile(
                HeaderDocument(name)).RunResult.Diagnostics);

            Assert.Equal("OACG100", diagnostic.Id);
            Assert.Contains("HTTP field-name tokens", diagnostic.GetMessage());
            Assert.Contains("/paths/~1value/get/parameters/0/name", diagnostic.GetMessage());
            Assert.Equal(TestBundleFactory.SourcePath, diagnostic.Location.GetLineSpan().Path);
        }

        [Theory]
        [InlineData("X!#$%&'*+-.^_`|~Value")]
        [InlineData("X-Trace_123")]
        public void ValidAsciiHeaderTokenCompiles(string name)
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                HeaderDocument(name));

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
        }

        [Theory]
        [InlineData("/pets}")]
        [InlineData("/pets/{id}}")]
        [InlineData("/pets/{id}/tail}")]
        public void ExtraClosingPathBraceReportsPositionedDiagnostic(string path)
        {
            Diagnostic diagnostic = Assert.Single(Phase4GeneratorTestHarness.GenerateAndCompile(
                PathDocument(path)).RunResult.Diagnostics);

            Assert.Equal("OACG100", diagnostic.Id);
            Assert.Contains("unmatched '}'", diagnostic.GetMessage());
            Assert.Contains("/paths/", diagnostic.GetMessage());
            Assert.Equal(TestBundleFactory.SourcePath, diagnostic.Location.GetLineSpan().Path);
        }

        [Theory]
        [InlineData("/pets")]
        [InlineData("/pets/{id}")]
        [InlineData("/pets/{id}/tail")]
        public void WellFormedPathTemplatesCompile(string path)
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                PathDocument(path));

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
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
            using var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://example.test/")
            };
            object client = Activator.CreateInstance(
                assembly.GetType("Generated.Phase4.Phase4Api")!, httpClient)!;
            var task = (Task)client.GetType().GetMethod("readValue")!
                .Invoke(client, new object[] { "trace-123", CancellationToken.None })!;

            await task;

            Assert.Equal("trace-123", handler.Value);
            Assert.False(handler.HadContent);
        }

        [Theory]
        [InlineData("safe\r\nInjected: true")]
        [InlineData("safe\nInjected: true")]
        [InlineData("safe\rInjected: true")]
        [InlineData("safe\0")]
        [InlineData("safe\u001f")]
        [InlineData("safe\u007f")]
        [InlineData("safe\u0080")]
        [InlineData("safe\u00ff")]
        [InlineData("safe\u0100")]
        [InlineData("safe\ud83d\ude00")]
        public Task HeaderCharactersOutsideFieldValueRangeAreRejectedBeforeHttpSend(string value)
        {
            return AssertInvalidHeaderValueIsRejectedBeforeHttpSend(value);
        }

        [Fact]
        public Task LoneHighSurrogateInHeaderIsRejectedBeforeHttpSend()
        {
            return AssertInvalidHeaderValueIsRejectedBeforeHttpSend("safe\ud83d");
        }

        [Fact]
        public Task LoneLowSurrogateInHeaderIsRejectedBeforeHttpSend()
        {
            return AssertInvalidHeaderValueIsRejectedBeforeHttpSend("safe\ude00");
        }

        private static async Task AssertInvalidHeaderValueIsRejectedBeforeHttpSend(string value)
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                HeaderDocument("X-Trace"));
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();
            var handler = new HeaderHandler();
            using var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://example.test/")
            };
            object client = Activator.CreateInstance(
                assembly.GetType("Generated.Phase4.Phase4Api")!, httpClient)!;
            var task = (Task)client.GetType().GetMethod("readValue")!
                .Invoke(client, new object[] { value, CancellationToken.None })!;

            ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(async () => await task);
            Assert.Contains("X-Trace", error.Message);
            Assert.Equal(0, handler.SendCount);
        }

        [Theory]
        [InlineData("before\tafter")]
        [InlineData("before !~after")]
        public async Task HeaderCharactersWithinFieldValueRangeAreSent(string value)
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                HeaderDocument("X-Trace"));
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();
            var handler = new HeaderHandler();
            using var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://example.test/")
            };
            object client = Activator.CreateInstance(
                assembly.GetType("Generated.Phase4.Phase4Api")!, httpClient)!;
            var task = (Task)client.GetType().GetMethod("readValue")!
                .Invoke(client, new object[] { value, CancellationToken.None })!;

            await task;
            Assert.Equal(1, handler.SendCount);
            Assert.True(handler.HadXTrace);
        }

        [Fact]
        public async Task OptionalHeaderIsOmittedWhenNull()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                HeaderDocument("X-Trace", required: false));
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();
            var handler = new HeaderHandler();
            using var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://example.test/")
            };
            object client = Activator.CreateInstance(
                assembly.GetType("Generated.Phase4.Phase4Api")!, httpClient)!;
            var task = (Task)client.GetType().GetMethod("readValue")!
                .Invoke(client, new object?[] { null, CancellationToken.None })!;

            await task;
            Assert.Equal(1, handler.SendCount);
            Assert.False(handler.HadXTrace);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TraceRequestBodyReportsPositionedUnsupportedDiagnostic(bool reference)
        {
            Diagnostic diagnostic = Assert.Single(Phase4GeneratorTestHarness.GenerateAndCompile(
                TraceDocument(reference)).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("TRACE operations cannot declare a requestBody", diagnostic.GetMessage());
            Assert.Contains("/paths/~1value/trace/requestBody", diagnostic.GetMessage());
            Assert.Equal(TestBundleFactory.SourcePath, diagnostic.Location.GetLineSpan().Path);
        }

        [Fact]
        public void BodylessTraceStillCompiles()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                TraceDocument(reference: null));

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
        }

        [Theory]
        [InlineData(true, "application/json; profile=\"Ā\"", false)]
        [InlineData(false, "application/json; profile=\"Ā\"", false)]
        [InlineData(true, "application/json; profile=\"\\Ā\"", false)]
        [InlineData(false, "application/json; profile=\"\\Ā\"", false)]
        [InlineData(false, "application/json; profile=\"\u0080\"", true)]
        [InlineData(false, "application/json; profile=\"\\\u0080\"", true)]
        [InlineData(false, "application/json; profile=\"ÿ\"", true)]
        [InlineData(false, "application/json; profile=\"\\ÿ\"", true)]
        public void QuotedMediaParameterUsesByteRange(bool request, string mediaType, bool valid)
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                MediaDocument(request, mediaType));
            if (valid)
            {
                Assert.Empty(execution.RunResult.Diagnostics);
                Assert.Empty(execution.CompilationErrors);
                return;
            }

            Diagnostic diagnostic = Assert.Single(execution.RunResult.Diagnostics);
            Assert.Equal("OACG100", diagnostic.Id);
            Assert.Contains("invalid value", diagnostic.GetMessage());
            Assert.Contains(request ? "/requestBody/content/" : "/responses/200/content/",
                diagnostic.GetMessage());
            Assert.Equal(TestBundleFactory.SourcePath, diagnostic.Location.GetLineSpan().Path);
        }

        [Theory]
        [InlineData("application/json; profile=\"\u0080\"")]
        [InlineData("application/json; profile=\"\\\u0080\"")]
        [InlineData("application/json; profile=\"\u00ff\"")]
        [InlineData("application/json; profile=\"\\\u00ff\"")]
        public void SelectedRequestMediaTypeWithNonAsciiCharacterReportsPositionedDiagnostic(string mediaType)
        {
            Diagnostic diagnostic = Assert.Single(Phase4GeneratorTestHarness.GenerateAndCompile(
                MediaDocument(true, mediaType)).RunResult.Diagnostics);

            Assert.Equal("OACG100", diagnostic.Id);
            Assert.Contains("request media type must contain ASCII characters only", diagnostic.GetMessage());
            Assert.Contains("/paths/~1value/post/requestBody/content/" + mediaType.Replace("/", "~1", StringComparison.Ordinal),
                diagnostic.GetMessage());
            Assert.Equal(TestBundleFactory.SourcePath, diagnostic.Location.GetLineSpan().Path);
        }

        [Theory]
        [InlineData("application/json; profile=\"\\~\"")]
        [InlineData("application/json; profile=\"ASCII-~\"")]
        public void SelectedRequestMediaTypeWithAsciiParameterCompiles(string mediaType)
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                MediaDocument(true, mediaType));

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
        }

        [Fact]
        public void NonSelectedNonAsciiRequestMediaTypeDoesNotBlockAsciiContentType()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                TwoRequestMediaDocument("application/json", "application/problem+json; profile=\"\u00ff\""));

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assert.Contains("CreateJsonContent(requestJson, \"application/json\")", execution.GeneratedSource);
        }

        [Fact]
        public void SelectedNonAsciiRequestMediaTypeIsRejectedWithMultipleEntries()
        {
            string selectedMediaType = "application/json; profile=\"\u0080\"";
            Diagnostic diagnostic = Assert.Single(Phase4GeneratorTestHarness.GenerateAndCompile(
                TwoRequestMediaDocument(selectedMediaType, "application/problem+json"))
                .RunResult.Diagnostics);

            Assert.Equal("OACG100", diagnostic.Id);
            Assert.Contains("/requestBody/content/application~1json; profile=\"\u0080\"", diagnostic.GetMessage());
        }

        [Fact]
        public void NonSelectedMalformedRequestMediaTypeStillReportsDiagnostic()
        {
            Diagnostic diagnostic = Assert.Single(Phase4GeneratorTestHarness.GenerateAndCompile(
                TwoRequestMediaDocument("application/json", "application/problem+json; profile=\"\u0100\""))
                .RunResult.Diagnostics);

            Assert.Equal("OACG100", diagnostic.Id);
            Assert.Contains("invalid value", diagnostic.GetMessage());
            Assert.Contains("/requestBody/content/application~1problem+json; profile=\"\u0100\"",
                diagnostic.GetMessage());
        }

        [Theory]
        [InlineData(true, "BINARY")]
        [InlineData(false, "BINARY")]
        [InlineData(true, "Binary")]
        [InlineData(false, "Binary")]
        public void NoncanonicalBinaryFormatIsOrdinaryString(bool request, string format)
        {
            string document = MediaDocument(request, "application/json").Replace(
                "\"schema\":{\"type\":\"string\"}",
                "\"schema\":{\"type\":\"string\",\"format\":\"" + format + "\"}",
                StringComparison.Ordinal);
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(document);

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assert.Contains(request ? "string? body" : "Task<string>", execution.GeneratedSource);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void LowercaseBinaryFormatRemainsUnsupported(bool request)
        {
            string document = MediaDocument(request, "application/json").Replace(
                "\"schema\":{\"type\":\"string\"}",
                "\"schema\":{\"type\":\"string\",\"format\":\"binary\"}",
                StringComparison.Ordinal);
            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(document).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("Binary schemas", diagnostic.GetMessage());
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
                "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"a:False:String:,b\":{\"type\":\"string\"}}}",
                "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"a\":{\"type\":\"string\"}," +
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

        private static string HeaderDocument(string name, bool required = true)
        {
            return "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Headers\",\"version\":\"1\"}," +
                "\"paths\":{\"/value\":{\"get\":{\"operationId\":\"readValue\"," +
                "\"parameters\":[{\"name\":" + JsonConvert.SerializeObject(name) + ",\"in\":\"header\",\"required\":" +
                (required ? "true" : "false") + "," +
                "\"schema\":{\"type\":\"string\"}}]," +
                "\"responses\":{\"204\":{\"description\":\"OK\"}}}}}}";
        }

        private static string TraceDocument(bool? reference)
        {
            string requestBody = reference is null
                ? string.Empty
                : "\"requestBody\":" + (reference.Value
                    ? "{\"$ref\":\"#/components/requestBodies/Payload\"}"
                    : "{\"content\":{\"application/json\":{\"schema\":{\"type\":\"string\"}}}}") + ",";
            return "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Trace\",\"version\":\"1\"}," +
                "\"paths\":{\"/value\":{\"trace\":{\"operationId\":\"traceValue\"," +
                requestBody + "\"responses\":{\"204\":{\"description\":\"Done\"}}}}}," +
                "\"components\":{\"requestBodies\":{\"Payload\":{\"content\":{\"application/json\":{" +
                "\"schema\":{\"type\":\"string\"}}}}}}}";
        }

        private static string MediaDocument(bool request, string mediaType)
        {
            string key = JsonConvert.SerializeObject(mediaType);
            string content = "{\"content\":{" + key + ":{\"schema\":{\"type\":\"string\"}}}}";
            string operation = request
                ? "\"requestBody\":" + content + ",\"responses\":{\"204\":{\"description\":\"Done\"}}"
                : "\"responses\":{\"200\":{\"description\":\"OK\"," +
                  "\"content\":{" + key + ":{\"schema\":{\"type\":\"string\"}}}}}";
            return "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Media\",\"version\":\"1\"}," +
                "\"paths\":{\"/value\":{\"" + (request ? "post" : "get") +
                "\":{\"operationId\":\"readValue\"," + operation + "}}}}";
        }

        private static string TwoRequestMediaDocument(string firstMediaType, string secondMediaType)
        {
            return "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Media\",\"version\":\"1\"}," +
                "\"paths\":{\"/value\":{\"post\":{\"operationId\":\"readValue\"," +
                "\"requestBody\":{\"content\":{" + JsonConvert.SerializeObject(firstMediaType) +
                ":{\"schema\":{\"type\":\"string\"}}," + JsonConvert.SerializeObject(secondMediaType) +
                ":{\"schema\":{\"type\":\"string\"}}}}," +
                "\"responses\":{\"204\":{\"description\":\"Done\"}}}}}}";
        }

        private static string PathDocument(string path)
        {
            string parameter = path.Contains("{id}", StringComparison.Ordinal)
                ? "\"parameters\":[{\"name\":\"id\",\"in\":\"path\",\"required\":true," +
                  "\"schema\":{\"type\":\"string\"}}],"
                : string.Empty;
            return "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Path\",\"version\":\"1\"}," +
                "\"paths\":{" + JsonConvert.SerializeObject(path) + ":{\"get\":{\"operationId\":\"readValue\"," +
                parameter + "\"responses\":{\"204\":{\"description\":\"OK\"}}}}}}";
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
            internal bool HadXTrace { get; private set; }
            internal int SendCount { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                SendCount++;
                HadXTrace = request.Headers.Contains("X-Trace");
                Value = request.Headers.Contains("Content-Trace")
                    ? string.Join(",", request.Headers.GetValues("Content-Trace"))
                    : null;
                HadContent = request.Content is not null;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
        }
    }
}
