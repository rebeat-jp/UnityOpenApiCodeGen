using System;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using Newtonsoft.Json;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    public sealed class Phase4SemanticReviewRegressionTests
    {
        [Fact]
        public async Task ParameterizedApplicationJsonIsNormalizedAndUsesDeclaredEncoding()
        {
            const string Document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Media"", ""version"": ""1"" },
  ""servers"": [ { ""url"": ""https://example.test"" } ],
  ""paths"": { ""/values"": { ""post"": {
    ""operationId"": ""sendValue"",
    ""requestBody"": { ""required"": true, ""content"": {
      "" Application/JSON ; Charset=\""UTF8\"" ; profile=compact"": {
        ""schema"": { ""type"": ""string"" }
      }
    } },
    ""responses"": { ""204"": { ""description"": ""No Content"" } }
  } } }
}";
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(Document);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();
            var handler = new RecordingHandler(HttpStatusCode.NoContent, string.Empty);
            using var httpClient = new HttpClient(handler);
            object client = CreateClient(assembly, httpClient);

            await Invoke(client, "sendValue", "雪", CancellationToken.None);

            Assert.Equal("application/json", handler.ContentType);
            Assert.Equal("utf-8", handler.Charset);
            Assert.Equal(JsonConvert.SerializeObject("雪"), handler.RequestBody);
        }

        [Theory]
        [InlineData("application/json; charset")]
        [InlineData("application/json; charset=\"unterminated")]
        [InlineData("application json")]
        public void InvalidMediaTypeSyntaxReportsSourceDiagnostic(string mediaType)
        {
            string document = CreateRequestDocument(mediaType);
            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(document).RunResult.Diagnostics);

            Assert.Equal("OACG100", diagnostic.Id);
            Assert.Contains("Invalid media type", diagnostic.GetMessage());
            Assert.Contains("logical path '/paths/~1values/post/requestBody/content/", diagnostic.GetMessage());
        }

        [Fact]
        public void UnsupportedRequestCharsetIsRejectedBeforeRuntime()
        {
            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(
                    CreateRequestDocument("application/json; charset=shift_jis"))
                    .RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("charset 'shift_jis' is not supported", diagnostic.GetMessage());
        }

        [Fact]
        public void ResponseExtensionsAreIgnoredButXPrefixedComponentResponseNamesRemainReferenceable()
        {
            const string Document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Responses"", ""version"": ""1"" },
  ""paths"": { ""/values"": { ""get"": {
    ""operationId"": ""getValue"",
    ""responses"": {
      ""x-documentation"": { ""any"": [""literal extension data""] },
      ""200"": { ""$ref"": ""#/components/responses/x-success"" }
    }
  } } },
  ""components"": { ""responses"": {
    ""x-success"": {
      ""description"": ""OK"",
      ""content"": { ""application/json"": { ""schema"": { ""type"": ""string"" } } }
    }
  } }
}";
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(Document);

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assert.Contains("Task<string> getValue", execution.GeneratedSource);
        }

        [Fact]
        public async Task InlineAndReferencedPlainTextErrorResponsesAreValidatedWithoutBecomingSuccessBodies()
        {
            const string Document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Errors"", ""version"": ""1"" },
  ""servers"": [ { ""url"": ""https://example.test"" } ],
  ""paths"": { ""/values"": { ""get"": {
    ""operationId"": ""getValue"",
    ""responses"": {
      ""200"": { ""description"": ""OK"", ""content"": {
        ""application/json"": { ""schema"": { ""type"": ""string"" } }
      } },
      ""400"": { ""description"": ""Bad"", ""content"": {
        ""text/plain; charset=iso-8859-1"": { ""schema"": { ""type"": ""string"" } }
      } },
      ""default"": { ""$ref"": ""#/components/responses/PlainError"" }
    }
  } } },
  ""components"": { ""responses"": {
    ""PlainError"": { ""description"": ""Error"", ""content"": {
      ""text/plain"": { ""schema"": { ""type"": ""string"" } }
    } }
  } }
}";
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(Document);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assembly assembly = execution.EmitAssembly();
            var handler = new RecordingHandler(HttpStatusCode.BadRequest, "plain failure");
            using var httpClient = new HttpClient(handler);
            object client = CreateClient(assembly, httpClient);

            Exception exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
                await Invoke(client, "getValue", CancellationToken.None));

            Assert.Equal("Phase4ApiException", exception.GetType().Name);
            Assert.Equal("plain failure", exception.GetType().GetProperty("ResponseBody")!.GetValue(exception));
        }

        [Fact]
        public void NonSuccessResponseStillRequiresAStringDescription()
        {
            const string Document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Errors"", ""version"": ""1"" },
  ""paths"": { ""/values"": { ""get"": {
    ""operationId"": ""getValue"",
    ""responses"": {
      ""204"": { ""description"": ""OK"" },
      ""400"": { ""description"": 42, ""content"": { ""text/plain"": {} } }
    }
  } } }
}";
            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(Document).RunResult.Diagnostics);

            Assert.Equal("OACG100", diagnostic.Id);
            Assert.Contains("response description must be a string", diagnostic.GetMessage());
        }

        [Theory]
        [InlineData("array", "\"items\": { \"type\": \"string\" }")]
        [InlineData("object", "\"properties\": { \"value\": { \"type\": \"string\" } }")]
        public void ReferencedComplexParameterSchemaIsRejected(string type, string shape)
        {
            string document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Parameters"", ""version"": ""1"" },
  ""paths"": { ""/values"": { ""get"": {
    ""operationId"": ""getValues"",
    ""parameters"": [ { ""name"": ""filter"", ""in"": ""query"", ""schema"": {
      ""$ref"": ""#/components/schemas/Filter""
    } } ],
    ""responses"": { ""204"": { ""description"": ""OK"" } }
  } } },
  ""components"": { ""schemas"": { ""Filter"": {
    ""type"": """ + type + @""", " + shape + @"
  } } }
}";
            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(document).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("Array and object parameters", diagnostic.GetMessage());
        }

        [Fact]
        public void ExternalYamlBareArrayParameterSchemaIsRejected()
        {
            const string Root = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Parameters"", ""version"": ""1"" },
  ""paths"": { ""/values"": { ""get"": {
    ""operationId"": ""getValues"",
    ""parameters"": [ { ""name"": ""filter"", ""in"": ""query"", ""schema"": {
      ""$ref"": ""filter.yaml""
    } } ],
    ""responses"": { ""204"": { ""description"": ""OK"" } }
  } } }
}";
            const string External = "{\"type\":\"array\",\"items\":{\"type\":\"string\"}}";
            const string SourcePointer = "/paths/~1values/get/parameters/0/schema/$ref";
            string bundle = TestBundleFactory.CreateV2(
                Root,
                new[]
                {
                    new TestBundleFactory.V2DocumentSpec(
                        "Assets/Specs/filter.yaml",
                        "Assets/Specs/filter.yaml",
                        "yaml",
                        External),
                },
                new[]
                {
                    new TestBundleFactory.V2ReferenceSpec(
                        "root",
                        SourcePointer,
                        "Assets/Specs/filter.yaml",
                        string.Empty),
                });

            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(Root, bundle: bundle).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("Array and object parameters", diagnostic.GetMessage());
        }

        [Fact]
        public void ReferencedScalarEnumAndNullableParameterSchemasRemainSupported()
        {
            const string Document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Parameters"", ""version"": ""1"" },
  ""paths"": { ""/values"": { ""get"": {
    ""operationId"": ""getValues"",
    ""parameters"": [
      { ""name"": ""count"", ""in"": ""query"", ""required"": true,
        ""schema"": { ""$ref"": ""#/components/schemas/CountAlias"" } },
      { ""name"": ""state"", ""in"": ""query"", ""required"": true,
        ""schema"": { ""$ref"": ""#/components/schemas/StateAlias"" } }
    ],
    ""responses"": { ""204"": { ""description"": ""OK"" } }
  } } },
  ""components"": { ""schemas"": {
    ""CountAlias"": { ""$ref"": ""#/components/schemas/NullableCount"" },
    ""NullableCount"": { ""type"": [""integer"", ""null""], ""format"": ""int32"" },
    ""StateAlias"": { ""$ref"": ""#/components/schemas/NullableState"" },
    ""NullableState"": { ""type"": [""string"", ""null""], ""enum"": [""ready"", ""done""] }
  } }
}";
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(Document);

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            MethodDeclarationSyntax method = CSharpSyntaxTree.ParseText(execution.GeneratedSource)
                .GetCompilationUnitRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(value => value.Identifier.ValueText == "getValues");
            string[] types = method.ParameterList.Parameters
                .Where(value => value.Identifier.ValueText != "cancellationToken")
                .Select(value => value.Type!.ToString())
                .ToArray();
            Assert.Contains("int?", types);
            Assert.Contains("NullableState?", types);
        }

        [Fact]
        public void ReferencedNullableRequestAndResponseSchemasRemainNullable()
        {
            const string Document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Nullable refs"", ""version"": ""1"" },
  ""paths"": { ""/values"": { ""post"": {
    ""operationId"": ""roundTrip"",
    ""requestBody"": { ""required"": true, ""content"": {
      ""application/json"": { ""schema"": { ""$ref"": ""#/components/schemas/CountAlias"" } }
    } },
    ""responses"": { ""200"": { ""description"": ""OK"", ""content"": {
      ""application/json"": { ""schema"": { ""$ref"": ""#/components/schemas/StateAlias"" } }
    } } }
  } } },
  ""components"": { ""schemas"": {
    ""CountAlias"": { ""$ref"": ""#/components/schemas/NullableCount"" },
    ""NullableCount"": { ""type"": [""integer"", ""null""], ""format"": ""int32"" },
    ""StateAlias"": { ""$ref"": ""#/components/schemas/NullableState"" },
    ""NullableState"": { ""type"": [""string"", ""null""], ""enum"": [""ready"", ""done""] }
  } }
}";
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(Document);
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            MethodDeclarationSyntax method = CSharpSyntaxTree.ParseText(execution.GeneratedSource)
                .GetCompilationUnitRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(value => value.Identifier.ValueText == "roundTrip");
            ParameterSyntax body = method.ParameterList.Parameters
                .Single(value => value.Identifier.ValueText != "cancellationToken");

            Assert.Equal("int?", body.Type!.ToString());
            Assert.Contains("NullableState?", method.ReturnType.ToString());
        }

        [Theory]
        [InlineData(
            "400",
            "application/octet-stream",
            "{ \"type\": \"string\", \"format\": \"binary\" }")]
        [InlineData(
            "default",
            "application/json",
            "{ \"type\": \"object\", \"additionalProperties\": { \"type\": \"string\" } }")]
        public void ErrorResponseSchemasDoNotApplySuccessBodyGenerationLimits(
            string statusCode,
            string mediaType,
            string schema)
        {
            string document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Error schemas"", ""version"": ""1"" },
  ""paths"": { ""/values"": { ""get"": {
    ""operationId"": ""getValues"",
    ""responses"": {
      ""204"": { ""description"": ""OK"" },
      """ + statusCode + @""": { ""description"": ""Error"", ""content"": {
        """ + mediaType + @""": { ""schema"": " + schema + @" }
      } }
    }
  } } }
}";
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(document);

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
        }

        [Theory]
        [InlineData("{ \"type\": 42 }")]
        [InlineData("{ \"type\": \"object\", \"required\": \"name\" }")]
        [InlineData("{ \"type\": \"string\", \"enum\": \"bad\" }")]
        public void ErrorResponseSchemaKeywordShapesRemainValidated(string schema)
        {
            string document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Invalid error schema"", ""version"": ""1"" },
  ""paths"": { ""/values"": { ""get"": {
    ""operationId"": ""getValues"",
    ""responses"": {
      ""204"": { ""description"": ""OK"" },
      ""400"": { ""description"": ""Bad"", ""content"": {
        ""application/json"": { ""schema"": " + schema + @" }
      } }
    }
  } } }
}";
            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(document).RunResult.Diagnostics);

            Assert.Equal("OACG100", diagnostic.Id);
            Assert.Contains("schema", diagnostic.GetMessage(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SharedErrorResponseSchemaDagReusesCompletedReferenceValidation()
        {
            string document = CreateSharedDagErrorDocument(depth: 15);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();

            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(document);

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assert.True(
                allocated < 96L * 1024L * 1024L,
                "Shared error response DAG validation allocated " + allocated + " bytes.");
        }

        [Fact]
        public void SuccessfulPlainTextBodyRemainsUnsupported()
        {
            const string Document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Responses"", ""version"": ""1"" },
  ""paths"": { ""/values"": { ""get"": {
    ""operationId"": ""getValues"",
    ""responses"": { ""200"": { ""description"": ""OK"", ""content"": {
      ""text/plain"": { ""schema"": { ""type"": ""string"" } }
    } } }
  } } }
}";
            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(Document).RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("Only application/json", diagnostic.GetMessage());
        }

        [Fact]
        public void ErrorResponseSchemaReferencesAreStillResolved()
        {
            const string Document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Responses"", ""version"": ""1"" },
  ""paths"": { ""/values"": { ""get"": {
    ""operationId"": ""getValues"",
    ""responses"": {
      ""204"": { ""description"": ""OK"" },
      ""400"": { ""description"": ""Bad"", ""content"": {
        ""text/plain"": { ""schema"": { ""$ref"": ""#/components/schemas/Missing"" } }
      } }
    }
  } } }
}";
            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(Document).RunResult.Diagnostics);

            Assert.Equal("OACG102", diagnostic.Id);
            Assert.Contains("could not be resolved", diagnostic.GetMessage());
        }

        [Fact]
        public void HeaderParameterIdentityIsCaseInsensitiveAndOperationWireNameWins()
        {
            const string Document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Headers"", ""version"": ""1"" },
  ""paths"": { ""/values"": {
    ""parameters"": [ { ""name"": ""x-trace"", ""in"": ""header"", ""schema"": { ""type"": ""string"" } } ],
    ""get"": {
      ""operationId"": ""getValues"",
      ""parameters"": [ { ""name"": ""X-TRACE"", ""in"": ""header"", ""required"": true,
        ""schema"": { ""type"": ""integer"", ""format"": ""int32"" } } ],
      ""responses"": { ""204"": { ""description"": ""OK"" } }
    }
  } }
}";
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(Document);

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            MethodDeclarationSyntax method = CSharpSyntaxTree.ParseText(execution.GeneratedSource)
                .GetCompilationUnitRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(value => value.Identifier.ValueText == "getValues");
            ParameterSyntax parameter = Assert.Single(
                method.ParameterList.Parameters.Where(value =>
                    value.Identifier.ValueText != "cancellationToken"));
            Assert.Equal("int", parameter.Type!.ToString());
            Assert.Contains("\"X-TRACE\"", execution.GeneratedSource);
            Assert.DoesNotContain("\"x-trace\"", execution.GeneratedSource);
        }

        [Fact]
        public void GenericClientDefinitionReportsOacg005()
        {
            CSharpCompilation compilation = Phase4GeneratorTestHarness.CreateCompilation();
            SyntaxTree originalTree = Assert.Single(compilation.SyntaxTrees);
            string source = originalTree.GetText().ToString().Replace(
                "public partial class Phase4Api",
                "public partial class Phase4Api<T>");
            compilation = compilation.RemoveAllSyntaxTrees().AddSyntaxTrees(
                CSharpSyntaxTree.ParseText(source, (CSharpParseOptions)originalTree.Options));
            GeneratorDriver driver = Phase4GeneratorTestHarness.CreateDriver(
                compilation,
                ImmutableArray<AdditionalText>.Empty);
            GeneratorDriverRunResult result = driver.RunGenerators(compilation).GetRunResult();
            Diagnostic diagnostic = Assert.Single(result.Diagnostics);

            Assert.Equal("OACG005", diagnostic.Id);
            Assert.Contains("non-generic class", diagnostic.GetMessage());
            Assert.Empty(result.GeneratedTrees);
        }

        [Theory]
        [InlineData("２00")]
        [InlineData("6XX")]
        [InlineData("200OK")]
        public void InvalidOrNonAsciiResponseStatusReportsSourceDiagnostic(string status)
        {
            string document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Status"", ""version"": ""1"" },
  ""paths"": { ""/values"": { ""get"": {
    ""operationId"": ""getValues"",
    ""responses"": {
      ""204"": { ""description"": ""OK"" },
      """ + status + @""": { ""description"": ""Invalid"" }
    }
  } } }
}";
            Diagnostic diagnostic = Assert.Single(
                Phase4GeneratorTestHarness.GenerateAndCompile(document).RunResult.Diagnostics);

            Assert.Equal("OACG100", diagnostic.Id);
            Assert.Contains("ASCII HTTP status code", diagnostic.GetMessage());
            Assert.Contains("/responses/", diagnostic.GetMessage());
        }

        private static string CreateRequestDocument(string mediaType)
        {
            return @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Media"", ""version"": ""1"" },
  ""paths"": { ""/values"": { ""post"": {
    ""operationId"": ""sendValue"",
    ""requestBody"": { ""content"": { " +
                   JsonConvert.SerializeObject(mediaType) + @": {
      ""schema"": { ""type"": ""string"" }
    } } },
    ""responses"": { ""204"": { ""description"": ""No Content"" } }
  } } }
}";
        }

        private static string CreateSharedDagErrorDocument(int depth)
        {
            var schemas = new StringBuilder();
            schemas.Append("\"S0\":{\"type\":\"string\"}");
            for (int index = 1; index <= depth; index++)
            {
                schemas.Append(",\"S").Append(index).Append("\":{");
                schemas.Append("\"type\":\"object\",\"properties\":{");
                schemas.Append("\"a\":{\"$ref\":\"#/components/schemas/S")
                    .Append(index - 1)
                    .Append("\"},");
                schemas.Append("\"b\":{\"$ref\":\"#/components/schemas/S")
                    .Append(index - 1)
                    .Append("\"}}}");
            }

            return "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"DAG\",\"version\":\"1\"}," +
                   "\"paths\":{\"/values\":{\"get\":{\"operationId\":\"getValues\",\"responses\":{" +
                   "\"204\":{\"description\":\"OK\"}," +
                   "\"400\":{\"description\":\"Bad\",\"content\":{\"application/json\":{\"schema\":{" +
                   "\"$ref\":\"#/components/schemas/S" + depth + "\"}}}}}}}}," +
                   "\"components\":{\"schemas\":{" + schemas + "}}}";
        }

        private static object CreateClient(Assembly assembly, HttpClient httpClient)
        {
            Type clientType = assembly.GetType("Generated.Phase4.Phase4Api", throwOnError: true)!;
            return Activator.CreateInstance(clientType, httpClient)!;
        }

        private static async Task Invoke(object client, string methodName, params object[] arguments)
        {
            var task = (Task)client.GetType().GetMethod(methodName)!.Invoke(client, arguments)!;
            await task.ConfigureAwait(false);
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _statusCode;
            private readonly string _responseBody;

            internal RecordingHandler(HttpStatusCode statusCode, string responseBody)
            {
                _statusCode = statusCode;
                _responseBody = responseBody;
            }

            internal string RequestBody { get; private set; } = string.Empty;

            internal string? ContentType { get; private set; }

            internal string? Charset { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                if (request.Content is not null)
                {
                    RequestBody = await request.Content.ReadAsStringAsync().ConfigureAwait(false);
                    ContentType = request.Content.Headers.ContentType?.MediaType;
                    Charset = request.Content.Headers.ContentType?.CharSet;
                }

                return new HttpResponseMessage(_statusCode)
                {
                    Content = new StringContent(_responseBody)
                };
            }
        }
    }
}
