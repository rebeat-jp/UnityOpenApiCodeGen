using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    [Collection("JsonConvert default settings isolation")]
    public sealed class Phase4Pr56ReviewRegressionTests
    {
        [Fact]
        public async Task EnumParametersIgnoreGlobalSettingsAndRejectUndeclaredValuesBeforeSend()
        {
            var parameters = new JArray
            {
                Parameter("state", "path", true, "ready", "READY", " leading ", "", "2026-01-02T03:04:05Z"),
                Parameter("queryState", "query", false, "ready", "READY", " leading ", "", "2026-01-02T03:04:05Z"),
                Parameter("X-State", "header", true, "ready", "READY", " leading ", "", "2026-01-02T03:04:05Z")
            };
            Assembly assembly = Compile(Document(parameters, "/values/{state}"));
            var handler = new ByteResponseHandler(Array.Empty<byte>(), HttpStatusCode.NoContent);
            using var http = new HttpClient(handler);
            object api = Activator.CreateInstance(assembly.GetType("Generated.Phase4.Phase4Api")!, http)!;
            MethodInfo method = api.GetType().GetMethod("readValue")!;
            ParameterInfo[] methodParameters = method.GetParameters();
            int pathIndex = Array.FindIndex(methodParameters, parameter => parameter.Name == "state");
            int queryIndex = Array.FindIndex(methodParameters, parameter => parameter.Name == "queryState");
            int headerIndex = Array.FindIndex(methodParameters, parameter =>
                parameter.Name != "state" && parameter.Name != "queryState" &&
                parameter.Name != "cancellationToken");
            var arguments = new object[methodParameters.Length];
            arguments[pathIndex] = EnumValue(methodParameters[pathIndex].ParameterType, " leading ");
            arguments[queryIndex] = EnumValue(methodParameters[queryIndex].ParameterType, "");
            arguments[headerIndex] = EnumValue(methodParameters[headerIndex].ParameterType, "READY");
            arguments[methodParameters.Length - 1] = CancellationToken.None;
            object dateLikeQuery = EnumValue(methodParameters[queryIndex].ParameterType,
                "2026-01-02T03:04:05Z");
            Func<JsonSerializerSettings>? previous = JsonConvert.DefaultSettings;
            int invoked = 0;
            try
            {
                JsonConvert.DefaultSettings = () =>
                {
                    invoked++;
                    return new JsonSerializerSettings
                    {
                        Converters = new List<JsonConverter> { new ReplaceStringConverter() },
                        ContractResolver = new CamelCasePropertyNamesContractResolver()
                    };
                };
                await Invoke(method, api, arguments);
                Assert.Equal("/values/%20leading%20?queryState=", handler.Uri!.PathAndQuery);
                Assert.Equal("READY", handler.Header);
                Assert.Equal(0, invoked);

                arguments[queryIndex] = dateLikeQuery;
                await Invoke(method, api, arguments);
                Assert.Equal("/values/%20leading%20?queryState=2026-01-02T03%3A04%3A05Z",
                    handler.Uri!.PathAndQuery);
                Assert.Equal(0, invoked);

                arguments[queryIndex] = null!;
                await Invoke(method, api, arguments);
                Assert.Equal("/values/%20leading%20", handler.Uri!.PathAndQuery);
                Assert.Equal(0, invoked);

                arguments[pathIndex] = Enum.ToObject(methodParameters[pathIndex].ParameterType, 99);
                await Assert.ThrowsAsync<JsonSerializationException>(() =>
                    Invoke(method, api, arguments));
                Assert.Equal(3, handler.SendCount);
                Assert.Equal(0, invoked);
            }
            finally
            {
                JsonConvert.DefaultSettings = previous;
            }
        }

        [Theory]
        [InlineData("3.0.3", false)]
        [InlineData("3.1.0", false)]
        [InlineData("3.1.0", true)]
        public void UnknownParameterFieldReportsItsOwnLocation(string version, bool component)
        {
            JObject target = Parameter("id", "query", false, "ready");
            target["requird"] = true;
            var parameters = new JArray(component
                ? new JObject { ["$ref"] = "#/components/parameters/Target" }
                : target);
            Diagnostic diagnostic = Assert.Single(Phase4GeneratorTestHarness.GenerateAndCompile(
                Document(parameters, version: version,
                    componentParameter: component ? target : null)).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("Unsupported Parameter field: 'requird'", diagnostic.GetMessage());
            Assert.Contains(component
                ? "/components/parameters/Target/requird"
                : "/paths/~1values/get/parameters/0/requird", diagnostic.GetMessage());
        }

        [Fact]
        public void UnknownExternalParameterFieldReportsTargetLocation()
        {
            const string externalId = "Assets/Specs/other.json";
            string root = Document(new JArray(new JObject
            {
                ["$ref"] = "other.json#/components/parameters/Target"
            }));
            var external = new JObject
            {
                ["openapi"] = "3.1.0",
                ["info"] = new JObject { ["title"] = "External", ["version"] = "1" },
                ["paths"] = new JObject(),
                ["components"] = new JObject
                {
                    ["parameters"] = new JObject
                    {
                        ["Target"] = new JObject
                        {
                            ["name"] = "id", ["in"] = "query",
                            ["schema"] = new JObject { ["type"] = "string" },
                            ["requird"] = true
                        }
                    }
                }
            }.ToString(Formatting.None);
            string bundle = TestBundleFactory.CreateV2(root,
                new[] { new TestBundleFactory.V2DocumentSpec(externalId, externalId, "json", external) },
                new[] { new TestBundleFactory.V2ReferenceSpec("root",
                    "/paths/~1values/get/parameters/0/$ref", externalId,
                    "/components/parameters/Target") });

            Diagnostic diagnostic = Assert.Single(Phase4GeneratorTestHarness.GenerateAndCompile(
                root, bundle: bundle).RunResult.Diagnostics);
            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("/components/parameters/Target/requird", diagnostic.GetMessage());
            Assert.Equal(externalId, diagnostic.Location.GetLineSpan().Path);
        }

        [Fact]
        public void UnknownPathItemParameterFieldReportsItsOwnLocation()
        {
            var root = JObject.Parse(Document(new JArray()));
            JObject pathItem = (JObject)root["paths"]!["/values"]!;
            ((JObject)pathItem["get"]!).Remove("parameters");
            JObject parameter = Parameter("id", "query", false, "ready");
            parameter["requird"] = true;
            pathItem["parameters"] = new JArray(parameter);

            Diagnostic diagnostic = Assert.Single(Phase4GeneratorTestHarness.GenerateAndCompile(
                root.ToString(Formatting.None)).RunResult.Diagnostics);
            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("/paths/~1values/parameters/0/requird", diagnostic.GetMessage());
        }

        [Theory]
        [InlineData("3.0.3")]
        [InlineData("3.1.0")]
        public void StandardParameterAnnotationsAndExtensionsAreAcceptedButContentRemainsUnsupported(string version)
        {
            JObject parameter = Parameter("id", "query", false, "ready");
            parameter["description"] = "query state";
            parameter["deprecated"] = false;
            parameter["allowEmptyValue"] = false;
            parameter["style"] = "form";
            parameter["explode"] = true;
            parameter["allowReserved"] = false;
            parameter["example"] = "ready";
            parameter["x-note"] = new JObject { ["ignored"] = true };
            Phase4GeneratorExecution accepted = Phase4GeneratorTestHarness.GenerateAndCompile(
                Document(new JArray(parameter), version: version));
            Assert.Empty(accepted.RunResult.Diagnostics);
            Assert.Empty(accepted.CompilationErrors);

            parameter.Remove("example");
            parameter["examples"] = new JObject { ["named"] = new JObject { ["value"] = "ready" } };
            Phase4GeneratorExecution examples = Phase4GeneratorTestHarness.GenerateAndCompile(
                Document(new JArray(parameter), version: version));
            Assert.Empty(examples.RunResult.Diagnostics);
            Assert.Empty(examples.CompilationErrors);

            Phase4GeneratorExecution referenced = Phase4GeneratorTestHarness.GenerateAndCompile(
                Document(new JArray(new JObject { ["$ref"] = "#/components/parameters/Target" }),
                    version: version, componentParameter: parameter));
            Assert.Empty(referenced.RunResult.Diagnostics);
            Assert.Empty(referenced.CompilationErrors);

            parameter["content"] = new JObject();
            Diagnostic diagnostic = Assert.Single(Phase4GeneratorTestHarness.GenerateAndCompile(
                Document(new JArray(parameter), version: version)).RunResult.Diagnostics);
            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("Parameter content is not supported", diagnostic.GetMessage());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task InvalidUtf8AndUtf16ResponseBytesFailWithDecoderCause(bool utf16)
        {
            Assembly assembly = Compile(Document(new JArray(), typedResponse: true));
            byte[] bytes = utf16
                ? new byte[] { 0x22, 0x00, 0x00, 0xD8, 0x22, 0x00 }
                : new byte[] { 0x22, 0xC3, 0x28, 0x22 };
            var handler = new ByteResponseHandler(bytes, charset: utf16 ? "utf-16" : null);
            JsonSerializationException error = await Assert.ThrowsAsync<JsonSerializationException>(
                () => ReadValue(assembly, handler));
            Assert.IsType<DecoderFallbackException>(error.InnerException);
        }

        [Theory]
        [InlineData("utf-8", false, null)]
        [InlineData("utf-8", true, null)]
        [InlineData("utf-8", true, "utf-8")]
        [InlineData("utf-16", true, null)]
        [InlineData("utf-16", true, "\"utf-16\"")]
        [InlineData("utf-16BE", true, null)]
        [InlineData("utf-32", true, null)]
        public async Task UnicodeResponseDecodesWithDeclaredCharsetOrBom(
            string charset, bool bom, string? declaredCharset)
        {
            Assembly assembly = Compile(Document(new JArray(), typedResponse: true));
            string json = "\"日本語😀\"";
            Encoding encoding = Encoding.GetEncoding(charset);
            byte[] body = encoding.GetBytes(json);
            if (bom)
            {
                body = encoding.GetPreamble().Concat(body).ToArray();
            }
            var handler = new ByteResponseHandler(body, charset: declaredCharset);

            Assert.Equal("日本語😀", await ReadValue(assembly, handler));
        }

        [Fact]
        public async Task DeclaredCharsetTakesPriorityAndOnlyMatchingBomIsRemoved()
        {
            Assembly assembly = Compile(Document(new JArray(), typedResponse: true));
            byte[] utf16 = Encoding.Unicode.GetBytes("\"日本語\"");
            Assert.Equal("日本語", await ReadValue(assembly,
                new ByteResponseHandler(utf16, charset: "\"utf-16\"")));

            byte[] contrary = Encoding.UTF8.GetPreamble().Concat(utf16).ToArray();
            await Assert.ThrowsAsync<JsonSerializationException>(() => ReadValue(assembly,
                new ByteResponseHandler(contrary, charset: "utf-16")));
        }

        [Fact]
        public async Task EmptyTypedResponseAndErrorResponsePreserveTheirContracts()
        {
            Assembly assembly = Compile(Document(new JArray(), typedResponse: true));
            await Assert.ThrowsAsync<JsonSerializationException>(() => ReadValue(assembly,
                new ByteResponseHandler(Array.Empty<byte>(), charset: "unknown-charset")));
            await Assert.ThrowsAsync<InvalidOperationException>(() => ReadValue(assembly,
                new ByteResponseHandler(Encoding.UTF8.GetBytes("\"value\""),
                    charset: "unknown-charset")));

            var handler = new ByteResponseHandler(new byte[] { 0xC3, 0x28 },
                HttpStatusCode.BadRequest);
            Exception error = await Assert.ThrowsAnyAsync<Exception>(() => ReadValue(assembly, handler));
            Assert.Equal("Phase4ApiException", error.GetType().Name);
            Assert.Equal("\uFFFD(", error.GetType().GetProperty("ResponseBody")!.GetValue(error));

            Assembly bodyless = Compile(Document(new JArray()));
            await ReadValue(bodyless, new ByteResponseHandler(
                new byte[] { 0xC3, 0x28 }, HttpStatusCode.NoContent));
        }

        [Theory]
        [InlineData("ReadJsonResponseBodyAsync")]
        [InlineData("HasPreamble")]
        public void GeneratedHelperNamesAvoidApiOperationAndParameterCollisions(string name)
        {
            JObject parameter = Parameter(name, "query", false, "ready");
            string document = Document(new JArray(parameter), operationId: name, typedResponse: true);
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                document);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);

            Diagnostic apiNameDiagnostic = Assert.Single(Phase4GeneratorTestHarness.GenerateAndCompile(
                document, apiName: name).RunResult.Diagnostics);
            Assert.Equal("OACG005", apiNameDiagnostic.Id);
        }

        [Fact]
        public void TypedResponseKeepsErrorBodyParameterName()
        {
            string document = Document(
                new JArray(Parameter("errorBody", "query", false, "ready")),
                typedResponse: true);
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(document);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);

            MethodInfo method = execution.EmitAssembly()
                .GetType("Generated.Phase4.Phase4Api")!.GetMethod("readValue")!;
            Assert.Contains(method.GetParameters(), parameter => parameter.Name == "errorBody");
        }

        private static JObject Parameter(string name, string location, bool required, params string[] values)
        {
            return new JObject
            {
                ["name"] = name,
                ["in"] = location,
                ["required"] = required,
                ["schema"] = new JObject { ["type"] = "string", ["enum"] = new JArray(values) }
            };
        }

        private static string Document(JArray parameters, string path = "/values",
            string version = "3.1.0", JObject? componentParameter = null,
            bool typedResponse = false, string operationId = "readValue")
        {
            var operation = new JObject
            {
                ["operationId"] = operationId,
                ["parameters"] = parameters,
                ["responses"] = new JObject
                {
                    [typedResponse ? "200" : "204"] = typedResponse
                        ? new JObject
                        {
                            ["description"] = "OK",
                            ["content"] = new JObject
                            {
                                ["application/json"] = new JObject
                                {
                                    ["schema"] = new JObject { ["type"] = "string" }
                                }
                            }
                        }
                        : new JObject { ["description"] = "Done" }
                }
            };
            var root = new JObject
            {
                ["openapi"] = version,
                ["info"] = new JObject { ["title"] = "PR56", ["version"] = "1" },
                ["servers"] = new JArray(new JObject { ["url"] = "https://example.test/" }),
                ["paths"] = new JObject { [path] = new JObject { ["get"] = operation } }
            };
            if (componentParameter is not null)
            {
                root["components"] = new JObject
                {
                    ["parameters"] = new JObject { ["Target"] = componentParameter }
                };
            }
            return root.ToString(Formatting.None);
        }

        private static Assembly Compile(string document)
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(document);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            return execution.EmitAssembly();
        }

        private static object EnumValue(Type type, string wire) =>
            JsonConvert.DeserializeObject(JsonConvert.SerializeObject(wire), type,
                new JsonSerializerSettings { DateParseHandling = DateParseHandling.None })!;

        private static async Task Invoke(MethodInfo method, object api, params object[] arguments)
        {
            await (Task)method.Invoke(api, arguments)!;
        }

        private static async Task<object?> ReadValue(Assembly assembly, ByteResponseHandler handler)
        {
            using var http = new HttpClient(handler);
            object api = Activator.CreateInstance(assembly.GetType("Generated.Phase4.Phase4Api")!, http)!;
            var task = (Task)api.GetType().GetMethod("readValue")!
                .Invoke(api, new object[] { CancellationToken.None })!;
            await task;
            return task.GetType().GetProperty("Result")?.GetValue(task);
        }

        private sealed class ReplaceStringConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType) => objectType == typeof(string);
            public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) =>
                writer.WriteValue("corrupt");
            public override object ReadJson(JsonReader reader, Type objectType, object? existingValue,
                JsonSerializer serializer) => "corrupt";
        }

        private sealed class ByteResponseHandler : HttpMessageHandler
        {
            private readonly byte[] _body;
            private readonly HttpStatusCode _status;
            private readonly string? _charset;

            internal ByteResponseHandler(byte[] body, HttpStatusCode status = HttpStatusCode.OK,
                string? charset = null)
            {
                _body = body;
                _status = status;
                _charset = charset;
            }

            internal int SendCount { get; private set; }
            internal Uri? Uri { get; private set; }
            internal string? Header { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                SendCount++;
                Uri = request.RequestUri;
                Header = request.Headers.TryGetValues("X-State", out IEnumerable<string>? values)
                    ? values.Single() : null;
                var content = new ByteArrayContent(_body);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
                {
                    CharSet = _charset
                };
                return Task.FromResult(new HttpResponseMessage(_status) { Content = content });
            }
        }
    }
}
