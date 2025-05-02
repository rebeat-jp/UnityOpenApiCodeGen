#nullable enable
using ReBeat.OpenApiCodeGen.Core;

namespace ReBeat.OpenApiCodeGen.UI
{
    public class GenerateMenuDto
    {
        public GenerateProvider GenerateProvider { get; private set; }
        public string ApiDocumentFilePathOrUrl { get; private set; }
        public string ApiClientOutputFolderPath { get; private set; }

        public GenerateMenuDto(GenerateProvider generateProvider, string apiDocumentFilePathOrUrl, string apiClientOutputFolderPath)
        {
            GenerateProvider = generateProvider;
            ApiDocumentFilePathOrUrl = apiDocumentFilePathOrUrl;
            ApiClientOutputFolderPath = apiClientOutputFolderPath;
        }
    }
}