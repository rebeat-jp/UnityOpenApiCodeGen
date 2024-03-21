#nullable enable
using ReBeat.OpenApiCodeGen.Core;

namespace ReBeat.OpenApiCodeGen.UI
{
    public class GeneralSettingsDto
    {
        public GenerateProvider GenerateProvider { get; private set; }
        public string JavaPath { get; private set; }
        public string ApiDocumentFilePathOrUrl { get; private set; }
        public string ApiClientOutputFolderPath { get; private set; }

        public GeneralSettingsDto(GenerateProvider generateProvider, string javaPath, string apiDocumentFilePathOrUrl, string apiClientOutputFolderPath)
        {
            GenerateProvider = generateProvider;
            JavaPath = javaPath;
            ApiDocumentFilePathOrUrl = apiDocumentFilePathOrUrl;
            ApiClientOutputFolderPath = apiClientOutputFolderPath;
        }
    }
}