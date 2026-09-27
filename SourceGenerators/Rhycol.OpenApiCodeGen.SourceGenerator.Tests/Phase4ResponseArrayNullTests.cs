using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    [CollectionDefinition("JsonConvert default settings isolation", DisableParallelization = true)]
    public sealed class JsonConvertDefaultSettingsIsolationCollection
    {
    }

    [Collection("JsonConvert default settings isolation")]
    public sealed class Phase4ResponseArrayNullTests
    {
        private const string StringArray = @"{ ""type"": ""array"", ""items"": { ""type"": ""string"" } }";
        private const string NullableStringArray = @"{ ""type"": ""array"", ""items"": { ""type"": [""string"", ""null""] } }";
        private const string NestedArray = @"{ ""type"": ""array"", ""items"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } } }";
        private const string EnvelopeRef = @"{ ""$ref"": ""#/components/schemas/Envelope"" }";
        private const string EnvelopeSchema = @"{ ""Envelope"": { ""type"": ""object"", ""additionalProperties"": false, ""properties"": { ""values"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } } } } }";
        private const string DtoArray = @"{ ""type"": ""array"", ""items"": { ""$ref"": ""#/components/schemas/Item"" } }";
        private const string NullableDtoArray = @"{ ""type"": ""array"", ""items"": { ""$ref"": ""#/components/schemas/NullableItem"" } }";
        private const string ItemSchemas = @"{ ""Item"": { ""type"": ""object"", ""additionalProperties"": false, ""properties"": { ""name"": { ""type"": ""string"" } } }, ""NullableItem"": { ""type"": [""object"", ""null""], ""additionalProperties"": false, ""properties"": { ""name"": { ""type"": ""string"" } } } }";
        private const string EnvelopeArray = @"{ ""type"": ""array"", ""items"": { ""$ref"": ""#/components/schemas/Envelope"" } }";

        public static TheoryData<string, string, string, string?> Cases => new()
        {
            { StringArray, "", "[null]", "$[0]" },
            { StringArray, "", "[\"x\"]", null },
            { NullableStringArray, "", "[null]", null },
            { NestedArray, "", "[[\"x\",null]]", "$[0][1]" },
            { NestedArray, "", "[null]", "$[0]" },
            { EnvelopeRef, EnvelopeSchema, "{\"values\":[null]}", "$.values[0]" },
            { EnvelopeRef, EnvelopeSchema, "{}", null },
            { DtoArray, ItemSchemas, "[null]", "$[0]" },
            { NullableDtoArray, ItemSchemas, "[null]", null },
            { EnvelopeArray, EnvelopeSchema, "[{\"values\":[null]}]", "$[0].values[0]" },
            { EnvelopeArray, EnvelopeSchema, "[{\"values\":[\"x\"]}]", null },
            { @"{ ""type"": [""array"", ""null""], ""items"": { ""type"": ""string"" } }", "", "null", null }
        };

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task GeneratedResponseRejectsOnlyNonNullableArrayElements(
            string schema,
            string components,
            string responseBody,
            string? rejectedPath)
        {
            Assembly assembly = GenerateAndCompile(schema, components);
            await AssertResponse(assembly, responseBody, rejectedPath);
        }

        [Theory]
        [InlineData(StringArray, "", "[null]", "$[0]")]
        [InlineData(EnvelopeRef, EnvelopeSchema, "{\"values\":[null]}", "$.values[0]")]
        public async Task GlobalNullValueHandlingIgnoreCannotHideInvalidArrayElements(
            string schema,
            string components,
            string responseBody,
            string rejectedPath)
        {
            Assembly assembly = GenerateAndCompile(schema, components);
            Func<JsonSerializerSettings>? previous = JsonConvert.DefaultSettings;
            try
            {
                JsonConvert.DefaultSettings = () => new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                };
                await AssertResponse(assembly, responseBody, rejectedPath);
            }
            finally
            {
                JsonConvert.DefaultSettings = previous;
            }
        }

        [Fact]
        public async Task ValidatorMethodNameDoesNotConflictWithOperationParameter()
        {
            Assembly assembly = GenerateAndCompile(
                StringArray,
                string.Empty,
                queryParameterName: "__OacgResponseValidator_0");
            using var httpClient = new HttpClient(new FixedResponseHandler("[\"x\"]"));
            object api = Activator.CreateInstance(assembly.GetType("Generated.Phase4.Phase4Api")!, httpClient)!;
            var task = (Task)api.GetType().GetMethod("readValue")!
                .Invoke(api, new object?[] { "q", CancellationToken.None })!;

            await task;
            Assert.NotNull(task.GetType().GetProperty("Result")!.GetValue(task));
        }

        [Fact]
        public async Task ValidatorComparerNameDoesNotHideDtoType()
        {
            const string schema = @"{ ""$ref"": ""#/components/schemas/__OacgResponseValidator_Comparer"" }";
            const string components = @"{ ""__OacgResponseValidator_Comparer"": {
  ""type"": ""object"", ""additionalProperties"": false, ""properties"": {
    ""values"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } }
  }
} }";
            Assembly assembly = GenerateAndCompile(schema, components);

            await AssertResponse(assembly, "{\"values\":[null]}", "$.values[0]");
            await AssertResponse(assembly, "{\"values\":[\"x\"]}", null);
        }

        [Fact]
        public void SharedDtoDagWithoutArraysCompilesWithoutResponseValidators()
        {
            var components = new JObject();
            for (int index = 0; index < 32; index++)
            {
                string nextReference = "#/components/schemas/Node" + (index + 1);
                components["Node" + index] = new JObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["properties"] = new JObject
                    {
                        ["left"] = new JObject { ["$ref"] = nextReference },
                        ["right"] = new JObject { ["$ref"] = nextReference }
                    }
                };
            }

            components["Node32"] = new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["value"] = new JObject { ["type"] = "string" }
                }
            };

            Assembly assembly = GenerateAndCompile(
                @"{ ""$ref"": ""#/components/schemas/Node0"" }",
                components.ToString(Formatting.None));
            Type api = assembly.GetType("Generated.Phase4.Phase4Api")!;

            Assert.NotNull(api);
            Assert.DoesNotContain(api.GetMethods(BindingFlags.NonPublic | BindingFlags.Static),
                method => method.Name.StartsWith("__OacgResponseValidator_", StringComparison.Ordinal));
        }

        [Fact]
        public async Task SharedDtoDagResponseIsValidatedOncePerObjectAndContract()
        {
            const int depth = 26;
            var components = new JObject();
            for (int index = 0; index < depth; index++)
            {
                string nextReference = "#/components/schemas/Node" + (index + 1);
                components["Node" + index] = new JObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["properties"] = new JObject
                    {
                        ["left"] = new JObject { ["$ref"] = nextReference },
                        ["right"] = new JObject { ["$ref"] = nextReference }
                    }
                };
            }

            components["Node" + depth] = new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["values"] = JToken.Parse(StringArray)
                }
            };

            JObject response = new JObject
            {
                ["$id"] = depth.ToString(),
                ["values"] = new JArray("x")
            };
            for (int index = depth - 1; index >= 0; index--)
            {
                response = new JObject
                {
                    ["$id"] = index.ToString(),
                    ["left"] = response,
                    ["right"] = new JObject { ["$ref"] = (index + 1).ToString() }
                };
            }

            Assembly assembly = GenerateAndCompile(
                @"{ ""$ref"": ""#/components/schemas/Node0"" }",
                components.ToString(Formatting.None));
            using var httpClient = new HttpClient(new FixedResponseHandler(response.ToString(Formatting.None)));
            object api = Activator.CreateInstance(assembly.GetType("Generated.Phase4.Phase4Api")!, httpClient)!;
            var task = (Task)api.GetType().GetMethod("readValue")!
                .Invoke(api, new object[] { CancellationToken.None })!;

            await task;
            object node = task.GetType().GetProperty("Result")!.GetValue(task)!;
            for (int index = 0; index < depth; index++)
            {
                object left = node.GetType().GetProperty("Left")!.GetValue(node)!;
                object right = node.GetType().GetProperty("Right")!.GetValue(node)!;
                Assert.Same(left, right);
                node = left;
            }

            Assert.NotNull(node.GetType().GetProperty("Values")!.GetValue(node));
        }

        [Fact]
        public async Task SharedListIsCheckedUnderBothNullableAndNonNullableItemContracts()
        {
            const string schema = @"{ ""$ref"": ""#/components/schemas/Envelope"" }";
            const string components = @"{ ""Envelope"": {
  ""type"": ""object"", ""additionalProperties"": false, ""properties"": {
    ""aPermissive"": { ""type"": ""array"", ""items"": {
      ""type"": [""array"", ""null""], ""items"": { ""type"": ""string"" }
    } },
    ""zStrict"": { ""type"": ""array"", ""items"": {
      ""type"": ""array"", ""items"": { ""type"": ""string"" }
    } }
  }
} }";
            Assembly assembly = GenerateAndCompile(schema, components);
            const string response = @"{ ""aPermissive"": { ""$id"": ""shared"", ""$values"": [null] },
  ""zStrict"": { ""$ref"": ""shared"" } }";

            await AssertResponse(assembly, response, "$.zStrict[0]");
        }

        private static Assembly GenerateAndCompile(
            string schema,
            string components,
            string? queryParameterName = null)
        {
            var operation = new JObject
            {
                ["operationId"] = "readValue",
                ["responses"] = new JObject
                {
                    ["200"] = new JObject
                    {
                        ["description"] = "OK",
                        ["content"] = new JObject
                        {
                            ["application/json"] = new JObject
                            {
                                ["schema"] = JToken.Parse(schema)
                            }
                        }
                    }
                }
            };
            if (queryParameterName is not null)
            {
                operation["parameters"] = new JArray(new JObject
                {
                    ["name"] = queryParameterName,
                    ["in"] = "query",
                    ["required"] = true,
                    ["schema"] = new JObject { ["type"] = "string" }
                });
            }

            var document = new JObject
            {
                ["openapi"] = "3.1.0",
                ["info"] = new JObject { ["title"] = "Array responses", ["version"] = "1" },
                ["servers"] = new JArray(new JObject { ["url"] = "https://example.test/" }),
                ["paths"] = new JObject
                {
                    ["/value"] = new JObject
                    {
                        ["get"] = operation
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

        private static async Task AssertResponse(
            Assembly assembly,
            string responseBody,
            string? rejectedPath)
        {
            using var httpClient = new HttpClient(new FixedResponseHandler(responseBody));
            object api = Activator.CreateInstance(assembly.GetType("Generated.Phase4.Phase4Api")!, httpClient)!;
            var task = (Task)api.GetType().GetMethod("readValue")!
                .Invoke(api, new object[] { CancellationToken.None })!;

            if (rejectedPath is not null)
            {
                JsonSerializationException exception = await Assert.ThrowsAsync<JsonSerializationException>(
                    async () => await task);
                Assert.Contains(rejectedPath, exception.Message);
            }
            else
            {
                await task;
                object? result = task.GetType().GetProperty("Result")!.GetValue(task);
                Assert.Equal(responseBody == "null", result is null);
            }
        }

        private sealed class FixedResponseHandler : HttpMessageHandler
        {
            private readonly string _body;

            internal FixedResponseHandler(string body)
            {
                _body = body;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json")
                });
            }
        }
    }
}
