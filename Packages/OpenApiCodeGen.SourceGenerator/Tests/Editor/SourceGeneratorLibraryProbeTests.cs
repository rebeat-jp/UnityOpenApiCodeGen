#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using Rhycol.OpenApiCodeGen.Editor.Generation;
using Rhycol.OpenApiCodeGen.SourceGenerator;
using Rhycol.OpenApiCodeGen.SourceGenerator.Editor;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    /// <summary>An environment with fixed player assemblies and DLL versions.</summary>
    internal sealed class FakeLibraryEnvironment : ISourceGeneratorLibraryEnvironment
    {
        readonly List<PlayerAssemblyInfo> assemblies = new List<PlayerAssemblyInfo>();
        readonly Dictionary<string, Version> versions = new Dictionary<string, Version>();

        internal bool Throws { get; set; }

        internal FakeLibraryEnvironment Assembly(string name, params string[] references)
        {
            assemblies.Add(new PlayerAssemblyInfo(name, references));
            return this;
        }

        internal FakeLibraryEnvironment Version(string path, string version)
        {
            versions[path] = System.Version.Parse(version);
            return this;
        }

        public IReadOnlyList<PlayerAssemblyInfo> GetPlayerAssemblies()
        {
            if (Throws)
            {
                throw new InvalidOperationException("boom");
            }

            return assemblies;
        }

        public Version? GetAssemblyVersion(string assemblyPath)
        {
            return versions.TryGetValue(assemblyPath, out Version version) ? version : null;
        }
    }

    internal static class LibraryProbeTestSupport
    {
        internal const string Newtonsoft = "/p/Newtonsoft.Json.dll";
        internal const string SystemTextJson = "/p/System.Text.Json.dll";
        internal const string EncodingsWeb = "/p/System.Text.Encodings.Web.dll";
        internal const string UnityWebRequest = "/e/UnityEngine.UnityWebRequestModule.dll";
        internal const string Http = "/e/System.Net.Http.dll";

        internal static FakeLibraryEnvironment AllInstalledEnvironment(string assemblyName = "Example.Clients")
        {
            return new FakeLibraryEnvironment()
                .Assembly(assemblyName, Newtonsoft, SystemTextJson, EncodingsWeb, UnityWebRequest, Http)
                .Version(Newtonsoft, "13.0.0.0")
                .Version(SystemTextJson, "8.0.0.0")
                .Version(EncodingsWeb, "8.0.0.0");
        }

        /// <summary>A probe that finds every library, so tests of generation do not depend on the project.</summary>
        internal static SourceGeneratorLibraryProbe AllInstalledProbe()
        {
            return new SourceGeneratorLibraryProbe(AllInstalledEnvironment());
        }
    }

    internal sealed class SourceGeneratorLibraryProbeTests
    {
        [Test]
        public void EveryLibraryIsInstalledWhenTheTargetReferencesIt()
        {
            var probe = new SourceGeneratorLibraryProbe(LibraryProbeTestSupport.AllInstalledEnvironment());

            GenerationLibraryReport report = probe.Inspect("Example.Clients", false);

            Assert.That(report.ResolutionFailure, Is.Empty);
            foreach (GenerationLibraryState state in new[]
                     {
                         report.NewtonsoftJson, report.SystemTextJson, report.HttpClient, report.UnityWebRequest,
                     })
            {
                Assert.That(state.Status, Is.EqualTo(GenerationLibraryStatus.Installed), state.Name);
            }

            Assert.That(report.NewtonsoftJson.Version, Is.EqualTo("13.0.0"));
            Assert.That(report.SystemTextJson.Version, Is.EqualTo("8.0.0"));
            Assert.That(
                report.IsReady(GenerationHttpTransport.UnityWebRequest, GenerationJsonSerializer.SystemTextJson),
                Is.True);
        }

        [Test]
        public void LibrariesAreMissingWhenNoAssemblyReferencesThem()
        {
            var environment = new FakeLibraryEnvironment().Assembly("Example.Clients");
            var probe = new SourceGeneratorLibraryProbe(environment);

            GenerationLibraryReport report = probe.Inspect("Example.Clients", false);

            Assert.That(report.NewtonsoftJson.Status, Is.EqualTo(GenerationLibraryStatus.Missing));
            Assert.That(report.NewtonsoftJson.InstallGuidance, Does.Contain("com.unity.nuget.newtonsoft-json"));
            Assert.That(report.SystemTextJson.Status, Is.EqualTo(GenerationLibraryStatus.Missing));
            Assert.That(report.SystemTextJson.InstallGuidance, Does.Contain("org.nuget.system.text.json"));
            Assert.That(report.UnityWebRequest.Status, Is.EqualTo(GenerationLibraryStatus.Missing));
            Assert.That(report.UnityWebRequest.InstallGuidance, Does.Contain("com.unity.modules.unitywebrequest"));
            Assert.That(report.HttpClient.Status, Is.EqualTo(GenerationLibraryStatus.Missing));
            Assert.That(
                report.IsReady(GenerationHttpTransport.HttpClient, GenerationJsonSerializer.NewtonsoftJson),
                Is.False);
        }

        [Test]
        public void ALibraryUsedElsewhereButNotByTheTargetIsNotReferenced()
        {
            var environment = new FakeLibraryEnvironment()
                .Assembly("Example.Clients", LibraryProbeTestSupport.Http)
                .Assembly("Other", LibraryProbeTestSupport.Newtonsoft)
                .Version(LibraryProbeTestSupport.Newtonsoft, "13.0.0.0");
            var probe = new SourceGeneratorLibraryProbe(environment);

            GenerationLibraryReport report = probe.Inspect("Example.Clients", false);

            Assert.That(report.NewtonsoftJson.Status, Is.EqualTo(GenerationLibraryStatus.NotReferenced));
            Assert.That(report.NewtonsoftJson.InstallGuidance, Does.Contain("Override References"));
        }

        [Test]
        public void VersionsBelowTheMinimumAreTooOld()
        {
            var environment = new FakeLibraryEnvironment()
                .Assembly(
                    "Example.Clients",
                    LibraryProbeTestSupport.Newtonsoft,
                    LibraryProbeTestSupport.SystemTextJson,
                    LibraryProbeTestSupport.EncodingsWeb)
                .Version(LibraryProbeTestSupport.Newtonsoft, "12.0.3.0")
                .Version(LibraryProbeTestSupport.SystemTextJson, "7.0.0.0");
            var probe = new SourceGeneratorLibraryProbe(environment);

            GenerationLibraryReport report = probe.Inspect("Example.Clients", false);

            Assert.That(report.NewtonsoftJson.Status, Is.EqualTo(GenerationLibraryStatus.TooOld));
            Assert.That(report.NewtonsoftJson.Version, Is.EqualTo("12.0.3"));
            Assert.That(report.NewtonsoftJson.RequiredVersion, Is.EqualTo("13.0"));
            Assert.That(report.SystemTextJson.Status, Is.EqualTo(GenerationLibraryStatus.TooOld));
            Assert.That(report.SystemTextJson.RequiredVersion, Is.EqualTo("8.0"));
        }

        [Test]
        public void TheHighestVersionWinsWhenSeveralReferencesDefineTheLibrary()
        {
            var environment = new FakeLibraryEnvironment()
                .Assembly("Example.Clients", "/a/Newtonsoft.Json.dll", "/b/Newtonsoft.Json.dll")
                .Version("/a/Newtonsoft.Json.dll", "12.0.0.0")
                .Version("/b/Newtonsoft.Json.dll", "13.0.0.0");
            var probe = new SourceGeneratorLibraryProbe(environment);

            Assert.That(
                probe.Inspect("Example.Clients", false).NewtonsoftJson.Status,
                Is.EqualTo(GenerationLibraryStatus.Installed));
        }

        [Test]
        public void SystemTextJsonNeedsEncodingsWebBecauseTheGeneratedCodeUsesItAtRunTime()
        {
            var environment = new FakeLibraryEnvironment()
                .Assembly("Example.Clients", LibraryProbeTestSupport.SystemTextJson)
                .Version(LibraryProbeTestSupport.SystemTextJson, "8.0.0.0");
            var probe = new SourceGeneratorLibraryProbe(environment);

            GenerationLibraryReport report = probe.Inspect("Example.Clients", false);

            Assert.That(report.SystemTextJson.IsInstalled, Is.False);
            Assert.That(report.SystemTextJson.Name, Does.Contain("Encodings.Web"));
            Assert.That(report.SystemTextJson.InstallGuidance, Does.Contain("run time"));
        }

        [Test]
        public void NoEngineReferencesMakesUnityWebRequestUnreferenced()
        {
            var probe = new SourceGeneratorLibraryProbe(LibraryProbeTestSupport.AllInstalledEnvironment());

            GenerationLibraryReport report = probe.Inspect("Example.Clients", true);

            Assert.That(report.UnityWebRequest.Status, Is.EqualTo(GenerationLibraryStatus.NotReferenced));
            Assert.That(report.UnityWebRequest.InstallGuidance, Does.Contain("No Engine References"));
        }

        [Test]
        public void AnAssemblyUnknownToTheCompilationPipelineFallsBackToTheProjectWideReferences()
        {
            var environment = LibraryProbeTestSupport.AllInstalledEnvironment("Existing.Assembly");
            var probe = new SourceGeneratorLibraryProbe(environment);

            GenerationLibraryReport report = probe.Inspect("Brand.New.Assembly", false);

            Assert.That(report.NewtonsoftJson.IsInstalled, Is.True);
            Assert.That(report.UnityWebRequest.IsInstalled, Is.True);
        }

        [Test]
        public void NetStandardAloneIsEnoughForHttpClient()
        {
            var environment = new FakeLibraryEnvironment()
                .Assembly("Example.Clients", "/e/netstandard.dll");
            var probe = new SourceGeneratorLibraryProbe(environment);

            Assert.That(probe.Inspect("Example.Clients", false).HttpClient.IsInstalled, Is.True);
        }

        [Test]
        public void TheCurrentProjectReportsHttpClientAndNewtonsoftFromTheRealCompilationPipeline()
        {
            // Runs against the Unity project that is executing the tests (which has Newtonsoft.Json).
            // The runtime assembly is a player assembly the pipeline knows.
            GenerationLibraryReport report = SourceGeneratorLibraryProbe.CreateForCurrentProject()
                .Inspect("Unity.OpenApiCodeGen.SourceGenerator", false);

            Assert.That(report.ResolutionFailure, Is.Empty);
            Assert.That(report.HttpClient.Status, Is.EqualTo(GenerationLibraryStatus.Installed), report.HttpClient.Describe());
            Assert.That(report.NewtonsoftJson.Status, Is.EqualTo(GenerationLibraryStatus.Installed), report.NewtonsoftJson.Describe());
            Assert.That(report.NewtonsoftJson.Version, Does.StartWith("13."));
            TestContext.Out.WriteLine(
                "REALPROBE " + report.NewtonsoftJson.Describe() + " | " + report.SystemTextJson.Describe() +
                " | " + report.HttpClient.Describe() + " | " + report.UnityWebRequest.Describe());
        }

        [Test]
        public void AFailingEnvironmentProducesAnUnresolvedReportInsteadOfThrowing()
        {
            var probe = new SourceGeneratorLibraryProbe(new FakeLibraryEnvironment { Throws = true });

            GenerationLibraryReport report = probe.Inspect("Example.Clients", false);

            Assert.That(report.ResolutionFailure, Does.Contain("boom"));
        }

        [Test]
        public void FileNameMatchingIgnoresCaseAndDirectory()
        {
            var environment = new FakeLibraryEnvironment()
                .Assembly("Example.Clients", "C:\\Lib\\NEWTONSOFT.JSON.DLL")
                .Version("C:\\Lib\\NEWTONSOFT.JSON.DLL", "13.0.0.0");
            var probe = new SourceGeneratorLibraryProbe(environment);

            Assert.That(
                probe.IsLibraryPresentInProject(SourceGeneratorLibraryProbe.NewtonsoftJsonAssemblyFileName),
                Is.True);
            Assert.That(
                probe.IsLibraryPresentInProject(SourceGeneratorLibraryProbe.SystemTextJsonAssemblyFileName),
                Is.False);
        }

        [Test]
        public void MinimumVersionsMatchTheAnalyzerContract()
        {
            // The analyzer's LibraryAvailability requires 13.0 / 8.0. The analyzer test project compares this
            // file against LibraryAvailability, so changing these values here without the analyzer fails there.
            Assert.That(Version.Parse(SourceGeneratorLibraryProbe.NewtonsoftJsonMinimumVersion), Is.EqualTo(new Version(13, 0)));
            Assert.That(Version.Parse(SourceGeneratorLibraryProbe.SystemTextJsonMinimumVersion), Is.EqualTo(new Version(8, 0)));
        }
    }

    internal sealed class SourceGeneratorLibraryGateTests
    {
        string projectRoot = string.Empty;
        string rawSpecPath = string.Empty;
        string outputFolder = string.Empty;
        int compilationRequestCount;

        [SetUp]
        public void SetUp()
        {
            projectRoot = Path.Combine(
                Path.GetTempPath(),
                "SourceGeneratorLibraryGateTests",
                Guid.NewGuid().ToString("N"));
            rawSpecPath = Path.Combine(projectRoot, "Assets", "Specs", "petstore.json");
            outputFolder = Path.Combine(projectRoot, "Assets", "Clients", "PetStore");
            Directory.CreateDirectory(Path.GetDirectoryName(rawSpecPath)!);
            Directory.CreateDirectory(outputFolder);
            File.WriteAllText(rawSpecPath, "{\"openapi\":\"3.0.3\"}", new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(projectRoot, "Assets", "Clients", "Example.Clients.asmdef"),
                "{\n  \"name\": \"Example.Clients\",\n  \"references\": [\"Unity.OpenApiCodeGen.SourceGenerator\"]\n}\n",
                new UTF8Encoding(false));
            compilationRequestCount = 0;
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, true);
            }
        }

        [Test]
        public void GenerateFailsWithGuidanceAndWritesNothingWhenTheJsonLibraryIsMissing()
        {
            SourceGeneratorGenerationProvider provider = CreateProvider(
                new FakeLibraryEnvironment().Assembly("Example.Clients", LibraryProbeTestSupport.Http));

            GenerationResult result = provider.Generate(CreateRequest());

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Does.Contain("Newtonsoft.Json").And.Contain("com.unity.nuget.newtonsoft-json"));
            Assert.That(result.Message, Does.Contain("Nothing was generated"));
            Assert.That(Directory.GetFiles(outputFolder), Is.Empty);
            Assert.That(compilationRequestCount, Is.EqualTo(0));
        }

        [Test]
        public void GenerateAsyncAlsoChecksTheLibrariesFirst()
        {
            SourceGeneratorGenerationProvider provider = CreateProvider(
                new FakeLibraryEnvironment().Assembly("Example.Clients", LibraryProbeTestSupport.Http));

            GenerationResult result = provider.GenerateAsync(CreateRequest()).GetAwaiter().GetResult();

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(Directory.GetFiles(outputFolder), Is.Empty);
        }

        [Test]
        public void TheRequestedCombinationIsCheckedAndWrittenToTheDefinition()
        {
            // Newtonsoft is installed but System.Text.Json is not: the request for STJ is refused.
            var environment = new FakeLibraryEnvironment()
                .Assembly(
                    "Example.Clients",
                    LibraryProbeTestSupport.Newtonsoft,
                    LibraryProbeTestSupport.Http,
                    LibraryProbeTestSupport.UnityWebRequest)
                .Version(LibraryProbeTestSupport.Newtonsoft, "13.0.0.0");
            SourceGeneratorGenerationProvider provider = CreateProvider(environment);

            GenerationRequest stj = CreateRequest();
            stj.JsonSerializer = GenerationJsonSerializer.SystemTextJson;
            GenerationResult refused = provider.Generate(stj);
            Assert.That(refused.IsSuccess, Is.False);
            Assert.That(refused.Message, Does.Contain("System.Text.Json"));
            Assert.That(Directory.GetFiles(outputFolder), Is.Empty);

            GenerationRequest uwr = CreateRequest();
            uwr.HttpTransport = GenerationHttpTransport.UnityWebRequest;
            GenerationResult accepted = provider.Generate(uwr);
            Assert.That(accepted.IsSuccess, Is.True, accepted.Message);
            string definition = File.ReadAllText(Path.Combine(outputFolder, "PetStoreApi.OpenApiDefinition.cs"));
            Assert.That(definition, Does.Contain("OpenApiHttpTransport.UnityWebRequest"));
            Assert.That(definition, Does.Not.Contain("JsonSerializer"));
        }

        [Test]
        public void ADefaultRequestWritesTheOriginalDefinitionWhenTheLibrariesAreReady()
        {
            SourceGeneratorGenerationProvider provider = CreateProvider(
                LibraryProbeTestSupport.AllInstalledEnvironment());

            GenerationResult result = provider.Generate(CreateRequest());

            Assert.That(result.IsSuccess, Is.True, result.Message);
            string definition = File.ReadAllText(Path.Combine(outputFolder, "PetStoreApi.OpenApiDefinition.cs"));
            Assert.That(definition, Does.Not.Contain("HttpTransport").And.Not.Contain("JsonSerializer"));
        }

        [Test]
        public void ANonexistentOutputAssemblyIsLeftToTheNormalDefinitionChecks()
        {
            // No asmdef above this folder: the provider's own error, not a library error, is reported.
            string bare = Path.Combine(projectRoot, "Assets", "Bare");
            Directory.CreateDirectory(bare);
            SourceGeneratorGenerationProvider provider = CreateProvider(
                new FakeLibraryEnvironment().Assembly("Other"));
            var request = new GenerationRequest(rawSpecPath, bare, "PetStoreApi", "Example.Generated.PetStore");

            GenerationResult result = provider.Generate(request);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Does.Not.Contain("Nothing was generated"));
        }

        [Test]
        public void InspectReportsTheStatesWithoutGenerating()
        {
            SourceGeneratorGenerationProvider provider = CreateProvider(
                new FakeLibraryEnvironment().Assembly("Example.Clients", LibraryProbeTestSupport.Http));

            GenerationLibraryReport report = provider.Inspect(outputFolder);

            Assert.That(report.NewtonsoftJson.Status, Is.EqualTo(GenerationLibraryStatus.Missing));
            Assert.That(report.HttpClient.IsInstalled, Is.True);
            Assert.That(Directory.GetFiles(outputFolder), Is.Empty);
            Assert.That(provider, Is.InstanceOf<IGenerationLibraryInspector>());
        }

        [Test]
        public void MissingLibrariesDoNotChangeProviderAvailability()
        {
            // Availability decides the OPENAPI_CODEGEN_SOURCE_GENERATOR define. It must stay available so
            // existing definitions keep compiling and the analyzer can report OACG011 itself.
            SourceGeneratorGenerationProvider provider = CreateProvider(new FakeLibraryEnvironment().Assembly("Example.Clients"));

            Assert.That(provider.Descriptor.Availability.IsAvailable, Is.True);
        }

        GenerationRequest CreateRequest()
        {
            return new GenerationRequest(rawSpecPath, outputFolder, "PetStoreApi", "Example.Generated.PetStore");
        }

        SourceGeneratorGenerationProvider CreateProvider(FakeLibraryEnvironment environment)
        {
            var cacheService = new NormalizedSpecCacheService(
                projectRoot,
                new RawJsonNormalizer(),
                new AtomicFileWriter(),
                new NullMirrorImporter());
            var definitionWriter = new OpenApiClientDefinitionWriter(
                projectRoot,
                new AtomicFileWriter(),
                _ => { });
            return new SourceGeneratorGenerationProvider(
                GenerationProviderAvailability.Available(),
                cacheService,
                definitionWriter,
                () => compilationRequestCount++,
                new SourceGeneratorLibraryProbe(environment));
        }

        sealed class NullMirrorImporter : ICompilerMirrorImporter
        {
            public void Import(string mirrorAssetPath)
            {
            }
        }
    }

    internal sealed class SourceGeneratorNewtonsoftNoticeTests
    {
        static string Definition(string namedArguments)
        {
            return OpenApiClientDefinitionWriter.OwnedFileHeader + "\n// Spec ID: x\n" +
                   "    [global::Rhycol.OpenApiCodeGen.SourceGenerator.OpenApiClientDefinitionAttribute(\n" +
                   "        global::Rhycol.OpenApiCodeGen.SourceGenerator.OpenApiDocumentFormat.Json" + namedArguments;
        }

        [Test]
        public void ADefinitionWithoutTheSerializerArgumentUsesNewtonsoft()
        {
            Assert.That(SourceGeneratorNewtonsoftNotice.UsesNewtonsoftJson(Definition(")]")), Is.True);
            Assert.That(
                SourceGeneratorNewtonsoftNotice.UsesNewtonsoftJson(
                    Definition(",\n        HttpTransport = X.UnityWebRequest)]")),
                Is.True);
            Assert.That(
                SourceGeneratorNewtonsoftNotice.UsesNewtonsoftJson(
                    Definition(",\n        JsonSerializer = global::X.OpenApiJsonSerializer.SystemTextJson)]")),
                Is.False);
            Assert.That(SourceGeneratorNewtonsoftNotice.UsesNewtonsoftJson("class Foreign {}"), Is.False);
        }

        [Test]
        public void TheNoticeIsShownOnlyOnceAndOnlyWhenNewtonsoftIsMissing()
        {
            string[] definitions = { Definition(")]") };
            Assert.That(SourceGeneratorNewtonsoftNotice.ShouldNotify(definitions, false, false), Is.True);
            Assert.That(SourceGeneratorNewtonsoftNotice.ShouldNotify(definitions, true, false), Is.False);
            Assert.That(SourceGeneratorNewtonsoftNotice.ShouldNotify(definitions, false, true), Is.False);
            Assert.That(
                SourceGeneratorNewtonsoftNotice.ShouldNotify(new string[0], false, false),
                Is.False);
            Assert.That(
                SourceGeneratorNewtonsoftNotice.ShouldNotify(
                    new[] { Definition(",\n        JsonSerializer = global::X.OpenApiJsonSerializer.SystemTextJson)]") },
                    false,
                    false),
                Is.False);
        }

        [Test]
        public void TheMessageNamesTheCauseTheDiagnosticAndTheFix()
        {
            string message = SourceGeneratorNewtonsoftNotice.BuildMessage();
            Assert.That(message, Does.Contain("OACG011"));
            Assert.That(message, Does.Contain("com.unity.nuget.newtonsoft-json"));
        }
    }
}
