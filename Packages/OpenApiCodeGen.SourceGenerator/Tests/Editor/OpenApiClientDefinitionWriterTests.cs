#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using NUnit.Framework;
using Rhycol.OpenApiCodeGen.SourceGenerator.Editor;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    internal sealed class OpenApiClientDefinitionWriterTests
    {
        string projectRoot = string.Empty;
        string outputFolder = string.Empty;
        List<string> importedAssetPaths = null!;
        OpenApiClientDefinitionWriter writer = null!;

        [SetUp]
        public void SetUp()
        {
            projectRoot = Path.Combine(
                Path.GetTempPath(),
                "OpenApiClientDefinitionWriterTests",
                Guid.NewGuid().ToString("N"));
            outputFolder = Path.Combine(projectRoot, "Assets", "Clients", "PetStore");
            Directory.CreateDirectory(outputFolder);
            WriteAsmdef(
                Path.Combine(projectRoot, "Assets", "Clients"),
                "Example.Clients",
                "Unity.OpenApiCodeGen.SourceGenerator");
            importedAssetPaths = new List<string>();
            writer = new OpenApiClientDefinitionWriter(
                projectRoot,
                new AtomicFileWriter(),
                importedAssetPaths.Add);
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
        public void PublishWritesOwnedAttributeDefinitionToTargetAssembly()
        {
            OpenApiClientDefinitionPlan plan = writer.Prepare(
                outputFolder,
                "PetStoreApi",
                "Example.Generated.PetStore");

            bool changed = writer.Publish(plan);

            string source = File.ReadAllText(plan.DefinitionPath, new UTF8Encoding(false, true));
            Assert.That(changed, Is.True);
            Assert.That(
                Path.GetFileName(plan.DefinitionPath),
                Is.EqualTo("PetStoreApi.OpenApiDefinition.cs"));
            Assert.That(plan.DefinitionAssetPath, Is.EqualTo(
                "Assets/Clients/PetStore/PetStoreApi.OpenApiDefinition.cs"));
            Assert.That(plan.TargetAssemblyName, Is.EqualTo("Example.Clients"));
            Assert.That(source, Does.StartWith(OpenApiClientDefinitionWriter.OwnedFileHeader));
            Assert.That(source, Does.Contain("#if OPENAPI_CODEGEN_SOURCE_GENERATOR"));
            Assert.That(source, Does.Contain("public partial class PetStoreApi"));
            Assert.That(
                source,
                Does.Contain(
                    "global::Rhycol.OpenApiCodeGen.SourceGenerator.OpenApiDocumentFormat.Json"));
            Assert.That(source, Does.Contain("\"" + plan.SpecId + "\""));
            Assert.That(Guid.TryParseExact(plan.SpecId, "N", out Guid parsed), Is.True);
            Assert.That(
                parsed.ToString("N", CultureInfo.InvariantCulture),
                Is.EqualTo(plan.SpecId));
            Assert.That(importedAssetPaths, Is.EqualTo(new[] { plan.DefinitionAssetPath }));
        }

        [Test]
        public void PrepareAndPublishWritesYamlDocumentFormat()
        {
            OpenApiClientDefinitionPlan plan = writer.Prepare(
                outputFolder,
                "PetStoreApi",
                "Example.Generated.PetStore",
                OpenApiDocumentFormat.Yaml);

            Assert.That(plan.DocumentFormat, Is.EqualTo(OpenApiDocumentFormat.Yaml));
            Assert.That(writer.Publish(plan), Is.True);
            string source = File.ReadAllText(plan.DefinitionPath, new UTF8Encoding(false, true));

            Assert.That(
                source,
                Does.Contain(
                    "global::Rhycol.OpenApiCodeGen.SourceGenerator.OpenApiDocumentFormat.Yaml"));
        }

        [Test]
        public void SameClientIdentityProducesStableSpecIdAcrossOutputSubfolders()
        {
            OpenApiClientDefinitionPlan first = writer.Prepare(
                outputFolder,
                "PetStoreApi",
                "Example.Generated.PetStore");
            string secondOutput = Path.Combine(
                projectRoot,
                "Assets",
                "Clients",
                "Moved",
                "PetStore");

            OpenApiClientDefinitionPlan second = writer.Prepare(
                secondOutput,
                "PetStoreApi",
                "Example.Generated.PetStore");

            Assert.That(second.SpecId, Is.EqualTo(first.SpecId));
            Assert.That(second.ClientIdentitySha256, Is.EqualTo(first.ClientIdentitySha256));
        }

        [Test]
        public void PublishMovesOwnedDefinitionAndMetaWithinTheSameAssembly()
        {
            OpenApiClientDefinitionPlan initial = PublishInitialDefinition();
            const string Meta = "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\n";
            File.WriteAllText(initial.DefinitionPath + ".meta", Meta, new UTF8Encoding(false));
            string movedOutput = Path.Combine(
                projectRoot,
                "Assets",
                "Clients",
                "Moved",
                "PetStore");
            importedAssetPaths.Clear();

            OpenApiClientDefinitionPlan moved = writer.Prepare(
                movedOutput,
                "PetStoreApi",
                "Example.Generated.PetStore");
            bool changed = writer.Publish(moved);

            Assert.That(changed, Is.True);
            Assert.That(moved.IsFolderMigration, Is.True);
            Assert.That(moved.SpecId, Is.EqualTo(initial.SpecId));
            Assert.That(File.Exists(initial.DefinitionPath), Is.False);
            Assert.That(File.Exists(initial.DefinitionPath + ".meta"), Is.False);
            Assert.That(File.Exists(moved.DefinitionPath), Is.True);
            Assert.That(File.ReadAllText(moved.DefinitionPath + ".meta"), Is.EqualTo(Meta));
            Assert.That(importedAssetPaths, Is.EqualTo(new[] { moved.DefinitionAssetPath }));
        }

        [Test]
        public void FolderMigrationCompletesAllFilesystemChangesBeforeImport()
        {
            OpenApiClientDefinitionPlan initial = PublishInitialDefinition();
            byte[] metaBytes = new UTF8Encoding(false).GetBytes(
                "fileFormatVersion: 2\nguid: fedcba9876543210fedcba9876543210\n");
            File.WriteAllBytes(initial.DefinitionPath + ".meta", metaBytes);
            string movedOutput = Path.Combine(projectRoot, "Assets", "Clients", "Moved");
            OpenApiClientDefinitionPlan moved = writer.Prepare(
                movedOutput,
                "PetStoreApi",
                "Example.Generated.PetStore");
            bool importObservedFinalState = false;
            writer = new OpenApiClientDefinitionWriter(
                projectRoot,
                new AtomicFileWriter(),
                _ =>
                {
                    importObservedFinalState =
                        !File.Exists(initial.DefinitionPath) &&
                        !File.Exists(initial.DefinitionPath + ".meta") &&
                        File.Exists(moved.DefinitionPath) &&
                        File.ReadAllBytes(moved.DefinitionPath + ".meta").SequenceEqual(metaBytes);
                });

            writer.Publish(moved);

            Assert.That(importObservedFinalState, Is.True);
        }

        [Test]
        public void PrepareRejectsOccupiedMigrationDestinationMetadata()
        {
            PublishInitialDefinition();
            string movedOutput = Path.Combine(projectRoot, "Assets", "Clients", "Moved");
            Directory.CreateDirectory(movedOutput);
            string destinationMeta = Path.Combine(
                movedOutput,
                "PetStoreApi.OpenApiDefinition.cs.meta");
            File.WriteAllText(destinationMeta, "foreign", new UTF8Encoding(false));

            SafeGenerationException exception = Assert.Throws<SafeGenerationException>(
                () => writer.Prepare(
                    movedOutput,
                    "PetStoreApi",
                    "Example.Generated.PetStore"))!;

            Assert.That(exception.Message, Does.Contain("occupied"));
            Assert.That(File.ReadAllText(destinationMeta), Is.EqualTo("foreign"));
        }

        [Test]
        public void PrepareRejectsAmbiguousOwnedMigrationSources()
        {
            OpenApiClientDefinitionPlan initial = PublishInitialDefinition();
            string duplicateFolder = Path.Combine(projectRoot, "Assets", "Clients", "Duplicate");
            Directory.CreateDirectory(duplicateFolder);
            File.Copy(
                initial.DefinitionPath,
                Path.Combine(duplicateFolder, Path.GetFileName(initial.DefinitionPath)));
            string movedOutput = Path.Combine(projectRoot, "Assets", "Clients", "Moved");

            SafeGenerationException exception = Assert.Throws<SafeGenerationException>(
                () => writer.Prepare(
                    movedOutput,
                    "PetStoreApi",
                    "Example.Generated.PetStore"))!;

            Assert.That(exception.Message, Does.Contain("Multiple owned definitions"));
        }

        [Test]
        public void PrepareRejectsSimultaneousNamespaceAndFolderChange()
        {
            PublishInitialDefinition();
            string movedOutput = Path.Combine(projectRoot, "Assets", "Clients", "Moved");

            SafeGenerationException exception = Assert.Throws<SafeGenerationException>(
                () => writer.Prepare(
                    movedOutput,
                    "PetStoreApi",
                    "Example.Renamed.PetStore"))!;

            Assert.That(exception.Message, Does.Contain("namespace"));
            Assert.That(exception.Message, Does.Contain("before moving"));
        }

        [Test]
        public void PublishRejectsMigrationSourceChangedAfterPrepare()
        {
            OpenApiClientDefinitionPlan initial = PublishInitialDefinition();
            string movedOutput = Path.Combine(projectRoot, "Assets", "Clients", "Moved");
            OpenApiClientDefinitionPlan moved = writer.Prepare(
                movedOutput,
                "PetStoreApi",
                "Example.Generated.PetStore");
            File.AppendAllText(initial.DefinitionPath, "// user edit\n", new UTF8Encoding(false));

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => writer.Publish(moved))!;

            Assert.That(exception.Message, Does.Contain("modified outside"));
            Assert.That(File.Exists(moved.DefinitionPath), Is.False);
        }

        [Test]
        public void MigrationRollbackDoesNotOverwriteConcurrentDestinationEdit()
        {
            OpenApiClientDefinitionPlan initial = PublishInitialDefinition();
            File.WriteAllText(
                initial.DefinitionPath + ".meta",
                "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\n",
                new UTF8Encoding(false));
            string movedOutput = Path.Combine(projectRoot, "Assets", "Clients", "Moved");
            OpenApiClientDefinitionPlan moved = writer.Prepare(
                movedOutput,
                "PetStoreApi",
                "Example.Generated.PetStore");
            DefinitionPublication publication = writer.PublishTransactional(moved);
            byte[] concurrentBytes = new UTF8Encoding(false).GetBytes("// concurrent user edit\n");
            File.WriteAllBytes(moved.DefinitionPath, concurrentBytes);

            publication.Rollback();

            Assert.That(File.ReadAllBytes(moved.DefinitionPath), Is.EqualTo(concurrentBytes));
            Assert.That(File.Exists(initial.DefinitionPath), Is.False);
        }

        [Test]
        public void MigrationRollbackPreservesConcurrentlyCreatedPreviouslyMissingMeta()
        {
            OpenApiClientDefinitionPlan initial = PublishInitialDefinition();
            Assert.That(File.Exists(initial.DefinitionPath + ".meta"), Is.False);
            string movedOutput = Path.Combine(projectRoot, "Assets", "Clients", "Moved");
            OpenApiClientDefinitionPlan moved = writer.Prepare(
                movedOutput,
                "PetStoreApi",
                "Example.Generated.PetStore");
            DefinitionPublication publication = writer.PublishTransactional(moved);
            byte[] concurrentMeta = new UTF8Encoding(false).GetBytes(
                "fileFormatVersion: 2\nguid: ffffffffffffffffffffffffffffffff\n");
            File.WriteAllBytes(initial.DefinitionPath + ".meta", concurrentMeta);

            publication.Rollback();

            Assert.That(
                File.ReadAllBytes(initial.DefinitionPath + ".meta"),
                Is.EqualTo(concurrentMeta));
            Assert.That(File.Exists(moved.DefinitionPath), Is.True);
        }

        [Test]
        public void PublishDoesNotRewriteOrImportByteIdenticalDefinition()
        {
            OpenApiClientDefinitionPlan plan = writer.Prepare(
                outputFolder,
                "PetStoreApi",
                "Example.Generated.PetStore");
            Assert.That(writer.Publish(plan), Is.True);
            var preservedTimestamp = new DateTime(2004, 5, 6, 7, 8, 9, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(plan.DefinitionPath, preservedTimestamp);
            importedAssetPaths.Clear();

            OpenApiClientDefinitionPlan repeated = writer.Prepare(
                outputFolder,
                "PetStoreApi",
                "Example.Generated.PetStore");
            bool changed = writer.Publish(repeated);

            Assert.That(changed, Is.False);
            Assert.That(
                File.GetLastWriteTimeUtc(plan.DefinitionPath),
                Is.EqualTo(preservedTimestamp));
            Assert.That(importedAssetPaths, Is.Empty);
        }

        [Test]
        public void PrepareNeverOverwritesForeignFileAtDeterministicPath()
        {
            string definitionPath = Path.Combine(
                outputFolder,
                "PetStoreApi.OpenApiDefinition.cs");
            const string ForeignSource = "public class UserOwnedClient {}";
            File.WriteAllText(definitionPath, ForeignSource, new UTF8Encoding(false));

            InvalidOperationException exception = Assert.Throws<SafeGenerationException>(
                () => writer.Prepare(
                    outputFolder,
                    "PetStoreApi",
                    "Example.Generated.PetStore"))!;

            Assert.That(exception.Message, Does.Contain("not owned"));
            Assert.That(File.ReadAllText(definitionPath), Is.EqualTo(ForeignSource));
            Assert.That(importedAssetPaths, Is.Empty);
        }

        [Test]
        public void PrepareSafelyReusesSpecIdFromMatchingOwnedDefinition()
        {
            OpenApiClientDefinitionPlan initial = writer.Prepare(
                outputFolder,
                "PetStoreApi",
                "Example.Generated.PetStore");
            Assert.That(writer.Publish(initial), Is.True);
            string reusableSpecId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            string ownedSource = File.ReadAllText(initial.DefinitionPath, new UTF8Encoding(false));
            string reusableSource = ownedSource
                .Replace(
                    OpenApiClientDefinitionWriter.SpecIdPrefix + initial.SpecId,
                    OpenApiClientDefinitionWriter.SpecIdPrefix + reusableSpecId)
                .Replace(
                    "        \"" + initial.SpecId + "\",",
                    "        \"" + reusableSpecId + "\",");
            Assert.That(
                reusableSource,
                Does.Contain(
                    OpenApiClientDefinitionWriter.ClientIdentityPrefix +
                    initial.ClientIdentitySha256));
            File.WriteAllText(
                initial.DefinitionPath,
                reusableSource,
                new UTF8Encoding(false));

            OpenApiClientDefinitionPlan repeated = writer.Prepare(
                outputFolder,
                "PetStoreApi",
                "Example.Generated.PetStore");

            Assert.That(repeated.SpecId, Is.EqualTo(reusableSpecId));
        }

        [Test]
        public void PrepareReadsExistingGeneratedDefinitionWithCrLfLineEndings()
        {
            OpenApiClientDefinitionPlan initial = PublishInitialDefinition();
            string source = File.ReadAllText(initial.DefinitionPath, new UTF8Encoding(false));
            File.WriteAllText(
                initial.DefinitionPath,
                source.Replace("\n", "\r\n"),
                new UTF8Encoding(false));

            OpenApiClientDefinitionPlan repeated = writer.Prepare(
                outputFolder,
                "PetStoreApi",
                "Example.Generated.PetStore");

            Assert.That(repeated.SpecId, Is.EqualTo(initial.SpecId));
        }

        [Test]
        public void NamespaceChangePreservesSpecIdDefinitionPathAndMetaGuid()
        {
            OpenApiClientDefinitionPlan initial = writer.Prepare(
                outputFolder,
                "PetStoreApi",
                "Example.Generated.PetStore");
            Assert.That(writer.Publish(initial), Is.True);
            string metaPath = initial.DefinitionPath + ".meta";
            const string Meta = "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\n";
            File.WriteAllText(metaPath, Meta, new UTF8Encoding(false));
            importedAssetPaths.Clear();

            OpenApiClientDefinitionPlan renamed = writer.Prepare(
                outputFolder,
                "PetStoreApi",
                "Example.Renamed.PetStore");
            bool changed = writer.Publish(renamed);

            Assert.That(changed, Is.True);
            Assert.That(renamed.SpecId, Is.EqualTo(initial.SpecId));
            Assert.That(renamed.DefinitionPath, Is.EqualTo(initial.DefinitionPath));
            Assert.That(renamed.ClientIdentitySha256, Is.Not.EqualTo(initial.ClientIdentitySha256));
            Assert.That(
                File.ReadAllText(renamed.DefinitionPath, new UTF8Encoding(false)),
                Does.Contain("namespace Example.Renamed.PetStore"));
            Assert.That(File.ReadAllText(metaPath), Is.EqualTo(Meta));
            Assert.That(importedAssetPaths, Is.EqualTo(new[] { renamed.DefinitionAssetPath }));
        }

        [Test]
        public void PrepareRejectsOwnedDefinitionWithTamperedIdentityHeader()
        {
            OpenApiClientDefinitionPlan initial = PublishInitialDefinition();
            string source = File.ReadAllText(initial.DefinitionPath, new UTF8Encoding(false));
            File.WriteAllText(
                initial.DefinitionPath,
                source.Replace(
                    initial.ClientIdentitySha256,
                    new string('0', initial.ClientIdentitySha256.Length)),
                new UTF8Encoding(false));

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => writer.Prepare(
                    outputFolder,
                    "PetStoreApi",
                    "Example.Renamed.PetStore"))!;

            Assert.That(exception.Message, Does.Contain("identity"));
        }

        [Test]
        public void PrepareRejectsOwnedDefinitionWithTamperedSourceNamespace()
        {
            OpenApiClientDefinitionPlan initial = PublishInitialDefinition();
            string source = File.ReadAllText(initial.DefinitionPath, new UTF8Encoding(false));
            File.WriteAllText(
                initial.DefinitionPath,
                source.Replace(
                    "namespace Example.Generated.PetStore",
                    "namespace Example.Tampered.PetStore"),
                new UTF8Encoding(false));

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => writer.Prepare(
                    outputFolder,
                    "PetStoreApi",
                    "Example.Renamed.PetStore"))!;

            Assert.That(exception.Message, Does.Contain("recognized generated format"));
        }

        [Test]
        public void PrepareRejectsOwnedDefinitionForDifferentApiName()
        {
            OpenApiClientDefinitionPlan initial = PublishInitialDefinition();
            string source = File.ReadAllText(initial.DefinitionPath, new UTF8Encoding(false));
            File.WriteAllText(
                initial.DefinitionPath,
                source.Replace("PetStoreApi", "OtherApi"),
                new UTF8Encoding(false));

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => writer.Prepare(
                    outputFolder,
                    "PetStoreApi",
                    "Example.Renamed.PetStore"))!;

            Assert.That(exception.Message, Does.Contain("different API name"));
        }

        [Test]
        public void PublishRejectsDefinitionChangedAfterPrepare()
        {
            OpenApiClientDefinitionPlan initial = PublishInitialDefinition();
            OpenApiClientDefinitionPlan renamed = writer.Prepare(
                outputFolder,
                "PetStoreApi",
                "Example.Renamed.PetStore");
            byte[] changedBytes = new UTF8Encoding(false).GetBytes(
                File.ReadAllText(initial.DefinitionPath, new UTF8Encoding(false)) + "// changed\n");
            File.WriteAllBytes(initial.DefinitionPath, changedBytes);
            importedAssetPaths.Clear();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => writer.Publish(renamed))!;

            Assert.That(exception.Message, Does.Contain("modified outside"));
            Assert.That(File.ReadAllBytes(initial.DefinitionPath), Is.EqualTo(changedBytes));
            Assert.That(importedAssetPaths, Is.Empty);
        }

        [Test]
        public void PublishRejectsAnotherValidNamespaceUpdateAfterPrepare()
        {
            OpenApiClientDefinitionPlan initial = PublishInitialDefinition();
            OpenApiClientDefinitionPlan prepared = writer.Prepare(
                outputFolder,
                "PetStoreApi",
                "Example.Prepared.PetStore");
            OpenApiClientDefinitionPlan competing = writer.Prepare(
                outputFolder,
                "PetStoreApi",
                "Example.Competing.PetStore");
            Assert.That(writer.Publish(competing), Is.True);
            byte[] competingBytes = File.ReadAllBytes(initial.DefinitionPath);
            importedAssetPaths.Clear();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => writer.Publish(prepared))!;

            Assert.That(exception.Message, Does.Contain("changed after generation was prepared"));
            Assert.That(File.ReadAllBytes(initial.DefinitionPath), Is.EqualTo(competingBytes));
            Assert.That(importedAssetPaths, Is.Empty);
        }

        [Test]
        public void PrepareRejectsSymbolicLinkAtDefinitionPath()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                Assert.Ignore("The symbolic-link fixture uses the Unix link API.");
            }

            string definitionPath = Path.Combine(
                outputFolder,
                "PetStoreApi.OpenApiDefinition.cs");
            string linkTarget = Path.Combine(projectRoot, "owned-target.cs");
            File.WriteAllText(linkTarget, "foreign", new UTF8Encoding(false));
            if (CreateUnixSymbolicLink(linkTarget, definitionPath) != 0)
            {
                Assert.Ignore("The temporary symbolic-link fixture could not be created.");
            }

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => writer.Prepare(
                    outputFolder,
                    "PetStoreApi",
                    "Example.Generated.PetStore"))!;

            Assert.That(exception.Message, Does.Contain("symbolic link"));
            Assert.That(File.ReadAllText(linkTarget), Is.EqualTo("foreign"));
        }

        [Test]
        public void PrepareRejectsOutputOutsideAssets()
        {
            string outsideAssets = Path.Combine(projectRoot, "Generated");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => writer.Prepare(
                    outsideAssets,
                    "PetStoreApi",
                    "Example.Generated"))!;

            Assert.That(exception.Message, Does.Contain("Assets"));
        }

        [Test]
        public void PrepareRequiresNearestTargetAsmdefToReferenceRuntime()
        {
            string nestedOutput = Path.Combine(outputFolder, "Nested");
            Directory.CreateDirectory(nestedOutput);
            WriteAsmdef(outputFolder, "Example.Clients.Nested", "Some.Other.Assembly");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => writer.Prepare(
                    nestedOutput,
                    "PetStoreApi",
                    "Example.Generated"))!;

            Assert.That(exception.Message, Does.Contain("Example.Clients.Nested"));
            Assert.That(exception.Message, Does.Contain("must reference"));
        }

        [Test]
        public void PrepareAcceptsRuntimeAsmdefGuidReference()
        {
            string guidOutput = Path.Combine(projectRoot, "Assets", "GuidClients");
            Directory.CreateDirectory(guidOutput);
            WriteAsmdef(
                guidOutput,
                "Example.GuidClients",
                "GUID:2734be4203e44073a484476db8e446f3");

            OpenApiClientDefinitionPlan plan = writer.Prepare(
                guidOutput,
                "GuidClientApi",
                "Example.Generated");

            Assert.That(plan.TargetAssemblyName, Is.EqualTo("Example.GuidClients"));
        }

        [TestCase("9PetStore")]
        [TestCase("class")]
        [TestCase("Pet-Store")]
        public void PrepareRejectsInvalidApiName(string apiName)
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => writer.Prepare(outputFolder, apiName, "Example.Generated"))!;

            Assert.That(exception.Message, Does.Contain("API name"));
        }

        [TestCase("Example..Generated")]
        [TestCase("Example.class")]
        [TestCase("Example.Generated-")]
        public void PrepareRejectsInvalidNamespace(string generatedNamespace)
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => writer.Prepare(outputFolder, "PetStoreApi", generatedNamespace))!;

            Assert.That(exception.Message, Does.Contain("namespace"));
        }

        OpenApiClientDefinitionPlan PublishInitialDefinition()
        {
            OpenApiClientDefinitionPlan initial = writer.Prepare(
                outputFolder,
                "PetStoreApi",
                "Example.Generated.PetStore");
            Assert.That(writer.Publish(initial), Is.True);
            importedAssetPaths.Clear();
            return initial;
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

        [DllImport("libc", EntryPoint = "symlink", SetLastError = true)]
        static extern int CreateUnixSymbolicLink(string target, string linkPath);
    }
}
