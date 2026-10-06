using System;
using System.Collections;
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
    public sealed class Phase4RequestSpecifiedArrayTests
    {
        [Theory]
        [InlineData(false, false, null)]
        [InlineData(false, true, "$.values[0]")]
        [InlineData(true, false, null)]
        [InlineData(true, true, "$.child.values[0]")]
        public async Task OptionalNullableDtoPropertyIsValidatedOnlyWhenSpecified(
            bool nestedDto,
            bool specified,
            string? rejectedPath)
        {
            Assembly assembly = GenerateAndCompile(nestedDto);
            var handler = new RecordingHandler();
            using var httpClient = new HttpClient(handler);
            object api = Activator.CreateInstance(assembly.GetType("Generated.Phase4.Phase4Api")!, httpClient)!;
            MethodInfo method = api.GetType().GetMethod("sendBody")!;
            object body = Activator.CreateInstance(method.GetParameters()[0].ParameterType)!;

            if (nestedDto)
            {
                PropertyInfo childProperty = body.GetType().GetProperty("Child")!;
                object child = Activator.CreateInstance(childProperty.PropertyType)!;
                SetArrayWithNull(child, "Values");
                childProperty.SetValue(body, child);
                body.GetType().GetProperty("ChildSpecified")!.SetValue(body, specified);
            }
            else
            {
                SetArrayWithNull(body, "Values");
                body.GetType().GetProperty("ValuesSpecified")!.SetValue(body, specified);
            }

            var task = (Task)method.Invoke(api, new object?[] { body, CancellationToken.None })!;
            if (rejectedPath is not null)
            {
                JsonSerializationException exception = await Assert.ThrowsAsync<JsonSerializationException>(
                    async () => await task);
                Assert.Contains(rejectedPath, exception.Message);
                Assert.Equal(0, handler.SendCount);
            }
            else
            {
                await task;
                Assert.Equal(1, handler.SendCount);
                Assert.Equal("{}", handler.Content);
            }
        }

        private static void SetArrayWithNull(object target, string propertyName)
        {
            PropertyInfo property = target.GetType().GetProperty(propertyName)!;
            var values = (IList)Activator.CreateInstance(property.PropertyType)!;
            values.Add(null);
            property.SetValue(target, values);
        }

        private static Assembly GenerateAndCompile(bool nestedDto)
        {
            var envelope = new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject()
            };
            var schemas = new JObject { ["Envelope"] = envelope };
            if (nestedDto)
            {
                ((JObject)envelope["properties"]!)["child"] = new JObject
                {
                    ["$ref"] = "#/components/schemas/Child"
                };
                schemas["Child"] = new JObject
                {
                    ["type"] = new JArray("object", "null"),
                    ["additionalProperties"] = false,
                    ["required"] = new JArray("values"),
                    ["properties"] = new JObject
                    {
                        ["values"] = new JObject
                        {
                            ["type"] = "array",
                            ["items"] = new JObject { ["type"] = "string" }
                        }
                    }
                };
            }
            else
            {
                ((JObject)envelope["properties"]!)["values"] = new JObject
                {
                    ["type"] = new JArray("array", "null"),
                    ["items"] = new JObject { ["type"] = "string" }
                };
            }

            var document = new JObject
            {
                ["openapi"] = "3.1.0",
                ["info"] = new JObject { ["title"] = "Specified request array", ["version"] = "1" },
                ["servers"] = new JArray(new JObject { ["url"] = "https://example.test/" }),
                ["paths"] = new JObject
                {
                    ["/body"] = new JObject
                    {
                        ["post"] = new JObject
                        {
                            ["operationId"] = "sendBody",
                            ["requestBody"] = new JObject
                            {
                                ["required"] = true,
                                ["content"] = new JObject
                                {
                                    ["application/json"] = new JObject
                                    {
                                        ["schema"] = new JObject { ["$ref"] = "#/components/schemas/Envelope" }
                                    }
                                }
                            },
                            ["responses"] = new JObject
                            {
                                ["204"] = new JObject { ["description"] = "Done" }
                            }
                        }
                    }
                },
                ["components"] = new JObject { ["schemas"] = schemas }
            };

            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                document.ToString(Formatting.None));
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            return execution.EmitAssembly();
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            internal int SendCount { get; private set; }
            internal string? Content { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                SendCount++;
                Content = request.Content is null ? null : await request.Content.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
        }
    }
}
