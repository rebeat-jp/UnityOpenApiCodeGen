using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    public sealed class Phase4RequestArrayNullTests
    {
        private const string StringArray = @"{ ""type"": ""array"", ""items"": { ""type"": ""string"" } }";
        private const string NullableStringArray = @"{ ""type"": ""array"", ""items"": { ""type"": [""string"", ""null""] } }";
        private const string NestedArray = @"{ ""type"": ""array"", ""items"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } } }";
        private const string Item = @"{ ""$ref"": ""#/components/schemas/Item"" }";
        private const string ItemArray = @"{ ""type"": ""array"", ""items"": { ""$ref"": ""#/components/schemas/Item"" } }";
        private const string NullableItemArray = @"{ ""type"": ""array"", ""items"": { ""$ref"": ""#/components/schemas/NullableItem"" } }";
        private const string Envelope = @"{ ""$ref"": ""#/components/schemas/Envelope"" }";
        private const string Components = @"{
  ""Item"": { ""type"": ""object"", ""additionalProperties"": false, ""properties"": {
    ""values"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } }
  } },
  ""NullableItem"": { ""type"": [""object"", ""null""], ""additionalProperties"": false, ""properties"": {
    ""values"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } }
  } },
  ""Envelope"": { ""type"": ""object"", ""additionalProperties"": false, ""properties"": {
    ""items"": { ""type"": ""array"", ""items"": { ""$ref"": ""#/components/schemas/Item"" } }
  } }
}";

        public static TheoryData<string, string, string?, bool, string?> Cases => new()
        {
            { StringArray, "", "[null]", true, "$[0]" },
            { StringArray, "", "[\"x\"]", true, null },
            { StringArray, "", "[]", true, null },
            { NullableStringArray, "", "[null]", true, null },
            { NestedArray, "", "[[\"x\",null]]", true, "$[0][1]" },
            { NestedArray, "", "[null]", true, "$[0]" },
            { Item, Components, "{\"values\":[null]}", true, "$.values[0]" },
            { Item, Components, "{\"values\":[\"x\"]}", true, null },
            { ItemArray, Components, "[null]", true, "$[0]" },
            { NullableItemArray, Components, "[null]", true, null },
            { Envelope, Components, "{\"items\":[{\"values\":[null]}]}", true, "$.items[0].values[0]" },
            { StringArray, "", null, false, null },
            { @"{ ""type"": [""array"", ""null""], ""items"": { ""type"": ""string"" } }", "", "null", true, null }
        };

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task RequestArraysAreValidatedBeforeSending(
            string schema,
            string components,
            string? bodyJson,
            bool required,
            string? rejectedPath)
        {
            Assembly assembly = GenerateAndCompile(schema, components, required);
            var handler = new CaptureHandler();
            using var httpClient = new HttpClient(handler);
            object api = Activator.CreateInstance(assembly.GetType("Generated.Phase4.Phase4Api")!, httpClient)!;
            MethodInfo method = api.GetType().GetMethod("sendBody")!;
            Type bodyType = method.GetParameters()[0].ParameterType;
            object? body = bodyJson is null
                ? null
                : JsonConvert.DeserializeObject(bodyJson, bodyType);
            var task = (Task)method.Invoke(api, new object?[] { body, CancellationToken.None })!;

            if (rejectedPath is not null)
            {
                JsonSerializationException exception = await Assert.ThrowsAsync<JsonSerializationException>(
                    async () => await task);
                Assert.Contains(rejectedPath, exception.Message);
                Assert.Equal(0, handler.SendCount);
            }
            else
            {
                await task;
                Assert.Equal(1, handler.SendCount);
                Assert.Equal(bodyJson is null ? null : JToken.Parse(bodyJson),
                    handler.Content is null ? null : JToken.Parse(handler.Content));
            }
        }

        private static Assembly GenerateAndCompile(string schema, string components, bool required)
        {
            var document = new JObject
            {
                ["openapi"] = "3.1.0",
                ["info"] = new JObject { ["title"] = "Request arrays", ["version"] = "1" },
                ["servers"] = new JArray(new JObject { ["url"] = "https://example.test/" }),
                ["paths"] = new JObject
                {
                    ["/body"] = new JObject
                    {
                        ["post"] = new JObject
                        {
                            ["operationId"] = "sendBody",
                            ["requestBody"] = new JObject
                            {
                                ["required"] = required,
                                ["content"] = new JObject
                                {
                                    ["application/json"] = new JObject
                                    {
                                        ["schema"] = JToken.Parse(schema)
                                    }
                                }
                            },
                            ["responses"] = new JObject
                            {
                                ["204"] = new JObject { ["description"] = "Done" }
                            }
                        }
                    }
                }
            };
            if (!string.IsNullOrEmpty(components))
            {
                document["components"] = new JObject { ["schemas"] = JObject.Parse(components) };
            }

            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                document.ToString(Formatting.None));
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            return execution.EmitAssembly();
        }

        private sealed class CaptureHandler : HttpMessageHandler
        {
            internal int SendCount { get; private set; }
            internal string? Content { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                SendCount++;
                Content = request.Content is null ? null : await request.Content.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
        }
    }
}
