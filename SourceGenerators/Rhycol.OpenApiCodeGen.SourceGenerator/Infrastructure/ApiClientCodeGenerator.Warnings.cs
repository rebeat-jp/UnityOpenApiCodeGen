using System;
using System.Collections.Generic;

using Microsoft.CodeAnalysis;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    public sealed partial class ApiClientCodeGenerator
    {
        // Test seam: lets tests push warnings through the real parser -> model -> diagnostic path.
        // It is null in production (the public parameterless constructor).
        private readonly Action<OpenApiSemanticParser, SpecNode>? _testWarningInjector;

        public ApiClientCodeGenerator()
        {
        }

        internal ApiClientCodeGenerator(Action<OpenApiSemanticParser, SpecNode> testWarningInjector)
        {
            _testWarningInjector = testWarningInjector
                                   ?? throw new ArgumentNullException(nameof(testWarningInjector));
        }

        /// <summary>
        /// 無視した要素の Warning を OACG108 の Diagnostic に変換する。
        /// 致命エラーで生成が失敗した場合は、収集済みの warning は報告せずエラー診断のみを報告する
        /// （この変換は生成成功後にだけ呼ばれる）。
        /// Converts ignored-element warnings into OACG108 diagnostics. When a fatal error aborts
        /// generation, already collected warnings are not reported: only the error diagnostic is
        /// (this conversion is only called after generation succeeded).
        /// </summary>
        internal static IReadOnlyList<Diagnostic> CreateIgnoredElementDiagnostics(
            IReadOnlyList<OpenApiSemanticWarning> warnings,
            string bundleSourcePath)
        {
            var diagnostics = new List<Diagnostic>(warnings.Count);
            foreach (OpenApiSemanticWarning warning in warnings)
            {
                string sourcePath = GetCollisionSourcePath(warning.Location, bundleSourcePath);
                diagnostics.Add(Diagnostic.Create(
                    IgnoredOpenApiElementDiagnostic,
                    CreateExternalLocation(sourcePath, warning.Location.Line, warning.Location.Column),
                    sourcePath,
                    warning.Location.LogicalPath,
                    warning.Message));
            }

            return diagnostics;
        }
    }
}
