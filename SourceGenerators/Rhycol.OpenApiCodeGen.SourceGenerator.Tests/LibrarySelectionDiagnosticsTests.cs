using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    /// <summary>
    /// OACG010-OACG014 とライブラリ検出の incremental キャッシュを検証する。
    /// Verifies OACG010-OACG014 and the incremental caching of library detection.
    /// </summary>
    public sealed class LibrarySelectionDiagnosticsTests
    {
        private const string EmptyDocument = @"{
  ""openapi"": ""3.1.0"",
  ""info"": { ""title"": ""Libraries"", ""version"": ""1"" },
  ""paths"": {}
}";

        [Fact]
        public void DefaultSelectionWithDefaultReferencesReportsNothingAndGenerates()
        {
            Phase4GeneratorExecution execution = Run(new Phase4CompilationOptions());

            Assert.Empty(SelectionDiagnosticIds(execution));
            Assert.NotEmpty(execution.RunResult.GeneratedTrees);
            Assert.Empty(execution.CompilationErrors);
        }

        [Fact]
        public void ExplicitDefaultNamedArgumentsProduceTheSameOutputAsOmittingThem()
        {
            Phase4GeneratorExecution implicitDefaults = Run(new Phase4CompilationOptions());
            Phase4GeneratorExecution explicitDefaults = Run(new Phase4CompilationOptions
            {
                HttpTransport = "HttpClient",
                JsonSerializer = "NewtonsoftJson"
            });

            Assert.Empty(SelectionDiagnosticIds(explicitDefaults));
            Assert.Equal(implicitDefaults.GeneratedSource, explicitDefaults.GeneratedSource);
        }

        [Theory]
        [InlineData("5", null)]
        [InlineData("-1", null)]
        [InlineData(null, "2")]
        [InlineData("5", "7")]
        public void UnknownNamedArgumentValuesReportOACG010WithoutGenerating(
            string? httpTransport,
            string? jsonSerializer)
        {
            Phase4GeneratorExecution execution = Run(new Phase4CompilationOptions
            {
                HttpTransport = httpTransport,
                JsonSerializer = jsonSerializer
            });

            Assert.Equal(new[] { "OACG010" }, SelectionDiagnosticIds(execution));
            Assert.Empty(execution.RunResult.GeneratedTrees);
            Diagnostic diagnostic = execution.RunResult.Diagnostics.Single(
                static item => item.Id == "OACG010");
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Contains("is unknown", diagnostic.GetMessage());
        }

        [Theory]
        [InlineData("UnityWebRequest", "NewtonsoftJson")]
        [InlineData("HttpClient", "SystemTextJson")]
        [InlineData("UnityWebRequest", "SystemTextJson")]
        public void UnimplementedCombinationsReportOACG010WhenLibrariesArePresent(
            string httpTransport,
            string jsonSerializer)
        {
            Phase4GeneratorExecution execution = Run(new Phase4CompilationOptions
            {
                UnityWebRequestStub = true,
                HttpTransport = httpTransport,
                JsonSerializer = jsonSerializer
            });

            Assert.Equal(new[] { "OACG010" }, SelectionDiagnosticIds(execution));
            Assert.Empty(execution.RunResult.GeneratedTrees);
            Assert.Contains(
                "is not implemented",
                execution.RunResult.Diagnostics.Single(static item => item.Id == "OACG010").GetMessage());
        }

        [Fact]
        public void MissingNewtonsoftJsonReportsOACG011WithoutGenerating()
        {
            Phase4GeneratorExecution execution = Run(new Phase4CompilationOptions { Newtonsoft = false });

            Assert.Equal(new[] { "OACG011" }, SelectionDiagnosticIds(execution));
            Assert.Empty(execution.RunResult.GeneratedTrees);
            Diagnostic diagnostic = execution.RunResult.Diagnostics.Single(
                static item => item.Id == "OACG011");
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Contains("Newtonsoft.Json", diagnostic.GetMessage());
        }

        [Fact]
        public void MissingSystemTextJsonIsOnlyReportedWhenItIsSelected()
        {
            Phase4GeneratorExecution defaultSelection = Run(
                new Phase4CompilationOptions { SystemTextJson = false });
            Assert.Empty(SelectionDiagnosticIds(defaultSelection));
            Assert.NotEmpty(defaultSelection.RunResult.GeneratedTrees);

            Phase4GeneratorExecution selected = Run(new Phase4CompilationOptions
            {
                SystemTextJson = false,
                JsonSerializer = "SystemTextJson"
            });
            Assert.Equal(new[] { "OACG010", "OACG011" }, SelectionDiagnosticIds(selected));
            Assert.Empty(selected.RunResult.GeneratedTrees);
            Assert.Contains(
                "System.Text.Json",
                selected.RunResult.Diagnostics.Single(static item => item.Id == "OACG011").GetMessage());
        }

        [Fact]
        public void MissingHttpClientReportsOACG012WithoutGenerating()
        {
            Phase4GeneratorExecution execution = Run(new Phase4CompilationOptions { HttpClient = false });

            Assert.Equal(new[] { "OACG012" }, SelectionDiagnosticIds(execution));
            Assert.Empty(execution.RunResult.GeneratedTrees);
            Assert.Contains(
                "System.Net.Http.HttpClient",
                execution.RunResult.Diagnostics.Single(static item => item.Id == "OACG012").GetMessage());
        }

        [Fact]
        public void MissingUnityWebRequestReportsOACG012AlongsideTheUnimplementedCombination()
        {
            Phase4GeneratorExecution execution = Run(new Phase4CompilationOptions
            {
                HttpTransport = "UnityWebRequest"
            });

            Assert.Equal(new[] { "OACG010", "OACG012" }, SelectionDiagnosticIds(execution));
            Assert.Empty(execution.RunResult.GeneratedTrees);
            Assert.Contains(
                "UnityEngine.Networking.UnityWebRequest",
                execution.RunResult.Diagnostics.Single(static item => item.Id == "OACG012").GetMessage());
        }

        [Fact]
        public void NewtonsoftJsonBelowTheMinimumVersionReportsOACG013()
        {
            Phase4GeneratorExecution tooLow = Run(new Phase4CompilationOptions
            {
                NewtonsoftStubVersion = new Version(12, 0, 3, 0)
            });
            Assert.Equal(new[] { "OACG013" }, SelectionDiagnosticIds(tooLow));
            Assert.Empty(tooLow.RunResult.GeneratedTrees);
            Assert.Contains(
                "12.0.3.0",
                tooLow.RunResult.Diagnostics.Single(static item => item.Id == "OACG013").GetMessage());

            Phase4GeneratorExecution minimum = Run(new Phase4CompilationOptions
            {
                NewtonsoftStubVersion = LibraryAvailability.MinimumNewtonsoftJsonVersion
            });
            Assert.Empty(SelectionDiagnosticIds(minimum));
            Assert.NotEmpty(minimum.RunResult.GeneratedTrees);
        }

        [Fact]
        public void SystemTextJsonBelowTheMinimumVersionReportsOACG013WhenSelected()
        {
            Phase4GeneratorExecution tooLow = Run(new Phase4CompilationOptions
            {
                SystemTextJsonStubVersion = new Version(7, 0, 0, 0),
                JsonSerializer = "SystemTextJson"
            });
            Assert.Equal(new[] { "OACG010", "OACG013" }, SelectionDiagnosticIds(tooLow));

            Phase4GeneratorExecution minimum = Run(new Phase4CompilationOptions
            {
                SystemTextJsonStubVersion = LibraryAvailability.MinimumSystemTextJsonVersion,
                JsonSerializer = "SystemTextJson"
            });
            Assert.Equal(new[] { "OACG010" }, SelectionDiagnosticIds(minimum));

            Phase4GeneratorExecution notSelected = Run(new Phase4CompilationOptions
            {
                SystemTextJsonStubVersion = new Version(7, 0, 0, 0)
            });
            Assert.Empty(SelectionDiagnosticIds(notSelected));
        }

        [Fact]
        public void HttpClientOnWebGlWarnsButStillGenerates()
        {
            Phase4GeneratorExecution execution = Run(new Phase4CompilationOptions
            {
                PreprocessorSymbols = new[] { "UNITY_WEBGL" }
            });

            Assert.Equal(new[] { "OACG014" }, SelectionDiagnosticIds(execution));
            Assert.Equal(
                DiagnosticSeverity.Warning,
                execution.RunResult.Diagnostics.Single(static item => item.Id == "OACG014").Severity);
            Assert.NotEmpty(execution.RunResult.GeneratedTrees);
        }

        [Fact]
        public void UnityWebRequestOnWebGlDoesNotWarn()
        {
            Phase4GeneratorExecution execution = Run(new Phase4CompilationOptions
            {
                PreprocessorSymbols = new[] { "UNITY_WEBGL" },
                UnityWebRequestStub = true,
                HttpTransport = "UnityWebRequest"
            });

            Assert.Equal(new[] { "OACG010" }, SelectionDiagnosticIds(execution));
        }

        [Fact]
        public void DiagnosticsPointAtTheAttributeApplication()
        {
            Phase4GeneratorExecution execution = Run(new Phase4CompilationOptions { Newtonsoft = false });

            Diagnostic diagnostic = execution.RunResult.Diagnostics.Single(
                static item => item.Id == "OACG011");
            Assert.Equal(LocationKind.SourceFile, diagnostic.Location.Kind);
        }

        [Fact]
        public void EvaluateChecksLibrariesForTheWholeCombination()
        {
            var nothing = new LibraryAvailability(null, null, false, false, isWebGl: false);
            var tooOld = new LibraryAvailability(
                new Version(12, 0, 0, 0),
                new Version(6, 0, 0, 0),
                hasUnityWebRequest: true,
                hasHttpClient: true,
                isWebGl: true);

            Assert.Equal(
                new[]
                {
                    LibrarySelectionIssueKind.JsonLibraryMissing,
                    LibrarySelectionIssueKind.HttpImplementationMissing
                },
                Kinds(HttpTransportKind.HttpClient, JsonSerializerKind.NewtonsoftJson, nothing));
            Assert.Equal(
                new[]
                {
                    LibrarySelectionIssueKind.NotImplemented,
                    LibrarySelectionIssueKind.JsonLibraryMissing,
                    LibrarySelectionIssueKind.HttpImplementationMissing
                },
                Kinds(HttpTransportKind.UnityWebRequest, JsonSerializerKind.SystemTextJson, nothing));
            Assert.Equal(
                new[]
                {
                    LibrarySelectionIssueKind.VersionTooLow,
                    LibrarySelectionIssueKind.WebGlHttpClient
                },
                Kinds(HttpTransportKind.HttpClient, JsonSerializerKind.NewtonsoftJson, tooOld));
            Assert.Equal(
                new[]
                {
                    LibrarySelectionIssueKind.NotImplemented,
                    LibrarySelectionIssueKind.VersionTooLow
                },
                Kinds(HttpTransportKind.UnityWebRequest, JsonSerializerKind.SystemTextJson, tooOld));
        }

        [Fact]
        public void UnrelatedEditsKeepTheLibraryAvailabilityAndWorkItemsCached()
        {
            CSharpCompilation first = Phase4GeneratorTestHarness.CreateCompilation();
            GeneratorDriver driver = Phase4GeneratorTestHarness.CreateDriver(
                first,
                ImmutableArray.Create<AdditionalText>(CreateBundle()));
            driver = driver.RunGenerators(first);
            GeneratorDriverRunResult initial = driver.GetRunResult();
            Assert.NotEmpty(initial.GeneratedTrees);

            CSharpCompilation edited = first.AddSyntaxTrees(
                CSharpSyntaxTree.ParseText(
                    "namespace Unrelated { public sealed class Edit { public int Value => 1; } }",
                    (CSharpParseOptions)first.SyntaxTrees.First().Options));
            driver = driver.RunGenerators(edited);
            GeneratorDriverRunResult afterEdit = driver.GetRunResult();

            Assert.All(
                Phase4GeneratorTestHarness.GetReasons(afterEdit, "OpenApiLibraryAvailability"),
                reason => Assert.Equal(IncrementalStepRunReason.Unchanged, reason));
            Assert.All(
                Phase4GeneratorTestHarness.GetReasons(afterEdit, "OpenApiMatchDefinitionToBundle"),
                reason => Assert.Contains(
                    reason,
                    new[] { IncrementalStepRunReason.Cached, IncrementalStepRunReason.Unchanged }));
            Assert.Equal(
                initial.GeneratedTrees.Select(static tree => tree.ToString()),
                afterEdit.GeneratedTrees.Select(static tree => tree.ToString()));
        }

        [Fact]
        public void ChangingTheReferencedLibrariesInvalidatesTheAvailability()
        {
            CSharpCompilation first = Phase4GeneratorTestHarness.CreateCompilation();
            GeneratorDriver driver = Phase4GeneratorTestHarness.CreateDriver(
                first,
                ImmutableArray.Create<AdditionalText>(CreateBundle()));
            driver = driver.RunGenerators(first);

            MetadataReference newtonsoft = first.References.Single(
                static reference => reference.Display?.EndsWith(
                    "Newtonsoft.Json.dll",
                    StringComparison.OrdinalIgnoreCase) == true);
            CSharpCompilation withoutNewtonsoft = first.RemoveReferences(newtonsoft);
            driver = driver.RunGenerators(withoutNewtonsoft);
            GeneratorDriverRunResult result = driver.GetRunResult();

            Assert.Contains(
                IncrementalStepRunReason.Modified,
                Phase4GeneratorTestHarness.GetReasons(result, "OpenApiLibraryAvailability"));
            Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Id == "OACG011");
            Assert.Empty(result.GeneratedTrees);
        }

        [Fact]
        public void DuplicateReferencesToTheSameTypeUseTheHighestVersion()
        {
            Assert.Empty(SelectionDiagnosticIds(Run(new Phase4CompilationOptions
            {
                NewtonsoftStubVersion = new Version(12, 0, 0, 0),
                DuplicateNewtonsoftStubVersion = new Version(13, 0, 0, 0)
            })));
            Assert.Empty(SelectionDiagnosticIds(Run(new Phase4CompilationOptions
            {
                NewtonsoftStubVersion = new Version(13, 0, 0, 0),
                DuplicateNewtonsoftStubVersion = new Version(12, 0, 0, 0)
            })));
            Assert.Equal(
                new[] { "OACG013" },
                SelectionDiagnosticIds(Run(new Phase4CompilationOptions
                {
                    NewtonsoftStubVersion = new Version(12, 0, 0, 0),
                    DuplicateNewtonsoftStubVersion = new Version(12, 5, 0, 0)
                })));
        }

        [Fact]
        public void RuntimeEnumsGeneratorEnumsAndTestContractsStayInSync()
        {
            Assert.Equal(
                EnumValues<OpenApiHttpTransport>(),
                EnumValues<HttpTransportKind>());
            Assert.Equal(
                EnumValues<OpenApiJsonSerializer>(),
                EnumValues<JsonSerializerKind>());

            foreach (string contract in new[]
                     {
                         Phase4GeneratorTestHarness.AttributeContract,
                         SourceGeneratorTests.AttributeContract
                     })
            {
                CSharpCompilation compilation = CSharpCompilation.Create(
                    "ContractCheck",
                    new[] { CSharpSyntaxTree.ParseText(contract) },
                    new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                Assert.Empty(compilation.GetDiagnostics()
                    .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

                Assert.Equal(EnumValues<OpenApiDocumentFormat>(), ContractEnum(compilation, "OpenApiDocumentFormat"));
                Assert.Equal(EnumValues<OpenApiHttpTransport>(), ContractEnum(compilation, "OpenApiHttpTransport"));
                Assert.Equal(EnumValues<OpenApiJsonSerializer>(), ContractEnum(compilation, "OpenApiJsonSerializer"));

                INamedTypeSymbol attribute = compilation.GetTypeByMetadataName(
                    ApiClientCodeGenerator.AttributeMetadataName)!;
                string[] optionProperties = { "HttpTransport", "JsonSerializer" };
                Assert.Equal(
                    typeof(OpenApiClientDefinitionAttribute).GetProperties()
                        .Where(property => optionProperties.Contains(property.Name))
                        .Select(property => property.Name + ":" + property.PropertyType.Name + ":" + property.CanWrite)
                        .OrderBy(value => value, StringComparer.Ordinal),
                    attribute.GetMembers().OfType<IPropertySymbol>()
                        .Where(property => optionProperties.Contains(property.Name))
                        .Select(property => property.Name + ":" + property.Type.Name + ":" + (property.SetMethod != null))
                        .OrderBy(value => value, StringComparer.Ordinal));
            }
        }

        [Fact]
        public void EmitMvpModelRejectsUnimplementedCombinations()
        {
            var service = new GenerationService();
            OpenApiGenerationModel model = service.BuildMvpModelFromOpenApiNode(
                TestBundleFactory.ReadRoot(EmptyDocument),
                new GeneratorOptions("Api", "Generated.Switch"));

            Assert.NotEmpty(service.EmitMvpModel(model));
            Assert.NotEmpty(service.EmitMvpModel(
                model,
                HttpTransportKind.HttpClient,
                JsonSerializerKind.NewtonsoftJson));
            Assert.Throws<NotSupportedException>(() => service.EmitMvpModel(
                model,
                HttpTransportKind.HttpClient,
                JsonSerializerKind.SystemTextJson));
            Assert.Throws<NotSupportedException>(() => service.EmitMvpModel(
                model,
                HttpTransportKind.UnityWebRequest,
                JsonSerializerKind.NewtonsoftJson));
        }

        [Fact]
        public void EachDefinitionInOneCompilationReportsItsOwnDiagnostics()
        {
            const string SecondSpecId = "fedcba9876543210fedcba9876543210";
            const string ThirdSpecId = "00112233445566778899aabbccddeeff";
            string Definition(string specId, string name, string extra) =>
                "[global::Rhycol.OpenApiCodeGen.SourceGenerator.OpenApiClientDefinitionAttribute(\"" +
                specId + "\", \"" + name + "\", \"Multi\", " +
                "global::Rhycol.OpenApiCodeGen.SourceGenerator.OpenApiDocumentFormat.Json" + extra + ")]\n" +
                "public partial class " + name + " { }\n";
            string source = Phase4GeneratorTestHarness.AttributeContract + "namespace Multi\n{\n" +
                Definition(TestBundleFactory.SpecId, "Plain", string.Empty) +
                Definition(SecondSpecId, "FirstUwr", ", HttpTransport = global::Rhycol.OpenApiCodeGen.SourceGenerator.OpenApiHttpTransport.UnityWebRequest") +
                Definition(ThirdSpecId, "SecondUwr", ", HttpTransport = global::Rhycol.OpenApiCodeGen.SourceGenerator.OpenApiHttpTransport.UnityWebRequest") +
                "}\n";
            CSharpCompilation compilation = CSharpCompilation.Create(
                "MultiDefinitions",
                new[] { CSharpSyntaxTree.ParseText(source) },
                Phase4GeneratorTestHarness.CreateCompilation().References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = Phase4GeneratorTestHarness.CreateDriver(
                compilation,
                ImmutableArray.Create<AdditionalText>(
                    Phase4GeneratorTestHarness.CreateBundleText(
                        TestBundleFactory.SpecId, TestBundleFactory.Create(EmptyDocument)),
                    Phase4GeneratorTestHarness.CreateBundleText(
                        SecondSpecId, TestBundleFactory.Create(EmptyDocument, specId: SecondSpecId)),
                    Phase4GeneratorTestHarness.CreateBundleText(
                        ThirdSpecId, TestBundleFactory.Create(EmptyDocument, specId: ThirdSpecId))));
            GeneratorDriverRunResult result = driver.RunGenerators(compilation).GetRunResult();

            Assert.Equal(2, result.Diagnostics.Count(static item => item.Id == "OACG010"));
            Assert.Equal(2, result.Diagnostics.Count(static item => item.Id == "OACG012"));
            Assert.Equal(
                2,
                result.Diagnostics.Where(static item => item.Id == "OACG012")
                    .Select(static item => item.Location.SourceSpan.Start).Distinct().Count());
            Assert.All(
                result.GeneratedTrees,
                static tree => Assert.Contains("Plain", tree.ToString()));
            Assert.NotEmpty(result.GeneratedTrees);
        }

        private static IReadOnlyList<string> EnumValues<TEnum>()
            where TEnum : struct, Enum
        {
            return Enum.GetValues(typeof(TEnum)).Cast<TEnum>()
                .Select(static value => value + "=" + Convert.ToInt32(value))
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray();
        }

        private static IReadOnlyList<string> ContractEnum(Compilation compilation, string name)
        {
            INamedTypeSymbol type = compilation.GetTypeByMetadataName(
                "Rhycol.OpenApiCodeGen.SourceGenerator." + name)!;
            return type.GetMembers().OfType<IFieldSymbol>()
                .Where(static field => field.HasConstantValue)
                .Select(static field => field.Name + "=" + field.ConstantValue)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray();
        }

        private static Phase4GeneratorExecution Run(Phase4CompilationOptions options)
        {
            return Phase4GeneratorTestHarness.GenerateAndCompile(EmptyDocument, options: options);
        }

        private static InMemoryAdditionalText CreateBundle()
        {
            return Phase4GeneratorTestHarness.CreateBundleText(
                TestBundleFactory.SpecId,
                TestBundleFactory.Create(EmptyDocument));
        }

        private static string[] SelectionDiagnosticIds(Phase4GeneratorExecution execution)
        {
            return execution.RunResult.Diagnostics
                .Select(static diagnostic => diagnostic.Id)
                .Where(static id => string.CompareOrdinal(id, "OACG010") >= 0 &&
                                    string.CompareOrdinal(id, "OACG014") <= 0)
                .OrderBy(static id => id, StringComparer.Ordinal)
                .ToArray();
        }

        private static LibrarySelectionIssueKind[] Kinds(
            HttpTransportKind transport,
            JsonSerializerKind serializer,
            LibraryAvailability availability)
        {
            return LibrarySelection.Evaluate(transport, serializer, availability)
                .Select(static issue => issue.Kind)
                .ToArray();
        }
    }
}
