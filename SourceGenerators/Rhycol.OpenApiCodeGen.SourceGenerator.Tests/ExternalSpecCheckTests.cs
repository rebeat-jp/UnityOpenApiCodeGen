using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;

using Rhycol.OpenApiCodeGen.SourceGenerator.Editor;

using Xunit;
using Xunit.Abstractions;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    /// <summary>
    /// Local pre-release check of a real OpenAPI document (run through SourceGenerators/scripts/check-spec.sh).
    /// The test is a no-op unless OPENAPI_CODEGEN_CHECK_SPEC (absolute path of the spec) is set, so the
    /// regular test.sh and CI runs are not affected. It runs the editor loader (YAML/JSON normalization,
    /// external reference graph), writes the canonical bundle, runs the source generator and compiles the
    /// result, then lists every diagnostic. OPENAPI_CODEGEN_CHECK_OUTPUT (optional) is a directory that the
    /// generated source is written to.
    /// </summary>
    public sealed class ExternalSpecCheckTests
    {
        private const string SpecVariable = "OPENAPI_CODEGEN_CHECK_SPEC";
        private const string OutputVariable = "OPENAPI_CODEGEN_CHECK_OUTPUT";
        private const string SpecId = "0123456789abcdef0123456789abcdef";

        private readonly ITestOutputHelper output;

        public ExternalSpecCheckTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public async Task CheckExternalSpecWhenOpenApiCodegenCheckSpecIsSet()
        {
            string? specPath = Environment.GetEnvironmentVariable(SpecVariable);
            if (string.IsNullOrWhiteSpace(specPath))
            {
                return;
            }

            // Second line of defense behind check-spec.sh: refuse to write into the repository before anything runs.
            string? requestedOutput = Environment.GetEnvironmentVariable(OutputVariable);
            if (!string.IsNullOrWhiteSpace(requestedOutput))
            {
                Assert.False(
                    IsInsideRepository(requestedOutput),
                    OutputVariable + " must be outside the repository working tree: " + requestedOutput);
            }

            specPath = Path.GetFullPath(specPath);
            Assert.True(File.Exists(specPath), "The spec does not exist: " + specPath);

            // The loader only accepts local external references inside its project root, so the spec's own
            // directory is the root (nothing is copied; relative $ref files next to the spec are resolved,
            // references that leave that directory are rejected by the loader).
            string projectRoot = Path.GetDirectoryName(specPath)!;
            string fileName = Path.GetFileName(specPath);
            int errors = 0;
            int warnings = 0;
            output.WriteLine("spec: " + specPath);

            string bundleText;
            string rawSource;
            try
            {
                var loader = new ExternalSpecGraphLoader(projectRoot, new RawJsonNormalizer(), new RawYamlNormalizer());
                NormalizedSpecGraph graph = await loader.LoadAsync(fileName, SpecId, CancellationToken.None);
                bundleText = Encoding.UTF8.GetString(CanonicalSpecBundleWriter.Write(
                    graph.SpecId, graph.RawSha256, graph.Documents, graph.Edges));
                rawSource = File.ReadAllText(specPath);
                output.WriteLine("loader: OK (" + graph.Documents.Count + " document(s))");
            }
            catch (NormalizedSpecException exception)
            {
                string code = string.IsNullOrEmpty(exception.DiagnosticCode) ? "(no code)" : exception.DiagnosticCode;
                output.WriteLine(
                    "[ERROR] loader " + code + " " + exception.SourcePath + ":" + exception.Line + ":" +
                    exception.Column + " " + exception.Message);
                output.WriteLine("summary: errors=1 warnings=0 (stopped at the loader)");
                Assert.Fail("The loader rejected the spec: " + code);
                return;
            }
            catch (Exception exception)
            {
                output.WriteLine("[ERROR] loader " + exception.GetType().Name + ": " + exception.Message);
                output.WriteLine("summary: errors=1 warnings=0 (stopped at the loader)");
                throw;
            }

            Phase4GeneratorExecution execution = MatrixMode.GenerateAndCompile(rawSource, bundle: bundleText);

            foreach (Diagnostic diagnostic in execution.RunResult.Diagnostics)
            {
                output.WriteLine(
                    "[" + diagnostic.Severity.ToString().ToUpperInvariant() + "] generator " + diagnostic.Id + " " +
                    diagnostic.Location.GetLineSpan() + " " + diagnostic.GetMessage());
                Count(diagnostic.Severity, ref errors, ref warnings);
            }

            IReadOnlyList<Diagnostic> compilationErrors = execution.CompilationErrors;
            foreach (Diagnostic diagnostic in compilationErrors)
            {
                output.WriteLine("[ERROR] compile " + diagnostic);
                errors++;
            }

            string? outputDirectory = Environment.GetEnvironmentVariable(OutputVariable);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
                string target = Path.Combine(outputDirectory, "Generated.cs");
                File.WriteAllText(target, execution.GeneratedSource);
                output.WriteLine("generated source: " + target);
            }

            output.WriteLine(
                "summary: errors=" + errors + " warnings=" + warnings + " (generator diagnostics=" +
                execution.RunResult.Diagnostics.Length + ", compile errors=" + compilationErrors.Count + ")");
            Assert.True(errors == 0, "The spec produced " + errors + " error(s); see the listed diagnostics.");
        }

        private static bool IsInsideRepository(string directory)
        {
            string projectDirectory = typeof(ExternalSpecCheckTests).Assembly
                .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
                .Cast<System.Reflection.AssemblyMetadataAttribute>()
                .Single(static attribute => attribute.Key == "ProjectDirectory").Value!;
            // <repository>/SourceGenerators/<tests project>
            string root = Path.GetFullPath(Path.Combine(projectDirectory, "..", ".."))
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            // Case-insensitive on purpose: the default macOS file system does not tell the spellings apart.
            string candidate = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        private static void Count(DiagnosticSeverity severity, ref int errors, ref int warnings)
        {
            if (severity == DiagnosticSeverity.Error)
            {
                errors++;
            }
            else if (severity == DiagnosticSeverity.Warning)
            {
                warnings++;
            }
        }
    }
}
