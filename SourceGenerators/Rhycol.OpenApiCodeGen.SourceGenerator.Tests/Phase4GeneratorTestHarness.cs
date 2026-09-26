using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

using Newtonsoft.Json;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    internal static class Phase4GeneratorTestHarness
    {
        internal const string GeneratedNamespace = "Generated.Phase4";
        internal const string ApiName = "Phase4Api";

        private const string AttributeContract = @"
namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    public enum OpenApiDocumentFormat
    {
        Json = 0,
        Yaml = 1
    }

    [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class OpenApiClientDefinitionAttribute : System.Attribute
    {
        public OpenApiClientDefinitionAttribute(
            string specId,
            string apiName,
            string generatedNamespace,
            OpenApiDocumentFormat documentFormat)
        {
        }
    }
}
";

        internal static Phase4GeneratorExecution GenerateAndCompile(
            string openApiJson,
            string apiName = ApiName,
            string generatedNamespace = GeneratedNamespace,
            string specId = TestBundleFactory.SpecId,
            string? bundle = null)
        {
            CSharpCompilation compilation = CreateCompilation(apiName, generatedNamespace, specId);
            GeneratorDriver driver = CreateDriver(
                compilation,
                ImmutableArray.Create<AdditionalText>(CreateBundleText(
                    specId,
                    bundle ?? TestBundleFactory.Create(openApiJson, specId: specId))));
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation outputCompilation,
                out ImmutableArray<Diagnostic> generatorDiagnostics);
            GeneratorDriverRunResult runResult = driver.GetRunResult();
            return new Phase4GeneratorExecution(
                driver,
                runResult,
                (CSharpCompilation)outputCompilation,
                generatorDiagnostics);
        }

        internal static CSharpCompilation CreateCompilation(
            string apiName = ApiName,
            string generatedNamespace = GeneratedNamespace,
            string specId = TestBundleFactory.SpecId,
            string? assemblyName = null)
        {
            string definition = "\nnamespace " + generatedNamespace + @"
{
    [global::Rhycol.OpenApiCodeGen.SourceGenerator.OpenApiClientDefinitionAttribute(" +
                ToLiteral(specId) + ", " +
                ToLiteral(apiName) + ", " +
                ToLiteral(generatedNamespace) + @",
        global::Rhycol.OpenApiCodeGen.SourceGenerator.OpenApiDocumentFormat.Json)]
    public partial class " + apiName + @" { }
}
";
            return CSharpCompilation.Create(
                assemblyName ?? "Phase4GeneratedTests_" + Guid.NewGuid().ToString("N"),
                new[]
                {
                    CSharpSyntaxTree.ParseText(
                        AttributeContract + definition,
                        new CSharpParseOptions(LanguageVersion.CSharp9))
                },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }

        internal static GeneratorDriver CreateDriver(
            CSharpCompilation compilation,
            ImmutableArray<AdditionalText> additionalTexts)
        {
            return CSharpGeneratorDriver.Create(
                generators: new[] { new ApiClientCodeGenerator().AsSourceGenerator() },
                additionalTexts: additionalTexts,
                parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options,
                driverOptions: new GeneratorDriverOptions(
                    disabledOutputs: IncrementalGeneratorOutputKind.None,
                    trackIncrementalGeneratorSteps: true));
        }

        internal static InMemoryAdditionalText CreateBundleText(string specId, string bundle)
        {
            return new InMemoryAdditionalText(
                specId + ApiClientCodeGenerator.AdditionalFileSuffix,
                bundle);
        }

        internal static IReadOnlyList<IncrementalStepRunReason> GetReasons(
            GeneratorDriverRunResult result,
            string trackingName)
        {
            return result.Results.Single().TrackedSteps[trackingName]
                .SelectMany(static step => step.Outputs)
                .Select(static output => output.Reason)
                .ToArray();
        }

        private static IEnumerable<MetadataReference> GetMetadataReferences()
        {
            string trustedAssemblies =
                (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
            var paths = new HashSet<string>(
                trustedAssemblies.Split(Path.PathSeparator),
                StringComparer.OrdinalIgnoreCase);
            paths.Add(typeof(JsonConvert).Assembly.Location);
            return paths.Where(static path => !string.IsNullOrWhiteSpace(path))
                .Select(static path => MetadataReference.CreateFromFile(path));
        }

        private static string ToLiteral(string value)
        {
            return SymbolDisplay.FormatLiteral(value, quote: true);
        }
    }

    internal sealed class Phase4GeneratorExecution
    {
        internal Phase4GeneratorExecution(
            GeneratorDriver driver,
            GeneratorDriverRunResult runResult,
            CSharpCompilation outputCompilation,
            ImmutableArray<Diagnostic> generatorDiagnostics)
        {
            Driver = driver;
            RunResult = runResult;
            OutputCompilation = outputCompilation;
            GeneratorDiagnostics = generatorDiagnostics;
        }

        internal GeneratorDriver Driver { get; }

        internal GeneratorDriverRunResult RunResult { get; }

        internal CSharpCompilation OutputCompilation { get; }

        internal ImmutableArray<Diagnostic> GeneratorDiagnostics { get; }

        internal string GeneratedSource => string.Join(
            "\n",
            RunResult.Results.Single().GeneratedSources
                .OrderBy(static source => source.HintName, StringComparer.Ordinal)
                .Select(static source => source.SourceText.ToString()));

        internal IReadOnlyList<Diagnostic> CompilationErrors => OutputCompilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        internal Assembly EmitAssembly()
        {
            using var stream = new MemoryStream();
            EmitResult result = OutputCompilation.Emit(stream);
            if (!result.Success)
            {
                throw new InvalidOperationException(string.Join(
                    Environment.NewLine,
                    result.Diagnostics.Select(static diagnostic => diagnostic.ToString())));
            }

            stream.Position = 0;
            return AssemblyLoadContext.Default.LoadFromStream(stream);
        }
    }
}
