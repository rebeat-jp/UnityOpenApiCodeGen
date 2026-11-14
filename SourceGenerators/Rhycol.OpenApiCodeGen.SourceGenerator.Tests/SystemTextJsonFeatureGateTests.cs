using System;
using System.IO;
using System.Linq;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    /// <summary>
    /// System.Text.Json does not generate composition (polymorphic converters) or the run-time validation
    /// of constraints yet (P2). A spec that needs them is rejected with OACG010, decided from the model,
    /// and nothing is generated; the same spec still generates with Newtonsoft.Json.
    /// </summary>
    public sealed class SystemTextJsonFeatureGateTests
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

        public static readonly object[][] RejectedSpecs =
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
        [MemberData(nameof(RejectedSpecs))]
        public void SystemTextJsonRejectsSpecsThatNeedCompositionOrConstraintsAndNewtonsoftStillGenerates(
            string description,
            string spec,
            string expectedKeyword)
        {
            Phase4GeneratorExecution rejected = Phase4GeneratorTestHarness.GenerateAndCompile(
                spec, options: new Phase4CompilationOptions { JsonSerializer = "SystemTextJson" });

            Assert.True(rejected.RunResult.Diagnostics.Any(static d => d.Id == "OACG010"), description);
            Assert.Equal(new[] { "OACG010" }, rejected.RunResult.Diagnostics.Select(static d => d.Id).ToArray());
            Assert.Empty(rejected.RunResult.GeneratedTrees);
            Assert.Contains(
                expectedKeyword,
                rejected.RunResult.Diagnostics.Single().GetMessage(),
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, rejected.RunResult.Diagnostics.Single().Severity);

            Phase4GeneratorExecution accepted = Phase4GeneratorTestHarness.GenerateAndCompile(spec);
            Assert.Empty(accepted.RunResult.Diagnostics);
            Assert.NotEmpty(accepted.RunResult.GeneratedTrees);
            Assert.Empty(accepted.CompilationErrors);
        }

        [Fact]
        public void ASpecWithoutThoseFeaturesIsAcceptedAndGateFindsNothing()
        {
            string spec = Spec(Plain);
            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                spec, options: new Phase4CompilationOptions { JsonSerializer = "SystemTextJson" });

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            Assert.NotEmpty(execution.RunResult.GeneratedTrees);

            var service = new GenerationService();
            OpenApiGenerationModel model = service.BuildMvpModelFromOpenApiNode(
                TestBundleFactory.ReadRoot(spec), new GeneratorOptions("Api", "Generated.Gate"));
            Assert.Empty(SystemTextJsonFeatureGate.FindUnsupported(model));
        }

        [Theory]
        [InlineData("composition.json", "composition")]
        [InlineData("openapi32.json", "composition")]
        [InlineData("swagger2.json", "composition")]
        public void TheCompositionSpecsOfTheCatalogAreRejectedForSystemTextJson(string specFile, string keyword)
        {
            string spec = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ApiSurfaceSpecs", specFile));

            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                spec, options: new Phase4CompilationOptions { JsonSerializer = "SystemTextJson" });

            Assert.Equal(new[] { "OACG010" }, execution.RunResult.Diagnostics.Select(static d => d.Id).ToArray());
            Assert.Empty(execution.RunResult.GeneratedTrees);
            Assert.Contains(keyword, execution.RunResult.Diagnostics.Single().GetMessage());
        }

        [Fact]
        public void EmitMvpModelThrowsTheSelectionExceptionBeforeProducingFiles()
        {
            string spec = Spec("{ \"oneOf\": [ { \"type\": \"string\" }, { \"type\": \"integer\" } ] }");
            var service = new GenerationService();
            OpenApiGenerationModel model = service.BuildMvpModelFromOpenApiNode(
                TestBundleFactory.ReadRoot(spec), new GeneratorOptions("Api", "Generated.Gate"));

            Assert.NotEmpty(SystemTextJsonFeatureGate.FindUnsupported(model));
            var failure = Assert.Throws<LibrarySelectionNotSupportedException>(() => service.EmitMvpModel(
                model, HttpTransportKind.HttpClient, JsonSerializerKind.SystemTextJson));
            Assert.Contains("JsonSerializer=SystemTextJson", failure.Message);
            Assert.NotEmpty(service.EmitMvpModel(
                model, HttpTransportKind.HttpClient, JsonSerializerKind.NewtonsoftJson));
        }

        [Fact]
        public void RejectionDoesNotAffectTheOtherDefinitionsOfTheSameCompilation()
        {
            // One definition per compilation in the harness: the unsupported spec must not leak generated
            // sources, while the same spec with the default serializer is generated.
            string spec = Spec("{ \"oneOf\": [ { \"type\": \"string\" }, { \"type\": \"integer\" } ] }");

            Phase4GeneratorExecution rejected = Phase4GeneratorTestHarness.GenerateAndCompile(
                spec, options: new Phase4CompilationOptions { JsonSerializer = "SystemTextJson" });
            Phase4GeneratorExecution accepted = Phase4GeneratorTestHarness.GenerateAndCompile(spec);

            Assert.Empty(rejected.RunResult.GeneratedTrees);
            Assert.NotEmpty(accepted.RunResult.GeneratedTrees);
        }
    }
}
