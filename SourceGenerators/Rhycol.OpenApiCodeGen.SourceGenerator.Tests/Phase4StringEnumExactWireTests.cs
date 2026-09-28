using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    public sealed class Phase4StringEnumExactWireTests
    {
        private const string DateLikeValue = "2026-01-02T03:04:05Z";
        private const string EnumDocument = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Enum wire"", ""version"": ""1"" },
  ""servers"": [{ ""url"": ""https://api.example.test/"" }],
  ""paths"": { ""/state"": { ""post"": {
    ""operationId"": ""echoState"",
    ""requestBody"": { ""required"": true, ""content"": {
      ""application/json"": { ""schema"": { ""$ref"": ""#/components/schemas/State"" } }
    } },
    ""responses"": { ""200"": { ""description"": ""OK"", ""content"": {
      ""application/json"": { ""schema"": { ""$ref"": ""#/components/schemas/State"" } }
    } } }
  } } },
  ""components"": { ""schemas"": {
    ""State"": { ""type"": ""string"", ""enum"": [
      ""ready"", ""READY"", "" leading "", ""a,b"", """", ""123"", ""2026-01-02T03:04:05Z""
    ] },
    ""StateWireConverter"": { ""type"": ""object"", ""additionalProperties"": false, ""properties"": {
      ""value"": { ""type"": ""string"" }
    } }
  } }
}";

        [Fact]
        public void DeclaredWireValuesRoundTripExactlyAndPreservePublicEnumContract()
        {
            Phase4GeneratorExecution execution = GenerateEnum();
            Assembly assembly = execution.EmitAssembly();
            Type enumType = assembly.GetType("Generated.Phase4.State", true)!;
            var settings = new JsonSerializerSettings { DateParseHandling = DateParseHandling.None };
            string[] declared = { "ready", "READY", " leading ", "a,b", "", "123", DateLikeValue };

            foreach (string wire in declared)
            {
                string json = JsonConvert.SerializeObject(wire);
                object value = JsonConvert.DeserializeObject(json, enumType, settings)!;
                Assert.Equal(json, JsonConvert.SerializeObject(value));
            }

            Assert.Equal(typeof(int), Enum.GetUnderlyingType(enumType));
            Assert.Equal(Enumerable.Range(0, declared.Length),
                Enum.GetValues(enumType).Cast<object>().Select(Convert.ToInt32));
            Assert.Equal(declared.OrderBy(static value => value, StringComparer.Ordinal),
                enumType.GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Select(field => field.GetCustomAttribute<EnumMemberAttribute>()!.Value));
            Assert.NotNull(assembly.GetType("Generated.Phase4.StateWireConverter", true));
            Assert.NotNull(assembly.GetType("Generated.Phase4.StateWireConverter2", true));
        }

        [Theory]
        [InlineData("\"Ready\"")]
        [InlineData("\" ready\"")]
        [InlineData("\"ready \"")]
        [InlineData("\"a, b\"")]
        [InlineData("\"missing\"")]
        [InlineData("123")]
        [InlineData("0")]
        public void UndeclaredOrNonStringWireValuesAreRejected(string json)
        {
            Type enumType = GenerateEnum().EmitAssembly().GetType("Generated.Phase4.State", true)!;
            var settings = new JsonSerializerSettings { DateParseHandling = DateParseHandling.None };

            Assert.Throws<JsonSerializationException>(() =>
                JsonConvert.DeserializeObject(json, enumType, settings));
            Assert.Throws<JsonSerializationException>(() =>
                JsonConvert.SerializeObject(Enum.ToObject(enumType, 99)));
        }

        [Fact]
        public void NullableJsonNullAndDeclaredEmptyStringRemainDistinct()
        {
            Type enumType = GenerateEnum().EmitAssembly().GetType("Generated.Phase4.State", true)!;
            Type nullableType = typeof(Nullable<>).MakeGenericType(enumType);
            var settings = new JsonSerializerSettings { DateParseHandling = DateParseHandling.None };

            Assert.Null(JsonConvert.DeserializeObject("null", nullableType, settings));
            object emptyWireValue = JsonConvert.DeserializeObject("\"\"", nullableType, settings)!;
            Assert.Equal("\"\"", JsonConvert.SerializeObject(emptyWireValue));
            Assert.Equal("null", JsonConvert.SerializeObject(null, nullableType, settings));
        }

        [Fact]
        public async Task HttpClientWritesExactWireValueAndReadsDateLikeValueAsEnumString()
        {
            Phase4GeneratorExecution execution = GenerateEnum();
            Assembly assembly = execution.EmitAssembly();
            Type enumType = assembly.GetType("Generated.Phase4.State", true)!;
            object requestValue = JsonConvert.DeserializeObject("\"a,b\"", enumType)!;
            var handler = new RecordingHandler("\"" + DateLikeValue + "\"");
            using var httpClient = new HttpClient(handler);
            object client = Activator.CreateInstance(
                assembly.GetType("Generated.Phase4.Phase4Api", true)!, httpClient)!;

            object responseValue = await EchoState(client, requestValue);

            Assert.Equal("\"a,b\"", handler.RequestBody);
            Assert.Equal("\"" + DateLikeValue + "\"", JsonConvert.SerializeObject(responseValue));
            Assert.Equal(1, handler.SendCount);

            handler.ResponseBody = "\"Ready\"";
            await Assert.ThrowsAsync<JsonSerializationException>(() => EchoState(client, requestValue));
            Assert.Equal(2, handler.SendCount);
        }

        [Fact]
        public async Task DateLikeEnumQueryValueKeepsItsExactWireText()
        {
            const string document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Enum query"", ""version"": ""1"" },
  ""servers"": [{ ""url"": ""https://api.example.test/"" }],
  ""paths"": { ""/query"": { ""get"": {
    ""operationId"": ""getState"",
    ""parameters"": [{ ""name"": ""state"", ""in"": ""query"", ""required"": true,
      ""schema"": { ""type"": ""string"", ""enum"": [""2026-01-02T03:04:05Z""] } }],
    ""responses"": { ""204"": { ""description"": ""Done"" } }
  } } }
}";
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(document);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();
            var handler = new RecordingHandler(string.Empty, HttpStatusCode.NoContent);
            using var httpClient = new HttpClient(handler);
            object client = Activator.CreateInstance(
                assembly.GetType("Generated.Phase4.Phase4Api", true)!, httpClient)!;
            MethodInfo method = client.GetType().GetMethod("getState")!;
            Type enumType = method.GetParameters()[0].ParameterType;
            object state = JsonConvert.DeserializeObject(
                JsonConvert.SerializeObject(DateLikeValue), enumType,
                new JsonSerializerSettings { DateParseHandling = DateParseHandling.None })!;

            await (Task)method.Invoke(client, new[] { state, CancellationToken.None })!;

            Assert.Equal("state=" + DateLikeValue,
                Uri.UnescapeDataString(handler.RequestUri!.Query.TrimStart('?')));
        }

        [Fact]
        public async Task DateAndDateTimeOffsetResponsesRemainTyped()
        {
            const string document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Dates"", ""version"": ""1"" },
  ""servers"": [{ ""url"": ""https://api.example.test/"" }],
  ""paths"": {
    ""/date"": { ""get"": { ""operationId"": ""readDate"", ""responses"": {
      ""200"": { ""description"": ""OK"", ""content"": { ""application/json"": {
        ""schema"": { ""type"": ""string"", ""format"": ""date"" }
      } } }
    } } },
    ""/time"": { ""get"": { ""operationId"": ""readTime"", ""responses"": {
      ""200"": { ""description"": ""OK"", ""content"": { ""application/json"": {
        ""schema"": { ""type"": ""string"", ""format"": ""date-time"" }
      } } }
    } } }
  }
}";
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(document);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();
            var handler = new RecordingHandler("\"2026-01-02\"");
            using var httpClient = new HttpClient(handler);
            object client = Activator.CreateInstance(
                assembly.GetType("Generated.Phase4.Phase4Api", true)!, httpClient)!;

            object date = await InvokeResult(client, "readDate");
            Assert.Equal(new DateTime(2026, 1, 2), Assert.IsType<DateTime>(date));

            handler.ResponseBody = "\"2026-01-02T03:04:05+09:00\"";
            object time = await InvokeResult(client, "readTime");
            Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(9)),
                Assert.IsType<DateTimeOffset>(time));
        }

        private static Phase4GeneratorExecution GenerateEnum()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(EnumDocument);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            return execution;
        }

        private static async Task<object> EchoState(object client, object requestValue)
        {
            var task = (Task)client.GetType().GetMethod("echoState")!
                .Invoke(client, new[] { requestValue, CancellationToken.None })!;
            await task.ConfigureAwait(false);
            return task.GetType().GetProperty("Result")!.GetValue(task)!;
        }

        private static async Task<object> InvokeResult(object client, string operation)
        {
            var task = (Task)client.GetType().GetMethod(operation)!
                .Invoke(client, new object[] { CancellationToken.None })!;
            await task.ConfigureAwait(false);
            return task.GetType().GetProperty("Result")!.GetValue(task)!;
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            internal RecordingHandler(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
            {
                ResponseBody = responseBody;
                StatusCode = statusCode;
            }

            internal string ResponseBody { get; set; }
            internal HttpStatusCode StatusCode { get; }
            internal string? RequestBody { get; private set; }
            internal Uri? RequestUri { get; private set; }
            internal int SendCount { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                SendCount++;
                RequestUri = request.RequestUri;
                RequestBody = request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new HttpResponseMessage(StatusCode)
                {
                    Content = StatusCode == HttpStatusCode.NoContent
                        ? null
                        : new StringContent(ResponseBody, Encoding.UTF8, "application/json")
                };
            }
        }
    }
}
