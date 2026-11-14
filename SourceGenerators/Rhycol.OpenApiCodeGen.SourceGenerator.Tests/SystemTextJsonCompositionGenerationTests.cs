using System;
using System.IO;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    /// <summary>
    /// P2: System.Text.Json generates composition (polymorphic converters) and the run-time validation of
    /// constraints like Newtonsoft.Json does. The specs that were rejected with OACG010 in P1 now generate
    /// without a diagnostic, compile without any reference to Newtonsoft.Json, and are covered by the
    /// serializer matrix tests for their behavior.
    /// </summary>
    public sealed class SystemTextJsonCompositionGenerationTests
    {
        private static string Spec(string schema, string parameters = "[]") => @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Gate"", ""version"": ""1"" },
  ""paths"": {
    ""/a"": {
      ""get"": {
        ""operationId"": ""getA"",
        ""parameters"": " + parameters + @",
        ""responses"": {
          ""200"": {
            ""description"": ""ok"",
            ""content"": { ""application/json"": { ""schema"": " + schema + @" } }
          }
        }
      }
    }
  }
}";

        private const string Plain =
            "{ \"type\": \"object\", \"required\": [\"name\"], \"properties\": { \"name\": { \"type\": \"string\" } } }";

        public static readonly object[][] FormerlyRejectedSpecs =
        {
            new object[]
            {
                "minLength in a response member",
                Spec("{ \"type\": \"object\", \"properties\": { \"name\": { \"type\": \"string\", \"minLength\": 3 } } }"),
                "constraint"
            },
            new object[]
            {
                "minimum of a query parameter",
                Spec(Plain, "[ { \"name\": \"q\", \"in\": \"query\", \"schema\": { \"type\": \"integer\", \"minimum\": 1 } } ]"),
                "constraint"
            },
            new object[]
            {
                "minItems of an array response",
                Spec("{ \"type\": \"array\", \"minItems\": 1, \"items\": { \"type\": \"string\" } }"),
                "constraint"
            },
            new object[]
            {
                "pattern of a pet member in an object schema",
                Spec("{ \"type\": \"object\", \"properties\": { \"code\": { \"type\": \"string\", \"pattern\": \"^[a-z]+$\" } } }"),
                "constraint"
            },
            new object[]
            {
                "minProperties of an object schema",
                Spec("{ \"type\": \"object\", \"minProperties\": 1, \"additionalProperties\": { \"type\": \"string\" } }"),
                "constraint"
            },
            new object[]
            {
                "oneOf",
                Spec("{ \"oneOf\": [ { \"type\": \"string\" }, { \"type\": \"integer\" } ] }"),
                "composition"
            },
            new object[]
            {
                "anyOf of objects",
                Spec("{ \"anyOf\": [ { \"type\": \"object\", \"properties\": { \"a\": { \"type\": \"string\" } } }, { \"type\": \"object\", \"properties\": { \"b\": { \"type\": \"integer\" } } } ] }"),
                "composition"
            },
            new object[]
            {
                "oneOf with a discriminator",
                Spec("{ \"oneOf\": [ { \"$ref\": \"#/components/schemas/Cat\" }, { \"$ref\": \"#/components/schemas/Dog\" } ], \"discriminator\": { \"propertyName\": \"kind\" } }")
                    .Replace("\r\n", "\n")
                    .Replace(
                        "\n  }\n}",
                        "\n  },\n  \"components\": { \"schemas\": {" +
                        " \"Cat\": { \"type\": \"object\", \"required\": [\"kind\"], \"properties\": { \"kind\": { \"type\": \"string\" } } }," +
                        " \"Dog\": { \"type\": \"object\", \"required\": [\"kind\"], \"properties\": { \"kind\": { \"type\": \"string\" } } } } }\n}"),
                "composition"
            },
            new object[]
            {
                "not",
                Spec("{ \"type\": \"string\", \"not\": { \"enum\": [ \"x\" ] } }"),
                "not keyword"
            }
        };

        [Theory]
        [MemberData(nameof(FormerlyRejectedSpecs))]
        public void SpecsThatNeededCompositionOrConstraintsNowGenerateForSystemTextJson(
            string description,
            string spec,
            string expectedKeyword)
        {
            Phase4GeneratorExecution stj = Phase4GeneratorTestHarness.GenerateAndCompile(
                spec,
                options: new Phase4CompilationOptions { JsonSerializer = "SystemTextJson", Newtonsoft = false });

            Assert.Empty(stj.RunResult.Diagnostics);
            Assert.NotEmpty(stj.RunResult.GeneratedTrees);
            Assert.Empty(stj.CompilationErrors);
            Assert.DoesNotContain("Newtonsoft", stj.GeneratedSource);
            // The feature that the spec needs is in the generated source.
            string expected = expectedKeyword == "composition" ? "JsonConverter<" :
                expectedKeyword == "not keyword" ? "notMatched" : "__OacgConstraint_";
            Assert.True(stj.GeneratedSource.Contains(expected), description);

            Phase4GeneratorExecution newtonsoft = Phase4GeneratorTestHarness.GenerateAndCompile(spec);
            Assert.Empty(newtonsoft.RunResult.Diagnostics);
            Assert.Empty(newtonsoft.CompilationErrors);
        }

        [Theory]
        [InlineData("composition.json")]
        [InlineData("openapi32.json")]
        [InlineData("swagger2.json")]
        public void TheCatalogSpecsGenerateAndCompileForSystemTextJson(string specFile)
        {
            string spec = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ApiSurfaceSpecs", specFile));

            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                spec, options: new Phase4CompilationOptions { JsonSerializer = "SystemTextJson", Newtonsoft = false });

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.NotEmpty(execution.RunResult.GeneratedTrees);
            Assert.Empty(execution.CompilationErrors);
            Assert.DoesNotContain("Newtonsoft", execution.GeneratedSource);
        }

        [Fact]
        public void UnionMemberKindsAreCollectedForTheConverterRegistration()
        {
            string spec = Spec("{ \"oneOf\": [ { \"type\": \"integer\", \"format\": \"int32\" }, { \"type\": \"string\" } ] }");
            var service = new GenerationService();
            OpenApiGenerationModel model = service.BuildMvpModelFromOpenApiNode(
                TestBundleFactory.ReadRoot(spec), new GeneratorOptions("Api", "Generated.Gate"));

            var kinds = SystemTextJsonTypeKinds.CollectKinds(model);

            Assert.Contains(GeneratedTypeKind.Int32, kinds);
            Assert.Contains(GeneratedTypeKind.String, kinds);
            Assert.NotEmpty(service.EmitMvpModel(
                model, HttpTransportKind.HttpClient, JsonSerializerKind.SystemTextJson));
        }
    }
}
