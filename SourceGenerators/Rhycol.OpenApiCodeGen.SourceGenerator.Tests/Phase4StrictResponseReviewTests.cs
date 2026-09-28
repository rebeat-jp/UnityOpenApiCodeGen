using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    public sealed class Phase4StrictResponseReviewTests
    {
        [Theory]
        [InlineData("{\"type\":\"integer\"}", "\"12\"")]
        [InlineData("{\"type\":\"integer\"}", "12.5")]
        [InlineData("{\"type\":\"boolean\"}", "\"true\"")]
        [InlineData("{\"type\":\"string\"}", "12")]
        [InlineData("{\"type\":\"number\"}", "\"12.5\"")]
        [InlineData("{\"type\":\"number\",\"format\":\"decimal\"}", "\"12.5\"")]
        [InlineData("{\"type\":\"array\",\"items\":{\"type\":\"integer\"}}", "[\"12\"]")]
        [InlineData("{\"type\":\"array\",\"items\":{\"type\":\"boolean\"}}", "[1]")]
        public async Task ResponseRejectsWrongJsonTokenKinds(string schema, string json)
        {
            Assembly assembly = Compile(schema);
            await Assert.ThrowsAsync<JsonSerializationException>(() => Invoke(assembly, json));
        }

        [Theory]
        [InlineData("{\"type\":\"integer\"}", "12")]
        [InlineData("{\"type\":\"boolean\"}", "true")]
        [InlineData("{\"type\":\"string\"}", "\"12\"")]
        [InlineData("{\"type\":\"number\",\"format\":\"double\"}", "12.5")]
        [InlineData("{\"type\":\"number\",\"format\":\"decimal\"}", "12.5")]
        [InlineData("{\"type\":\"array\",\"items\":{\"type\":\"integer\"}}", "[12]")]
        [InlineData("{\"type\":\"array\",\"items\":{\"type\":\"number\",\"format\":\"float\"}}", "[1.5]")]
        public async Task ResponseAcceptsMatchingJsonTokenKinds(string schema, string json)
        {
            Assembly assembly = Compile(schema);
            await Invoke(assembly, json);
        }

        [Theory]
        [InlineData("date", "\"2026-1-2\"")]
        [InlineData("date", "\"2026-02-30\"")]
        [InlineData("date", "\"2026-01-02T00:00:00Z\"")]
        [InlineData("date-time", "\"2026-01-02 03:04:05Z\"")]
        [InlineData("date-time", "\"2026-01-02T03:04:05\"")]
        [InlineData("date-time", "\"2026-01-02T03:04:05.Z\"")]
        [InlineData("date-time", "\"2026-01-02T24:04:05Z\"")]
        [InlineData("uuid", "\"0123456789abcdef0123456789abcdef\"")]
        [InlineData("uuid", "\"{01234567-89ab-cdef-0123-456789abcdef}\"")]
        [InlineData("uuid", "\"01234567-89ab-cdef-0123-456789abcdeg\"")]
        public async Task FormattedResponseRejectsInvalidWireValue(string format, string json)
        {
            Assembly assembly = Compile("{\"type\":\"string\",\"format\":\"" + format + "\"}");
            JsonSerializationException error = await Assert.ThrowsAsync<JsonSerializationException>(
                () => Invoke(assembly, json));
            Assert.Contains("$", error.Message);
        }

        [Theory]
        [InlineData("date", "\"2024-02-29\"")]
        [InlineData("date-time", "\"2026-01-02t03:04:05z\"")]
        [InlineData("date-time", "\"2026-01-02T03:04:05.123+09:00\"")]
        [InlineData("uuid", "\"01234567-89ab-CDEF-0123-456789abcdef\"")]
        [InlineData("uuid", "\"FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF\"")]
        public async Task FormattedResponseAcceptsValidWireValue(string format, string json)
        {
            Assembly assembly = Compile("{\"type\":\"string\",\"format\":\"" + format + "\"}");
            Assert.NotNull(await InvokeResult(assembly, json));
        }

        [Fact]
        public async Task FormattedResponseValidationReportsNestedPropertyAndArrayItemPaths()
        {
            const string schema = "{\"$ref\":\"#/components/schemas/Envelope\"}";
            const string components = "{\"Envelope\":{\"type\":\"object\",\"additionalProperties\":false," +
                "\"properties\":{\"dates\":{\"type\":\"array\",\"items\":{\"type\":\"string\",\"format\":\"date\"}}," +
                "\"timestamp\":{\"type\":\"string\",\"format\":\"date-time\"}," +
                "\"id\":{\"type\":\"string\",\"format\":\"uuid\"}}}}";
            Assembly assembly = Compile(schema, components);
            await Invoke(assembly, "{\"dates\":[\"2024-02-29\"],\"timestamp\":\"2026-01-02T03:04:05Z\"," +
                "\"id\":\"01234567-89ab-cdef-0123-456789abcdef\"}");

            foreach ((string json, string path) in new[]
            {
                ("{\"dates\":[\"2026-01-02\",\"2026-02-30\"]}", "$.dates[1]"),
                ("{\"timestamp\":\"2026-01-02 03:04:05Z\"}", "$.timestamp"),
                ("{\"id\":\"0123456789abcdef0123456789abcdef\"}", "$.id")
            })
            {
                JsonSerializationException error = await Assert.ThrowsAsync<JsonSerializationException>(
                    () => Invoke(assembly, json));
                Assert.Contains(path, error.Message);
            }
        }

        [Theory]
        [InlineData("3.0.3")]
        [InlineData("3.1.0")]
        public async Task IntegerResponseUsesMathematicalValueWithoutRounding(string openapiVersion)
        {
            Assembly int32 = Compile("{\"type\":\"integer\",\"format\":\"int32\"}",
                openapiVersion: openapiVersion);
            foreach (string json in new[] { "1", "1.0", "1e0", "100e-2", "-0.0" })
            {
                Assert.Equal(json == "-0.0" ? 0 : 1, (int)(await InvokeResult(int32, json))!);
            }

            foreach (string json in new[]
            {
                "1.5", "1e-323", "9007199254740992.5", "2147483648.0", "NaN", "Infinity", "-Infinity"
            })
            {
                await Assert.ThrowsAsync<JsonSerializationException>(() => Invoke(int32, json));
            }

            Assembly int64 = Compile("{\"type\":\"integer\",\"format\":\"int64\"}",
                openapiVersion: openapiVersion);
            Assert.Equal(9007199254740993L, (long)(await InvokeResult(int64, "9007199254740993.0"))!);
            await Assert.ThrowsAsync<JsonSerializationException>(() => Invoke(int64, "9223372036854775808.0"));

            Assembly array = Compile("{\"type\":\"array\",\"items\":{\"type\":\"integer\"}}");
            await Invoke(array, "[1.0, 1e0]");
            await Assert.ThrowsAsync<JsonSerializationException>(() => Invoke(array, "[1.5]"));
        }

        [Theory]
        [InlineData("{\"type\":[\"number\",\"null\"],\"format\":\"double\"}", "")]
        [InlineData("{\"type\":[\"number\",\"null\"],\"format\":\"double\"}", " \n ")]
        [InlineData("{\"type\":[\"object\",\"null\"],\"additionalProperties\":false,\"properties\":{\"name\":{\"type\":\"string\"}}}", "")]
        public async Task NullableResponseStillRequiresPresentJson(string schema, string json)
        {
            Assembly assembly = Compile(schema);
            await Assert.ThrowsAsync<JsonSerializationException>(() => Invoke(assembly, json));
            await Invoke(assembly, "null");
        }

        [Theory]
        [InlineData("NaN")]
        [InlineData("Infinity")]
        [InlineData("-Infinity")]
        [InlineData("1e400")]
        public async Task NonFiniteResponseNumberIsRejected(string json)
        {
            Assembly scalar = Compile("{\"type\":\"number\",\"format\":\"double\"}");
            await Assert.ThrowsAsync<JsonSerializationException>(() => Invoke(scalar, json));
            Assembly array = Compile("{\"type\":\"array\",\"items\":{\"type\":\"number\",\"format\":\"float\"}}");
            await Assert.ThrowsAsync<JsonSerializationException>(() => Invoke(array, "[" + json + "]"));
        }

        [Fact]
        public async Task ClosedDtoRejectsUnknownPropertiesAndNestedCoercion()
        {
            const string schema = "{\"$ref\":\"#/components/schemas/Envelope\"}";
            const string components = "{\"Envelope\":{\"type\":\"object\",\"additionalProperties\":false," +
                "\"properties\":{\"child\":{\"$ref\":\"#/components/schemas/Child\"}}}," +
                "\"Child\":{\"type\":\"object\",\"additionalProperties\":false," +
                "\"properties\":{\"count\":{\"type\":\"integer\"},\"ok\":{\"type\":\"boolean\"}," +
                "\"name\":{\"type\":\"string\"},\"ratio\":{\"type\":\"number\",\"format\":\"double\"}}}}";
            Assembly assembly = Compile(schema, components);
            await Invoke(assembly, "{\"child\":{\"count\":1,\"ok\":true,\"name\":\"x\",\"ratio\":1.5}}");
            await Invoke(assembly, "{\n  \"child\":{\"count\":1.0,\"ok\":true}} ");
            await Invoke(assembly, "{\r\n  \"child\":{\"count\":1e0}} ");
            foreach (string json in new[]
            {
                "{\"unknown\":1}",
                "{\"Child\":{\"count\":1}}",
                "{\"child\":{\"unknown\":1}}",
                "{\"child\":{\"count\":\"1\"}}",
                "{\"child\":{\"count\":1.5}}",
                "{\"child\":{\"ok\":\"true\"}}",
                "{\"child\":{\"name\":1}}",
                "{\"child\":{\"ratio\":\"1.5\"}}",
                "{\"child\":{\"ratio\":NaN}}",
                "{\"child\":{\"ratio\":Infinity}}",
                "{\"child\":{\"ratio\":-Infinity}}"
            })
            {
                await Assert.ThrowsAsync<JsonSerializationException>(() => Invoke(assembly, json));
            }
        }

        [Theory]
        [InlineData("{\"child\":{\"name\":12},\"child\":{}}")]
        [InlineData("{\"child\":{\"unknown\":true},\"child\":{\"name\":\"x\"}}")]
        [InlineData("{\"child\":{\"name\":\"x\",\"name\":\"y\"}}")]
        public async Task DuplicateResponsePropertiesCannotBypassClosedDtoValidation(string json)
        {
            const string schema = "{\"$ref\":\"#/components/schemas/Envelope\"}";
            const string components = "{\"Envelope\":{\"type\":\"object\",\"additionalProperties\":false," +
                "\"properties\":{\"child\":{\"$ref\":\"#/components/schemas/Child\"}}}," +
                "\"Child\":{\"type\":\"object\",\"additionalProperties\":false," +
                "\"properties\":{\"name\":{\"type\":\"string\"}}}}";
            Assembly assembly = Compile(schema, components);

            await Assert.ThrowsAnyAsync<JsonException>(() => Invoke(assembly, json));
        }

        [Theory]
        [InlineData("0x10")]
        [InlineData("01")]
        [InlineData("1.")]
        [InlineData("+1")]
        [InlineData("NaN")]
        [InlineData("Infinity")]
        [InlineData("1e")]
        [InlineData("1 2")]
        [InlineData("/*comment*/1")]
        public async Task ResponseRejectsNonJsonNumberSyntax(string json)
        {
            Assembly assembly = Compile("{\"type\":\"integer\",\"format\":\"int64\"}");
            await Assert.ThrowsAsync<JsonSerializationException>(() => Invoke(assembly, json));
        }

        [Theory]
        [InlineData("{\"name\":\"ok\",}")]
        [InlineData("{name:\"ok\"}")]
        [InlineData("{'name':'ok'}")]
        [InlineData("{\"name\":\"\\x61\"}")]
        [InlineData("{\"name\":\"bad\nline\"}")]
        [InlineData("{\"name\":\"ok\"} //comment")]
        public async Task ResponseRejectsNonJsonObjectSyntax(string json)
        {
            Assembly assembly = Compile("{\"$ref\":\"#/components/schemas/Value\"}",
                "{\"Value\":{\"type\":\"object\",\"additionalProperties\":false," +
                "\"properties\":{\"name\":{\"type\":\"string\"}}}}");
            await Assert.ThrowsAsync<JsonSerializationException>(() => Invoke(assembly, json));
        }

        [Theory]
        [InlineData("[1,]")]
        [InlineData("[1,,2]")]
        [InlineData("[1 /*comment*/]")]
        public async Task ResponseRejectsNonJsonArraySyntax(string json)
        {
            Assembly assembly = Compile("{\"type\":\"array\",\"items\":{\"type\":\"integer\"}}");
            await Assert.ThrowsAsync<JsonSerializationException>(() => Invoke(assembly, json));
        }

        private static Assembly Compile(string schema, string? components = null, string openapiVersion = "3.1.0")
        {
            var document = new JObject
            {
                ["openapi"] = openapiVersion,
                ["info"] = new JObject { ["title"] = "Strict response", ["version"] = "1" },
                ["servers"] = new JArray(new JObject { ["url"] = "https://example.test/" }),
                ["paths"] = new JObject
                {
                    ["/value"] = new JObject
                    {
                        ["get"] = new JObject
                        {
                            ["operationId"] = "readValue",
                            ["responses"] = new JObject
                            {
                                ["200"] = new JObject
                                {
                                    ["description"] = "OK",
                                    ["content"] = new JObject
                                    {
                                        ["application/json"] = new JObject { ["schema"] = JToken.Parse(schema) }
                                    }
                                }
                            }
                        }
                    }
                }
            };
            if (components is not null)
            {
                document["components"] = new JObject { ["schemas"] = JToken.Parse(components) };
            }

            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(document.ToString(Formatting.None));
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            return execution.EmitAssembly();
        }

        private static async Task Invoke(Assembly assembly, string json)
        {
            await InvokeResult(assembly, json);
        }

        private static async Task<object?> InvokeResult(Assembly assembly, string json)
        {
            using var client = new HttpClient(new FixedResponseHandler(json));
            object api = Activator.CreateInstance(assembly.GetType("Generated.Phase4.Phase4Api")!, client)!;
            var task = (Task)api.GetType().GetMethod("readValue")!
                .Invoke(api, new object[] { CancellationToken.None })!;
            await task;
            return task.GetType().GetProperty("Result")!.GetValue(task);
        }

        private sealed class FixedResponseHandler : HttpMessageHandler
        {
            private readonly string _json;

            internal FixedResponseHandler(string json) => _json = json;

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_json, Encoding.UTF8, "application/json")
                });
        }
    }
}
