using System;
using System.IO;
using Rhycol.OpenApiCodeGen.SourceGenerator;
using UnityEditor;
using UnityEngine;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Editor
{
    /// <summary>
    /// Editor-side boundary used by the Source Generator provider to normalize and publish one local JSON or YAML spec.
    /// This service performs no Docker fallback.
    /// </summary>
    internal sealed class NormalizedSpecCacheService
    {
        private readonly string projectRoot;
        private readonly RawJsonNormalizer normalizer;
        private readonly RawYamlNormalizer yamlNormalizer;
        private readonly IAtomicFileWriter fileWriter;
        private readonly ICompilerMirrorImporter importer;

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
                "normalized-v1.json");
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
}
