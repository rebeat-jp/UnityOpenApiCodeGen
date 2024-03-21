using System;

using Newtonsoft.Json;

namespace ReBeat.OpenApiCodeGen.Lib
{
    [JsonObject]
    public class OpenApiConfigSchema
    {
        [JsonIgnore]
        public static readonly string ConfigFilePath = $"{Environment.CurrentDirectory}/Packages/SwaggerCodeGen/Editor/Lib/OpenApi/config.json";


        [JsonProperty("packageName")]
        public string PackageName { get; private set; }
        [JsonProperty("targetFramework")]
        public string TargetFramework { get; private set; }

        [JsonProperty("optionalAssemblyInfo")]
        public bool IncludeOptionalAssemblyInfo { get; private set; }

        [JsonProperty("optionalProjectFile")]
        public bool IncludeOptionalProjectFile { get; private set; }

        [JsonProperty("nullableReferenceTypes")]
        public bool IncludeNullableReferenceTypes { get; private set; }

        [JsonProperty("library")]
        public string DependenceLibrary { get; private set; }

        [JsonProperty("validatable")]
        public bool IsValidatable { get; private set; }

        public OpenApiConfigSchema(
            string packageName = "ReBeat.OpenApiCodeGen",
            string targetFramework = "netstandard2.1",
            bool includeOptionalAssemblyInfo = false,
            bool includeOptionalProjectFile = false,
            bool includeNullableReferenceTypes = true,
            string dependenceLibrary = "unityWebRequest",
            bool isValidatable = true)
        {
            this.PackageName = packageName;
            this.TargetFramework = targetFramework;
            this.IncludeOptionalAssemblyInfo = includeOptionalAssemblyInfo;
            this.IncludeOptionalProjectFile = includeOptionalProjectFile;
            this.IncludeNullableReferenceTypes = includeNullableReferenceTypes;
            this.DependenceLibrary = dependenceLibrary;
            this.IsValidatable = isValidatable;
        }
    }

}
