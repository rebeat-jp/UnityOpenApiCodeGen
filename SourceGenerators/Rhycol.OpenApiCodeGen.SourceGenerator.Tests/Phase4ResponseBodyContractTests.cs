using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    public sealed class Phase4ResponseBodyContractTests
    {
        [Theory]
        [InlineData("", false)]
        [InlineData("   ", false)]
        [InlineData("null", false)]
        [InlineData(" \nnull\t", false)]
        [InlineData("null", true)]
        public async Task ResponseNullabilityIsEnforcedAtRuntime(string responseBody, bool nullable)
        {
            string document = DocumentWithResponseSchema(nullable
                ? "{ \"type\": [\"integer\", \"null\"] }"
                : "{ \"type\": \"integer\" }");
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(document);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();
            using var client = new HttpClient(new CaptureHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
                }));
            object api = Activator.CreateInstance(assembly.GetType("Generated.Phase4.Phase4Api")!, client)!;
            var task = (Task)api.GetType().GetMethod("readValue")!.Invoke(api, new object[] { CancellationToken.None })!;

            if (nullable)
            {
                await task;
                Assert.Null(task.GetType().GetProperty("Result")!.GetValue(task));
            }
            else
            {
                await Assert.ThrowsAsync<JsonSerializationException>(async () => await task);
            }
        }

        [Theory]
        [InlineData("", true)]
        [InlineData("null", true)]
        [InlineData("{}", false)]
        public async Task NonNullableDtoResponseRequiresAnObjectBody(
            string responseBody,
            bool throws)
        {
            const string Document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""DTO response"", ""version"": ""1"" },
  ""servers"": [{ ""url"": ""https://example.test/"" }],
  ""paths"": { ""/value"": { ""get"": {
    ""operationId"": ""readValue"",
    ""responses"": { ""200"": { ""description"": ""OK"", ""content"": {
      ""application/json"": { ""schema"": { ""$ref"": ""#/components/schemas/Value"" } }
    } } }
  } } },
  ""components"": { ""schemas"": {
    ""Value"": { ""type"": ""object"", ""properties"": {
      ""name"": { ""type"": ""string"" }
    } }
  } }
}";
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(Document);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();
            using var client = new HttpClient(new CaptureHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
                }));
            object api = Activator.CreateInstance(assembly.GetType("Generated.Phase4.Phase4Api")!, client)!;
            var task = (Task)api.GetType().GetMethod("readValue")!
                .Invoke(api, new object[] { CancellationToken.None })!;

            if (throws)
            {
                await Assert.ThrowsAsync<JsonSerializationException>(async () => await task);
            }
            else
            {
                await task;
                Assert.NotNull(task.GetType().GetProperty("Result")!.GetValue(task));
            }
        }

        [Theory]
        [InlineData(false, null, null)]
        [InlineData(false, "hello", "\"hello\"")]
        [InlineData(true, null, "null")]
        public async Task OptionalNullableBodyHasOmittedValueAndExplicitNullStates(
            bool bodySpecified,
            string? body,
            string? expectedJson)
        {
            const string Document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Body"", ""version"": ""1"" },
  ""servers"": [{ ""url"": ""https://example.test/"" }],
  ""paths"": { ""/body"": { ""post"": {
    ""operationId"": ""sendBody"",
    ""requestBody"": { ""content"": {
      ""application/json"": { ""schema"": { ""type"": [""string"", ""null""] } }
    } },
    ""responses"": { ""204"": { ""description"": ""Done"" } }
  } } }
}";
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(Document);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();
            var handler = new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
            using var client = new HttpClient(handler);
            object api = Activator.CreateInstance(assembly.GetType("Generated.Phase4.Phase4Api")!, client)!;
            MethodInfo method = api.GetType().GetMethod("sendBody")!;
            ParameterInfo[] parameters = method.GetParameters();
            Assert.Equal("body", parameters[0].Name);
            Assert.Equal("cancellationToken", parameters[1].Name);
            Assert.Equal("bodySpecified", parameters[2].Name);
            Assert.True(parameters[2].IsOptional);

            await (Task)method.Invoke(api, new object?[] { body, CancellationToken.None, bodySpecified })!;

            Assert.Equal(expectedJson, handler.RequestBody);
            Assert.Equal(expectedJson is null ? null : "application/json", handler.ContentType);
        }

        [Fact]
        public void BodyPresenceParameterAvoidsExistingOperationParameterName()
        {
            const string Document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Body collision"", ""version"": ""1"" },
  ""paths"": { ""/body"": { ""post"": {
    ""operationId"": ""sendBody"",
    ""parameters"": [{ ""name"": ""bodySpecified"", ""in"": ""query"",
      ""schema"": { ""type"": ""boolean"" } }],
    ""requestBody"": { ""content"": {
      ""application/json"": { ""schema"": { ""type"": [""string"", ""null""] } }
    } },
    ""responses"": { ""204"": { ""description"": ""Done"" } }
  } } }
}";
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(Document);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();
            Type api = assembly.GetType("Generated.Phase4.Phase4Api")!;
            Assert.Equal(new[] { "bodySpecified", "body", "cancellationToken", "bodySpecified2" },
                api.GetMethod("sendBody")!.GetParameters().Select(parameter => parameter.Name));
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, true)]
        [InlineData(true, false)]
        public void AliasResponsesCompareTerminalSchemaAndEffectiveNullability(
            bool firstAliasNullable,
            bool secondAliasNullable)
        {
            string document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Aliases"", ""version"": ""1"" },
  ""paths"": { ""/value"": { ""get"": {
    ""operationId"": ""readValue"",
    ""responses"": {
      ""200"": { ""description"": ""First"", ""content"": {
        ""application/json"": { ""schema"": { ""$ref"": ""#/components/schemas/First"" } }
      } },
      ""201"": { ""description"": ""Second"", ""content"": {
        ""application/json"": { ""schema"": { ""$ref"": ""#/components/schemas/Second"" } }
      } }
    }
  } } },
  ""components"": { ""schemas"": {
    ""First"": { ""$ref"": ""#/components/schemas/FirstMiddle"" },
    ""Second"": { ""$ref"": ""#/components/schemas/SecondMiddle"" },
    ""FirstMiddle"": { ""$ref"": ""#/components/schemas/FIRST_REF"" },
    ""SecondMiddle"": { ""$ref"": ""#/components/schemas/SECOND_REF"" },
    ""Terminal"": { ""type"": ""integer"" },
    ""NullableTerminal"": { ""type"": [""integer"", ""null""] }
  } }
}".Replace("FIRST_REF", firstAliasNullable ? "NullableTerminal" : "Terminal")
  .Replace("SECOND_REF", secondAliasNullable ? "NullableTerminal" : "Terminal");
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(document);
            if (firstAliasNullable == secondAliasNullable)
            {
                Assert.Empty(execution.RunResult.Diagnostics);
                Assert.Empty(execution.CompilationErrors);
            }
            else
            {
                Assert.Contains(execution.RunResult.Diagnostics,
                    diagnostic => diagnostic.GetMessage().Contains("same JSON body contract"));
            }
        }

        [Fact]
        public void DifferentNamedResponseTypesRemainInconsistent()
        {
            const string Document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Named aliases"", ""version"": ""1"" },
  ""paths"": { ""/value"": { ""get"": {
    ""operationId"": ""readValue"",
    ""responses"": {
      ""200"": { ""description"": ""First"", ""content"": {
        ""application/json"": { ""schema"": { ""$ref"": ""#/components/schemas/FirstAlias"" } }
      } },
      ""201"": { ""description"": ""Second"", ""content"": {
        ""application/json"": { ""schema"": { ""$ref"": ""#/components/schemas/SecondAlias"" } }
      } }
    }
  } } },
  ""components"": { ""schemas"": {
    ""FirstAlias"": { ""$ref"": ""#/components/schemas/FirstType"" },
    ""SecondAlias"": { ""$ref"": ""#/components/schemas/SecondType"" },
    ""FirstType"": { ""type"": ""object"", ""properties"": { ""id"": { ""type"": ""integer"" } } },
    ""SecondType"": { ""type"": ""object"", ""properties"": { ""id"": { ""type"": ""integer"" } } }
  } }
}";
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(Document);
            Assert.Contains(execution.RunResult.Diagnostics,
                diagnostic => diagnostic.GetMessage().Contains("same JSON body contract"));
        }

        private static string DocumentWithResponseSchema(string schema) => @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Response"", ""version"": ""1"" },
  ""servers"": [{ ""url"": ""https://example.test/"" }],
  ""paths"": { ""/value"": { ""get"": {
    ""operationId"": ""readValue"",
    ""responses"": { ""200"": { ""description"": ""OK"", ""content"": {
      ""application/json"": { ""schema"": " + schema + @" }
    } } }
  } } }
}";

        private sealed class CaptureHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

            internal CaptureHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            {
                _respond = respond;
            }

            internal string? RequestBody { get; private set; }
            internal string? ContentType { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                RequestBody = request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync();
                ContentType = request.Content?.Headers.ContentType?.MediaType;
                return _respond(request);
            }
        }
    }
}
