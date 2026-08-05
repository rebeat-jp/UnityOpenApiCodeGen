#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using NUnit.Framework;
using Rhycol.OpenApiCodeGen.Editor.Generation;
using Rhycol.OpenApiCodeGen.SourceGenerator.Editor;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    internal sealed class SourceGeneratorGenerationProviderTests
    {
        string projectRoot = string.Empty;
        string rawSpecPath = string.Empty;
        string outputFolder = string.Empty;
        RecordingMirrorImporter mirrorImporter = null!;
        List<string> definitionImports = null!;
        int compilationRequestCount;

        [SetUp]
        public void SetUp()
        {
            projectRoot = Path.Combine(
                Path.GetTempPath(),
                "SourceGeneratorGenerationProviderTests",
                Guid.NewGuid().ToString("N"));
            rawSpecPath = Path.Combine(projectRoot, "Assets", "Specs", "petstore.json");
            outputFolder = Path.Combine(projectRoot, "Assets", "Clients", "PetStore");
            Directory.CreateDirectory(Path.GetDirectoryName(rawSpecPath));
            Directory.CreateDirectory(outputFolder);
            File.WriteAllText(
                rawSpecPath,
                "{\"openapi\":\"3.0.3\"}",
                new UTF8Encoding(false));
            WriteAsmdef(
                Path.Combine(projectRoot, "Assets", "Clients"),
                "Example.Clients",
                "Unity.OpenApiCodeGen.SourceGenerator");
            mirrorImporter = new RecordingMirrorImporter();
            definitionImports = new List<string>();
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
        public void GeneratePublishesNormalizedMirrorAndPhysicalDefinitionThenRequestsCompilation()
        {
            SourceGeneratorGenerationProvider provider = CreateProvider();

            GenerationResult result = provider.Generate(CreateRequest());

            Assert.That(result.IsSuccess, Is.True, result.Message);
            Assert.That(
                result.Message,
                Does.StartWith(SourceGeneratorGenerationProvider.GenerationSucceededMessage));
            string specId = ReadSpecId(result.Message);
            string mirrorPath = Path.Combine(
                projectRoot,
                NormalizedSpecBundleConstants.CompilerMirrorRelativePath,
                specId + NormalizedSpecBundleConstants.AdditionalFileSuffix);
            string definitionPath = Path.Combine(
                outputFolder,
                "PetStoreApi.OpenApiDefinition.cs");
            Assert.That(File.Exists(mirrorPath), Is.True);
            Assert.That(File.Exists(definitionPath), Is.True);
            Assert.That(
                File.ReadAllText(definitionPath),
                Does.Contain("public partial class PetStoreApi"));
            Assert.That(File.ReadAllText(definitionPath), Does.Contain("\"" + specId + "\""));
            Assert.That(mirrorImporter.ImportedAssetPaths, Has.Count.EqualTo(1));
            Assert.That(
                definitionImports,
                Is.EqualTo(new[]
                {
                    "Assets/Clients/PetStore/PetStoreApi.OpenApiDefinition.cs",
                }));
            Assert.That(compilationRequestCount, Is.EqualTo(1));
        }

        [Test]
        public void ByteIdenticalRerunDoesNotRewriteImportOrRequestCompilation()
        {
            SourceGeneratorGenerationProvider provider = CreateProvider();
            GenerationResult first = provider.Generate(CreateRequest());
            Assert.That(first.IsSuccess, Is.True, first.Message);
            string specId = ReadSpecId(first.Message);
            string mirrorPath = Path.Combine(
                projectRoot,
                NormalizedSpecBundleConstants.CompilerMirrorRelativePath,
                specId + NormalizedSpecBundleConstants.AdditionalFileSuffix);
            string definitionPath = Path.Combine(
                outputFolder,
                "PetStoreApi.OpenApiDefinition.cs");
            var mirrorTimestamp = new DateTime(2005, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var definitionTimestamp = new DateTime(2006, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(mirrorPath, mirrorTimestamp);
            File.SetLastWriteTimeUtc(definitionPath, definitionTimestamp);
            mirrorImporter.ImportedAssetPaths.Clear();
            definitionImports.Clear();

            GenerationResult repeated = provider.Generate(CreateRequest());

            Assert.That(repeated.IsSuccess, Is.True, repeated.Message);
            Assert.That(
                repeated.Message,
                Does.StartWith(SourceGeneratorGenerationProvider.GenerationAlreadyCurrentMessage));
            Assert.That(compilationRequestCount, Is.EqualTo(1));
            Assert.That(mirrorImporter.ImportedAssetPaths, Is.Empty);
            Assert.That(definitionImports, Is.Empty);
            Assert.That(File.GetLastWriteTimeUtc(mirrorPath), Is.EqualTo(mirrorTimestamp));
            Assert.That(File.GetLastWriteTimeUtc(definitionPath), Is.EqualTo(definitionTimestamp));
        }

        [TestCase("https://example.test/openapi.json", "URLs are not supported")]
        [TestCase("Assets/Specs/openapi.txt", "supports local .json, .yaml, and .yml documents only")]
        public void GenerateRejectsInputsOutsideLocalSupportedScope(
            string input,
            string expectedMessage)
        {
            SourceGeneratorGenerationProvider provider = CreateProvider();
            GenerationRequest request = new GenerationRequest(
                input,
                outputFolder,
                "PetStoreApi",
                "Example.Generated.PetStore");

            GenerationResult result = provider.Generate(request);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Does.Contain(expectedMessage));
            Assert.That(compilationRequestCount, Is.Zero);
            Assert.That(definitionImports, Is.Empty);
        }

        [TestCase("petstore.yaml")]
        [TestCase("petstore.YML")]
        public void GenerateAcceptsLocalYamlAndPropagatesDocumentFormat(string fileName)
        {
            string yamlPath = Path.Combine(projectRoot, "Assets", "Specs", fileName);
            File.WriteAllText(
                yamlPath,
                "openapi: 3.0.3\ninfo:\n  title: Pet Store\n  version: 1.0.0\npaths: {}\n",
                new UTF8Encoding(false));
            SourceGeneratorGenerationProvider provider = CreateProvider();
            GenerationRequest request = new GenerationRequest(
                yamlPath,
                outputFolder,
                "PetStoreApi",
                "Example.Generated.PetStore");

            GenerationResult result = provider.Generate(request);

            Assert.That(result.IsSuccess, Is.True, result.Message);
            string specId = ReadSpecId(result.Message);
            string mirrorPath = Path.Combine(
                projectRoot,
                NormalizedSpecBundleConstants.CompilerMirrorRelativePath,
                specId + NormalizedSpecBundleConstants.AdditionalFileSuffix);
            string definitionPath = Path.Combine(
                outputFolder,
                "PetStoreApi.OpenApiDefinition.cs");
            Assert.That(File.Exists(mirrorPath), Is.True);
            Assert.That(
                File.ReadAllText(definitionPath),
                Does.Contain("OpenApiDocumentFormat.Yaml"));
            Assert.That(mirrorImporter.ImportedAssetPaths, Has.Count.EqualTo(1));
            Assert.That(compilationRequestCount, Is.EqualTo(1));
        }

        [Test]
        public void InvalidYamlPreservesLastKnownGoodCacheAndDefinitionWithoutCompilation()
        {
            string yamlPath = Path.Combine(projectRoot, "Assets", "Specs", "petstore.yaml");
            File.WriteAllText(
                yamlPath,
                "openapi: 3.0.3\npaths: {}\n",
                new UTF8Encoding(false));
            SourceGeneratorGenerationProvider provider = CreateProvider();
            GenerationRequest request = new GenerationRequest(
                yamlPath,
                outputFolder,
                "PetStoreApi",
                "Example.Generated.PetStore");
            GenerationResult first = provider.Generate(request);
            Assert.That(first.IsSuccess, Is.True, first.Message);
            string specId = ReadSpecId(first.Message);
            string mirrorPath = Path.Combine(
                projectRoot,
                NormalizedSpecBundleConstants.CompilerMirrorRelativePath,
                specId + NormalizedSpecBundleConstants.AdditionalFileSuffix);
            string definitionPath = Path.Combine(
                outputFolder,
                "PetStoreApi.OpenApiDefinition.cs");
            byte[] mirrorBytes = File.ReadAllBytes(mirrorPath);
            byte[] definitionBytes = File.ReadAllBytes(definitionPath);
            DateTime mirrorTimestamp = File.GetLastWriteTimeUtc(mirrorPath);
            DateTime definitionTimestamp = File.GetLastWriteTimeUtc(definitionPath);
            mirrorImporter.ImportedAssetPaths.Clear();
            definitionImports.Clear();
            File.WriteAllText(yamlPath, "openapi: [\n", new UTF8Encoding(false));

            GenerationResult invalid = provider.Generate(request);

            Assert.That(invalid.IsSuccess, Is.False);
            Assert.That(invalid.Message, Does.Contain("YAML"));
            Assert.That(File.ReadAllBytes(mirrorPath), Is.EqualTo(mirrorBytes));
            Assert.That(File.ReadAllBytes(definitionPath), Is.EqualTo(definitionBytes));
            Assert.That(File.GetLastWriteTimeUtc(mirrorPath), Is.EqualTo(mirrorTimestamp));
            Assert.That(File.GetLastWriteTimeUtc(definitionPath), Is.EqualTo(definitionTimestamp));
            Assert.That(mirrorImporter.ImportedAssetPaths, Is.Empty);
            Assert.That(definitionImports, Is.Empty);
            Assert.That(compilationRequestCount, Is.EqualTo(1));
        }

        [Test]
        public void InvalidJsonDoesNotPublishDefinitionOrRequestCompilation()
        {
            File.WriteAllText(rawSpecPath, "{\"openapi\":}", new UTF8Encoding(false));
            SourceGeneratorGenerationProvider provider = CreateProvider();

            GenerationResult result = provider.Generate(CreateRequest());

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Does.Contain("JSON"));
            Assert.That(
                File.Exists(Path.Combine(outputFolder, "PetStoreApi.OpenApiDefinition.cs")),
                Is.False);
            Assert.That(compilationRequestCount, Is.Zero);
            Assert.That(definitionImports, Is.Empty);
        }

        [Test]
        public void InvalidUtf8YamlReportsDiagnosticAndDoesNotPublishOrRequestCompilation()
        {
            string yamlPath = Path.Combine(projectRoot, "Assets", "Specs", "petstore.yaml");
            File.WriteAllBytes(yamlPath, new byte[] { 0x80 });
            SourceGeneratorGenerationProvider provider = CreateProvider();
            GenerationRequest request = new GenerationRequest(
                yamlPath,
                outputFolder,
                "PetStoreApi",
                "Example.Generated.PetStore");

            GenerationResult result = provider.Generate(request);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Does.Contain("not valid UTF-8"));
            string sourcePath = "Assets/Specs/petstore.yaml";
            Assert.That(
                CountOccurrences(result.Message, "YAML001"),
                Is.EqualTo(1),
                "Actual message: " + result.Message);
            Assert.That(
                CountOccurrences(result.Message, sourcePath + ":1:1"),
                Is.EqualTo(1),
                "Actual message: " + result.Message);
            Assert.That(
                File.Exists(Path.Combine(outputFolder, "PetStoreApi.OpenApiDefinition.cs")),
                Is.False);
            Assert.That(
                Directory.Exists(Path.Combine(
                    projectRoot,
                    NormalizedSpecBundleConstants.AuthoritativeCacheRelativePath)),
                Is.False);
            Assert.That(mirrorImporter.ImportedAssetPaths, Is.Empty);
            Assert.That(definitionImports, Is.Empty);
            Assert.That(compilationRequestCount, Is.Zero);
        }

        [Test]
        public void ForeignDefinitionStopsBeforeCachePublication()
        {
            string definitionPath = Path.Combine(
                outputFolder,
                "PetStoreApi.OpenApiDefinition.cs");
            File.WriteAllText(
                definitionPath,
                "public partial class PetStoreApi {}",
                new UTF8Encoding(false));
            SourceGeneratorGenerationProvider provider = CreateProvider();

            GenerationResult result = provider.Generate(CreateRequest());

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Does.Contain("not owned"));
            Assert.That(
                Directory.Exists(Path.Combine(
                    projectRoot,
                    NormalizedSpecBundleConstants.CompilerMirrorRelativePath)),
                Is.False);
            Assert.That(compilationRequestCount, Is.Zero);
        }

        [Test]
        public void GenerateAsyncPropagatesCancellationBeforePublishing()
        {
            SourceGeneratorGenerationProvider provider = CreateProvider();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.CatchAsync<OperationCanceledException>(
                async () => await provider.GenerateAsync(CreateRequest(), cancellation.Token));

            Assert.That(compilationRequestCount, Is.Zero);
            Assert.That(definitionImports, Is.Empty);
        }

        SourceGeneratorGenerationProvider CreateProvider()
        {
            var cacheService = new NormalizedSpecCacheService(
                projectRoot,
                new RawJsonNormalizer(),
                new AtomicFileWriter(),
                mirrorImporter);
            var definitionWriter = new OpenApiClientDefinitionWriter(
                projectRoot,
                new AtomicFileWriter(),
                definitionImports.Add);
            return new SourceGeneratorGenerationProvider(
                GenerationProviderAvailability.Available(),
                cacheService,
                definitionWriter,
                () => compilationRequestCount++);
        }

        GenerationRequest CreateRequest()
        {
            return new GenerationRequest(
                rawSpecPath,
                outputFolder,
                "PetStoreApi",
                "Example.Generated.PetStore");
        }

        static string ReadSpecId(string message)
        {
            const string Prefix = "Spec ID: ";
            int start = message.IndexOf(Prefix, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0));
            return message.Substring(start + Prefix.Length).Trim();
        }

        static void WriteAsmdef(string directory, string assemblyName, string reference)
        {
            Directory.CreateDirectory(directory);
            string json = string.Format(
                CultureInfo.InvariantCulture,
                "{{\n  \"name\": \"{0}\",\n  \"references\": [\"{1}\"]\n}}\n",
                assemblyName,
                reference);
            File.WriteAllText(
                Path.Combine(directory, assemblyName + ".asmdef"),
                json,
                new UTF8Encoding(false));
        }

        static int CountOccurrences(string value, string substring)
        {
            int count = 0;
            int offset = 0;
            while ((offset = value.IndexOf(substring, offset, StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += substring.Length;
            }

            return count;
        }

        sealed class RecordingMirrorImporter : ICompilerMirrorImporter
        {
            internal List<string> ImportedAssetPaths { get; } = new List<string>();

            public void Import(string mirrorAssetPath)
            {
                ImportedAssetPaths.Add(mirrorAssetPath);
            }
        }
    }
}
