#nullable enable

using ReBeat.OpenApiCodeGen.Core;
using ReBeat.OpenApiCodeGen.Lib;

namespace ReBeat.OpenApiCodeGen.Presenter
{
    class SettingPresenter : ISettingPresenter
    {
        readonly JsonRepository<GeneralConfigSchema> _generalSettingRepository;
        readonly JsonRepository<OpenApiConfigSchema> _openApiSettingRepository;
        public SettingPresenter()
        {
            _generalSettingRepository = new();
            _openApiSettingRepository = new();
        }

        public (GeneralConfigSchema generalSettings, OpenApiConfigSchema openApiSettings) LoadSetting()
        {
            var generalSettings = _generalSettingRepository.Read(GeneralConfigSchema.ConfigFilePath)
            ?? _generalSettingRepository.Save(
              GeneralConfigSchema.ConfigFilePath,
              new GeneralConfigSchema()
              );
            var openApiSettings = _openApiSettingRepository.Read(OpenApiConfigSchema.ConfigFilePath)
            ?? _openApiSettingRepository.Save(
              OpenApiConfigSchema.ConfigFilePath,
              new OpenApiConfigSchema()
              );

            return (generalSettings, openApiSettings);
        }

        public ProcessResponse RunJavaTest(string javaPath)
        {
            var javaTester = new JavaRuntimeTester();
            return javaTester.Test(javaPath);
        }


        public GeneralConfigSchema SaveGeneral(GenerateProvider generateProvider, string javaPath, string apiDocumentFilePathOrUrl, string apiClientOutputFolder)
        {
            var result = _generalSettingRepository.Save(
                GeneralConfigSchema.ConfigFilePath,
                new GeneralConfigSchema(generateProvider, javaPath, apiDocumentFilePathOrUrl, apiClientOutputFolder)
                );
            return result;
        }

        public OpenApiConfigSchema SaveOpenApi(
            string packageName,
            string targetFramework,
            bool includeOptionalAssemblyInfo,
            bool includeOptionalProjectFile,
            bool includeNullableReferenceTypes,
            string dependenceLibrary,
            bool isValidatable
            )
        {
            var result = _openApiSettingRepository.Save(
              OpenApiConfigSchema.ConfigFilePath,
              new OpenApiConfigSchema(
                    packageName,
                    targetFramework,
                    includeOptionalAssemblyInfo,
                    includeOptionalProjectFile,
                    includeNullableReferenceTypes,
                    dependenceLibrary,
                    isValidatable
                    )
                );
            return result;
        }
    }
}