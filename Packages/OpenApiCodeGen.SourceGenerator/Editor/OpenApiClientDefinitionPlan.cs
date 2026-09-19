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
            string preparedDefinitionSha256)
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
    }
}
