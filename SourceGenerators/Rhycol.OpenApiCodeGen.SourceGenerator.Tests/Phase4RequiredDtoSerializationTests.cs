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
    public sealed class Phase4RequiredDtoSerializationTests
    {
        private const string Document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Required DTO"", ""version"": ""1"" },
  ""servers"": [{ ""url"": ""https://api.example.test/"" }],
  ""paths"": { ""/payload"": { ""post"": {
    ""operationId"": ""sendPayload"",
    ""requestBody"": { ""required"": true, ""content"": {
      ""application/json"": { ""schema"": { ""$ref"": ""#/components/schemas/Outer"" } }
    } },
    ""responses"": { ""204"": { ""description"": ""Done"" } }
  } } },
  ""components"": { ""schemas"": {
    ""Outer"": {
      ""type"": ""object"",
      ""required"": [""name"", ""child"", ""nullableName"", ""count"", ""enabled"", ""tags""],
      ""properties"": {
        ""name"": { ""type"": ""string"" },
        ""child"": { ""$ref"": ""#/components/schemas/Child"" },
        ""nullableName"": { ""type"": [""string"", ""null""] },
        ""count"": { ""type"": ""integer"" },
        ""enabled"": { ""type"": ""boolean"" },
        ""tags"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } },
        ""optionalLabel"": { ""type"": [""string"", ""null""] },
        ""plain"": { ""type"": ""string"" },
        ""validateRequiredPropertiesOnSerializing"": { ""type"": ""string"" }
      }
    },
    ""Child"": { ""type"": ""object"", ""required"": [""token""],
      ""properties"": { ""token"": { ""type"": ""string"" } } }
  } }
}";

        [Fact]
        public async Task MissingRequiredReferenceStopsHttpSendForRootAndNestedDto()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(Document);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();
            var handler = new RecordingHandler();
            using var httpClient = new HttpClient(handler);
            object client = CreateClient(assembly, httpClient);
            object outer = CreateOuter(assembly);
            var ignoreSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore
            };

            TargetInvocationException configuredError = Assert.Throws<TargetInvocationException>(
                () => JsonConvert.SerializeObject(outer, ignoreSettings));
            Assert.Contains("'name'", Assert.IsType<JsonSerializationException>(configuredError.InnerException).Message);

            TargetInvocationException rootError = await Assert.ThrowsAsync<TargetInvocationException>(
                () => Send(client, outer));
            Assert.Contains("'name'", Assert.IsType<JsonSerializationException>(rootError.InnerException).Message);
            Assert.Equal(0, handler.SendCount);

            outer.GetType().GetProperty("Name")!.SetValue(outer, "ready");
            TargetInvocationException nestedError = await Assert.ThrowsAsync<TargetInvocationException>(
                () => Send(client, outer));
            Assert.Contains("'token'", Assert.IsType<JsonSerializationException>(nestedError.InnerException).Message);
            Assert.Equal(0, handler.SendCount);
        }

        [Fact]
        public async Task RequiredNullableAndDefaultValueFieldsSurviveJsonSettingsAndHttpSend()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(Document);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();
            object outer = CreateOuter(assembly);
            outer.GetType().GetProperty("Name")!.SetValue(outer, "ready");
            object child = outer.GetType().GetProperty("Child")!.GetValue(outer)!;
            child.GetType().GetProperty("Token")!.SetValue(child, "nested");
            var ignoreSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore
            };

            JObject configuredJson = JObject.Parse(JsonConvert.SerializeObject(outer, ignoreSettings));
            Assert.Equal(JTokenType.Null, configuredJson["nullableName"]!.Type);
            Assert.Equal(0, (int?)configuredJson["count"]);
            Assert.False((bool?)configuredJson["enabled"]);
            Assert.Empty(Assert.IsType<JArray>(configuredJson["tags"]));
            Assert.False(configuredJson.ContainsKey("optionalLabel"));
            Assert.False(configuredJson.ContainsKey("plain"));

            outer.GetType().GetProperty("OptionalLabel")!.SetValue(outer, null);
            JObject explicitOptionalNull = JObject.Parse(JsonConvert.SerializeObject(outer, ignoreSettings));
            Assert.Equal(JTokenType.Null, explicitOptionalNull["optionalLabel"]!.Type);

            var handler = new RecordingHandler();
            using var httpClient = new HttpClient(handler);
            object client = CreateClient(assembly, httpClient);
            await Send(client, outer);

            Assert.Equal(1, handler.SendCount);
            JObject sentJson = JObject.Parse(handler.Body!);
            Assert.Equal("nested", (string?)sentJson["child"]?["token"]);
            Assert.Equal(JTokenType.Null, sentJson["nullableName"]!.Type);
            Assert.Equal(0, (int?)sentJson["count"]);
            Assert.False((bool?)sentJson["enabled"]);
            Assert.Empty(Assert.IsType<JArray>(sentJson["tags"]));
            Assert.Equal(JTokenType.Null, sentJson["optionalLabel"]!.Type);
        }

        [Fact]
        public async Task RequiredArrayRejectsMissingValueBeforeHttpAndAcceptsEmptyArray()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(Document);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();
            object outer = CreateOuter(assembly, initializeTags: false);
            outer.GetType().GetProperty("Name")!.SetValue(outer, "ready");
            object child = outer.GetType().GetProperty("Child")!.GetValue(outer)!;
            child.GetType().GetProperty("Token")!.SetValue(child, "nested");
            PropertyInfo tagsProperty = outer.GetType().GetProperty("Tags")!;
            Assert.Null(tagsProperty.GetValue(outer));
            var handler = new RecordingHandler();
            using var httpClient = new HttpClient(handler);
            object client = CreateClient(assembly, httpClient);

            TargetInvocationException error = await Assert.ThrowsAsync<TargetInvocationException>(
                () => Send(client, outer));
            Assert.Contains("'tags'", Assert.IsType<JsonSerializationException>(error.InnerException).Message);
            Assert.Equal(0, handler.SendCount);

            tagsProperty.SetValue(outer, Activator.CreateInstance(tagsProperty.PropertyType));
            await Send(client, outer);

            Assert.Equal(1, handler.SendCount);
            JObject sentJson = JObject.Parse(handler.Body!);
            Assert.Empty(Assert.IsType<JArray>(sentJson["tags"]));
        }

        [Fact]
        public void SerializationCallbackNameAvoidsSchemaProperty()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(Document);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Type outerType = execution.EmitAssembly().GetType("Generated.Phase4.Outer", true)!;

            Assert.NotNull(outerType.GetProperty("ValidateRequiredPropertiesOnSerializing"));
            MethodInfo callback = outerType.GetMethod(
                "ValidateRequiredPropertiesOnSerializing2",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.NotNull(callback);
            Assert.NotNull(callback.GetCustomAttribute<System.Runtime.Serialization.OnSerializingAttribute>());
        }

        private static object CreateOuter(Assembly assembly, bool initializeTags = true)
        {
            Type outerType = assembly.GetType("Generated.Phase4.Outer", true)!;
            Type childType = assembly.GetType("Generated.Phase4.Child", true)!;
            object outer = Activator.CreateInstance(outerType)!;
            outerType.GetProperty("Child")!.SetValue(outer, Activator.CreateInstance(childType));
            if (initializeTags)
            {
                PropertyInfo tagsProperty = outerType.GetProperty("Tags")!;
                tagsProperty.SetValue(outer, Activator.CreateInstance(tagsProperty.PropertyType));
            }
            return outer;
        }

        private static object CreateClient(Assembly assembly, HttpClient httpClient)
        {
            return Activator.CreateInstance(
                assembly.GetType("Generated.Phase4.Phase4Api", true)!, httpClient)!;
        }

        private static async Task Send(object client, object outer)
        {
            var task = (Task)client.GetType().GetMethod("sendPayload")!
                .Invoke(client, new object[] { outer, CancellationToken.None })!;
            await task.ConfigureAwait(false);
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            internal int SendCount { get; private set; }
            internal string? Body { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                SendCount++;
                Body = request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
        }
    }
}
