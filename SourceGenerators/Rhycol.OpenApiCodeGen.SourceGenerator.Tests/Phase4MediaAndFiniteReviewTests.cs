using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    public sealed class Phase4MediaAndFiniteReviewTests
    {
        [Fact]
        public async Task RelativeRootPathDoesNotDuplicateBaseAddressQuery()
        {
            string document = CreateDocument("getValue", "get", "not-used", "application/json", "\"type\": \"string\"", "200", "application/json", "", "/");
            Assembly assembly = Compile(document);
            var handler = new ReplyHandler(HttpStatusCode.OK, "\"ok\"", "application/json");
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://fallback.example.test/root/?once=1") };
            object api = Activator.CreateInstance(assembly.GetType("Generated.Phase4.Phase4Api")!, http)!;

            Assert.Equal("ok", await Invoke(api, "getValue", CancellationToken.None));
            Assert.Equal("https://fallback.example.test/root/?once=1", handler.RequestUri!.AbsoluteUri);
        }

        [Theory]
        [InlineData("200", "application/json", "200", "APPLICATION/JSON; charset=utf-8", false)]
        [InlineData("200", "application/json", "200", "text/plain", true)]
        [InlineData("200", "application/json; profile=one", "200", "application/json; profile=two", true)]
        [InlineData("200", "application/json; profile=one", "200", "APPLICATION/JSON; profile=one; charset=utf-8", false)]
        [InlineData("200", "application/json; profile=one", "200", @"application/json; profile=""\one""", false)]
        [InlineData("200", @"application/json; profile=""https://example.test""", "200", @"application/json; profile=""https:\/\/example.test""", false)]
        [InlineData("200", @"application/json; profile=""https:\/\/example.test""", "200", @"application/json; profile=""https://example.test""", false)]
        [InlineData("200", "application/json; profile=one", "200", @"application/json; profile=""\two""", true)]
        [InlineData("200", "application/*+json", "200", "application/problem+json", false)]
        [InlineData("200", "application/*+json", "200", "application/json", true)]
        [InlineData("200", "application/json", "200", null, false)]
        public async Task SuppliedResponseContentTypeMustMatchDeclaration(
            string declaredStatus, string declaredMedia, string actualStatus, string? actualMedia, bool rejects)
        {
            Assembly assembly = Compile(CreateDocument("getValue", "get", "not-used", "application/json",
                "\"type\": \"string\"", declaredStatus, declaredMedia));
            var handler = new ReplyHandler((HttpStatusCode)int.Parse(actualStatus), "\"ok\"", actualMedia);
            using var http = new HttpClient(handler);
            object api = Activator.CreateInstance(assembly.GetType("Generated.Phase4.Phase4Api")!, http)!;
            if (rejects)
            {
                JsonSerializationException exception = await Assert.ThrowsAsync<JsonSerializationException>(
                    () => Invoke(api, "getValue", CancellationToken.None));
                Assert.Contains("Content-Type", exception.Message);
            }
            else
            {
                Assert.Equal("ok", await Invoke(api, "getValue", CancellationToken.None));
            }
        }

        [Theory]
        [InlineData("invalid media value")]
        [InlineData("application/json, text/plain")]
        [InlineData("application/json; profile=\"one\\\"")]
        public async Task MalformedSuppliedContentTypeIsRejected(string contentType)
        {
            Assembly assembly = Compile(CreateDocument("getValue", "get", "not-used", "application/json",
                "\"type\": \"string\"", "200", "application/json"));
            var handler = new ReplyHandler(HttpStatusCode.OK, "\"ok\"", contentType);
            using var http = new HttpClient(handler);
            object api = Activator.CreateInstance(assembly.GetType("Generated.Phase4.Phase4Api")!, http)!;
            JsonSerializationException exception = await Assert.ThrowsAsync<JsonSerializationException>(
                () => Invoke(api, "getValue", CancellationToken.None));
            Assert.Contains("Content-Type", exception.Message);
        }

        [Theory]
        [InlineData("application /json", false)]
        [InlineData("application/ json", false)]
        [InlineData("application/json", true)]
        public void GeneratedMediaTypeScannerRequiresContiguousTypeAndSubtype(string contentType, bool valid)
        {
            Assembly assembly = Compile(CreateDocument("getValue", "get", "not-used", "application/json",
                "\"type\": \"string\"", "200", "application/json"));
            Type api = assembly.GetType("Generated.Phase4.Phase4Api")!;
            MethodInfo scanner = Assert.Single(api.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                .Where(method => method.Name.EndsWith("HasValidHead", StringComparison.Ordinal)));

            // HttpContentHeaders canonicalizes these malformed raw values before a handler can return them.
            Assert.Equal(valid, scanner.Invoke(null, new object[] { contentType }));
        }

        [Fact]
        public async Task MultipleSuppliedContentTypesAreRejected()
        {
            Assembly assembly = Compile(CreateDocument("getValue", "get", "not-used", "application/json",
                "\"type\": \"string\"", "200", "application/json"));
            var handler = new ReplyHandler(HttpStatusCode.OK, "\"ok\"", "application/json", "text/plain");
            using var http = new HttpClient(handler);
            object api = Activator.CreateInstance(assembly.GetType("Generated.Phase4.Phase4Api")!, http)!;
            await Assert.ThrowsAsync<JsonSerializationException>(() => Invoke(api, "getValue", CancellationToken.None));
        }

        [Fact]
        public async Task MediaValidatorNamesDoNotCollideWithOperationsOrParameters()
        {
            var document = JObject.Parse(CreateDocument("__OacgContentType_Validate", "get",
                "not-used", "application/json", "\"type\": \"string\"", "200", "application/json"));
            document["paths"]!["/value"]!["get"]!["parameters"] = new JArray(new JObject
            {
                ["name"] = "__OacgContentType_DeclaredMediaTypes", ["in"] = "query",
                ["required"] = true, ["schema"] = new JObject { ["type"] = "string" }
            });
            Assembly assembly = Compile(document.ToString(Formatting.None));
            var handler = new ReplyHandler(HttpStatusCode.OK, "\"ok\"", "application/json");
            using var http = new HttpClient(handler);
            object api = Activator.CreateInstance(assembly.GetType("Generated.Phase4.Phase4Api")!, http)!;
            Assert.Equal("ok", await Invoke(api, "__OacgContentType_Validate", "value", CancellationToken.None));
        }

        [Fact]
        public void WildcardOnlyRequestMediaReportsLocatedDiagnostic()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                CreateDocument("sendValue", "post", "application/*+json", "application/json",
                    "\"type\": \"string\"", "204", null));
            Diagnostic diagnostic = Assert.Single(execution.RunResult.Diagnostics);
            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("requestBody/content", diagnostic.GetMessage());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void UnsupportedWildcardSubtypeReportsLocatedDiagnostic(bool response)
        {
            string document = response
                ? CreateDocument("getValue", "get", "not-used", "application/json",
                    "\"type\": \"string\"", "200", "application/vnd.*+json")
                : CreateDocument("sendValue", "post", "application/vnd.*+json", "application/json",
                    "\"type\": \"string\"", "204", null);

            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(document).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("Only application/*+json", diagnostic.GetMessage());
            Assert.Contains("content/application~1vnd.*+json", diagnostic.GetMessage());
        }

        [Theory]
        [InlineData("application/*+json")]
        [InlineData("application/vnd.example+json")]
        public void SupportedResponseJsonMediaRangesCompile(string mediaType)
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                CreateDocument("getValue", "get", "not-used", "application/json",
                    "\"type\": \"string\"", "200", mediaType));

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
        }

        [Fact]
        public async Task ConcreteRequestMediaWinsOverWildcard()
        {
            string document = CreateDocument("sendValue", "post", "application/*+json", "application/json",
                "\"type\": \"string\"", "204", null);
            var root = JObject.Parse(document);
            root["paths"]!["/value"]!["post"]!["requestBody"]!["content"]!["application/json"] =
                JObject.Parse("{\"schema\":{\"type\":\"string\"}}");
            Assembly assembly = Compile(root.ToString(Formatting.None));
            var handler = new ReplyHandler(HttpStatusCode.NoContent, "", null);
            using var http = new HttpClient(handler);
            object api = Activator.CreateInstance(assembly.GetType("Generated.Phase4.Phase4Api")!, http)!;
            await Invoke(api, "sendValue", "x", CancellationToken.None);
            Assert.Equal("application/json", handler.RequestContentType);
        }

        [Theory]
        [InlineData("float", "NaN")]
        [InlineData("float", "Infinity")]
        [InlineData("double", "NaN")]
        [InlineData("double", "-Infinity")]
        public async Task NonFiniteRootNumberIsRejectedBeforeSending(string format, string value)
        {
            Assembly assembly = Compile(CreateDocument("sendValue", "post", "application/json", "application/json",
                "\"type\": \"number\", \"format\": \"" + format + "\"", "204", null));
            var handler = new ReplyHandler(HttpStatusCode.NoContent, "", null);
            using var http = new HttpClient(handler);
            object api = Activator.CreateInstance(assembly.GetType("Generated.Phase4.Phase4Api")!, http)!;
            MethodInfo method = api.GetType().GetMethod("sendValue")!;
            object number = format == "float"
                ? (object)(value == "NaN" ? float.NaN : value == "Infinity" ? float.PositiveInfinity : float.NegativeInfinity)
                : value == "NaN" ? double.NaN : value == "Infinity" ? double.PositiveInfinity : double.NegativeInfinity;
            var task = (Task)method.Invoke(api, new object[] { number, CancellationToken.None })!;
            JsonSerializationException exception = await Assert.ThrowsAsync<JsonSerializationException>(async () => await task);
            Assert.Contains("$", exception.Message);
            Assert.Equal(0, handler.SendCount);
        }

        [Theory]
        [InlineData("\"type\": \"array\", \"items\": {\"type\": \"number\", \"format\": \"float\"}", "[\"NaN\"]", "$[0]")]
        [InlineData("\"type\": \"object\", \"additionalProperties\": false, \"required\": [\"score\"], \"properties\": {\"score\": {\"type\": \"number\", \"format\": \"double\"}}", "{\"score\":\"Infinity\"}", "$.score")]
        public async Task NonFiniteArrayAndDtoNumbersAreRejectedBeforeSending(string schema, string json, string path)
        {
            Assembly assembly = Compile(CreateDocument("sendValue", "post", "application/json", "application/json",
                schema, "204", null));
            var handler = new ReplyHandler(HttpStatusCode.NoContent, "", null);
            using var http = new HttpClient(handler);
            object api = Activator.CreateInstance(assembly.GetType("Generated.Phase4.Phase4Api")!, http)!;
            MethodInfo method = api.GetType().GetMethod("sendValue")!;
            object body = JsonConvert.DeserializeObject(json, method.GetParameters()[0].ParameterType)!;
            var task = (Task)method.Invoke(api, new object[] { body, CancellationToken.None })!;
            JsonSerializationException exception = await Assert.ThrowsAsync<JsonSerializationException>(async () => await task);
            Assert.Contains(path, exception.Message);
            Assert.Equal(0, handler.SendCount);
        }

        [Theory]
        [InlineData("{\"values\":[\"NaN\"]}", "$.values[0]", true)]
        [InlineData("{\"values\":[1.5]}", null, true)]
        [InlineData("{\"values\":[\"Infinity\"]}", null, false)]
        public async Task NestedRequestNumberHonorsSpecifiedFlag(string json, string? rejectedPath, bool specified)
        {
            const string schema = "\"type\": \"object\", \"additionalProperties\": false, \"properties\": {\"values\": {\"type\": [\"array\", \"null\"], \"items\": {\"type\": \"number\", \"format\": \"double\"}}}";
            Assembly assembly = Compile(CreateDocument("sendValue", "post", "application/json", "application/json", schema, "204", null));
            var handler = new ReplyHandler(HttpStatusCode.NoContent, "", null);
            using var http = new HttpClient(handler);
            object api = Activator.CreateInstance(assembly.GetType("Generated.Phase4.Phase4Api")!, http)!;
            MethodInfo method = api.GetType().GetMethod("sendValue")!;
            object body = JsonConvert.DeserializeObject(json, method.GetParameters()[0].ParameterType)!;
            body.GetType().GetProperty("ValuesSpecified")!.SetValue(body, specified);
            var task = (Task)method.Invoke(api, new object[] { body, CancellationToken.None })!;
            if (rejectedPath != null)
            {
                JsonSerializationException exception = await Assert.ThrowsAsync<JsonSerializationException>(async () => await task);
                Assert.Contains(rejectedPath, exception.Message);
                Assert.Equal(0, handler.SendCount);
            }
            else
            {
                await task;
                Assert.Equal(1, handler.SendCount);
                if (!specified) Assert.Equal("{}", handler.RequestBody);
            }
        }

        private static Assembly Compile(string document)
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(document);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            return execution.EmitAssembly();
        }

        private static string CreateDocument(string operationId, string method, string requestMedia, string responseMedia,
            string schemaProperties, string status, string? responseMediaKey, string server = "https://api.example.test/", string path = "/value")
        {
            var schema = JObject.Parse("{" + schemaProperties + "}");
            var operation = new JObject { ["operationId"] = operationId };
            if (method == "post")
            {
                operation["requestBody"] = new JObject { ["required"] = true,
                    ["content"] = new JObject { [requestMedia] = new JObject { ["schema"] = schema } } };
            }
            operation["responses"] = new JObject { [status] = responseMediaKey == null
                ? new JObject { ["description"] = "Done" }
                : new JObject { ["description"] = "Done", ["content"] = new JObject {
                    [responseMediaKey] = new JObject { ["schema"] = schema } } } };
            return new JObject { ["openapi"] = "3.1.0", ["info"] = new JObject { ["title"] = "Review", ["version"] = "1" },
                ["servers"] = new JArray(new JObject { ["url"] = server }),
                ["paths"] = new JObject { [path] = new JObject { [method] = operation } } }.ToString(Formatting.None);
        }

        private static async Task<object?> Invoke(object api, string method, params object[] args)
        {
            var task = (Task)api.GetType().GetMethod(method)!.Invoke(api, args)!;
            await task;
            return task.GetType().IsGenericType ? task.GetType().GetProperty("Result")!.GetValue(task) : null;
        }

        private sealed class ReplyHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            private readonly string _body;
            private readonly string? _extraContentType;
            internal ReplyHandler(HttpStatusCode status, string body, string? contentType, string? extraContentType = null)
            {
                _status = status;
                _body = body;
                ContentType = contentType;
                _extraContentType = extraContentType;
            }
            internal string? ContentType { get; set; }
            internal int SendCount { get; private set; }
            internal Uri? RequestUri { get; private set; }
            internal string? RequestContentType { get; private set; }
            internal string? RequestBody { get; private set; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                SendCount++;
                RequestUri = request.RequestUri;
                RequestContentType = request.Content?.Headers.ContentType?.MediaType;
                RequestBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                var response = new HttpResponseMessage(_status);
                if (_status != HttpStatusCode.NoContent)
                {
                    response.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(_body));
                    if (ContentType != null) response.Content.Headers.TryAddWithoutValidation("Content-Type", ContentType);
                    if (_extraContentType != null) response.Content.Headers.TryAddWithoutValidation("Content-Type", _extraContentType);
                }
                return Task.FromResult(response);
            }
        }
    }
}
