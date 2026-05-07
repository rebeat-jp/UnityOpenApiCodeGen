#nullable enable

using System;
using System.IO;
using System.Threading.Tasks;

using Rhycol.OpenApiCodeGen.Core;


namespace Rhycol.OpenApiCodeGen.Lib
{
    internal class GenerationCSharpSettingJsonRepository : IAsyncRepository<GenerationCSharpSetting>
    {
        readonly JsonFileStore<GenerationCSharpSettingJson> _jsonFileStore;
        const string FILE_NAME = "csharpSettings.json";

        public GenerationCSharpSettingJsonRepository()
        {
            var saveDirectory = ApplicationConstant.PROJECT_FOLDER_PATH;
            if (!string.IsNullOrEmpty(saveDirectory) && !Directory.Exists(saveDirectory))
            {
                Directory.CreateDirectory(saveDirectory);
            }

            _jsonFileStore = new(
                Path.Combine(saveDirectory, FILE_NAME)
            );
        }

        public async Task<GenerationCSharpSetting?> ReadAsync()
        {
            var json = await _jsonFileStore.ReadAsync();
            return json?.ToDomain();
        }

        public async Task SaveAsync(GenerationCSharpSetting value)
        {
            await _jsonFileStore.SaveAsync(GenerationCSharpSettingJson.FromDomain(value));
        }

        public async Task DeleteAsync()
        {
            await Task.Run(() => _jsonFileStore.Delete());
        }

        [Serializable]
        private class GenerationCSharpSettingJson
        {
            public bool AllowUnicodeIdentifiers = false;
            public string ApiName = "Api";
            public bool CaseInsensitiveResponseHeaders = false;
            public bool ConditionalSerialization = false;
            public bool DisallowAdditionalPropertiesIfNotPresent = true;
            public bool Equatable;
            public bool HideGenerationTimestamp;
            public string InterfacePrefix = "I";
            public string Library = "unityWebRequest";
            public string? LicenseId = null;
            public string ModelPropertyNaming = "PascalCase";
            public bool NetCoreProjectFile = false;
            public bool NonPublicApi = false;
            public bool NullableReferenceTypes = true;
            public bool OptionalEmitDefaultValues = false;
            public bool OptionalMethodArgument = true;
            public bool OptionalAssemblyInfo = true;
            public bool OptionalProjectFile = false;
            public string PackageName = "Rhycol.OpenApiCodeGen";
            public bool ReturnICollection = false;
            public string TargetFramework = "netstandard2.1";
            public bool UseCollection = false;
            public bool UseOneOfDiscriminatorLookup = false;
            public bool Validatable = true;

            public static GenerationCSharpSettingJson FromDomain(GenerationCSharpSetting value)
            {
                return new GenerationCSharpSettingJson
                {
                    AllowUnicodeIdentifiers = value.AllowUnicodeIdentifiers,
                    ApiName = value.ApiName,
                    CaseInsensitiveResponseHeaders = value.CaseInsensitiveResponseHeaders,
                    ConditionalSerialization = value.ConditionalSerialization,
                    DisallowAdditionalPropertiesIfNotPresent = value.DisallowAdditionalPropertiesIfNotPresent,
                    Equatable = value.Equatable,
                    HideGenerationTimestamp = value.HideGenerationTimestamp,
                    InterfacePrefix = value.InterfacePrefix,
                    Library = value.Library,
                    LicenseId = value.LicenseId,
                    ModelPropertyNaming = value.ModelPropertyNaming,
                    NetCoreProjectFile = value.NetCoreProjectFile,
                    NonPublicApi = value.NonPublicApi,
                    NullableReferenceTypes = value.NullableReferenceTypes,
                    OptionalEmitDefaultValues = value.OptionalEmitDefaultValues,
                    OptionalMethodArgument = value.OptionalMethodArgument,
                    OptionalAssemblyInfo = value.OptionalAssemblyInfo,
                    OptionalProjectFile = value.OptionalProjectFile,
                    PackageName = value.PackageName,
                    ReturnICollection = value.ReturnICollection,
                    TargetFramework = value.TargetFramework,
                    UseCollection = value.UseCollection,
                    UseOneOfDiscriminatorLookup = value.UseOneOfDiscriminatorLookup,
                    Validatable = value.Validatable,
                };
            }

            public GenerationCSharpSetting ToDomain()
            {
                return new GenerationCSharpSetting
                {
                    AllowUnicodeIdentifiers = AllowUnicodeIdentifiers,
                    ApiName = ApiName,
                    CaseInsensitiveResponseHeaders = CaseInsensitiveResponseHeaders,
                    ConditionalSerialization = ConditionalSerialization,
                    DisallowAdditionalPropertiesIfNotPresent = DisallowAdditionalPropertiesIfNotPresent,
                    Equatable = Equatable,
                    HideGenerationTimestamp = HideGenerationTimestamp,
                    InterfacePrefix = InterfacePrefix,
                    Library = Library,
                    LicenseId = LicenseId,
                    ModelPropertyNaming = ModelPropertyNaming,
                    NetCoreProjectFile = NetCoreProjectFile,
                    NonPublicApi = NonPublicApi,
                    NullableReferenceTypes = NullableReferenceTypes,
                    OptionalEmitDefaultValues = OptionalEmitDefaultValues,
                    OptionalMethodArgument = OptionalMethodArgument,
                    OptionalAssemblyInfo = OptionalAssemblyInfo,
                    OptionalProjectFile = OptionalProjectFile,
                    PackageName = PackageName,
                    ReturnICollection = ReturnICollection,
                    TargetFramework = TargetFramework,
                    UseCollection = UseCollection,
                    UseOneOfDiscriminatorLookup = UseOneOfDiscriminatorLookup,
                    Validatable = Validatable,
                };
            }
        }

    }
}
