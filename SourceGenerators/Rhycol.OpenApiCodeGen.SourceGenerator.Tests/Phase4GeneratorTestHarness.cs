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

        internal const string AttributeContract = @"
namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    public enum OpenApiDocumentFormat
    {
        Json = 0,
        Yaml = 1
    }

    public enum OpenApiHttpTransport
    {
        HttpClient = 0,
        UnityWebRequest = 1
    }

    public enum OpenApiJsonSerializer
    {
        NewtonsoftJson = 0,
        SystemTextJson = 1
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

        public OpenApiHttpTransport HttpTransport { get; set; }

        public OpenApiJsonSerializer JsonSerializer { get; set; }
    }
}
";

        internal static Phase4GeneratorExecution GenerateAndCompile(
            string openApiJson,
            string apiName = ApiName,
            string generatedNamespace = GeneratedNamespace,
            string specId = TestBundleFactory.SpecId,
            string? bundle = null,
            ApiClientCodeGenerator? generator = null,
            Phase4CompilationOptions? options = null)
        {
            CSharpCompilation compilation = CreateCompilation(
                apiName,
                generatedNamespace,
                specId,
                options: options);
            GeneratorDriver driver = CreateDriver(
                compilation,
                ImmutableArray.Create<AdditionalText>(CreateBundleText(
                    specId,
                    bundle ?? TestBundleFactory.Create(openApiJson, specId: specId))),
                generator);
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
            string? assemblyName = null,
            Phase4CompilationOptions? options = null)
        {
            options ??= new Phase4CompilationOptions();
            string definition = "\nnamespace " + generatedNamespace + @"
{
    [global::Rhycol.OpenApiCodeGen.SourceGenerator.OpenApiClientDefinitionAttribute(" +
                ToLiteral(specId) + ", " +
                ToLiteral(apiName) + ", " +
                ToLiteral(generatedNamespace) + @",
        global::Rhycol.OpenApiCodeGen.SourceGenerator.OpenApiDocumentFormat.Json" +
                options.NamedArguments + @")]
    public partial class " + apiName + @" { }
}
";
            return CSharpCompilation.Create(
                assemblyName ?? "Phase4GeneratedTests_" + Guid.NewGuid().ToString("N"),
                new[]
                {
                    CSharpSyntaxTree.ParseText(
                        AttributeContract + definition,
                        new CSharpParseOptions(
                            LanguageVersion.CSharp9,
                            preprocessorSymbols: options.PreprocessorSymbols))
                },
                GetMetadataReferences(options),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }

        internal static GeneratorDriver CreateDriver(
            CSharpCompilation compilation,
            ImmutableArray<AdditionalText> additionalTexts,
            ApiClientCodeGenerator? generator = null)
        {
            return CSharpGeneratorDriver.Create(
                generators: new[] { (generator ?? new ApiClientCodeGenerator()).AsSourceGenerator() },
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

        private static IEnumerable<MetadataReference> GetMetadataReferences(
            Phase4CompilationOptions options)
        {
            string trustedAssemblies =
                (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
            var paths = new HashSet<string>(
                options.ReferenceDirectory is null
                    ? trustedAssemblies.Split(Path.PathSeparator)
                    : Directory.GetFiles(options.ReferenceDirectory, "*.dll"),
                StringComparer.OrdinalIgnoreCase);
            if (options.ReferencePaths is null)
            {
                paths.Add(typeof(JsonConvert).Assembly.Location);
            }
            else
            {
                paths = new HashSet<string>(options.ReferencePaths, StringComparer.OrdinalIgnoreCase);
            }

            var excludedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!options.Newtonsoft || options.NewtonsoftStubVersion is not null)
            {
                excludedFileNames.Add("Newtonsoft.Json.dll");
            }

            if (!options.SystemTextJson || options.SystemTextJsonStubVersion is not null)
            {
                excludedFileNames.Add("System.Text.Json.dll");
            }

            if (!options.HttpClient)
            {
                excludedFileNames.Add("System.Net.Http.dll");
            }

            var references = new List<MetadataReference>(
                paths.Where(path => !string.IsNullOrWhiteSpace(path) &&
                                    !excludedFileNames.Contains(Path.GetFileName(path)))
                    .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path)));
            if (options.NewtonsoftStubVersion is not null)
            {
                references.Add(CreateStubReference(
                    "Newtonsoft.Json",
                    options.NewtonsoftStubVersion,
                    "namespace Newtonsoft.Json { public static class JsonConvert { } }"));
            }

            if (options.DuplicateNewtonsoftStubVersion is not null)
            {
                references.Add(CreateStubReference(
                    "Newtonsoft.Json.Duplicate",
                    options.DuplicateNewtonsoftStubVersion,
                    "namespace Newtonsoft.Json { public static class JsonConvert { } }"));
            }

            if (options.SystemTextJsonStubVersion is not null)
            {
                references.Add(CreateStubReference(
                    "System.Text.Json",
                    options.SystemTextJsonStubVersion,
                    "namespace System.Text.Json { public static class JsonSerializer { } }"));
            }

            if (options.UnityWebRequestStub)
            {
                references.Add(UnityEngineStub.CreateReference(options.ReferencePaths));
            }

            return references;
        }

        // Compiles a tiny assembly with the given name and version. It stands in for a library
        // that is not downloadable in tests and lets a test control the referenced version.
        private static MetadataReference CreateStubReference(
            string assemblyName,
            Version version,
            string source)
        {
            string versionedSource =
                "[assembly: System.Reflection.AssemblyVersion(\"" + version + "\")]\n" + source;
            CSharpCompilation stub = CSharpCompilation.Create(
                assemblyName,
                new[] { CSharpSyntaxTree.ParseText(versionedSource) },
                GetMetadataReferences(new Phase4CompilationOptions()),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var stream = new MemoryStream();
            EmitResult result = stub.Emit(stream);
            if (!result.Success)
            {
                throw new InvalidOperationException(string.Join(
                    Environment.NewLine,
                    result.Diagnostics.Select(static diagnostic => diagnostic.ToString())));
            }

            return MetadataReference.CreateFromImage(stream.ToArray());
        }

        private static string ToLiteral(string value)
        {
            return SymbolDisplay.FormatLiteral(value, quote: true);
        }
    }

    /// <summary>
    /// Controls the reference set, preprocessor symbols and named attribute arguments of a test
    /// compilation. Defaults reproduce the historical harness: the runtime BCL plus Newtonsoft.Json.
    /// </summary>
    internal sealed class Phase4CompilationOptions
    {
        internal bool Newtonsoft { get; init; } = true;

        internal bool SystemTextJson { get; init; } = true;

        internal bool HttpClient { get; init; } = true;

        /// <summary>
        /// When set, the framework references come from this directory (for example the reference
        /// assemblies of .NET 8) instead of the runtime that executes the tests.
        /// </summary>
        internal string? ReferenceDirectory { get; init; }

        /// <summary>When set, these assemblies are the complete reference set (nothing from the running runtime).</summary>
        internal IReadOnlyList<string>? ReferencePaths { get; init; }

        internal bool UnityWebRequestStub { get; init; }

        /// <summary>When set, replaces the real Newtonsoft.Json reference with a stub of this version.</summary>
        internal Version? NewtonsoftStubVersion { get; init; }

        /// <summary>When set, replaces the real System.Text.Json reference with a stub of this version.</summary>
        internal Version? SystemTextJsonStubVersion { get; init; }

        /// <summary>When set, adds a second assembly (different name) that also defines Newtonsoft.Json.JsonConvert.</summary>
        internal Version? DuplicateNewtonsoftStubVersion { get; init; }

        internal IReadOnlyList<string> PreprocessorSymbols { get; init; } = Array.Empty<string>();

        /// <summary>Enum member name or integer literal for the HttpTransport named argument.</summary>
        internal string? HttpTransport { get; init; }

        /// <summary>Enum member name or integer literal for the JsonSerializer named argument.</summary>
        internal string? JsonSerializer { get; init; }

        internal string NamedArguments
        {
            get
            {
                string text = string.Empty;
                if (HttpTransport is not null)
                {
                    text += ", HttpTransport = " + Qualify(HttpTransport, "OpenApiHttpTransport");
                }

                if (JsonSerializer is not null)
                {
                    text += ", JsonSerializer = " + Qualify(JsonSerializer, "OpenApiJsonSerializer");
                }

                return text;
            }
        }

        private static string Qualify(string expression, string enumName)
        {
            string prefix = "global::Rhycol.OpenApiCodeGen.SourceGenerator." + enumName;
            return char.IsDigit(expression[0]) || expression[0] == '-'
                ? "(" + prefix + ")" + expression
                : prefix + "." + expression;
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
