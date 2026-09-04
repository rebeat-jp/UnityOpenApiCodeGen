#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using Rhycol.OpenApiCodeGen.Editor.Generation;
using Rhycol.OpenApiCodeGen.SourceGenerator;
using Rhycol.OpenApiCodeGen.SourceGenerator.Editor;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    internal sealed class Phase6GraphLoadingTests
    {
        private const string SpecId = "0123456789abcdef0123456789abcdef";

        private string projectRoot = string.Empty;
        private RecordingImporter importer = null!;

        [SetUp]
        public void SetUp()
        {
            projectRoot = Path.Combine(
                Path.GetTempPath(),
                "OpenApiCodeGenPhase6GraphTests",
                Guid.NewGuid().ToString("N"));
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
        public async Task NormalizeAndCacheAsyncPublishesBundleV2ForLocalExternalYaml()
        {
            WriteText("Assets/Specs/root.json", CreateRootWithReference("child.yaml#/components/schemas/Pet"));
            WriteText(
                "Assets/Specs/child.yaml",
                "openapi: 3.1.0\n" +
                "info:\n" +
                "  title: Child\n" +
                "  version: '1'\n" +
                "paths: {}\n" +
                "components:\n" +
                "  schemas:\n" +
                "    Pet:\n" +
                "      type: object\n" +
                "      properties:\n" +
                "        name:\n" +
                "          type: string\n");

            NormalizedSpecCacheResult result = await CreateService().NormalizeAndCacheAsync(
                "Assets/Specs/root.json",
                SpecId,
                CancellationToken.None);
            string bundle = File.ReadAllText(result.AuthoritativePath, new UTF8Encoding(false));
            string manifestPath = Path.Combine(
                Path.GetDirectoryName(result.AuthoritativePath)!,
                NormalizedSpecBundleConstants.ManifestFileName);
            string manifest = File.ReadAllText(manifestPath, new UTF8Encoding(false));

            Assert.That(Path.GetFileName(result.AuthoritativePath), Is.EqualTo(NormalizedSpecBundleConstants.BundleFileName));
            Assert.That(result.Format, Is.EqualTo(OpenApiDocumentFormat.Json));
            Assert.That(result.AuthoritativeChanged, Is.True);
            Assert.That(result.MirrorChanged, Is.True);
            Assert.That(result.ManifestChanged, Is.True);
            Assert.That(bundle, Does.Contain("\"formatVersion\": 2"));
            Assert.That(bundle, Does.Contain("\"documentId\": \"root\""));
            Assert.That(bundle, Does.Contain("\"documentId\": \"Assets/Specs/child.yaml\""));
            Assert.That(bundle, Does.Contain("\"sourcePath\": \"Assets/Specs/child.yaml\""));
            Assert.That(bundle, Does.Contain("\"format\": \"yaml\""));
            Assert.That(
                bundle,
                Does.Contain("\"sourcePointer\": \"/paths/~1pets/get/responses/200/content/application~1json/schema/$ref\""));
            Assert.That(bundle, Does.Contain("\"targetPointer\": \"/components/schemas/Pet\""));
            Assert.That(manifest, Does.Contain("\"manifestVersion\": 1"));
            Assert.That(manifest, Does.Contain("\"bundleFormatVersion\": 2"));
            Assert.That(manifest, Does.Contain("\"documents\": ["));
            Assert.That(manifest, Does.Contain("\"sourcePath\": \"Assets/Specs/root.json\""));
            Assert.That(manifest, Does.Contain("\"retrievalKind\": \"local\""));
            Assert.That(File.ReadAllBytes(result.MirrorPath), Is.EqualTo(File.ReadAllBytes(result.AuthoritativePath)));
            Assert.That(importer.ImportedAssetPaths, Is.EqualTo(new[] { result.MirrorAssetPath }));
        }

        [Test]
        public async Task ExternalGraphResolvesRelativeRootAgainstTheUnityProjectRoot()
        {
            WriteText(
                "Assets/Specs/root.json",
                "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Root\",\"version\":\"1\"},\"paths\":{}}");
            string originalCurrentDirectory = Directory.GetCurrentDirectory();
            string unrelatedDirectory = Path.Combine(
                Path.GetTempPath(),
                "OpenApiCodeGenPhase6UnrelatedCwd",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(unrelatedDirectory);

            try
            {
                Directory.SetCurrentDirectory(unrelatedDirectory);
                NormalizedSpecGraph graph = await CreateLoader().LoadAsync(
                    "Assets/Specs/root.json",
                    SpecId,
                    CancellationToken.None);

                Assert.That(graph.SourcePath, Is.EqualTo("Assets/Specs/root.json"));
                Assert.That(graph.Documents, Has.Count.EqualTo(1));
            }
            finally
            {
                Directory.SetCurrentDirectory(originalCurrentDirectory);
                if (Directory.Exists(unrelatedDirectory))
                {
                    Directory.Delete(unrelatedDirectory, true);
                }
            }
        }

        [Test]
        public async Task NormalizeAndCacheAsyncPreservesSchemaIdentityKeywordsForAnalyzer()
        {
            WriteText(
                "Assets/Specs/root.json",
                "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Root\",\"version\":\"1\"}," +
                "\"paths\":{},\"components\":{\"schemas\":{\"Pet\":{" +
                "\"$id\":\"urn:example:pet\",\"$anchor\":\"Pet\"," +
                "\"$dynamicAnchor\":\"PetDynamic\"," +
                "\"$dynamicRef\":\"#/components/schemas/Base\"}}}}");

            NormalizedSpecCacheResult result = await CreateService().NormalizeAndCacheAsync(
                "Assets/Specs/root.json",
                SpecId,
                CancellationToken.None);
            string bundle = File.ReadAllText(result.AuthoritativePath, new UTF8Encoding(false));

            Assert.That(bundle, Does.Contain("\"name\": \"$id\""));
            Assert.That(bundle, Does.Contain("\"name\": \"$anchor\""));
            Assert.That(bundle, Does.Contain("\"name\": \"$dynamicAnchor\""));
            Assert.That(bundle, Does.Contain("\"name\": \"$dynamicRef\""));
            Assert.That(bundle, Does.Contain("\"value\": \"#/components/schemas/Base\""));
            Assert.That(bundle, Does.Contain("\"referenceEdges\": []"));
            Assert.That(bundle, Does.Not.Contain("/components/schemas/Pet/$dynamicRef"));
        }

        [Test]
        public async Task NormalizeAndCacheAsyncDoesNotRewriteIdenticalBundleOrManifest()
        {
            WriteText("Assets/Specs/root.json", "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Root\",\"version\":\"1\"},\"paths\":{}}");
            NormalizedSpecCacheService service = CreateService();
            NormalizedSpecCacheResult first = await service.NormalizeAndCacheAsync(
                "Assets/Specs/root.json",
                SpecId,
                CancellationToken.None);
            string manifestPath = Path.Combine(
                Path.GetDirectoryName(first.AuthoritativePath)!,
                NormalizedSpecBundleConstants.ManifestFileName);
            DateTime bundleTimestamp = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            DateTime mirrorTimestamp = new DateTime(2002, 3, 4, 5, 6, 7, DateTimeKind.Utc);
            DateTime manifestTimestamp = new DateTime(2003, 4, 5, 6, 7, 8, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(first.AuthoritativePath, bundleTimestamp);
            File.SetLastWriteTimeUtc(first.MirrorPath, mirrorTimestamp);
            File.SetLastWriteTimeUtc(manifestPath, manifestTimestamp);
            importer.ImportedAssetPaths.Clear();

            NormalizedSpecCacheResult second = await service.NormalizeAndCacheAsync(
                "Assets/Specs/root.json",
                SpecId,
                CancellationToken.None);

            Assert.That(second.AuthoritativeChanged, Is.False);
            Assert.That(second.MirrorChanged, Is.False);
            Assert.That(second.ManifestChanged, Is.False);
            Assert.That(File.GetLastWriteTimeUtc(second.AuthoritativePath), Is.EqualTo(bundleTimestamp));
            Assert.That(File.GetLastWriteTimeUtc(second.MirrorPath), Is.EqualTo(mirrorTimestamp));
            Assert.That(File.GetLastWriteTimeUtc(manifestPath), Is.EqualTo(manifestTimestamp));
            Assert.That(importer.ImportedAssetPaths, Is.Empty);
        }

        [Test]
        public async Task NormalizeAndCacheAsyncRetainsLastKnownGoodGenerationWhenGraphLoadFails()
        {
            WriteText("Assets/Specs/root.json", CreateRootWithReference("child.yaml#/components/schemas/Pet"));
            WriteText("Assets/Specs/child.yaml", ValidChildYaml());
            NormalizedSpecCacheService service = CreateService();
            NormalizedSpecCacheResult first = await service.NormalizeAndCacheAsync(
                "Assets/Specs/root.json",
                SpecId,
                CancellationToken.None);
            string manifestPath = Path.Combine(
                Path.GetDirectoryName(first.AuthoritativePath)!,
                NormalizedSpecBundleConstants.ManifestFileName);
            byte[] oldBundle = File.ReadAllBytes(first.AuthoritativePath);
            byte[] oldMirror = File.ReadAllBytes(first.MirrorPath);
            byte[] oldManifest = File.ReadAllBytes(manifestPath);
            importer.ImportedAssetPaths.Clear();
            WriteText("Assets/Specs/child.yaml", "openapi: [\n");

            Exception exception = await CaptureExceptionAsync<Exception>(async () =>
                await service.NormalizeAndCacheAsync("Assets/Specs/root.json", SpecId, CancellationToken.None));

            Assert.That(exception, Is.Not.Null);
            Assert.That(File.ReadAllBytes(first.AuthoritativePath), Is.EqualTo(oldBundle));
            Assert.That(File.ReadAllBytes(first.MirrorPath), Is.EqualTo(oldMirror));
            Assert.That(File.ReadAllBytes(manifestPath), Is.EqualTo(oldManifest));
            Assert.That(importer.ImportedAssetPaths, Is.Empty);
        }

        [Test]
        public async Task NormalizeAndCacheAsyncRedactsQueriesButFetchesQueryIdentity()
        {
            using (var server = new LoopbackHttpServer())
            {
                server.Add(
                    "/root?token=secret",
                    200,
                    "application/json",
                    CreateRootWithReference("child.json?version=1#/components/schemas/Pet"));
                server.Add(
                    "/child.json?version=1",
                    200,
                    "application/json",
                    CreateOpenApiDocument("Child", "\"Pet\":{\"type\":\"object\"}"));
                server.Start();

                NormalizedSpecCacheResult result = await CreateService().NormalizeAndCacheAsync(
                    server.Url("root?token=secret"),
                    SpecId,
                    CancellationToken.None);
                string bundle = File.ReadAllText(result.AuthoritativePath, new UTF8Encoding(false));
                string manifest = ReadManifest(result);

                Assert.That(result.SourcePath, Does.Not.Contain("?"));
                Assert.That(result.WarningMessage, Does.Contain("Re-enter"));
                Assert.That(bundle, Does.Not.Contain("secret"));
                Assert.That(bundle, Does.Not.Contain("version=1"));
                Assert.That(manifest, Does.Not.Contain("secret"));
                Assert.That(manifest, Does.Not.Contain("version=1"));
                Assert.That(manifest, Does.Contain("\"retrievalKind\": \"http\""));
                Assert.That(manifest, Does.Contain("\"httpStatus\": 200"));
                Assert.That(server.RequestTargets, Does.Contain("/root?token=secret"));
                Assert.That(server.RequestTargets, Does.Contain("/child.json?version=1"));
            }
        }

        [Test]
        public async Task NormalizeAndCacheAsyncRecordsRedirectWithoutPersistingQuery()
        {
            using (var server = new LoopbackHttpServer())
            {
                server.AddRedirect("/redirect?token=secret", "/root.json?token=secret");
                server.Add(
                    "/root.json?token=secret",
                    200,
                    "application/json",
                    "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Root\",\"version\":\"1\"},\"paths\":{}}");
                server.Start();

                NormalizedSpecCacheResult result = await CreateService().NormalizeAndCacheAsync(
                    server.Url("redirect?token=secret"),
                    SpecId,
                    CancellationToken.None);
                string manifest = ReadManifest(result);

                Assert.That(manifest, Does.Contain("\"redirectCount\": 1"));
                Assert.That(manifest, Does.Contain("\"requestedSource\": \"http://127.0.0.1:"));
                Assert.That(manifest, Does.Contain("/redirect\""));
                Assert.That(manifest, Does.Contain("\"effectiveSource\": \"http://127.0.0.1:"));
                Assert.That(manifest, Does.Contain("/root.json\""));
                Assert.That(manifest, Does.Not.Contain("secret"));
                Assert.That(result.SourcePath, Does.EndWith("/root.json"));
            }
        }

        [Test]
        public async Task ExternalGraphAllowsCrossAuthorityRedirects()
        {
            using (var redirectServer = new LoopbackHttpServer())
            using (var targetServer = new LoopbackHttpServer())
            {
                targetServer.Add(
                    "/root.json",
                    200,
                    "application/json",
                    "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Root\",\"version\":\"1\"},\"paths\":{}}");
                targetServer.Start();
                string crossHostTarget = targetServer.Url("root.json")
                    .Replace("127.0.0.1", "localhost");
                redirectServer.AddRedirect("/redirect", crossHostTarget);
                redirectServer.Start();

                NormalizedSpecGraph graph = await CreateLoader().LoadAsync(
                    redirectServer.Url("redirect"),
                    SpecId,
                    CancellationToken.None);

                Assert.That(graph.RedirectCount, Is.EqualTo(1));
                Assert.That(graph.SourcePath, Is.EqualTo(crossHostTarget));
                Assert.That(targetServer.RequestTargets, Does.Contain("/root.json"));
            }
        }

        [Test]
        public async Task ExternalGraphRejectsFormatConflictAndUnknownMediaType()
        {
            using (var server = new LoopbackHttpServer())
            {
                server.Add("/conflict.json", 200, "application/yaml", "{}");
                server.Add("/unknown", 200, "application/octet-stream", "{}");
                server.Start();
                ExternalSpecGraphLoader loader = CreateLoader();

                Exception conflict = await CaptureExceptionAsync<Exception>(async () =>
                    await loader.LoadAsync(server.Url("conflict.json"), SpecId, CancellationToken.None));
                Exception unknown = await CaptureExceptionAsync<Exception>(async () =>
                    await loader.LoadAsync(server.Url("unknown"), SpecId, CancellationToken.None));

                Assert.That(conflict.Message, Does.Contain("disagree"));
                Assert.That(unknown.Message, Does.Contain("neither a recognized extension"));
            }
        }

        [Test]
        public async Task ExternalGraphRemoteFailureDoesNotRetainQueryInExceptionChain()
        {
            using (var server = new LoopbackHttpServer())
            {
                server.Add("/failure?token=secret", 500, "application/json", "{}");
                server.Start();

                Exception failure = await CaptureExceptionAsync<Exception>(async () =>
                    await CreateLoader().LoadAsync(
                        server.Url("failure?token=secret"),
                        SpecId,
                        CancellationToken.None));

                Assert.That(failure.ToString(), Does.Not.Contain("secret"));
                Assert.That(failure.ToString(), Does.Not.Contain("token="));
            }
        }

        [Test]
        public async Task ExternalGraphRemoteFailureDoesNotExposeCredentialsOrResponseHeaders()
        {
            using (var server = new LoopbackHttpServer())
            {
                server.AddWithHeader(
                    "/failure?token=query-secret",
                    500,
                    "application/json",
                    "{}",
                    "X-Secret-Header",
                    "header-secret");
                server.Start();

                Exception queryFailure = await CaptureExceptionAsync<Exception>(async () =>
                    await CreateLoader().LoadAsync(
                        server.Url("failure?token=query-secret"),
                        SpecId,
                        CancellationToken.None));

                Assert.That(queryFailure.ToString(), Does.Not.Contain("query-secret"));
                Assert.That(queryFailure.ToString(), Does.Not.Contain("header-secret"));
                Assert.That(queryFailure.ToString(), Does.Not.Contain("X-Secret-Header"));

                Exception userInfoFailure = await CaptureExceptionAsync<Exception>(async () =>
                    await CreateLoader().LoadAsync(
                        "http://user:password@127.0.0.1/openapi.json",
                        SpecId,
                        CancellationToken.None));

                Assert.That(userInfoFailure.ToString(), Does.Not.Contain("user:password@"));
                Assert.That(userInfoFailure.ToString(), Does.Not.Contain("password"));
            }
        }

        [Test]
        public async Task ExternalGraphRejectsUnsafeRootAndExternalReferences()
        {
            ExternalSpecGraphLoader loader = CreateLoader();
            Exception ftp = await CaptureExceptionAsync<Exception>(async () =>
                await loader.LoadAsync("ftp://127.0.0.1/openapi.json", SpecId, CancellationToken.None));
            Exception userInfo = await CaptureExceptionAsync<Exception>(async () =>
                await loader.LoadAsync("http://user:password@127.0.0.1/openapi.json", SpecId, CancellationToken.None));

            Assert.That(ftp.Message, Does.Contain("HTTP(S)"));
            Assert.That(userInfo.Message, Does.Contain("userinfo"));

            string outsidePath = Path.Combine(
                Directory.GetParent(projectRoot)!.FullName,
                "outside-" + Guid.NewGuid().ToString("N") + ".json");
            WriteText("Assets/Specs/root.json", CreateRootWithReference("../../../" + Path.GetFileName(outsidePath)));
            File.WriteAllText(outsidePath, "{}", new UTF8Encoding(false));
            try
            {
                Exception outside = await CaptureExceptionAsync<Exception>(async () =>
                    await loader.LoadAsync("Assets/Specs/root.json", SpecId, CancellationToken.None));
                Assert.That(outside.Message, Does.Contain("inside the Unity project"));
            }
            finally
            {
                if (File.Exists(outsidePath))
                {
                    File.Delete(outsidePath);
                }
            }

            WriteText("Assets/Specs/root.json", CreateRootWithReference("file:///tmp/outside.json"));
            Exception fileUri = await CaptureExceptionAsync<Exception>(async () =>
                await loader.LoadAsync("Assets/Specs/root.json", SpecId, CancellationToken.None));
            Assert.That(fileUri.Message, Does.Contain("Explicit file URI"));
        }

        [Test]
        public void RepairPendingPublicationRejectsMalformedAndPathTraversalMarkers()
        {
            NormalizedSpecCacheService service = CreateService();
            string markerPath = GetPublishPendingPath(SpecId);
            Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);

            File.WriteAllText(
                markerPath,
                "version=1\nstate=pending\ncount=3\n",
                new UTF8Encoding(false));
            IOException malformed = Assert.Throws<IOException>(
                () => service.RepairPendingPublicationForSpec(SpecId));
            Assert.That(malformed.Message, Does.Contain("artifact count"));
            Assert.That(File.Exists(markerPath), Is.True);

            File.WriteAllText(
                markerPath,
                CreateStructuredMarker(
                    "Library/OpenApiCodeGen/SourceGenerator/SpecCache/" + SpecId + "/normalized-v2.json",
                    "Library/OpenApiCodeGen/SourceGenerator/SpecCache/" + SpecId + "/manifest-v1.json",
                    "../../outside.txt"),
                new UTF8Encoding(false));
            IOException traversal = Assert.Throws<IOException>(
                () => service.RepairPendingPublicationForSpec(SpecId));
            Assert.That(traversal.Message, Does.Contain("unsafe target path"));
            Assert.That(File.Exists(markerPath), Is.True);
        }

        [Test]
        public async Task ExternalGraphCanonicalizesFragmentsAndDeduplicatesDocuments()
        {
            WriteText(
                "Assets/Specs/root.json",
                "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Root\",\"version\":\"1\"}," +
                "\"paths\":{},\"components\":{\"schemas\":{" +
                "\"Escaped\":{\"$ref\":\"sub/../child.json#/components/schemas/a~1b~0c\"}," +
                "\"Whole\":{\"$ref\":\"child.json#\"}}}}");
            WriteText(
                "Assets/Specs/child.json",
                "{\"components\":{\"schemas\":{\"a/b~c\":{\"type\":\"object\"}}}}");

            NormalizedSpecGraph graph = await CreateLoader().LoadAsync(
                "Assets/Specs/root.json",
                SpecId,
                CancellationToken.None);

            Assert.That(graph.Documents, Has.Count.EqualTo(2));
            Assert.That(graph.Documents[0].DocumentId, Is.EqualTo("root"));
            Assert.That(graph.Documents[1].DocumentId, Is.EqualTo("Assets/Specs/child.json"));
            Assert.That(graph.Edges, Has.Count.EqualTo(2));
            Assert.That(graph.Edges[0].TargetDocumentId, Is.EqualTo("Assets/Specs/child.json"));
            Assert.That(graph.Edges[0].TargetPointer, Is.EqualTo("/components/schemas/a~1b~0c"));
            Assert.That(graph.Edges[1].TargetDocumentId, Is.EqualTo("Assets/Specs/child.json"));
            Assert.That(graph.Edges[1].TargetPointer, Is.EqualTo(string.Empty));
        }

        [Test]
        public async Task ExternalGraphAcceptsKnownFormatFromEitherExtensionOrContentType()
        {
            using (var server = new LoopbackHttpServer())
            {
                server.Add("/without-extension", 200, "application/json", "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Json\",\"version\":\"1\"},\"paths\":{}}");
                server.Add("/vendor", 200, "application/vnd.api+json", "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Vendor\",\"version\":\"1\"},\"paths\":{}}");
                server.Add("/yaml", 200, "text/x-yaml", ValidChildYaml());
                server.Add("/extension.yml", 200, string.Empty, ValidChildYaml());
                server.Start();

                ExternalSpecGraphLoader loader = CreateLoader();
                NormalizedSpecGraph json = await loader.LoadAsync(
                    server.Url("without-extension"),
                    SpecId,
                    CancellationToken.None);
                NormalizedSpecGraph vendorJson = await loader.LoadAsync(
                    server.Url("vendor"),
                    SpecId,
                    CancellationToken.None);
                NormalizedSpecGraph yaml = await loader.LoadAsync(
                    server.Url("yaml"),
                    SpecId,
                    CancellationToken.None);
                NormalizedSpecGraph extensionYaml = await loader.LoadAsync(
                    server.Url("extension.yml"),
                    SpecId,
                    CancellationToken.None);

                Assert.That(json.Format, Is.EqualTo("json"));
                Assert.That(vendorJson.Format, Is.EqualTo("json"));
                Assert.That(yaml.Format, Is.EqualTo("yaml"));
                Assert.That(extensionYaml.Format, Is.EqualTo("yaml"));
            }
        }

        [Test]
        public async Task ExternalGraphRejectsRedirectChainsBeyondTheLimit()
        {
            using (var server = new LoopbackHttpServer())
            {
                for (int index = 0; index <= NormalizedSpecBundleConstants.MaximumRedirects; index++)
                {
                    server.AddRedirect("/redirect" + index, "/redirect" + (index + 1));
                }

                server.Add(
                    "/redirect" + (NormalizedSpecBundleConstants.MaximumRedirects + 1),
                    200,
                    "application/json",
                    "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Root\",\"version\":\"1\"},\"paths\":{}}");
                server.Start();

                Exception exception = await CaptureExceptionAsync<Exception>(async () =>
                    await CreateLoader().LoadAsync(
                        server.Url("redirect0"),
                        SpecId,
                        CancellationToken.None));

                Assert.That(exception.Message, Does.Contain("maximum redirect count"));
            }
        }

        [Test]
        public async Task ExternalGraphCancellationStopsAnInFlightRequest()
        {
            using (var server = new LoopbackHttpServer())
            using (var cancellation = new CancellationTokenSource())
            {
                server.AddDelayed(
                    "/slow.json",
                    200,
                    "application/json",
                    "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Slow\",\"version\":\"1\"},\"paths\":{}}",
                    500);
                server.Start();

                Task<NormalizedSpecGraph> pending = CreateLoader().LoadAsync(
                    server.Url("slow.json"),
                    SpecId,
                    cancellation.Token);
                for (int attempt = 0; attempt < 100 && server.RequestTargets.Count == 0; attempt++)
                {
                    await Task.Delay(10);
                }

                Assert.That(server.RequestTargets, Is.Not.Empty);
                cancellation.Cancel();

                OperationCanceledException exception = await CaptureExceptionAsync<OperationCanceledException>(async () =>
                    await pending);
                Assert.That(exception, Is.Not.Null);
            }
        }

        [Test]
        public async Task ExternalGraphRejectsAnOversizedDocumentBeforeParsing()
        {
            WriteBytes(
                "Assets/Specs/oversized.json",
                Encoding.UTF8.GetBytes(new string('x', NormalizedSpecBundleConstants.MaximumDocumentBytes + 1)));

            Exception exception = await CaptureExceptionAsync<Exception>(async () =>
                await CreateLoader().LoadAsync(
                    "Assets/Specs/oversized.json",
                    SpecId,
                    CancellationToken.None));

            Assert.That(exception.Message, Does.Contain("4 MiB"));
        }

        [Test]
        public async Task ExternalGraphRejectsTheMaximumDocumentCount()
        {
            const int externalDocumentCount = NormalizedSpecBundleConstants.MaximumDocumentCount;
            WriteText("Assets/Specs/root.json", CreateRootWithReferences(externalDocumentCount));
            for (int index = 0; index < externalDocumentCount; index++)
            {
                WriteText("Assets/Specs/child" + index + ".json", "{}");
            }

            Exception exception = await CaptureExceptionAsync<Exception>(async () =>
                await CreateLoader().LoadAsync(
                    "Assets/Specs/root.json",
                    SpecId,
                    CancellationToken.None));

            Assert.That(exception.Message, Does.Contain("maximum document count"));
        }

        [Test]
        public async Task ExternalGraphRejectsJsonBeyondTheMaximumParseDepth()
        {
            var source = new StringBuilder();
            for (int index = 0; index <= NormalizedSpecBundleConstants.MaximumDepth; index++)
            {
                source.Append("{\"nested\":");
            }
            source.Append("null");
            for (int index = 0; index <= NormalizedSpecBundleConstants.MaximumDepth; index++)
            {
                source.Append('}');
            }
            WriteText("Assets/Specs/root.json", source.ToString());

            Exception exception = await CaptureExceptionAsync<Exception>(async () =>
                await CreateLoader().LoadAsync(
                    "Assets/Specs/root.json",
                    SpecId,
                    CancellationToken.None));

            Assert.That(exception.Message, Does.Contain("depth").IgnoreCase);
        }

        [Test]
        public async Task ExternalGraphRejectsSymbolicLinkTraversalInsideTheProject()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                Assert.Ignore("The symbolic-link fixture uses the Unix link API.");
            }

            string outsideDirectory = Path.Combine(
                Path.GetTempPath(),
                "OpenApiCodeGenPhase6SymlinkTarget",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outsideDirectory);
            string outsidePath = Path.Combine(outsideDirectory, "outside.json");
            string linkPath = Path.Combine(projectRoot, "Assets", "Specs", "linked.json");
            File.WriteAllText(
                outsidePath,
                "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Outside\",\"version\":\"1\"},\"paths\":{}}",
                new UTF8Encoding(false));

            try
            {
                if (CreateUnixSymbolicLink(outsidePath, linkPath) != 0)
                {
                    Assert.Ignore("The temporary symbolic-link fixture could not be created.");
                }

                WriteText("Assets/Specs/root.json", CreateRootWithReference("linked.json"));
                Exception exception = await CaptureExceptionAsync<Exception>(async () =>
                    await CreateLoader().LoadAsync(
                        "Assets/Specs/root.json",
                        SpecId,
                        CancellationToken.None));

                Assert.That(exception.Message, Does.Contain("symbolic link"));
            }
            finally
            {
                if (File.Exists(linkPath))
                {
                    File.Delete(linkPath);
                }

                if (Directory.Exists(outsideDirectory))
                {
                    Directory.Delete(outsideDirectory, true);
                }
            }
        }

        [Test]
        public async Task ExternalGraphRejectsInvalidJsonPointerFragments()
        {
            WriteText("Assets/Specs/child.json", "{}");
            ExternalSpecGraphLoader loader = CreateLoader();

            WriteText("Assets/Specs/root.json", CreateRootWithReference("child.json#not-a-pointer"));
            Exception nonPointer = await CaptureExceptionAsync<Exception>(async () =>
                await loader.LoadAsync("Assets/Specs/root.json", SpecId, CancellationToken.None));
            Assert.That(nonPointer.Message, Does.Contain("JSON Pointer"));

            WriteText("Assets/Specs/root.json", CreateRootWithReference("child.json#/components~2schemas"));
            Exception badEscape = await CaptureExceptionAsync<Exception>(async () =>
                await loader.LoadAsync("Assets/Specs/root.json", SpecId, CancellationToken.None));
            Assert.That(badEscape.Message, Does.Contain("invalid JSON Pointer escape"));
        }

        [Test]
        public async Task ExternalGraphRejectsRemoteUrlsContainingWhitespace()
        {
            Exception exception = await CaptureExceptionAsync<Exception>(async () =>
                await CreateLoader().LoadAsync(
                    "http://127.0.0.1/open api.json",
                    SpecId,
                    CancellationToken.None));

            Assert.That(exception.Message, Does.Contain("whitespace"));
        }

        [Test]
        public async Task ExternalGraphRejectsAggregateGraphSizeBeyondTheLimit()
        {
            const int childCount = 9;
            const int childSize = 3_800_000;
            WriteText("Assets/Specs/root.json", CreateRootWithReferences(childCount));
            string child = CreatePaddedJson(childSize);
            for (int index = 0; index < childCount; index++)
            {
                WriteText("Assets/Specs/child" + index + ".json", child);
            }

            Exception exception = await CaptureExceptionAsync<Exception>(async () =>
                await CreateLoader().LoadAsync(
                    "Assets/Specs/root.json",
                    SpecId,
                    CancellationToken.None));

            Assert.That(exception.Message, Does.Contain("maximum size of 32 MiB"));
        }

        [Test]
        public async Task NormalizeAndCacheAsyncKeepsGenerationOnMirrorFailure()
        {
            WriteText("Assets/Specs/root.json", "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Root\",\"version\":\"1\"},\"paths\":{}}");
            NormalizedSpecCacheService firstService = CreateService();
            NormalizedSpecCacheResult first = await firstService.NormalizeAndCacheAsync(
                "Assets/Specs/root.json",
                SpecId,
                CancellationToken.None);
            string manifestPath = Path.Combine(
                Path.GetDirectoryName(first.AuthoritativePath)!,
                NormalizedSpecBundleConstants.ManifestFileName);
            byte[] oldBundle = File.ReadAllBytes(first.AuthoritativePath);
            byte[] oldMirror = File.ReadAllBytes(first.MirrorPath);
            byte[] oldManifest = File.ReadAllBytes(manifestPath);
            WriteText(
                "Assets/Specs/root.json",
                "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Changed\",\"version\":\"1\"},\"paths\":{}}");
            var failingWriter = new FailOnPathWriter(first.MirrorPath);
            var failingService = new NormalizedSpecCacheService(
                projectRoot,
                new RawJsonNormalizer(),
                new RawYamlNormalizer(),
                failingWriter,
                importer);

            Exception failure = await CaptureExceptionAsync<Exception>(async () =>
                await failingService.NormalizeAndCacheAsync("Assets/Specs/root.json", SpecId, CancellationToken.None));

            Assert.That(failure, Is.Not.Null);
            Assert.That(File.ReadAllBytes(first.AuthoritativePath), Is.EqualTo(oldBundle));
            Assert.That(File.ReadAllBytes(first.MirrorPath), Is.EqualTo(oldMirror));
            Assert.That(File.ReadAllBytes(manifestPath), Is.EqualTo(oldManifest));
        }

        [Test]
        public void GenerateRecoversThePreviousDefinitionWhenThePendingMarkerNamesAChangedOutputPath()
        {
            string rawPath = Path.Combine(projectRoot, "Assets", "Specs", "root.json");
            string firstOutputFolder = Path.Combine(projectRoot, "Assets", "Clients", "PetStore");
            string changedOutputFolder = Path.Combine(projectRoot, "Assets", "Clients", "Renamed");
            Directory.CreateDirectory(firstOutputFolder);
            Directory.CreateDirectory(changedOutputFolder);
            WriteAsmdef(
                Path.Combine(projectRoot, "Assets", "Clients"),
                "Example.Clients",
                "Unity.OpenApiCodeGen.SourceGenerator");
            File.WriteAllText(
                rawPath,
                "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Root\",\"version\":\"1\"},\"paths\":{}}",
                new UTF8Encoding(false));

            var mirrorImporter = new RecordingImporter();
            var definitionImports = new List<string>();
            int compilationRequests = 0;
            SourceGeneratorGenerationProvider firstProvider = CreateProvider(
                mirrorImporter,
                new AtomicFileWriter(),
                definitionImports,
                () => compilationRequests++);
            GenerationRequest firstRequest = new GenerationRequest(
                rawPath,
                firstOutputFolder,
                "PetStoreApi",
                "Example.Generated.PetStore");
            GenerationResult first = firstProvider.Generate(firstRequest);
            Assert.That(first.IsSuccess, Is.True, first.Message);
            string specId = ReadSpecId(first.Message);
            string firstDefinitionPath = Path.Combine(
                firstOutputFolder,
                "PetStoreApi.OpenApiDefinition.cs");
            string changedDefinitionPath = Path.Combine(
                changedOutputFolder,
                "PetStoreApi.OpenApiDefinition.cs");
            byte[] firstDefinition = File.ReadAllBytes(firstDefinitionPath);
            string markerPath = GetPublishPendingPath(specId);

            var failingProvider = CreateProvider(
                mirrorImporter,
                new AtomicFileWriter(),
                definitionImports,
                () => compilationRequests++,
                string.Empty,
                _ => throw new IOException("Simulated changed-output import failure."));
            GenerationResult failed = failingProvider.Generate(
                new GenerationRequest(
                    rawPath,
                    changedOutputFolder,
                    "PetStoreApi",
                    "Example.Generated.PetStore"));

            Assert.That(failed.IsSuccess, Is.False);
            Assert.That(File.Exists(markerPath), Is.True);
            Assert.That(File.Exists(changedDefinitionPath), Is.False);
            Assert.That(File.ReadAllBytes(firstDefinitionPath), Is.EqualTo(firstDefinition));

            GenerationResult repaired = firstProvider.Generate(firstRequest);

            Assert.That(repaired.IsSuccess, Is.True, repaired.Message);
            Assert.That(File.Exists(markerPath), Is.False);
            Assert.That(File.Exists(changedDefinitionPath), Is.False);
            Assert.That(File.ReadAllBytes(firstDefinitionPath), Is.EqualTo(firstDefinition));
        }

        [Test]
        public void GenerateRestoresAllArtifactsWhenDefinitionPublicationFails()
        {
            string rawPath = Path.Combine(projectRoot, "Assets", "Specs", "root.json");
            string changedRawPath = Path.Combine(projectRoot, "Assets", "Specs", "root.yaml");
            string outputFolder = Path.Combine(projectRoot, "Assets", "Clients", "PetStore");
            Directory.CreateDirectory(outputFolder);
            WriteAsmdef(
                Path.Combine(projectRoot, "Assets", "Clients"),
                "Example.Clients",
                "Unity.OpenApiCodeGen.SourceGenerator");
            File.WriteAllText(
                rawPath,
                "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Root\",\"version\":\"1\"},\"paths\":{}}",
                new UTF8Encoding(false));

            var mirrorImporter = new RecordingImporter();
            var definitionImports = new List<string>();
            int compilationRequests = 0;
            SourceGeneratorGenerationProvider firstProvider = CreateProvider(
                mirrorImporter,
                new AtomicFileWriter(),
                definitionImports,
                () => compilationRequests++);
            GenerationRequest request = new GenerationRequest(
                rawPath,
                outputFolder,
                "PetStoreApi",
                "Example.Generated.PetStore");

            GenerationResult first = firstProvider.Generate(request);
            Assert.That(first.IsSuccess, Is.True, first.Message);
            string specId = ReadSpecId(first.Message);
            string definitionPath = Path.Combine(
                outputFolder,
                "PetStoreApi.OpenApiDefinition.cs");
            string manifestPath = Path.Combine(
                projectRoot,
                NormalizedSpecBundleConstants.AuthoritativeCacheRelativePath,
                specId,
                NormalizedSpecBundleConstants.ManifestFileName);
            string mirrorPath = Path.Combine(
                projectRoot,
                NormalizedSpecBundleConstants.CompilerMirrorRelativePath,
                specId + NormalizedSpecBundleConstants.AdditionalFileSuffix);
            byte[] oldBundle = File.ReadAllBytes(Path.Combine(
                projectRoot,
                NormalizedSpecBundleConstants.AuthoritativeCacheRelativePath,
                specId,
                NormalizedSpecBundleConstants.BundleFileName));
            byte[] oldManifest = File.ReadAllBytes(manifestPath);
            byte[] oldMirror = File.ReadAllBytes(mirrorPath);
            byte[] oldDefinition = File.ReadAllBytes(definitionPath);

            File.WriteAllText(
                changedRawPath,
                "openapi: 3.1.0\ninfo:\n  title: Changed\n  version: '1'\npaths: {}\n",
                new UTF8Encoding(false));
            mirrorImporter.ImportedAssetPaths.Clear();
            definitionImports.Clear();
            var failingProvider = CreateProvider(
                mirrorImporter,
                new AtomicFileWriter(),
                definitionImports,
                () => compilationRequests++,
                string.Empty,
                _ => throw new IOException("Simulated definition import failure."));

            GenerationResult failure = failingProvider.Generate(
                new GenerationRequest(
                    changedRawPath,
                    outputFolder,
                    "PetStoreApi",
                    "Example.Generated.PetStore"));

            Assert.That(failure.IsSuccess, Is.False);
            Assert.That(File.ReadAllBytes(Path.Combine(
                projectRoot,
                NormalizedSpecBundleConstants.AuthoritativeCacheRelativePath,
                specId,
                NormalizedSpecBundleConstants.BundleFileName)), Is.EqualTo(oldBundle));
            Assert.That(File.ReadAllBytes(manifestPath), Is.EqualTo(oldManifest));
            Assert.That(File.ReadAllBytes(mirrorPath), Is.EqualTo(oldMirror));
            Assert.That(File.ReadAllBytes(definitionPath), Is.EqualTo(oldDefinition));
            Assert.That(File.Exists(Path.Combine(
                projectRoot,
                NormalizedSpecBundleConstants.AuthoritativeCacheRelativePath,
                specId,
                NormalizedSpecBundleConstants.PublishPendingFileName)), Is.True);
            Assert.That(compilationRequests, Is.EqualTo(1));
            Assert.That(definitionImports, Is.Empty);

            GenerationResult repaired = firstProvider.Generate(request);

            Assert.That(repaired.IsSuccess, Is.True, repaired.Message);
            Assert.That(File.Exists(Path.Combine(
                projectRoot,
                NormalizedSpecBundleConstants.AuthoritativeCacheRelativePath,
                specId,
                NormalizedSpecBundleConstants.PublishPendingFileName)), Is.False);
            Assert.That(compilationRequests, Is.EqualTo(1));
        }

        private NormalizedSpecCacheService CreateService()
        {
            return new NormalizedSpecCacheService(
                projectRoot,
                new RawJsonNormalizer(),
                new RawYamlNormalizer(),
                new AtomicFileWriter(),
                importer);
        }

        private ExternalSpecGraphLoader CreateLoader()
        {
            return new ExternalSpecGraphLoader(
                projectRoot,
                new RawJsonNormalizer(),
                new RawYamlNormalizer());
        }

        private string ReadManifest(NormalizedSpecCacheResult result)
        {
            string path = Path.Combine(
                Path.GetDirectoryName(result.AuthoritativePath)!,
                NormalizedSpecBundleConstants.ManifestFileName);
            return File.ReadAllText(path, new UTF8Encoding(false));
        }

        private string GetPublishPendingPath(string specId)
        {
            return Path.Combine(
                projectRoot,
                NormalizedSpecBundleConstants.AuthoritativeCacheRelativePath,
                specId,
                NormalizedSpecBundleConstants.PublishPendingFileName);
        }

        private static string CreateStructuredMarker(params string[] relativePaths)
        {
            var builder = new StringBuilder();
            builder.Append("version=1\nstate=pending\ncount=");
            builder.Append(relativePaths.Length);
            builder.Append('\n');
            for (int index = 0; index < relativePaths.Length; index++)
            {
                builder.Append("target=");
                builder.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(relativePaths[index])));
                builder.Append('\n');
            }

            return builder.ToString();
        }

        private void WriteText(string relativePath, string content)
        {
            string path = Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        private void WriteBytes(string relativePath, byte[] content)
        {
            string path = Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, content);
        }

        private static string ValidChildYaml()
        {
            return "openapi: 3.1.0\n" +
                   "info:\n" +
                   "  title: Child\n" +
                   "  version: '1'\n" +
                   "paths: {}\n" +
                   "components:\n" +
                   "  schemas:\n" +
                   "    Pet:\n" +
                   "      type: object\n";
        }

        private static string CreateRootWithReference(string reference)
        {
            return "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Root\",\"version\":\"1\"}," +
                   "\"paths\":{\"/pets\":{\"get\":{\"operationId\":\"getPet\",\"responses\":{\"200\":{" +
                   "\"description\":\"OK\",\"content\":{\"application/json\":{\"schema\":{" +
                   "\"$ref\":\"" + reference + "\"}}}}}}}},\"components\":{}}";
        }

        private static string CreateRootWithReferences(int count)
        {
            var builder = new StringBuilder();
            builder.Append("{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Root\",\"version\":\"1\"},");
            builder.Append("\"paths\":{},\"components\":{\"schemas\":{");
            for (int index = 0; index < count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                builder.Append("\"Schema");
                builder.Append(index);
                builder.Append("\":{\"$ref\":\"child");
                builder.Append(index);
                builder.Append(".json\"}");
            }

            builder.Append("}}}");
            return builder.ToString();
        }

        private static string CreatePaddedJson(int targetBytes)
        {
            const string prefix = "{\"padding\":\"";
            const string suffix = "\"}";
            return prefix + new string('a', targetBytes - prefix.Length - suffix.Length) + suffix;
        }

        private static string CreateOpenApiDocument(string title, string schemaEntry)
        {
            return "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"" + title +
                   "\",\"version\":\"1\"},\"paths\":{},\"components\":{\"schemas\":{" +
                   schemaEntry + "}}}";
        }

        private SourceGeneratorGenerationProvider CreateProvider(
            RecordingImporter mirrorImporter,
            IAtomicFileWriter definitionFileWriter,
            List<string> definitionImports,
            Action requestCompilation,
            string definitionFailurePath = "",
            Action<string>? definitionImport = null)
        {
            var cacheService = new NormalizedSpecCacheService(
                projectRoot,
                new RawJsonNormalizer(),
                new RawYamlNormalizer(),
                new AtomicFileWriter(),
                mirrorImporter);
            IAtomicFileWriter writer = string.IsNullOrEmpty(definitionFailurePath)
                ? definitionFileWriter
                : new FailOnPathWriter(definitionFailurePath);
            var definitionWriter = new OpenApiClientDefinitionWriter(
                projectRoot,
                writer,
                definitionImport ?? definitionImports.Add);
            return new SourceGeneratorGenerationProvider(
                GenerationProviderAvailability.Available(),
                cacheService,
                definitionWriter,
                requestCompilation);
        }

        private static string ReadSpecId(string message)
        {
            const string prefix = "Spec ID: ";
            int start = message.IndexOf(prefix, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0));
            return message.Substring(start + prefix.Length).Trim();
        }

        private static void WriteAsmdef(string directory, string assemblyName, string reference)
        {
            Directory.CreateDirectory(directory);
            string json = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{{\n  \"name\": \"{0}\",\n  \"references\": [\"{1}\"]\n}}\n",
                assemblyName,
                reference);
            File.WriteAllText(
                Path.Combine(directory, assemblyName + ".asmdef"),
                json,
                new UTF8Encoding(false));
        }

        private static async Task<TException> CaptureExceptionAsync<TException>(Func<Task> action)
            where TException : Exception
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is TException)
            {
                return (TException)exception;
            }

            throw new AssertionException(
                "Expected an exception assignable to " + typeof(TException).FullName + ".");
        }

        [DllImport("libc", EntryPoint = "symlink", SetLastError = true)]
        private static extern int CreateUnixSymbolicLink(string target, string linkPath);

        private sealed class RecordingImporter : ICompilerMirrorImporter
        {
            internal List<string> ImportedAssetPaths { get; } = new List<string>();

            public void Import(string mirrorAssetPath)
            {
                ImportedAssetPaths.Add(mirrorAssetPath);
            }
        }

        private sealed class FailOnPathWriter : IAtomicFileWriter
        {
            private readonly string failurePath;
            private readonly AtomicFileWriter inner = new AtomicFileWriter();

            internal FailOnPathWriter(string failurePath)
            {
                this.failurePath = failurePath;
            }

            public void WriteAllBytesAtomically(string path, byte[] bytes)
            {
                if (string.Equals(path, failurePath, StringComparison.Ordinal))
                {
                    throw new IOException("Simulated mirror publication failure.");
                }

                inner.WriteAllBytesAtomically(path, bytes);
            }
        }

        private sealed class LoopbackHttpServer : IDisposable
        {
            private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            private readonly Dictionary<string, Response> responses = new Dictionary<string, Response>(StringComparer.Ordinal);
            private readonly List<string> requestTargets = new List<string>();
            private readonly CancellationTokenSource stop = new CancellationTokenSource();
            private Task? acceptTask;

            internal IReadOnlyList<string> RequestTargets
            {
                get
                {
                    lock (requestTargets)
                    {
                        return new List<string>(requestTargets);
                    }
                }
            }

            internal int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

            internal void Start()
            {
                listener.Start();
                acceptTask = Task.Run(AcceptLoopAsync);
            }

            internal string Url(string pathAndQuery)
            {
                return "http://127.0.0.1:" + Port + "/" + pathAndQuery;
            }

            internal void Add(string target, int statusCode, string contentType, string body)
            {
                responses[target] = new Response(statusCode, contentType, body, string.Empty, 0);
            }

            internal void AddWithHeader(
                string target,
                int statusCode,
                string contentType,
                string body,
                string headerName,
                string headerValue)
            {
                responses[target] = new Response(
                    statusCode,
                    contentType,
                    body,
                    string.Empty,
                    0,
                    headerName,
                    headerValue);
            }

            internal void AddDelayed(
                string target,
                int statusCode,
                string contentType,
                string body,
                int delayMilliseconds)
            {
                responses[target] = new Response(
                    statusCode,
                    contentType,
                    body,
                    string.Empty,
                    delayMilliseconds);
            }

            internal void AddRedirect(string target, string location)
            {
                responses[target] = new Response(302, string.Empty, string.Empty, location, 0);
            }

            public void Dispose()
            {
                stop.Cancel();
                listener.Stop();
                if (acceptTask != null)
                {
                    try
                    {
                        acceptTask.GetAwaiter().GetResult();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                    catch (SocketException)
                    {
                    }
                }

                stop.Dispose();
            }

            private async Task AcceptLoopAsync()
            {
                try
                {
                    while (!stop.IsCancellationRequested)
                    {
                        TcpClient client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                        await HandleClientAsync(client).ConfigureAwait(false);
                    }
                }
                catch (ObjectDisposedException)
                {
                }
                catch (SocketException)
                {
                }
                catch (IOException)
                {
                    // A cancelled client can close its socket while the delayed fixture is writing.
                }
            }

            private async Task HandleClientAsync(TcpClient client)
            {
                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    var requestBytes = new List<byte>();
                    var buffer = new byte[1024];
                    while (requestBytes.Count < 16384)
                    {
                        int read = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                        if (read == 0)
                        {
                            break;
                        }

                        requestBytes.AddRange(new ArraySegment<byte>(buffer, 0, read));
                        if (ContainsHeaderTerminator(requestBytes))
                        {
                            break;
                        }
                    }

                    string request = Encoding.ASCII.GetString(requestBytes.ToArray());
                    string[] lines = request.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    string[] requestLine = lines.Length == 0
                        ? Array.Empty<string>()
                        : lines[0].Split(' ');
                    string target = requestLine.Length > 1 ? requestLine[1] : string.Empty;
                    lock (requestTargets)
                    {
                        requestTargets.Add(target);
                    }

                    Response response;
                    if (!responses.TryGetValue(target, out response!))
                    {
                        response = new Response(404, "text/plain", "not found", string.Empty, 0);
                    }

                    if (response.DelayMilliseconds > 0)
                    {
                        await Task.Delay(response.DelayMilliseconds).ConfigureAwait(false);
                    }

                    byte[] body = Encoding.UTF8.GetBytes(response.Body);
                    var responseBuilder = new StringBuilder();
                    responseBuilder.Append("HTTP/1.1 ");
                    responseBuilder.Append(response.StatusCode);
                    responseBuilder.Append(response.StatusCode == 200 ? " OK\r\n" : " Found\r\n");
                    if (!string.IsNullOrEmpty(response.ContentType))
                    {
                        responseBuilder.Append("Content-Type: ");
                        responseBuilder.Append(response.ContentType);
                        responseBuilder.Append("\r\n");
                    }

                    if (!string.IsNullOrEmpty(response.Location))
                    {
                        responseBuilder.Append("Location: ");
                        responseBuilder.Append(response.Location);
                        responseBuilder.Append("\r\n");
                    }

                    if (!string.IsNullOrEmpty(response.HeaderName))
                    {
                        responseBuilder.Append(response.HeaderName);
                        responseBuilder.Append(": ");
                        responseBuilder.Append(response.HeaderValue);
                        responseBuilder.Append("\r\n");
                    }

                    responseBuilder.Append("Content-Length: ");
                    responseBuilder.Append(body.Length);
                    responseBuilder.Append("\r\nConnection: close\r\n\r\n");
                    byte[] headers = Encoding.ASCII.GetBytes(responseBuilder.ToString());
                    await stream.WriteAsync(headers, 0, headers.Length).ConfigureAwait(false);
                    if (body.Length > 0)
                    {
                        await stream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
                    }
                }
            }

            private static bool ContainsHeaderTerminator(List<byte> bytes)
            {
                int count = bytes.Count;
                return count >= 4 &&
                       bytes[count - 4] == (byte)'\r' &&
                       bytes[count - 3] == (byte)'\n' &&
                       bytes[count - 2] == (byte)'\r' &&
                       bytes[count - 1] == (byte)'\n';
            }

            private sealed class Response
            {
                internal Response(
                    int statusCode,
                    string contentType,
                    string body,
                    string location,
                    int delayMilliseconds,
                    string headerName = "",
                    string headerValue = "")
                {
                    StatusCode = statusCode;
                    ContentType = contentType;
                    Body = body;
                    Location = location;
                    DelayMilliseconds = delayMilliseconds;
                    HeaderName = headerName;
                    HeaderValue = headerValue;
                }

                internal int StatusCode { get; }
                internal string ContentType { get; }
                internal string Body { get; }
                internal string Location { get; }
                internal int DelayMilliseconds { get; }
                internal string HeaderName { get; }
                internal string HeaderValue { get; }
            }
        }
    }
}
