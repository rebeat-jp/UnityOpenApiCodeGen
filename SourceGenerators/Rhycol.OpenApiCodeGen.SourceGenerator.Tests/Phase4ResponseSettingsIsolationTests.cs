using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    [Collection("JsonConvert default settings isolation")]
    public sealed class Phase4ResponseSettingsIsolationTests
    {
        private const string IntegerDtoDocument = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Response settings"", ""version"": ""1"" },
  ""paths"": { ""/value"": { ""get"": {
    ""operationId"": ""readValue"",
    ""responses"": { ""200"": { ""description"": ""OK"", ""content"": {
      ""application/json"": { ""schema"": { ""$ref"": ""#/components/schemas/Value"" } }
    } } }
  } } },
  ""components"": { ""schemas"": { ""Value"": {
    ""type"": ""object"", ""additionalProperties"": false,
    ""required"": [""number""],
    ""properties"": { ""number"": { ""type"": ""integer"" } }
  } } }
}";

        private const string StringArrayDocument = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Response depth"", ""version"": ""1"" },
  ""paths"": { ""/value"": { ""get"": {
    ""operationId"": ""readValue"",
    ""responses"": { ""200"": { ""description"": ""OK"", ""content"": {
      ""application/json"": { ""schema"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } } }
    } } }
  } } }
}";

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task HostConverterOrContractResolverCannotChangeValidatedResponse(bool useResolver)
        {
            Assembly assembly = Compile(IntegerDtoDocument);
            Func<JsonSerializerSettings>? previous = JsonConvert.DefaultSettings;
            int settingsCalls = 0;
            try
            {
                JsonConvert.DefaultSettings = () =>
                {
                    settingsCalls++;
                    var settings = new JsonSerializerSettings();
                    if (useResolver)
                    {
                        settings.ContractResolver = new IgnoreNumberResolver();
                    }
                    else
                    {
                        settings.Converters.Add(new ZeroIntegerConverter());
                    }

                    return settings;
                };

                object result = (await Invoke(assembly, "{\"number\":42}"))!;
                Assert.Equal(42, result.GetType().GetProperty("Number")!.GetValue(result));
                Assert.Equal(1, settingsCalls);
            }
            finally
            {
                JsonConvert.DefaultSettings = previous;
            }
        }

        [Theory]
        [InlineData(null, 70)]
        [InlineData(4, 6)]
        public async Task ResponseDepthUsesFiniteFallbackOrPositiveHostLimit(int? maxDepth, int nesting)
        {
            Assembly assembly = Compile(StringArrayDocument);
            string response = new string('[', nesting) + "\"value\"" + new string(']', nesting);
            Func<JsonSerializerSettings>? previous = JsonConvert.DefaultSettings;
            try
            {
                JsonConvert.DefaultSettings = () => new JsonSerializerSettings { MaxDepth = maxDepth };
                JsonSerializationException error = await Assert.ThrowsAsync<JsonSerializationException>(
                    () => Invoke(assembly, response));
                Assert.Contains("not valid JSON", error.Message);
            }
            finally
            {
                JsonConvert.DefaultSettings = previous;
            }
        }

        [Fact]
        public async Task ConfiguredDepthNameMayAlsoBeAnOperationParameter()
        {
            JObject document = JObject.Parse(IntegerDtoDocument);
            document["paths"]!["/value"]!["get"]!["parameters"] = new JArray(new JObject
            {
                ["name"] = "configuredResponseMaxDepth",
                ["in"] = "query",
                ["required"] = true,
                ["schema"] = new JObject { ["type"] = "string" }
            });
            Assembly assembly = Compile(document.ToString(Formatting.None));

            object result = (await Invoke(assembly, "{\"number\":42}", "depth"))!;
            Assert.Equal(42, result.GetType().GetProperty("Number")!.GetValue(result));
        }

        private static Assembly Compile(string document)
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(document);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            return execution.EmitAssembly();
        }

        private static async Task<object?> Invoke(Assembly assembly, string body, string? queryValue = null)
        {
            using var client = new HttpClient(new FixedResponseHandler(body))
            {
                BaseAddress = new Uri("https://example.test/")
            };
            object api = Activator.CreateInstance(assembly.GetType("Generated.Phase4.Phase4Api")!, client)!;
            var task = (Task)api.GetType().GetMethod("readValue")!
                .Invoke(api, queryValue is null
                    ? new object[] { CancellationToken.None }
                    : new object[] { queryValue, CancellationToken.None })!;
            await task;
            return task.GetType().GetProperty("Result")!.GetValue(task);
        }

        private sealed class ZeroIntegerConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType) => objectType == typeof(int);

            public override object ReadJson(JsonReader reader, Type objectType, object? existingValue,
                JsonSerializer serializer) => 0;

            public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) =>
                writer.WriteValue(value);
        }

        private sealed class IgnoreNumberResolver : DefaultContractResolver
        {
            protected override JsonProperty CreateProperty(MemberInfo member,
                MemberSerialization memberSerialization)
            {
                JsonProperty property = base.CreateProperty(member, memberSerialization);
                if (property.PropertyName == "number")
                {
                    property.Ignored = true;
                }

                return property;
            }
        }

        private sealed class FixedResponseHandler : HttpMessageHandler
        {
            private readonly string _body;

            internal FixedResponseHandler(string body) => _body = body;

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json")
                });
        }
    }
}
