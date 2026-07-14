#nullable enable

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Rhycol.OpenApiCodeGen.Editor.Generation;
using Rhycol.OpenApiCodeGen.SourceGenerator.Editor;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    internal sealed class SourceGeneratorProviderTests
    {
        string _packageRoot = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _packageRoot = Path.Combine(
                Path.GetTempPath(),
                "OpenApiCodeGenSourceGeneratorProviderTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_packageRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_packageRoot))
            {
                Directory.Delete(_packageRoot, true);
            }
        }

        [Test]
        public void ProbeReportsAvailableForPackagedAnalyzerWithRoslynAnalyzerLabel()
        {
            CreateAnalyzer("labels:\n- RoslynAnalyzer\n");

            GenerationProviderAvailability availability = CreateProbe().Probe();

            Assert.That(availability.IsAvailable, Is.True);
            Assert.That(availability.Reason, Is.Empty);
        }

        [Test]
        public void ProbeReportsUnavailableWhenPackageResolvedPathCannotBeDetermined()
        {
            var probe = new SourceGeneratorAvailabilityProbe(
                new FixedPackagePathResolver(null));

            GenerationProviderAvailability availability = probe.Probe();

            Assert.That(availability.IsAvailable, Is.False);
            Assert.That(availability.Reason, Does.Contain("resolved path"));
        }

        [Test]
        public void ProbeReportsUnavailableWhenPackageResolvedPathDoesNotExist()
        {
            Directory.Delete(_packageRoot);

            GenerationProviderAvailability availability = CreateProbe().Probe();

            Assert.That(availability.IsAvailable, Is.False);
            Assert.That(availability.Reason, Does.Contain("does not exist"));
        }

        [Test]
        public void ProbeReportsUnavailableWhenAnalyzerDllIsMissing()
        {
            GenerationProviderAvailability availability = CreateProbe().Probe();

            Assert.That(availability.IsAvailable, Is.False);
            Assert.That(availability.Reason, Does.Contain("analyzer DLL"));
        }

        [Test]
        public void ProbeReportsUnavailableWhenAnalyzerMetadataIsMissing()
        {
            string analyzerPath = GetAnalyzerPath();
            Directory.CreateDirectory(Path.GetDirectoryName(analyzerPath));
            File.WriteAllBytes(analyzerPath, Array.Empty<byte>());

            GenerationProviderAvailability availability = CreateProbe().Probe();

            Assert.That(availability.IsAvailable, Is.False);
            Assert.That(availability.Reason, Does.Contain("metadata was not found"));
        }

        [TestCase("labels:\n- OtherLabel\n")]
        [TestCase("labels:\n- roslynanalyzer\n")]
        public void ProbeRequiresExactRoslynAnalyzerLabel(string metadata)
        {
            CreateAnalyzer(metadata);

            GenerationProviderAvailability availability = CreateProbe().Probe();

            Assert.That(availability.IsAvailable, Is.False);
            Assert.That(availability.Reason, Does.Contain("RoslynAnalyzer"));
        }

        [Test]
        public void ProbeDoesNotTreatRoslynAnalyzerOutsideLabelsAsALabel()
        {
            CreateAnalyzer("PluginImporter:\n- RoslynAnalyzer\n");

            GenerationProviderAvailability availability = CreateProbe().Probe();

            Assert.That(availability.IsAvailable, Is.False);
            Assert.That(availability.Reason, Does.Contain("RoslynAnalyzer"));
        }

        [Test]
        public void ProbeConvertsInspectionExceptionToUnavailableReason()
        {
            var probe = new SourceGeneratorAvailabilityProbe(
                new ThrowingPackagePathResolver());

            GenerationProviderAvailability availability = probe.Probe();

            Assert.That(availability.IsAvailable, Is.False);
            Assert.That(availability.Reason, Does.Contain("probe failed"));
        }

        [Test]
        public void ProviderDescribesSourceGeneratorAndPreservesAvailability()
        {
            GenerationProviderAvailability availability =
                GenerationProviderAvailability.Unavailable("Analyzer is unavailable.");
            var provider = new SourceGeneratorGenerationProvider(availability);

            Assert.That(provider.Descriptor.Provider, Is.EqualTo(GenerateProvider.SourceGenerator));
            Assert.That(provider.Descriptor.DisplayName, Is.EqualTo("Source Generator"));
            Assert.That(provider.Descriptor.Availability, Is.SameAs(availability));
        }

        [Test]
        public void GenerateReturnsExplicitPhase3Failure()
        {
            var provider = new SourceGeneratorGenerationProvider(
                GenerationProviderAvailability.Available());

            GenerationResult result = provider.Generate(
                new GenerationRequest("openapi.json", "Generated"));

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Does.Contain("Phase 3"));
        }

        [Test]
        public async Task GenerateAsyncReturnsExplicitPhase3Failure()
        {
            var provider = new SourceGeneratorGenerationProvider(
                GenerationProviderAvailability.Available());

            GenerationResult result = await provider.GenerateAsync(
                new GenerationRequest("openapi.json", "Generated"));

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Does.Contain("Phase 3"));
        }

        [Test]
        public void InitializeOnLoadRegistrationUsesResolvedPackageAnalyzer()
        {
            SourceGeneratorProviderRegistration.EnsureRegistered();

            GenerationProviderResolution resolution =
                GenerationProviderRegistry.Shared.Resolve(
                    GenerateProvider.SourceGenerator);

            Assert.That(resolution.IsResolved, Is.True);
            Assert.That(
                resolution.Provider!.Descriptor.Availability.IsAvailable,
                Is.True,
                resolution.Provider.Descriptor.Availability.Reason);
        }

        [Test]
        public void RegistrationKeepsFirstProviderWhenRegistrationIsDuplicated()
        {
            CreateAnalyzer("labels:\n- RoslynAnalyzer\n");
            var registry = new GenerationProviderRegistry();
            SourceGeneratorAvailabilityProbe probe = CreateProbe();

            bool firstRegistered = SourceGeneratorProviderRegistration.TryRegister(
                registry,
                probe,
                out string firstFailureReason);
            IGenerationProvider first = registry
                .Resolve(GenerateProvider.SourceGenerator)
                .Provider!;
            bool duplicateRegistered = SourceGeneratorProviderRegistration.TryRegister(
                registry,
                probe,
                out string duplicateFailureReason);

            Assert.That(firstRegistered, Is.True);
            Assert.That(firstFailureReason, Is.Empty);
            Assert.That(duplicateRegistered, Is.False);
            Assert.That(duplicateFailureReason, Does.Contain("already registered"));
            Assert.That(
                registry.Resolve(GenerateProvider.SourceGenerator).Provider,
                Is.SameAs(first));
        }

        [Test]
        public void RegistrationRetainsUnavailableProviderWithReason()
        {
            var registry = new GenerationProviderRegistry();

            bool registered = SourceGeneratorProviderRegistration.TryRegister(
                registry,
                CreateProbe(),
                out string failureReason);

            Assert.That(registered, Is.True);
            Assert.That(failureReason, Is.Empty);
            GenerationProviderResolution resolution = registry.Resolve(
                GenerateProvider.SourceGenerator);
            Assert.That(resolution.IsResolved, Is.True);
            Assert.That(resolution.Provider!.Descriptor.Availability.IsAvailable, Is.False);
            Assert.That(
                resolution.Provider.Descriptor.Availability.Reason,
                Does.Contain("analyzer DLL"));
        }

        SourceGeneratorAvailabilityProbe CreateProbe()
        {
            return new SourceGeneratorAvailabilityProbe(
                new FixedPackagePathResolver(_packageRoot));
        }

        void CreateAnalyzer(string metadata)
        {
            string analyzerPath = GetAnalyzerPath();
            Directory.CreateDirectory(Path.GetDirectoryName(analyzerPath));
            File.WriteAllBytes(analyzerPath, Array.Empty<byte>());
            File.WriteAllText(analyzerPath + ".meta", metadata, new UTF8Encoding(false));
        }

        string GetAnalyzerPath()
        {
            return Path.Combine(
                _packageRoot,
                "Runtime",
                "Analyzers",
                SourceGeneratorAvailabilityProbe.AnalyzerFileName);
        }

        sealed class FixedPackagePathResolver : ISourceGeneratorPackagePathResolver
        {
            readonly string? _resolvedPath;

            internal FixedPackagePathResolver(string? resolvedPath)
            {
                _resolvedPath = resolvedPath;
            }

            public string? GetResolvedPath()
            {
                return _resolvedPath;
            }
        }

        sealed class ThrowingPackagePathResolver : ISourceGeneratorPackagePathResolver
        {
            public string? GetResolvedPath()
            {
                throw new InvalidOperationException("probe failed");
            }
        }
    }
}
