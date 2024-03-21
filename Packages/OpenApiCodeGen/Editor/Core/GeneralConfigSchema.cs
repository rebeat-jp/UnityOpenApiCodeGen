#nullable enable
using System;

using Newtonsoft.Json;

namespace ReBeat.OpenApiCodeGen.Core
{
    [JsonObject]
    public class GeneralConfigSchema
    {
        [JsonIgnore]
        public static readonly string ConfigFilePath = $"{Environment.CurrentDirectory}/Packages/OpenApiCodeGen/Editor/Core/config.json";

        [JsonProperty]
        public GenerateProvider GenerateProvider { get; private set; }
        [JsonProperty]
        public string JavaPath { get; private set; }
        [JsonProperty]
        public string ApiDocumentFilePathOrUrl { get; private set; }
        [JsonProperty]
        public string ApiClientOutputFolderPath { get; private set; }

        public GeneralConfigSchema(GenerateProvider generateProvider = GenerateProvider.OpenApi, string javaPath = "", string apiDocumentFilePathOrUrl = "", string apiClientOutputFolderPath = "")
        {
            GenerateProvider = generateProvider;
            JavaPath = javaPath;
            ApiDocumentFilePathOrUrl = apiDocumentFilePathOrUrl;
            ApiClientOutputFolderPath = apiClientOutputFolderPath;
        }
    }
}