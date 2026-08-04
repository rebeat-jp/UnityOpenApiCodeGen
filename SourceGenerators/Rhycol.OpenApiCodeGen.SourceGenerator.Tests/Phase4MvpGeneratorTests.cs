using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

using Newtonsoft.Json;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    public sealed class Phase4MvpGeneratorTests
    {
        [Theory]
        [InlineData("3.0.4")]
        [InlineData("3.1.1")]
        public void SupportedOpenApiVersionsGenerateCompilableClient(string version)
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                CreateMinimalDocument(version));

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.GeneratorDiagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assert.Contains("public partial class Phase4Api", execution.GeneratedSource);
            Assert.Contains("Task ping(", execution.GeneratedSource);
        }

        [Fact]
        public void OpenApi32IsRejectedAtVersionNode()
        {
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                CreateMinimalDocument("3.2.0"));
            Diagnostic diagnostic = Assert.Single(execution.RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Equal(TestBundleFactory.SourcePath, diagnostic.Location.GetLineSpan().Path);
            Assert.Contains("logical path '/openapi'", diagnostic.GetMessage());
            Assert.Contains("supports OpenAPI 3.0.* and 3.1.*", diagnostic.GetMessage());
            Assert.Empty(execution.RunResult.GeneratedTrees);
        }

        [Fact]
        public void InternalComponentReferenceGeneratesReferencedDto()
        {
            const string Document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""References"", ""version"": ""1"" },
  ""paths"": {
    ""/pets"": {
      ""get"": {
        ""operationId"": ""getPet"",
        ""responses"": {
          ""200"": {
            ""description"": ""OK"",
            ""content"": {
              ""application/json"": {
                ""schema"": { ""$ref"": ""#/components/schemas/Pet"" }
              }
            }
          }
        }
      }
    }
  },
  ""components"": {
    ""schemas"": {
      ""Pet"": {
        ""type"": ""object"",
        ""properties"": { ""name"": { ""type"": ""string"" } }
      }
    }
  }
}";

            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(Document);

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assert.Contains("public sealed class Pet", execution.GeneratedSource);
            Assert.Contains("Task<Pet> getPet", execution.GeneratedSource);
        }

        [Fact]
        public void EscapedJsonPointerTokensResolveComponentNameContainingSlashAndTilde()
        {
            const string Document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Escaped reference"", ""version"": ""1"" },
  ""paths"": {
    ""/items"": { ""get"": { ""operationId"": ""getItem"", ""responses"": {
      ""200"": { ""description"": ""OK"", ""content"": { ""application/json"": {
        ""schema"": { ""$ref"": ""#/components/schemas/A~1B~0C"" }
      } } }
    } } }
  },
  ""components"": { ""schemas"": {
    ""A/B~C"": { ""type"": ""object"", ""properties"": {
      ""value"": { ""type"": ""string"" }
    } }
  } }
}";

            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(Document);

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assert.Contains("public sealed class ABC", execution.GeneratedSource);
            Assert.Contains("Task<ABC> getItem", execution.GeneratedSource);
        }

        [Theory]
        [MemberData(nameof(InvalidReferenceCases))]
        public void InvalidReferencesReportSpecificDiagnostic(
            string reference,
            string expectedDiagnosticId,
            string expectedMessage)
        {
            string document = CreateReferenceDocument(reference);
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(document);
            Diagnostic diagnostic = Assert.Single(execution.RunResult.Diagnostics);

            Assert.Equal(expectedDiagnosticId, diagnostic.Id);
            Assert.Contains(expectedMessage, diagnostic.GetMessage());
            Assert.Contains("logical path '/paths/~1pets/get/responses/200/content/application~1json/schema/$ref'", diagnostic.GetMessage());
            Assert.Empty(execution.RunResult.GeneratedTrees);
        }

        public static IEnumerable<object[]> InvalidReferenceCases()
        {
            yield return new object[]
            {
                "#/components/schemas/Missing",
                "OACG102",
                "could not be resolved"
            };
            yield return new object[]
            {
                "other.json#/components/schemas/Pet",
                "OACG104",
                "External $ref values are not supported"
            };
            yield return new object[]
            {
                "#/components/schemas/A%2FB",
                "OACG101",
                "requires $ref values to target a named component directly"
            };
            yield return new object[]
            {
                "#/components/schemas/A%ZZ",
                "OACG102",
                "invalid percent encoding"
            };
        }

        [Fact]
        public void CyclicInternalReferenceReportsOacg103()
        {
            const string Document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Cycle"", ""version"": ""1"" },
  ""paths"": {},
  ""components"": {
    ""schemas"": {
      ""A"": { ""$ref"": ""#/components/schemas/B"" },
      ""B"": { ""$ref"": ""#/components/schemas/A"" }
    }
  }
}";

            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(Document);
            Diagnostic diagnostic = Assert.Single(execution.RunResult.Diagnostics);

            Assert.Equal("OACG103", diagnostic.Id);
            Assert.Contains("cyclic internal $ref", diagnostic.GetMessage());
            Assert.Contains("logical path '/components/schemas/B/$ref'", diagnostic.GetMessage());
        }

        [Fact]
        public void UnsupportedElementReportsRawSourcePositionAndLogicalPath()
        {
            const string Document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Position"", ""version"": ""1"" },
  ""security"": [],
  ""paths"": {}
}";
            string bundle = TestBundleFactory.Create(Document);
            const string Original =
                "\"name\":\"security\",\"line\":1,\"column\":1,\"value\":{\"kind\":\"array\",\"line\":1,\"column\":1,\"items\":[]}";
            const string Positioned =
                "\"name\":\"security\",\"line\":3,\"column\":3,\"value\":{\"kind\":\"array\",\"line\":23,\"column\":9,\"items\":[]}";
            Assert.Contains(Original, bundle);
            bundle = bundle.Replace(Original, Positioned);

            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                Document,
                bundle: bundle);
            Diagnostic diagnostic = Assert.Single(execution.RunResult.Diagnostics);
            FileLinePositionSpan span = diagnostic.Location.GetLineSpan();

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Equal(TestBundleFactory.SourcePath, span.Path);
            Assert.Equal(new LinePosition(22, 8), span.StartLinePosition);
            Assert.Contains("logical path '/security'", diagnostic.GetMessage());
        }

        [Fact]
        public void OperationParameterOverridesPathParameterByWireIdentity()
        {
            const string Document = @"{
  ""openapi"": ""3.0.3"",
  ""info"": { ""title"": ""Override"", ""version"": ""1"" },
  ""paths"": {
    ""/items/{id}"": {
      ""parameters"": [
        { ""name"": ""id"", ""in"": ""path"", ""required"": true, ""schema"": { ""type"": ""string"" } }
      ],
      ""get"": {
        ""operationId"": ""getItem"",
        ""parameters"": [
          { ""name"": ""id"", ""in"": ""path"", ""required"": true, ""schema"": { ""type"": ""integer"", ""format"": ""int32"" } }
        ],
        ""responses"": { ""204"": { ""description"": ""No Content"" } }
      }
    }
  }
}";

            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(Document);

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assert.Contains("Task getItem(int id,", execution.GeneratedSource);
            Assert.DoesNotContain("Task getItem(string id,", execution.GeneratedSource);
            INamedTypeSymbol client = execution.OutputCompilation.GetTypeByMetadataName(
                "Generated.Phase4.Phase4Api")!;
            IMethodSymbol method = Assert.Single(client.GetMembers("getItem").OfType<IMethodSymbol>());
            Assert.Equal(2, method.Parameters.Length);
            Assert.Equal(SpecialType.System_Int32, method.Parameters[0].Type.SpecialType);
            Assert.Equal("id", method.Parameters[0].Name);
        }

        [Fact]
        public void DuplicateParametersInOneScopeAreRejected()
        {
            const string Document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Duplicate"", ""version"": ""1"" },
  ""paths"": { ""/items"": { ""get"": {
    ""operationId"": ""getItems"",
    ""parameters"": [
      { ""name"": ""q"", ""in"": ""query"", ""schema"": { ""type"": ""string"" } },
      { ""name"": ""q"", ""in"": ""query"", ""schema"": { ""type"": ""integer"" } }
    ],
    ""responses"": { ""204"": { ""description"": ""No Content"" } }
  } } }
}";

            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(Document);
            Diagnostic diagnostic = Assert.Single(execution.RunResult.Diagnostics);

            Assert.Equal("OACG100", diagnostic.Id);
            Assert.Contains(
                "parameters array contains more than one parameter named 'q' in 'query'",
                diagnostic.GetMessage());
            Assert.Contains("logical path '/paths/~1items/get/parameters/1'", diagnostic.GetMessage());
            Assert.Empty(execution.RunResult.GeneratedTrees);
        }

        [Fact]
        public void PathParameterNotPresentInTemplateIsRejected()
        {
            const string Document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Unused path parameter"", ""version"": ""1"" },
  ""paths"": { ""/items"": { ""get"": {
    ""operationId"": ""getItems"",
    ""parameters"": [
      { ""name"": ""id"", ""in"": ""path"", ""required"": true, ""schema"": { ""type"": ""string"" } }
    ],
    ""responses"": { ""204"": { ""description"": ""No Content"" } }
  } } }
}";

            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(Document);
            Diagnostic diagnostic = Assert.Single(execution.RunResult.Diagnostics);

            Assert.Equal("OACG100", diagnostic.Id);
            Assert.Contains("Path parameter 'id' is not present", diagnostic.GetMessage());
            Assert.Empty(execution.RunResult.GeneratedTrees);
        }

        [Fact]
        public void OperationIdMatchingApiNameGetsDeterministicSuffixAndCompiles()
        {
            const string Document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Method collision"", ""version"": ""1"" },
  ""paths"": { ""/items"": { ""get"": {
    ""operationId"": ""Phase4Api"",
    ""parameters"": [
      { ""name"": ""relativePath"", ""in"": ""query"", ""required"": true, ""schema"": { ""type"": ""string"" } },
      { ""name"": ""request"", ""in"": ""query"", ""required"": true, ""schema"": { ""type"": ""string"" } },
      { ""name"": ""_httpClient"", ""in"": ""query"", ""required"": true, ""schema"": { ""type"": ""string"" } },
      { ""name"": ""CreateRequestUri"", ""in"": ""query"", ""required"": true, ""schema"": { ""type"": ""string"" } }
    ],
    ""responses"": { ""200"": { ""description"": ""OK"", ""content"": {
      ""application/json"": { ""schema"": { ""$ref"": ""#/components/schemas/Pet"" } }
    } } }
  } } },
  ""components"": { ""schemas"": {
    ""Pet"": { ""type"": ""object"", ""required"": [""pet"", ""status""], ""properties"": {
      ""pet"": { ""type"": ""string"" },
      ""status"": { ""$ref"": ""#/components/schemas/Status"" }
    } },
    ""Status"": { ""type"": ""string"", ""enum"": [""ok"", ""status""] }
  } }
}";

            Phase4GeneratorExecution first = Phase4GeneratorTestHarness.GenerateAndCompile(Document);
            Phase4GeneratorExecution second = Phase4GeneratorTestHarness.GenerateAndCompile(Document);

            Assert.Empty(first.RunResult.Diagnostics);
            Assert.Empty(first.CompilationErrors);
            Assert.Empty(second.RunResult.Diagnostics);
            Assert.Empty(second.CompilationErrors);
            Assert.Contains(" Phase4Api2(", first.GeneratedSource);
            Assert.Equal(GetHintedSources(first), GetHintedSources(second));

            INamedTypeSymbol client = first.OutputCompilation.GetTypeByMetadataName(
                "Generated.Phase4.Phase4Api")!;
            IMethodSymbol method = Assert.Single(
                client.GetMembers("Phase4Api2").OfType<IMethodSymbol>());
            Assert.Equal(
                new[]
                {
                    "CreateRequestUri2",
                    "_httpClient2",
                    "relativePath2",
                    "request2",
                    "cancellationToken"
                },
                method.Parameters.Select(static parameter => parameter.Name));

            INamedTypeSymbol pet = first.OutputCompilation.GetTypeByMetadataName(
                "Generated.Phase4.Pet")!;
            Assert.Contains(pet.GetMembers().OfType<IPropertySymbol>(), property => property.Name == "Pet2");
            INamedTypeSymbol status = first.OutputCompilation.GetTypeByMetadataName(
                "Generated.Phase4.Status")!;
            Assert.Contains(status.GetMembers().OfType<IFieldSymbol>(), field => field.Name == "Status2");
        }

        [Fact]
        public void RequiredPropertyMustBeDeclaredInProperties()
        {
            const string Document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Required"", ""version"": ""1"" },
  ""paths"": {},
  ""components"": { ""schemas"": { ""Payload"": {
    ""type"": ""object"",
    ""required"": [""missing""],
    ""properties"": { ""present"": { ""type"": ""string"" } }
  } } }
}";

            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(Document);
            Diagnostic diagnostic = Assert.Single(execution.RunResult.Diagnostics);

            Assert.Equal("OACG100", diagnostic.Id);
            Assert.Contains("Required property 'missing' is not declared", diagnostic.GetMessage());
            Assert.Contains(
                "logical path '/components/schemas/Payload/required/0'",
                diagnostic.GetMessage());
        }

        [Fact]
        public void DuplicateRequiredPropertyIsRejected()
        {
            const string Document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Required duplicate"", ""version"": ""1"" },
  ""paths"": {},
  ""components"": { ""schemas"": { ""Payload"": {
    ""type"": ""object"",
    ""required"": [""id"", ""id""],
    ""properties"": { ""id"": { ""type"": ""integer"" } }
  } } }
}";

            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(Document);
            Diagnostic diagnostic = Assert.Single(execution.RunResult.Diagnostics);

            Assert.Equal("OACG100", diagnostic.Id);
            Assert.Contains("required array contains the property name 'id' more than once", diagnostic.GetMessage());
            Assert.Contains(
                "logical path '/components/schemas/Payload/required/1'",
                diagnostic.GetMessage());
        }

        [Theory]
        [InlineData("readOnly")]
        [InlineData("writeOnly")]
        public void ReadOnlyAndWriteOnlySchemasAreRejected(string keyword)
        {
            string document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Directional property"", ""version"": ""1"" },
  ""paths"": {},
  ""components"": { ""schemas"": { ""Payload"": {
    ""type"": ""object"",
    ""properties"": { ""value"": { ""type"": ""string"", """ + keyword + @""": true } }
  } } }
}";

            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(document);
            Diagnostic diagnostic = Assert.Single(execution.RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("Schema keyword '" + keyword + "' is not supported", diagnostic.GetMessage());
            Assert.Contains(
                "logical path '/components/schemas/Payload/properties/value/" + keyword + "'",
                diagnostic.GetMessage());
        }

        [Fact]
        public void QueryAllowReservedFalseIsAcceptedButTrueIsRejected()
        {
            Phase4GeneratorExecution allowed = Phase4GeneratorTestHarness.GenerateAndCompile(
                CreateAllowReservedDocument(allowReserved: false));

            Assert.Empty(allowed.RunResult.Diagnostics);
            Assert.Empty(allowed.CompilationErrors);

            Phase4GeneratorExecution rejected = Phase4GeneratorTestHarness.GenerateAndCompile(
                CreateAllowReservedDocument(allowReserved: true));
            Diagnostic diagnostic = Assert.Single(rejected.RunResult.Diagnostics);

            Assert.Equal("OACG101", diagnostic.Id);
            Assert.Contains("allowReserved set to true are not supported", diagnostic.GetMessage());
            Assert.Contains(
                "logical path '/paths/~1items/get/parameters/0/allowReserved'",
                diagnostic.GetMessage());
        }

        [Fact]
        public void GenerationIsDeterministicAndEscapesReservedIdentifierCollisions()
        {
            const string DocumentA = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Names"", ""version"": ""1"" },
  ""paths"": {
    ""/b"": { ""get"": { ""operationId"": ""@class"", ""responses"": { ""204"": { ""description"": ""OK"" } } } },
    ""/a"": { ""get"": { ""operationId"": ""class"", ""parameters"": [
      { ""name"": ""event"", ""in"": ""query"", ""schema"": { ""type"": ""string"" } },
      { ""name"": ""@event"", ""in"": ""query"", ""schema"": { ""type"": ""string"" } }
    ], ""responses"": { ""204"": { ""description"": ""OK"" } } } }
  },
  ""components"": { ""schemas"": {
    ""foo-bar"": { ""type"": ""object"", ""properties"": {} },
    ""foo bar"": { ""type"": ""object"", ""properties"": {} }
  } }
}";
            const string DocumentB = @"{
  ""components"": { ""schemas"": {
    ""foo bar"": { ""properties"": {}, ""type"": ""object"" },
    ""foo-bar"": { ""properties"": {}, ""type"": ""object"" }
  } },
  ""paths"": {
    ""/a"": { ""get"": { ""responses"": { ""204"": { ""description"": ""OK"" } }, ""parameters"": [
      { ""schema"": { ""type"": ""string"" }, ""in"": ""query"", ""name"": ""@event"" },
      { ""schema"": { ""type"": ""string"" }, ""in"": ""query"", ""name"": ""event"" }
    ], ""operationId"": ""class"" } },
    ""/b"": { ""get"": { ""responses"": { ""204"": { ""description"": ""OK"" } }, ""operationId"": ""@class"" } }
  },
  ""info"": { ""version"": ""1"", ""title"": ""Names"" },
  ""openapi"": ""3.1.0""
}";

            Phase4GeneratorExecution first = Phase4GeneratorTestHarness.GenerateAndCompile(DocumentA);
            Phase4GeneratorExecution second = Phase4GeneratorTestHarness.GenerateAndCompile(DocumentB);

            Assert.Empty(first.RunResult.Diagnostics);
            Assert.Empty(second.RunResult.Diagnostics);
            Assert.Empty(first.CompilationErrors);
            Assert.Empty(second.CompilationErrors);
            Assert.Equal(GetHintedSources(first), GetHintedSources(second));
            Assert.Contains("Task @class(", first.GeneratedSource);
            Assert.Contains("Task @class2(", first.GeneratedSource);
            Assert.Contains("string? @event", first.GeneratedSource);
            Assert.Contains("string? @event2", first.GeneratedSource);
            Assert.Contains("public sealed class FooBar", first.GeneratedSource);
            Assert.Contains("public sealed class FooBar2", first.GeneratedSource);
        }

        [Fact]
        public void DtoEnumArrayNullableAndNewtonsoftContractCompilesAndRoundTrips()
        {
            const string Document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""DTO"", ""version"": ""1"" },
  ""paths"": {
    ""/pets"": { ""get"": { ""operationId"": ""getPets"", ""responses"": {
      ""200"": { ""description"": ""OK"", ""content"": { ""application/json"": {
        ""schema"": { ""type"": ""array"", ""items"": { ""$ref"": ""#/components/schemas/Pet"" } }
      } } }
    } } }
  },
  ""components"": { ""schemas"": {
    ""Pet"": {
      ""type"": ""object"",
      ""required"": [""id"", ""nullableName"", ""status""],
      ""properties"": {
        ""id"": { ""type"": ""integer"", ""format"": ""int64"" },
        ""nickname"": { ""type"": ""string"" },
        ""nullableName"": { ""type"": [""string"", ""null""] },
        ""status"": { ""$ref"": ""#/components/schemas/Status"" },
        ""tags"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } }
      }
    },
    ""Status"": { ""type"": ""string"", ""enum"": [""class"", ""in-progress""] }
  } }
}";

            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(Document);
            string source = execution.GeneratedSource;

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assert.Contains("JsonObject(global::Newtonsoft.Json.MemberSerialization.OptIn)", source);
            Assert.Contains("JsonProperty(\"id\", Required = global::Newtonsoft.Json.Required.Always)", source);
            Assert.Contains("public long Id { get; set; }", source);
            Assert.Contains("JsonProperty(\"nullableName\", Required = global::Newtonsoft.Json.Required.AllowNull)", source);
            Assert.Contains("public string? NullableName { get; set; }", source);
            Assert.Contains("JsonProperty(\"nickname\", Required = global::Newtonsoft.Json.Required.Default, NullValueHandling = global::Newtonsoft.Json.NullValueHandling.Ignore)", source);
            Assert.Contains("public global::System.Collections.Generic.List<string>? Tags { get; set; }", source);
            Assert.Contains("StringEnumConverter", source);
            Assert.Contains("EnumMember(Value = \"in-progress\")", source);
            Assert.DoesNotContain("System.Text.Json", source);

            System.Reflection.Assembly assembly = execution.EmitAssembly();
            Type petType = assembly.GetType("Generated.Phase4.Pet", throwOnError: true)!;
            Type statusType = assembly.GetType("Generated.Phase4.Status", throwOnError: true)!;
            object pet = Activator.CreateInstance(petType)!;
            petType.GetProperty("Id")!.SetValue(pet, 7L);
            petType.GetProperty("NullableName")!.SetValue(pet, null);
            petType.GetProperty("Status")!.SetValue(pet, Enum.Parse(statusType, "InProgress"));

            string json = JsonConvert.SerializeObject(pet);
            object restored = JsonConvert.DeserializeObject(json, petType)!;

            Assert.Contains("\"id\":7", json);
            Assert.Contains("\"nullableName\":null", json);
            Assert.Contains("\"status\":\"in-progress\"", json);
            Assert.DoesNotContain("nickname", json);
            Assert.Equal(7L, petType.GetProperty("Id")!.GetValue(restored));
        }

        [Fact]
        public void DefinitionOptionChangeInvalidatesDefinitionAndMatchButNotBundleParsing()
        {
            const string Document = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Incremental"", ""version"": ""1"" },
  ""paths"": {}
}";
            CSharpCompilation firstCompilation = Phase4GeneratorTestHarness.CreateCompilation();
            InMemoryAdditionalText bundle = Phase4GeneratorTestHarness.CreateBundleText(
                TestBundleFactory.SpecId,
                TestBundleFactory.Create(Document));
            GeneratorDriver driver = Phase4GeneratorTestHarness.CreateDriver(
                firstCompilation,
                ImmutableArray.Create<AdditionalText>(bundle));

            driver = driver.RunGenerators(firstCompilation);
            driver = driver.RunGenerators(firstCompilation);
            GeneratorDriverRunResult unchanged = driver.GetRunResult();
            Assert.All(
                Phase4GeneratorTestHarness.GetReasons(unchanged, "OpenApiClientDefinitions"),
                reason => Assert.Equal(IncrementalStepRunReason.Cached, reason));
            Assert.All(
                Phase4GeneratorTestHarness.GetReasons(unchanged, "OpenApiParseNormalizedBundle"),
                reason => Assert.Equal(IncrementalStepRunReason.Cached, reason));
            Assert.All(
                Phase4GeneratorTestHarness.GetReasons(unchanged, "OpenApiMatchDefinitionToBundle"),
                reason => Assert.Equal(IncrementalStepRunReason.Cached, reason));

            CSharpCompilation changedCompilation = Phase4GeneratorTestHarness.CreateCompilation(
                apiName: "ChangedApi",
                assemblyName: firstCompilation.AssemblyName);
            driver = driver.RunGenerators(changedCompilation);
            GeneratorDriverRunResult changed = driver.GetRunResult();

            Assert.Contains(
                IncrementalStepRunReason.Modified,
                Phase4GeneratorTestHarness.GetReasons(changed, "OpenApiClientDefinitions"));
            Assert.All(
                Phase4GeneratorTestHarness.GetReasons(changed, "OpenApiParseNormalizedBundle"),
                reason => Assert.Equal(IncrementalStepRunReason.Cached, reason));
            Assert.Contains(
                IncrementalStepRunReason.Modified,
                Phase4GeneratorTestHarness.GetReasons(changed, "OpenApiMatchDefinitionToBundle"));
            Assert.Contains(
                changed.GeneratedTrees,
                tree => tree.ToString().Contains("class ChangedApi"));
        }

        private static string CreateMinimalDocument(string version)
        {
            return "{\"openapi\":\"" + version +
                   "\",\"info\":{\"title\":\"Minimal\",\"version\":\"1\"}," +
                   "\"paths\":{\"/ping\":{\"get\":{\"operationId\":\"ping\"," +
                   "\"responses\":{\"204\":{\"description\":\"No Content\"}}}}}}";
        }

        private static string CreateReferenceDocument(string reference)
        {
            return "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"References\",\"version\":\"1\"}," +
                   "\"paths\":{\"/pets\":{\"get\":{\"operationId\":\"getPet\",\"responses\":{" +
                   "\"200\":{\"description\":\"OK\",\"content\":{\"application/json\":{" +
                   "\"schema\":{\"$ref\":" + SymbolDisplay.FormatLiteral(reference, quote: true) +
                   "}}}}}}}}}";
        }

        private static string CreateAllowReservedDocument(bool allowReserved)
        {
            return "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Reserved\",\"version\":\"1\"}," +
                   "\"paths\":{\"/items\":{\"get\":{\"operationId\":\"getItems\",\"parameters\":[{" +
                   "\"name\":\"q\",\"in\":\"query\",\"allowReserved\":" +
                   (allowReserved ? "true" : "false") +
                   ",\"schema\":{\"type\":\"string\"}}]," +
                   "\"responses\":{\"204\":{\"description\":\"No Content\"}}}}}}";
        }

        private static IReadOnlyList<string> GetHintedSources(Phase4GeneratorExecution execution)
        {
            return execution.RunResult.Results.Single().GeneratedSources
                .OrderBy(static source => source.HintName, StringComparer.Ordinal)
                .Select(static source => source.HintName + "\n" + source.SourceText)
                .ToArray();
        }

    }
}
