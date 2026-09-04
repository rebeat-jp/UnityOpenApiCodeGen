#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
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
            string reusableSpecId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            string ownedHeader = string.Join(
                "\n",
                OpenApiClientDefinitionWriter.OwnedFileHeader,
                OpenApiClientDefinitionWriter.ClientIdentityPrefix + initial.ClientIdentitySha256,
                OpenApiClientDefinitionWriter.SpecIdPrefix + reusableSpecId,
                string.Empty);
            File.WriteAllText(initial.DefinitionPath, ownedHeader, new UTF8Encoding(false));

            OpenApiClientDefinitionPlan repeated = writer.Prepare(
                outputFolder,
                "PetStoreApi",
                "Example.Generated.PetStore");

            Assert.That(repeated.SpecId, Is.EqualTo(reusableSpecId));
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
    }
}
