#nullable enable
namespace ReBeat.OpenApiCodeGen.Core
{
    internal class ProjectSetting
    {

        public GenerateProvider GenerateProvider { get; private set; }
        public string ApiDocumentFilePathOrUrl { get; private set; }
        public string ApiClientOutputFolderPath { get; private set; }

        public ProjectSetting(GenerateProvider generateProvider = GenerateProvider.OpenApi, string apiDocumentFilePathOrUrl = "", string apiClientOutputFolderPath = "", string cacheFolderPath = "")
        {
            GenerateProvider = generateProvider;
            ApiDocumentFilePathOrUrl = apiDocumentFilePathOrUrl;
            ApiClientOutputFolderPath = apiClientOutputFolderPath;
        }

        public ProjectSetting()
        {
            GenerateProvider = GenerateProvider.OpenApi;
            ApiDocumentFilePathOrUrl = "";
            ApiClientOutputFolderPath = "";
        }
    }
}
