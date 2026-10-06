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
    [Collection("JsonConvert default settings isolation")]
    public sealed class Phase4RequestSettingsIsolationTests
    {
        private const string RequestDocument = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Request settings"", ""version"": ""1"" },
  ""paths"": { ""/payload"": { ""post"": {
    ""operationId"": ""sendPayload"",
    ""requestBody"": { ""required"": true, ""content"": {
      ""application/json"": { ""schema"": { ""$ref"": ""#/components/schemas/Payload"" } }
    } },
    ""responses"": { ""204"": { ""description"": ""Done"" } }
  } } },
  ""components"": { ""schemas"": {
    ""Payload"": { ""type"": ""object"", ""additionalProperties"": false,
      ""required"": [""day"", ""child"", ""nullableName""],
      ""properties"": {
        ""day"": { ""type"": ""string"", ""format"": ""date"" },
        ""child"": { ""$ref"": ""#/components/schemas/Child"" },
        ""nullableName"": { ""type"": [""string"", ""null""] },
        ""optionalName"": { ""type"": [""string"", ""null""] }
      }
    },
    ""Child"": { ""type"": ""object"", ""additionalProperties"": false,
      ""required"": [""name""], ""properties"": { ""name"": { ""type"": ""string"" } } }
  } }
}";

        private const string BodylessDocument = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Bodyless"", ""version"": ""1"" },
  ""paths"": { ""/status"": { ""get"": {
    ""operationId"": ""getStatus"",
    ""responses"": { ""204"": { ""description"": ""Done"" } }
  } } }
}";

        private const string OptionalQueryDocument = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Optional query"", ""version"": ""1"" },
  ""paths"": { ""/status"": { ""get"": {
    ""operationId"": ""getStatus"",
    ""parameters"": [
      { ""name"": ""name"", ""in"": ""query"", ""schema"": { ""type"": ""string"" } }
    ],
    ""responses"": { ""204"": { ""description"": ""Done"" } }
  } } }
}";

        [Fact]
        public async Task RequestJsonIgnoresHostDefaultMetadataSettingsAndKeepsDateAndSpecifiedNull()
        {
            Assembly assembly = Compile(RequestDocument);
            object payload = Activator.CreateInstance(assembly.GetType("Generated.Phase4.Payload", true)!)!;
            object child = Activator.CreateInstance(assembly.GetType("Generated.Phase4.Child", true)!)!;
            child.GetType().GetProperty("Name")!.SetValue(child, "nested");
            payload.GetType().GetProperty("Day")!.SetValue(payload, new DateTime(2026, 8, 4, 15, 30, 0));
            payload.GetType().GetProperty("Child")!.SetValue(payload, child);
            payload.GetType().GetProperty("OptionalName")!.SetValue(payload, null);
            var handler = new RecordingHandler(null);
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
            object api = Activator.CreateInstance(assembly.GetType("Generated.Phase4.Phase4Api")!, http)!;
            Func<JsonSerializerSettings>? previous = JsonConvert.DefaultSettings;
            int defaultsInvoked = 0;
            try
            {
                JsonConvert.DefaultSettings = () =>
                {
                    defaultsInvoked++;
                    return new JsonSerializerSettings
                    {
                        TypeNameHandling = TypeNameHandling.Objects,
                        PreserveReferencesHandling = PreserveReferencesHandling.Objects,
                        NullValueHandling = NullValueHandling.Ignore,
                        DateFormatString = "yyyy"
                    };
                };

                await Invoke(api, "sendPayload", payload, CancellationToken.None);
                Assert.Equal(0, defaultsInvoked);
            }
            finally
            {
                JsonConvert.DefaultSettings = previous;
            }

            Assert.Equal(1, handler.SendCount);
            string sent = Assert.IsType<string>(handler.RequestBody);
            JObject json = JObject.Parse(sent);
            Assert.Equal("2026-08-04", (string?)json["day"]);
            Assert.Equal("nested", (string?)json["child"]?["name"]);
            Assert.Equal(JTokenType.Null, json["nullableName"]?.Type);
            Assert.Equal(JTokenType.Null, json["optionalName"]?.Type);
            Assert.DoesNotContain("$type", sent, StringComparison.Ordinal);
            Assert.DoesNotContain("$id", sent, StringComparison.Ordinal);
            Assert.DoesNotContain("$ref", sent, StringComparison.Ordinal);
        }

        [Fact]
        public async Task BodylessSuccessIgnoresContentTypeWhenNoMediaIsDeclared()
        {
            Assembly assembly = Compile(BodylessDocument);
            var handler = new RecordingHandler("text/plain");
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
            object api = Activator.CreateInstance(assembly.GetType("Generated.Phase4.Phase4Api")!, http)!;

            await Invoke(api, "getStatus", CancellationToken.None);

            Assert.Equal(1, handler.SendCount);
        }

        [Fact]
        public async Task OptionalQueryOmitsNullButSendsExplicitEmptyString()
        {
            Assembly assembly = Compile(OptionalQueryDocument);
            var handler = new RecordingHandler(null);
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
            object api = Activator.CreateInstance(assembly.GetType("Generated.Phase4.Phase4Api")!, http)!;

            await Invoke(api, "getStatus", null!, CancellationToken.None);
            Assert.Equal("https://example.test/status", handler.RequestUri!.AbsoluteUri);

            await Invoke(api, "getStatus", string.Empty, CancellationToken.None);
            Assert.Equal("https://example.test/status?name=", handler.RequestUri!.AbsoluteUri);
            Assert.Equal(2, handler.SendCount);
        }

        private static Assembly Compile(string document)
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(document);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            return execution.EmitAssembly();
        }

        private static async Task Invoke(object api, string method, params object[] args)
        {
            var task = (Task)api.GetType().GetMethod(method)!.Invoke(api, args)!;
            await task;
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly string? _contentType;

            internal RecordingHandler(string? contentType)
            {
                _contentType = contentType;
            }

            internal int SendCount { get; private set; }

            internal string? RequestBody { get; private set; }

            internal Uri? RequestUri { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                SendCount++;
                RequestUri = request.RequestUri;
                RequestBody = request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync();
                var response = new HttpResponseMessage(HttpStatusCode.NoContent)
                {
                    Content = new ByteArrayContent(Array.Empty<byte>())
                };
                if (_contentType is not null)
                {
                    response.Content.Headers.TryAddWithoutValidation("Content-Type", _contentType);
                }

                return response;
            }
        }
    }
}
