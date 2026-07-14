using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using Rhycol.OpenApiCodeGen.SourceGenerator.Editor;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    internal sealed class NormalizedSpecCacheServiceTests
    {
        private const string SpecId = "0123456789abcdef0123456789abcdef";
        private string projectRoot;
        private RecordingImporter importer;

        [SetUp]
        public void SetUp()
        {
            projectRoot = Path.Combine(Path.GetTempPath(), "OpenApiCodeGenSourceGeneratorTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(projectRoot, "Assets", "Specs"));
            importer = new RecordingImporter();
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

        private NormalizedSpecCacheService CreateService()
        {
            return new NormalizedSpecCacheService(
                projectRoot,
                new RawJsonNormalizer(),
                new AtomicFileWriter(),
                importer);
        }

        private void WriteRaw(string json)
        {
            File.WriteAllText(
                Path.Combine(projectRoot, "Assets", "Specs", "openapi.json"),
                json,
                new UTF8Encoding(false));
        }

        private sealed class RecordingImporter : ICompilerMirrorImporter
        {
            internal List<string> ImportedAssetPaths { get; } = new List<string>();

            public void Import(string mirrorAssetPath)
            {
                ImportedAssetPaths.Add(mirrorAssetPath);
            }
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
