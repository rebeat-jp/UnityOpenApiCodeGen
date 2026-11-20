using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    /// <summary>
    /// A const on a typed value (response header, parameter, member of a request body) is checked at run
    /// time like an enum: a response value that differs raises the serializer's JSON exception, a request
    /// value that differs is an ArgumentException before anything is sent. The values have no JSON
    /// converter that compares them, so the generated validators do.
    /// </summary>
    public sealed class TypedConstTests
    {
        private const string Spec = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Const"", ""version"": ""1"" },
  ""servers"": [ { ""url"": ""https://api.example.test"" } ],
  ""paths"": {
    ""/header"": {
      ""get"": {
        ""operationId"": ""readConstHeaders"",
        ""responses"": {
          ""204"": {
            ""description"": ""ok"",
            ""headers"": {
              ""X-Kind"": { ""required"": true, ""schema"": { ""type"": ""string"", ""const"": ""avatar"" } },
              ""X-Flag"": { ""schema"": { ""type"": ""boolean"", ""const"": true } },
              ""X-Level"": { ""schema"": { ""type"": ""integer"", ""const"": 3 } },
              ""X-Ratio"": { ""schema"": { ""type"": ""number"", ""const"": 1.5 } }
            }
          }
        }
      }
    },
    ""/param/{kind}"": {
      ""get"": {
        ""operationId"": ""sendConstParameters"",
        ""parameters"": [
          { ""name"": ""kind"", ""in"": ""path"", ""required"": true, ""schema"": { ""type"": ""string"", ""const"": ""avatar"" } },
          { ""name"": ""level"", ""in"": ""query"", ""schema"": { ""type"": ""integer"", ""const"": 3 } },
          { ""name"": ""X-Mode"", ""in"": ""header"", ""schema"": { ""type"": ""string"", ""const"": ""fast"" } }
        ],
        ""responses"": { ""204"": { ""description"": ""ok"" } }
      }
    },
    ""/body"": {
      ""post"": {
        ""operationId"": ""sendConstBody"",
        ""requestBody"": {
          ""required"": true,
          ""content"": { ""application/json"": { ""schema"": { ""$ref"": ""#/components/schemas/Tagged"" } } }
        },
        ""responses"": { ""204"": { ""description"": ""ok"" } }
      }
    },
    ""/text"": {
      ""get"": {
        ""operationId"": ""readConstText"",
        ""responses"": {
          ""200"": {
            ""description"": ""ok"",
            ""content"": { ""text/plain"": { ""schema"": { ""type"": ""string"", ""const"": ""pong"" } } }
          }
        }
      }
    }
  },
  ""components"": {
    ""schemas"": {
      ""Tagged"": {
        ""type"": ""object"",
        ""properties"": { ""kind"": { ""type"": ""string"", ""const"": ""avatar"" } }
      }
    }
  }
}";

        private static Assembly Api(string serializer) =>
            SerializerMatrixHarness.CompileText("typed-const", Spec, serializer);

        private static Type ResponseException(string serializer) =>
            SerializerMatrixHarness.IsStj(serializer)
                ? typeof(System.Text.Json.JsonException)
                : typeof(Newtonsoft.Json.JsonSerializationException);

        private static async Task<Exception?> ReadHeaders(string serializer, params (string Name, string Value)[] headers)
        {
            try
            {
                await Phase4BodyWireHarness.Call(
                    Api(serializer),
                    "readConstHeaders",
                    _ => Phase4BodyWireHarness.Respond(HttpStatusCode.NoContent, null, "application/json", headers))
                    .ConfigureAwait(false);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        [Theory]
        [InlineData(SerializerMatrixHarness.NewtonsoftJson)]
        [InlineData(SerializerMatrixHarness.SystemTextJson)]
        public async Task ResponseHeadersEqualToTheConstAreAccepted(string serializer)
        {
            Assert.Null(await ReadHeaders(
                serializer, ("X-Kind", "avatar"), ("X-Flag", "true"), ("X-Level", "3"), ("X-Ratio", "1.5")));
            Assert.Null(await ReadHeaders(serializer, ("X-Kind", "avatar")));
        }

        [Theory]
        [InlineData(SerializerMatrixHarness.NewtonsoftJson, "X-Kind", "no-cache")]
        [InlineData(SerializerMatrixHarness.SystemTextJson, "X-Kind", "no-cache")]
        [InlineData(SerializerMatrixHarness.NewtonsoftJson, "X-Kind", "Avatar")]
        [InlineData(SerializerMatrixHarness.SystemTextJson, "X-Kind", "Avatar")]
        [InlineData(SerializerMatrixHarness.NewtonsoftJson, "X-Flag", "false")]
        [InlineData(SerializerMatrixHarness.SystemTextJson, "X-Flag", "false")]
        [InlineData(SerializerMatrixHarness.NewtonsoftJson, "X-Level", "4")]
        [InlineData(SerializerMatrixHarness.SystemTextJson, "X-Level", "4")]
        [InlineData(SerializerMatrixHarness.NewtonsoftJson, "X-Ratio", "1.25")]
        [InlineData(SerializerMatrixHarness.SystemTextJson, "X-Ratio", "1.25")]
        public async Task AResponseHeaderThatDiffersFromTheConstIsRejected(string serializer, string header, string value)
        {
            (string, string)[] headers = header == "X-Kind"
                ? new[] { ("X-Kind", value) }
                : new[] { ("X-Kind", "avatar"), (header, value) };
            Exception? error = await ReadHeaders(serializer, headers);
            Assert.NotNull(error);
            Assert.IsType(ResponseException(serializer), error);
            Assert.Contains("const", error!.Message);
            Assert.Contains("header:" + header, error.Message);
            Assert.DoesNotContain(value, error.Message);
        }

        [Theory]
        [InlineData(SerializerMatrixHarness.NewtonsoftJson)]
        [InlineData(SerializerMatrixHarness.SystemTextJson)]
        public async Task ATextBodyThatDiffersFromTheConstIsRejected(string serializer)
        {
            async Task<Exception?> Read(string body)
            {
                try
                {
                    await Phase4BodyWireHarness.Call(
                        Api(serializer),
                        "readConstText",
                        _ => Phase4BodyWireHarness.Respond(HttpStatusCode.OK, body, "text/plain")).ConfigureAwait(false);
                    return null;
                }
                catch (Exception exception)
                {
                    return exception;
                }
            }

            Assert.Null(await Read("pong"));
            Exception? error = await Read("ping");
            Assert.IsType(ResponseException(serializer), error);
            Assert.Contains("const", error!.Message);
        }

        // The arguments are given as (kind, level, xMode) and placed by parameter name.
        private static async Task<(Exception? Error, int Calls)> SendParameters(string serializer, params object?[] values)
        {
            Assembly api = Api(serializer);
            ParameterInfo[] parameters = Phase4WireHarness.ClientType(api).GetMethod("sendConstParameters")!.GetParameters();
            string[] names = { "kind", "level", "xMode" };
            var arguments = new object?[parameters.Length - 1];
            for (int index = 0; index < arguments.Length; index++)
            {
                arguments[index] = values[Array.FindIndex(names, name =>
                    string.Equals(name, parameters[index].Name, StringComparison.OrdinalIgnoreCase))];
            }

            int calls = 0;
            try
            {
                await Phase4BodyWireHarness.Call(
                    api,
                    "sendConstParameters",
                    _ =>
                    {
                        calls++;
                        return Phase4BodyWireHarness.Respond(HttpStatusCode.NoContent);
                    },
                    arguments).ConfigureAwait(false);
                return (null, calls);
            }
            catch (Exception exception)
            {
                return (exception, calls);
            }
        }

        [Theory]
        [InlineData(SerializerMatrixHarness.NewtonsoftJson)]
        [InlineData(SerializerMatrixHarness.SystemTextJson)]
        public async Task ParametersThatDifferFromTheConstAreRejectedBeforeSending(string serializer)
        {
            (Exception? error, int calls) = await SendParameters(serializer, "avatar", 3, "fast");
            Assert.Null(error);
            Assert.Equal(1, calls);

            (error, _) = await SendParameters(serializer, "avatar", null, null);
            Assert.Null(error);

            (string Name, object?[] Arguments)[] cases =
            {
                ("kind", new object?[] { "no-cache", 3, "fast" }),
                ("level", new object?[] { "avatar", 4, "fast" }),
                ("xMode", new object?[] { "avatar", 3, "slow" }),
            };
            foreach ((string name, object?[] arguments) in cases)
            {
                (error, calls) = await SendParameters(serializer, arguments);
                ArgumentException argument = Assert.IsType<ArgumentException>(error);
                Assert.Contains("const", argument.Message);
                Assert.Equal(0, calls);
                Assert.Equal(name, argument.ParamName, StringComparer.OrdinalIgnoreCase);
            }
        }

        [Theory]
        [InlineData(SerializerMatrixHarness.NewtonsoftJson)]
        [InlineData(SerializerMatrixHarness.SystemTextJson)]
        public async Task ARequestBodyMemberThatDiffersFromTheConstIsRejectedBeforeSending(string serializer)
        {
            Assembly api = Api(serializer);
            int calls = 0;
            Func<HttpRequestMessage, HttpResponseMessage> respond = _ =>
            {
                calls++;
                return Phase4BodyWireHarness.Respond(HttpStatusCode.NoContent);
            };

            // The member starts at its const, so a body built without touching it is sent.
            await Phase4BodyWireHarness.Call(api, "sendConstBody", respond, SerializerMatrixHarness.New(api, "Tagged"));
            Assert.Equal(1, calls);

            object changed = SerializerMatrixHarness.New(api, "Tagged", ("Kind", "other"));
            ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(
                () => Phase4BodyWireHarness.Call(api, "sendConstBody", respond, changed));
            Assert.Contains("const", error.Message);
            Assert.Equal(1, calls);
        }
    }
}
