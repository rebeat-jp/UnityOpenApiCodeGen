using UnityEngine;

namespace ReBeat.OpenApiCodeGen.Core
{
    [CreateAssetMenu(menuName = "UnityOpenApiCodeGen/GeneralConfigSchema")]
    public class GeneralConfigSchemaAsScriptableObject : ScriptableObject
    {
        public GenerateProvider GenerateProvider = GenerateProvider.OpenApi;
        public string DockerPath = "";
        public string DefaultApiDocumentFilePathOrUrl = "";
        public string DefaultApiClientOutputFolderPath = "";
        public string CacheFolderPath = "";


    }
}