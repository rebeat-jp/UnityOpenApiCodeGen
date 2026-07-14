using System;
using System.IO;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ReBeat.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// インクリメンタル Source Generator の入口。
    /// Entry point for the incremental source generator.
    /// </summary>
    public class ApiClientCodeGenerator : IIncrementalGenerator
    {
        /// <summary>
        /// 解析失敗時の診断情報。
        /// Diagnostic descriptor for parse failures.
        /// </summary>
        private static readonly DiagnosticDescriptor OpenApiParseFailed = new(
            "OACG001",
            "OpenAPI parse failed",
            "Failed to parse OpenAPI document '{0}': {1}",
            "OpenApiCodeGen",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        /// <summary>
        /// ジェネレータの初期化。
        /// Initializes the generator.
        /// </summary>
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var optionsProvider = context.AnalyzerConfigOptionsProvider.Select((provider, _) =>
            {
                var options = new ApiClientGenerateOption();
                if (provider.GlobalOptions.TryGetValue("build_property.OpenApiApiName", out var apiName) &&
                    !string.IsNullOrWhiteSpace(apiName))
                {
                    options.ApiName = apiName;
                }

                if (provider.GlobalOptions.TryGetValue("build_property.OpenApiNamespace", out var ns) &&
                    !string.IsNullOrWhiteSpace(ns))
                {
                    options.Namespace = ns;
                }

                return options;
            });

            // AdditionalFiles から OpenAPI の入力を収集する。
            var openApiFiles = context.AdditionalTextsProvider
                .Where(text => IsOpenApiSpec(text.Path))
                .Select((text, token) => (Path: text.Path, Content: text.GetText(token)?.ToString()))
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Content));

            var combined = openApiFiles.Combine(optionsProvider);
            context.RegisterSourceOutput(combined, (spc, entry) =>
            {
                var (spec, options) = entry;
                var content = spec.Content ?? string.Empty;
                try
                {
                    // コア生成ロジックでコードを生成する。
                    var generator = new GenerationService();
                    var files = generator.GenerateFromOpenApiJson(content, options);
                    foreach (var file in files)
                    {
                        spc.AddSource(file.FileName, SourceText.From(file.Content, Encoding.UTF8));
                    }
                }
                catch (Exception ex)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(OpenApiParseFailed, Location.None, spec.Path, ex.Message));
                }
            });
        }

        /// <summary>
        /// OpenAPI 入力として扱うファイル名かを判定する。
        /// Checks if the file name should be treated as an OpenAPI spec.
        /// </summary>
        private static bool IsOpenApiSpec(string path)
        {
            var fileName = Path.GetFileName(path);
            return fileName is not null && (fileName.EndsWith("openapi.json", StringComparison.OrdinalIgnoreCase) ||
                   fileName.EndsWith(".openapi.json", StringComparison.OrdinalIgnoreCase));
        }
    }
}
