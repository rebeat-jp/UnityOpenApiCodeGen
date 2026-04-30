#nullable enable
using ReBeat.OpenApiCodeGen.Core;

namespace ReBeat.OpenApiCodeGen.UI
{
    internal class GenerateApiClientDto
    {
        public GenerateProvider GenerateProvider { get; private set; }
        public string ApiDocumentFilePathOrUrl { get; private set; }
        public string ApiClientOutputFolderPath { get; private set; }

        public GenerateApiClientDto()
        {
            GenerateProvider = GenerateProvider.OpenApi;
            ApiDocumentFilePathOrUrl = string.Empty;
            ApiClientOutputFolderPath = string.Empty;
        }

        public GenerateApiClientDto(GenerateProvider generateProvider, string apiDocumentFilePathOrUrl, string apiClientOutputFolderPath)
        {
            GenerateProvider = generateProvider;
            ApiDocumentFilePathOrUrl = apiDocumentFilePathOrUrl;
            ApiClientOutputFolderPath = apiClientOutputFolderPath;
        }
    }
}