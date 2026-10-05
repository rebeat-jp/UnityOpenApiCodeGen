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
    public sealed class Phase4ResponseMetadataPropertyTests
    {
        [Theory]
        [InlineData("$id", false)]
        [InlineData("$ref", false)]
        [InlineData("$values", false)]
        [InlineData("$type", false)]
        [InlineData("$type", true)]
        public void ResponseDtoCannotDeclareJsonNetMetadataProperty(string wireName, bool required)
        {
            string document = CreateDocument(
                responseSchema: new JObject { ["$ref"] = "#/components/schemas/Payload" },
                components: new JObject
                {
                    ["Payload"] = ClosedObject(new JObject
                    {
                        [wireName] = new JObject { ["type"] = "string" }
                    }, required ? wireName : null)
                });

            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(document).RunResult.Diagnostics);

            Assert.Equal("OACG100", diagnostic.Id);
            Assert.Contains("Response DTO property '" + wireName + "'", diagnostic.GetMessage());
            Assert.Contains("logical path '/components/schemas/Payload/properties/" + wireName + "'",
                diagnostic.GetMessage());
        }

        [Theory]
        [InlineData(false, "$id")]
        [InlineData(true, "$values")]
        [InlineData(true, "$type")]
        public void NestedResponseDtoCannotDeclareJsonNetMetadataProperty(bool array, string wireName)
        {
            JObject childReference = new JObject { ["$ref"] = "#/components/schemas/Child" };
            JToken memberSchema = array
                ? new JObject { ["type"] = "array", ["items"] = childReference }
                : childReference;
            string document = CreateDocument(
                responseSchema: new JObject { ["$ref"] = "#/components/schemas/Envelope" },
                components: new JObject
                {
                    ["Envelope"] = ClosedObject(new JObject { ["member"] = memberSchema }),
                    ["Child"] = ClosedObject(new JObject
                    {
                        [wireName] = new JObject { ["type"] = "string" }
                    })
                });

            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(document).RunResult.Diagnostics);

            Assert.Equal("OACG100", diagnostic.Id);
            Assert.Contains("logical path '/components/schemas/Child/properties/" + wireName + "'",
                diagnostic.GetMessage());
        }

        [Fact]
        public async Task RequestOnlyDtoMayDeclareJsonNetMetadataNames()
        {
            var metadataProperties = new JObject
            {
                ["$id"] = new JObject { ["type"] = "string" },
                ["$ref"] = new JObject { ["type"] = "string" },
                ["$values"] = new JObject { ["type"] = "string" },
                ["$type"] = new JObject { ["type"] = "string" }
            };
            string document = CreateDocument(
                responseSchema: null,
                components: new JObject { ["Payload"] = ClosedObject(metadataProperties, "$type") },
                requestSchema: new JObject { ["$ref"] = "#/components/schemas/Payload" });
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(document);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();
            Type dtoType = assembly.GetType("Generated.Phase4.Payload", throwOnError: true)!;
            object body = Activator.CreateInstance(dtoType)!;
            foreach (PropertyInfo property in dtoType.GetProperties())
            {
                string? name = property.GetCustomAttribute<JsonPropertyAttribute>()?.PropertyName;
                if (name == "$id" || name == "$ref" || name == "$values" || name == "$type")
                {
                    property.SetValue(body, name + "-value");
                }
            }

            var handler = new RecordingHandler(HttpStatusCode.NoContent, string.Empty);
            using var httpClient = new HttpClient(handler);
            object api = Activator.CreateInstance(
                assembly.GetType("Generated.Phase4.Phase4Api", throwOnError: true)!, httpClient)!;
            await (Task)api.GetType().GetMethod("sendValue")!
                .Invoke(api, new[] { body, CancellationToken.None })!;

            JObject sent = JObject.Parse(handler.RequestBody!);
            Assert.Equal("$id-value", (string?)sent["$id"]);
            Assert.Equal("$ref-value", (string?)sent["$ref"]);
            Assert.Equal("$values-value", (string?)sent["$values"]);
            Assert.Equal("$type-value", (string?)sent["$type"]);
        }

        [Fact]
        public async Task ResponseReferenceMetadataStillSupportsArrayWrapperAndDtoReferences()
        {
            string document = CreateDocument(
                responseSchema: new JObject
                {
                    ["type"] = "array",
                    ["items"] = new JObject { ["$ref"] = "#/components/schemas/Item" }
                },
                components: new JObject
                {
                    ["Item"] = ClosedObject(new JObject
                    {
                        ["name"] = new JObject { ["type"] = "string" }
                    })
                });
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(document);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();
            const string response = "{\"$id\":\"array\",\"$values\":[" +
                "{\"$id\":\"item\",\"name\":\"first\"},{\"$ref\":\"item\"}]}";
            var handler = new RecordingHandler(HttpStatusCode.OK, response);
            using var httpClient = new HttpClient(handler);
            object api = Activator.CreateInstance(
                assembly.GetType("Generated.Phase4.Phase4Api", throwOnError: true)!, httpClient)!;
            var task = (Task)api.GetType().GetMethod("readValue")!
                .Invoke(api, new object[] { CancellationToken.None })!;

            await task;
            var values = ((System.Collections.IEnumerable)task.GetType().GetProperty("Result")!
                .GetValue(task)!).Cast<object>().ToArray();
            Assert.Equal(2, values.Length);
            Assert.Same(values[0], values[1]);
        }

        private static JObject ClosedObject(JObject properties, string? requiredProperty = null)
        {
            var schema = new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = properties
            };
            if (requiredProperty is not null)
            {
                schema["required"] = new JArray(requiredProperty);
            }

            return schema;
        }

        private static string CreateDocument(JToken? responseSchema, JObject components,
            JToken? requestSchema = null)
        {
            var operation = new JObject
            {
                ["operationId"] = requestSchema is null ? "readValue" : "sendValue",
                ["responses"] = new JObject
                {
                    [responseSchema is null ? "204" : "200"] = responseSchema is null
                        ? new JObject { ["description"] = "Done" }
                        : new JObject
                        {
                            ["description"] = "OK",
                            ["content"] = new JObject
                            {
                                ["application/json"] = new JObject { ["schema"] = responseSchema }
                            }
                        }
                }
            };
            if (requestSchema is not null)
            {
                operation["requestBody"] = new JObject
                {
                    ["required"] = true,
                    ["content"] = new JObject
                    {
                        ["application/json"] = new JObject { ["schema"] = requestSchema }
                    }
                };
            }

            return new JObject
            {
                ["openapi"] = "3.1.0",
                ["info"] = new JObject { ["title"] = "Metadata", ["version"] = "1" },
                ["servers"] = new JArray(new JObject { ["url"] = "https://example.test/" }),
                ["paths"] = new JObject { ["/value"] = new JObject
                {
                    [requestSchema is null ? "get" : "post"] = operation
                } },
                ["components"] = new JObject { ["schemas"] = components }
            }.ToString(Formatting.None);
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            private readonly string _response;

            internal RecordingHandler(HttpStatusCode status, string response)
            {
                _status = status;
                _response = response;
            }

            internal string? RequestBody { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                RequestBody = request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync();
                return new HttpResponseMessage(_status)
                {
                    Content = new StringContent(_response, Encoding.UTF8, "application/json")
                };
            }
        }
    }
}
