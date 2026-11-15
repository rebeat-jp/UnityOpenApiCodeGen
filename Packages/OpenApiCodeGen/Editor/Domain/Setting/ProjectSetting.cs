#nullable enable

using Rhycol.OpenApiCodeGen.Editor.Generation;

namespace Rhycol.OpenApiCodeGen.Core
{
    internal class ProjectSetting
    {

        public GenerateProvider GenerateProvider { get; private set; }
        public string ApiDocumentFilePathOrUrl { get; private set; }
        public string ApiClientOutputFolderPath { get; private set; }
        public GenerationHttpTransport HttpTransport { get; private set; }
        public GenerationJsonSerializer JsonSerializer { get; private set; }

        public ProjectSetting(
            GenerateProvider generateProvider = GenerateProvider.OpenApi,
            string apiDocumentFilePathOrUrl = "",
            string apiClientOutputFolderPath = "",
            string cacheFolderPath = "",
            GenerationHttpTransport httpTransport = GenerationHttpTransport.HttpClient,
            GenerationJsonSerializer jsonSerializer = GenerationJsonSerializer.NewtonsoftJson)
        {
            GenerateProvider = generateProvider;
            ApiDocumentFilePathOrUrl = apiDocumentFilePathOrUrl;
            ApiClientOutputFolderPath = apiClientOutputFolderPath;
            HttpTransport = httpTransport;
            JsonSerializer = jsonSerializer;
        }

        public ProjectSetting()
        {
            GenerateProvider = GenerateProvider.OpenApi;
            ApiDocumentFilePathOrUrl = "";
            ApiClientOutputFolderPath = "";
            HttpTransport = GenerationHttpTransport.HttpClient;
            JsonSerializer = GenerationJsonSerializer.NewtonsoftJson;
        }
    }
}
