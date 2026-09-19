using System;
using System.Collections;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    public sealed class Phase4HttpClientReviewFixTests
    {
        public static TheoryData<string> CSharpLineSeparators => new()
        {
            "\n",
            "\r",
            "\r\n",
            "\u0085",
            "\u2028",
            "\u2029"
        };

        [Theory]
        [MemberData(nameof(CSharpLineSeparators))]
        public void XmlDocumentationTextCannotAddGeneratedClassMembers(string lineSeparator)
        {
            string summary = "Summary" + lineSeparator +
                             "        public const int SummaryInjected = 1;" + lineSeparator +
                             "        ///";
            string parameterName = "trace" + lineSeparator +
                                   "        public const int ParameterInjected = 2;" + lineSeparator +
                                   "        ///";
            string document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Comments"", ""version"": ""1"" },
  ""paths"": { ""/comments"": { ""get"": {
    ""operationId"": ""getComments"",
    ""summary"": " + JsonConvert.SerializeObject(summary) + @",
    ""parameters"": [
      { ""name"": " + JsonConvert.SerializeObject(parameterName) + @", ""in"": ""header"", ""required"": true, ""schema"": { ""type"": ""string"" } }
    ],
    ""responses"": { ""204"": { ""description"": ""No Content"" } }
  } } }
}";

            Phase4GeneratorExecution execution =
                Phase4GeneratorTestHarness.GenerateAndCompile(document);
            CompilationUnitSyntax syntax = CSharpSyntaxTree.ParseText(execution.GeneratedSource)
                .GetCompilationUnitRoot();
            ClassDeclarationSyntax client = syntax.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Single(declaration => declaration.Identifier.ValueText == "Phase4Api");
            string[] fieldNames = client.Members
                .OfType<FieldDeclarationSyntax>()
                .SelectMany(static field => field.Declaration.Variables)
                .Select(static variable => variable.Identifier.ValueText)
                .ToArray();

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assert.DoesNotContain("SummaryInjected", fieldNames);
            Assert.DoesNotContain("ParameterInjected", fieldNames);
        }

        [Fact]
        public async Task ServerAndOperationQueriesRemainAfterPathJoining()
        {
            const string Document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Queries"", ""version"": ""1"" },
  ""servers"": [ { ""url"": ""https://api.example.test/v1?api-version=2026-09-01&tenant=a%2Fb"" } ],
  ""paths"": { ""/pets"": { ""get"": {
    ""operationId"": ""getPets"",
    ""parameters"": [
      { ""name"": ""limit"", ""in"": ""query"", ""required"": true, ""schema"": { ""type"": ""integer"" } }
    ],
    ""responses"": { ""204"": { ""description"": ""No Content"" } }
  } } }
}";
            Phase4GeneratorExecution execution =
                Phase4GeneratorTestHarness.GenerateAndCompile(Document);
            Assembly assembly = AssertCompilesAndEmit(execution);
            var handler = new RecordingHandler();
            using var httpClient = new HttpClient(handler);
            object client = CreateClient(assembly, httpClient);

            await Invoke(client, "getPets", 3, CancellationToken.None);

            Assert.Equal(
                "https://api.example.test/v1/pets?api-version=2026-09-01&tenant=a%2Fb&limit=3",
                handler.RequestUri!.AbsoluteUri);
        }

        [Fact]
        public async Task HttpClientBaseAddressQueryRemainsWhenDocumentBaseUrlIsOverriddenWithEmptyString()
        {
            const string Document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Base address"", ""version"": ""1"" },
  ""paths"": { ""/pets"": { ""get"": {
    ""operationId"": ""getPets"",
    ""parameters"": [
      { ""name"": ""limit"", ""in"": ""query"", ""required"": true, ""schema"": { ""type"": ""integer"" } }
    ],
    ""responses"": { ""204"": { ""description"": ""No Content"" } }
  } } }
}";
            Phase4GeneratorExecution execution =
                Phase4GeneratorTestHarness.GenerateAndCompile(Document);
            Assembly assembly = AssertCompilesAndEmit(execution);
            var handler = new RecordingHandler();
            using var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://fallback.example.test/root/?token=abc")
            };
            object client = CreateClient(assembly, httpClient, string.Empty);

            await Invoke(client, "getPets", 5, CancellationToken.None);

            Assert.Equal(
                "https://fallback.example.test/root/pets?token=abc&limit=5",
                handler.RequestUri!.AbsoluteUri);
        }

        [Theory]
        [InlineData("/v1?api-version=1")]
        [InlineData("v1?api-version=1")]
        public async Task RelativeServerUrlUsesHttpClientBaseAddressWithoutLosingPathsOrQueries(
            string serverUrl)
        {
            string document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Relative server"", ""version"": ""1"" },
  ""servers"": [ { ""url"": " + JsonConvert.SerializeObject(serverUrl) + @" } ],
  ""paths"": { ""/pets/{petId}"": { ""get"": {
    ""operationId"": ""getPet"",
    ""parameters"": [
      { ""name"": ""petId"", ""in"": ""path"", ""required"": true, ""schema"": { ""type"": ""string"" } },
      { ""name"": ""limit"", ""in"": ""query"", ""required"": true, ""schema"": { ""type"": ""integer"" } }
    ],
    ""responses"": { ""204"": { ""description"": ""No Content"" } }
  } } }
}";
            Phase4GeneratorExecution execution =
                Phase4GeneratorTestHarness.GenerateAndCompile(document);
            Assembly assembly = AssertCompilesAndEmit(execution);
            var handler = new RecordingHandler();
            using var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://fallback.example.test/root/?token=abc")
            };
            object client = CreateClient(assembly, httpClient);

            await Invoke(client, "getPet", "a/b", 5, CancellationToken.None);

            Assert.Equal(
                "https://fallback.example.test/root/v1/pets/a%2Fb?token=abc&api-version=1&limit=5",
                handler.RequestUri!.AbsoluteUri);
            Assert.Equal(Uri.UriSchemeHttps, handler.RequestUri.Scheme);
        }

        [Fact]
        public async Task RequestJsonUsesDateOnlyFormatWithoutChangingDateTimeOffset()
        {
            const string Document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Dates"", ""version"": ""1"" },
  ""servers"": [ { ""url"": ""https://api.example.test/"" } ],
  ""paths"": { ""/dates"": { ""post"": {
    ""operationId"": ""sendDates"",
    ""requestBody"": { ""required"": true, ""content"": {
      ""application/json"": { ""schema"": { ""$ref"": ""#/components/schemas/DatePayload"" } }
    } },
    ""responses"": { ""204"": { ""description"": ""No Content"" } }
  } } },
  ""components"": { ""schemas"": {
    ""NestedDate"": {
      ""type"": ""object"", ""required"": [""day""],
      ""properties"": { ""day"": { ""type"": ""string"", ""format"": ""date"" } }
    },
    ""DatePayload"": {
      ""type"": ""object"",
      ""required"": [""day"", ""occurredAt"", ""days"", ""nested""],
      ""properties"": {
        ""day"": { ""type"": ""string"", ""format"": ""date"" },
        ""optionalDay"": { ""type"": ""string"", ""format"": ""date"" },
        ""occurredAt"": { ""type"": ""string"", ""format"": ""date-time"" },
        ""days"": { ""type"": ""array"", ""items"": { ""type"": ""string"", ""format"": ""date"" } },
        ""nested"": { ""$ref"": ""#/components/schemas/NestedDate"" }
      }
    }
  } }
}";
            Phase4GeneratorExecution execution =
                Phase4GeneratorTestHarness.GenerateAndCompile(Document);
            Assembly assembly = AssertCompilesAndEmit(execution);
            object body = Activator.CreateInstance(
                assembly.GetType("Generated.Phase4.DatePayload", throwOnError: true)!)!;
            SetProperty(body, "Day", new DateTime(2026, 8, 4, 15, 30, 0));
            SetProperty(body, "OptionalDay", new DateTime(2026, 8, 5, 23, 59, 59));
            SetProperty(
                body,
                "OccurredAt",
                new DateTimeOffset(2026, 8, 4, 12, 34, 56, TimeSpan.FromHours(9)));
            var days = (IList)Activator.CreateInstance(body.GetType().GetProperty("Days")!.PropertyType)!;
            days.Add(new DateTime(2026, 8, 6, 9, 0, 0));
            SetProperty(body, "Days", days);
            object nested = Activator.CreateInstance(
                assembly.GetType("Generated.Phase4.NestedDate", throwOnError: true)!)!;
            SetProperty(nested, "Day", new DateTime(2026, 8, 7, 18, 0, 0));
            SetProperty(body, "Nested", nested);
            var handler = new RecordingHandler();
            using var httpClient = new HttpClient(handler);
            object client = CreateClient(assembly, httpClient);

            await Invoke(client, "sendDates", body, CancellationToken.None);

            JObject json = JObject.Parse(handler.Content);
            Assert.Equal("2026-08-04", (string?)json["day"]);
            Assert.Equal("2026-08-05", (string?)json["optionalDay"]);
            Assert.Contains(
                "\"occurredAt\":\"2026-08-04T12:34:56+09:00\"",
                handler.Content);
            Assert.Equal("2026-08-06", (string?)json["days"]![0]);
            Assert.Equal("2026-08-07", (string?)json["nested"]!["day"]);
        }

        [Fact]
        public async Task ParameterizedJsonContentTypePreservesParametersAndUsesDeclaredCharset()
        {
            const string Document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Media type"", ""version"": ""1"" },
  ""servers"": [ { ""url"": ""https://api.example.test/"" } ],
  ""paths"": { ""/merge"": { ""patch"": {
    ""operationId"": ""merge"",
    ""requestBody"": { ""required"": true, ""content"": {
      ""application/merge-patch+json; charset=utf-16; profile=merge"": {
        ""schema"": { ""type"": ""string"" }
      }
    } },
    ""responses"": { ""204"": { ""description"": ""No Content"" } }
  } } }
}";
            Phase4GeneratorExecution execution =
                Phase4GeneratorTestHarness.GenerateAndCompile(Document);
            Assembly assembly = AssertCompilesAndEmit(execution);
            var handler = new RecordingHandler();
            using var httpClient = new HttpClient(handler);
            object client = CreateClient(assembly, httpClient);

            await Invoke(client, "merge", "雪", CancellationToken.None);

            Assert.Equal("application/merge-patch+json", handler.ContentType!.MediaType);
            Assert.Equal("utf-16", handler.ContentType.CharSet);
            Assert.Contains(
                handler.ContentType.Parameters,
                parameter => parameter.Name == "profile" &&
                             parameter.Value == "merge");
            Assert.Equal(JsonConvert.SerializeObject("雪"), Encoding.Unicode.GetString(handler.ContentBytes));
        }

        private static Assembly AssertCompilesAndEmit(Phase4GeneratorExecution execution)
        {
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            return execution.EmitAssembly();
        }

        private static object CreateClient(
            Assembly assembly,
            HttpClient httpClient,
            string? baseUrl = null)
        {
            Type clientType = assembly.GetType(
                "Generated.Phase4.Phase4Api",
                throwOnError: true)!;
            return baseUrl is null
                ? Activator.CreateInstance(clientType, httpClient)!
                : Activator.CreateInstance(clientType, httpClient, baseUrl)!;
        }

        private static void SetProperty(object target, string propertyName, object value)
        {
            target.GetType().GetProperty(propertyName)!.SetValue(target, value);
        }

        private static async Task Invoke(object client, string methodName, params object[] arguments)
        {
            var task = (Task)client.GetType().GetMethod(methodName)!.Invoke(client, arguments)!;
            await task.ConfigureAwait(false);
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            internal Uri? RequestUri { get; private set; }

            internal string Content { get; private set; } = string.Empty;

            internal byte[] ContentBytes { get; private set; } = Array.Empty<byte>();

            internal System.Net.Http.Headers.MediaTypeHeaderValue? ContentType { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                RequestUri = request.RequestUri;
                Content = request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync().ConfigureAwait(false);
                ContentBytes = request.Content is null
                    ? Array.Empty<byte>()
                    : await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                ContentType = request.Content?.Headers.ContentType;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
        }
    }
}
