using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;

using Rhycol.OpenApiCodeGen.SourceGenerator.Editor;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    /// <summary>
    /// P9 (G32): Swagger 2.0. A Swagger 2.0 document is normalized into an OpenAPI 3.0 SpecNode tree before
    /// semantic parsing (Swagger2Normalizer), so the Phase 4 pipeline applies unchanged. The tests cover
    /// the normalized tree (every mapping rule), the generated clients against a fake HttpMessageHandler
    /// (wire format and deserialization), diagnostics that keep the original Swagger location, the
    /// equivalence with a hand-written OpenAPI 3.0 document and the Editor bundling of Swagger documents.
    /// </summary>
    public sealed class Phase9Swagger2Tests
    {
        private const string SpecId = "0123456789abcdef0123456789abcdef";

        private static readonly Lazy<Assembly> PetstoreAssembly = new Lazy<Assembly>(() =>
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(ReadSpec("swagger2.json"));
            if (execution.RunResult.Diagnostics.Length > 0 || execution.CompilationErrors.Count > 0)
            {
                throw new InvalidOperationException(
                    "swagger2.json: " +
                    string.Join("; ", execution.RunResult.Diagnostics.Select(static value => value.ToString())) +
                    string.Join("; ", execution.CompilationErrors.Select(static value => value.ToString())));
            }

            return execution.EmitAssembly();
        });

        private static string ReadSpec(string name)
        {
            return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ApiSurfaceSpecs", name));
        }

        private static string Swagger(
            string paths,
            string definitions = "",
            string extraRoot = "",
            string hostAndBase = "\"host\":\"api.example.test\",\"basePath\":\"/v1\",")
        {
            return "{\"swagger\":\"2.0\",\"info\":{\"title\":\"P9\",\"version\":\"1\"}," + hostAndBase + extraRoot +
                   "\"paths\":{" + paths + "}" +
                   (definitions.Length > 0 ? ",\"definitions\":{" + definitions + "}" : string.Empty) + "}";
        }

        private static Phase4GeneratorExecution Generate(string spec)
        {
            return Phase4GeneratorTestHarness.GenerateAndCompile(spec);
        }

        private static Diagnostic SingleDiagnostic(string spec, string id)
        {
            Phase4GeneratorExecution execution = Generate(spec);
            Diagnostic diagnostic = Assert.Single(execution.RunResult.Diagnostics);
            Assert.Equal(id, diagnostic.Id);
            return diagnostic;
        }

        private static void AssertLogicalPath(Diagnostic diagnostic, string logicalPath)
        {
            Assert.Contains("logical path '" + logicalPath + "'", diagnostic.GetMessage(), StringComparison.Ordinal);
        }

        private static void AssertClean(Phase4GeneratorExecution execution)
        {
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
        }

        // ---- the normalized tree ---------------------------------------------------------------------

        private static SpecNode Normalized(string swaggerJson)
        {
            return Swagger2Normalizer.Normalize(TestBundleFactory.ReadRoot(swaggerJson));
        }

        private static SpecNode At(SpecNode root, string pointer)
        {
            SpecNode current = root;
            foreach (string encoded in pointer.Split('/').Skip(1))
            {
                string token = encoded.Replace("~1", "/").Replace("~0", "~");
                if (current.ValueKind == SpecValueKind.Array)
                {
                    current = current.EnumerateArray().ElementAt(int.Parse(token));
                }
                else
                {
                    Assert.True(current.TryGetProperty(token, out SpecNode child), "Missing " + pointer);
                    current = child;
                }
            }

            return current;
        }

        private static string Json(SpecNode root, string pointer)
        {
            return At(root, pointer).GetRawJson();
        }

        [Fact]
        public void HostBasePathAndSchemesBecomeServers()
        {
            SpecNode root = Normalized(Swagger(string.Empty, hostAndBase:
                "\"host\":\"api.example.test:8443\",\"basePath\":\"/v1/\",\"schemes\":[\"https\",\"http\"],"));

            Assert.Equal("\"3.0.3\"", Json(root, "/openapi"));
            Assert.Equal(
                "[{\"url\":\"https://api.example.test:8443/v1\"},{\"url\":\"http://api.example.test:8443/v1\"}]",
                Json(root, "/servers"));
            Assert.False(root.TryGetProperty("host", out _));
            Assert.False(root.TryGetProperty("basePath", out _));
            Assert.False(root.TryGetProperty("schemes", out _));
        }

        [Fact]
        public void HostWithoutSchemesDefaultsToHttpsAndBasePathSlashIsDropped()
        {
            SpecNode root = Normalized(Swagger(string.Empty, hostAndBase: "\"host\":\"api.example.test\",\"basePath\":\"/\","));

            Assert.Equal("[{\"url\":\"https://api.example.test\"}]", Json(root, "/servers"));
        }

        [Fact]
        public void WithoutHostTheServerIsTheRelativeBasePathOrSlash()
        {
            Assert.Equal(
                "[{\"url\":\"/v2\"}]",
                Json(Normalized(Swagger(string.Empty, hostAndBase: "\"basePath\":\"/v2\",\"schemes\":[\"https\"],")), "/servers"));
            Assert.Equal(
                "[{\"url\":\"/\"}]",
                Json(Normalized(Swagger(string.Empty, hostAndBase: "\"schemes\":[\"https\"],")), "/servers"));
            Assert.False(Normalized(Swagger(string.Empty, hostAndBase: string.Empty)).TryGetProperty("servers", out _));
        }

        [Fact]
        public void OperationSchemesBecomeOperationServers()
        {
            SpecNode root = Normalized(Swagger(
                "\"/p\":{\"get\":{\"schemes\":[\"http\"],\"responses\":{\"204\":{\"description\":\"d\"}}}}"));

            Assert.Equal("[{\"url\":\"http://api.example.test/v1\"}]", Json(root, "/paths/~1p/get/servers"));
        }

        [Fact]
        public void DefinitionsParametersResponsesAndSecurityDefinitionsMoveToComponents()
        {
            SpecNode root = Normalized(Swagger(
                "\"/p\":{\"get\":{\"parameters\":[{\"$ref\":\"#/parameters/Lim\"}]," +
                "\"responses\":{\"200\":{\"$ref\":\"#/responses/Ok\"}}}}",
                "\"Pet\":{\"type\":\"object\",\"properties\":{\"p\":{\"$ref\":\"#/definitions/Pet\"}}}",
                "\"parameters\":{\"Lim\":{\"name\":\"limit\",\"in\":\"query\",\"type\":\"integer\"}}," +
                "\"responses\":{\"Ok\":{\"description\":\"ok\",\"schema\":{\"$ref\":\"#/definitions/Pet\"}}}," +
                "\"securityDefinitions\":{\"k\":{\"type\":\"apiKey\",\"name\":\"X\",\"in\":\"query\"}},"));

            Assert.Equal(
                "{\"type\":\"object\",\"properties\":{\"p\":{\"$ref\":\"#/components/schemas/Pet\"}}}",
                Json(root, "/components/schemas/Pet"));
            Assert.Equal(
                "{\"name\":\"limit\",\"in\":\"query\",\"schema\":{\"type\":\"integer\"}}",
                Json(root, "/components/parameters/Lim"));
            Assert.Equal("[{\"$ref\":\"#/components/parameters/Lim\"}]", Json(root, "/paths/~1p/get/parameters"));
            Assert.Equal(
                "{\"description\":\"ok\",\"content\":{\"application/json\":{\"schema\":{\"$ref\":\"#/components/schemas/Pet\"}}}}",
                Json(root, "/components/responses/Ok"));
            Assert.Equal("{\"$ref\":\"#/components/responses/Ok\"}", Json(root, "/paths/~1p/get/responses/200"));
            Assert.Equal(
                "{\"k\":{\"type\":\"apiKey\",\"name\":\"X\",\"in\":\"query\"}}",
                Json(root, "/components/securitySchemes"));
            Assert.False(root.TryGetProperty("definitions", out _));
            Assert.False(root.TryGetProperty("securityDefinitions", out _));
        }

        [Fact]
        public void ABodyParameterBecomesARequestBodyForEveryConsumesMediaType()
        {
            SpecNode root = Normalized(Swagger(
                "\"/p\":{\"post\":{\"consumes\":[\"application/json\",\"text/json\"],\"parameters\":[" +
                "{\"name\":\"b\",\"in\":\"body\",\"required\":true,\"description\":\"the body\",\"schema\":{\"$ref\":\"#/definitions/T\"}}]," +
                "\"responses\":{\"204\":{\"description\":\"d\"}}}}",
                "\"T\":{\"type\":\"string\"}"));

            Assert.Equal(
                "{\"description\":\"the body\",\"required\":true,\"content\":{" +
                "\"application/json\":{\"schema\":{\"$ref\":\"#/components/schemas/T\"}}," +
                "\"text/json\":{\"schema\":{\"$ref\":\"#/components/schemas/T\"}}}}",
                Json(root, "/paths/~1p/post/requestBody"));
            Assert.False(At(root, "/paths/~1p/post").TryGetProperty("parameters", out _));
            Assert.False(At(root, "/paths/~1p/post").TryGetProperty("consumes", out _));
        }

        [Fact]
        public void TheGlobalConsumesAndTheJsonDefaultApplyToABodyParameter()
        {
            string body = "\"parameters\":[{\"name\":\"b\",\"in\":\"body\",\"schema\":{\"type\":\"string\"}}]," +
                          "\"responses\":{\"204\":{\"description\":\"d\"}}";
            SpecNode global = Normalized(Swagger(
                "\"/p\":{\"post\":{" + body + "}}",
                extraRoot: "\"consumes\":[\"text/plain\"],"));
            SpecNode none = Normalized(Swagger("\"/p\":{\"post\":{" + body + "}}"));
            SpecNode overridden = Normalized(Swagger(
                "\"/p\":{\"post\":{\"consumes\":[\"text/csv\"]," + body + "}}",
                extraRoot: "\"consumes\":[\"text/plain\"],"));

            Assert.Equal("{\"text/plain\":{\"schema\":{\"type\":\"string\"}}}", Json(global, "/paths/~1p/post/requestBody/content"));
            Assert.Equal("{\"application/json\":{\"schema\":{\"type\":\"string\"}}}", Json(none, "/paths/~1p/post/requestBody/content"));
            Assert.Equal("{\"text/csv\":{\"schema\":{\"type\":\"string\"}}}", Json(overridden, "/paths/~1p/post/requestBody/content"));
        }

        [Fact]
        public void AFileParameterMakesTheFormMultipartAndFilesAreBinary()
        {
            SpecNode root = Normalized(Swagger(
                "\"/p\":{\"post\":{\"parameters\":[" +
                "{\"name\":\"title\",\"in\":\"formData\",\"type\":\"string\",\"required\":true,\"description\":\"T\"}," +
                "{\"name\":\"doc\",\"in\":\"formData\",\"type\":\"file\"}]," +
                "\"responses\":{\"204\":{\"description\":\"d\"}}}}"));

            Assert.Equal(
                "{\"required\":true,\"content\":{\"multipart/form-data\":{\"schema\":{\"type\":\"object\"," +
                "\"additionalProperties\":false,\"properties\":{\"title\":{\"type\":\"string\",\"description\":\"T\"}," +
                "\"doc\":{\"type\":\"string\",\"format\":\"binary\"}},\"required\":[\"title\"]}}}}",
                Json(root, "/paths/~1p/post/requestBody"));
        }

        [Fact]
        public void FormDataIsUrlEncodedUnlessTheConsumesListsMultipart()
        {
            string parameters = "\"parameters\":[{\"name\":\"a\",\"in\":\"formData\",\"type\":\"string\"}]," +
                                "\"responses\":{\"204\":{\"description\":\"d\"}}";
            SpecNode urlEncoded = Normalized(Swagger("\"/p\":{\"post\":{" + parameters + "}}"));
            SpecNode multipart = Normalized(Swagger(
                "\"/p\":{\"post\":{\"consumes\":[\"multipart/form-data\"]," + parameters + "}}"));
            SpecNode unrelated = Normalized(Swagger(
                "\"/p\":{\"post\":{\"consumes\":[\"application/json\"]," + parameters + "}}"));

            Assert.Equal(
                new[] { "application/x-www-form-urlencoded" },
                At(urlEncoded, "/paths/~1p/post/requestBody/content").EnumerateObject().Select(static p => p.Name));
            Assert.Equal(
                new[] { "multipart/form-data" },
                At(multipart, "/paths/~1p/post/requestBody/content").EnumerateObject().Select(static p => p.Name));
            Assert.Equal(
                new[] { "application/x-www-form-urlencoded" },
                At(unrelated, "/paths/~1p/post/requestBody/content").EnumerateObject().Select(static p => p.Name));
        }

        [Theory]
        [InlineData("query", "csv", "\"style\":\"form\",\"explode\":false")]
        [InlineData("query", null, "\"style\":\"form\",\"explode\":false")]
        [InlineData("query", "ssv", "\"style\":\"spaceDelimited\",\"explode\":false")]
        [InlineData("query", "pipes", "\"style\":\"pipeDelimited\",\"explode\":false")]
        [InlineData("query", "multi", "")]
        [InlineData("header", "csv", "")]
        [InlineData("header", null, "")]
        public void CollectionFormatBecomesStyleAndExplode(string location, string? collectionFormat, string expectedStyle)
        {
            string format = collectionFormat is null ? string.Empty : ",\"collectionFormat\":\"" + collectionFormat + "\"";
            string required = location == "path" ? ",\"required\":true" : string.Empty;
            SpecNode root = Normalized(Swagger(
                "\"/p\":{\"get\":{\"parameters\":[{\"name\":\"v\",\"in\":\"" + location + "\"" + required +
                ",\"type\":\"array\",\"items\":{\"type\":\"string\"}" + format + "}]," +
                "\"responses\":{\"204\":{\"description\":\"d\"}}}}"));

            string expected = "{\"name\":\"v\",\"in\":\"" + location + "\"" + required + "," +
                              (expectedStyle.Length > 0 ? expectedStyle + "," : string.Empty) +
                              "\"schema\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}}";
            Assert.Equal(expected, Json(root, "/paths/~1p/get/parameters/0"));
        }

        [Fact]
        public void NonBodyParameterFieldsMoveIntoTheSchema()
        {
            SpecNode root = Normalized(Swagger(
                "\"/p\":{\"get\":{\"parameters\":[{\"name\":\"n\",\"in\":\"query\",\"description\":\"d\",\"type\":\"integer\"," +
                "\"format\":\"int32\",\"minimum\":1,\"maximum\":9,\"exclusiveMaximum\":true,\"default\":3,\"enum\":[1,3,9]," +
                "\"x-flag\":true}],\"responses\":{\"204\":{\"description\":\"d\"}}}}"));

            Assert.Equal(
                "{\"name\":\"n\",\"in\":\"query\",\"description\":\"d\",\"x-flag\":true,\"schema\":{\"type\":\"integer\"," +
                "\"format\":\"int32\",\"minimum\":1,\"maximum\":9,\"exclusiveMaximum\":true,\"default\":3,\"enum\":[1,3,9]}}",
                Json(root, "/paths/~1p/get/parameters/0"));
        }

        [Fact]
        public void PathLevelParametersStayPathLevelAndAFormParameterOverridesByNameAndLocation()
        {
            SpecNode root = Normalized(Swagger(
                "\"/p/{id}\":{\"parameters\":[{\"name\":\"id\",\"in\":\"path\",\"required\":true,\"type\":\"string\"}," +
                "{\"name\":\"f\",\"in\":\"formData\",\"type\":\"string\"}]," +
                "\"post\":{\"parameters\":[{\"name\":\"f\",\"in\":\"formData\",\"type\":\"integer\"}]," +
                "\"responses\":{\"204\":{\"description\":\"d\"}}}}"));

            Assert.Equal(
                "[{\"name\":\"id\",\"in\":\"path\",\"required\":true,\"schema\":{\"type\":\"string\"}}]",
                Json(root, "/paths/~1p~1{id}/parameters"));
            Assert.Equal(
                "{\"type\":\"integer\"}",
                Json(root, "/paths/~1p~1{id}/post/requestBody/content/application~1x-www-form-urlencoded/schema/properties/f"));
        }

        [Fact]
        public void ResponsesGetTheProducesMediaTypesFilesAreBinaryAndHeadersGetASchema()
        {
            SpecNode root = Normalized(Swagger(
                "\"/p\":{\"get\":{\"produces\":[\"image/png\",\"image/jpeg\"],\"responses\":{" +
                "\"200\":{\"description\":\"f\",\"schema\":{\"type\":\"file\"},\"examples\":{\"image/png\":\"x\"}," +
                "\"headers\":{\"X-Rate\":{\"type\":\"integer\",\"description\":\"r\"}}}," +
                "\"204\":{\"description\":\"none\"}}}}",
                extraRoot: "\"produces\":[\"application/xml\"],"));

            Assert.Equal(
                "{\"description\":\"f\",\"content\":{\"image/png\":{\"schema\":{\"type\":\"string\",\"format\":\"binary\"}}," +
                "\"image/jpeg\":{\"schema\":{\"type\":\"string\",\"format\":\"binary\"}}}," +
                "\"headers\":{\"X-Rate\":{\"description\":\"r\",\"schema\":{\"type\":\"integer\"}}}}",
                Json(root, "/paths/~1p/get/responses/200"));
            Assert.Equal("{\"description\":\"none\"}", Json(root, "/paths/~1p/get/responses/204"));
            Assert.False(At(root, "/paths/~1p/get").TryGetProperty("produces", out _));
        }

        [Fact]
        public void AResponseSchemaWithoutProducesIsJsonAndAFileIsOctetStream()
        {
            SpecNode root = Normalized(Swagger(
                "\"/p\":{\"get\":{\"responses\":{\"200\":{\"description\":\"j\",\"schema\":{\"type\":\"string\"}}}}}," +
                "\"/f\":{\"get\":{\"responses\":{\"200\":{\"description\":\"f\",\"schema\":{\"type\":\"file\"}}}}}"));

            Assert.Equal(
                "{\"application/json\":{\"schema\":{\"type\":\"string\"}}}",
                Json(root, "/paths/~1p/get/responses/200/content"));
            Assert.Equal(
                "{\"application/octet-stream\":{\"schema\":{\"type\":\"string\",\"format\":\"binary\"}}}",
                Json(root, "/paths/~1f/get/responses/200/content"));
        }

        [Fact]
        public void SecurityDefinitionsMapToOpenApiSecuritySchemes()
        {
            SpecNode root = Normalized(Swagger(
                string.Empty,
                extraRoot: "\"securityDefinitions\":{" +
                           "\"b\":{\"type\":\"basic\",\"description\":\"d\"}," +
                           "\"a\":{\"type\":\"apiKey\",\"name\":\"X-K\",\"in\":\"header\"}," +
                           "\"i\":{\"type\":\"oauth2\",\"flow\":\"implicit\",\"authorizationUrl\":\"https://a.test/a\",\"scopes\":{\"r\":\"read\"}}," +
                           "\"p\":{\"type\":\"oauth2\",\"flow\":\"password\",\"tokenUrl\":\"https://a.test/t\",\"scopes\":{}}," +
                           "\"c\":{\"type\":\"oauth2\",\"flow\":\"application\",\"tokenUrl\":\"https://a.test/t\"}," +
                           "\"x\":{\"type\":\"oauth2\",\"flow\":\"accessCode\",\"authorizationUrl\":\"https://a.test/a\",\"tokenUrl\":\"https://a.test/t\",\"scopes\":{\"w\":\"write\"}}},"));

            Assert.Equal("{\"type\":\"http\",\"scheme\":\"basic\",\"description\":\"d\"}", Json(root, "/components/securitySchemes/b"));
            Assert.Equal("{\"type\":\"apiKey\",\"name\":\"X-K\",\"in\":\"header\"}", Json(root, "/components/securitySchemes/a"));
            Assert.Equal(
                "{\"type\":\"oauth2\",\"flows\":{\"implicit\":{\"authorizationUrl\":\"https://a.test/a\",\"scopes\":{\"r\":\"read\"}}}}",
                Json(root, "/components/securitySchemes/i"));
            Assert.Equal(
                "{\"type\":\"oauth2\",\"flows\":{\"password\":{\"tokenUrl\":\"https://a.test/t\",\"scopes\":{}}}}",
                Json(root, "/components/securitySchemes/p"));
            Assert.Equal(
                "{\"type\":\"oauth2\",\"flows\":{\"clientCredentials\":{\"tokenUrl\":\"https://a.test/t\",\"scopes\":{}}}}",
                Json(root, "/components/securitySchemes/c"));
            Assert.Equal(
                "{\"type\":\"oauth2\",\"flows\":{\"authorizationCode\":{\"authorizationUrl\":\"https://a.test/a\"," +
                "\"tokenUrl\":\"https://a.test/t\",\"scopes\":{\"w\":\"write\"}}}}",
                Json(root, "/components/securitySchemes/x"));
        }

        [Fact]
        public void SchemaKeywordsAreConvertedOrKept()
        {
            SpecNode root = Normalized(Swagger(
                string.Empty,
                "\"Base\":{\"type\":\"object\",\"discriminator\":\"kind\",\"required\":[\"kind\"],\"readOnly\":true," +
                "\"example\":{\"kind\":\"a\"},\"x-vendor\":{\"k\":[1,2]},\"properties\":{\"kind\":{\"type\":\"string\"}," +
                "\"n\":{\"type\":\"string\",\"x-nullable\":true},\"s\":{\"type\":\"string\",\"x-nullable\":false}," +
                "\"f\":{\"type\":\"file\"},\"r\":{\"$ref\":\"#/definitions/Base\",\"x-nullable\":true}," +
                "\"d\":{\"type\":\"object\",\"properties\":{\"discriminator\":{\"type\":\"string\"},\"x-nullable\":{\"type\":\"boolean\"}}}}}," +
                "\"Sub\":{\"allOf\":[{\"$ref\":\"#/definitions/Base\"},{\"type\":\"object\"}]}"));

            Assert.Equal(
                "{\"type\":\"object\",\"discriminator\":{\"propertyName\":\"kind\"},\"required\":[\"kind\"],\"readOnly\":true," +
                "\"example\":{\"kind\":\"a\"},\"x-vendor\":{\"k\":[1,2]},\"properties\":{\"kind\":{\"type\":\"string\"}," +
                "\"n\":{\"type\":\"string\",\"nullable\":true},\"s\":{\"type\":\"string\"}," +
                "\"f\":{\"type\":\"string\",\"format\":\"binary\"},\"r\":{\"$ref\":\"#/components/schemas/Base\",\"nullable\":true}," +
                "\"d\":{\"type\":\"object\",\"properties\":{\"discriminator\":{\"type\":\"string\"},\"x-nullable\":{\"type\":\"boolean\"}}}}}",
                Json(root, "/components/schemas/Base"));
            Assert.Equal(
                "{\"allOf\":[{\"$ref\":\"#/components/schemas/Base\"},{\"type\":\"object\"}]}",
                Json(root, "/components/schemas/Sub"));
        }

        [Fact]
        public void TopLevelTagsExternalDocsInfoAndExtensionsPassThrough()
        {
            SpecNode root = Normalized(Swagger(
                string.Empty,
                extraRoot: "\"tags\":[{\"name\":\"t\"}],\"externalDocs\":{\"url\":\"https://d.test\"},\"x-ms-paths\":{\"a\":1},"));

            Assert.Equal("[{\"name\":\"t\"}]", Json(root, "/tags"));
            Assert.Equal("{\"url\":\"https://d.test\"}", Json(root, "/externalDocs"));
            Assert.Equal("{\"a\":1}", Json(root, "/x-ms-paths"));
            Assert.Equal("{\"title\":\"P9\",\"version\":\"1\"}", Json(root, "/info"));
        }

        [Fact]
        public void SynthesizedNodesKeepTheOriginalPathAndPosition()
        {
            SpecNode source = TestBundleFactory.ReadRoot(Swagger(
                "\"/p\":{\"post\":{\"parameters\":[{\"name\":\"q\",\"in\":\"query\",\"type\":\"string\"}," +
                "{\"name\":\"f\",\"in\":\"formData\",\"type\":\"string\"}],\"responses\":{\"200\":{\"description\":\"d\"," +
                "\"schema\":{\"type\":\"string\"}}}}}",
                "\"D\":{\"type\":\"object\",\"properties\":{\"a\":{\"type\":\"string\"}}}"));
            SpecNode root = Swagger2Normalizer.Normalize(source);

            SpecNode property = At(root, "/components/schemas/D/properties/a");
            Assert.Equal("/components/schemas/D/properties/a", property.LogicalPath);
            Assert.Equal("/definitions/D/properties/a", property.DiagnosticLogicalPath);
            Assert.Equal(At(source, "/definitions/D/properties/a").Line, property.Line);
            Assert.Equal(At(source, "/definitions/D/properties/a").Column, property.Column);

            SpecNode form = At(root, "/paths/~1p/post/requestBody/content/application~1x-www-form-urlencoded/schema/properties/f");
            Assert.Equal("/paths/~1p/post/parameters/1", form.DiagnosticLogicalPath);
            Assert.Equal("/paths/~1p/post/parameters/0", At(root, "/paths/~1p/post/parameters/0").DiagnosticLogicalPath);
            Assert.Equal(
                "/paths/~1p/post/responses/200/schema",
                At(root, "/paths/~1p/post/responses/200/content/application~1json/schema").DiagnosticLogicalPath);
        }

        // ---- the generated client -------------------------------------------------------------------

        [Fact]
        public void PetstoreGeneratesWithoutDiagnosticsOrCompileErrors()
        {
            Assert.NotNull(PetstoreAssembly.Value);
            var servers = (IReadOnlyList<string>)Phase4WireHarness.ClientType(PetstoreAssembly.Value)
                .GetProperty("Servers")!.GetValue(null)!;
            Assert.Equal(
                new[] { "https://petstore.example.test:8443/v2", "http://petstore.example.test:8443/v2" },
                servers);
        }

        private sealed class Handler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

            internal Handler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            {
                _respond = respond;
            }

            internal Phase4BodyWireHarness.Exchange? Last { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                var exchange = new Phase4BodyWireHarness.Exchange { Method = request.Method.Method, Uri = request.RequestUri };
                foreach (var header in request.Headers)
                {
                    exchange.Headers[header.Key] = string.Join(",", header.Value);
                }

                if (request.Content is not null)
                {
                    foreach (var header in request.Content.Headers)
                    {
                        exchange.ContentHeaders[header.Key] = string.Join(",", header.Value);
                    }

                    exchange.Body = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                }

                Last = exchange;
                return _respond(request);
            }
        }

        private static async Task<(object? Result, Phase4BodyWireHarness.Exchange Request)> Call(
            string method,
            Func<HttpRequestMessage, HttpResponseMessage>? respond,
            Dictionary<string, object?>? properties,
            params object?[] arguments)
        {
            Assembly assembly = PetstoreAssembly.Value;
            var handler = new Handler(respond ?? (_ => new HttpResponseMessage(HttpStatusCode.NoContent)));
            using var httpClient = new HttpClient(handler);
            object client = Phase4WireHarness.CreateClient(assembly, httpClient);
            Type type = Phase4WireHarness.ClientType(assembly);
            if (properties is not null)
            {
                foreach (KeyValuePair<string, object?> property in properties)
                {
                    type.GetProperty(property.Key)!.SetValue(client, property.Value);
                }
            }

            MethodInfo info = type.GetMethod(method)!;
            ParameterInfo[] parameters = info.GetParameters();
            var full = new object?[parameters.Length];
            for (int index = 0; index < parameters.Length; index++)
            {
                full[index] = index < arguments.Length
                    ? arguments[index]
                    : parameters[index].ParameterType == typeof(CancellationToken)
                        ? CancellationToken.None
                        : parameters[index].HasDefaultValue ? parameters[index].DefaultValue : null;
            }

            object? result = null;
            try
            {
                var task = (Task)info.Invoke(client, full)!;
                await task.ConfigureAwait(false);
                PropertyInfo? resultProperty = task.GetType().GetProperty("Result");
                if (resultProperty is not null && resultProperty.PropertyType.Name != "VoidTaskResult")
                {
                    result = resultProperty.GetValue(task);
                }
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            }

            return (result, handler.Last!);
        }

        private static Type PetstoreType(string name)
        {
            return PetstoreAssembly.Value.GetType(Phase4WireHarness.Ns + name, throwOnError: true)!;
        }

        private static object New(string typeName, params (string Property, object? Value)[] values)
        {
            Type type = PetstoreType(typeName);
            object instance = Activator.CreateInstance(type)!;
            foreach ((string property, object? value) in values)
            {
                type.GetProperty(property)!.SetValue(instance, value);
            }

            return instance;
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body, params (string Name, string Value)[] headers)
        {
            return Phase4BodyWireHarness.Respond(status, body, "application/json", headers);
        }

        private static JsonElement Body(Phase4BodyWireHarness.Exchange exchange)
        {
            return JsonDocument.Parse(exchange.BodyText).RootElement;
        }

        private static string? ExtractParameter(string disposition, string name)
        {
            foreach (string segment in disposition.Split(';').Skip(1))
            {
                string[] pair = segment.Split(new[] { '=' }, 2);
                if (string.Equals(pair[0].Trim(), name, StringComparison.OrdinalIgnoreCase))
                {
                    return pair[1].Trim().Trim('"');
                }
            }

            return null;
        }

        private static string Wire(Phase4BodyWireHarness.Exchange exchange)
        {
            return exchange.Uri!.GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped);
        }

        [Fact]
        public async Task ListPetsSendsTheCollectionFormatsAndReadsTheResponseHeaderAndTheUnion()
        {
            (object? result, Phase4BodyWireHarness.Exchange request) = await Call(
                "listPetsWithHttpInfo",
                _ => Json(
                    HttpStatusCode.OK,
                    "[{\"petType\":\"Cat\",\"name\":\"Tom\",\"huntingSkill\":\"pounce\"}," +
                    "{\"petType\":\"Dog\",\"name\":\"Rex\",\"packSize\":3,\"id\":9}]",
                    ("X-Total-Count", "7")),
                null,
                "req-1",
                new List<string> { "a", "b" },
                new List<long> { 1, 2 },
                new List<string> { "x", "y" },
                5,
                new List<string> { "t1", "t2" });

            Assert.Equal("GET", request.Method);
            Assert.Equal(
                "https://petstore.example.test:8443/v2/pets?cols=a%20b&ids=1,2&kinds=x%7Cy&limit=5&tags=t1&tags=t2",
                Wire(request));
            Assert.Equal("req-1", request.Headers["X-Request-Id"]);
            Assert.False(request.Headers.ContainsKey("X-Api-Key"));

            Assert.Equal(7, result!.GetType().GetProperty("XTotalCount")!.GetValue(result));
            var pets = (System.Collections.IList)result.GetType().GetProperty("Data")!.GetValue(result)!;
            Assert.Equal(2, pets.Count);
            object cat = pets[0]!.GetType().GetMethod("AsCat")!.Invoke(pets[0], null)!;
            Assert.Equal("Tom", cat.GetType().GetProperty("Name")!.GetValue(cat));
            Assert.Equal("pounce", cat.GetType().GetProperty("HuntingSkill")!.GetValue(cat));
            object dog = pets[1]!.GetType().GetMethod("AsDog")!.Invoke(pets[1], null)!;
            Assert.Equal(3, dog.GetType().GetProperty("PackSize")!.GetValue(dog));
            Assert.Equal(9L, dog.GetType().GetProperty("Id")!.GetValue(dog));
        }

        [Fact]
        public async Task ErrorResponsesDeclaredWithAResponseReferenceAreTypedErrors()
        {
            Exception exception = await Assert.ThrowsAnyAsync<Exception>(() => Call(
                "getPet",
                _ => Json(HttpStatusCode.NotFound, "{\"code\":404,\"message\":\"nope\"}"),
                null,
                7L));

            Assert.Equal("Phase4ApiException", exception.GetType().Name);
        }

        [Fact]
        public async Task AddPetSendsTheBodyParameterAsJsonAndTheApiKeyFromTheDocumentSecurity()
        {
            object dog = New("Dog", ("Name", "Rex"), ("PackSize", 4));
            object pet = PetstoreType("Pet").GetMethod("FromDog")!.Invoke(null, new[] { dog })!;

            (object? result, Phase4BodyWireHarness.Exchange request) = await Call(
                "addPet",
                _ => Json(HttpStatusCode.Created, "{\"petType\":\"Dog\",\"name\":\"Rex\",\"id\":1,\"packSize\":4}"),
                new Dictionary<string, object?> { ["ApiKeyAuthApiKey"] = "key-1" },
                pet);

            Assert.Equal("POST", request.Method);
            Assert.Equal("https://petstore.example.test:8443/v2/pets", Wire(request));
            Assert.StartsWith("application/json", request.ContentType, StringComparison.Ordinal);
            Assert.Equal("key-1", request.Headers["X-Api-Key"]);
            JsonElement body = Body(request);
            Assert.Equal("Rex", body.GetProperty("name").GetString());
            Assert.Equal("Dog", body.GetProperty("petType").GetString());
            Assert.Equal(4, body.GetProperty("packSize").GetInt32());
            Assert.False(body.TryGetProperty("id", out _));
            Assert.Equal("Rex", PetstoreType("Dog").GetProperty("Name")!.GetValue(
                result!.GetType().GetMethod("AsDog")!.Invoke(result, null)));
        }

        [Fact]
        public async Task UpdatePetSendsAnInlineBodyTheOAuthTokenAndTheNullableProperty()
        {
            object specifiedNull = New("UpdatePetRequest", ("Name", "n"), ("Tag", null));
            object unspecified = New("UpdatePetRequest", ("Name", "n"));
            var token = new Dictionary<string, object?> { ["PetstoreAuthAccessToken"] = "tok" };

            (_, Phase4BodyWireHarness.Exchange withNull) = await Call("updatePet", null, token, 7L, specifiedNull);
            (_, Phase4BodyWireHarness.Exchange without) = await Call("updatePet", null, token, 7L, unspecified);

            Assert.Equal("PUT", withNull.Method);
            Assert.Equal("https://petstore.example.test:8443/v2/pets/7", Wire(withNull));
            Assert.Equal("Bearer tok", withNull.Headers["Authorization"]);
            Assert.False(withNull.Headers.ContainsKey("X-Api-Key"));
            Assert.Equal(JsonValueKind.Null, Body(withNull).GetProperty("tag").ValueKind);
            Assert.False(Body(without).TryGetProperty("tag", out _));
        }

        [Fact]
        public async Task DeletePetUsesTheOperationSecurityBasicCredentials()
        {
            (_, Phase4BodyWireHarness.Exchange request) = await Call(
                "deletePet",
                null,
                new Dictionary<string, object?>
                {
                    ["BasicAuthUsername"] = "user",
                    ["BasicAuthPassword"] = "pw",
                    ["ApiKeyAuthApiKey"] = "ignored"
                },
                7L);

            Assert.Equal("DELETE", request.Method);
            Assert.Equal("https://petstore.example.test:8443/v2/pets/7", Wire(request));
            Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("user:pw")), request.Headers["Authorization"]);
            Assert.False(request.Headers.ContainsKey("X-Api-Key"));
        }

        [Fact]
        public async Task UploadPhotoSendsAMultipartFormWithAFilePart()
        {
            object file = Activator.CreateInstance(
                PetstoreType("Phase4ApiFileParameter"),
                new byte[] { 1, 2, 3 },
                "p.png",
                "image/png")!;
            object form = New("UploadPhotoRequest", ("Caption", "hello"), ("Photo", file));

            (object? result, Phase4BodyWireHarness.Exchange request) = await Call(
                "uploadPhoto",
                _ => Json(HttpStatusCode.OK, "{\"id\":5,\"label\":\"stored\"}"),
                null,
                7L,
                form);

            Assert.Equal("POST", request.Method);
            Assert.Equal("https://petstore.example.test:8443/v2/pets/7/photo", Wire(request));
            Assert.StartsWith("multipart/form-data", request.ContentType, StringComparison.Ordinal);
            List<Phase4BodyWireHarness.Part> parts = Phase4BodyWireHarness.ReadMultipart(request, out _);
            Assert.Equal(2, parts.Count);
            Phase4BodyWireHarness.Part caption = parts.Single(part => ExtractParameter(part.Disposition, "name") == "caption");
            Assert.Equal("hello", caption.Body);
            Phase4BodyWireHarness.Part photo = parts.Single(part => ExtractParameter(part.Disposition, "name") == "photo");
            Assert.Equal("p.png", ExtractParameter(photo.Disposition, "filename"));
            Assert.Equal("image/png", photo.ContentType);
            Assert.Equal("\u0001\u0002\u0003", photo.Body);
            Assert.Equal("stored", result!.GetType().GetProperty("Label")!.GetValue(result));
        }

        [Fact]
        public async Task LoginSendsAUrlEncodedFormWithACsvArray()
        {
            object form = New(
                "LoginRequest",
                ("Username", "u s"),
                ("Password", "p&w"),
                ("Scopes", new List<string> { "read", "write" }));

            (object? result, Phase4BodyWireHarness.Exchange request) = await Call(
                "login",
                _ => Json(HttpStatusCode.OK, "\"token-1\""),
                new Dictionary<string, object?> { ["ApiKeyAuthApiKey"] = "not-sent" },
                form);

            Assert.Equal("POST", request.Method);
            Assert.Equal("https://petstore.example.test:8443/v2/login", Wire(request));
            Assert.StartsWith("application/x-www-form-urlencoded", request.ContentType, StringComparison.Ordinal);
            Assert.Equal("password=p%26w&scopes=read,write&username=u%20s", request.BodyText);
            Assert.False(request.Headers.ContainsKey("X-Api-Key"));
            Assert.Equal("token-1", result);
        }

        [Fact]
        public async Task DownloadPhotoReadsTheFileResponseAsBytes()
        {
            (object? result, Phase4BodyWireHarness.Exchange request) = await Call(
                "downloadPhoto",
                _ => Phase4BodyWireHarness.RespondBytes(HttpStatusCode.OK, new byte[] { 9, 8, 7 }, "image/png"),
                null,
                7L);

            Assert.Equal("https://petstore.example.test:8443/v2/pets/7/photo", Wire(request));
            Assert.Equal(new byte[] { 9, 8, 7 }, (byte[])result!);
        }

        [Fact]
        public async Task TheDeclaredParameterConstraintsAreEnforcedBeforeSending()
        {
            Exception exception = await Assert.ThrowsAnyAsync<Exception>(() => Call(
                "listPets",
                null,
                null,
                null,
                null,
                null,
                null,
                1000,
                null));

            Assert.Contains("limit", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        // ---- diagnostics keep the original Swagger location -------------------------------------------

        private const string Ok = "\"responses\":{\"204\":{\"description\":\"d\"}}";

        [Fact]
        public void AnErrorInsideADefinitionReportsTheDefinitionsPath()
        {
            Diagnostic diagnostic = SingleDiagnostic(
                Swagger(
                    "\"/p\":{\"get\":{" + Ok + "}}",
                    "\"Bad\":{\"type\":\"object\",\"properties\":{\"x\":{\"type\":\"bogus\"}}}"),
                "OACG101");

            AssertLogicalPath(diagnostic, "/definitions/Bad/properties/x");
            Assert.DoesNotContain("/components", diagnostic.GetMessage(), StringComparison.Ordinal);
        }

        [Fact]
        public void AnErrorInAFormDataParameterReportsTheParameterPath()
        {
            Diagnostic diagnostic = SingleDiagnostic(
                Swagger(
                    "\"/p\":{\"post\":{\"parameters\":[{\"name\":\"a\",\"in\":\"query\",\"type\":\"string\"}," +
                    "{\"name\":\"f\",\"in\":\"formData\",\"type\":\"bogus\"}]," + Ok + "}}"),
                "OACG101");

            AssertLogicalPath(diagnostic, "/paths/~1p/post/parameters/1");
        }

        [Fact]
        public void AnErrorInABodyParameterSchemaReportsTheSchemaPathInsideTheParameter()
        {
            Diagnostic diagnostic = SingleDiagnostic(
                Swagger(
                    "\"/p\":{\"post\":{\"parameters\":[{\"name\":\"b\",\"in\":\"body\",\"schema\":" +
                    "{\"type\":\"object\",\"properties\":{\"x\":{\"type\":\"bogus\"}}}}]," + Ok + "}}"),
                "OACG101");

            AssertLogicalPath(diagnostic, "/paths/~1p/post/parameters/0/schema/properties/x");
        }

        [Fact]
        public void AnErrorInAResponseSchemaReportsTheResponseSchemaPath()
        {
            Diagnostic diagnostic = SingleDiagnostic(
                Swagger(
                    "\"/p\":{\"get\":{\"responses\":{\"200\":{\"description\":\"d\",\"schema\":{\"type\":\"bogus\"}}}}}"),
                "OACG101");

            AssertLogicalPath(diagnostic, "/paths/~1p/get/responses/200/schema");
        }

        [Fact]
        public void AnErrorInASharedParameterReportsItsDefinitionPath()
        {
            Diagnostic diagnostic = SingleDiagnostic(
                Swagger(
                    "\"/p\":{\"get\":{\"parameters\":[{\"$ref\":\"#/parameters/P\"}]," + Ok + "}}",
                    extraRoot: "\"parameters\":{\"P\":{\"name\":\"x\",\"in\":\"query\",\"type\":\"bogus\"}},"),
                "OACG101");

            AssertLogicalPath(diagnostic, "/parameters/P");
        }

        [Fact]
        public void AnUnresolvedReferenceReportsTheRefValueLocation()
        {
            Diagnostic diagnostic = SingleDiagnostic(
                Swagger(
                    "\"/p\":{\"get\":{\"responses\":{\"200\":{\"description\":\"d\"," +
                    "\"schema\":{\"$ref\":\"#/definitions/Missing\"}}}}}"),
                "OACG102");

            AssertLogicalPath(diagnostic, "/paths/~1p/get/responses/200/schema/$ref");
        }

        [Fact]
        public void TsvCollectionFormatIsUnsupported()
        {
            Diagnostic diagnostic = SingleDiagnostic(
                Swagger(
                    "\"/p\":{\"get\":{\"parameters\":[{\"name\":\"v\",\"in\":\"query\",\"type\":\"array\"," +
                    "\"items\":{\"type\":\"string\"},\"collectionFormat\":\"tsv\"}]," + Ok + "}}"),
                "OACG101");

            AssertLogicalPath(diagnostic, "/paths/~1p/get/parameters/0/collectionFormat");
            Assert.Contains("tsv", diagnostic.GetMessage(), StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("header", "ssv")]
        [InlineData("header", "pipes")]
        [InlineData("path", "multi")]
        public void CollectionFormatsThatPathAndHeaderCannotExpressAreUnsupported(string location, string format)
        {
            string path = location == "path" ? "/p/{v}" : "/p";
            string required = location == "path" ? "\"required\":true," : string.Empty;
            Diagnostic diagnostic = SingleDiagnostic(
                Swagger(
                    "\"" + path + "\":{\"get\":{\"parameters\":[{\"name\":\"v\",\"in\":\"" + location + "\"," + required +
                    "\"type\":\"array\",\"items\":{\"type\":\"string\"},\"collectionFormat\":\"" + format + "\"}]," + Ok + "}}"),
                "OACG101");

            AssertLogicalPath(diagnostic, "/paths/~1" + (location == "path" ? "p~1{v}" : "p") + "/get/parameters/0/collectionFormat");
        }

        [Fact]
        public void IgnoredHeaderParametersKeepTheirOriginalLocationInTheWarning()
        {
            Phase4GeneratorExecution execution = Generate(Swagger(
                "\"/p\":{\"get\":{\"parameters\":[{\"name\":\"Accept\",\"in\":\"header\",\"type\":\"string\"}]," + Ok + "}}"));

            Diagnostic warning = Assert.Single(execution.RunResult.Diagnostics);
            Assert.Equal("OACG108", warning.Id);
            Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
            AssertLogicalPath(warning, "/paths/~1p/get/parameters/0/name");
            Assert.Empty(execution.CompilationErrors);
        }

        [Theory]
        [InlineData("\"1.2\"")]
        [InlineData("2.0")]
        [InlineData("\"3.0\"")]
        public void OnlySwagger20IsAccepted(string version)
        {
            string spec = Swagger("\"/p\":{\"get\":{" + Ok + "}}").Replace("\"swagger\":\"2.0\"", "\"swagger\":" + version);

            Diagnostic diagnostic = SingleDiagnostic(spec, "OACG101");

            AssertLogicalPath(diagnostic, "/swagger");
        }

        [Fact]
        public void ADocumentDeclaringOpenApiAndSwaggerIsRejected()
        {
            string spec = Swagger("\"/p\":{\"get\":{" + Ok + "}}").Replace("{\"swagger\"", "{\"openapi\":\"3.0.3\",\"swagger\"");

            Diagnostic diagnostic = SingleDiagnostic(spec, "OACG101");

            AssertLogicalPath(diagnostic, "/swagger");
        }

        [Fact]
        public void ABodyParameterCannotBeCombinedWithFormDataOrAnotherBody()
        {
            Diagnostic mixed = SingleDiagnostic(
                Swagger(
                    "\"/p\":{\"post\":{\"parameters\":[{\"name\":\"b\",\"in\":\"body\",\"schema\":{\"type\":\"string\"}}," +
                    "{\"name\":\"f\",\"in\":\"formData\",\"type\":\"string\"}]," + Ok + "}}"),
                "OACG100");
            AssertLogicalPath(mixed, "/paths/~1p/post/parameters/1");

            Diagnostic twice = SingleDiagnostic(
                Swagger(
                    "\"/p\":{\"post\":{\"parameters\":[{\"name\":\"a\",\"in\":\"body\",\"schema\":{\"type\":\"string\"}}," +
                    "{\"name\":\"b\",\"in\":\"body\",\"schema\":{\"type\":\"string\"}}]," + Ok + "}}"),
                "OACG100");
            AssertLogicalPath(twice, "/paths/~1p/post/parameters/1");

            Diagnostic noSchema = SingleDiagnostic(
                Swagger("\"/p\":{\"post\":{\"parameters\":[{\"name\":\"a\",\"in\":\"body\"}]," + Ok + "}}"),
                "OACG100");
            AssertLogicalPath(noSchema, "/paths/~1p/post/parameters/0");
        }

        [Fact]
        public void InvalidParametersAreReportedAtTheirOriginalLocation()
        {
            Diagnostic file = SingleDiagnostic(
                Swagger("\"/p\":{\"get\":{\"parameters\":[{\"name\":\"a\",\"in\":\"query\",\"type\":\"file\"}]," + Ok + "}}"),
                "OACG100");
            AssertLogicalPath(file, "/paths/~1p/get/parameters/0/type");

            Diagnostic location = SingleDiagnostic(
                Swagger("\"/p\":{\"get\":{\"parameters\":[{\"name\":\"a\",\"in\":\"cookie\",\"type\":\"string\"}]," + Ok + "}}"),
                "OACG100");
            AssertLogicalPath(location, "/paths/~1p/get/parameters/0/in");

            Diagnostic noType = SingleDiagnostic(
                Swagger("\"/p\":{\"get\":{\"parameters\":[{\"name\":\"a\",\"in\":\"query\"}]," + Ok + "}}"),
                "OACG100");
            AssertLogicalPath(noType, "/paths/~1p/get/parameters/0");

            Diagnostic noFormType = SingleDiagnostic(
                Swagger("\"/p\":{\"post\":{\"parameters\":[{\"name\":\"a\",\"in\":\"formData\"}]," + Ok + "}}"),
                "OACG100");
            AssertLogicalPath(noFormType, "/paths/~1p/post/parameters/0");
        }

        [Fact]
        public void NonHttpSchemesAreReportedAtTheSchemeLocation()
        {
            Diagnostic diagnostic = SingleDiagnostic(
                Swagger("\"/p\":{\"get\":{" + Ok + "}}", extraRoot: "\"schemes\":[\"https\",\"ws\"],"),
                "OACG101");

            AssertLogicalPath(diagnostic, "/schemes/1");
        }

        [Fact]
        public void OperationSchemesWithoutAHostAreIgnoredWithAWarning()
        {
            Phase4GeneratorExecution execution = Generate(Swagger(
                "\"/p\":{\"get\":{\"schemes\":[\"http\"]," + Ok + "}}",
                hostAndBase: "\"basePath\":\"/v1\","));

            Diagnostic warning = Assert.Single(execution.RunResult.Diagnostics);
            Assert.Equal("OACG108", warning.Id);
            AssertLogicalPath(warning, "/paths/~1p/get/schemes");
            Assert.Empty(execution.CompilationErrors);
        }

        // ---- structural anomalies ---------------------------------------------------------------------

        [Fact]
        public void StructuralAnomaliesAreReportedAtTheirOriginalLocation()
        {
            Diagnostic paths = SingleDiagnostic(
                "{\"swagger\":\"2.0\",\"info\":{\"title\":\"P9\",\"version\":\"1\"},\"paths\":[]}",
                "OACG100");
            AssertLogicalPath(paths, "/paths");

            Diagnostic parameters = SingleDiagnostic(
                Swagger("\"/p\":{\"get\":{\"parameters\":{\"a\":1}," + Ok + "}}"),
                "OACG100");
            AssertLogicalPath(parameters, "/paths/~1p/get/parameters");

            Diagnostic pathParameters = SingleDiagnostic(
                Swagger("\"/p\":{\"parameters\":\"x\",\"get\":{" + Ok + "}}"),
                "OACG100");
            AssertLogicalPath(pathParameters, "/paths/~1p/parameters");
        }

        [Fact]
        public void ParameterReferenceCyclesAndChainsAreRejectedAtTheReference()
        {
            Diagnostic diagnostic = SingleDiagnostic(
                Swagger(
                    "\"/p\":{\"get\":{\"parameters\":[{\"$ref\":\"#/parameters/A\"}]," + Ok + "}}",
                    extraRoot: "\"parameters\":{\"A\":{\"$ref\":\"#/parameters/B\"},\"B\":{\"$ref\":\"#/parameters/A\"}},"),
                "OACG101");

            AssertLogicalPath(diagnostic, "/paths/~1p/get/parameters/0/$ref");
        }

        [Fact]
        public void ResponseReferenceCyclesAreReportedAsCyclicReferences()
        {
            Diagnostic diagnostic = SingleDiagnostic(
                Swagger(
                    "\"/p\":{\"get\":{\"responses\":{\"200\":{\"$ref\":\"#/responses/A\"}}}}",
                    extraRoot: "\"responses\":{\"A\":{\"$ref\":\"#/responses/B\"},\"B\":{\"$ref\":\"#/responses/A\"}},"),
                "OACG103");

            Assert.Contains("logical path '/responses/", diagnostic.GetMessage(), StringComparison.Ordinal);
            Assert.EndsWith("/$ref", Regex.Match(diagnostic.GetMessage(), "logical path '([^']*)'").Groups[1].Value, StringComparison.Ordinal);
        }

        [Fact]
        public void ANonStringReferenceIsInvalidAtTheRefNode()
        {
            Diagnostic parameter = SingleDiagnostic(
                Swagger("\"/p\":{\"get\":{\"parameters\":[{\"$ref\":5}]," + Ok + "}}"),
                "OACG100");
            AssertLogicalPath(parameter, "/paths/~1p/get/parameters/0/$ref");
            Assert.Contains("$ref value must be a string", parameter.GetMessage(), StringComparison.Ordinal);

            Diagnostic response = SingleDiagnostic(
                Swagger("\"/p\":{\"get\":{\"responses\":{\"200\":{\"$ref\":true}}}}"),
                "OACG100");
            AssertLogicalPath(response, "/paths/~1p/get/responses/200/$ref");

            Diagnostic schema = SingleDiagnostic(
                Swagger(
                    "\"/p\":{\"get\":{\"responses\":{\"200\":{\"description\":\"d\",\"schema\":{\"$ref\":[]}}}}}"),
                "OACG100");
            AssertLogicalPath(schema, "/paths/~1p/get/responses/200/schema/$ref");
        }

        // ---- XML media types next to JSON ---------------------------------------------------------------

        private const string XmlPet =
            "\"Pet\":{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"name\"]," +
            "\"properties\":{\"name\":{\"type\":\"string\"}}}";

        private static string PetstoreWithXml(string consumes, string produces, string operationMedia)
        {
            return Swagger(
                "\"/pet\":{\"post\":{\"operationId\":\"addPet\"," + operationMedia +
                "\"parameters\":[{\"name\":\"body\",\"in\":\"body\",\"required\":true,\"schema\":{\"$ref\":\"#/definitions/Pet\"}}]," +
                "\"responses\":{\"200\":{\"description\":\"ok\",\"schema\":{\"$ref\":\"#/definitions/Pet\"}}}}}",
                XmlPet,
                "\"consumes\":" + consumes + ",\"produces\":" + produces + ",");
        }

        private static void AssertXmlWarnings(Phase4GeneratorExecution execution, params string[] logicalPaths)
        {
            Assert.Equal(logicalPaths.Length, execution.RunResult.Diagnostics.Length);
            Assert.All(execution.RunResult.Diagnostics, static d =>
            {
                Assert.Equal("OACG108", d.Id);
                Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
            });
            foreach (string path in logicalPaths)
            {
                Assert.Contains(
                    execution.RunResult.Diagnostics,
                    d => d.GetMessage().Contains("logical path '" + path + "'", StringComparison.Ordinal) &&
                         d.GetMessage().Contains("xml", StringComparison.OrdinalIgnoreCase));
            }

            Assert.Empty(execution.CompilationErrors);
        }

        private static async Task AssertJsonWire(Phase4GeneratorExecution execution)
        {
            Assembly assembly = execution.EmitAssembly();
            Type pet = assembly.GetType(Phase4WireHarness.Ns + "Pet", throwOnError: true)!;
            object dto = Activator.CreateInstance(pet)!;
            pet.GetProperty("Name")!.SetValue(dto, "Rex");

            (object? result, Phase4BodyWireHarness.Exchange request) = await Phase4BodyWireHarness.Call(
                assembly,
                "addPet",
                _ => Json(HttpStatusCode.OK, "{\"name\":\"Tom\"}"),
                dto);

            Assert.Equal("POST", request.Method);
            Assert.StartsWith("application/json", request.ContentType, StringComparison.Ordinal);
            Assert.Equal("Rex", JsonDocument.Parse(request.BodyText).RootElement.GetProperty("name").GetString());
            // The generated client sends no Accept header (also without XML in the document), so XML is never requested.
            Assert.False(request.Headers.ContainsKey("Accept"));
            Assert.Equal("Tom", pet.GetProperty("Name")!.GetValue(result));
        }

        [Fact]
        public async Task AnXmlMediaTypeNextToJsonIsIgnoredWithAWarningInAnOpenApi30Document()
        {
            string spec =
                "{\"openapi\":\"3.0.3\",\"info\":{\"title\":\"X\",\"version\":\"1\"},\"servers\":[{\"url\":\"https://api.example.test\"}]," +
                "\"paths\":{\"/pet\":{\"post\":{\"operationId\":\"addPet\",\"requestBody\":{\"required\":true,\"content\":{" +
                "\"application/json\":{\"schema\":{\"$ref\":\"#/components/schemas/Pet\"}}," +
                "\"application/xml\":{\"schema\":{\"$ref\":\"#/components/schemas/Pet\"}}}}," +
                "\"responses\":{\"200\":{\"description\":\"ok\",\"content\":{" +
                "\"application/json\":{\"schema\":{\"$ref\":\"#/components/schemas/Pet\"}}," +
                "\"application/xml\":{\"schema\":{\"$ref\":\"#/components/schemas/Pet\"}}}}}}}}," +
                "\"components\":{\"schemas\":{" + XmlPet + "}}}";

            Phase4GeneratorExecution execution = Generate(spec);

            AssertXmlWarnings(
                execution,
                "/paths/~1pet/post/requestBody/content/application~1xml",
                "/paths/~1pet/post/responses/200/content/application~1xml");
            await AssertJsonWire(execution);
        }

        [Fact]
        public async Task AnXmlMediaTypeNextToJsonIsIgnoredInASwagger20PetstoreAtDocumentAndOperationLevel()
        {
            Phase4GeneratorExecution document = Generate(PetstoreWithXml(
                "[\"application/json\",\"application/xml\"]",
                "[\"application/json\",\"application/xml\"]",
                string.Empty));
            AssertXmlWarnings(
                document,
                "/paths/~1pet/post/parameters/0",
                "/paths/~1pet/post/responses/200/schema");
            await AssertJsonWire(document);

            Phase4GeneratorExecution operation = Generate(PetstoreWithXml(
                "[\"application/json\"]",
                "[\"application/json\"]",
                "\"consumes\":[\"application/xml\",\"application/json\"],\"produces\":[\"application/json\",\"application/xml\"],"));
            AssertXmlWarnings(
                operation,
                "/paths/~1pet/post/parameters/0",
                "/paths/~1pet/post/responses/200/schema");
            await AssertJsonWire(operation);
        }

        [Fact]
        public void XmlAsTheOnlyMediaTypeStaysUnsupported()
        {
            Diagnostic swagger = SingleDiagnostic(
                PetstoreWithXml("[\"application/xml\"]", "[\"application/json\"]", string.Empty),
                "OACG101");
            AssertLogicalPath(swagger, "/paths/~1pet/post/parameters/0/schema");

            string openApi =
                "{\"openapi\":\"3.0.3\",\"info\":{\"title\":\"X\",\"version\":\"1\"},\"servers\":[{\"url\":\"https://api.example.test\"}]," +
                "\"paths\":{\"/pet\":{\"post\":{\"operationId\":\"addPet\",\"requestBody\":{\"content\":{" +
                "\"application/xml\":{\"schema\":{\"$ref\":\"#/components/schemas/Pet\"}}}}," +
                "\"responses\":{\"204\":{\"description\":\"ok\"}}}}}," +
                "\"components\":{\"schemas\":{" + XmlPet + "}}}";
            Diagnostic diagnostic = SingleDiagnostic(openApi, "OACG101");
            AssertLogicalPath(diagnostic, "/paths/~1pet/post/requestBody/content/application~1xml/schema");
        }

        [Fact]
        public void ContentWithOnlyXmlVariantsStaysUnsupported()
        {
            // application/xml and text/xml are both XML representations: none of them is a media type that can be
            // generated, so no entry is skipped and the first one (in ordinal order) is rejected.
            string spec =
                "{\"openapi\":\"3.0.3\",\"info\":{\"title\":\"X\",\"version\":\"1\"},\"servers\":[{\"url\":\"https://api.example.test\"}]," +
                "\"paths\":{\"/pet\":{\"post\":{\"operationId\":\"addPet\",\"requestBody\":{\"content\":{" +
                "\"application/xml\":{\"schema\":{\"$ref\":\"#/components/schemas/Pet\"}}," +
                "\"text/xml\":{\"schema\":{\"$ref\":\"#/components/schemas/Pet\"}}}}," +
                "\"responses\":{\"204\":{\"description\":\"ok\"}}}}}," +
                "\"components\":{\"schemas\":{" + XmlPet + "}}}";

            Diagnostic diagnostic = SingleDiagnostic(spec, "OACG101");

            AssertLogicalPath(diagnostic, "/paths/~1pet/post/requestBody/content/application~1xml/schema");
        }

        [Fact]
        public void SecurityDefinitionsThatCannotBeConvertedAreReportedAtTheirLocation()
        {
            Diagnostic flow = SingleDiagnostic(
                Swagger(
                    "\"/p\":{\"get\":{" + Ok + "}}",
                    extraRoot: "\"securityDefinitions\":{\"o\":{\"type\":\"oauth2\",\"flow\":\"device\",\"scopes\":{}}},"),
                "OACG100");
            AssertLogicalPath(flow, "/securityDefinitions/o/flow");

            Diagnostic type = SingleDiagnostic(
                Swagger(
                    "\"/p\":{\"get\":{" + Ok + "}}",
                    extraRoot: "\"securityDefinitions\":{\"o\":{\"type\":\"mutualTLS\"}},"),
                "OACG101");
            AssertLogicalPath(type, "/securityDefinitions/o/type");

            Diagnostic apiKey = SingleDiagnostic(
                Swagger(
                    "\"/p\":{\"get\":{" + Ok + "}}",
                    extraRoot: "\"securityDefinitions\":{\"k\":{\"type\":\"apiKey\",\"name\":\"X\",\"in\":\"body\"}},"),
                "OACG100");
            AssertLogicalPath(apiKey, "/securityDefinitions/k/in");
        }

        [Fact]
        public void PathItemReferencesAndReferencesOutsideTheDefinitionSectionsAreUnsupported()
        {
            Diagnostic pathItem = SingleDiagnostic(
                Swagger("\"/p\":{\"$ref\":\"other.json\"}"),
                "OACG101");
            AssertLogicalPath(pathItem, "/paths/~1p/$ref");

            Diagnostic target = SingleDiagnostic(
                Swagger(
                    "\"/p\":{\"get\":{\"responses\":{\"200\":{\"description\":\"d\"," +
                    "\"schema\":{\"$ref\":\"#/x-foo/bar\"}}}}}"),
                "OACG101");
            AssertLogicalPath(target, "/paths/~1p/get/responses/200/schema/$ref");
        }

        [Fact]
        public void ANonBooleanXNullableAndAMalformedConsumesAreInvalid()
        {
            Diagnostic nullable = SingleDiagnostic(
                Swagger(
                    "\"/p\":{\"get\":{" + Ok + "}}",
                    "\"A\":{\"type\":\"string\",\"x-nullable\":\"yes\"}"),
                "OACG100");
            AssertLogicalPath(nullable, "/definitions/A/x-nullable");

            Diagnostic consumes = SingleDiagnostic(
                Swagger("\"/p\":{\"get\":{" + Ok + "}}", extraRoot: "\"consumes\":\"application/json\","),
                "OACG100");
            AssertLogicalPath(consumes, "/consumes");
        }

        [Fact]
        public async Task DiagnosticsCarryTheLineAndColumnOfTheOriginalSwaggerNode()
        {
            string projectRoot = CreateProjectRoot();
            try
            {
                string text =
                    "{\n" +
                    "  \"swagger\": \"2.0\",\n" +
                    "  \"info\": { \"title\": \"L\", \"version\": \"1\" },\n" +
                    "  \"host\": \"api.example.test\",\n" +
                    "  \"paths\": {\n" +
                    "    \"/p\": {\n" +
                    "      \"post\": {\n" +
                    "        \"parameters\": [\n" +
                    "          { \"name\": \"a\", \"in\": \"query\", \"type\": \"string\" },\n" +
                    "          { \"name\": \"f\", \"in\": \"formData\", \"type\": \"bogus\" }\n" +
                    "        ],\n" +
                    "        \"responses\": { \"204\": { \"description\": \"d\" } }\n" +
                    "      }\n" +
                    "    }\n" +
                    "  },\n" +
                    "  \"definitions\": {\n" +
                    "    \"Bad\": { \"type\": \"object\", \"properties\": { \"x\": { \"type\": \"bogus\" } } }\n" +
                    "  }\n" +
                    "}\n";
                WriteText(projectRoot, "Assets/Specs/root.json", text);
                (_, string bundle) = await Bundle(projectRoot, "Assets/Specs/root.json");

                Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(text, bundle: bundle);

                // Components (definitions) are parsed first, so the definitions error is reported.
                Diagnostic diagnostic = Assert.Single(execution.RunResult.Diagnostics);
                AssertLogicalPath(diagnostic, "/definitions/Bad/properties/x");
                Assert.Equal(17, diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1);
                Assert.Equal(
                    text.Split('\n')[16].IndexOf("{ \"type\": \"bogus\" }", StringComparison.Ordinal) + 1,
                    diagnostic.Location.GetLineSpan().StartLinePosition.Character + 1);

                string withoutDefinitions = text.Replace(
                    ",\n  \"definitions\": {\n    \"Bad\": { \"type\": \"object\", \"properties\": { \"x\": { \"type\": \"bogus\" } } }\n  }",
                    string.Empty);
                WriteText(projectRoot, "Assets/Specs/root2.json", withoutDefinitions);
                (_, string bundle2) = await Bundle(projectRoot, "Assets/Specs/root2.json");
                Diagnostic formDiagnostic = Assert.Single(Phase4GeneratorTestHarness.GenerateAndCompile(
                    withoutDefinitions,
                    bundle: bundle2).RunResult.Diagnostics);
                AssertLogicalPath(formDiagnostic, "/paths/~1p/post/parameters/1");
                Assert.Equal(10, formDiagnostic.Location.GetLineSpan().StartLinePosition.Line + 1);
                Assert.Equal(
                    text.Split('\n')[9].IndexOf("{ \"name\": \"f\"", StringComparison.Ordinal) + 1,
                    formDiagnostic.Location.GetLineSpan().StartLinePosition.Character + 1);
            }
            finally
            {
                Directory.Delete(projectRoot, true);
            }
        }

        // ---- equivalence with a hand-written OpenAPI 3.0 document ------------------------------------

        [Fact]
        public void ASwagger20DocumentAndItsOpenApi30EquivalentProduceTheSameClient()
        {
            const string Namespace = "Generated.Equivalence";
            Phase4GeneratorExecution swagger = Phase4GeneratorTestHarness.GenerateAndCompile(
                ReadSpec("swagger2.json"), apiName: "EquivalenceApi", generatedNamespace: Namespace);
            Phase4GeneratorExecution openApi = Phase4GeneratorTestHarness.GenerateAndCompile(
                ReadSpec("swagger2-as-openapi30.json"), apiName: "EquivalenceApi", generatedNamespace: Namespace);
            AssertClean(swagger);
            AssertClean(openApi);

            IReadOnlyList<string> swaggerSurface = PublicApiSurfaceDescriber.Describe(swagger.OutputCompilation, Namespace);
            IReadOnlyList<string> openApiSurface = PublicApiSurfaceDescriber.Describe(openApi.OutputCompilation, Namespace);

            Assert.NotEmpty(swaggerSurface);
            Assert.Equal(openApiSurface, swaggerSurface);
            // The generated sources are identical as well: nothing in the pipeline can tell the two apart.
            Assert.Equal(openApi.GeneratedSource, swagger.GeneratedSource);
        }

        // ---- the legacy Swagger generator is untouched ------------------------------------------------

        [Fact]
        public void TheLegacyApiDocumentEntryPointStillUsesTheSeparateSwaggerGenerator()
        {
            var option = new ApiClientGenerateOption
            {
                ApiName = "LegacySwaggerApi",
                Namespace = "Rhycol.OpenApiCodeGen.Generated",
                HttpLibraryType = HttpLibrary.HttpClient,
                JsonLibraryType = JsonLibrary.SystemTextJson,
                UseNullableReferenceTypes = true
            };

            var files = new GenerationService().GenerateFromApiDocument(
                TestBundleFactory.ReadRoot(TestAssetLoader.LoadAsset("swagger2-sample.json")),
                option);

            Assert.Contains("public partial class LegacySwaggerApi", Assert.Single(files).Content, StringComparison.Ordinal);
        }

        // ---- Editor bundling and cross-document references ----------------------------------------------

        private static string CreateProjectRoot()
        {
            string path = Path.Combine(Path.GetTempPath(), "OpenApiCodeGenPhase9", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(path, "Assets", "Specs"));
            return path;
        }

        private static void WriteText(string projectRoot, string relativePath, string content)
        {
            string path = Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        private static async Task<(NormalizedSpecGraph Graph, string BundleText)> Bundle(string projectRoot, string rootPath)
        {
            var loader = new ExternalSpecGraphLoader(projectRoot, new RawJsonNormalizer(), new RawYamlNormalizer());
            NormalizedSpecGraph graph = await loader.LoadAsync(rootPath, SpecId, CancellationToken.None);
            byte[] bytes = CanonicalSpecBundleWriter.Write(graph.SpecId, graph.RawSha256, graph.Documents, graph.Edges);
            return (graph, Encoding.UTF8.GetString(bytes));
        }

        private static async Task<Phase4GeneratorExecution> GenerateBundled(string projectRoot, string rootPath)
        {
            (_, string bundle) = await Bundle(projectRoot, rootPath);
            return Phase4GeneratorTestHarness.GenerateAndCompile(
                File.ReadAllText(Path.Combine(projectRoot, rootPath.Replace('/', Path.DirectorySeparatorChar))),
                bundle: bundle);
        }

        private const string SplitRoot =
            "{\"swagger\":\"2.0\",\"info\":{\"title\":\"Split\",\"version\":\"1\"},\"host\":\"api.example.test\",\"basePath\":\"/v1\"," +
            "\"paths\":{\"/pets\":{" +
            "\"get\":{\"operationId\":\"listPets\",\"parameters\":[{\"$ref\":\"params.json#/parameters/Limit\"}]," +
            "\"responses\":{\"200\":{\"description\":\"ok\",\"schema\":{\"type\":\"array\",\"items\":{\"$ref\":\"defs.json#/definitions/Pet\"}}}," +
            "\"404\":{\"$ref\":\"defs.json#/responses/NotFound\"}}}," +
            "\"post\":{\"operationId\":\"addPet\",\"parameters\":[{\"name\":\"body\",\"in\":\"body\",\"required\":true," +
            "\"schema\":{\"$ref\":\"defs.json#/definitions/Pet\"}}]," +
            "\"responses\":{\"201\":{\"description\":\"c\",\"schema\":{\"$ref\":\"#/definitions/Local\"}}}}}}," +
            "\"definitions\":{\"Local\":{\"type\":\"object\",\"additionalProperties\":false," +
            "\"properties\":{\"pet\":{\"$ref\":\"defs.json#/definitions/Pet\"}}}}}";

        private const string SplitDefs =
            "{\"swagger\":\"2.0\",\"info\":{\"title\":\"Defs\",\"version\":\"1\"},\"paths\":{}," +
            "\"definitions\":{\"Pet\":{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"name\"]," +
            "\"properties\":{\"name\":{\"type\":\"string\"},\"tag\":{\"$ref\":\"#/definitions/Tag\",\"x-nullable\":true}}}," +
            "\"Tag\":{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"label\":{\"type\":\"string\"}}}," +
            "\"Error\":{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"message\"],\"properties\":{\"message\":{\"type\":\"string\"}}}}," +
            "\"responses\":{\"NotFound\":{\"description\":\"nf\",\"schema\":{\"$ref\":\"#/definitions/Error\"}}}}";

        private const string SplitParams =
            "{\"swagger\":\"2.0\",\"info\":{\"title\":\"Params\",\"version\":\"1\"},\"paths\":{}," +
            "\"parameters\":{\"Limit\":{\"name\":\"limit\",\"in\":\"query\",\"type\":\"integer\",\"format\":\"int32\",\"maximum\":50}}}";

        private static void WriteSplitDocuments(string projectRoot)
        {
            WriteText(projectRoot, "Assets/Specs/root.json", SplitRoot);
            WriteText(projectRoot, "Assets/Specs/defs.json", SplitDefs);
            WriteText(projectRoot, "Assets/Specs/params.json", SplitParams);
        }

        [Fact]
        public async Task EditorBundlingCollectsTheReferencesOfSwaggerShapes()
        {
            string projectRoot = CreateProjectRoot();
            try
            {
                WriteSplitDocuments(projectRoot);

                (NormalizedSpecGraph graph, string text) = await Bundle(projectRoot, "Assets/Specs/root.json");
                NormalizedSpecBundle bundle = NormalizedSpecBundleReader.Read(text);

                Assert.Equal(3, graph.Documents.Count);
                (string Document, string Pointer)[] expected =
                {
                    ("root", "/paths/~1pets/get/parameters/0/$ref"),
                    ("root", "/paths/~1pets/get/responses/200/schema/items/$ref"),
                    ("root", "/paths/~1pets/get/responses/404/$ref"),
                    ("root", "/paths/~1pets/post/parameters/0/schema/$ref"),
                    ("root", "/paths/~1pets/post/responses/201/schema/$ref"),
                    ("root", "/definitions/Local/properties/pet/$ref"),
                    ("Assets/Specs/defs.json", "/definitions/Pet/properties/tag/$ref"),
                    ("Assets/Specs/defs.json", "/responses/NotFound/schema/$ref")
                };
                foreach ((string document, string pointer) in expected)
                {
                    Assert.True(
                        bundle.ReferenceEdges.Any(edge => edge.SourceDocumentId == document && edge.SourcePointer == pointer),
                        "Missing " + document + pointer);
                }

                Assert.Equal(expected.Length, bundle.ReferenceEdges.Count);
            }
            finally
            {
                Directory.Delete(projectRoot, true);
            }
        }

        [Fact]
        public async Task ABundledSwagger20SpecAcrossDocumentsGeneratesAClient()
        {
            string projectRoot = CreateProjectRoot();
            try
            {
                WriteSplitDocuments(projectRoot);

                Phase4GeneratorExecution execution = await GenerateBundled(projectRoot, "Assets/Specs/root.json");

                AssertClean(execution);
                Assert.Contains("class Pet", execution.GeneratedSource, StringComparison.Ordinal);
                Assert.Contains("class Tag", execution.GeneratedSource, StringComparison.Ordinal);
                Assert.Contains("class Error", execution.GeneratedSource, StringComparison.Ordinal);
                Assert.Contains("class Local", execution.GeneratedSource, StringComparison.Ordinal);

                Assembly assembly = execution.EmitAssembly();
                var handler = new Handler(request => request.Method == HttpMethod.Get
                    ? Json(HttpStatusCode.OK, "[{\"name\":\"Rex\",\"tag\":null}]")
                    : Json(HttpStatusCode.NotFound, "{\"message\":\"nf\"}"));
                using var httpClient = new HttpClient(handler);
                object client = Phase4WireHarness.CreateClient(assembly, httpClient);
                MethodInfo list = Phase4WireHarness.ClientType(assembly).GetMethod("listPets")!;

                var task = (Task)list.Invoke(client, new object?[] { 3, CancellationToken.None })!;
                await task;
                var pets = (System.Collections.IList)task.GetType().GetProperty("Result")!.GetValue(task)!;

                Assert.Equal("https://api.example.test/v1/pets?limit=3", Wire(handler.Last!));
                Assert.Equal("Rex", pets[0]!.GetType().GetProperty("Name")!.GetValue(pets[0]));

                // The cross-document maximum (from params.json) is enforced before sending.
                var tooBig = (Task)list.Invoke(client, new object?[] { 51, CancellationToken.None })!;
                await Assert.ThrowsAnyAsync<Exception>(() => tooBig);
            }
            finally
            {
                Directory.Delete(projectRoot, true);
            }
        }

        [Fact]
        public async Task AnErrorInAnExternalSwaggerDocumentReportsItsSourcePathAndOriginalPointer()
        {
            string projectRoot = CreateProjectRoot();
            try
            {
                WriteSplitDocuments(projectRoot);
                WriteText(
                    projectRoot,
                    "Assets/Specs/defs.json",
                    SplitDefs.Replace(
                        "\"Tag\":{\"type\":\"object\"",
                        "\"Tag\":{\"type\":\"object\",\"x-nullable\":\"maybe\""));

                Phase4GeneratorExecution execution = await GenerateBundled(projectRoot, "Assets/Specs/root.json");

                Diagnostic diagnostic = Assert.Single(execution.RunResult.Diagnostics);
                Assert.Equal("OACG100", diagnostic.Id);
                Assert.Contains("Assets/Specs/defs.json", diagnostic.GetMessage(), StringComparison.Ordinal);
                AssertLogicalPath(diagnostic, "/definitions/Tag/x-nullable");
            }
            finally
            {
                Directory.Delete(projectRoot, true);
            }
        }

        [Fact]
        public async Task ASwagger20DocumentCannotReferenceAnOpenApi3DocumentOrABareSchemaFile()
        {
            string projectRoot = CreateProjectRoot();
            try
            {
                string root =
                    "{\"swagger\":\"2.0\",\"info\":{\"title\":\"M\",\"version\":\"1\"},\"host\":\"api.example.test\"," +
                    "\"paths\":{\"/p\":{\"get\":{\"responses\":{\"200\":{\"description\":\"d\",\"schema\":{\"$ref\":\"TARGET\"}}}}}}}";
                WriteText(
                    projectRoot,
                    "Assets/Specs/v3.json",
                    "{\"openapi\":\"3.0.3\",\"info\":{\"title\":\"V\",\"version\":\"1\"},\"paths\":{}," +
                    "\"components\":{\"schemas\":{\"X\":{\"type\":\"string\"}}}}");
                WriteText(projectRoot, "Assets/Specs/bare.json", "{\"type\":\"string\"}");

                foreach (string target in new[] { "v3.json#/components/schemas/X", "bare.json" })
                {
                    WriteText(projectRoot, "Assets/Specs/root.json", root.Replace("TARGET", target));

                    Phase4GeneratorExecution execution = await GenerateBundled(projectRoot, "Assets/Specs/root.json");

                    Diagnostic diagnostic = Assert.Single(execution.RunResult.Diagnostics);
                    Assert.Equal("OACG101", diagnostic.Id);
                    Assert.Contains("Assets/Specs/root.json", diagnostic.GetMessage(), StringComparison.Ordinal);
                    AssertLogicalPath(diagnostic, "/paths/~1p/get/responses/200/schema/$ref");
                }
            }
            finally
            {
                Directory.Delete(projectRoot, true);
            }
        }

        [Fact]
        public async Task AnOpenApi3DocumentCannotReferenceASwagger20Document()
        {
            string projectRoot = CreateProjectRoot();
            try
            {
                WriteText(
                    projectRoot,
                    "Assets/Specs/root.json",
                    "{\"openapi\":\"3.0.3\",\"info\":{\"title\":\"R\",\"version\":\"1\"},\"servers\":[{\"url\":\"https://api.example.test\"}]," +
                    "\"paths\":{\"/p\":{\"get\":{\"responses\":{\"200\":{\"description\":\"d\",\"content\":{\"application/json\":" +
                    "{\"schema\":{\"$ref\":\"sw.json#/definitions/X\"}}}}}}}}}");
                WriteText(
                    projectRoot,
                    "Assets/Specs/sw.json",
                    "{\"swagger\":\"2.0\",\"info\":{\"title\":\"S\",\"version\":\"1\"},\"paths\":{},\"definitions\":{\"X\":{\"type\":\"string\"}}}");

                Phase4GeneratorExecution execution = await GenerateBundled(projectRoot, "Assets/Specs/root.json");

                Diagnostic diagnostic = Assert.Single(execution.RunResult.Diagnostics);
                Assert.Equal("OACG101", diagnostic.Id);
                Assert.Contains("Assets/Specs/root.json", diagnostic.GetMessage(), StringComparison.Ordinal);
                AssertLogicalPath(diagnostic, "/paths/~1p/get/responses/200/content/application~1json/schema/$ref");
            }
            finally
            {
                Directory.Delete(projectRoot, true);
            }
        }

        [Fact]
        public async Task ABodyParameterCannotBeReferencedFromAnotherSwaggerDocument()
        {
            string projectRoot = CreateProjectRoot();
            try
            {
                WriteText(
                    projectRoot,
                    "Assets/Specs/root.json",
                    "{\"swagger\":\"2.0\",\"info\":{\"title\":\"R\",\"version\":\"1\"},\"host\":\"api.example.test\"," +
                    "\"paths\":{\"/p\":{\"post\":{\"parameters\":[{\"$ref\":\"other.json#/parameters/Body\"}]," +
                    "\"responses\":{\"204\":{\"description\":\"d\"}}}}}}");
                WriteText(
                    projectRoot,
                    "Assets/Specs/other.json",
                    "{\"swagger\":\"2.0\",\"info\":{\"title\":\"O\",\"version\":\"1\"},\"paths\":{}," +
                    "\"parameters\":{\"Body\":{\"name\":\"b\",\"in\":\"body\",\"schema\":{\"type\":\"string\"}}}}");

                Phase4GeneratorExecution execution = await GenerateBundled(projectRoot, "Assets/Specs/root.json");

                Diagnostic diagnostic = Assert.Single(execution.RunResult.Diagnostics);
                Assert.Equal("OACG101", diagnostic.Id);
                AssertLogicalPath(diagnostic, "/paths/~1p/post/parameters/0/$ref");
            }
            finally
            {
                Directory.Delete(projectRoot, true);
            }
        }
    }
}
