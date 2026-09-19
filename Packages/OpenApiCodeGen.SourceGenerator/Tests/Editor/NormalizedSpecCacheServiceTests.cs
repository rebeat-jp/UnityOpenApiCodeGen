using System;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using Rhycol.OpenApiCodeGen.SourceGenerator;
using Rhycol.OpenApiCodeGen.SourceGenerator.Editor;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    internal sealed class NormalizedSpecCacheServiceTests
    {
        private const string SpecId = "0123456789abcdef0123456789abcdef";
        private string projectRoot;
        private RecordingImporter importer;
        private int compilationRequestCount;

        [SetUp]
        public void SetUp()
        {
            projectRoot = Path.Combine(Path.GetTempPath(), "OpenApiCodeGenSourceGeneratorTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(projectRoot, "Assets", "Specs"));
            importer = new RecordingImporter();
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
        public void NormalizeAndCacheCreatesByteIdenticalAuthoritativeAndMirrorFiles()
        {
            WriteRaw("{\"openapi\":\"3.0.3\"}");

            NormalizedSpecCacheResult result = CreateService().NormalizeAndCache("Assets/Specs/openapi.json", SpecId);

            Assert.That(result.SourcePath, Is.EqualTo("Assets/Specs/openapi.json"));
            Assert.That(result.AuthoritativeChanged, Is.True);
            Assert.That(result.MirrorChanged, Is.True);
            Assert.That(File.ReadAllBytes(result.MirrorPath), Is.EqualTo(File.ReadAllBytes(result.AuthoritativePath)));
            Assert.That(Path.GetFileName(result.MirrorPath),
                Is.EqualTo(SpecId + NormalizedSpecBundleConstants.AdditionalFileSuffix));
            Assert.That(importer.ImportedAssetPaths, Is.EqualTo(new[] { result.MirrorAssetPath }));
        }

        [Test]
        public void NormalizeAndCacheSupportsYamlAndPreservesFormatAndLocations()
        {
            WriteRawAt(
                "Assets/Specs/openapi.yaml",
                "openapi: 3.0.3\ninfo:\n  title: Sample\n  version: 1.0.0\npaths: {}\n");

            NormalizedSpecCacheResult result = CreateService().NormalizeAndCache(
                "Assets/Specs/openapi.yaml",
                SpecId,
                OpenApiDocumentFormat.Yaml);
            string canonical = Encoding.UTF8.GetString(File.ReadAllBytes(result.AuthoritativePath));

            Assert.That(result.Format, Is.EqualTo(OpenApiDocumentFormat.Yaml));
            Assert.That(canonical, Does.Contain("\"name\": \"openapi\""));
            Assert.That(canonical, Does.Contain("\"line\": 1"));
            Assert.That(canonical, Does.Contain("\"sourcePath\": \"Assets/Specs/openapi.yaml\""));
            Assert.That(File.ReadAllBytes(result.MirrorPath), Is.EqualTo(File.ReadAllBytes(result.AuthoritativePath)));
        }

        [Test]
        public void InvalidYamlDoesNotReplaceLastKnownGoodCacheOrMirror()
        {
            WriteRawAt("Assets/Specs/openapi.yaml", "openapi: 3.0.3\npaths: {}\n");
            NormalizedSpecCacheService service = CreateService();
            NormalizedSpecCacheResult valid = service.NormalizeAndCache(
                "Assets/Specs/openapi.yaml",
                SpecId,
                OpenApiDocumentFormat.Yaml);
            byte[] authoritativeBytes = File.ReadAllBytes(valid.AuthoritativePath);
            byte[] mirrorBytes = File.ReadAllBytes(valid.MirrorPath);
            File.WriteAllText(
                Path.Combine(projectRoot, "Assets", "Specs", "openapi.yaml"),
                "openapi: [\n",
                new UTF8Encoding(false));

            NormalizedSpecException exception = Assert.Throws<NormalizedSpecException>(
                () => service.NormalizeAndCache(
                    "Assets/Specs/openapi.yaml",
                    SpecId,
                    OpenApiDocumentFormat.Yaml));

            Assert.That(exception.DiagnosticCode, Does.StartWith("YAML"));
            Assert.That(File.ReadAllBytes(valid.AuthoritativePath), Is.EqualTo(authoritativeBytes));
            Assert.That(File.ReadAllBytes(valid.MirrorPath), Is.EqualTo(mirrorBytes));
        }

        [Test]
        public void NormalizeAndCacheDoesNotRewriteByteIdenticalFiles()
        {
            WriteRaw("{\"openapi\":\"3.0.3\"}");
            NormalizedSpecCacheService service = CreateService();
            NormalizedSpecCacheResult first = service.NormalizeAndCache("Assets/Specs/openapi.json", SpecId);
            var authoritativeTimestamp = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            var mirrorTimestamp = new DateTime(2002, 3, 4, 5, 6, 7, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(first.AuthoritativePath, authoritativeTimestamp);
            File.SetLastWriteTimeUtc(first.MirrorPath, mirrorTimestamp);
            importer.ImportedAssetPaths.Clear();

            NormalizedSpecCacheResult second = service.NormalizeAndCache("Assets/Specs/openapi.json", SpecId);

            Assert.That(second.AuthoritativeChanged, Is.False);
            Assert.That(second.MirrorChanged, Is.False);
            Assert.That(File.GetLastWriteTimeUtc(second.AuthoritativePath), Is.EqualTo(authoritativeTimestamp));
            Assert.That(File.GetLastWriteTimeUtc(second.MirrorPath), Is.EqualTo(mirrorTimestamp));
            Assert.That(importer.ImportedAssetPaths, Is.Empty);
        }

        [Test]
        public void GenerationLockRejectsASecondOwnerAndCanBeReacquiredAfterRelease()
        {
            NormalizedSpecCacheService service = CreateService();
            using (IDisposable first = service.AcquireGenerationLock())
            {
                Assert.Throws<SafeGenerationException>(
                    () => service.AcquireGenerationLock());
                Assert.Throws<SafeGenerationException>(
                    () => CreateService().AcquireGenerationLock());
                var caseAliasedService = new NormalizedSpecCacheService(projectRoot.ToUpperInvariant(),
                    new RawJsonNormalizer(), new AtomicFileWriter(), importer);
                Assert.Throws<SafeGenerationException>(() => caseAliasedService.AcquireGenerationLock());
            }

            using (IDisposable second = service.AcquireGenerationLock())
            {
                Assert.That(second, Is.Not.Null);
            }
        }

        [Test]
        public void DisposingAnOldGenerationLockAgainDoesNotReleaseItsReplacement()
        {
            IDisposable first = CreateService().AcquireGenerationLock();
            first.Dispose();
            using (IDisposable second = CreateService().AcquireGenerationLock())
            {
                first.Dispose();
                Assert.Throws<SafeGenerationException>(() => CreateService().AcquireGenerationLock());
            }
            using (IDisposable third = CreateService().AcquireGenerationLock())
                Assert.That(third, Is.Not.Null);
        }

        [Test]
        public void ConcurrentThreadsAcquireOnlyOneGenerationLock()
        {
            using (var start = new ManualResetEventSlim(false))
            {
                var acquisitions = new System.Threading.Tasks.Task<IDisposable>[2];
                for (int index = 0; index < acquisitions.Length; index++)
                {
                    acquisitions[index] = System.Threading.Tasks.Task.Run(() =>
                    {
                        start.Wait();
                        try { return CreateService().AcquireGenerationLock(); }
                        catch (SafeGenerationException) { return null; }
                    });
                }
                start.Set();
                try
                {
                    Assert.That(System.Threading.Tasks.Task.WaitAll(acquisitions, 30000), Is.True);
                    int owners = 0;
                    foreach (var acquisition in acquisitions)
                        if (acquisition.Result != null) owners++;
                    Assert.That(owners, Is.EqualTo(1));
                }
                finally
                {
                    foreach (var acquisition in acquisitions)
                        if (acquisition.Status == System.Threading.Tasks.TaskStatus.RanToCompletion)
                            acquisition.Result?.Dispose();
                }
            }
            using (IDisposable generationLock = CreateService().AcquireGenerationLock())
                Assert.That(generationLock, Is.Not.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        [UnityEngine.TestTools.UnityPlatform(UnityEngine.RuntimePlatform.OSXEditor,
            UnityEngine.RuntimePlatform.LinuxEditor)]
        public void GenerationLockExcludesAnotherProcessAndReleasesOnExit(bool terminateOwner)
        {
            string applicationContentsPath = UnityEditor.EditorApplication.applicationContentsPath;
            string monoRoot = FindUnityDirectory(
                applicationContentsPath,
                "MonoBleedingEdge",
                Path.Combine("Resources", "Scripting", "MonoBleedingEdge"));
            string monoPath = Path.Combine(monoRoot, "bin", "mono");
            string csiPath = Path.Combine(monoRoot, "lib", "mono", "4.5", "csi.exe");
            Assert.That(File.Exists(monoPath), Is.True, "Unity's bundled Mono runtime is required.");
            Assert.That(File.Exists(csiPath), Is.True, "Unity's bundled C# interpreter is required.");
            string managedRoot = FindUnityDirectory(
                applicationContentsPath,
                "Managed",
                Path.Combine("Resources", "Scripting", "Managed"));
            string unityEngineManagedRoot = Path.Combine(managedRoot, "UnityEngine");
            Assert.That(Directory.Exists(managedRoot), Is.True,
                "Unity's bundled managed assemblies are required.");
            Assert.That(Directory.Exists(unityEngineManagedRoot), Is.True,
                "UnityEngine's bundled managed assemblies are required.");
            string scriptPath = Path.Combine(projectRoot, "lock-owner.csx");
            string readyPath = Path.Combine(projectRoot, "lock-owner.ready");
            string releasePath = Path.Combine(projectRoot, "lock-owner.release");
            // Run the actual service method from the freshly compiled Editor assembly. Only
            // projectRoot is used by this method, so bypass unrelated Unity-native dependencies.
            File.WriteAllText(scriptPath, @"
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
var assembly = Assembly.LoadFrom(Args[0]);
var type = assembly.GetType(""Rhycol.OpenApiCodeGen.SourceGenerator.Editor.NormalizedSpecCacheService"", true);
var service = FormatterServices.GetUninitializedObject(type);
type.GetField(""projectRoot"", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(service, Args[1]);
var acquire = type.GetMethod(""AcquireGenerationLock"", BindingFlags.Instance | BindingFlags.NonPublic);
using ((IDisposable)acquire.Invoke(service, null))
{
    try
    {
        using ((IDisposable)acquire.Invoke(service, null)) { }
        throw new Exception(""The same-process owner was not rejected."");
    }
    catch (TargetInvocationException exception)
    {
        if (exception.InnerException.GetType().Name != ""SafeGenerationException"") throw;
    }
    File.WriteAllText(Args[2], ""locked"");
    while (!File.Exists(Args[3])) Thread.Sleep(20);
}
", new UTF8Encoding(false));
            string assemblyPath = typeof(NormalizedSpecCacheService).Assembly.Location;
            var start = new System.Diagnostics.ProcessStartInfo(monoPath,
                QuoteProcessArgument(csiPath) + " -- " + QuoteProcessArgument(scriptPath) + " " +
                QuoteProcessArgument(assemblyPath) + " " + QuoteProcessArgument(projectRoot) + " " +
                QuoteProcessArgument(readyPath) + " " + QuoteProcessArgument(releasePath))
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            start.EnvironmentVariables["MONO_PATH"] = Path.GetDirectoryName(assemblyPath) +
                Path.PathSeparator + managedRoot +
                Path.PathSeparator + unityEngineManagedRoot;
            using (var owner = System.Diagnostics.Process.Start(start))
            {
                try
                {
                    var timeout = System.Diagnostics.Stopwatch.StartNew();
                    while (!File.Exists(readyPath) && !owner.HasExited && timeout.ElapsedMilliseconds < 30000)
                        Thread.Sleep(20);
                    Assert.That(File.Exists(readyPath), Is.True,
                        owner.HasExited ? owner.StandardError.ReadToEnd() : "Lock owner did not become ready.");
                    Assert.Throws<SafeGenerationException>(() => CreateService().AcquireGenerationLock());
                    if (terminateOwner)
                        owner.Kill();
                    else
                        File.WriteAllText(releasePath, "release");
                    Assert.That(owner.WaitForExit(10000), Is.True, "Lock owner did not exit.");
                    if (!terminateOwner)
                        Assert.That(owner.ExitCode, Is.Zero, owner.StandardError.ReadToEnd());
                    using (IDisposable generationLock = CreateService().AcquireGenerationLock())
                        Assert.That(generationLock, Is.Not.Null);
                }
                finally
                {
                    if (!owner.HasExited)
                    {
                        owner.Kill();
                        owner.WaitForExit(10000);
                    }
                }
            }
        }

        private static string FindUnityDirectory(
            string applicationContentsPath,
            params string[] relativePaths)
        {
            foreach (string relativePath in relativePaths)
            {
                string candidate = Path.Combine(applicationContentsPath, relativePath);
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return Path.Combine(applicationContentsPath, relativePaths[0]);
        }

        private static string QuoteProcessArgument(string argument)
        {
            return "\"" + argument.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        [Test]
        public void PublishGraphReportsCompilationRequiredAndNoOpDoesNotRequestCompilation()
        {
            WriteRaw("{\"openapi\":\"3.0.3\"}");
            NormalizedSpecCacheService service = CreateService(() => compilationRequestCount++);
            NormalizedSpecGraph graph = service.LoadGraphAsync(
                "Assets/Specs/openapi.json",
                SpecId,
                CancellationToken.None).GetAwaiter().GetResult();

            NormalizedSpecCacheResult first = service.PublishGraph(graph);

            Assert.That(first.CompilationRequired, Is.True);
            Assert.That(compilationRequestCount, Is.Zero);
            string markerPath = Path.Combine(
                Path.GetDirectoryName(first.AuthoritativePath),
                NormalizedSpecBundleConstants.PublishPendingFileName);
            Assert.That(File.ReadAllText(markerPath), Does.Contain("state=published-awaiting-compilation\n"));
            service.AcknowledgeCompilationRequest(SpecId);

            NormalizedSpecCacheResult second = service.PublishGraph(graph);

            Assert.That(second.CompilationRequired, Is.False);
            Assert.That(compilationRequestCount, Is.Zero);
            Assert.That(File.Exists(markerPath), Is.False);
        }

        [Test]
        public void RecoverPendingCompilationsPreservesAwaitingBytesAndRequestsCompilation()
        {
            WriteRaw("{\"openapi\":\"3.0.3\"}");
            NormalizedSpecCacheService publishingService = CreateService();
            NormalizedSpecGraph graph = publishingService.LoadGraphAsync(
                "Assets/Specs/openapi.json",
                SpecId,
                CancellationToken.None).GetAwaiter().GetResult();
            NormalizedSpecCacheResult published = publishingService.PublishGraph(graph);
            byte[] authoritativeBytes = File.ReadAllBytes(published.AuthoritativePath);
            byte[] mirrorBytes = File.ReadAllBytes(published.MirrorPath);

            NormalizedSpecCacheService recoveryService = CreateService(() => compilationRequestCount++);
            recoveryService.RecoverPendingCompilations();

            string markerPath = Path.Combine(
                Path.GetDirectoryName(published.AuthoritativePath),
                NormalizedSpecBundleConstants.PublishPendingFileName);
            Assert.That(compilationRequestCount, Is.EqualTo(1));
            Assert.That(File.Exists(markerPath), Is.False);
            Assert.That(File.ReadAllBytes(published.AuthoritativePath), Is.EqualTo(authoritativeBytes));
            Assert.That(File.ReadAllBytes(published.MirrorPath), Is.EqualTo(mirrorBytes));
        }

        [Test]
        public void RecoverPendingPublishingRollsBackArtifactsImportsMirrorAndRequestsCompilation()
        {
            WriteRaw("{\"openapi\":\"3.0.3\",\"info\":{\"version\":\"one\"}}" );
            NormalizedSpecCacheService firstService = CreateService();
            NormalizedSpecGraph firstGraph = firstService.LoadGraphAsync(
                "Assets/Specs/openapi.json",
                SpecId,
                CancellationToken.None).GetAwaiter().GetResult();
            NormalizedSpecCacheResult first = firstService.PublishGraph(firstGraph);
            firstService.AcknowledgeCompilationRequest(SpecId);
            byte[] oldAuthoritative = File.ReadAllBytes(first.AuthoritativePath);
            byte[] oldMirror = File.ReadAllBytes(first.MirrorPath);

            WriteRaw("{\"openapi\":\"3.0.3\",\"info\":{\"version\":\"two\"}}" );
            var failingService = new NormalizedSpecCacheService(
                projectRoot,
                new RawJsonNormalizer(),
                new TargetFailingWriter(first.MirrorPath),
                importer);
            NormalizedSpecGraph secondGraph = failingService.LoadGraphAsync(
                "Assets/Specs/openapi.json",
                SpecId,
                CancellationToken.None).GetAwaiter().GetResult();
            Assert.Throws<IOException>(() => failingService.PublishGraph(secondGraph));

            string markerPath = Path.Combine(
                Path.GetDirectoryName(first.AuthoritativePath),
                NormalizedSpecBundleConstants.PublishPendingFileName);
            Assert.That(File.ReadAllText(markerPath), Does.Contain("state=publishing\n"));
            importer.ImportedAssetPaths.Clear();

            NormalizedSpecCacheService recoveryService = CreateService(() => compilationRequestCount++);
            recoveryService.RecoverPendingCompilations();

            Assert.That(compilationRequestCount, Is.EqualTo(1));
            Assert.That(File.Exists(markerPath), Is.False);
            Assert.That(File.ReadAllBytes(first.AuthoritativePath), Is.EqualTo(oldAuthoritative));
            Assert.That(File.ReadAllBytes(first.MirrorPath), Is.EqualTo(oldMirror));
            Assert.That(importer.ImportedAssetPaths, Is.EqualTo(new[] { first.MirrorAssetPath }));
        }

        [Test]
        public void RecoveryDoesNotOverwriteConcurrentArtifactEdit()
        {
            WriteRaw("{\"openapi\":\"3.0.3\",\"info\":{\"version\":\"one\"}}");
            NormalizedSpecCacheService firstService = CreateService();
            NormalizedSpecGraph firstGraph = firstService.LoadGraphAsync(
                "Assets/Specs/openapi.json",
                SpecId,
                CancellationToken.None).GetAwaiter().GetResult();
            NormalizedSpecCacheResult first = firstService.PublishGraph(firstGraph);
            firstService.AcknowledgeCompilationRequest(SpecId);

            WriteRaw("{\"openapi\":\"3.0.3\",\"info\":{\"version\":\"two\"}}");
            var failingService = new NormalizedSpecCacheService(
                projectRoot,
                new RawJsonNormalizer(),
                new TargetFailingWriter(first.MirrorPath),
                importer);
            NormalizedSpecGraph secondGraph = failingService.LoadGraphAsync(
                "Assets/Specs/openapi.json",
                SpecId,
                CancellationToken.None).GetAwaiter().GetResult();
            Assert.Throws<IOException>(() => failingService.PublishGraph(secondGraph));
            byte[] concurrentBytes = new UTF8Encoding(false).GetBytes("concurrent user state");
            File.WriteAllBytes(first.AuthoritativePath, concurrentBytes);

            NormalizedSpecCacheService recoveryService = CreateService(() => compilationRequestCount++);
            recoveryService.RecoverPendingCompilations();

            Assert.That(File.ReadAllBytes(first.AuthoritativePath), Is.EqualTo(concurrentBytes));
            Assert.That(compilationRequestCount, Is.EqualTo(1));
        }

        [Test]
        public void RecoveryReadsVersionTwoThreeArtifactJournalWithoutOutputSnapshots()
        {
            WriteRaw("{\"openapi\":\"3.0.3\",\"info\":{\"version\":\"one\"}}");
            NormalizedSpecCacheService firstService = CreateService();
            NormalizedSpecGraph firstGraph = firstService.LoadGraphAsync(
                "Assets/Specs/openapi.json",
                SpecId,
                CancellationToken.None).GetAwaiter().GetResult();
            NormalizedSpecCacheResult first = firstService.PublishGraph(firstGraph);
            firstService.AcknowledgeCompilationRequest(SpecId);
            byte[] oldAuthoritative = File.ReadAllBytes(first.AuthoritativePath);

            WriteRaw("{\"openapi\":\"3.0.3\",\"info\":{\"version\":\"two\"}}");
            var failingService = new NormalizedSpecCacheService(
                projectRoot,
                new RawJsonNormalizer(),
                new TargetFailingWriter(first.MirrorPath),
                importer);
            NormalizedSpecGraph secondGraph = failingService.LoadGraphAsync(
                "Assets/Specs/openapi.json",
                SpecId,
                CancellationToken.None).GetAwaiter().GetResult();
            Assert.Throws<IOException>(() => failingService.PublishGraph(secondGraph));
            string cacheDirectory = Path.GetDirectoryName(first.AuthoritativePath);
            string markerPath = Path.Combine(
                cacheDirectory,
                NormalizedSpecBundleConstants.PublishPendingFileName);
            string marker = File.ReadAllText(markerPath, new UTF8Encoding(false));
            File.WriteAllText(
                markerPath,
                marker.Replace("version=3\n", "version=2\n"),
                new UTF8Encoding(false));
            string backupDirectory = Path.Combine(
                cacheDirectory,
                NormalizedSpecBundleConstants.PublishBackupDirectoryName);
            foreach (string outputSnapshot in Directory.GetFiles(
                         backupDirectory,
                         "*.output.*",
                         SearchOption.TopDirectoryOnly))
            {
                File.Delete(outputSnapshot);
            }

            CreateService(() => compilationRequestCount++).RecoverPendingCompilations();

            Assert.That(File.ReadAllBytes(first.AuthoritativePath), Is.EqualTo(oldAuthoritative));
            Assert.That(compilationRequestCount, Is.EqualTo(1));
        }

        [Test]
        public void RecoveryReadsVersionTwoFourArtifactJournalWithoutOutputSnapshots()
        {
            WriteRaw("{\"openapi\":\"3.0.3\"}");
            NormalizedSpecCacheService service = CreateService();
            NormalizedSpecGraph graph = service.LoadGraphAsync(
                "Assets/Specs/openapi.json",
                SpecId,
                CancellationToken.None).GetAwaiter().GetResult();
            NormalizedSpecCacheResult published = service.PublishGraph(graph);
            service.AcknowledgeCompilationRequest(SpecId);
            string cacheDirectory = Path.GetDirectoryName(published.AuthoritativePath);
            string manifestPath = Path.Combine(
                cacheDirectory,
                NormalizedSpecBundleConstants.ManifestFileName);
            string definitionPath = Path.Combine(
                projectRoot,
                "Assets",
                "Clients",
                "PetStoreApi.OpenApiDefinition.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(definitionPath));
            File.WriteAllText(definitionPath, "old definition", new UTF8Encoding(false));
            string[] targets =
            {
                published.AuthoritativePath,
                manifestPath,
                published.MirrorPath,
                definitionPath,
            };
            var oldBytes = new byte[targets.Length][];
            string backupDirectory = Path.Combine(
                cacheDirectory,
                NormalizedSpecBundleConstants.PublishBackupDirectoryName);
            Directory.CreateDirectory(backupDirectory);
            for (int index = 0; index < targets.Length; index++)
            {
                oldBytes[index] = File.ReadAllBytes(targets[index]);
                File.WriteAllBytes(
                    Path.Combine(backupDirectory, "artifact-" + index.ToString("D3") + ".bytes"),
                    oldBytes[index]);
                File.WriteAllText(targets[index], "new " + index, new UTF8Encoding(false));
            }

            var marker = new StringBuilder();
            marker.Append("version=2\nstate=publishing\ncount=4\ncompilationRequired=true\n");
            for (int index = 0; index < targets.Length; index++)
            {
                string relativePath = targets[index].Substring(projectRoot.Length + 1).Replace('\\', '/');
                marker.Append("target=")
                    .Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(relativePath)))
                    .Append('\n');
            }

            File.WriteAllText(
                Path.Combine(cacheDirectory, NormalizedSpecBundleConstants.PublishPendingFileName),
                marker.ToString(),
                new UTF8Encoding(false));
            importer.ImportedAssetPaths.Clear();
            NormalizedSpecCacheService recovery = CreateService(() => compilationRequestCount++);

            recovery.RepairPendingPublicationForSpec(
                SpecId,
                definitionPath,
                "Assets/Clients/PetStoreApi.OpenApiDefinition.cs");

            for (int index = 0; index < targets.Length; index++)
            {
                Assert.That(File.ReadAllBytes(targets[index]), Is.EqualTo(oldBytes[index]));
            }
            Assert.That(compilationRequestCount, Is.EqualTo(1));
            Assert.That(importer.ImportedAssetPaths, Does.Contain(published.MirrorAssetPath));
            Assert.That(
                importer.ImportedAssetPaths,
                Does.Contain("Assets/Clients/PetStoreApi.OpenApiDefinition.cs"));
        }

        [Test]
        public void RestartRecoveryRestoresMovedDefinitionAndMetaFromDurableSnapshots()
        {
            InterruptedMigration migration = CreateInterruptedMigrationJournal(withMeta: true);
            importer.ImportedAssetPaths.Clear();
            NormalizedSpecCacheService recovery = CreateService(() => compilationRequestCount++);

            recovery.RepairPendingPublicationForSpec(
                SpecId,
                migration.NewDefinitionPath,
                migration.NewDefinitionAssetPath);

            Assert.That(
                File.ReadAllBytes(migration.OldDefinitionPath),
                Is.EqualTo(migration.DefinitionBytes));
            Assert.That(
                File.ReadAllBytes(migration.OldDefinitionPath + ".meta"),
                Is.EqualTo(migration.MetaBytes));
            Assert.That(File.Exists(migration.NewDefinitionPath), Is.False);
            Assert.That(File.Exists(migration.NewDefinitionPath + ".meta"), Is.False);
            Assert.That(compilationRequestCount, Is.EqualTo(1));
            Assert.That(
                importer.ImportedAssetPaths,
                Does.Contain(migration.OldDefinitionAssetPath));
            Assert.That(
                importer.ImportedAssetPaths,
                Does.Not.Contain(migration.NewDefinitionAssetPath));
        }

        [Test]
        public void RestartRecoveryPreservesConcurrentlyCreatedPreviouslyMissingOldMeta()
        {
            InterruptedMigration migration = CreateInterruptedMigrationJournal(withMeta: false);
            byte[] concurrentMeta = new UTF8Encoding(false).GetBytes(
                "fileFormatVersion: 2\nguid: ffffffffffffffffffffffffffffffff\n");
            File.WriteAllBytes(migration.OldDefinitionPath + ".meta", concurrentMeta);
            NormalizedSpecCacheService recovery = CreateService(() => compilationRequestCount++);

            recovery.RepairPendingPublicationForSpec(
                SpecId,
                migration.NewDefinitionPath,
                migration.NewDefinitionAssetPath);

            Assert.That(
                File.ReadAllBytes(migration.OldDefinitionPath + ".meta"),
                Is.EqualTo(concurrentMeta));
            Assert.That(File.Exists(migration.OldDefinitionPath), Is.False);
            Assert.That(
                File.ReadAllBytes(migration.NewDefinitionPath),
                Is.EqualTo(migration.DefinitionBytes));
        }

        [Test]
        public void RestartRecoveryPreservesEntireMovedGroupAfterDestinationEdit()
        {
            InterruptedMigration migration = CreateInterruptedMigrationJournal(withMeta: true);
            byte[] concurrentBytes = new UTF8Encoding(false).GetBytes("// user destination edit\n");
            File.WriteAllBytes(migration.NewDefinitionPath, concurrentBytes);
            NormalizedSpecCacheService recovery = CreateService(() => compilationRequestCount++);

            recovery.RepairPendingPublicationForSpec(
                SpecId,
                migration.NewDefinitionPath,
                migration.NewDefinitionAssetPath);

            Assert.That(
                File.ReadAllBytes(migration.NewDefinitionPath),
                Is.EqualTo(concurrentBytes));
            Assert.That(
                File.ReadAllBytes(migration.NewDefinitionPath + ".meta"),
                Is.EqualTo(migration.MetaBytes));
            Assert.That(File.Exists(migration.OldDefinitionPath), Is.False);
            Assert.That(File.Exists(migration.OldDefinitionPath + ".meta"), Is.False);
            for (int index = 0; index < migration.CachePaths.Length; index++)
            {
                Assert.That(
                    File.ReadAllBytes(migration.CachePaths[index]),
                    Is.EqualTo(migration.CacheBytes[index]));
            }
        }

        [Test]
        public void RawContentChangeUpdatesBothFiles()
        {
            WriteRaw("{\"version\":1}");
            NormalizedSpecCacheService service = CreateService();
            NormalizedSpecCacheResult first = service.NormalizeAndCache("Assets/Specs/openapi.json", SpecId);
            byte[] firstBytes = File.ReadAllBytes(first.AuthoritativePath);
            WriteRaw("{\"version\":2}");

            NormalizedSpecCacheResult second = service.NormalizeAndCache("Assets/Specs/openapi.json", SpecId);

            Assert.That(second.RawSha256, Is.Not.EqualTo(first.RawSha256));
            Assert.That(second.AuthoritativeChanged, Is.True);
            Assert.That(second.MirrorChanged, Is.True);
            Assert.That(File.ReadAllBytes(second.AuthoritativePath), Is.Not.EqualTo(firstBytes));
            Assert.That(File.ReadAllBytes(second.MirrorPath), Is.EqualTo(File.ReadAllBytes(second.AuthoritativePath)));
        }

        [Test]
        public void NormalizeAndCacheRepairsOnlyMissingOrCorruptMirror()
        {
            WriteRaw("{\"openapi\":\"3.0.3\"}");
            NormalizedSpecCacheService service = CreateService();
            NormalizedSpecCacheResult first = service.NormalizeAndCache("Assets/Specs/openapi.json", SpecId);
            DateTime authoritativeTimestamp = File.GetLastWriteTimeUtc(first.AuthoritativePath);
            File.WriteAllText(first.MirrorPath, "corrupt");
            importer.ImportedAssetPaths.Clear();

            NormalizedSpecCacheResult repaired = service.NormalizeAndCache("Assets/Specs/openapi.json", SpecId);

            Assert.That(repaired.AuthoritativeChanged, Is.False);
            Assert.That(repaired.MirrorChanged, Is.True);
            Assert.That(File.GetLastWriteTimeUtc(repaired.AuthoritativePath), Is.EqualTo(authoritativeTimestamp));
            Assert.That(File.ReadAllBytes(repaired.MirrorPath), Is.EqualTo(File.ReadAllBytes(repaired.AuthoritativePath)));
            Assert.That(importer.ImportedAssetPaths, Is.EqualTo(new[] { repaired.MirrorAssetPath }));
        }

        [Test]
        public void InvalidRawJsonPreservesLastKnownGoodCacheAndMirror()
        {
            WriteRaw("{\"openapi\":\"3.0.3\"}");
            NormalizedSpecCacheService service = CreateService();
            NormalizedSpecCacheResult valid = service.NormalizeAndCache("Assets/Specs/openapi.json", SpecId);
            byte[] authoritativeBytes = File.ReadAllBytes(valid.AuthoritativePath);
            byte[] mirrorBytes = File.ReadAllBytes(valid.MirrorPath);
            DateTime authoritativeTimestamp = File.GetLastWriteTimeUtc(valid.AuthoritativePath);
            DateTime mirrorTimestamp = File.GetLastWriteTimeUtc(valid.MirrorPath);
            WriteRaw("{\"openapi\":}");

            Assert.Throws<NormalizedSpecException>(
                () => service.NormalizeAndCache("Assets/Specs/openapi.json", SpecId));

            Assert.That(File.ReadAllBytes(valid.AuthoritativePath), Is.EqualTo(authoritativeBytes));
            Assert.That(File.ReadAllBytes(valid.MirrorPath), Is.EqualTo(mirrorBytes));
            Assert.That(File.GetLastWriteTimeUtc(valid.AuthoritativePath), Is.EqualTo(authoritativeTimestamp));
            Assert.That(File.GetLastWriteTimeUtc(valid.MirrorPath), Is.EqualTo(mirrorTimestamp));
        }

        [Test]
        public void SourceIdentityChangeUpdatesBothFilesEvenWhenRawBytesMatch()
        {
            const string Json = "{\"openapi\":\"3.0.3\"}";
            WriteRaw(Json);
            string secondPath = Path.Combine(projectRoot, "Assets", "Specs", "renamed.json");
            File.WriteAllText(secondPath, Json, new UTF8Encoding(false));
            NormalizedSpecCacheService service = CreateService();
            NormalizedSpecCacheResult first = service.NormalizeAndCache("Assets/Specs/openapi.json", SpecId);

            NormalizedSpecCacheResult second = service.NormalizeAndCache("Assets/Specs/renamed.json", SpecId);

            Assert.That(second.SourcePath, Is.EqualTo("Assets/Specs/renamed.json"));
            Assert.That(second.RawSha256, Is.EqualTo(first.RawSha256));
            Assert.That(second.AuthoritativeChanged, Is.True);
            Assert.That(second.MirrorChanged, Is.True);
        }

        [Test]
        public void ProjectExternalSourceUsesNormalizedAbsoluteIdentity()
        {
            string externalDirectory = Path.Combine(Path.GetTempPath(), "OpenApiCodeGenExternalSpecs", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(externalDirectory);
            string externalPath = Path.Combine(externalDirectory, "openapi.json");
            File.WriteAllText(externalPath, "{}", new UTF8Encoding(false));

            try
            {
                NormalizedSpecCacheResult result = CreateService().NormalizeAndCache(externalPath, SpecId);

                Assert.That(result.SourcePath, Is.EqualTo(Path.GetFullPath(externalPath).Replace('\\', '/')));
            }
            finally
            {
                Directory.Delete(externalDirectory, true);
            }
        }

        [Test]
        public void MirrorWriteFailureLeavesOldMirrorIntactAndReportsFailure()
        {
            WriteRaw("{\"version\":1}");
            NormalizedSpecCacheResult first = CreateService().NormalizeAndCache("Assets/Specs/openapi.json", SpecId);
            byte[] oldMirror = File.ReadAllBytes(first.MirrorPath);
            WriteRaw("{\"version\":2}");
            var failingWriter = new MirrorFailingWriter(first.MirrorPath);
            var service = new NormalizedSpecCacheService(
                projectRoot,
                new RawJsonNormalizer(),
                failingWriter,
                importer);

            Assert.Throws<IOException>(() => service.NormalizeAndCache("Assets/Specs/openapi.json", SpecId));

            Assert.That(File.ReadAllBytes(first.MirrorPath), Is.EqualTo(oldMirror));
            Assert.That(File.ReadAllBytes(first.AuthoritativePath), Is.Not.EqualTo(oldMirror));
        }

        [Test]
        public void AuthoritativeWriteFailureLeavesBothLastKnownGoodFilesIntact()
        {
            WriteRaw("{\"version\":1}");
            NormalizedSpecCacheResult first = CreateService().NormalizeAndCache("Assets/Specs/openapi.json", SpecId);
            byte[] oldAuthoritative = File.ReadAllBytes(first.AuthoritativePath);
            byte[] oldMirror = File.ReadAllBytes(first.MirrorPath);
            WriteRaw("{\"version\":2}");
            var failingWriter = new TargetFailingWriter(first.AuthoritativePath);
            var service = new NormalizedSpecCacheService(
                projectRoot,
                new RawJsonNormalizer(),
                failingWriter,
                importer);

            Assert.Throws<IOException>(() => service.NormalizeAndCache("Assets/Specs/openapi.json", SpecId));

            Assert.That(File.ReadAllBytes(first.AuthoritativePath), Is.EqualTo(oldAuthoritative));
            Assert.That(File.ReadAllBytes(first.MirrorPath), Is.EqualTo(oldMirror));
        }

        [Test]
        public void ImportFailureIsRetriedByANewServiceInstanceWithoutRewritingMirror()
        {
            WriteRaw("{\"version\":1}");
            NormalizedSpecCacheResult first = CreateService().NormalizeAndCache("Assets/Specs/openapi.json", SpecId);
            WriteRaw("{\"version\":2}");
            var failingService = new NormalizedSpecCacheService(
                projectRoot,
                new RawJsonNormalizer(),
                new AtomicFileWriter(),
                new ThrowingImporter());

            Assert.Throws<InvalidOperationException>(
                () => failingService.NormalizeAndCache("Assets/Specs/openapi.json", SpecId));

            byte[] publishedBytes = File.ReadAllBytes(first.MirrorPath);
            DateTime publishedTimestamp = File.GetLastWriteTimeUtc(first.MirrorPath);
            importer.ImportedAssetPaths.Clear();

            NormalizedSpecCacheResult retried = CreateService().NormalizeAndCache(
                "Assets/Specs/openapi.json",
                SpecId);

            Assert.That(retried.AuthoritativeChanged, Is.False);
            Assert.That(retried.MirrorChanged, Is.False);
            Assert.That(File.ReadAllBytes(retried.MirrorPath), Is.EqualTo(publishedBytes));
            Assert.That(File.GetLastWriteTimeUtc(retried.MirrorPath), Is.EqualTo(publishedTimestamp));
            Assert.That(importer.ImportedAssetPaths, Is.EqualTo(new[] { retried.MirrorAssetPath }));
            Assert.That(
                File.Exists(Path.Combine(
                    Path.GetDirectoryName(retried.AuthoritativePath),
                    NormalizedSpecBundleConstants.MirrorImportPendingFileName)),
                Is.False);
        }

        [Test]
        public void AtomicWriterReplacesExistingFileWithoutLeavingTemporaryFiles()
        {
            string target = Path.Combine(projectRoot, "Library", "target.json");
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.WriteAllText(target, "old");

            new AtomicFileWriter().WriteAllBytesAtomically(target, Encoding.UTF8.GetBytes("new"));

            Assert.That(File.ReadAllText(target), Is.EqualTo("new"));
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(target), "*.tmp"), Is.Empty);
        }

        private InterruptedMigration CreateInterruptedMigrationJournal(bool withMeta)
        {
            WriteRaw("{\"openapi\":\"3.0.3\"}");
            NormalizedSpecCacheService service = CreateService();
            NormalizedSpecGraph graph = service.LoadGraphAsync(
                "Assets/Specs/openapi.json",
                SpecId,
                CancellationToken.None).GetAwaiter().GetResult();
            NormalizedSpecCacheResult published = service.PublishGraph(graph);
            service.AcknowledgeCompilationRequest(SpecId);
            string cacheDirectory = Path.GetDirectoryName(published.AuthoritativePath);
            string manifestPath = Path.Combine(
                cacheDirectory,
                NormalizedSpecBundleConstants.ManifestFileName);
            string oldDefinitionPath = Path.Combine(
                projectRoot,
                "Assets",
                "Clients",
                "Old",
                "PetStoreApi.OpenApiDefinition.cs");
            string newDefinitionPath = Path.Combine(
                projectRoot,
                "Assets",
                "Clients",
                "New",
                "PetStoreApi.OpenApiDefinition.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(oldDefinitionPath));
            Directory.CreateDirectory(Path.GetDirectoryName(newDefinitionPath));
            byte[] definitionBytes = new UTF8Encoding(false).GetBytes("owned definition\n");
            byte[] metaBytes = new UTF8Encoding(false).GetBytes(
                "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\n");
            File.WriteAllBytes(oldDefinitionPath, definitionBytes);
            if (withMeta)
            {
                File.WriteAllBytes(oldDefinitionPath + ".meta", metaBytes);
            }

            string[] targets =
            {
                published.AuthoritativePath,
                manifestPath,
                published.MirrorPath,
                newDefinitionPath,
                newDefinitionPath + ".meta",
                oldDefinitionPath,
                oldDefinitionPath + ".meta",
            };
            var before = new TestFileState[targets.Length];
            for (int index = 0; index < targets.Length; index++)
            {
                before[index] = CaptureTestFileState(targets[index]);
            }

            File.WriteAllBytes(newDefinitionPath, definitionBytes);
            if (withMeta)
            {
                File.WriteAllBytes(newDefinitionPath + ".meta", metaBytes);
            }
            File.Delete(oldDefinitionPath);
            File.Delete(oldDefinitionPath + ".meta");
            for (int index = 0; index < 3; index++)
            {
                File.WriteAllText(
                    targets[index],
                    "interrupted transaction output " + index,
                    new UTF8Encoding(false));
            }

            var after = new TestFileState[targets.Length];
            for (int index = 0; index < targets.Length; index++)
            {
                after[index] = CaptureTestFileState(targets[index]);
            }

            string backupDirectory = Path.Combine(
                cacheDirectory,
                NormalizedSpecBundleConstants.PublishBackupDirectoryName);
            Directory.CreateDirectory(backupDirectory);
            for (int index = 0; index < targets.Length; index++)
            {
                WriteTestFileState(
                    backupDirectory,
                    "artifact-" + index.ToString("D3"),
                    before[index],
                    string.Empty);
                WriteTestFileState(
                    backupDirectory,
                    "artifact-" + index.ToString("D3"),
                    after[index],
                    ".output");
            }

            var marker = new StringBuilder();
            marker.Append("version=3\nstate=publishing\ncount=7\ncompilationRequired=true\n");
            for (int index = 0; index < targets.Length; index++)
            {
                string relativePath = targets[index]
                    .Substring(projectRoot.Length + 1)
                    .Replace('\\', '/');
                marker.Append("target=")
                    .Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(relativePath)))
                    .Append('\n');
            }

            File.WriteAllText(
                Path.Combine(cacheDirectory, NormalizedSpecBundleConstants.PublishPendingFileName),
                marker.ToString(),
                new UTF8Encoding(false));
            return new InterruptedMigration(
                oldDefinitionPath,
                newDefinitionPath,
                definitionBytes,
                withMeta ? metaBytes : new byte[0],
                new[] { targets[0], targets[1], targets[2] },
                new[] { before[0].Bytes, before[1].Bytes, before[2].Bytes });
        }

        private static TestFileState CaptureTestFileState(string path)
        {
            return File.Exists(path)
                ? new TestFileState(true, File.ReadAllBytes(path))
                : new TestFileState(false, new byte[0]);
        }

        private static void WriteTestFileState(
            string directory,
            string name,
            TestFileState state,
            string infix)
        {
            string suffix = state.Exists ? ".bytes" : ".absent";
            File.WriteAllBytes(
                Path.Combine(directory, name + infix + suffix),
                state.Exists ? state.Bytes : new byte[] { 1 });
        }

        private NormalizedSpecCacheService CreateService(Action compilationRequester = null)
        {
            NormalizedSpecCacheService service = new NormalizedSpecCacheService(
                projectRoot,
                new RawJsonNormalizer(),
                new AtomicFileWriter(),
                importer);
            service.CompilationRequester = compilationRequester ?? (() => { });
            return service;
        }

        private void WriteRaw(string json)
        {
            WriteRawAt("Assets/Specs/openapi.json", json);
        }

        private void WriteRawAt(string relativePath, string content)
        {
            string path = Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        private sealed class RecordingImporter : ICompilerMirrorImporter
        {
            internal List<string> ImportedAssetPaths { get; } = new List<string>();

            public void Import(string mirrorAssetPath)
            {
                ImportedAssetPaths.Add(mirrorAssetPath);
            }
        }

        private sealed class TestFileState
        {
            internal TestFileState(bool exists, byte[] bytes)
            {
                Exists = exists;
                Bytes = bytes;
            }

            internal bool Exists { get; }
            internal byte[] Bytes { get; }
        }

        private sealed class InterruptedMigration
        {
            internal InterruptedMigration(
                string oldDefinitionPath,
                string newDefinitionPath,
                byte[] definitionBytes,
                byte[] metaBytes,
                string[] cachePaths,
                byte[][] cacheBytes)
            {
                OldDefinitionPath = oldDefinitionPath;
                NewDefinitionPath = newDefinitionPath;
                DefinitionBytes = definitionBytes;
                MetaBytes = metaBytes;
                CachePaths = cachePaths;
                CacheBytes = cacheBytes;
            }

            internal string OldDefinitionPath { get; }
            internal string NewDefinitionPath { get; }
            internal byte[] DefinitionBytes { get; }
            internal byte[] MetaBytes { get; }
            internal string[] CachePaths { get; }
            internal byte[][] CacheBytes { get; }
            internal string OldDefinitionAssetPath =>
                "Assets/Clients/Old/PetStoreApi.OpenApiDefinition.cs";
            internal string NewDefinitionAssetPath =>
                "Assets/Clients/New/PetStoreApi.OpenApiDefinition.cs";
        }

        private class TargetFailingWriter : IAtomicFileWriter
        {
            private readonly string failurePath;
            private readonly AtomicFileWriter inner = new AtomicFileWriter();

            internal TargetFailingWriter(string failurePath)
            {
                this.failurePath = failurePath;
            }

            public void WriteAllBytesAtomically(string path, byte[] bytes)
            {
                if (string.Equals(path, failurePath, StringComparison.Ordinal))
                {
                    throw new IOException("Simulated atomic write failure.");
                }

                inner.WriteAllBytesAtomically(path, bytes);
            }
        }

        private sealed class MirrorFailingWriter : TargetFailingWriter
        {
            internal MirrorFailingWriter(string mirrorPath)
                : base(mirrorPath)
            {
            }
        }

        private sealed class ThrowingImporter : ICompilerMirrorImporter
        {
            public void Import(string mirrorAssetPath)
            {
                throw new InvalidOperationException("Simulated AssetDatabase import failure.");
            }
        }
    }
}
