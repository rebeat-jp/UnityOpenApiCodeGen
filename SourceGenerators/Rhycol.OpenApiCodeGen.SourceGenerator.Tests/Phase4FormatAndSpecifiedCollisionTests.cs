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
    public sealed class Phase4FormatAndSpecifiedCollisionTests
    {
        [Fact]
        public void FormatNamesAreCaseSensitiveAndUnknownCasingUsesBaseType()
        {
            var properties = new JObject();
            AddFormat("date", "string", "date");
            AddFormat("dateUpper", "string", "Date");
            AddFormat("dateTime", "string", "date-time");
            AddFormat("dateTimeUpper", "string", "Date-Time");
            AddFormat("uuid", "string", "uuid");
            AddFormat("uuidUpper", "string", "UUID");
            AddFormat("float", "number", "float");
            AddFormat("floatUpper", "number", "Float");
            AddFormat("decimal", "number", "decimal");
            AddFormat("decimalUpper", "number", "Decimal");
            AddFormat("int64", "integer", "int64");
            AddFormat("int64Upper", "integer", "INT64");

            Assembly assembly = Generate(new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = new JArray(
                    "date", "dateUpper", "dateTime", "dateTimeUpper", "uuid", "uuidUpper",
                    "float", "floatUpper", "decimal", "decimalUpper", "int64", "int64Upper"),
                ["properties"] = properties
            }, includeOperation: false);
            Type payload = assembly.GetType("Generated.Phase4.Payload", throwOnError: true)!;

            Assert.Equal(typeof(DateTime), payload.GetProperty("Date")!.PropertyType);
            Assert.Equal(typeof(string), payload.GetProperty("DateUpper")!.PropertyType);
            Assert.Equal(typeof(DateTimeOffset), payload.GetProperty("DateTime")!.PropertyType);
            Assert.Equal(typeof(string), payload.GetProperty("DateTimeUpper")!.PropertyType);
            Assert.Equal(typeof(Guid), payload.GetProperty("Uuid")!.PropertyType);
            Assert.Equal(typeof(string), payload.GetProperty("UuidUpper")!.PropertyType);
            Assert.Equal(typeof(float), payload.GetProperty("Float")!.PropertyType);
            Assert.Equal(typeof(double), payload.GetProperty("FloatUpper")!.PropertyType);
            Assert.Equal(typeof(decimal), payload.GetProperty("Decimal")!.PropertyType);
            Assert.Equal(typeof(double), payload.GetProperty("DecimalUpper")!.PropertyType);
            Assert.Equal(typeof(long), payload.GetProperty("Int64")!.PropertyType);
            Assert.Equal(typeof(int), payload.GetProperty("Int64Upper")!.PropertyType);

            void AddFormat(string name, string type, string format)
            {
                properties[name] = new JObject { ["type"] = type, ["format"] = format };
            }
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, false)]
        [InlineData(true, true)]
        [InlineData(false, true)]
        public async Task SchemaBooleanNamedFooSpecifiedCannotSuppressFooInRealRequest(
            bool requiredNullableFoo,
            bool chainedName)
        {
            JToken fooType = requiredNullableFoo
                ? new JArray("string", "null")
                : new JValue("string");
            var requiredNames = requiredNullableFoo
                ? new JArray("foo", "fooSpecified")
                : new JArray("fooSpecified");
            var properties = new JObject
            {
                ["foo"] = new JObject { ["type"] = fooType },
                ["fooSpecified"] = new JObject { ["type"] = "boolean" },
                ["maybe"] = new JObject { ["type"] = new JArray("string", "null") }
            };
            if (chainedName)
            {
                // This wire name sorts before foo, so its CLR name exists before
                // FooSpecified is renamed and can become a second presence flag.
                properties["FooSpecified2Specified"] = new JObject { ["type"] = "boolean" };
                requiredNames.Add("FooSpecified2Specified");
            }
            var payloadSchema = new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = requiredNames,
                ["properties"] = properties
            };
            Assembly assembly = Generate(payloadSchema, includeOperation: true);
            Type clientType = assembly.GetType("Generated.Phase4.Phase4Api", throwOnError: true)!;
            var handler = new BodyRecordingHandler();
            using var httpClient = new HttpClient(handler);
            object client = Activator.CreateInstance(clientType, httpClient)!;
            MethodInfo operation = clientType.GetMethod("sendPayload")!;
            object body = Activator.CreateInstance(operation.GetParameters()[0].ParameterType)!;
            Type bodyType = body.GetType();

            string schemaFlagName = chainedName ? "FooSpecified3" : "FooSpecified2";
            Assert.NotNull(bodyType.GetProperty(schemaFlagName));
            bodyType.GetProperty("Foo")!.SetValue(body, requiredNullableFoo ? null : "value");
            bodyType.GetProperty(schemaFlagName)!.SetValue(body, false);
            if (chainedName)
            {
                bodyType.GetProperty("FooSpecified2Specified")!.SetValue(body, false);
            }
            bodyType.GetProperty("Maybe")!.SetValue(body, null);
            Assert.True((bool)bodyType.GetProperty("MaybeSpecified")!.GetValue(body)!);

            await (Task)operation.Invoke(client, new object[] { body, CancellationToken.None })!;
            JObject sent = JObject.Parse(handler.RequestBody!);
            Assert.True(sent.ContainsKey("foo"), sent.ToString());
            Assert.Equal(requiredNullableFoo ? JTokenType.Null : JTokenType.String, sent["foo"]!.Type);
            Assert.Equal(false, (bool?)sent["fooSpecified"]);
            if (chainedName) Assert.Equal(false, (bool?)sent["FooSpecified2Specified"]);
            Assert.Equal(JTokenType.Null, sent["maybe"]!.Type);

            bodyType.GetProperty("MaybeSpecified")!.SetValue(body, false);
            await (Task)operation.Invoke(client, new object[] { body, CancellationToken.None })!;
            JObject withoutMaybe = JObject.Parse(handler.RequestBody!);
            Assert.True(withoutMaybe.ContainsKey("foo"));
            Assert.Equal(false, (bool?)withoutMaybe["fooSpecified"]);
            if (chainedName) Assert.Equal(false, (bool?)withoutMaybe["FooSpecified2Specified"]);
            Assert.False(withoutMaybe.ContainsKey("maybe"));
        }

        private static Assembly Generate(JObject payloadSchema, bool includeOperation)
        {
            var document = new JObject
            {
                ["openapi"] = "3.1.0",
                ["info"] = new JObject { ["title"] = "Format and name collision", ["version"] = "1" },
                ["servers"] = new JArray(new JObject { ["url"] = "https://example.test/" }),
                ["paths"] = new JObject(),
                ["components"] = new JObject
                {
                    ["schemas"] = new JObject { ["Payload"] = payloadSchema }
                }
            };
            if (includeOperation)
            {
                document["paths"] = new JObject
                {
                    ["/payload"] = new JObject
                    {
                        ["post"] = new JObject
                        {
                            ["operationId"] = "sendPayload",
                            ["requestBody"] = new JObject
                            {
                                ["required"] = true,
                                ["content"] = new JObject
                                {
                                    ["application/json"] = new JObject
                                    {
                                        ["schema"] = new JObject
                                        {
                                            ["$ref"] = "#/components/schemas/Payload"
                                        }
                                    }
                                }
                            },
                            ["responses"] = new JObject
                            {
                                ["204"] = new JObject { ["description"] = "Done" }
                            }
                        }
                    }
                };
            }

            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                document.ToString(Formatting.None));
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            return execution.EmitAssembly();
        }

        private sealed class BodyRecordingHandler : HttpMessageHandler
        {
            internal string? RequestBody { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                RequestBody = await request.Content!.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
        }
    }
}
