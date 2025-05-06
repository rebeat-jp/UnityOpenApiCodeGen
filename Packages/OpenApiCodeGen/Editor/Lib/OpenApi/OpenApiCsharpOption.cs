#nullable enable

using Newtonsoft.Json;

namespace ReBeat.OpenApiCodeGen.Lib
{
    internal class OpenApiCsharpOption
    {
        [JsonProperty("allowUnicodeIdentifiers")]
        public bool AllowUnicodeIdentifiers;
        [JsonProperty("apiName")]
        public string ApiName = "Api";
        [JsonProperty("caseInsensitiveResponseHeaders")]
        public bool CaseInsensitiveResponseHeaders = false;
        [JsonProperty("conditionalSerialization")]
        public bool ConditionalSerialization = false;
        [JsonProperty("disallowAdditionalPropertiesIfNotPresent")]
        public bool DisallowAdditionalPropertiesIfNotPresent = true;
        [JsonProperty("equatable")]
        public bool Equatable;
        [JsonProperty("hideGenerationTimestamp")]
        public bool HideGenerationTimestamp;
        [JsonProperty("interfacePrefix")]
        public string InterfacePrefix = "I";
        [JsonProperty("library")]
        public string Library = "unityWebRequest";
        [JsonProperty("licenseId")]
        public string? LicenseId = null;
        [JsonProperty("modelPropertyNaming")]
        public string ModelPropertyNaming = "PascalCase";
        [JsonProperty("netCoreProjectFile")]
        public bool NetCoreProjectFile = false;
        [JsonProperty("nonPublicApi")]
        public bool NonPublicApi = false;
        [JsonProperty("nullableReferenceTypes")]
        public bool NullableReferenceTypes = true;
        [JsonProperty("optionalEmitDefaultValues")]
        public bool OptionalEmitDefaultValues = false;
        [JsonProperty("optionalMethodArgument")]
        public bool OptionalMethodArgument = true;
        [JsonProperty("optionalAssemblyInfo")]
        public bool OptionalAssemblyInfo = true;
        [JsonProperty("optionalProjectFile")]
        public bool OptionalProjectFile = false;
        [JsonProperty("packageName")]
        public string PackageName = "ReBeat.OpenApiCodeGen";
        [JsonProperty("returnICollection")]
        public bool ReturnICollection = false;
        /// <summary>
        /// The target .NET framework version. To target multiple frameworks, use ; as the separator, 
        /// e.g. [netstandard2.1;netcoreapp3.1]
        /// </summary>
        [JsonProperty("targetFramework")]
        public string TargetFramework = "netstandard2.1";
        [JsonProperty("useCollection")]
        public bool UseCollection = false;
        [JsonProperty("useOneOfDiscriminatorLookup")]
        public bool UseOneOfDiscriminatorLookup = false;
        [JsonProperty("validatable")]
        public bool Validatable = true;

    }
}