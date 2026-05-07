#nullable enable


using Rhycol.OpenApiCodeGen.Core;

namespace Rhycol.OpenApiCodeGen.Lib
{
    internal class OpenApiCsharpOption
    {
#pragma warning disable IDE1006 // 命名スタイル
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
        public string packageName = "Rhycol.OpenApiCodeGen";
        public bool returnICollection = false;
        /// <summary>
        /// The target .NET framework version. To target multiple frameworks, use ; as the separator, 
        /// e.g. [netstandard2.1;netcoreapp3.1]
        /// </summary>
        public string targetFramework = "netstandard2.1";
        public bool useCollection = false;
        public bool useOneOfDiscriminatorLookup = false;
        public bool validatable = true;

#pragma warning restore IDE1006 // 命名スタイル
        public OpenApiCsharpOption()
        {
        }
        public OpenApiCsharpOption(GenerationCSharpSetting cSharpSetting)
        {
            allowUnicodeIdentifiers = cSharpSetting.AllowUnicodeIdentifiers;
            apiName = cSharpSetting.ApiName;
            caseInsensitiveResponseHeaders = cSharpSetting.CaseInsensitiveResponseHeaders;
            conditionalSerialization = cSharpSetting.ConditionalSerialization;
            disallowAdditionalPropertiesIfNotPresent = cSharpSetting.DisallowAdditionalPropertiesIfNotPresent;
            equatable = cSharpSetting.Equatable;
            hideGenerationTimestamp = cSharpSetting.HideGenerationTimestamp;
            interfacePrefix = cSharpSetting.InterfacePrefix;
            library = cSharpSetting.Library;
            licenseId = cSharpSetting.LicenseId;
            modelPropertyNaming = cSharpSetting.ModelPropertyNaming;
            netCoreProjectFile = cSharpSetting.NetCoreProjectFile;
            nonPublicApi = cSharpSetting.NonPublicApi;
            nullableReferenceTypes = cSharpSetting.NullableReferenceTypes;
            optionalEmitDefaultValues = cSharpSetting.OptionalEmitDefaultValues;
            optionalMethodArgument = cSharpSetting.OptionalMethodArgument;
            optionalAssemblyInfo = cSharpSetting.OptionalAssemblyInfo;
            optionalProjectFile = cSharpSetting.OptionalProjectFile;
            packageName = cSharpSetting.PackageName;
            returnICollection = cSharpSetting.ReturnICollection;
            targetFramework = cSharpSetting.TargetFramework;
            useCollection = cSharpSetting.UseCollection;
            useOneOfDiscriminatorLookup = cSharpSetting.UseOneOfDiscriminatorLookup;
            validatable = cSharpSetting.Validatable;
        }
    }
}