using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    /// <summary>
    /// The Unity EditMode fixture compiles a committed snapshot of the generated UnityWebRequest client
    /// (BuildTools/UnityVerificationFixture/UnityWebRequestClient/Generated/UwrApi.g.cs) so that the real
    /// UnityWebRequest runs the generated adapter. This test keeps the snapshot equal to the current generator
    /// output; UPDATE_UNITY_FIXTURE_SNAPSHOT=1 rewrites it.
    /// </summary>
    public sealed class UnityWebRequestFixtureSnapshotTests
    {
        private const string UpdateVariable = "UPDATE_UNITY_FIXTURE_SNAPSHOT";
        private const string ApiName = "UwrApi";
        private const string GeneratedNamespace = "Rhycol.OpenApiCodeGen.UnityWebRequestVerification";

        [Fact]
        public void SnapshotMatchesTheGeneratorOutput()
        {
            string spec = File.ReadAllText(Path.Combine(
                AppContext.BaseDirectory, "ApiSurfaceSpecs", "unity-uwr-fixture-openapi.json"));

            Phase4GeneratorExecution execution = Phase4GeneratorTestHarness.GenerateAndCompile(
                spec,
                apiName: ApiName,
                generatedNamespace: GeneratedNamespace,
                options: new Phase4CompilationOptions
                {
                    UnityWebRequestStub = true,
                    HttpTransport = "UnityWebRequest"
                });

            Assert.Empty(execution.RunResult.Diagnostics);
            Assert.Empty(execution.CompilationErrors);
            string actual = execution.GeneratedSource.Replace("\r\n", "\n");
            if (!actual.EndsWith("\n", StringComparison.Ordinal))
            {
                actual += "\n";
            }

            string path = Path.Combine(
                typeof(UnityWebRequestFixtureSnapshotTests).Assembly
                    .GetCustomAttributes<AssemblyMetadataAttribute>()
                    .Single(static attribute => attribute.Key == "ProjectDirectory")
                    .Value!,
                "..", "BuildTools", "UnityVerificationFixture", "UnityWebRequestClient", "Generated", "UwrApi.g.cs");
            path = Path.GetFullPath(path);
            if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
            {
                File.WriteAllText(path, actual, new UTF8Encoding(false));
                return;
            }

            Assert.True(File.Exists(path), "Missing snapshot. Run with " + UpdateVariable + "=1 to create it.");
            Assert.True(
                string.Equals(File.ReadAllText(path).Replace("\r\n", "\n"), actual, StringComparison.Ordinal),
                "The Unity fixture snapshot is stale. Rerun with " + UpdateVariable + "=1 and review the diff: " + path);
            Assert.DoesNotContain("HttpClient _httpClient", actual);
            Assert.Contains("SendUnityWebRequestAsync", actual);
        }
    }
}
