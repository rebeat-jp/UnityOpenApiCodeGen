#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
        public void PublicationJournalRemainsAwaitingCompilationUntilTheCompilationRequestReturns()
        {
            string markerAtCompilationRequest = string.Empty;
            var cacheRoot = Path.Combine(
                projectRoot,
                NormalizedSpecBundleConstants.AuthoritativeCacheRelativePath);
            SourceGeneratorGenerationProvider provider = CreateProvider(
                requestCompilation: () =>
                {
                    compilationRequestCount++;
                    string[] markers = Directory.Exists(cacheRoot)
                        ? Directory.GetFiles(
                            cacheRoot,
                            NormalizedSpecBundleConstants.PublishPendingFileName,
                            SearchOption.AllDirectories)
                        : Array.Empty<string>();
                    Assert.That(markers, Has.Length.EqualTo(1));
                    markerAtCompilationRequest = File.ReadAllText(markers[0]);
                });

            GenerationResult result = provider.Generate(CreateRequest());

            Assert.That(result.IsSuccess, Is.True, result.Message);
            Assert.That(
                markerAtCompilationRequest,
                Does.Contain("state=published-awaiting-compilation\n"));
            Assert.That(
                Directory.Exists(cacheRoot),
                Is.True);
            Assert.That(
                Directory.GetFiles(
                    cacheRoot,
                    NormalizedSpecBundleConstants.PublishPendingFileName,
                    SearchOption.AllDirectories),
                Is.Empty);
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

        [Test]
        public void NamespaceChangePreservesSpecIdAndReusesExistingCacheAndMirror()
        {
            SourceGeneratorGenerationProvider provider = CreateProvider();
            GenerationResult first = provider.Generate(CreateRequest());
            Assert.That(first.IsSuccess, Is.True, first.Message);
            string specId = ReadSpecId(first.Message);
            string definitionPath = Path.Combine(
                outputFolder,
                "PetStoreApi.OpenApiDefinition.cs");
            string mirrorPath = Path.Combine(
                projectRoot,
                NormalizedSpecBundleConstants.CompilerMirrorRelativePath,
                specId + NormalizedSpecBundleConstants.AdditionalFileSuffix);
            byte[] mirrorBytes = File.ReadAllBytes(mirrorPath);
            File.WriteAllText(
                definitionPath + ".meta",
                "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\n",
                new UTF8Encoding(false));
            string metaBefore = File.ReadAllText(definitionPath + ".meta");
            mirrorImporter.ImportedAssetPaths.Clear();
            definitionImports.Clear();

            GenerationResult renamed = provider.Generate(CreateRequest(
                "Example.Renamed.PetStore"));

            Assert.That(renamed.IsSuccess, Is.True, renamed.Message);
            Assert.That(ReadSpecId(renamed.Message), Is.EqualTo(specId));
            Assert.That(
                File.ReadAllText(definitionPath),
                Does.Contain("namespace Example.Renamed.PetStore"));
            Assert.That(File.ReadAllText(definitionPath + ".meta"), Is.EqualTo(metaBefore));
            Assert.That(File.ReadAllBytes(mirrorPath), Is.EqualTo(mirrorBytes));
            Assert.That(mirrorImporter.ImportedAssetPaths, Is.Empty);
            Assert.That(definitionImports, Is.EqualTo(new[]
            {
                "Assets/Clients/PetStore/PetStoreApi.OpenApiDefinition.cs",
            }));
            Assert.That(compilationRequestCount, Is.EqualTo(2));
        }

        [Test]
        public void OutputFolderChangeMovesDefinitionAndMetaWithStableSpecId()
        {
            SourceGeneratorGenerationProvider provider = CreateProvider();
            GenerationResult first = provider.Generate(CreateRequest());
            Assert.That(first.IsSuccess, Is.True, first.Message);
            string specId = ReadSpecId(first.Message);
            string originalDefinition = Path.Combine(
                outputFolder,
                "PetStoreApi.OpenApiDefinition.cs");
            byte[] metaBytes = new UTF8Encoding(false).GetBytes(
                "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\n");
            File.WriteAllBytes(originalDefinition + ".meta", metaBytes);
            string movedOutput = Path.Combine(projectRoot, "Assets", "Clients", "Moved");
            definitionImports.Clear();
            mirrorImporter.ImportedAssetPaths.Clear();
            string migrationMarker = string.Empty;
            provider = CreateProvider(requestCompilation: () =>
            {
                compilationRequestCount++;
                string cacheDirectory = Path.Combine(
                    projectRoot,
                    NormalizedSpecBundleConstants.AuthoritativeCacheRelativePath,
                    specId);
                migrationMarker = File.ReadAllText(Path.Combine(
                    cacheDirectory,
                    NormalizedSpecBundleConstants.PublishPendingFileName));
            });

            GenerationResult moved = provider.Generate(CreateRequest(
                generatedNamespace: "Example.Generated.PetStore",
                outputFolderOverride: movedOutput));

            string movedDefinition = Path.Combine(
                movedOutput,
                "PetStoreApi.OpenApiDefinition.cs");
            Assert.That(moved.IsSuccess, Is.True, moved.Message);
            Assert.That(ReadSpecId(moved.Message), Is.EqualTo(specId));
            Assert.That(File.Exists(originalDefinition), Is.False);
            Assert.That(File.Exists(originalDefinition + ".meta"), Is.False);
            Assert.That(File.Exists(movedDefinition), Is.True);
            Assert.That(File.ReadAllBytes(movedDefinition + ".meta"), Is.EqualTo(metaBytes));
            Assert.That(mirrorImporter.ImportedAssetPaths, Is.Empty);
            Assert.That(definitionImports, Is.EqualTo(new[]
            {
                "Assets/Clients/Moved/PetStoreApi.OpenApiDefinition.cs",
            }));
            Assert.That(migrationMarker, Does.Contain("version=3\n"));
            Assert.That(migrationMarker, Does.Contain("count=7\n"));
            Assert.That(compilationRequestCount, Is.EqualTo(2));
        }

        [Test]
        public void OutputFolderMigrationImportFailureRestoresOldPathAndMeta()
        {
            SourceGeneratorGenerationProvider initialProvider = CreateProvider();
            GenerationResult first = initialProvider.Generate(CreateRequest());
            Assert.That(first.IsSuccess, Is.True, first.Message);
            string originalDefinition = Path.Combine(
                outputFolder,
                "PetStoreApi.OpenApiDefinition.cs");
            byte[] definitionBefore = File.ReadAllBytes(originalDefinition);
            byte[] metaBefore = new UTF8Encoding(false).GetBytes(
                "fileFormatVersion: 2\nguid: abcdef0123456789abcdef0123456789\n");
            File.WriteAllBytes(originalDefinition + ".meta", metaBefore);
            string movedOutput = Path.Combine(projectRoot, "Assets", "Clients", "Moved");
            string movedDefinition = Path.Combine(
                movedOutput,
                "PetStoreApi.OpenApiDefinition.cs");
            SourceGeneratorGenerationProvider failingProvider = CreateProvider(
                importDefinition: _ => throw new IOException("Simulated definition import failure."));

            GenerationResult failed = failingProvider.Generate(CreateRequest(
                outputFolderOverride: movedOutput));

            Assert.That(failed.IsSuccess, Is.False);
            Assert.That(File.ReadAllBytes(originalDefinition), Is.EqualTo(definitionBefore));
            Assert.That(File.ReadAllBytes(originalDefinition + ".meta"), Is.EqualTo(metaBefore));
            Assert.That(File.Exists(movedDefinition), Is.False);
            Assert.That(File.Exists(movedDefinition + ".meta"), Is.False);
        }

        [Test]
        public void OutputFolderMigrationImportEditThenThrowPreservesMovedDefinitionGroup()
        {
            GenerationResult first = CreateProvider().Generate(CreateRequest());
            Assert.That(first.IsSuccess, Is.True, first.Message);
            string specId = ReadSpecId(first.Message);
            string originalDefinition = Path.Combine(
                outputFolder,
                "PetStoreApi.OpenApiDefinition.cs");
            byte[] metaBytes = new UTF8Encoding(false).GetBytes(
                "fileFormatVersion: 2\nguid: abcdef0123456789abcdef0123456789\n");
            File.WriteAllBytes(originalDefinition + ".meta", metaBytes);
            string movedOutput = Path.Combine(projectRoot, "Assets", "Clients", "Moved");
            string movedDefinition = Path.Combine(
                movedOutput,
                "PetStoreApi.OpenApiDefinition.cs");
            byte[] concurrentBytes = new UTF8Encoding(false).GetBytes("// concurrent user edit\n");
            SourceGeneratorGenerationProvider failingProvider = CreateProvider(
                importDefinition: _ =>
                {
                    File.WriteAllBytes(movedDefinition, concurrentBytes);
                    throw new IOException("Simulated import-time external edit.");
                });

            GenerationResult failed = failingProvider.Generate(CreateRequest(
                outputFolderOverride: movedOutput));

            Assert.That(failed.IsSuccess, Is.False);
            Assert.That(File.Exists(originalDefinition), Is.False);
            Assert.That(File.Exists(originalDefinition + ".meta"), Is.False);
            Assert.That(File.ReadAllBytes(movedDefinition), Is.EqualTo(concurrentBytes));
            Assert.That(File.ReadAllBytes(movedDefinition + ".meta"), Is.EqualTo(metaBytes));

            var recovery = new NormalizedSpecCacheService(
                projectRoot,
                new RawJsonNormalizer(),
                new AtomicFileWriter(),
                mirrorImporter);
            recovery.CompilationRequester = () => { };
            recovery.RepairPendingPublicationForSpec(
                specId,
                movedDefinition,
                "Assets/Clients/Moved/PetStoreApi.OpenApiDefinition.cs");

            Assert.That(File.Exists(originalDefinition), Is.False);
            Assert.That(File.Exists(originalDefinition + ".meta"), Is.False);
            Assert.That(File.ReadAllBytes(movedDefinition), Is.EqualTo(concurrentBytes));
            Assert.That(File.ReadAllBytes(movedDefinition + ".meta"), Is.EqualTo(metaBytes));
        }

        [Test]
        public void NamespaceChangeFailureRestoresDefinitionCacheAndMirror()
        {
            SourceGeneratorGenerationProvider initialProvider = CreateProvider();
            GenerationResult first = initialProvider.Generate(CreateRequest());
            Assert.That(first.IsSuccess, Is.True, first.Message);
            string specId = ReadSpecId(first.Message);
            string definitionPath = Path.Combine(
                outputFolder,
                "PetStoreApi.OpenApiDefinition.cs");
            string cachePath = Path.Combine(
                projectRoot,
                NormalizedSpecBundleConstants.AuthoritativeCacheRelativePath,
                specId,
                NormalizedSpecBundleConstants.BundleFileName);
            string mirrorPath = Path.Combine(
                projectRoot,
                NormalizedSpecBundleConstants.CompilerMirrorRelativePath,
                specId + NormalizedSpecBundleConstants.AdditionalFileSuffix);
            byte[] definitionBefore = File.ReadAllBytes(definitionPath);
            byte[] cacheBefore = File.ReadAllBytes(cachePath);
            byte[] mirrorBefore = File.ReadAllBytes(mirrorPath);
            mirrorImporter.ImportedAssetPaths.Clear();
            definitionImports.Clear();
            SourceGeneratorGenerationProvider failingProvider = CreateProvider(
                importDefinition: _ => throw new IOException("Simulated definition import failure."));

            GenerationResult failed = failingProvider.Generate(CreateRequest(
                "Example.Renamed.PetStore"));

            Assert.That(failed.IsSuccess, Is.False);
            Assert.That(File.ReadAllBytes(definitionPath), Is.EqualTo(definitionBefore));
            Assert.That(File.ReadAllBytes(cachePath), Is.EqualTo(cacheBefore));
            Assert.That(File.ReadAllBytes(mirrorPath), Is.EqualTo(mirrorBefore));
            Assert.That(mirrorImporter.ImportedAssetPaths, Is.Empty);
            Assert.That(definitionImports, Is.Empty);
            Assert.That(compilationRequestCount, Is.EqualTo(1));
        }

        [Test]
        public async Task AsyncFormatDetectionDoesNotAdoptDefinitionChangedAfterInitialPrepare()
        {
            const string Yaml =
                "openapi: 3.0.3\ninfo:\n  title: Pet Store\n  version: 1.0.0\npaths: {}\n";
            using var server = new OneShotHttpServer(Yaml, "application/yaml");
            SourceGeneratorGenerationProvider provider = CreateProvider();
            var request = new GenerationRequest(
                server.Url,
                outputFolder,
                "PetStoreApi",
                "Example.Generated.PetStore");
            string definitionPath = Path.Combine(
                outputFolder,
                "PetStoreApi.OpenApiDefinition.cs");
            byte[] competingBytes = Array.Empty<byte>();
            string competingSpecId = string.Empty;
            bool injected = false;
            SetProgressReporter(request, value =>
            {
                if (value != 0.75 || injected)
                {
                    return;
                }

                injected = true;
                var competingWriter = new OpenApiClientDefinitionWriter(
                    projectRoot,
                    new AtomicFileWriter(),
                    definitionImports.Add);
                OpenApiClientDefinitionPlan competingPlan = competingWriter.Prepare(
                    outputFolder,
                    "PetStoreApi",
                    "Example.Competing.PetStore",
                    OpenApiDocumentFormat.Yaml);
                Assert.That(competingWriter.Publish(competingPlan), Is.True);
                competingSpecId = competingPlan.SpecId;
                competingBytes = File.ReadAllBytes(definitionPath);
            });

            GenerationResult result = await provider.GenerateAsync(
                request,
                CancellationToken.None);

            Assert.That(injected, Is.True);
            Assert.That(result.IsSuccess, Is.False, result.Message);
            Assert.That(File.ReadAllBytes(definitionPath), Is.EqualTo(competingBytes));
            Assert.That(
                File.ReadAllText(definitionPath),
                Does.Contain("\"" + competingSpecId + "\""));
            Assert.That(compilationRequestCount, Is.Zero);
        }

        [Test]
        public void MirrorImporterDefinitionChangeIsNotDestroyedByTransactionRollbackOrRepair()
        {
            var originalWriter = new OpenApiClientDefinitionWriter(
                projectRoot,
                new AtomicFileWriter(),
                definitionImports.Add);
            OpenApiClientDefinitionPlan originalPlan = originalWriter.Prepare(
                outputFolder,
                "PetStoreApi",
                "Example.Generated.PetStore");
            string definitionPath = originalPlan.DefinitionPath;
            byte[] competingBytes = Array.Empty<byte>();
            string competingSpecId = string.Empty;
            mirrorImporter.OnImport = _ =>
            {
                mirrorImporter.OnImport = null;
                var competingWriter = new OpenApiClientDefinitionWriter(
                    projectRoot,
                    new AtomicFileWriter(),
                    definitionImports.Add);
                OpenApiClientDefinitionPlan competingPlan = competingWriter.Prepare(
                    outputFolder,
                    "PetStoreApi",
                    "Example.Competing.PetStore");
                Assert.That(competingWriter.Publish(competingPlan), Is.True);
                competingSpecId = competingPlan.SpecId;
                competingBytes = File.ReadAllBytes(definitionPath);
            };

            GenerationResult result = CreateProvider().Generate(CreateRequest());

            Assert.That(result.IsSuccess, Is.False, result.Message);
            Assert.That(competingSpecId, Is.Not.EqualTo(originalPlan.SpecId));
            Assert.That(File.ReadAllBytes(definitionPath), Is.EqualTo(competingBytes));

            string replacementSpecId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            string competingSource = new UTF8Encoding(false).GetString(competingBytes);
            byte[] editedAgainBytes = new UTF8Encoding(false).GetBytes(
                competingSource.Replace(competingSpecId, replacementSpecId));
            File.WriteAllBytes(definitionPath, editedAgainBytes);

            mirrorImporter.OnImport = null;
            var repairService = new NormalizedSpecCacheService(
                projectRoot,
                new RawJsonNormalizer(),
                new AtomicFileWriter(),
                mirrorImporter);
            repairService.CompilationRequester = () => { };
            repairService.RepairPendingPublicationForSpec(
                originalPlan.SpecId,
                definitionPath,
                originalPlan.DefinitionAssetPath);

            Assert.That(File.ReadAllBytes(definitionPath), Is.EqualTo(editedAgainBytes));
            Assert.That(
                File.ReadAllText(definitionPath),
                Does.Contain("\"" + replacementSpecId + "\""));
            Assert.That(compilationRequestCount, Is.Zero);
        }

        [TestCase("ftp://example.test/openapi.json", "local paths and HTTP(S) URLs only")]
        [TestCase("Assets/Specs/openapi.txt", "supports local .json, .yaml, and .yml documents, or HTTP(S) URLs")]
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
        public void InvalidUtf8YamlReportsFailureAndDoesNotPublishOrRequestCompilation()
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
            Assert.That(
                result.Message,
                Does.Contain("JSON Pointer ''"),
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

        [Test]
        public void GenerateAsyncCancellationAfterGraphLoadingPreservesPublishedArtifacts()
        {
            SourceGeneratorGenerationProvider provider = CreateProvider();
            GenerationResult first = provider.Generate(CreateRequest());
            Assert.That(first.IsSuccess, Is.True, first.Message);
            File.WriteAllText(rawSpecPath,
                "{\"openapi\":\"3.0.3\",\"info\":{\"version\":\"changed\"}}",
                new UTF8Encoding(false));
            var publishedFiles = new Dictionary<string, byte[]>();
            foreach (string path in Directory.GetFiles(projectRoot, "*", SearchOption.AllDirectories))
                publishedFiles.Add(path, File.ReadAllBytes(path));
            mirrorImporter.ImportedAssetPaths.Clear();
            definitionImports.Clear();
            compilationRequestCount = 0;
            using var cancellation = new CancellationTokenSource();
            bool reachedPostLoadProgress = false;
            GenerationRequest request = CreateRequest();
            // Progress is an internal contract in a different assembly. Close the generic
            // synchronous test reporter over that contract without widening production access.
            var progressProperty = typeof(GenerationRequest).GetProperty("Progress",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Type progressType = progressProperty.PropertyType.GetGenericArguments()[0];
            Action<object> onProgress = progress =>
            {
                double value = (double)progressType.GetProperty("Value",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .GetValue(progress);
                if (value == 0.75)
                {
                    reachedPostLoadProgress = true;
                    cancellation.Cancel();
                }
            };
            object reporter = Activator.CreateInstance(typeof(ImmediateProgress<>).MakeGenericType(progressType),
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic, null, new object[] { onProgress }, null);
            progressProperty.SetValue(request, reporter);

            Assert.CatchAsync<OperationCanceledException>(
                async () => await provider.GenerateAsync(request, cancellation.Token));

            Assert.That(reachedPostLoadProgress, Is.True);
            Assert.That(Directory.GetFiles(projectRoot, "*", SearchOption.AllDirectories),
                Is.EquivalentTo(publishedFiles.Keys));
            foreach (KeyValuePair<string, byte[]> file in publishedFiles)
                Assert.That(File.ReadAllBytes(file.Key), Is.EqualTo(file.Value), file.Key);
            Assert.That(mirrorImporter.ImportedAssetPaths, Is.Empty);
            Assert.That(definitionImports, Is.Empty);
            Assert.That(compilationRequestCount, Is.Zero);
        }

        [Test]
        public void ConcurrentGenerationReportsSafeLockFailureWithoutProjectPath()
        {
            var cache = new NormalizedSpecCacheService(projectRoot, new RawJsonNormalizer(),
                new AtomicFileWriter(), mirrorImporter);
            using (cache.AcquireGenerationLock())
            {
                GenerationResult result = CreateProvider().Generate(CreateRequest());
                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.Message, Does.Contain("Another Unity process"));
                Assert.That(result.Message, Does.Not.Contain(projectRoot));
            }
        }

        [Test]
        public void UnexpectedCompilationFailureDoesNotExposeExceptionDetails()
        {
            GenerationResult result = CreateProvider(() =>
                throw new InvalidOperationException("secret-token " + projectRoot)).Generate(CreateRequest());
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Is.EqualTo("Source Generator generation failed."));
        }

        SourceGeneratorGenerationProvider CreateProvider(
            Action? requestCompilation = null,
            Action<string>? importDefinition = null)
        {
            var cacheService = new NormalizedSpecCacheService(
                projectRoot,
                new RawJsonNormalizer(),
                new AtomicFileWriter(),
                mirrorImporter);
            var definitionWriter = new OpenApiClientDefinitionWriter(
                projectRoot,
                new AtomicFileWriter(),
                importDefinition ?? definitionImports.Add);
            return new SourceGeneratorGenerationProvider(
                GenerationProviderAvailability.Available(),
                cacheService,
                definitionWriter,
                requestCompilation ?? (() => compilationRequestCount++));
        }

        GenerationRequest CreateRequest(
            string generatedNamespace = "Example.Generated.PetStore",
            string? outputFolderOverride = null)
        {
            return new GenerationRequest(
                rawSpecPath,
                outputFolderOverride ?? outputFolder,
                "PetStoreApi",
                generatedNamespace);
        }

        static string ReadSpecId(string message)
        {
            const string Prefix = "Spec ID: ";
            int start = message.IndexOf(Prefix, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0));
            return message.Substring(start + Prefix.Length).Trim();
        }

        static void SetProgressReporter(
            GenerationRequest request,
            Action<double> onProgress)
        {
            var progressProperty = typeof(GenerationRequest).GetProperty(
                "Progress",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            Type progressType = progressProperty.PropertyType.GetGenericArguments()[0];
            Action<object> report = progress => onProgress((double)progressType.GetProperty(
                    "Value",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                .GetValue(progress));
            object reporter = Activator.CreateInstance(
                typeof(ImmediateProgress<>).MakeGenericType(progressType),
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic,
                null,
                new object[] { report },
                null);
            progressProperty.SetValue(request, reporter);
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

        sealed class RecordingMirrorImporter : ICompilerMirrorImporter
        {
            internal List<string> ImportedAssetPaths { get; } = new List<string>();

            internal Action<string>? OnImport { get; set; }

            public void Import(string mirrorAssetPath)
            {
                ImportedAssetPaths.Add(mirrorAssetPath);
                OnImport?.Invoke(mirrorAssetPath);
            }
        }

        sealed class OneShotHttpServer : IDisposable
        {
            readonly TcpListener listener;
            readonly Task serverTask;
            readonly byte[] responseBody;
            readonly string contentType;

            internal OneShotHttpServer(string responseBody, string contentType)
            {
                this.responseBody = new UTF8Encoding(false).GetBytes(responseBody);
                this.contentType = contentType;
                listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Url = "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) +
                    "/openapi";
                serverTask = Task.Run(ServeOnceAsync);
            }

            internal string Url { get; }

            public void Dispose()
            {
                listener.Stop();
                try
                {
                    serverTask.GetAwaiter().GetResult();
                }
                catch (SocketException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            }

            async Task ServeOnceAsync()
            {
                using TcpClient client = await listener.AcceptTcpClientAsync();
                using NetworkStream stream = client.GetStream();
                using (var reader = new StreamReader(
                    stream,
                    Encoding.ASCII,
                    false,
                    1024,
                    true))
                {
                    string line;
                    do
                    {
                        line = await reader.ReadLineAsync();
                    }
                    while (!string.IsNullOrEmpty(line));
                }

                string headers =
                    "HTTP/1.1 200 OK\r\n" +
                    "Content-Type: " + contentType + "\r\n" +
                    "Content-Length: " + responseBody.Length.ToString(CultureInfo.InvariantCulture) + "\r\n" +
                    "Connection: close\r\n\r\n";
                byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
                await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
                await stream.WriteAsync(responseBody, 0, responseBody.Length);
            }
        }

        sealed class ImmediateProgress<T> : IProgress<T>
        {
            readonly Action<object> report;

            internal ImmediateProgress(Action<object> report)
            {
                this.report = report;
            }

            public void Report(T value) => report(value!);
        }
    }
}
