using System;
using System.Collections.Generic;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// インクリメンタル Source Generator の入口。
    /// Entry point for the incremental source generator.
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public sealed class ApiClientCodeGenerator : IIncrementalGenerator
    {
        internal const string AdditionalFileSuffix = ".Rhycol.OpenApiCodeGen.SourceGenerator.additionalfile";

        private static readonly DiagnosticDescriptor MalformedBundle = new(
            "OACG001",
            "Malformed normalized spec bundle",
            "Normalized spec bundle '{0}' is malformed: {1}",
            "OpenApiCodeGen",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor UnsupportedBundleVersion = new(
            "OACG002",
            "Unsupported normalized spec bundle version",
            "Normalized spec bundle '{0}' uses unsupported format version '{1}'.",
            "OpenApiCodeGen",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor GenerationFailed = new(
            "OACG003",
            "OpenAPI generation failed",
            "OpenAPI generation failed for '{0}' at logical path '{1}': {2}",
            "OpenApiCodeGen",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor MultipleBundles = new(
            "OACG004",
            "Multiple normalized spec bundles",
            "Only one normalized spec bundle is supported per target assembly. Found: {0}",
            "OpenApiCodeGen",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>
        /// ジェネレータの初期化。
        /// Initializes the generator.
        /// </summary>
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            IncrementalValueProvider<GeneratorOptions> optionsProvider =
                context.AnalyzerConfigOptionsProvider.Select((provider, _) =>
                {
                    string apiName = "Api";
                    if (provider.GlobalOptions.TryGetValue("build_property.OpenApiApiName", out string? configuredApiName) &&
                        !string.IsNullOrWhiteSpace(configuredApiName))
                    {
                        apiName = configuredApiName;
                    }

                    string generatedNamespace = "Rhycol.OpenApiCodeGen.Generated";
                    if (provider.GlobalOptions.TryGetValue("build_property.OpenApiNamespace", out string? configuredNamespace) &&
                        !string.IsNullOrWhiteSpace(configuredNamespace))
                    {
                        generatedNamespace = configuredNamespace;
                    }

                    return new GeneratorOptions(apiName, generatedNamespace);
                })
                .WithTrackingName("OpenApiGeneratorOptions");

            IncrementalValueProvider<System.Collections.Immutable.ImmutableArray<NormalizedSpecInput>> bundles =
                context.AdditionalTextsProvider
                    .Where(text => text.Path.EndsWith(AdditionalFileSuffix, StringComparison.Ordinal))
                    .Select((text, cancellationToken) => new NormalizedSpecInput(
                        text.Path,
                        text.GetText(cancellationToken)?.ToString() ?? string.Empty))
                    .WithTrackingName("OpenApiReadNormalizedBundle")
                    .Collect()
                    .WithTrackingName("OpenApiCollectNormalizedBundles");

            context.RegisterSourceOutput(bundles.Combine(optionsProvider), (productionContext, input) =>
            {
                System.Collections.Immutable.ImmutableArray<NormalizedSpecInput> candidates = input.Left;
                if (candidates.Length == 0)
                {
                    return;
                }

                if (candidates.Length > 1)
                {
                    productionContext.ReportDiagnostic(Diagnostic.Create(
                        MultipleBundles,
                        Location.None,
                        JoinPaths(candidates)));
                    return;
                }

                Generate(productionContext, candidates[0], input.Right);
            });
        }

        private static void Generate(
            SourceProductionContext context,
            NormalizedSpecInput input,
            GeneratorOptions options)
        {
            NormalizedSpecBundle bundle;
            try
            {
                bundle = NormalizedSpecBundleReader.Read(input.Content);
            }
            catch (UnsupportedNormalizedSpecVersionException exception)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UnsupportedBundleVersion,
                    CreateExternalLocation(input.Path, exception.Line, exception.Column),
                    input.Path,
                    exception.Version));
                return;
            }
            catch (NormalizedSpecBundleFormatException exception)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    MalformedBundle,
                    CreateExternalLocation(input.Path, exception.Line, exception.Column),
                    input.Path,
                    exception.Message));
                return;
            }
            catch (Exception exception)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    MalformedBundle,
                    CreateExternalLocation(input.Path, 1, 1),
                    input.Path,
                    exception.Message));
                return;
            }

            try
            {
                var service = new GenerationService();
                IReadOnlyList<GeneratedFile> files = service.GenerateFromApiDocument(
                    bundle.Root,
                    options.ToDomainOptions());
                foreach (GeneratedFile file in files)
                {
                    context.AddSource(file.FileName, SourceText.From(file.Content, Encoding.UTF8));
                }
            }
            catch (Exception exception)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    GenerationFailed,
                    CreateExternalLocation(bundle.SourcePath, bundle.Root.Line, bundle.Root.Column),
                    bundle.SourcePath,
                    bundle.Root.LogicalPath,
                    exception.Message));
            }
        }

        private static Location CreateExternalLocation(string path, int line, int column)
        {
            var position = new LinePosition(Math.Max(0, line - 1), Math.Max(0, column - 1));
            return Location.Create(
                path,
                new TextSpan(0, 0),
                new LinePositionSpan(position, position));
        }

        private static string JoinPaths(
            System.Collections.Immutable.ImmutableArray<NormalizedSpecInput> inputs)
        {
            var builder = new StringBuilder();
            for (int index = 0; index < inputs.Length; index++)
            {
                if (index > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(inputs[index].Path);
            }

            return builder.ToString();
        }
    }
}
