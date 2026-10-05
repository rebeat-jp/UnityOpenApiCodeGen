#nullable enable
using Rhycol.OpenApiCodeGen.Core;
using Rhycol.OpenApiCodeGen.Editor.Generation;

namespace Rhycol.OpenApiCodeGen.UI
{
    internal class ProjectSettingDisplayDto
    {

        public GenerateProvider GenerateProvider { get; private set; }
        public string ApiDocumentFilePathOrUrl { get; private set; }
        public string ApiClientOutputFolderPath { get; private set; }

        public ProjectSettingDisplayDto(GenerateProvider generateProvider = GenerateProvider.OpenApi, string apiDocumentFilePathOrUrl = "", string apiClientOutputFolderPath = "", string cacheFolderPath = "")
        {
            GenerateProvider = generateProvider;
            ApiDocumentFilePathOrUrl = apiDocumentFilePathOrUrl;
            ApiClientOutputFolderPath = apiClientOutputFolderPath;
        }

        public ProjectSettingDisplayDto()
        {
            GenerateProvider = GenerateProvider.OpenApi;
            ApiDocumentFilePathOrUrl = "";
            ApiClientOutputFolderPath = "";
        }
    }
}
