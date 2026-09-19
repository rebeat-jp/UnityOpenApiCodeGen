using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Rhycol.OpenApiCodeGen.SourceGenerator;
using Rhycol.OpenApiCodeGen.Editor.Generation;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Editor
{
    /// <summary>
    /// Editor-side boundary used by the Source Generator provider to normalize and publish one complete
    /// JSON/YAML document graph. This service performs no Docker fallback.
    /// </summary>
    internal sealed class NormalizedSpecCacheService
    {
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly object GenerationLocksSync = new object();
        // Conservatively fold case so aliases on Windows and default macOS filesystems
        // cannot open a second descriptor and drop an existing Unix lock.
        private static readonly HashSet<string> GenerationLocks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private const string GenerationLockFailureMessage =
            "Another Unity process is already generating OpenAPI source. Wait for it to finish and retry Generate.";

        private const string PublicationMarkerVersion = "2";
        private const string PublicationMarkerPendingState = "publishing";
        private const string PublicationMarkerCompleteState = "complete";
        private const string PublicationMarkerAwaitingCompilationState = "published-awaiting-compilation";
        private const string PublicationMarkerTargetPrefix = "target=";
        private const string PublicationBackupFilePrefix = "artifact-";

        private readonly string projectRoot;
        private readonly RawJsonNormalizer normalizer;
        private readonly RawYamlNormalizer yamlNormalizer;
        private readonly IAtomicFileWriter fileWriter;
        private readonly ICompilerMirrorImporter importer;
        private readonly ExternalSpecGraphLoader graphLoader;

        internal Action CompilationRequester { get; set; } = CompilationPipeline.RequestScriptCompilation;

        internal NormalizedSpecCacheService(
            string projectRoot,
            RawJsonNormalizer normalizer,
            IAtomicFileWriter fileWriter,
            ICompilerMirrorImporter importer)
            : this(
                projectRoot,
                normalizer,
                new RawYamlNormalizer(),
                fileWriter,
                importer)
        {
        }

        internal NormalizedSpecCacheService(
            string projectRoot,
            RawJsonNormalizer normalizer,
            RawYamlNormalizer yamlNormalizer,
            IAtomicFileWriter fileWriter,
            ICompilerMirrorImporter importer)
        {
            if (string.IsNullOrEmpty(projectRoot))
            {
                throw new ArgumentException("A project root is required.", nameof(projectRoot));
            }

            this.projectRoot = Path.GetFullPath(projectRoot);
            this.normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
            this.yamlNormalizer = yamlNormalizer ?? throw new ArgumentNullException(nameof(yamlNormalizer));
            this.fileWriter = fileWriter ?? throw new ArgumentNullException(nameof(fileWriter));
            this.importer = importer ?? throw new ArgumentNullException(nameof(importer));
            graphLoader = new ExternalSpecGraphLoader(
                this.projectRoot,
                this.normalizer,
                this.yamlNormalizer);
        }

        internal static NormalizedSpecCacheService CreateForCurrentProject()
        {
            string currentProjectRoot = Directory.GetParent(Application.dataPath).FullName;
            return new NormalizedSpecCacheService(
                currentProjectRoot,
                new RawJsonNormalizer(),
                new RawYamlNormalizer(),
                new AtomicFileWriter(),
                new AssetDatabaseCompilerMirrorImporter());
        }

        internal NormalizedSpecCacheResult NormalizeAndCache(string rawSpecPath, string specId)
        {
            return NormalizeAndCache(rawSpecPath, specId, OpenApiDocumentFormat.Json);
        }

        internal NormalizedSpecCacheResult NormalizeAndCache(
            string rawSpecPath,
            string specId,
            OpenApiDocumentFormat format)
        {
            if (string.IsNullOrEmpty(rawSpecPath))
            {
                throw new ArgumentException("A raw spec path is required.", nameof(rawSpecPath));
            }

            if (format != OpenApiDocumentFormat.Json && format != OpenApiDocumentFormat.Yaml)
            {
                throw new ArgumentOutOfRangeException(nameof(format));
            }

            string fullRawSpecPath = GetFullPath(rawSpecPath);
            string sourceIdentity = GetSourceIdentity(projectRoot, fullRawSpecPath);
            byte[] rawBytes;
            try
            {
                rawBytes = File.ReadAllBytes(fullRawSpecPath);
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException)
            {
                throw new NormalizedSpecException(
                    "The raw document could not be read.",
                    sourceIdentity,
                    1,
                    1,
                    string.Empty,
                    exception);
            }

            // Normalization must finish before either last-known-good cache file is touched.
            NormalizedSpecBundle bundle = format == OpenApiDocumentFormat.Yaml
                ? yamlNormalizer.Normalize(rawBytes, specId, sourceIdentity)
                : normalizer.Normalize(rawBytes, specId, sourceIdentity);
            string authoritativePath = Path.Combine(
                projectRoot,
                NormalizedSpecBundleConstants.AuthoritativeCacheRelativePath,
                specId,
                NormalizedSpecBundleConstants.LegacyBundleFileName);
            string mirrorFileName = specId + NormalizedSpecBundleConstants.AdditionalFileSuffix;
            string mirrorRelativePath = CombineAssetPath(
                NormalizedSpecBundleConstants.CompilerMirrorRelativePath,
                mirrorFileName);
            string mirrorPath = Path.Combine(projectRoot, mirrorRelativePath.Replace('/', Path.DirectorySeparatorChar));
            string mirrorImportPendingPath = Path.Combine(
                Path.GetDirectoryName(authoritativePath),
                NormalizedSpecBundleConstants.MirrorImportPendingFileName);

            bool authoritativeChanged = !FileContentEquals(authoritativePath, bundle.Bytes);
            bool mirrorChanged = !FileContentEquals(mirrorPath, bundle.Bytes);
            bool mirrorImportPending = File.Exists(mirrorImportPendingPath);

            if (authoritativeChanged)
            {
                fileWriter.WriteAllBytesAtomically(authoritativePath, bundle.Bytes);
            }

            if (mirrorChanged)
            {
                // Persist publication intent before replacing the mirror. If AssetDatabase import
                // fails or the Editor exits, a new service instance still retries publication.
                fileWriter.WriteAllBytesAtomically(mirrorImportPendingPath, new byte[] { 1 });
                fileWriter.WriteAllBytesAtomically(mirrorPath, bundle.Bytes);
                mirrorImportPending = true;
            }

            if (mirrorImportPending)
            {
                importer.Import(mirrorRelativePath);
                File.Delete(mirrorImportPendingPath);
            }

            return new NormalizedSpecCacheResult(
                bundle.SpecId,
                bundle.RawSha256,
                bundle.SourcePath,
                authoritativePath,
                mirrorPath,
                mirrorRelativePath,
                format,
                authoritativeChanged,
                mirrorChanged);
        }

        internal async Task<NormalizedSpecCacheResult> NormalizeAndCacheAsync(
            string rawSpecPathOrUrl,
            string specId,
            CancellationToken cancellationToken)
        {
            return await NormalizeAndCacheAsync(
                rawSpecPathOrUrl,
                specId,
                cancellationToken,
                string.Empty,
                string.Empty,
                null);
        }

        internal async Task<NormalizedSpecCacheResult> NormalizeAndCacheAsync(
            string rawSpecPathOrUrl,
            string specId,
            CancellationToken cancellationToken,
            string definitionPath,
            string definitionAssetPath,
            Func<OpenApiDocumentFormat, Action> publishDefinition,
            IProgress<GenerationProgress>? progress = null)
        {
            RepairPendingPublicationForSpec(specId, definitionPath, definitionAssetPath);
            NormalizedSpecGraph graph = await LoadGraphAsync(
                rawSpecPathOrUrl,
                specId,
                cancellationToken,
                progress);
            return PublishGraph(
                graph,
                definitionPath,
                definitionAssetPath,
                publishDefinition);
        }

        internal Task<NormalizedSpecGraph> LoadGraphAsync(
            string rawSpecPathOrUrl,
            string specId,
            CancellationToken cancellationToken,
            IProgress<GenerationProgress>? progress = null)
        {
            return graphLoader.LoadAsync(rawSpecPathOrUrl, specId, cancellationToken, progress);
        }

        internal void RepairPendingPublicationForSpec(string specId)
        {
            RepairPendingPublicationForSpec(specId, string.Empty, string.Empty);
        }

        internal void RepairPendingPublicationForSpec(
            string specId,
            string definitionPath,
            string definitionAssetPath)
        {
            Guid parsedSpecId;
            if (specId == null ||
                !Guid.TryParseExact(specId, "N", out parsedSpecId) ||
                !string.Equals(parsedSpecId.ToString("N"), specId, StringComparison.Ordinal))
            {
                return;
            }

            string authoritativePath = Path.Combine(
                projectRoot,
                NormalizedSpecBundleConstants.AuthoritativeCacheRelativePath,
                specId,
                NormalizedSpecBundleConstants.BundleFileName);
            string mirrorRelativePath = CombineAssetPath(
                NormalizedSpecBundleConstants.CompilerMirrorRelativePath,
                specId + NormalizedSpecBundleConstants.AdditionalFileSuffix);
            string mirrorPath = Path.Combine(
                projectRoot,
                mirrorRelativePath.Replace('/', Path.DirectorySeparatorChar));
            string cacheDirectory = Path.GetDirectoryName(authoritativePath);
            string manifestPath = Path.Combine(
                cacheDirectory,
                NormalizedSpecBundleConstants.ManifestFileName);
            string mirrorImportPendingPath = Path.Combine(
                cacheDirectory,
                NormalizedSpecBundleConstants.MirrorImportPendingFileName);
            string publishPendingPath = Path.Combine(
                cacheDirectory,
                NormalizedSpecBundleConstants.PublishPendingFileName);
            string publishBackupDirectory = Path.Combine(
                cacheDirectory,
                NormalizedSpecBundleConstants.PublishBackupDirectoryName);
            if (!File.Exists(publishPendingPath))
            {
                if (!TryDeleteDirectory(publishBackupDirectory))
                {
                    throw new IOException("The stale Source Generator publication backup could not be cleared.");
                }

                return;
            }

            PublicationMarker marker = ReadPublicationMarker(
                publishPendingPath,
                authoritativePath,
                manifestPath,
                mirrorPath);
            if (marker.IsAwaitingCompilation)
            {
                CompilationRequester();
                AcknowledgeCompilationRequest(specId);
                return;
            }
            PublicationArtifact[] artifacts;
            if (marker.IsLegacy)
            {
                // Before the structured marker was introduced, backup names were derived
                // from the current target file names. Keep that format as a bounded fallback;
                // a legacy marker cannot safely recover a definition path that is no longer
                // supplied by the caller.
                ValidateDefinitionPath(definitionPath);
                artifacts = CreatePublicationArtifacts(
                    authoritativePath,
                    manifestPath,
                    mirrorPath,
                    publishBackupDirectory,
                    definitionPath,
                    false,
                    false);
            }
            else
            {
                artifacts = CreateRecoveryPublicationArtifacts(
                    marker.TargetPaths,
                    publishBackupDirectory);
            }

            RepairPendingPublication(
                artifacts,
                publishPendingPath,
                publishBackupDirectory,
                mirrorImportPendingPath,
                mirrorRelativePath,
                definitionPath,
                definitionAssetPath,
                marker.IsComplete,
                marker.IsLegacy,
                marker.CompilationRequired);
        }

        // The marker survives until the provider has requested compilation.  This closes the
        // crash window where AdditionalFiles were replaced but Unity was never told to compile.
        internal void MarkCompilationRequested(string specId)
        {
            // Kept as an internal compatibility shim. Publication itself now durably records
            // compilation intent before it returns to the provider.
        }

        internal void AcknowledgeCompilationRequest(string specId)
        {
            string directory = Path.Combine(projectRoot, NormalizedSpecBundleConstants.AuthoritativeCacheRelativePath, specId);
            // Remove the journal first after the request succeeds. A crash during backup
            // cleanup then leaves harmless orphan backups, not a broken rollback journal.
            DeleteFileOrThrow(Path.Combine(directory, NormalizedSpecBundleConstants.PublishPendingFileName));
            TryDeleteDirectory(Path.Combine(directory, NormalizedSpecBundleConstants.PublishBackupDirectoryName));
        }

        internal void RecoverPendingCompilations()
        {
            using (AcquireGenerationLock())
            {
            string root = Path.Combine(projectRoot, NormalizedSpecBundleConstants.AuthoritativeCacheRelativePath);
            if (!Directory.Exists(root)) return;
            EnsureNoSymbolicLink(projectRoot, root);
            foreach (string directory in Directory.GetDirectories(root))
            {
                string specId = Path.GetFileName(directory);
                Guid parsed;
                if (!Guid.TryParseExact(specId, "N", out parsed) || parsed.ToString("N") != specId)
                    continue;
                try
                {
                    EnsureNoSymbolicLink(projectRoot, directory);
                    RepairPendingPublicationForSpec(specId);
                }
                catch (Exception)
                {
                    Debug.LogError("Source Generator publication recovery marker is invalid and was retained.");
                }
            }
            }
        }

        internal IDisposable AcquireGenerationLock()
        {
            string directory = Path.Combine(projectRoot, "Library", "OpenApiCodeGen", "SourceGenerator");
            string lockPath = Path.GetFullPath(Path.Combine(directory, ".generation.lock"));
            // Reject a local owner before opening any descriptor. On Unix, even closing a
            // failed FileShare.None open can release this process's existing byte-range lock.
            lock (GenerationLocksSync)
            {
                if (!GenerationLocks.Add(lockPath))
                    throw new SafeGenerationException(GenerationLockFailureMessage);
            }
            FileStream generationLock = null;
            try
            {
                EnsureNoSymbolicLink(projectRoot, lockPath);
                Directory.CreateDirectory(directory);
                generationLock = new FileStream(lockPath, FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None);
                // Unity's Unix Mono does not enforce FileShare.None across processes.
                generationLock.Lock(0, 1);
                return new GenerationLock(lockPath, generationLock);
            }
            catch (IOException)
            {
                ReleaseGenerationLock(lockPath, generationLock);
                throw new SafeGenerationException(GenerationLockFailureMessage);
            }
            catch
            {
                ReleaseGenerationLock(lockPath, generationLock);
                throw;
            }
        }

        private static void ReleaseGenerationLock(string lockPath, FileStream stream)
        {
            lock (GenerationLocksSync)
            {
                try
                {
                    stream?.Dispose();
                }
                finally
                {
                    GenerationLocks.Remove(lockPath);
                }
            }
        }

        private sealed class GenerationLock : IDisposable
        {
            private readonly string lockPath;
            private readonly FileStream stream;
            private bool disposed;

            internal GenerationLock(string lockPath, FileStream stream)
            {
                this.lockPath = lockPath;
                this.stream = stream;
            }

            public void Dispose()
            {
                lock (GenerationLocksSync)
                {
                    if (disposed) return;
                    disposed = true;
                    ReleaseGenerationLock(lockPath, stream);
                }
            }
        }

        internal NormalizedSpecCacheResult PublishGraph(NormalizedSpecGraph graph)
        {
            return PublishGraph(graph, string.Empty, string.Empty, null);
        }

        internal NormalizedSpecCacheResult PublishGraph(
            NormalizedSpecGraph graph,
            string definitionPath,
            string definitionAssetPath,
            Func<OpenApiDocumentFormat, Action> publishDefinition,
            byte[] definitionContent = null)
        {
            RepairPendingPublicationForSpec(
                graph.SpecId,
                definitionPath,
                definitionAssetPath);
            byte[] bundleBytes = CanonicalSpecBundleWriter.Write(
                graph.SpecId,
                graph.RawSha256,
                graph.Documents,
                graph.Edges);
            if (bundleBytes.Length > NormalizedSpecBundleConstants.MaximumGraphBytes)
            {
                throw new SafeGenerationException(
                    "The normalized OpenAPI Bundle exceeds the maximum size of 32 MiB.");
            }

            string authoritativePath = Path.Combine(
                projectRoot,
                NormalizedSpecBundleConstants.AuthoritativeCacheRelativePath,
                graph.SpecId,
                NormalizedSpecBundleConstants.BundleFileName);
            string mirrorFileName = graph.SpecId + NormalizedSpecBundleConstants.AdditionalFileSuffix;
            string mirrorRelativePath = CombineAssetPath(
                NormalizedSpecBundleConstants.CompilerMirrorRelativePath,
                mirrorFileName);
            string mirrorPath = Path.Combine(
                projectRoot,
                mirrorRelativePath.Replace('/', Path.DirectorySeparatorChar));
            string cacheDirectory = Path.GetDirectoryName(authoritativePath);
            string manifestPath = Path.Combine(
                cacheDirectory,
                NormalizedSpecBundleConstants.ManifestFileName);
            string mirrorImportPendingPath = Path.Combine(
                cacheDirectory,
                NormalizedSpecBundleConstants.MirrorImportPendingFileName);
            byte[] manifestBytes = NormalizedSpecManifestWriter.Write(
                graph,
                Sha256Hex(bundleBytes),
                CombineAssetPath(
                    CombineAssetPath(
                        NormalizedSpecBundleConstants.AuthoritativeCacheRelativePath,
                        graph.SpecId),
                    NormalizedSpecBundleConstants.BundleFileName));

            string publishPendingPath = Path.Combine(
                cacheDirectory,
                NormalizedSpecBundleConstants.PublishPendingFileName);
            string publishBackupDirectory = Path.Combine(
                cacheDirectory,
                NormalizedSpecBundleConstants.PublishBackupDirectoryName);

            bool authoritativeChanged = !FileContentEquals(authoritativePath, bundleBytes);
            bool manifestChanged = !FileContentEquals(manifestPath, manifestBytes);
            bool mirrorChanged = !FileContentEquals(mirrorPath, bundleBytes);
            bool mirrorImportPending = File.Exists(mirrorImportPendingPath);
            bool mirrorPublicationRequired = mirrorChanged || mirrorImportPending;
            bool definitionPublicationRequired = publishDefinition != null &&
                (definitionContent == null || !FileContentEquals(definitionPath, definitionContent));
            bool compilationRequired = authoritativeChanged || mirrorPublicationRequired || definitionPublicationRequired;

            // Retrieval metadata is not a compiler input. Its single-file atomic update does
            // not need a multi-artifact journal and must not schedule compilation.
            if (!compilationRequired && manifestChanged)
            {
                fileWriter.WriteAllBytesAtomically(manifestPath, manifestBytes);
            }

            // All parsing, reference resolution, and canonicalization completed before this
            // publication phase. Snapshot and durable backups make a failed multi-file publish
            // recoverable without exposing a mixed Bundle/manifest/mirror generation.
            if (compilationRequired)
            {
                ValidateDefinitionPath(definitionPath);
                PublicationArtifact[] artifacts = CreatePublicationArtifacts(
                    authoritativePath,
                    manifestPath,
                    mirrorPath,
                    publishBackupDirectory,
                    definitionPath,
                    true,
                    true);
                PreparePublicationBackups(artifacts, publishBackupDirectory);
                if (definitionPublicationRequired && definitionContent != null)
                {
                    artifacts[3].SetTransactionOutput(
                        new FileSnapshot(true, definitionContent));
                }

                Action rollbackDefinition = null;
                try
                {
                    WritePublicationMarker(
                        publishPendingPath,
                        PublicationMarkerPendingState,
                        artifacts,
                        true);

                    if (authoritativeChanged)
                    {
                        artifacts[0].SetTransactionOutput(
                            new FileSnapshot(true, bundleBytes));
                        fileWriter.WriteAllBytesAtomically(authoritativePath, bundleBytes);
                    }

                    if (manifestChanged)
                    {
                        artifacts[1].SetTransactionOutput(
                            new FileSnapshot(true, manifestBytes));
                        fileWriter.WriteAllBytesAtomically(manifestPath, manifestBytes);
                    }

                    if (mirrorChanged)
                    {
                        fileWriter.WriteAllBytesAtomically(mirrorImportPendingPath, new byte[] { 1 });
                        artifacts[2].SetTransactionOutput(
                            new FileSnapshot(true, bundleBytes));
                        fileWriter.WriteAllBytesAtomically(mirrorPath, bundleBytes);
                        mirrorImportPending = true;
                    }

                    if (mirrorImportPending)
                    {
                        importer.Import(mirrorRelativePath);
                        File.Delete(mirrorImportPendingPath);
                    }

                    if (definitionPublicationRequired)
                    {
                        rollbackDefinition = publishDefinition(
                            string.Equals(graph.Format, "yaml", StringComparison.Ordinal)
                                ? OpenApiDocumentFormat.Yaml
                                : OpenApiDocumentFormat.Json);
                        if (definitionContent == null)
                        {
                            artifacts[3].SetTransactionOutput(
                                CaptureFileSnapshot(definitionPath));
                        }
                    }

                    bool definitionActuallyChanged = artifacts.Length > 3 &&
                        !SnapshotMatchesFile(artifacts[3]);
                    compilationRequired = authoritativeChanged || mirrorPublicationRequired || definitionActuallyChanged;
                    // There is no interval between completed publication and durable compile
                    // intent: a crash before this write rolls back, after it requests compile.
                    WritePublicationMarker(
                        publishPendingPath,
                        compilationRequired ? PublicationMarkerAwaitingCompilationState : PublicationMarkerCompleteState,
                        artifacts,
                        compilationRequired);
                }
                catch
                {
                    bool definitionWasExternallyChanged = RestorePublicationSnapshot(artifacts);
                    if (definitionWasExternallyChanged)
                    {
                        ExcludeDefinitionFromPendingPublication(
                            artifacts,
                            publishPendingPath);
                    }

                    if (rollbackDefinition != null && !definitionWasExternallyChanged)
                    {
                        try
                        {
                            rollbackDefinition();
                        }
                        catch (IOException)
                        {
                        }
                        catch (UnauthorizedAccessException)
                        {
                        }
                    }

                    if (mirrorPublicationRequired)
                    {
                        try
                        {
                            fileWriter.WriteAllBytesAtomically(mirrorImportPendingPath, new byte[] { 1 });
                        }
                        catch (IOException)
                        {
                        }
                        catch (UnauthorizedAccessException)
                        {
                        }
                    }

                    throw;
                }

                if (!compilationRequired)
                    AcknowledgeCompilationRequest(graph.SpecId);
            }

            return new NormalizedSpecCacheResult(
                graph.SpecId,
                graph.RawSha256,
                graph.SourcePath,
                authoritativePath,
                mirrorPath,
                mirrorRelativePath,
                string.Equals(graph.Format, "yaml", StringComparison.Ordinal)
                    ? OpenApiDocumentFormat.Yaml
                    : OpenApiDocumentFormat.Json,
                authoritativeChanged,
                mirrorChanged,
                manifestChanged,
                graph.QueryStripped
                    ? "The URL query was used only for this Generate operation and was not persisted. " +
                      "Re-enter the complete URL, including its query, for the next Generate."
                      : string.Empty,
                compilationRequired);
        }

        private static bool SnapshotMatchesFile(PublicationArtifact artifact)
        {
            return SnapshotMatchesFile(artifact.TargetPath, artifact.Snapshot);
        }

        private static bool SnapshotMatchesFile(string path, FileSnapshot snapshot)
        {
            return snapshot.Exists
                ? FileContentEquals(path, snapshot.Bytes)
                : !File.Exists(path);
        }

        private static PublicationArtifact[] CreatePublicationArtifacts(
            string authoritativePath,
            string manifestPath,
            string mirrorPath,
            string backupDirectory,
            string definitionPath,
            bool indexedBackupNames,
            bool captureSnapshots)
        {
            var artifacts = new List<PublicationArtifact>
            {
                CreatePublicationArtifact(
                    authoritativePath,
                    backupDirectory,
                    0,
                    indexedBackupNames,
                    captureSnapshots),
                CreatePublicationArtifact(
                    manifestPath,
                    backupDirectory,
                    1,
                    indexedBackupNames,
                    captureSnapshots),
                CreatePublicationArtifact(
                    mirrorPath,
                    backupDirectory,
                    2,
                    indexedBackupNames,
                    captureSnapshots),
            };
            if (!string.IsNullOrEmpty(definitionPath))
            {
                artifacts.Add(CreatePublicationArtifact(
                    definitionPath,
                    backupDirectory,
                    3,
                    indexedBackupNames,
                    captureSnapshots));
            }

            return artifacts.ToArray();
        }

        private static PublicationArtifact[] CreateRecoveryPublicationArtifacts(
            IReadOnlyList<string> targetPaths,
            string backupDirectory)
        {
            var artifacts = new PublicationArtifact[targetPaths.Count];
            for (int index = 0; index < targetPaths.Count; index++)
            {
                artifacts[index] = CreatePublicationArtifact(
                    targetPaths[index],
                    backupDirectory,
                    index,
                    true,
                    false);
            }

            return artifacts;
        }

        private static PublicationArtifact CreatePublicationArtifact(
            string path,
            string backupDirectory,
            int index,
            bool indexedBackupNames,
            bool captureSnapshot)
        {
            string fileName = indexedBackupNames
                ? PublicationBackupFilePrefix + index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)
                : Path.GetFileName(path);
            return new PublicationArtifact(
                path,
                Path.Combine(backupDirectory, fileName + ".bytes"),
                Path.Combine(backupDirectory, fileName + ".absent"),
                captureSnapshot
                    ? CaptureFileSnapshot(path)
                    : new FileSnapshot(false, new byte[0]));
        }

        private static FileSnapshot CaptureFileSnapshot(string path)
        {
            if (!File.Exists(path))
            {
                return new FileSnapshot(false, new byte[0]);
            }

            return new FileSnapshot(true, File.ReadAllBytes(path));
        }

        private void PreparePublicationBackups(
            IReadOnlyList<PublicationArtifact> artifacts,
            string backupDirectory)
        {
            if (!TryDeleteDirectory(backupDirectory))
            {
                throw new IOException("The previous Source Generator publication backup could not be cleared.");
            }

            Directory.CreateDirectory(backupDirectory);
            for (int index = 0; index < artifacts.Count; index++)
            {
                PublicationArtifact artifact = artifacts[index];
                if (artifact.Snapshot.Exists)
                {
                    fileWriter.WriteAllBytesAtomically(
                        artifact.BackupPath,
                        artifact.Snapshot.Bytes);
                }
                else
                {
                    fileWriter.WriteAllBytesAtomically(
                        artifact.AbsentPath,
                        new byte[] { 1 });
                }
            }
        }

        private void WritePublicationMarker(
            string path,
            string state,
            IReadOnlyList<PublicationArtifact> artifacts,
            bool compilationRequired = false)
        {
            if (!string.Equals(state, PublicationMarkerPendingState, StringComparison.Ordinal) &&
                !string.Equals(state, PublicationMarkerCompleteState, StringComparison.Ordinal) &&
                !string.Equals(state, PublicationMarkerAwaitingCompilationState, StringComparison.Ordinal))
            {
                throw new ArgumentException("An invalid publication marker state was requested.", nameof(state));
            }

            if (artifacts.Count != 3 && artifacts.Count != 4)
            {
                throw new IOException("The Source Generator publication artifact set is invalid.");
            }

            var builder = new StringBuilder();
            builder.Append("version=").Append(PublicationMarkerVersion).Append('\n');
            builder.Append("state=").Append(state).Append('\n');
            builder.Append("count=").Append(artifacts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.Append('\n');
            builder.Append("compilationRequired=").Append(compilationRequired ? "true" : "false").Append('\n');
            for (int index = 0; index < artifacts.Count; index++)
            {
                EnsureNoSymbolicLink(projectRoot, artifacts[index].TargetPath);
                string relativePath = GetProjectRelativePath(artifacts[index].TargetPath);
                builder.Append(PublicationMarkerTargetPrefix);
                builder.Append(Convert.ToBase64String(StrictUtf8.GetBytes(relativePath)));
                builder.Append('\n');
            }

            // The marker is itself an atomic publication. It is written before any artifact is
            // replaced and again after all imports succeed, so an interrupted transaction can
            // be recovered without guessing which output folder was used for the definition.
            fileWriter.WriteAllBytesAtomically(path, StrictUtf8.GetBytes(builder.ToString()));
        }

        private PublicationMarker ReadPublicationMarker(
            string path,
            string authoritativePath,
            string manifestPath,
            string mirrorPath)
        {
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException)
            {
                throw new IOException("The Source Generator publication marker could not be read.", exception);
            }

            string marker;
            try
            {
                marker = StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException exception)
            {
                throw new IOException("The Source Generator publication marker is not strict UTF-8.", exception);
            }

            // These exact legacy forms are retained for projects interrupted by the pre-v2
            // marker implementation. Anything else must use the structured format below.
            if (string.Equals(marker, "pending\n", StringComparison.Ordinal) ||
                string.Equals(marker, "pending", StringComparison.Ordinal) ||
                string.Equals(marker, "publishing\n", StringComparison.Ordinal))
            {
                return PublicationMarker.LegacyPending();
            }

            if (string.Equals(marker, "complete\n", StringComparison.Ordinal) ||
                string.Equals(marker, "complete", StringComparison.Ordinal))
            {
                return PublicationMarker.LegacyComplete();
            }

            if (marker.Length == 0 || marker.IndexOf('\r') >= 0 || !marker.EndsWith("\n", StringComparison.Ordinal))
            {
                throw new IOException("The Source Generator publication marker is malformed.");
            }

            string[] lines = marker.Split(new[] { '\n' }, StringSplitOptions.None);
            if (lines.Length < 4 || lines[lines.Length - 1].Length != 0 ||
                (!string.Equals(lines[0], "version=1", StringComparison.Ordinal) &&
                 !string.Equals(lines[0], "version=" + PublicationMarkerVersion, StringComparison.Ordinal)))
            {
                throw new IOException("The Source Generator publication marker is malformed.");
            }

            string state = ReadMarkerField(lines[1], "state=");
            bool isComplete;
            if (string.Equals(state, PublicationMarkerPendingState, StringComparison.Ordinal) ||
                (lines[0] == "version=1" && state == "pending"))
            {
                isComplete = false;
            }
            else if (string.Equals(state, PublicationMarkerCompleteState, StringComparison.Ordinal))
            {
                isComplete = true;
            }
            else if (string.Equals(state, PublicationMarkerAwaitingCompilationState, StringComparison.Ordinal))
            {
                isComplete = true;
            }
            else
            {
                throw new IOException("The Source Generator publication marker has an invalid state.");
            }

            bool hasCompilationField = lines.Length > 3 && lines[3].StartsWith("compilationRequired=", StringComparison.Ordinal);
            bool compilationRequired = !isComplete;
            if (hasCompilationField)
            {
                string flag = ReadMarkerField(lines[3], "compilationRequired=");
                if (flag != "true" && flag != "false")
                    throw new IOException("The Source Generator publication marker has an invalid compilation flag.");
                compilationRequired = flag == "true";
            }
            bool isAwaiting = state == PublicationMarkerAwaitingCompilationState;
            if (isAwaiting && hasCompilationField && !compilationRequired)
                throw new IOException("The Source Generator publication marker has contradictory compilation state.");
            compilationRequired |= isAwaiting;
            int targetOffset = hasCompilationField ? 4 : 3;
            string countValue = ReadMarkerField(lines[2], "count=");
            int count;
            if (!int.TryParse(
                    countValue,
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out count) ||
                (count != 3 && count != 4) ||
                lines.Length != count + targetOffset + 1)
            {
                throw new IOException("The Source Generator publication marker has an invalid artifact count.");
            }

            var targetPaths = new List<string>(count);
            for (int index = 0; index < count; index++)
            {
                string encodedPath = ReadMarkerField(
                    lines[index + targetOffset],
                    PublicationMarkerTargetPrefix,
                    true);
                if (encodedPath.Length == 0)
                {
                    throw new IOException("The Source Generator publication marker contains an empty target path.");
                }

                byte[] relativePathBytes;
                try
                {
                    relativePathBytes = Convert.FromBase64String(encodedPath);
                }
                catch (FormatException exception)
                {
                    throw new IOException("The Source Generator publication marker contains an invalid target path.", exception);
                }

                if (!string.Equals(
                        Convert.ToBase64String(relativePathBytes),
                        encodedPath,
                        StringComparison.Ordinal))
                {
                    throw new IOException("The Source Generator publication marker contains a non-canonical target path.");
                }

                string relativePath;
                try
                {
                    relativePath = StrictUtf8.GetString(relativePathBytes);
                }
                catch (DecoderFallbackException exception)
                {
                    throw new IOException("The Source Generator publication marker contains a non-UTF-8 target path.", exception);
                }

                string targetPath = ResolveMarkerTargetPath(relativePath);
                for (int previous = 0; previous < targetPaths.Count; previous++)
                {
                    if (PathsEqual(targetPaths[previous], targetPath))
                    {
                        throw new IOException("The Source Generator publication marker contains duplicate target paths.");
                    }
                }

                targetPaths.Add(targetPath);
            }

            ValidateStructuredMarkerTargets(
                targetPaths,
                authoritativePath,
                manifestPath,
                mirrorPath);
            return new PublicationMarker(isComplete, targetPaths, isAwaiting, compilationRequired);
        }

        private static string ReadMarkerField(string line, string prefix, bool allowEquals = false)
        {
            if (line == null || !line.StartsWith(prefix, StringComparison.Ordinal) ||
                (!allowEquals && line.IndexOf('=', prefix.Length) >= 0))
            {
                throw new IOException("The Source Generator publication marker contains an unknown or duplicate field.");
            }

            return line.Substring(prefix.Length);
        }

        private string ResolveMarkerTargetPath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath) ||
                relativePath.IndexOf('\\') >= 0 ||
                relativePath[0] == '/' ||
                Path.IsPathRooted(relativePath))
            {
                throw new IOException("The Source Generator publication marker contains an unsafe target path.");
            }

            string[] segments = relativePath.Split('/');
            for (int index = 0; index < segments.Length; index++)
            {
                if (segments[index].Length == 0 ||
                    string.Equals(segments[index], ".", StringComparison.Ordinal) ||
                    string.Equals(segments[index], "..", StringComparison.Ordinal))
                {
                    throw new IOException("The Source Generator publication marker contains an unsafe target path.");
                }
            }

            string targetPath = Path.GetFullPath(
                Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsSameOrDescendant(projectRoot, targetPath) ||
                !string.Equals(GetProjectRelativePath(targetPath), relativePath, StringComparison.Ordinal))
            {
                throw new IOException("The Source Generator publication marker points outside the project.");
            }

            EnsureNoSymbolicLink(projectRoot, targetPath);

            return targetPath;
        }

        private void ValidateStructuredMarkerTargets(
            IReadOnlyList<string> targetPaths,
            string authoritativePath,
            string manifestPath,
            string mirrorPath)
        {
            if (targetPaths.Count != 3 && targetPaths.Count != 4 ||
                !PathsEqual(targetPaths[0], authoritativePath) ||
                !PathsEqual(targetPaths[1], manifestPath) ||
                !PathsEqual(targetPaths[2], mirrorPath))
            {
                throw new IOException("The Source Generator publication marker has an invalid artifact target set.");
            }

            if (targetPaths.Count == 4)
            {
                string assetsRoot = Path.Combine(projectRoot, "Assets");
                if (!IsSameOrDescendant(assetsRoot, targetPaths[3]))
                {
                    throw new IOException("The Source Generator publication marker points a definition outside Assets.");
                }
            }
        }

        private void ValidateDefinitionPath(string definitionPath)
        {
            if (string.IsNullOrEmpty(definitionPath))
            {
                return;
            }

            string fullPath = Path.GetFullPath(definitionPath);
            string assetsRoot = Path.Combine(projectRoot, "Assets");
            if (!IsSameOrDescendant(assetsRoot, fullPath))
            {
                throw new IOException("The Source Generator definition path must resolve under the project Assets folder.");
            }
        }

        private string GetProjectRelativePath(string path)
        {
            string fullPath = Path.GetFullPath(path);
            if (!IsSameOrDescendant(projectRoot, fullPath))
            {
                throw new IOException("The Source Generator publication target must remain inside the project.");
            }

            string root = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return fullPath.Substring(root.Length + 1).Replace('\\', '/');
        }

        private string GetAssetPath(string path)
        {
            string relativePath = GetProjectRelativePath(path);
            if (!relativePath.StartsWith("Assets/", StringComparison.Ordinal) &&
                !string.Equals(relativePath, "Assets", StringComparison.Ordinal))
            {
                throw new IOException("The Source Generator definition target is not a Unity asset path.");
            }

            return relativePath;
        }

        private static bool PathsEqual(string first, string second)
        {
            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return string.Equals(
                Path.GetFullPath(first),
                Path.GetFullPath(second),
                comparison);
        }

        private static bool IsSameOrDescendant(string parentPath, string candidatePath)
        {
            string parent = Path.GetFullPath(parentPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string candidate = Path.GetFullPath(candidatePath);
            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return string.Equals(parent, candidate, comparison) ||
                candidate.StartsWith(parent + Path.DirectorySeparatorChar, comparison);
        }

        private static void EnsureNoSymbolicLink(string rootPath, string candidatePath)
        {
            string current = Path.GetFullPath(candidatePath);
            while (IsSameOrDescendant(rootPath, current))
            {
                if ((File.Exists(current) || Directory.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException("The Source Generator publication marker traverses a symbolic link.");
                }

                if (PathsEqual(rootPath, current))
                {
                    break;
                }

                DirectoryInfo parent = Directory.GetParent(current);
                if (parent == null)
                {
                    break;
                }

                current = parent.FullName;
            }
        }

        private void RepairPendingPublication(
            IReadOnlyList<PublicationArtifact> artifacts,
            string publishPendingPath,
            string backupDirectory,
            string mirrorImportPendingPath,
            string mirrorRelativePath,
            string definitionPath,
            string definitionAssetPath,
            bool markerComplete,
            bool markerIsLegacy,
            bool compilationRequired)
        {
            if (!File.Exists(publishPendingPath))
            {
                if (!TryDeleteDirectory(backupDirectory))
                {
                    throw new IOException("The stale Source Generator publication backup could not be cleared.");
                }

                return;
            }

            if (markerComplete)
            {
                // The new generation is authoritative; only cleanup was interrupted.
                DeleteFileOrThrow(publishPendingPath);
                if (!TryDeleteDirectory(backupDirectory))
                {
                    throw new IOException("The completed Source Generator publication backup could not be cleared.");
                }

                return;
            }

            if (!markerIsLegacy)
            {
                ValidateRecoveryBackups(artifacts, backupDirectory);
            }

            bool definitionWasPresent = artifacts.Count > 3 &&
                File.Exists(artifacts[3].BackupPath);
            RestorePublicationBackups(artifacts);
            if (compilationRequired || File.Exists(mirrorImportPendingPath))
            {
                importer.Import(mirrorRelativePath);
                DeleteFileOrThrow(mirrorImportPendingPath);
            }

            string restoredDefinitionPath = artifacts.Count > 3
                ? artifacts[3].TargetPath
                : definitionPath;
            string restoredDefinitionAssetPath = artifacts.Count > 3 && !markerIsLegacy
                ? GetAssetPath(restoredDefinitionPath)
                : definitionAssetPath;
            bool shouldImportRestoredDefinition = !string.IsNullOrEmpty(restoredDefinitionAssetPath) &&
                (!markerIsLegacy || (definitionWasPresent && File.Exists(restoredDefinitionPath)));
            if (shouldImportRestoredDefinition)
            {
                importer.Import(restoredDefinitionAssetPath);
            }

            if (compilationRequired)
            {
                // Persist restored compiler inputs as the new good state before asking Unity.
                // If the request fails or the Editor exits, startup safely requests it again.
                WritePublicationMarker(publishPendingPath, PublicationMarkerAwaitingCompilationState, artifacts, true);
                CompilationRequester();
            }
            DeleteFileOrThrow(publishPendingPath);
            if (!TryDeleteDirectory(backupDirectory))
            {
                throw new IOException("The repaired Source Generator publication backup could not be cleared.");
            }

        }

        private static void ValidateRecoveryBackups(
            IReadOnlyList<PublicationArtifact> artifacts,
            string backupDirectory)
        {
            if (!Directory.Exists(backupDirectory))
            {
                throw new IOException("The Source Generator publication backup is missing.");
            }

            string[] files = Directory.GetFiles(backupDirectory);
            var expected = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < artifacts.Count; index++)
            {
                PublicationArtifact artifact = artifacts[index];
                bool backupExists = File.Exists(artifact.BackupPath);
                bool absentExists = File.Exists(artifact.AbsentPath);
                if (backupExists == absentExists)
                {
                    throw new IOException("The Source Generator publication backup is incomplete or ambiguous.");
                }

                expected.Add(artifact.BackupPath);
                expected.Add(artifact.AbsentPath);
            }

            for (int index = 0; index < files.Length; index++)
            {
                if (!expected.Contains(files[index]))
                {
                    throw new IOException("The Source Generator publication backup contains an unexpected file.");
                }
            }
        }

        private void RestorePublicationBackups(IReadOnlyList<PublicationArtifact> artifacts)
        {
            for (int index = 0; index < artifacts.Count; index++)
            {
                PublicationArtifact artifact = artifacts[index];
                if (File.Exists(artifact.BackupPath))
                {
                    fileWriter.WriteAllBytesAtomically(
                        artifact.TargetPath,
                        File.ReadAllBytes(artifact.BackupPath));
                }
                else if (File.Exists(artifact.AbsentPath))
                {
                    DeleteFileOrThrow(artifact.TargetPath);
                }
                else
                {
                    throw new IOException(
                        "The Source Generator publication backup is incomplete: " +
                        artifact.TargetPath);
                }
            }
        }

        private bool RestorePublicationSnapshot(IReadOnlyList<PublicationArtifact> artifacts)
        {
            // The durable backup remains available when an in-process restore itself cannot
            // complete (for example, a simulated writer failure). Best effort here preserves the
            // common case without masking the original publication exception.
            bool definitionWasExternallyChanged = false;
            for (int index = 0; index < artifacts.Count; index++)
            {
                PublicationArtifact artifact = artifacts[index];
                if (!artifact.HasTransactionOutput)
                {
                    if (!SnapshotMatchesFile(artifact))
                    {
                        PreserveCurrentStateAsPublicationBackup(artifact);
                        if (index == 3)
                        {
                            definitionWasExternallyChanged = true;
                        }
                    }

                    continue;
                }

                if (!SnapshotMatchesFile(artifact.TargetPath, artifact.TransactionOutput))
                {
                    if (!SnapshotMatchesFile(artifact))
                    {
                        PreserveCurrentStateAsPublicationBackup(artifact);
                        if (index == 3)
                        {
                            definitionWasExternallyChanged = true;
                        }
                    }

                    continue;
                }

                try
                {
                    if (artifact.Snapshot.Exists)
                    {
                        fileWriter.WriteAllBytesAtomically(
                            artifact.TargetPath,
                            artifact.Snapshot.Bytes);
                    }
                    else if (File.Exists(artifact.TargetPath))
                    {
                        File.Delete(artifact.TargetPath);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            return definitionWasExternallyChanged;
        }

        private void ExcludeDefinitionFromPendingPublication(
            IReadOnlyList<PublicationArtifact> artifacts,
            string publishPendingPath)
        {
            if (artifacts.Count != 4)
            {
                return;
            }

            var cacheArtifacts = new[]
            {
                artifacts[0],
                artifacts[1],
                artifacts[2],
            };
            PublicationArtifact definitionArtifact = artifacts[3];
            try
            {
                // Persist the reduced ownership set before removing the obsolete definition
                // backup. A crash between these operations fails closed on an extra backup file.
                WritePublicationMarker(
                    publishPendingPath,
                    PublicationMarkerPendingState,
                    cacheArtifacts,
                    true);
                DeleteFileOrThrow(definitionArtifact.BackupPath);
                DeleteFileOrThrow(definitionArtifact.AbsentPath);
            }
            catch (IOException)
            {
                // If the marker cannot be rewritten, make its old four-artifact backup
                // incomplete so recovery refuses to overwrite the externally owned definition.
                TryDeleteFile(definitionArtifact.BackupPath);
                TryDeleteFile(definitionArtifact.AbsentPath);
            }
            catch (UnauthorizedAccessException)
            {
                TryDeleteFile(definitionArtifact.BackupPath);
                TryDeleteFile(definitionArtifact.AbsentPath);
            }
        }

        private void PreserveCurrentStateAsPublicationBackup(PublicationArtifact artifact)
        {
            FileSnapshot current = CaptureFileSnapshot(artifact.TargetPath);
            try
            {
                if (current.Exists)
                {
                    fileWriter.WriteAllBytesAtomically(artifact.BackupPath, current.Bytes);
                    DeleteFileOrThrow(artifact.AbsentPath);
                }
                else
                {
                    fileWriter.WriteAllBytesAtomically(
                        artifact.AbsentPath,
                        new byte[] { 1 });
                    DeleteFileOrThrow(artifact.BackupPath);
                }
            }
            catch (IOException)
            {
                // An incomplete backup is safer than retaining stale bytes that a later repair
                // could use to overwrite an external edit.
                TryDeleteFile(artifact.BackupPath);
                TryDeleteFile(artifact.AbsentPath);
            }
            catch (UnauthorizedAccessException)
            {
                TryDeleteFile(artifact.BackupPath);
                TryDeleteFile(artifact.AbsentPath);
            }
        }

        private static void DeleteFileOrThrow(string path)
        {
            if (!File.Exists(path))
            {
                return;
            }

            File.Delete(path);
            if (File.Exists(path))
            {
                throw new IOException("The Source Generator publication file could not be removed: " + path);
            }
        }

        private static bool TryDeleteFile(string path)
        {
            try
            {
                DeleteFileOrThrow(path);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static bool TryDeleteDirectory(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    return true;
                }

                Directory.Delete(path, true);
                return !Directory.Exists(path);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private string GetFullPath(string path)
        {
            return Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(projectRoot, path));
        }

        private static string GetSourceIdentity(string projectRoot, string sourcePath)
        {
            string normalizedRoot = Path.GetFullPath(projectRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedSource = Path.GetFullPath(sourcePath);
            string rootPrefix = normalizedRoot + Path.DirectorySeparatorChar;
            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (normalizedSource.StartsWith(rootPrefix, comparison))
            {
                return normalizedSource.Substring(rootPrefix.Length).Replace('\\', '/');
            }

            return normalizedSource.Replace('\\', '/');
        }

        private static bool FileContentEquals(string path, byte[] expected)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return false;
                }

                var info = new FileInfo(path);
                if (info.Length != expected.Length)
                {
                    return false;
                }

                using (FileStream stream = File.OpenRead(path))
                {
                    var buffer = new byte[8192];
                    int expectedOffset = 0;
                    while (expectedOffset < expected.Length)
                    {
                        int requested = Math.Min(buffer.Length, expected.Length - expectedOffset);
                        int read = stream.Read(buffer, 0, requested);
                        if (read == 0)
                        {
                            return false;
                        }

                        for (int index = 0; index < read; index++)
                        {
                            if (buffer[index] != expected[expectedOffset + index])
                            {
                                return false;
                            }
                        }

                        expectedOffset += read;
                    }

                    return stream.ReadByte() == -1;
                }
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static string CombineAssetPath(string directory, string fileName)
        {
            return directory.TrimEnd('/') + "/" + fileName;
        }

        private static string Sha256Hex(byte[] bytes)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(bytes);
                var builder = new StringBuilder(hash.Length * 2);
                for (int index = 0; index < hash.Length; index++)
                {
                    builder.Append(hash[index].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }

        private sealed class PublicationMarker
        {
            internal PublicationMarker(bool isComplete, IReadOnlyList<string> targetPaths,
                bool isAwaitingCompilation = false, bool compilationRequired = false)
            {
                IsComplete = isComplete;
                IsLegacy = false;
                TargetPaths = targetPaths;
                IsAwaitingCompilation = isAwaitingCompilation;
                CompilationRequired = compilationRequired;
            }

            private PublicationMarker(bool isComplete)
            {
                IsComplete = isComplete;
                IsLegacy = true;
                TargetPaths = Array.Empty<string>();
            }

            internal bool IsComplete { get; }
            internal bool IsAwaitingCompilation { get; }
            internal bool CompilationRequired { get; }
            internal bool IsLegacy { get; }
            internal IReadOnlyList<string> TargetPaths { get; }

            internal static PublicationMarker LegacyPending()
            {
                return new PublicationMarker(false);
            }

            internal static PublicationMarker LegacyComplete()
            {
                return new PublicationMarker(true);
            }
        }

        private sealed class PublicationArtifact
        {
            internal PublicationArtifact(
                string targetPath,
                string backupPath,
                string absentPath,
                FileSnapshot snapshot)
            {
                TargetPath = targetPath;
                BackupPath = backupPath;
                AbsentPath = absentPath;
                Snapshot = snapshot;
                TransactionOutput = new FileSnapshot(false, new byte[0]);
            }

            internal string TargetPath { get; }
            internal string BackupPath { get; }
            internal string AbsentPath { get; }
            internal FileSnapshot Snapshot { get; }

            internal bool HasTransactionOutput { get; private set; }

            internal FileSnapshot TransactionOutput { get; private set; }

            internal void SetTransactionOutput(FileSnapshot transactionOutput)
            {
                TransactionOutput = transactionOutput;
                HasTransactionOutput = true;
            }
        }

        private sealed class FileSnapshot
        {
            internal FileSnapshot(bool exists, byte[] bytes)
            {
                Exists = exists;
                Bytes = bytes;
            }

            internal bool Exists { get; }
            internal byte[] Bytes { get; }
        }
    }

    internal sealed class NormalizedSpecCacheResult
    {
        internal NormalizedSpecCacheResult(
            string specId,
            string rawSha256,
            string sourcePath,
            string authoritativePath,
            string mirrorPath,
            string mirrorAssetPath,
            OpenApiDocumentFormat format,
            bool authoritativeChanged,
            bool mirrorChanged)
            : this(
                specId,
                rawSha256,
                sourcePath,
                authoritativePath,
                mirrorPath,
                mirrorAssetPath,
                format,
                authoritativeChanged,
                mirrorChanged,
                false,
                string.Empty)
        {
        }

        internal NormalizedSpecCacheResult(
            string specId,
            string rawSha256,
            string sourcePath,
            string authoritativePath,
            string mirrorPath,
            string mirrorAssetPath,
            OpenApiDocumentFormat format,
            bool authoritativeChanged,
            bool mirrorChanged,
            bool manifestChanged,
            string warningMessage,
            bool compilationRequired = false)
        {
            SpecId = specId;
            RawSha256 = rawSha256;
            SourcePath = sourcePath;
            AuthoritativePath = authoritativePath;
            MirrorPath = mirrorPath;
            MirrorAssetPath = mirrorAssetPath;
            Format = format;
            AuthoritativeChanged = authoritativeChanged;
            MirrorChanged = mirrorChanged;
            ManifestChanged = manifestChanged;
            WarningMessage = warningMessage ?? string.Empty;
            CompilationRequired = compilationRequired;
        }

        internal string SpecId { get; }

        internal string RawSha256 { get; }

        internal string SourcePath { get; }

        internal string AuthoritativePath { get; }

        internal string MirrorPath { get; }

        internal string MirrorAssetPath { get; }

        internal OpenApiDocumentFormat Format { get; }

        internal bool AuthoritativeChanged { get; }

        internal bool MirrorChanged { get; }

        internal bool ManifestChanged { get; }

        internal bool CompilationRequired { get; }

        internal string WarningMessage { get; }
    }

    internal interface IAtomicFileWriter
    {
        void WriteAllBytesAtomically(string path, byte[] bytes);
    }

    internal sealed class AtomicFileWriter : IAtomicFileWriter
    {
        public void WriteAllBytesAtomically(string path, byte[] bytes)
        {
            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                using (var stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(path))
                {
                    File.Replace(temporaryPath, path, null);
                }
                else
                {
                    File.Move(temporaryPath, path);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }

    internal interface ICompilerMirrorImporter
    {
        void Import(string mirrorAssetPath);
    }

    internal sealed class AssetDatabaseCompilerMirrorImporter : ICompilerMirrorImporter
    {
        public void Import(string mirrorAssetPath)
        {
            AssetDatabase.ImportAsset(
                mirrorAssetPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        }
    }

    internal static class NormalizedSpecManifestWriter
    {
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal static byte[] Write(
            NormalizedSpecGraph graph,
            string bundleSha256,
            string bundlePath)
        {
            var builder = new StringBuilder();
            builder.Append("{\n");
            builder.Append("  \"manifestVersion\": 1,\n");
            builder.Append("  \"bundleFormatVersion\": 2,\n");
            builder.Append("  \"specId\": ");
            AppendString(builder, graph.SpecId);
            builder.Append(",\n  \"rootDocumentId\": \"root\",");
            builder.Append("\n  \"bundlePath\": ");
            AppendString(builder, bundlePath);
            builder.Append(",\n  \"bundleSha256\": ");
            AppendString(builder, bundleSha256);
            builder.Append(",\n  \"documents\": [\n");
            for (int index = 0; index < graph.Documents.Count; index++)
            {
                AppendDocument(builder, graph.Documents[index], 2);
                builder.Append(index + 1 == graph.Documents.Count ? "\n" : ",\n");
            }

            builder.Append("  ]\n}\n");
            return StrictUtf8.GetBytes(builder.ToString());
        }

        private static void AppendDocument(StringBuilder builder, GraphDocument document, int indent)
        {
            AppendIndent(builder, indent);
            builder.Append("{\n");
            AppendIndent(builder, indent + 1);
            builder.Append("\"documentId\": ");
            AppendString(builder, document.DocumentId);
            builder.Append(",\n");
            if (document.IsRemote)
            {
                AppendIndent(builder, indent + 1);
                builder.Append("\"requestedSource\": ");
                AppendString(builder, document.RequestedSource);
                builder.Append(",\n");
                AppendIndent(builder, indent + 1);
                builder.Append("\"effectiveSource\": ");
                AppendString(builder, document.EffectiveSource);
                builder.Append(",\n");
            }
            else
            {
                AppendIndent(builder, indent + 1);
                builder.Append("\"sourcePath\": ");
                AppendString(builder, document.SourcePath);
                builder.Append(",\n");
            }

            AppendIndent(builder, indent + 1);
            builder.Append("\"sourceKeySha256\": ");
            AppendString(builder, document.SourceKeySha256);
            builder.Append(",\n");
            AppendIndent(builder, indent + 1);
            builder.Append("\"format\": ");
            AppendString(builder, document.Format);
            builder.Append(",\n");
            AppendIndent(builder, indent + 1);
            builder.Append("\"rawSha256\": ");
            AppendString(builder, document.RawSha256);
            builder.Append(",\n");
            AppendIndent(builder, indent + 1);
            builder.Append("\"retrievalKind\": ");
            AppendString(builder, document.RetrievalKind);
            builder.Append(",\n");
            AppendIndent(builder, indent + 1);
            builder.Append("\"httpStatus\": ");
            builder.Append(document.HttpStatus.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.Append(",\n");
            AppendIndent(builder, indent + 1);
            builder.Append("\"redirectCount\": ");
            builder.Append(document.RedirectCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.Append('\n');
            AppendIndent(builder, indent);
            builder.Append('}');
        }

        private static void AppendIndent(StringBuilder builder, int indent)
        {
            for (int index = 0; index < indent; index++)
            {
                builder.Append("  ");
            }
        }

        private static void AppendString(StringBuilder builder, string value)
        {
            builder.Append('"');
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                switch (character)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (character < 0x20 ||
                            (char.IsSurrogate(character) && !IsValidSurrogatePair(value, index)))
                        {
                            AppendUnicodeEscape(builder, character);
                        }
                        else
                        {
                            builder.Append(character);
                            if (char.IsHighSurrogate(character))
                            {
                                builder.Append(value[++index]);
                            }
                        }

                        break;
                }
            }

            builder.Append('"');
        }

        private static bool IsValidSurrogatePair(string value, int index)
        {
            return char.IsHighSurrogate(value[index]) &&
                   index + 1 < value.Length &&
                   char.IsLowSurrogate(value[index + 1]);
        }

        private static void AppendUnicodeEscape(StringBuilder builder, char character)
        {
            const string Hex = "0123456789abcdef";
            builder.Append("\\u");
            builder.Append(Hex[(character >> 12) & 0xF]);
            builder.Append(Hex[(character >> 8) & 0xF]);
            builder.Append(Hex[(character >> 4) & 0xF]);
            builder.Append(Hex[character & 0xF]);
        }

        private static string Sha256Hex(byte[] bytes)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(bytes);
                var builder = new StringBuilder(hash.Length * 2);
                for (int index = 0; index < hash.Length; index++)
                {
                    builder.Append(hash[index].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }
    }
}
