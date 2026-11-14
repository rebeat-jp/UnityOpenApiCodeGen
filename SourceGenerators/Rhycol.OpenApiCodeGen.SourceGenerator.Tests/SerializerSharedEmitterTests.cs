using System;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;

using Xunit;

using static Rhycol.OpenApiCodeGen.SourceGenerator.Tests.SerializerMatrixHarness;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    /// <summary>
    /// Text that comes from the spec (summaries, descriptions, names, literals) must reach the generated
    /// source unchanged for both serializers, even when it mentions the JSON libraries; and a declared
    /// error body is judged consistently by both serializers.
    /// </summary>
    public sealed class SerializerSharedEmitterTests
    {
        private const string Mention = "Uses Newtonsoft and global::Newtonsoft.Json.Linq.JToken, JObject, JsonSerializationException";

        private static readonly string TextSpec = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Mentions"", ""version"": ""1"", ""description"": """ + Mention + @""" },
  ""paths"": {
    ""/Newtonsoft/{id}"": {
      ""get"": {
        ""operationId"": ""getMention"",
        ""summary"": """ + Mention + @""",
        ""description"": """ + Mention + @""",
        ""parameters"": [
          { ""name"": ""id"", ""in"": ""path"", ""required"": true, ""description"": """ + Mention + @""", ""schema"": { ""type"": ""integer"" } },
          { ""name"": ""x-Newtonsoft-Trace"", ""in"": ""header"", ""description"": ""JToken header"", ""schema"": { ""type"": ""string"" } },
          { ""name"": ""JToken"", ""in"": ""query"", ""description"": ""JToken query"", ""schema"": { ""type"": ""string"" } }
        ],
        ""responses"": {
          ""200"": {
            ""description"": """ + Mention + @""",
            ""content"": { ""application/json"": { ""schema"": { ""$ref"": ""#/components/schemas/Mention"" } } }
          },
          ""400"": {
            ""description"": ""bad"",
            ""content"": { ""application/json"": { ""schema"": { ""$ref"": ""#/components/schemas/Problem"" } } }
          }
        }
      }
    }
  },
  ""components"": {
    ""schemas"": {
      ""Mention"": {
        ""type"": ""object"",
        ""description"": """ + Mention + @""",
        ""properties"": {
          ""any"": { ""description"": """ + Mention + @""" },
          ""JToken"": { ""type"": ""string"", ""description"": ""Newtonsoft"" },
          ""kind"": { ""type"": ""string"", ""enum"": [ ""Newtonsoft"", ""JToken"" ] }
        }
      },
      ""Problem"": {
        ""type"": ""object"",
        ""additionalProperties"": false,
        ""required"": [ ""code"", ""message"" ],
        ""properties"": {
          ""code"": { ""type"": ""integer"", ""format"": ""int32"" },
          ""message"": { ""type"": ""string"" }
        }
      }
    }
  }
}";

        [Theory]
        [InlineData(NewtonsoftJson)]
        [InlineData(SystemTextJson)]
        public void SpecTextThatMentionsTheJsonLibrariesIsKeptVerbatim(string serializer)
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                TextSpec,
                options: new Phase4CompilationOptions
                {
                    JsonSerializer = serializer,
                    // The System.Text.Json client must not need Newtonsoft.Json at all.
                    Newtonsoft = !IsStj(serializer)
                });

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            string source = execution.GeneratedSource;
            Assert.Contains(Mention, source);
            Assert.Contains("x-Newtonsoft-Trace", source);
            Assert.Contains("/Newtonsoft/", source);
            Assert.Contains("\"Newtonsoft\"", source);
            // The mention is only ever in documentation or literals: no code references the other library.
            if (IsStj(serializer))
            {
                Assert.DoesNotContain("using Newtonsoft", source);
                Assert.DoesNotContain("new global::Newtonsoft", source);
                Assert.DoesNotContain(": global::Newtonsoft", source);
            }
        }

        [Theory]
        [InlineData(NewtonsoftJson, "{\"code\":\"x\",\"message\":\"m\"}")]
        [InlineData(SystemTextJson, "{\"code\":\"x\",\"message\":\"m\"}")]
        [InlineData(NewtonsoftJson, "{\"code\":7}")]
        [InlineData(SystemTextJson, "{\"code\":7}")]
        [InlineData(NewtonsoftJson, "{\"code\":7,\"message\":null}")]
        [InlineData(SystemTextJson, "{\"code\":7,\"message\":null}")]
        [InlineData(NewtonsoftJson, "not json")]
        [InlineData(SystemTextJson, "not json")]
        [InlineData(NewtonsoftJson, "[1]")]
        [InlineData(SystemTextJson, "[1]")]
        public async Task ErrorBodyThatViolatesTheContractGivesNoTypedErrorInBothSerializers(string serializer, string body)
        {
            Assembly api = Compile("serializer-core.json", serializer);
            object widget = New(api, "Widget", ("Id", 1L), ("Name", "n"), ("Created", DateTimeOffset.UnixEpoch));

            Exception failure = await Assert.ThrowsAnyAsync<Exception>(() => Call(
                api, "createWidget", Json(HttpStatusCode.BadRequest, body), widget));

            Assert.Equal(body, Get(failure, "ResponseBody"));
            Assert.Null(Get(failure, "Error"));
            Assert.IsAssignableFrom(
                JsonExceptionType(serializer),
                Assert.IsAssignableFrom<Exception>(Get(failure, "ErrorDeserializationException")));
        }

        [Theory]
        [InlineData(NewtonsoftJson)]
        [InlineData(SystemTextJson)]
        public async Task ValidAndEmptyErrorBodiesAreTreatedTheSame(string serializer)
        {
            Assembly api = Compile("serializer-core.json", serializer);
            object widget = New(api, "Widget", ("Id", 1L), ("Name", "n"), ("Created", DateTimeOffset.UnixEpoch));

            Exception whole = await Assert.ThrowsAnyAsync<Exception>(() => Call(
                api, "createWidget", Json(HttpStatusCode.BadRequest, "{\"code\":7,\"message\":\"m\"}"), widget));
            Assert.Equal(7, Get(Get(whole, "Error")!, "Code"));

            foreach (string body in new[] { "null", "  " })
            {
                Exception empty = await Assert.ThrowsAnyAsync<Exception>(() => Call(
                    api, "createWidget", Json(HttpStatusCode.BadRequest, body), widget));
                Assert.Null(Get(empty, "Error"));
                Assert.Null(Get(empty, "ErrorDeserializationException"));
            }
        }

        [Theory]
        [InlineData("{\"code\":7,\"code\":8,\"message\":\"m\"}")]
        [InlineData("{\"code\":7,\"message\":\"m\",\"zzz\":1}")]
        public async Task SystemTextJsonErrorBodyAlsoGoesThroughTheResponseContract(string body)
        {
            Assembly api = Compile("serializer-core.json", SystemTextJson);
            object widget = New(api, "Widget", ("Id", 1L), ("Name", "n"), ("Created", DateTimeOffset.UnixEpoch));

            Exception failure = await Assert.ThrowsAnyAsync<Exception>(() => Call(
                api, "createWidget", Json(HttpStatusCode.BadRequest, body), widget));

            Assert.Null(Get(failure, "Error"));
            Assert.IsAssignableFrom<System.Text.Json.JsonException>(Get(failure, "ErrorDeserializationException"));
        }
    }
}
