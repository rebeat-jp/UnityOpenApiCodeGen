using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    public sealed class Phase4HttpClientRuntimeTests
    {
        private const string HttpDocument = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""HTTP"", ""version"": ""1"" },
  ""servers"": [ { ""url"": ""https://api.example.test/v1/"" } ],
  ""paths"": {
    ""/pets/{petId}"": {
      ""parameters"": [
        { ""name"": ""petId"", ""in"": ""path"", ""required"": true, ""schema"": { ""type"": ""string"" } }
      ],
      ""post"": {
        ""operationId"": ""sendPet"",
        ""parameters"": [
          { ""name"": ""limit"", ""in"": ""query"", ""schema"": { ""type"": ""integer"" } },
          { ""name"": ""X-Trace"", ""in"": ""header"", ""required"": true, ""schema"": { ""type"": ""string"" } }
        ],
        ""requestBody"": {
          ""required"": true,
          ""content"": { ""application/json"": { ""schema"": { ""$ref"": ""#/components/schemas/PetInput"" } } }
        },
        ""responses"": {
          ""201"": {
            ""description"": ""Created"",
            ""content"": { ""application/json"": { ""schema"": { ""$ref"": ""#/components/schemas/PetResponse"" } } }
          }
        }
      }
    }
  },
  ""components"": { ""schemas"": {
    ""PetInput"": {
      ""type"": ""object"",
      ""required"": [""name""],
      ""properties"": { ""name"": { ""type"": ""string"" } }
    },
    ""PetResponse"": {
      ""type"": ""object"",
      ""required"": [""id"", ""name""],
      ""properties"": {
        ""id"": { ""type"": ""integer"" },
        ""name"": { ""type"": ""string"" }
      }
    }
  } }
}";

        [Fact]
        public async Task GeneratedClientSendsPathQueryHeaderBodyAndDeserializesResponse()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(HttpDocument);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();
            var handler = new RecordingHandler(static (_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(
                        "{\"id\":42,\"name\":\"server\"}",
                        Encoding.UTF8,
                        "application/json")
                }));
            using var httpClient = new HttpClient(handler);
            object client = CreateClient(assembly, httpClient);
            object body = CreateRequestBody(assembly, "client");
            using var cancellationSource = new CancellationTokenSource();

            object? result = await InvokeSendPet(
                client,
                "trace-123",
                "a/b",
                3,
                body,
                cancellationSource.Token);

            Assert.Equal(HttpMethod.Post, handler.Method);
            Assert.Equal(
                "https://api.example.test/v1/pets/a%2Fb?limit=3",
                handler.RequestUri!.AbsoluteUri);
            Assert.Equal("trace-123", handler.TraceHeader);
            Assert.Contains("\"name\":\"client\"", handler.Content);
            Assert.Equal("application/json", handler.ContentType);
            Assert.True(handler.CancellationToken.CanBeCanceled);
            Assert.NotNull(result);
            Assert.Equal(42, result!.GetType().GetProperty("Id")!.GetValue(result));
            Assert.Equal("server", result.GetType().GetProperty("Name")!.GetValue(result));
        }

        [Fact]
        public async Task ExplicitBaseUrlOverridesDocumentServer()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(HttpDocument);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();
            var handler = new RecordingHandler(static (_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("{\"id\":1,\"name\":\"ok\"}")
                }));
            using var httpClient = new HttpClient(handler);
            object client = CreateClient(assembly, httpClient, "https://override.example/root");
            object body = CreateRequestBody(assembly, "client");

            await InvokeSendPet(client, "trace", "one", null, body, CancellationToken.None);

            Assert.Equal("https://override.example/root/pets/one", handler.RequestUri!.AbsoluteUri);
        }

        [Fact]
        public async Task UnexpectedStatusThrowsGeneratedExceptionWithResponseBody()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(HttpDocument);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();
            var handler = new RecordingHandler(static (_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
                {
                    Content = new StringContent("validation failed")
                }));
            using var httpClient = new HttpClient(handler);
            object client = CreateClient(assembly, httpClient);
            object body = CreateRequestBody(assembly, "client");

            Exception exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
                await InvokeSendPet(client, "trace", "one", null, body, CancellationToken.None));

            Assert.Equal("Phase4ApiException", exception.GetType().Name);
            Assert.Equal(
                HttpStatusCode.UnprocessableEntity,
                exception.GetType().GetProperty("StatusCode")!.GetValue(exception));
            Assert.Equal(
                "validation failed",
                exception.GetType().GetProperty("ResponseBody")!.GetValue(exception));
        }

        [Fact]
        public async Task DateDateTimeAndUuidQueryParametersUseInvariantWireFormats()
        {
            const string Document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Formatted query"", ""version"": ""1"" },
  ""servers"": [ { ""url"": ""https://api.example.test/"" } ],
  ""paths"": { ""/formatted"": { ""get"": {
    ""operationId"": ""getFormatted"",
    ""parameters"": [
      { ""name"": ""date"", ""in"": ""query"", ""required"": true, ""schema"": { ""type"": ""string"", ""format"": ""date"" } },
      { ""name"": ""dateTime"", ""in"": ""query"", ""required"": true, ""schema"": { ""type"": ""string"", ""format"": ""date-time"" } },
      { ""name"": ""id"", ""in"": ""query"", ""required"": true, ""schema"": { ""type"": ""string"", ""format"": ""uuid"" } }
    ],
    ""responses"": { ""204"": { ""description"": ""No Content"" } }
  } } }
}";
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(Document);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();
            var handler = new RecordingHandler(static (_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NoContent)));
            using var httpClient = new HttpClient(handler);
            object client = CreateClient(assembly, httpClient);
            MethodInfo method = client.GetType().GetMethod("getFormatted")!;
            var task = (Task)method.Invoke(
                client,
                new object[]
                {
                    new DateTime(2026, 8, 4),
                    new DateTimeOffset(2026, 8, 4, 12, 34, 56, TimeSpan.FromHours(9)),
                    Guid.Parse("01234567-89AB-CDEF-0123-456789ABCDEF"),
                    CancellationToken.None
                })!;

            await task;

            string decodedQuery = Uri.UnescapeDataString(handler.RequestUri!.Query.TrimStart('?'));
            Assert.Equal(
                "date=2026-08-04&dateTime=2026-08-04T12:34:56.0000000+09:00&" +
                "id=01234567-89ab-cdef-0123-456789abcdef",
                decodedQuery);
        }

        [Fact]
        public async Task CancellationTokenIsForwardedToHttpHandler()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(HttpDocument);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();
            var handler = new RecordingHandler(static (_, token) =>
                Task.FromCanceled<HttpResponseMessage>(token));
            using var httpClient = new HttpClient(handler);
            object client = CreateClient(assembly, httpClient);
            object body = CreateRequestBody(assembly, "client");
            using var cancellationSource = new CancellationTokenSource();
            cancellationSource.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await InvokeSendPet(
                    client,
                    "trace",
                    "one",
                    null,
                    body,
                    cancellationSource.Token));

            Assert.True(handler.CancellationToken.IsCancellationRequested);
        }

        private static object CreateClient(
            Assembly assembly,
            HttpClient httpClient,
            string? baseUrl = null)
        {
            Type type = assembly.GetType("Generated.Phase4.Phase4Api", throwOnError: true)!;
            return baseUrl is null
                ? Activator.CreateInstance(type, httpClient)!
                : Activator.CreateInstance(type, httpClient, baseUrl)!;
        }

        private static object CreateRequestBody(Assembly assembly, string name)
        {
            Type type = assembly.GetType("Generated.Phase4.PetInput", throwOnError: true)!;
            object body = Activator.CreateInstance(type)!;
            type.GetProperty("Name")!.SetValue(body, name);
            return body;
        }

        private static async Task<object?> InvokeSendPet(
            object client,
            string trace,
            string petId,
            int? limit,
            object body,
            CancellationToken cancellationToken)
        {
            MethodInfo method = client.GetType().GetMethod("sendPet")!;
            var task = (Task)method.Invoke(
                client,
                new object?[] { trace, petId, limit, body, cancellationToken })!;
            await task.ConfigureAwait(false);
            return task.GetType().GetProperty("Result")!.GetValue(task);
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _response;

            internal RecordingHandler(
                Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response)
            {
                _response = response;
            }

            internal HttpMethod? Method { get; private set; }

            internal Uri? RequestUri { get; private set; }

            internal string? TraceHeader { get; private set; }

            internal string Content { get; private set; } = string.Empty;

            internal string? ContentType { get; private set; }

            internal CancellationToken CancellationToken { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                Method = request.Method;
                RequestUri = request.RequestUri;
                TraceHeader = request.Headers.Contains("X-Trace")
                    ? string.Join(",", request.Headers.GetValues("X-Trace"))
                    : null;
                Content = request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync().ConfigureAwait(false);
                ContentType = request.Content?.Headers.ContentType?.MediaType;
                CancellationToken = cancellationToken;
                return await _response(request, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
