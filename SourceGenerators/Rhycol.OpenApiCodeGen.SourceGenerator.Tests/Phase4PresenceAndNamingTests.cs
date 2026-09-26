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
    public sealed class Phase4PresenceAndNamingTests
    {
        private const string DtoDocument = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Presence"", ""version"": ""1"" },
  ""paths"": {},
  ""components"": { ""schemas"": {
    ""MaybeLabel"": { ""type"": [""string"", ""null""] },
    ""Payload"": {
      ""type"": ""object"",
      ""required"": [""requiredName""],
      ""properties"": {
        ""requiredName"": { ""type"": ""string"" },
        ""label"": { ""type"": [""string"", ""null""] },
        ""count"": { ""type"": [""integer"", ""null""] },
        ""reference"": { ""$ref"": ""#/components/schemas/MaybeLabel"" },
        ""plain"": { ""type"": ""string"" }
      }
    }
  } }
}";

        [Fact]
        public void OptionalSchemaNullablePropertiesRoundTripAbsentNullAndValue()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(DtoDocument);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Type type = execution.EmitAssembly().GetType("Generated.Phase4.Payload", throwOnError: true)!;

            object absent = JsonConvert.DeserializeObject("{\"requiredName\":\"r\"}", type)!;
            Assert.False(GetSpecified(absent, "Label"));
            Assert.False(GetSpecified(absent, "Count"));
            Assert.False(GetSpecified(absent, "Reference"));
            JObject absentJson = JObject.Parse(JsonConvert.SerializeObject(absent));
            Assert.False(absentJson.ContainsKey("label"), absentJson.ToString());
            Assert.False(absentJson.ContainsKey("count"));
            Assert.False(absentJson.ContainsKey("reference"));
            Assert.False(absentJson.ContainsKey("labelSpecified"));

            object explicitNull = JsonConvert.DeserializeObject(
                "{\"requiredName\":\"r\",\"label\":null,\"count\":null,\"reference\":null}", type)!;
            Assert.True(GetSpecified(explicitNull, "Label"));
            Assert.True(GetSpecified(explicitNull, "Count"));
            Assert.True(GetSpecified(explicitNull, "Reference"));
            JObject nullJson = JObject.Parse(JsonConvert.SerializeObject(explicitNull));
            Assert.Equal(JTokenType.Null, nullJson["label"]!.Type);
            Assert.Equal(JTokenType.Null, nullJson["count"]!.Type);
            Assert.Equal(JTokenType.Null, nullJson["reference"]!.Type);
            var ignoreNulls = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            };
            JObject nullJsonWithGlobalIgnore = JObject.Parse(
                JsonConvert.SerializeObject(explicitNull, ignoreNulls));
            Assert.Equal(JTokenType.Null, nullJsonWithGlobalIgnore["label"]!.Type);
            Assert.Equal(JTokenType.Null, nullJsonWithGlobalIgnore["count"]!.Type);
            Assert.Equal(JTokenType.Null, nullJsonWithGlobalIgnore["reference"]!.Type);
            object explicitNullWithGlobalIgnore = JsonConvert.DeserializeObject(
                "{\"requiredName\":\"r\",\"label\":null}", type, ignoreNulls)!;
            Assert.True(GetSpecified(explicitNullWithGlobalIgnore, "Label"));

            object values = JsonConvert.DeserializeObject(
                "{\"requiredName\":\"r\",\"label\":\"yes\",\"count\":4,\"reference\":\"ref\"}", type)!;
            Assert.True(GetSpecified(values, "Label"));
            Assert.True(GetSpecified(values, "Count"));
            Assert.True(GetSpecified(values, "Reference"));
            JObject valuesJson = JObject.Parse(JsonConvert.SerializeObject(values));
            Assert.Equal("yes", (string?)valuesJson["label"]);
            Assert.Equal(4, (int?)valuesJson["count"]);
            Assert.Equal("ref", (string?)valuesJson["reference"]);

            type.GetProperty("Label")!.SetValue(absent, null);
            Assert.True(GetSpecified(absent, "Label"));
            Assert.Equal(JTokenType.Null, JObject.Parse(JsonConvert.SerializeObject(absent))["label"]!.Type);
            type.GetProperty("LabelSpecified")!.SetValue(absent, false);
            Assert.False(JObject.Parse(JsonConvert.SerializeObject(absent)).ContainsKey("label"));

            type.GetProperty("Plain")!.SetValue(absent, null);
            Assert.False(JObject.Parse(JsonConvert.SerializeObject(absent)).ContainsKey("plain"));
        }

        [Fact]
        public void SpecifiedMarkersAndBackingFieldsCannotCollideWithSchemaProperties()
        {
            const string document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Names"", ""version"": ""1"" },
  ""paths"": {},
  ""components"": { ""schemas"": { ""Payload"": {
    ""type"": ""object"",
    ""properties"": {
      ""NameSpecified"": { ""type"": ""string"" },
      ""_NameValue"": { ""type"": ""string"" },
      ""name"": { ""type"": [""string"", ""null""] }
    }
  } } }
}";
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(document);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Type type = execution.EmitAssembly().GetType("Generated.Phase4.Payload", throwOnError: true)!;
            Assert.NotNull(type.GetProperty("Name"));
            Assert.NotNull(type.GetProperty("NameSpecified"));
            Assert.NotNull(type.GetProperty("NameSpecified2"));
            Assert.NotNull(type.GetProperty("_NameValue"));
            Assert.NotNull(type.GetField("_NameValue2", BindingFlags.Instance | BindingFlags.NonPublic));
            object dto = JsonConvert.DeserializeObject("{\"name\":null,\"NameSpecified\":\"wire\"}", type)!;
            Assert.True(GetSpecified(dto, "Name"), JsonConvert.SerializeObject(dto));
            Assert.Equal("wire", type.GetProperty("NameSpecified2")!.GetValue(dto));
            JObject roundTrip = JObject.Parse(JsonConvert.SerializeObject(dto));
            Assert.Equal(JTokenType.Null, roundTrip["name"]!.Type);
            Assert.Equal("wire", (string?)roundTrip["NameSpecified"]);
        }

        [Theory]
        [InlineData("ConvertToString")]
        [InlineData("CreateRequestUri")]
        [InlineData("CombinePaths")]
        [InlineData("DateOnlyJsonConverter")]
        [InlineData("DateOnlyJsonConverterInstance")]
        [InlineData("_httpClient")]
        public async Task OperationNamesDoNotShadowGeneratedClientMembers(string operationId)
        {
            string document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Names"", ""version"": ""1"" },
  ""servers"": [{ ""url"": ""https://example.test/"" }],
  ""paths"": { ""/items/{id}"": { ""get"": {
    ""operationId"": " + JsonConvert.SerializeObject(operationId) + @",
    ""parameters"": [{ ""name"": ""id"", ""in"": ""path"", ""required"": true,
      ""schema"": { ""type"": ""integer"" } }],
    ""responses"": { ""204"": { ""description"": ""No Content"" } }
  } } }
}";
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(document);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();
            Type clientType = assembly.GetType("Generated.Phase4.Phase4Api", throwOnError: true)!;
            var handler = new RecordingHandler();
            using var httpClient = new HttpClient(handler);
            object client = Activator.CreateInstance(clientType, httpClient)!;
            MethodInfo operation = clientType.GetMethod(operationId + "2")!;
            Assert.NotNull(operation);

            await (Task)operation.Invoke(client, new object[] { 42, CancellationToken.None })!;

            Assert.Equal("https://example.test/items/42", handler.RequestUri?.AbsoluteUri);
        }

        private static bool GetSpecified(object value, string name)
        {
            return (bool)value.GetType().GetProperty(name + "Specified")!.GetValue(value)!;
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            internal Uri? RequestUri { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                RequestUri = request.RequestUri;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
        }
    }
}
