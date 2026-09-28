using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    public sealed class Phase4NonFiniteParameterTests
    {
        [Theory]
        [InlineData("path", "float", true)]
        [InlineData("path", "double", true)]
        [InlineData("query", "float", true)]
        [InlineData("query", "double", true)]
        [InlineData("query", "float", false)]
        [InlineData("query", "double", false)]
        [InlineData("header", "float", true)]
        [InlineData("header", "double", true)]
        [InlineData("header", "float", false)]
        [InlineData("header", "double", false)]
        public async Task NonFiniteScalarParameterFailsBeforeSend(
            string location,
            string format,
            bool required)
        {
            string path = location == "path" ? "/values/{number}" : "/values";
            string document = "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Number\",\"version\":\"1\"}," +
                "\"servers\":[{\"url\":\"https://example.test/\"}],\"paths\":{\"" + path +
                "\":{\"get\":{\"operationId\":\"readValue\",\"parameters\":[{" +
                "\"name\":\"number\",\"in\":\"" + location + "\",\"required\":" +
                (required ? "true" : "false") + ",\"schema\":{\"type\":\"number\",\"format\":\"" +
                format + "\"}}],\"responses\":{\"204\":{\"description\":\"Done\"}}}}}}";
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(document);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();
            var handler = new RecordingHandler();
            using var client = new HttpClient(handler);
            object api = Activator.CreateInstance(assembly.GetType("Generated.Phase4.Phase4Api")!, client)!;
            MethodInfo method = api.GetType().GetMethod("readValue")!;

            foreach (double nonFinite in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                object value = format == "float" ? (object)(float)nonFinite : nonFinite;
                ArgumentOutOfRangeException exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                    () => Invoke(method, api, value));
                Assert.Equal("number", exception.ParamName);
                Assert.Equal(0, handler.SendCount);
            }

            await Invoke(method, api, format == "float" ? (object)1.25f : 1.25d);
            Assert.Equal(1, handler.SendCount);
            if (location == "path")
            {
                Assert.EndsWith("/values/1.25", handler.RequestUri!.AbsolutePath);
            }
            else if (location == "query")
            {
                Assert.Equal("?number=1.25", handler.RequestUri!.Query);
            }
            else
            {
                Assert.Equal("1.25", handler.HeaderValue);
            }

            if (!required)
            {
                await Invoke(method, api, null);
                Assert.Equal(2, handler.SendCount);
                Assert.Equal("/values", handler.RequestUri!.AbsolutePath);
                Assert.Equal(string.Empty, handler.RequestUri.Query);
                Assert.Null(handler.HeaderValue);
            }
        }

        private static async Task Invoke(MethodInfo method, object api, object? value)
        {
            var task = (Task)method.Invoke(api, new[] { value, (object)CancellationToken.None })!;
            await task;
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            internal int SendCount { get; private set; }
            internal Uri? RequestUri { get; private set; }
            internal string? HeaderValue { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                SendCount++;
                RequestUri = request.RequestUri;
                HeaderValue = request.Headers.TryGetValues("number", out var values)
                    ? string.Join(",", values)
                    : null;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
        }
    }
}
