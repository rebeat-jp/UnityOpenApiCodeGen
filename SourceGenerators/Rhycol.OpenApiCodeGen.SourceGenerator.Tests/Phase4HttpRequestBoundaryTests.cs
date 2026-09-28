using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;

using Newtonsoft.Json;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    public sealed class Phase4HttpRequestBoundaryTests
    {
        [Theory]
        [InlineData("ftp://api.example.test/v1")]
        [InlineData("file:///tmp/api")]
        [InlineData("wss://api.example.test/v1")]
        public void NonHttpAbsoluteServerUrlReportsLocatedDiagnostic(string serverUrl)
        {
            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(CreateGetDocument(serverUrl))
                    .RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("Only HTTP and HTTPS", diagnostic.GetMessage());
            Assert.Contains("logical path '/servers/0/url'", diagnostic.GetMessage());
        }

        [Theory]
        [InlineData("http://api.example.test/v1")]
        [InlineData("https://api.example.test/v1")]
        [InlineData("//api.example.test/v1")]
        [InlineData("/v1")]
        [InlineData("v1")]
        public void HttpAndRelativeServerUrlsGenerate(string serverUrl)
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                CreateGetDocument(serverUrl));

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
        }

        [Theory]
        [InlineData(true, false, true, null)]
        [InlineData(true, true, false, "null")]
        [InlineData(false, false, false, null)]
        public async Task RequestBodyRequiredAndSchemaNullableControlNullHandling(
            bool required,
            bool nullable,
            bool throws,
            string? expectedJson)
        {
            string schema = nullable
                ? "{ \"type\": [\"object\", \"null\"], \"additionalProperties\": false, \"properties\": { \"name\": { \"type\": \"string\" } } }"
                : "{ \"type\": \"object\", \"additionalProperties\": false, \"properties\": { \"name\": { \"type\": \"string\" } } }";
            string document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Request body"", ""version"": ""1"" },
  ""servers"": [{ ""url"": ""https://api.example.test/"" }],
  ""paths"": { ""/body"": { ""post"": {
    ""operationId"": ""sendBody"",
    ""requestBody"": { ""required"": " + (required ? "true" : "false") + @", ""content"": {
      ""application/json"": { ""schema"": " + schema + @" }
    } },
    ""responses"": { ""204"": { ""description"": ""Done"" } }
  } } }
}";
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(document);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();
            var handler = new RecordingHandler();
            using var httpClient = new HttpClient(handler);
            object api = Activator.CreateInstance(assembly.GetType("Generated.Phase4.Phase4Api")!, httpClient)!;
            var task = (Task)api.GetType().GetMethod("sendBody")!
                .Invoke(api, new object?[] { null, CancellationToken.None })!;

            if (throws)
            {
                ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
                    async () => await task);
                Assert.Equal("body", exception.ParamName);
                Assert.Equal(0, handler.SendCount);
            }
            else
            {
                await task;
                Assert.Equal(1, handler.SendCount);
                Assert.Equal(expectedJson, handler.Content);
            }
        }

        [Theory]
        [InlineData("https", "https://api.example.test/v1/pets?server=1&limit=5")]
        [InlineData("http", "http://api.example.test/v1/pets?server=1&limit=5")]
        public async Task NetworkPathServerUsesOnlyBaseAddressScheme(
            string scheme,
            string expectedUri)
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                CreateGetDocument("//api.example.test/v1?server=1"));
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();
            var handler = new RecordingHandler();
            using var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(scheme + "://fallback.example.test/root/?base=0")
            };
            object api = Activator.CreateInstance(assembly.GetType("Generated.Phase4.Phase4Api")!, httpClient)!;

            await InvokeGet(api);

            Assert.Equal(1, handler.SendCount);
            Assert.Equal(expectedUri, handler.RequestUri!.AbsoluteUri);
        }

        [Fact]
        public async Task NetworkPathServerWithoutBaseAddressFailsBeforeSending()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                CreateGetDocument("//api.example.test/v1?server=1"));
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();
            var handler = new RecordingHandler();
            using var httpClient = new HttpClient(handler);
            object api = Activator.CreateInstance(assembly.GetType("Generated.Phase4.Phase4Api")!, httpClient)!;

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => InvokeGet(api));

            Assert.Contains("HttpClient.BaseAddress", exception.Message);
            Assert.Equal(0, handler.SendCount);
        }

        [Fact]
        public async Task RootRelativeServerWithoutBaseAddressFailsBeforeSending()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                CreateGetDocument("/v1?server=1"));
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();
            var handler = new RecordingHandler();
            using var httpClient = new HttpClient(handler);
            object api = Activator.CreateInstance(assembly.GetType("Generated.Phase4.Phase4Api")!, httpClient)!;

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => InvokeGet(api));

            Assert.Contains("HttpClient.BaseAddress", exception.Message);
            Assert.Contains("root-relative", exception.Message);
            Assert.Equal(0, handler.SendCount);
        }

        [Theory]
        [InlineData(false, false, "https://fallback.example.test/pets?limit=5")]
        [InlineData(true, false, "https://fallback.example.test/pets?limit=5")]
        [InlineData(false, true, "https://fallback.example.test/root/pets?base=0&limit=5")]
        public async Task MissingOrEmptyRootServersUseOriginRootUnlessBaseUrlIsOverridden(
            bool emptyServers,
            bool explicitEmptyOverride,
            string expectedUri)
        {
            string document = CreateGetDocument("/v1");
            document = document.Replace("  \"servers\": [{ \"url\": \"/v1\" }],",
                emptyServers ? "  \"servers\": []," : string.Empty);
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(document);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();
            var handler = new RecordingHandler();
            using var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://fallback.example.test/root/?base=0")
            };
            Type apiType = assembly.GetType("Generated.Phase4.Phase4Api")!;
            object api = explicitEmptyOverride
                ? Activator.CreateInstance(apiType, httpClient, string.Empty)!
                : Activator.CreateInstance(apiType, httpClient)!;

            await InvokeGet(api);

            Assert.Equal(expectedUri, handler.RequestUri!.AbsoluteUri);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task MissingOrEmptyRootServersNeedBaseAddressBeforeSend(bool emptyServers)
        {
            string document = CreateGetDocument("/v1");
            document = document.Replace("  \"servers\": [{ \"url\": \"/v1\" }],",
                emptyServers ? "  \"servers\": []," : string.Empty);
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(document);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();
            var handler = new RecordingHandler();
            using var httpClient = new HttpClient(handler);
            object api = Activator.CreateInstance(assembly.GetType("Generated.Phase4.Phase4Api")!, httpClient)!;

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => InvokeGet(api));

            Assert.Contains("HttpClient.BaseAddress", exception.Message);
            Assert.Equal(0, handler.SendCount);
        }

        [Theory]
        [InlineData("/v1?server=1", "https://fallback.example.test/v1/pets?server=1&limit=5")]
        [InlineData("v1?server=1", "https://fallback.example.test/root/v1/pets?base=0&server=1&limit=5")]
        [InlineData("https://api.example.test/v1?server=1", "https://api.example.test/v1/pets?server=1&limit=5")]
        public async Task RelativeAndAbsoluteServerUrlsKeepExistingResolution(
            string serverUrl,
            string expectedUri)
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                CreateGetDocument(serverUrl));
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();
            var handler = new RecordingHandler();
            using var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://fallback.example.test/root/?base=0")
            };
            object api = Activator.CreateInstance(assembly.GetType("Generated.Phase4.Phase4Api")!, httpClient)!;

            await InvokeGet(api);

            Assert.Equal(expectedUri, handler.RequestUri!.AbsoluteUri);
        }

        private static string CreateGetDocument(string serverUrl) => @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Server URL"", ""version"": ""1"" },
  ""servers"": [{ ""url"": " + JsonConvert.SerializeObject(serverUrl) + @" }],
  ""paths"": { ""/pets"": { ""get"": {
    ""operationId"": ""getPets"",
    ""parameters"": [{ ""name"": ""limit"", ""in"": ""query"", ""required"": true,
      ""schema"": { ""type"": ""integer"" } }],
    ""responses"": { ""204"": { ""description"": ""Done"" } }
  } } }
}";

        private static async Task InvokeGet(object api)
        {
            var task = (Task)api.GetType().GetMethod("getPets")!
                .Invoke(api, new object[] { 5, CancellationToken.None })!;
            await task;
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            internal int SendCount { get; private set; }
            internal string? Content { get; private set; }
            internal Uri? RequestUri { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                SendCount++;
                RequestUri = request.RequestUri;
                Content = request.Content is null ? null : await request.Content.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
        }
    }
}
