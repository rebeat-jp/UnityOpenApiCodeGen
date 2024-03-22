#nullable enable
using System;
using System.IO;

using Newtonsoft.Json;

using UnityEngine;

namespace ReBeat.OpenApiCodeGen.Core
{
    [JsonObject]
    public class GeneralConfigSchema
    {
        [JsonIgnore]
        public static readonly string ConfigFilePath = Path.Combine(Application.persistentDataPath, "OpenApiCodeGen/config.json");

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