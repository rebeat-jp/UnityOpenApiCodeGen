namespace Rhycol.OpenApiCodeGen.SourceGenerator.Editor
{
    internal static class NormalizedSpecBundleConstants
    {
        internal const int FormatVersion = 2;
        internal const int LegacyFormatVersion = 1;
        internal const int MaximumDepth = 256;
        internal const int MaximumDocumentCount = 64;
        internal const int MaximumDocumentBytes = 4 * 1024 * 1024;
        internal const int MaximumGraphBytes = 32 * 1024 * 1024;
        internal const int MaximumRedirects = 5;
        internal const int RequestTimeoutSeconds = 30;
        internal const int GraphTimeoutSeconds = 120;
        internal const string RootDocumentId = "root";
        internal const string AnalyzerAssemblyName = "Rhycol.OpenApiCodeGen.SourceGenerator";
        internal const string AdditionalFileSuffix = ".Rhycol.OpenApiCodeGen.SourceGenerator.additionalfile";
        internal const string AuthoritativeCacheRelativePath = "Library/OpenApiCodeGen/SourceGenerator/SpecCache";
        internal const string CompilerMirrorRelativePath = "Assets/OpenApiCodeGen/Generated/SpecCache";
        internal const string MirrorImportPendingFileName = ".mirror-import-pending";
        internal const string PublishPendingFileName = ".publish-pending";
        internal const string PublishBackupDirectoryName = ".publish-backup";
        internal const string BundleFileName = "normalized-v2.json";
        internal const string LegacyBundleFileName = "normalized-v1.json";
        internal const string ManifestFileName = "manifest-v1.json";
    }
}
