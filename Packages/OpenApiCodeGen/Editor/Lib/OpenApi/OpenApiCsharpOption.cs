#nullable enable


namespace ReBeat.OpenApiCodeGen.Lib
{
    internal class OpenApiCsharpOption
    {
        public bool allowUnicodeIdentifiers;
        public string apiName = "Api";
        public bool caseInsensitiveResponseHeaders = false;
        public bool conditionalSerialization = false;
        public bool disallowAdditionalPropertiesIfNotPresent = true;
        public bool equatable;
        public bool hideGenerationTimestamp;
        public string interfacePrefix = "I";
        public string library = "unityWebRequest";
        public string? licenseId = null;
        public string modelPropertyNaming = "PascalCase";
        public bool netCoreProjectFile = false;
        public bool nonPublicApi = false;
        public bool nullableReferenceTypes = true;
        public bool optionalEmitDefaultValues = false;
        public bool optionalMethodArgument = true;
        public bool optionalAssemblyInfo = true;
        public bool optionalProjectFile = false;
        public string packageName = "ReBeat.OpenApiCodeGen";
        public bool returnICollection = false;
        /// <summary>
        /// The target .NET framework version. To target multiple frameworks, use ; as the separator, 
        /// e.g. [netstandard2.1;netcoreapp3.1]
        /// </summary>
        public string targetFramework = "netstandard2.1";
        public bool useCollection = false;
        public bool useOneOfDiscriminatorLookup = false;
        public bool validatable = true;

    }
}