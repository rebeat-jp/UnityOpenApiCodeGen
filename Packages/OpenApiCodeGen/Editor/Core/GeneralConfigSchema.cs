#nullable enable
using System;

using ReBeat.OpenApiCodeGen.Lib;

using UnityEngine;

namespace ReBeat.OpenApiCodeGen.Core
{
    public class GeneralConfigSchema : IScriptable<GeneralConfigSchema>
    {

        public GenerateProvider GenerateProvider;
        public string DockerPath;
        public string ApiDocumentFilePathOrUrl;
        public string ApiClientOutputFolderPath;
        public string CacheFolderPath;

        public GeneralConfigSchema(GenerateProvider generateProvider = GenerateProvider.OpenApi, string dockerPath = "", string apiDocumentFilePathOrUrl = "", string apiClientOutputFolderPath = "", string cacheFolderPath = "")
        {
            GenerateProvider = generateProvider;
            DockerPath = dockerPath;
            ApiDocumentFilePathOrUrl = apiDocumentFilePathOrUrl;
            ApiClientOutputFolderPath = apiClientOutputFolderPath;
            CacheFolderPath = cacheFolderPath;
        }

        public GeneralConfigSchema()
        {
            GenerateProvider = GenerateProvider.OpenApi;
            DockerPath = "";
            ApiDocumentFilePathOrUrl = "";
            ApiClientOutputFolderPath = "";
            CacheFolderPath = "";

        }

        public ScriptableObject ToScriptable()
        {
            var value = ScriptableObject.CreateInstance<GeneralConfigSchemaAsScriptableObject>();
            return value;

        }

        public GeneralConfigSchema FromScriptable(ScriptableObject scriptableObject)
        {
            return scriptableObject is not GeneralConfigSchemaAsScriptableObject generalConfigSchemaAsScriptableObject
                ? throw new ArgumentException()
                : new GeneralConfigSchema()
                {
                    GenerateProvider = generalConfigSchemaAsScriptableObject.GenerateProvider,
                    DockerPath = generalConfigSchemaAsScriptableObject.DockerPath,
                    ApiDocumentFilePathOrUrl = generalConfigSchemaAsScriptableObject.DefaultApiDocumentFilePathOrUrl,
                    ApiClientOutputFolderPath = generalConfigSchemaAsScriptableObject.DefaultApiClientOutputFolderPath,
                    CacheFolderPath = generalConfigSchemaAsScriptableObject.CacheFolderPath,
                };
        }

        public Type GetScriptableType()
        {
            return typeof(GeneralConfigSchemaAsScriptableObject);
        }

    }
}