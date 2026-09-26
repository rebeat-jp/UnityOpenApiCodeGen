using Rhycol.OpenApiCodeGen.SourceGenerator;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Editor
{
    internal sealed class OpenApiClientDefinitionPlan
    {
        internal OpenApiClientDefinitionPlan(
            string specId,
            string clientIdentitySha256,
            string targetAssemblyName,
            string definitionPath,
            string definitionAssetPath,
            string apiName,
            string generatedNamespace,
            OpenApiDocumentFormat documentFormat,
            byte[] content,
            bool preparedDefinitionExists,
            string preparedDefinitionSha256,
            string migrationSourcePath = "",
            string migrationSourceAssetPath = "",
            bool preparedDestinationMetaExists = false,
            string preparedDestinationMetaSha256 = "",
            string preparedMigrationSourceSha256 = "",
            bool preparedMigrationSourceMetaExists = false,
            string preparedMigrationSourceMetaSha256 = "",
            byte[] preparedMigrationSourceMetaBytes = null)
        {
            SpecId = specId;
            ClientIdentitySha256 = clientIdentitySha256;
            TargetAssemblyName = targetAssemblyName;
            DefinitionPath = definitionPath;
            DefinitionAssetPath = definitionAssetPath;
            ApiName = apiName;
            GeneratedNamespace = generatedNamespace;
            DocumentFormat = documentFormat;
            Content = content;
            PreparedDefinitionExists = preparedDefinitionExists;
            PreparedDefinitionSha256 = preparedDefinitionSha256;
            MigrationSourcePath = migrationSourcePath;
            MigrationSourceAssetPath = migrationSourceAssetPath;
            PreparedDestinationMetaExists = preparedDestinationMetaExists;
            PreparedDestinationMetaSha256 = preparedDestinationMetaSha256;
            PreparedMigrationSourceSha256 = preparedMigrationSourceSha256;
            PreparedMigrationSourceMetaExists = preparedMigrationSourceMetaExists;
            PreparedMigrationSourceMetaSha256 = preparedMigrationSourceMetaSha256;
            PreparedMigrationSourceMetaBytes = preparedMigrationSourceMetaBytes ?? new byte[0];
        }

        internal string SpecId { get; }

        internal string ClientIdentitySha256 { get; }

        internal string TargetAssemblyName { get; }

        internal string DefinitionPath { get; }

        internal string DefinitionAssetPath { get; }

        internal string ApiName { get; }

        internal string GeneratedNamespace { get; }

        internal OpenApiDocumentFormat DocumentFormat { get; }

        internal byte[] Content { get; }

        internal bool PreparedDefinitionExists { get; }

        internal string PreparedDefinitionSha256 { get; }

        internal string MigrationSourcePath { get; }

        internal string MigrationSourceAssetPath { get; }

        internal bool PreparedDestinationMetaExists { get; }

        internal string PreparedDestinationMetaSha256 { get; }

        internal string PreparedMigrationSourceSha256 { get; }

        internal bool PreparedMigrationSourceMetaExists { get; }

        internal string PreparedMigrationSourceMetaSha256 { get; }

        internal byte[] PreparedMigrationSourceMetaBytes { get; }

        internal bool IsFolderMigration => !string.IsNullOrEmpty(MigrationSourcePath);

        internal bool RequiresPublication => IsFolderMigration ||
            !PreparedDefinitionExists ||
            !string.Equals(PreparedDefinitionSha256, ComputeContentSha256(Content), System.StringComparison.Ordinal);

        internal string[] DefinitionArtifactPaths => IsFolderMigration
            ? new[]
            {
                DefinitionPath,
                DefinitionPath + ".meta",
                MigrationSourcePath,
                MigrationSourcePath + ".meta",
            }
            : new[] { DefinitionPath };

        internal DefinitionArtifactOutput[] DefinitionArtifactOutputs => IsFolderMigration
            ? new[]
            {
                new DefinitionArtifactOutput(DefinitionPath, true, Content),
                new DefinitionArtifactOutput(
                    DefinitionPath + ".meta",
                    PreparedMigrationSourceMetaExists,
                    PreparedMigrationSourceMetaBytes),
                new DefinitionArtifactOutput(MigrationSourcePath, false, new byte[0]),
                new DefinitionArtifactOutput(MigrationSourcePath + ".meta", false, new byte[0]),
            }
            : new[]
            {
                new DefinitionArtifactOutput(DefinitionPath, true, Content),
            };

        private static string ComputeContentSha256(byte[] content)
        {
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(content);
                var builder = new System.Text.StringBuilder(hash.Length * 2);
                for (int index = 0; index < hash.Length; index++)
                {
                    builder.Append(hash[index].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }
    }


    internal sealed class DefinitionArtifactOutput
    {
        internal DefinitionArtifactOutput(string path, bool exists, byte[] bytes)
        {
            Path = path;
            Exists = exists;
            Bytes = bytes;
        }

        internal string Path { get; }

        internal bool Exists { get; }

        internal byte[] Bytes { get; }
    }
}
