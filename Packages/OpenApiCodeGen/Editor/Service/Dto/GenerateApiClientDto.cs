#nullable enable
using Rhycol.OpenApiCodeGen.Core;
using Rhycol.OpenApiCodeGen.Editor.Generation;

namespace Rhycol.OpenApiCodeGen.UI
{
    internal class GenerateApiClientDto
    {
        public GenerateProvider GenerateProvider { get; private set; }
        public string ApiDocumentFilePathOrUrl { get; private set; }
        public string ApiClientOutputFolderPath { get; private set; }
        // The saved library selection. It is carried so the Generator window can evaluate the libraries of the
        // combination without reading settings again; the form itself never edits it.
        public GenerationHttpTransport HttpTransport { get; private set; }
        public GenerationJsonSerializer JsonSerializer { get; private set; }

        public GenerateApiClientDto()
        {
            GenerateProvider = GenerateProvider.OpenApi;
            ApiDocumentFilePathOrUrl = string.Empty;
            ApiClientOutputFolderPath = string.Empty;
            HttpTransport = GenerationHttpTransport.HttpClient;
            JsonSerializer = GenerationJsonSerializer.NewtonsoftJson;
        }

        public GenerateApiClientDto(
            GenerateProvider generateProvider,
            string apiDocumentFilePathOrUrl,
            string apiClientOutputFolderPath,
            GenerationHttpTransport httpTransport = GenerationHttpTransport.HttpClient,
            GenerationJsonSerializer jsonSerializer = GenerationJsonSerializer.NewtonsoftJson)
        {
            GenerateProvider = generateProvider;
            ApiDocumentFilePathOrUrl = apiDocumentFilePathOrUrl;
            ApiClientOutputFolderPath = apiClientOutputFolderPath;
            HttpTransport = httpTransport;
            JsonSerializer = jsonSerializer;
        }
    }
}
